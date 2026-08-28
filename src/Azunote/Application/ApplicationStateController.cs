namespace Azunote;

/// <summary>
/// Owns the process-wide application state shared by all document windows.
/// Writes are serialized so opening several files in quick succession cannot
/// leave an older snapshot on disk after a newer one.
/// </summary>
internal sealed class ApplicationStateController : IDisposable
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly List<string> _pendingRecentFiles = [];
    private Task? _initializationTask;
    private Task _saveQueue = Task.CompletedTask;
    private AzunoteState _current = new();
    private WindowLayoutState? _pendingWindowSize;
    private bool? _pendingWordWrap;
    private bool? _pendingStatusBarVisible;
    private bool _initialized;
    private bool _disposed;

    public ApplicationStateController(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public bool IsInitialized
    {
        get
        {
            lock (_gate)
            {
                return _initialized;
            }
        }
    }

    public AzunoteState Current
    {
        get
        {
            lock (_gate)
            {
                return AzunoteStateNormalization.Clone(_current);
            }
        }
    }

    public Task InitializeAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(ApplicationStateController));
            return _initializationTask ??= InitializeCoreAsync();
        }
    }

    public void RecordRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var recentFiles = new List<string>(_current.RecentFiles.Length + 1)
            {
                fullPath
            };
            recentFiles.AddRange(_current.RecentFiles.Where(
                recentPath => !string.Equals(
                    recentPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase)));
            _current.RecentFiles = recentFiles
                .Take(AzunoteStateNormalization.MaximumRecentFiles)
                .ToArray();
            if (!_initialized)
            {
                _pendingRecentFiles.RemoveAll(recentPath => string.Equals(
                    recentPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));
                _pendingRecentFiles.Insert(0, fullPath);
            }
            else
            {
                QueueSaveLocked();
            }
        }
    }

    public bool RemoveRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var recentFiles = _current.RecentFiles.ToList();
            var index = recentFiles.FindIndex(recentPath => string.Equals(
                recentPath,
                fullPath,
                StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            recentFiles.RemoveAt(index);
            _current.RecentFiles = recentFiles.ToArray();
            if (!_initialized)
            {
                _pendingRecentFiles.RemoveAll(recentPath => string.Equals(
                    recentPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                QueueSaveLocked();
            }

            return true;
        }
    }

    public void RecordWindowSize(WindowLayoutState size)
    {
        ArgumentNullException.ThrowIfNull(size);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _current.Window = new WindowLayoutState
            {
                Width = size.Width,
                Height = size.Height
            };
            AzunoteStateNormalization.Normalize(_current);
            if (!_initialized)
            {
                _pendingWindowSize = new WindowLayoutState
                {
                    Width = _current.Window.Width,
                    Height = _current.Window.Height
                };
            }
            else
            {
                QueueSaveLocked();
            }
        }
    }

    public void RecordWordWrap(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _current.WordWrap = enabled;
            if (!_initialized)
            {
                _pendingWordWrap = enabled;
            }
            else
            {
                QueueSaveLocked();
            }
        }
    }

    public void RecordStatusBarVisible(bool visible)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _current.StatusBarVisible = visible;
            if (!_initialized)
            {
                _pendingStatusBarVisible = visible;
            }
            else
            {
                QueueSaveLocked();
            }
        }
    }

    public async Task FlushAsync()
    {
        Task initialization;
        lock (_gate)
        {
            initialization = _initializationTask ?? Task.CompletedTask;
        }

        try
        {
            await initialization.ConfigureAwait(false);
            Task saveQueue;
            lock (_gate)
            {
                saveQueue = _saveQueue;
            }
            await saveQueue.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorReporter.LogException("Could not save application state", exception);
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

        FlushAsync().GetAwaiter().GetResult();
    }

    private async Task InitializeCoreAsync()
    {
        AzunoteState? loadedState = null;
        try
        {
            await StateFileService.EnsureExistsAsync(_directory).ConfigureAwait(false);
            loadedState = await StateFileService.LoadAsync(_directory).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A corrupt or inaccessible state file should not prevent the
            // editor from starting. The current in-memory defaults remain
            // usable, and the failure is available in the application log.
            ErrorReporter.LogException("Could not load application state", exception);
        }
        finally
        {
            lock (_gate)
            {
                if (loadedState is not null)
                {
                    if (_pendingWindowSize is { } pendingWindowSize)
                    {
                        loadedState.Window = pendingWindowSize;
                    }

                    if (_pendingRecentFiles.Count > 0)
                    {
                        loadedState.RecentFiles = _pendingRecentFiles
                            .Concat(loadedState.RecentFiles)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(AzunoteStateNormalization.MaximumRecentFiles)
                            .ToArray();
                    }

                    if (_pendingWordWrap is { } pendingWordWrap)
                    {
                        loadedState.WordWrap = pendingWordWrap;
                    }

                    if (_pendingStatusBarVisible is { } pendingStatusBarVisible)
                    {
                        loadedState.StatusBarVisible = pendingStatusBarVisible;
                    }

                    _current = AzunoteStateNormalization.Normalize(loadedState);
                }

                var hasPendingChanges = _pendingWindowSize is not null
                    || _pendingRecentFiles.Count > 0
                    || _pendingWordWrap is not null
                    || _pendingStatusBarVisible is not null;
                _pendingWindowSize = null;
                _pendingRecentFiles.Clear();
                _pendingWordWrap = null;
                _pendingStatusBarVisible = null;
                _initialized = true;
                if (hasPendingChanges)
                {
                    QueueSaveLocked();
                }
            }
        }
    }

    private void QueueSaveLocked()
    {
        var snapshot = AzunoteStateNormalization.Clone(_current);
        _saveQueue = _saveQueue
            .ContinueWith(
                _ => StateFileService.SaveAsync(_directory, snapshot),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)
            .Unwrap();
    }
}
