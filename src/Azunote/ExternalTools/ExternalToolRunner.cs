using System.Diagnostics;
using System.Text;

namespace Azunote;

public sealed record ExternalToolResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string MixedOutput,
    int InvocationCount = 1)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Runs a tool directly when possible and uses the appropriate Windows
/// launcher for command and PowerShell scripts. Arguments remain separate for
/// direct and PowerShell launches; command-shell arguments are quoted into the
/// single command string required by cmd.exe.
/// </summary>
public sealed class ExternalToolRunner
{
    private static readonly UTF8Encoding StandardIoEncoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public static Task<ExternalToolResult> RunAsync(
        ExternalToolDefinition definition,
        ExternalToolContext context,
        CancellationToken cancellationToken = default) =>
        RunAsync(definition, context, warmPool: null, cancellationToken);

    /// <summary>
    /// Runs the tool, taking a waiting PowerShell process from
    /// <paramref name="warmPool"/> when the launch is a `pwsh` command and one
    /// is available. A null pool, or any failure to hand the request over,
    /// launches the process the usual way instead.
    /// </summary>
    internal static Task<ExternalToolResult> RunAsync(
        ExternalToolDefinition definition,
        ExternalToolContext context,
        PowerShellWarmPool? warmPool,
        CancellationToken cancellationToken = default) =>
        RunAsync(definition, context, warmPool, streamSink: null, cancellationToken);

    /// <summary>
    /// Runs the tool, handing <paramref name="streamSink"/> the output of the
    /// channels the definition streams as it arrives. One relay serves every
    /// part of a <c>per</c> run, so the parts reach the sink concatenated the
    /// way a buffered run concatenates them.
    /// </summary>
    internal static async Task<ExternalToolResult> RunAsync(
        ExternalToolDefinition definition,
        ExternalToolContext context,
        PowerShellWarmPool? warmPool,
        IExternalToolStreamSink? streamSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        var sourceInput = context.GetInput(definition.InputMode);
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var mixedOutput = new StringBuilder();
        var succeeded = true;
        var exitCode = 0;
        var invocationCount = 0;

        // The part count is known before the first process starts, so a run
        // that launches several processes can raise the waiting count for
        // exactly as long as it needs it.
        var inputParts = definition.Per.GetInputParts(sourceInput);
        using var relay = streamSink is not null
            && definition.Stream != ExternalToolStreamChannels.None
            ? new ExternalToolStreamRelay(streamSink, definition.Stream)
            : null;
        using var reservation = inputParts.Count > 1
            && definition.CommandMode == ExternalToolCommandMode.Pwsh
            ? warmPool?.Reserve(inputParts.Count)
            : null;

        foreach (var inputPart in inputParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            invocationCount++;
            var result = await RunSingleAsync(
                definition,
                context.WithInput(inputPart.Value, inputPart.Captures),
                warmPool,
                relay,
                cancellationToken);
            standardOutput.Append(result.StandardOutput);
            standardError.Append(result.StandardError);
            mixedOutput.Append(result.MixedOutput);

            if (!result.Succeeded)
            {
                if (succeeded)
                {
                    exitCode = result.ExitCode;
                }

                succeeded = false;
            }
        }

        relay?.Complete();
        return new ExternalToolResult(
            succeeded ? 0 : exitCode,
            standardOutput.ToString(),
            standardError.ToString(),
            mixedOutput.ToString(),
            invocationCount);
    }

