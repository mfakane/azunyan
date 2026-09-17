namespace Azunote;

/// <summary>
/// One reading of the settings folder: the settings themselves and the tools
/// prepared from the definitions below it. Nothing here changes after it is
/// built, so every window shares the one instance.
/// </summary>
internal sealed class SettingsSnapshot
{
    internal SettingsSnapshot(
        AzunoteSettings settings,
        IReadOnlyDictionary<ExternalToolSettings, PreparedExternalTool> preparedTools)
    {
        Settings = settings;
        PreparedTools = preparedTools;
    }

    internal AzunoteSettings Settings { get; }

    internal IReadOnlyDictionary<ExternalToolSettings, PreparedExternalTool> PreparedTools { get; }
}

/// <summary>
/// Reads the settings folder once for the whole application and watches it for
/// changes. A reload walks every tool definition and every syntax mode, so
/// leaving it to each window meant doing the same work as many times as there
/// were windows, and meant two windows could read the folder at different
/// moments and disagree about it.
/// </summary>
internal sealed class SettingsService : IDisposable
{
    private static readonly Lazy<SettingsService> LazyShared = new(() => new SettingsService(
        new SettingsController(SettingsFileService.GetDefaultDirectory()),
        new DefaultFileChangeMonitorFactory()));

    /// <summary>The instance the application runs on. Tests build their own.</summary>
    internal static SettingsService Shared => LazyShared.Value;

    private readonly SettingsController _settings;
    private readonly IFileChangeMonitorFactory _monitorFactory;
    private readonly object _gate = new();
    private IFileChangeMonitor? _monitor;
    private CancellationTokenSource? _reloadCancellation;
    private Task? _initialization;
    private bool _disposed;

    public SettingsService(SettingsController settings, IFileChangeMonitorFactory monitorFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _monitorFactory = monitorFactory ?? throw new ArgumentNullException(nameof(monitorFactory));
    }

    /// <summary>
    /// Raised off the UI thread once a reading has replaced <see cref="Current"/>.
    /// </summary>
    internal event EventHandler<SettingsSnapshot>? Loaded;

    /// <summary>
    /// Raised off the UI thread when a reading failed. The settings that were
    /// already current stay current, as does the menu built from them.
    /// </summary>
    internal event EventHandler<Exception>? LoadFailed;

    internal string Directory => _settings.Directory;

    internal SettingsSnapshot? Current { get; private set; }

    internal Task EnsureExistsAsync(CancellationToken cancellationToken = default) =>
        _settings.EnsureExistsAsync(cancellationToken);

    /// <summary>
    /// Reads the folder and starts watching it, once per application. Later
    /// callers await the same reading rather than repeating it. A reading that
    /// fails is reported through <see cref="LoadFailed"/> and is not retried
    /// here: the watch is what brings a corrected file back.
    /// </summary>
    internal Task EnsureInitializedAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            return _initialization ??= InitializeAsync();
        }
    }

    public void Dispose()
    {
        IFileChangeMonitor? monitor;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            monitor = _monitor;
            _monitor = null;
            _reloadCancellation?.Cancel();
            _reloadCancellation?.Dispose();
            _reloadCancellation = null;
        }

        if (monitor is not null)
        {
            monitor.Changed -= Monitor_Changed;
            monitor.Dispose();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await EnsureExistsAsync();
        }
        catch (Exception exception)
        {
            LoadFailed?.Invoke(this, exception);
        }

        await ReloadAsync();
        StartWatcher();
    }

    private async Task ReloadAsync()
    {
        CancellationToken cancellationToken;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // The superseded source is left to the garbage collector: the
            // reload it belongs to may still hold a registration on its token.
            _reloadCancellation?.Cancel();
            _reloadCancellation = new CancellationTokenSource();
            cancellationToken = _reloadCancellation.Token;
        }

        try
        {
            var settings = await _settings.LoadAsync(cancellationToken);
            var preparedTools = ExternalToolMenuBuilder.EnumerateTools(settings.ExternalToolMenu)
                .Distinct()
                .ToDictionary(tool => tool, tool => new PreparedExternalTool(tool));

            // A newer change has read a newer folder. Publishing what this one
            // found would put every window back the way it was.
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new SettingsSnapshot(settings, preparedTools);
            Current = snapshot;
            Loaded?.Invoke(this, snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            LoadFailed?.Invoke(this, exception);
        }
    }

    private void StartWatcher()
    {
        var directory = Path.GetFullPath(_settings.Directory);
        if (!System.IO.Directory.Exists(directory))
        {
            return;
        }

        IFileChangeMonitor monitor;
        lock (_gate)
        {
            if (_disposed || _monitor is not null)
            {
                return;
            }

            monitor = _monitorFactory.Create(
                directory,
                includeSubdirectories: true,
                IsApplicationStateChange);
            _monitor = monitor;
        }

        monitor.Changed += Monitor_Changed;
    }

    private void Monitor_Changed(object? sender, FileChangeDetectedEventArgs args)
    {
        if (ReferenceEquals(sender, _monitor))
        {
            _ = HandleChangedAsync();
        }
    }

    private async Task HandleChangedAsync()
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            await EnsureExistsAsync();
            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // A burst of settings notifications was superseded by a newer one.
        }
        catch (Exception exception)
        {
            LoadFailed?.Invoke(this, exception);
        }
    }

    private bool IsApplicationStateChange(string? changedPath) =>
        changedPath is not null
        && string.Equals(
            Path.GetFullPath(changedPath),
            StateFileService.GetStateFilePath(_settings.Directory),
            StringComparison.OrdinalIgnoreCase);
}
