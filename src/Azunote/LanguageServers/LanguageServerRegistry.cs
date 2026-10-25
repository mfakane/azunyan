namespace Azunote;

internal enum LanguageServerStatus
{
    Starting,
    Running,
    Failed
}

/// <summary>
/// A server process shared by every document of its workspace folder. It
/// starts when first acquired and is stopped a while after its last lease is
/// released, or at once when it is turned off.
/// </summary>
internal sealed class LanguageServerInstance
{
    private readonly object _gate = new();
    private LanguageServerClient? _client;

    public LanguageServerInstance(LanguageServerLaunch launch)
    {
        Launch = launch;
    }

    public LanguageServerLaunch Launch { get; }

    /// <summary>Raised on any thread when <see cref="Status"/> changes.</summary>
    public event EventHandler? StatusChanged;

    public LanguageServerStatus Status { get; private set; } = LanguageServerStatus.Starting;

    /// <summary>Why the server failed, when it has.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>The running server, or null while it starts or after it failed.</summary>
    public LanguageServerClient? Client
    {
        get
        {
            lock (_gate)
            {
                return Status == LanguageServerStatus.Running ? _client : null;
            }
        }
    }

    internal int References { get; set; }

    internal ITimer? StopTimer { get; set; }

    internal bool IsStopped { get; private set; }

    internal async Task StartAsync(
        Func<LanguageServerLaunch, LanguageServerClient> start,
        TimeSpan initializeTimeout)
    {
        LanguageServerClient? client = null;
        try
        {
            client = start(Launch);
            lock (_gate)
            {
                if (IsStopped)
                {
                    _ = client.DisposeAsync().AsTask();
                    return;
                }

                _client = client;
            }

            using var timeout = new CancellationTokenSource(initializeTimeout);
            await client.InitializeAsync(Launch.Definition.InitializationOptions, timeout.Token).ConfigureAwait(false);
            SetStatus(LanguageServerStatus.Running, null);
            _ = WatchAsync(client);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var message = exception is OperationCanceledException
                ? "The language server did not answer in time."
                : exception.Message;
            Fail(client, message);
        }
    }

