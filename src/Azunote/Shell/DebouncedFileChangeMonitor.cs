namespace Azunote;

/// <summary>
/// Owns FileSystemWatcher lifetime and notification coalescing. UI dispatch
/// remains the responsibility of the subscriber.
/// </summary>
internal sealed class DebouncedFileChangeMonitor : IFileChangeMonitor
{
    private const int DebounceMilliseconds = 150;
    private readonly FileSystemWatcher _watcher;
    private readonly string _monitoredPath;
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _disposed;

    public DebouncedFileChangeMonitor(string path, bool includeSubdirectories)
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

    private void OnChanged(object sender, FileSystemEventArgs args) => QueueNotification();

    private void OnRenamed(object sender, RenamedEventArgs args) => QueueNotification();

    private void QueueNotification()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer ??= new Timer(OnTimerElapsed);
            _timer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void OnTimerElapsed(object? state)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        Changed?.Invoke(this, new FileChangeDetectedEventArgs(_monitoredPath));
    }
}

internal sealed class FileChangeDetectedEventArgs : EventArgs
{
    public FileChangeDetectedEventArgs(string monitoredPath)
    {
        MonitoredPath = monitoredPath;
    }

    public string MonitoredPath { get; }
}

internal sealed class DefaultFileChangeMonitorFactory : IFileChangeMonitorFactory
{
    public IFileChangeMonitor Create(string path, bool includeSubdirectories) =>
        new DebouncedFileChangeMonitor(path, includeSubdirectories);
}
