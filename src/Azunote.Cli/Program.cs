using System.Diagnostics;

namespace Azunote.Cli;

/// <summary>
/// The console entry point for Azunote. The editor itself is a Windows
/// subsystem executable, so its standard output cannot be captured by a shell.
/// This client owns the console side of the contract: it forwards the command
/// line to the running editor, copies the requested output to standard output
/// exactly as the editor produced it, and reports the editor's outcome as the
/// process exit code.
/// </summary>
internal static class Program
{
    private const string EditorFileName = "Azunote.exe";
    private const int ColdStartConnectionAttempts = 200;
    private const int ExitCodeError = (int)SingleInstanceStatus.Failed;

    private static async Task<int> Main(string[] arguments)
    {
        // A completion request describes a command line the shell is still
        // editing, so it is answered here and never forwarded to the editor.
        if (AzunoteCommandLine.TryCompleteArguments(
            arguments,
            Console.Out,
            Console.Error,
            out var completionExitCode))
        {
            return completionExitCode;
        }

        AzunoteCommandLineOptions options;
        try
        {
            options = AzunoteCommandLine.Parse(arguments);
        }
        catch (CommandLineParseException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message);
            await Console.Error.WriteLineAsync(AzunoteCommandLine.Usage);
            return ExitCodeError;
        }

        if (options.ShowHelp)
        {
            Console.Out.WriteLine(AzunoteCommandLine.Usage);
            return 0;
        }

        try
        {
            var response = await SendAsync(
                arguments,
                options.ReadStandardInput ? await ReadStandardInputAsync() : null);
            if (response is null)
            {
                await Console.Error.WriteLineAsync(
                    "Could not contact Azunote. The editor did not start.");
                return ExitCodeError;
            }

            WriteStandardOutput(response.Payload.Span);
            return response.ExitCode;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync($"Could not contact Azunote: {exception.Message}");
            return ExitCodeError;
        }
    }

    private static async Task<SingleInstanceResponse?> SendAsync(
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte>? standardInput)
    {
        var workingDirectory = Environment.CurrentDirectory;
        var response = await SingleInstanceClient.TrySendAsync(
            SingleInstanceProtocol.DefaultInstanceName,
            arguments,
            workingDirectory,
            standardInput);
        if (response is not null)
        {
            return response;
        }

        // No editor is running. Start one without a command line of its own and
        // let the forwarded command open the document, so that the cold start
        // and the warm start take the same path.
        if (!TryStartEditor())
        {
            return null;
        }

        return await SingleInstanceClient.TrySendAsync(
            SingleInstanceProtocol.DefaultInstanceName,
            arguments,
            workingDirectory,
            standardInput,
            ColdStartConnectionAttempts);
    }

    private static bool TryStartEditor()
    {
        var editorPath = Path.Combine(AppContext.BaseDirectory, EditorFileName);
        if (!File.Exists(editorPath))
        {
            Console.Error.WriteLine($"Azunote was not found next to this client: {editorPath}");
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(editorPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            });
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not start Azunote: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads standard input as bytes. The editor treats the command line as
    /// UTF-8, so the text never goes through a decode on this side.
    /// </summary>
    private static async Task<ReadOnlyMemory<byte>> ReadStandardInputAsync()
    {
        using var input = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        return buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
    }

    /// <summary>
    /// Writes the payload bytes as the editor produced them: UTF-8 without a
    /// byte-order mark and without an added line ending.
    /// </summary>
    private static void WriteStandardOutput(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        using var output = Console.OpenStandardOutput();
        output.Write(payload);
        output.Flush();
    }
}