    internal ValueTask StopAsync()
    {
        LanguageServerClient? client;
        lock (_gate)
        {
            IsStopped = true;
            client = _client;
            _client = null;
        }

        StopTimer?.Dispose();
        StopTimer = null;
        return client?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private async Task WatchAsync(LanguageServerClient client)
    {
        try
        {
            await client.Completion.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The connection reports how it ended through the failure below.
        }

        Fail(client, "The language server stopped.");
    }

    private void Fail(LanguageServerClient? client, string message)
    {
        lock (_gate)
        {
            if (IsStopped || (client is not null && !ReferenceEquals(client, _client)))
            {
                return;
            }

            _client = null;
        }

        var errorOutput = client?.ErrorOutput;
        SetStatus(
            LanguageServerStatus.Failed,
            string.IsNullOrWhiteSpace(errorOutput) ? message : $"{message}{Environment.NewLine}{errorOutput}");
        if (client is not null)
        {
            _ = client.DisposeAsync().AsTask();
        }
    }

    private void SetStatus(LanguageServerStatus status, string? failureMessage)
    {
        lock (_gate)
        {
            if (IsStopped)
            {
                return;
            }

            Status = status;
            FailureMessage = failureMessage;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A document's hold on a shared server. Disposing it lets the server stop.</summary>
internal sealed class LanguageServerLease : IDisposable
{
    private readonly LanguageServerRegistry _registry;
    private int _disposed;

    public LanguageServerLease(LanguageServerRegistry registry, LanguageServerInstance instance)
    {
        _registry = registry;
        Instance = instance;
    }

    public LanguageServerInstance Instance { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _registry.Release(Instance);
        }
    }
}

/// <summary>
/// The language servers of the whole application: which are turned on for
/// which workspace folders, and the processes running for them. Windows on
/// the same folder share one process per server.
/// </summary>
internal sealed class LanguageServerRegistry : IDisposable
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, LanguageServerInstance> _instances = new(StringComparer.Ordinal);
    private readonly Func<string, string, bool> _isEnabled;
    private readonly Action<string, string, bool> _setEnabled;
    private readonly Func<LanguageServerLaunch, LanguageServerClient> _start;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _idleTimeout;
    private bool _disposed;

    public LanguageServerRegistry(
        Func<string, string, bool> isEnabled,
        Action<string, string, bool> setEnabled,
        Func<LanguageServerLaunch, LanguageServerClient>? start = null,
        TimeProvider? timeProvider = null,
        TimeSpan? idleTimeout = null)
    {
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
        _start = start ?? LanguageServerClient.Start;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
    }

    /// <summary>
    /// Raised on any thread when a server is turned on or off somewhere, or
    /// when the remembered choices are first read.
    /// </summary>
    public event EventHandler? EnablementChanged;

    public bool IsEnabled(string workspaceFolder, LanguageServerDefinition definition) =>
        _isEnabled(workspaceFolder, definition.Command);

    public void SetEnabled(string workspaceFolder, LanguageServerDefinition definition, bool enabled)
    {
        _setEnabled(workspaceFolder, definition.Command, enabled);
        if (!enabled)
        {
            LanguageServerInstance[] stopped;
            lock (_gate)
            {
                stopped = [.. _instances.Values.Where(instance =>
                    string.Equals(instance.Launch.WorkspaceFolder, workspaceFolder, StringComparison.OrdinalIgnoreCase)
                    && instance.Launch.Definition.Command == definition.Command)];
                foreach (var instance in stopped)
                {
                    _instances.Remove(instance.Launch.Key);
                }
            }

            foreach (var instance in stopped)
            {
                _ = instance.StopAsync().AsTask();
            }
        }

        NotifyEnablementChanged();
    }

    public void NotifyEnablementChanged() => EnablementChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Holds the server for a document, starting it if no document holds it
    /// yet. A server that failed and is held by no one is started anew.
    /// </summary>
    public LanguageServerLease Acquire(LanguageServerLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        LanguageServerInstance instance;
        var start = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_instances.TryGetValue(launch.Key, out instance!)
                || (instance.References == 0 && instance.Status == LanguageServerStatus.Failed))
            {
                if (instance is not null)
                {
                    _ = instance.StopAsync().AsTask();
                }

                instance = new LanguageServerInstance(launch);
                _instances[launch.Key] = instance;
                start = true;
            }

            instance.References++;
            instance.StopTimer?.Dispose();
            instance.StopTimer = null;
        }

        if (start)
        {
            _ = instance.StartAsync(_start, InitializeTimeout);
        }

        return new LanguageServerLease(this, instance);
    }

    internal void Release(LanguageServerInstance instance)
    {
        lock (_gate)
        {
            if (--instance.References > 0
                || !_instances.TryGetValue(instance.Launch.Key, out var current)
                || !ReferenceEquals(current, instance))
            {
                return;
            }

            if (instance.Status == LanguageServerStatus.Failed || _disposed)
            {
                _instances.Remove(instance.Launch.Key);
                _ = instance.StopAsync().AsTask();
                return;
            }

            instance.StopTimer = _timeProvider.CreateTimer(
                _ => StopIdle(instance),
                null,
                _idleTimeout,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void StopIdle(LanguageServerInstance instance)
    {
        lock (_gate)
        {
            if (instance.References > 0
                || !_instances.TryGetValue(instance.Launch.Key, out var current)
                || !ReferenceEquals(current, instance))
            {
                return;
            }

            _instances.Remove(instance.Launch.Key);
        }

        _ = instance.StopAsync().AsTask();
    }

    /// <summary>Stops every server, waiting a short while for them to exit.</summary>
    public void Dispose()
    {
        LanguageServerInstance[] instances;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            instances = [.. _instances.Values];
            _instances.Clear();
        }

        try
        {
            Task.WhenAll(instances.Select(instance => instance.StopAsync().AsTask()))
                .Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException exception)
        {
            ErrorReporter.LogException("Could not stop a language server", exception.GetBaseException());
        }
    }
}
