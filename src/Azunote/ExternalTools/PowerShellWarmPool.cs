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
    string Script,
    bool StandardInputConfigured = false,
    bool Streaming = false);

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
/// Keeps PowerShell processes started and parked so that the interpreter
/// start-up cost is paid before a tool run is requested. Processes are never
/// reused: a waiting process serves exactly one run and then exits, which keeps
/// exit codes, standard input, stream separation and environment isolation
/// identical to a cold launch. Every failure falls back to a cold launch.
///
/// The resting count is `tools.powerShellWarmIdleProcesses`. A run that is
/// known in advance to launch several processes reserves more, up to
/// `tools.powerShellWarmProcesses`, and the count returns to resting when that
/// run ends. Nothing waits at all until <see cref="EnsureWarm"/> reports that a
/// `pwsh` tool is configured.
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
    private readonly Queue<WarmInstance> _instances = new();
    private readonly List<int> _reservations = [];
    private int _maxProcesses = AzunoteToolsSettings.DefaultPowerShellWarmProcesses;
    private int _idleProcesses = AzunoteToolsSettings.DefaultPowerShellWarmIdleProcesses;
    private int _warming;
    private bool _enabled;
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

    /// <summary>Whether at least one healthy process is waiting.</summary>
    internal bool HasWarmProcess => WarmProcessCount > 0;

    /// <summary>Healthy processes currently waiting for a request.</summary>
    internal int WarmProcessCount
    {
        get
        {
            lock (_gate)
            {
                return _instances.Count(instance => instance.IsAlive);
            }
        }
    }

    /// <summary>Processes the pool is currently trying to keep waiting.</summary>
    internal int TargetProcessCount
    {
        get
        {
            lock (_gate)
            {
                return TargetLocked();
            }
        }
    }

    internal int? WarmProcessIdForTest
    {
        get
        {
            lock (_gate)
            {
                return _instances.Count == 0 ? null : _instances.Peek().ProcessId;
            }
        }
    }

    internal void KillWarmProcessesForTest()
    {
        WarmInstance[] instances;
        lock (_gate)
        {
            instances = [.. _instances];
        }

        foreach (var instance in instances)
        {
            instance.KillForTest();
        }
    }

    /// <summary>
    /// Applies `tools.powerShellWarmProcesses` and
    /// `tools.powerShellWarmIdleProcesses`.
    /// </summary>
    public void Configure(int maxProcesses, int idleProcesses)
    {
        lock (_gate)
        {
            _maxProcesses = Math.Max(1, maxProcesses);
            _idleProcesses = Math.Clamp(idleProcesses, 0, _maxProcesses);
        }

        Rebalance();
    }

    /// <summary>Starts waiting processes up to the resting count.</summary>
    public void EnsureWarm()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _enabled = true;
        }

        Rebalance();
    }

    /// <summary>Stops warming and terminates every waiting process.</summary>
    public void Cool()
    {
        lock (_gate)
        {
            _enabled = false;
        }

        _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Rebalance();
    }

    /// <summary>
    /// Raises the waiting count towards <paramref name="count"/>, bounded by
    /// the configured maximum, until the returned value is disposed. Callers
    /// use this when the number of processes a run needs is already known, such
    /// as the part count of a `per` run.
    /// </summary>
    public IDisposable Reserve(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        lock (_gate)
        {
            if (_disposed)
            {
                return NullReservation.Instance;
            }

            _reservations.Add(count);
        }

        Rebalance();
        return new Reservation(this, count);
    }

    /// <summary>
    /// Hands the request to a waiting process when one is available and
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

        try
        {
            using var measurement = ShellPerformance.Measure("powershell.warm.acquire");
            while (TakeInstance() is { } instance)
            {
                try
                {
                    if (!instance.Matches(launcherPath))
                    {
                        // A dead process, or a different interpreter than the
                        // one this run resolved to.
                        instance.Dispose();
                        continue;
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
                }
            }

            return null;
        }
        finally
        {
            // Refill right away so that the parts that follow the first one in
            // a `per` run find a waiting process.
            Rebalance();
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
            _enabled = false;
            _reservations.Clear();
        }

        Rebalance();
        _idleTimer.Dispose();
    }

    private WarmInstance? TakeInstance()
    {
        lock (_gate)
        {
            return _instances.Count == 0 ? null : _instances.Dequeue();
        }
    }

    private void Release(int count)
    {
        lock (_gate)
        {
            _reservations.Remove(count);
        }

        Rebalance();
    }

    private int TargetLocked()
    {
        if (_disposed || !_enabled)
        {
            return 0;
        }

        var reserved = 0;
        foreach (var reservation in _reservations)
        {
            reserved = Math.Max(reserved, reservation);
        }

        return Math.Clamp(Math.Max(_idleProcesses, reserved), 0, _maxProcesses);
    }

    /// <summary>
    /// Brings the number of waiting processes to the current target: starts the
    /// missing ones in parallel and terminates any surplus, such as the extra
    /// processes a finished `per` run no longer needs.
    /// </summary>
    private void Rebalance()
    {
        var surplus = new List<WarmInstance>();
        var missing = 0;
        lock (_gate)
        {
            var target = TargetLocked();
            while (_instances.Count > target)
            {
                surplus.Add(_instances.Dequeue());
            }

            missing = target - (_instances.Count + _warming);
            if (missing > 0)
            {
                _warming += missing;
            }
        }

        foreach (var instance in surplus)
        {
            instance.Dispose();
        }

        for (var i = 0; i < missing; i++)
        {
            _ = Task.Run(WarmOneAsync);
        }
    }

    private void DiscardIdle()
    {
        // Nothing has been parked for the idle timeout, so let the interpreters
        // go. The next menu refresh warms them again.
        WarmInstance[] instances;
        lock (_gate)
        {
            instances = [.. _instances];
            _instances.Clear();
        }

        foreach (var instance in instances)
        {
            instance.Dispose();
        }
    }

    private async Task WarmOneAsync()
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
                _warming--;
                if (instance is null)
                {
                    // Nothing to park.
                }
                else if (_instances.Count >= TargetLocked())
                {
                    discard = true;
                }
                else
                {
                    _instances.Enqueue(instance);
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
                Arguments = PowerShellToolWrapper.Arguments
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

    private sealed class Reservation : IDisposable
    {
        private readonly PowerShellWarmPool _pool;
        private readonly int _count;
        private bool _released;

        public Reservation(PowerShellWarmPool pool, int count)
        {
            _pool = pool;
            _count = count;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _pool.Release(_count);
        }
    }

    private sealed class NullReservation : IDisposable
    {
        public static readonly NullReservation Instance = new();

        public void Dispose()
        {
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
            IsAlive
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
                writer.WriteBoolean("stdin", request.StandardInputConfigured);
                writer.WriteBoolean("stream", request.Streaming);
                writer.WriteEndObject();
            }

            // One line: the waiting process reads the request with ReadLine.
            buffer.WriteByte((byte)'\n');
            return buffer.ToArray();
        }
    }
}
