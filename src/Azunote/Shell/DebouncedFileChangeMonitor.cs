namespace Azunote;

/// <summary>
/// Owns FileSystemWatcher lifetime and notification coalescing. UI dispatch
/// remains the responsibility of the subscriber. A recursive watch reports
/// only the entries a scan rooted at the same directory would visit; see
/// <see cref="FileSystemScanPolicy"/>.
/// </summary>
internal sealed class DebouncedFileChangeMonitor : IFileChangeMonitor
{
    private const int DebounceMilliseconds = 150;
    private readonly FileSystemWatcher _watcher;
    private readonly string _monitoredPath;
    private readonly bool _includeSubdirectories;
    private readonly Func<string?, bool>? _ignore;
    private readonly object _gate = new();
    private Timer? _timer;
    private string? _changedPath;
    private bool _disposed;

    public DebouncedFileChangeMonitor(
        string path,
        bool includeSubdirectories,
        Func<string?, bool>? ignore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = includeSubdirectories ? fullPath : Path.GetDirectoryName(fullPath);
        var filter = includeSubdirectories ? "*" : Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(filter))
        {
            throw new ArgumentException("The monitored path must have a valid directory and name.", nameof(path));
        }

        _monitoredPath = fullPath;
        _includeSubdirectories = includeSubdirectories;
        _ignore = ignore;
        _watcher = new FileSystemWatcher(directory, filter)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    public event EventHandler<FileChangeDetectedEventArgs>? Changed;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => QueueNotification(args.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs args) => QueueNotification(args.FullPath);

    private void QueueNotification(string? changedPath)
    {
        // A recursive watch reports everything below the monitored
        // directory, including the trees a scan of it would never walk.
        // Letting those through starts a debounce window and, at the end
        // of it, a reload that has nothing to read.
        if (_includeSubdirectories
            && FileSystemScanPolicy.IsSkipped(changedPath, _monitoredPath))
        {
            return;
        }

        // The window remembers one path, so a path nobody acts on must not
        // enter it: the change that arrived alongside it is the one the
        // subscriber is waiting for.
        if (_ignore?.Invoke(changedPath) == true)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _changedPath = changedPath;
            _timer ??= new Timer(OnTimerElapsed);
            _timer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void OnTimerElapsed(object? state)
    {
        string? changedPath;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            changedPath = _changedPath;
            _changedPath = null;
        }

        Changed?.Invoke(this, new FileChangeDetectedEventArgs(_monitoredPath, changedPath));
    }
}

internal sealed class FileChangeDetectedEventArgs : EventArgs
{
    public FileChangeDetectedEventArgs(string monitoredPath, string? changedPath = null)
    {
        MonitoredPath = monitoredPath;
        ChangedPath = changedPath;
    }

    public string MonitoredPath { get; }

    public string? ChangedPath { get; }
}

internal sealed class DefaultFileChangeMonitorFactory : IFileChangeMonitorFactory
{
    public IFileChangeMonitor Create(
        string path,
        bool includeSubdirectories,
        Func<string?, bool>? ignore = null) =>
        new DebouncedFileChangeMonitor(path, includeSubdirectories, ignore);
}
