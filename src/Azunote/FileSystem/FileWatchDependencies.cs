using Microsoft.Extensions.Caching.Memory;

namespace Azunote;

/// <summary>Owns the watches for one cache entry, including negative search dependencies.</summary>
internal sealed class FileWatchDependencies(
    SharedFileWatchRegistry registry, Action? invalidated, Action<FileWatchDependencies> released) : IDisposable
{
    private readonly List<SharedFileWatchRegistry.WatchLease> _leases = [];
    private readonly List<IDisposable> _callbacks = [];
    private int _disposed;
    private int _invalidated;
    private bool _complete = true;
    public bool FullyMonitored => _complete && _leases.Count > 0;

    public void ObserveDirectory(string directory, string[] names)
    {
        Watch(directory, names);
        // Detect replacement/removal of the watched directory itself as well.
        var parent = Directory.GetParent(directory)?.FullName;
        if (parent is not null) Watch(parent, [Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))], topologyOnly: true);
    }

    private void Watch(string directory, string[] names, bool topologyOnly = false)
    {
        var lease = registry.TryWatch(directory, topologyOnly, names);
        if (lease is null) { _complete = false; return; }
        _leases.Add(lease);
        _callbacks.Add(lease.Token.RegisterChangeCallback(_ =>
        {
            if (Volatile.Read(ref _disposed) == 0 && Interlocked.Exchange(ref _invalidated, 1) == 0)
                invalidated?.Invoke();
        }, null));
    }

    public void Attach(MemoryCacheEntryOptions options)
    {
        foreach (var lease in _leases) options.AddExpirationToken(lease.Token);
        options.RegisterPostEvictionCallback((_, _, _, state) => ((FileWatchDependencies)state!).Dispose(), this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var callback in _callbacks) callback.Dispose();
        foreach (var lease in _leases) lease.Dispose();
        released(this);
    }
}
