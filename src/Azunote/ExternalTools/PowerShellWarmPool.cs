using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Azunote;

/// <summary>
/// One request handed to a waiting PowerShell process. The working directory,
/// environment overrides and script text are not known when the process is
/// started, so they travel over the handshake pipe instead of the command line.
/// </summary>
internal sealed record PowerShellWarmRequest(
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    string Script);

/// <summary>
/// A waiting process that has been handed its request. The pipe stays open
/// until the run finishes so the handshake bytes cannot be lost; the caller
/// owns the process.
/// </summary>
internal sealed class PowerShellWarmLease : IDisposable
{
    private readonly IDisposable _pipe;

    public PowerShellWarmLease(Process process, IDisposable pipe)
    {
        Process = process;
        _pipe = pipe;
    }

    public Process Process { get; }

    public void Dispose()
    {
        try
        {
            _pipe.Dispose();
        }
        catch (IOException)
        {
            // The waiting process closed its end first.
        }

        Process.Dispose();
    }
}

/// <summary>
/// Keeps one PowerShell process started and parked so that the interpreter
/// start-up cost is paid before a tool run is requested. Processes are never
/// reused: a waiting process serves exactly one run and then exits, which keeps
/// exit codes, standard input, stream separation and environment isolation
/// identical to a cold launch. Every failure falls back to a cold launch.
/// </summary>
internal sealed class PowerShellWarmPool : IDisposable
{
    internal const string PipeEnvironmentVariable = "AZUNOTE_EXTERNAL_TOOL_PIPE";
    internal const string TokenEnvironmentVariable = "AZUNOTE_EXTERNAL_TOOL_TOKEN";

    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding PipeEncoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);
    private static readonly UTF8Encoding StandardIoEncoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private readonly Func<string?> _resolveLauncher;
    private readonly TimeSpan _idleTimeout;
    private readonly object _gate = new();
    private readonly ITimer _idleTimer;
    private WarmInstance? _instance;
    private Task? _warming;
    private bool _disposed;

    public PowerShellWarmPool(
        Func<string?>? resolveLauncher = null,
        TimeSpan? idleTimeout = null,
        TimeProvider? timeProvider = null)
    {
        _resolveLauncher = resolveLauncher ?? ExternalToolLaunchResolver.ResolvePowerShell;
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
        _idleTimer = (timeProvider ?? TimeProvider.System).CreateTimer(
            _ => DiscardIdle(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>Number of started processes, for tests and diagnostics.</summary>
    internal int StartCount { get; private set; }

    /// <summary>Whether a healthy process is waiting for a request.</summary>
    internal bool HasWarmProcess
    {
        get
        {
            lock (_gate)
            {
                return _instance is not null && _instance.IsAlive;
            }
        }
    }

    internal int? WarmProcessIdForTest
    {
        get
        {
            lock (_gate)
            {
                return _instance?.ProcessId;
            }
        }
    }

    internal void KillWarmProcessForTest()
    {
        WarmInstance? instance;
        lock (_gate)
        {
            instance = _instance;
        }

        instance?.KillForTest();
    }

    /// <summary>Starts a waiting process when none is available.</summary>
    public void EnsureWarm()
    {
        lock (_gate)
        {
            if (_disposed || _instance is not null || _warming is not null)
            {
                return;
            }

            _warming = Task.Run(WarmAsync);
        }
    }

    /// <summary>Stops warming and terminates the waiting process, if any.</summary>
    public void Cool()
    {
        WarmInstance? instance;
        lock (_gate)
        {
            instance = _instance;
            _instance = null;
        }

        _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        instance?.Dispose();
    }

    /// <summary>
    /// Hands the request to the waiting process when one is available and
    /// healthy. Returns <see langword="null"/> when the caller should launch a
    /// process the usual way.
    /// </summary>
    public async Task<PowerShellWarmLease?> TryAcquireAsync(
        string launcherPath,
        PowerShellWarmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launcherPath);
        ArgumentNullException.ThrowIfNull(request);

        WarmInstance? instance;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            instance = _instance;
            _instance = null;
        }

        if (instance is null)
        {
            EnsureWarm();
            return null;
        }

        using var measurement = ShellPerformance.Measure("powershell.warm.acquire");
        try
        {
            if (!instance.Matches(launcherPath))
            {
                instance.Dispose();
                return null;
            }

            await instance.SendAsync(request, HandshakeTimeout, cancellationToken);
            return instance.Lease();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorReporter.LogException(
                "Waiting PowerShell process could not accept the request",
                exception);
            instance.Dispose();
            return null;
        }
        finally
        {
            // Refill right away so that `per` runs find a warm process for the
            // parts that follow the first one.
            EnsureWarm();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Cool();
        _idleTimer.Dispose();
    }

    private void DiscardIdle()
    {
        WarmInstance? instance;
        lock (_gate)
        {
            instance = _instance;
            _instance = null;
        }

        instance?.Dispose();
    }

    private async Task WarmAsync()
    {
        WarmInstance? instance = null;
        try
        {
            instance = await StartAsync();
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException(
                "Could not start a waiting PowerShell process",
                exception);
        }
        finally
        {
            var discard = false;
            lock (_gate)
            {
                _warming = null;
                if (_disposed || _instance is not null)
                {
                    discard = true;
                }
                else
                {
                    _instance = instance;
                }
            }

            if (discard)
            {
                instance?.Dispose();
            }
            else if (instance is not null)
            {
                _idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private async Task<WarmInstance?> StartAsync()
    {
        var launcherPath = _resolveLauncher();
        if (launcherPath is null)
        {
            return null;
        }

        using var measurement = ShellPerformance.Measure("powershell.warm.start");
        var pipeName = "azunote-pwsh-" + Guid.NewGuid().ToString("n");
        var token = Guid.NewGuid().ToString("n");
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = StandardIoEncoding,
                StandardOutputEncoding = StandardIoEncoding,
                StandardErrorEncoding = StandardIoEncoding,
                WorkingDirectory = AppContext.BaseDirectory,
                Arguments = ExternalToolLaunchPlan.BuildPowerShellWarmArguments()
            };
            startInfo.Environment[PipeEnvironmentVariable] = pipeName;
            startInfo.Environment[TokenEnvironmentVariable] = token;

            var connected = pipe.WaitForConnectionAsync();
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Could not start a waiting PowerShell process: {launcherPath}");
            }

            lock (_gate)
            {
                StartCount++;
            }

            await connected.WaitAsync(HandshakeTimeout);
            var reader = new StreamReader(pipe, PipeEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var greeting = await reader.ReadLineAsync().WaitAsync(HandshakeTimeout);
            if (!string.Equals(greeting, token, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A waiting PowerShell process failed the handshake.");
            }

            var instance = new WarmInstance(process, pipe, launcherPath);
            process = null;
            return instance;
        }
        finally
        {
            if (process is not null)
            {
                TryKill(process);
                process.Dispose();
                pipe.Dispose();
            }
        }
    }

    internal static void TryKill(Process process)
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
            // The process exited on its own.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process is already terminating.
        }
    }

    private sealed class WarmInstance : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly string _launcherPath;
        private Process? _process;

        public WarmInstance(Process process, NamedPipeServerStream pipe, string launcherPath)
        {
            _process = process;
            _pipe = pipe;
            _launcherPath = launcherPath;
        }

        public bool IsAlive => _process is { HasExited: false };

        public int? ProcessId => _process?.Id;

        public void KillForTest()
        {
            if (_process is { } process)
            {
                TryKill(process);
                process.WaitForExit(5000);
            }
        }

        public bool Matches(string launcherPath) =>
            _process is { HasExited: false }
            && string.Equals(_launcherPath, launcherPath, StringComparison.OrdinalIgnoreCase);

        public async Task SendAsync(
            PowerShellWarmRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var payload = Serialize(request);
            await _pipe.WriteAsync(payload, cancellationToken).AsTask().WaitAsync(timeout, cancellationToken);
            await _pipe.FlushAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        }

        public PowerShellWarmLease Lease()
        {
            var process = _process
                ?? throw new InvalidOperationException("The waiting process was already taken.");
            _process = null;
            return new PowerShellWarmLease(process, _pipe);
        }

        public void Dispose()
        {
            var process = _process;
            _process = null;
            if (process is not null)
            {
                TryKill(process);
                process.Dispose();
            }

            try
            {
                _pipe.Dispose();
            }
            catch (IOException)
            {
                // The waiting process closed its end first.
            }
        }

        private static byte[] Serialize(PowerShellWarmRequest request)
        {
            // Written by hand so that no reflection-based serializer is needed
            // under trimming and ahead-of-time compilation.
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("cwd", request.WorkingDirectory);
                writer.WriteStartObject("env");
                foreach (var environmentVariable in request.Environment)
                {
                    writer.WriteString(environmentVariable.Key, environmentVariable.Value);
                }

                writer.WriteEndObject();
                writer.WriteString("script", request.Script);
                writer.WriteEndObject();
            }

            // One line: the waiting process reads the request with ReadLine.
            buffer.WriteByte((byte)'\n');
            return buffer.ToArray();
        }
    }
}