    private static async Task<ExternalToolResult> RunSingleAsync(
        ExternalToolDefinition definition,
        ExternalToolContext context,
        PowerShellWarmPool? warmPool,
        ExternalToolStreamRelay? relay,
        CancellationToken cancellationToken)
    {
        var environment = ExternalToolEnvironmentResolver.Resolve(definition, context);
        var command = context.Expand(definition.FileName, environment.Values);
        var launchPlan = ExternalToolLaunchResolver.Resolve(
            command,
            definition.CommandMode,
            definition.DefinitionDirectory);
        if (launchPlan is null)
        {
            var exception = new InvalidOperationException(
                $"Could not resolve external tool '{command}'.");
            ErrorReporter.LogException(
                "External tool launch could not be resolved",
                new InvalidOperationException(
                    $"Command: {command}{Environment.NewLine}"
                    + $"DefinitionDirectory: {definition.DefinitionDirectory}",
                    exception));
            throw exception;
        }

        var arguments = context.Expand(definition.Arguments, environment.Values);
        var standardInput = context.Expand(definition.Stdin, environment.Values);
        var startInfo = new ProcessStartInfo
        {
            FileName = launchPlan.LauncherPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = StandardIoEncoding,
            StandardOutputEncoding = StandardIoEncoding,
            StandardErrorEncoding = StandardIoEncoding,
            WorkingDirectory = ResolveWorkingDirectory(
                definition,
                context,
                environment.Values)
        };
        foreach (var environmentVariable in environment.Overrides)
        {
            startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
        }

        launchPlan.AddArguments(startInfo, arguments, standardInput.Length > 0);

        using var lease = await TryAcquireWarmAsync(
            warmPool,
            launchPlan,
            startInfo,
            environment.Overrides,
            standardInput.Length > 0,
            cancellationToken);
        using var coldProcess = lease is null ? new Process { StartInfo = startInfo } : null;
        var process = lease?.Process ?? coldProcess!;
        if (coldProcess is not null)
        {
            try
            {
                if (!coldProcess.Start())
                {
                    throw new InvalidOperationException(
                        $"Could not start external tool: {startInfo.FileName}");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ErrorReporter.LogException(
                    $"External tool launch failed: {startInfo.FileName}",
                    new InvalidOperationException(
                        DescribeLaunch(definition, launchPlan, startInfo, arguments, context),
                        exception));
                throw;
            }
        }

        try
        {
            var mixedBuilder = new StringBuilder();
            var mixedLock = new object();
            // The lock keeps the mixed stream in the order the reads arrived
            // and hands the relay its chunks in that same order.
            void AppendChunk(ExternalToolOutputChannel channel, string chunk)
            {
                lock (mixedLock)
                {
                    mixedBuilder.Append(chunk);
                    if (relay is not null)
                    {
                        relay.Append(channel, chunk);
                        relay.Append(ExternalToolOutputChannel.Mixed, chunk);
                    }
                }
            }

            var outputTask = ReadStreamAsync(
                process.StandardOutput,
                chunk => AppendChunk(ExternalToolOutputChannel.Stdout, chunk),
                cancellationToken);
            var errorTask = ReadStreamAsync(
                process.StandardError,
                chunk => AppendChunk(ExternalToolOutputChannel.Stderr, chunk),
                cancellationToken);
            if (standardInput.Length == 0)
            {
                process.StandardInput.Close();
            }
            else
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
            return new ExternalToolResult(
                process.ExitCode,
                await outputTask,
                await errorTask,
                mixedBuilder.ToString());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<PowerShellWarmLease?> TryAcquireWarmAsync(
        PowerShellWarmPool? warmPool,
        ExternalToolLaunchPlan launchPlan,
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> environmentOverrides,
        bool standardInputConfigured,
        CancellationToken cancellationToken)
    {
        if (warmPool is null
            || launchPlan.Kind != ExternalToolLaunchKind.PowerShellCommand)
        {
            return null;
        }

        return await warmPool.TryAcquireAsync(
            launchPlan.LauncherPath,
            new PowerShellWarmRequest(
                startInfo.WorkingDirectory,
                environmentOverrides,
                launchPlan.ResolvedPath,
                standardInputConfigured),
            cancellationToken);
    }

    private static async Task<string> ReadStreamAsync(
        StreamReader reader,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);
            if (count == 0)
            {
                return builder.ToString();
            }

            var chunk = new string(buffer, 0, count);
            builder.Append(chunk);
            onChunk(chunk);
        }
    }

    private static string DescribeLaunch(
        ExternalToolDefinition definition,
        ExternalToolLaunchPlan launchPlan,
        ProcessStartInfo startInfo,
        string[] arguments,
        ExternalToolContext context)
    {
        var formattedArguments = arguments.Length > 0
            ? string.Join(
                Environment.NewLine,
                arguments.Select(argument =>
                    $"  {MaskDocumentText(argument, context)}"))
            : "(none)";
        var environmentVariables = definition.Environment.Count == 0
            ? "(none)"
            : string.Join(", ", definition.Environment.Keys.Order(StringComparer.OrdinalIgnoreCase));

        return $"RequestedCommand: {launchPlan.RequestedCommand}{Environment.NewLine}"
            + $"ResolvedPath: {launchPlan.ResolvedPath}{Environment.NewLine}"
            + $"Launcher: {launchPlan.LauncherPath}{Environment.NewLine}"
            + $"LaunchKind: {launchPlan.Kind}{Environment.NewLine}"
            + $"FileName: {startInfo.FileName}{Environment.NewLine}"
            + $"Arguments:{Environment.NewLine}{formattedArguments}{Environment.NewLine}"
            + $"WorkingDirectory: {startInfo.WorkingDirectory}{Environment.NewLine}"
            + $"InputMode: {definition.InputMode}{Environment.NewLine}"
            + $"Per: {definition.Per}{Environment.NewLine}"
            + $"StdinConfigured: {!string.IsNullOrEmpty(definition.Stdin)}{Environment.NewLine}"
            + $"Output: {definition.Output}{Environment.NewLine}"
            + $"Stdout: {definition.Stdout}{Environment.NewLine}"
            + $"Stderr: {definition.Stderr}{Environment.NewLine}"
            + $"EnvironmentVariables: {environmentVariables}";
    }

    private static string MaskDocumentText(
        string argument,
        ExternalToolContext context)
    {
        if (!string.IsNullOrEmpty(context.Document))
        {
            argument = argument.Replace(
                context.Document,
                "<document text>",
                StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(context.Selection))
        {
            argument = argument.Replace(
                context.Selection,
                "<selection text>",
                StringComparison.Ordinal);
        }

        foreach (var capture in context.InputCaptures.Values)
        {
            if (!string.IsNullOrEmpty(capture))
            {
                argument = argument.Replace(
                    capture,
                    "<input capture>",
                    StringComparison.Ordinal);
            }
        }

        if (!string.IsNullOrEmpty(context.Input))
        {
            argument = argument.Replace(
                context.Input,
                "<input text>",
                StringComparison.Ordinal);
        }

        return argument;
    }

    private static string ResolveWorkingDirectory(
        ExternalToolDefinition definition,
        ExternalToolContext context,
        IReadOnlyDictionary<string, string> environment)
    {
        var configured = definition.WorkingDirectory is null
            ? context.DocumentDirname ?? context.FileDirname
            : context.Expand(definition.WorkingDirectory, environment);

        if (!string.IsNullOrWhiteSpace(configured)
            && !Path.IsPathRooted(configured)
            && !string.IsNullOrWhiteSpace(definition.DefinitionDirectory))
        {
            configured = Path.GetFullPath(Path.Combine(definition.DefinitionDirectory, configured));
        }

        return string.IsNullOrWhiteSpace(configured) || !Directory.Exists(configured)
            ? Environment.CurrentDirectory
            : configured;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between cancellation and cleanup.
        }
    }
}
