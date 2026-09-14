using Microsoft.Extensions.Primitives;

namespace Azunote;

/// <summary>Shares bounded, non-recursive directory watches across windows.</summary>
internal sealed class SharedFileWatchRegistry : IDisposable
{
    internal static SharedFileWatchRegistry Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, Action<string?, string?>, Action, IDisposable?> _create;
    private readonly TimeProvider _time;
    private readonly int _limit;
    private bool _disposed;

    public SharedFileWatchRegistry(
        Func<string, Action<string?, string?>, Action, IDisposable?>? create = null,
        TimeProvider? time = null, int limit = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        _create = create ?? CreateNative;
        _time = time ?? TimeProvider.System;
        _limit = limit;
    }

    internal WatchLease? TryWatch(string directory, params string[] names) => TryWatch(directory, false, names);

    internal WatchLease? TryWatch(string directory, bool topologyOnly, params string[] names)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        lock (_gate)
        {
            if (_disposed) return null;
            if (_retryAfter.TryGetValue(directory, out var retry))
            {
                if (_time.GetUtcNow() < retry) return null;
                _retryAfter.Remove(directory);
            }
            if (!_entries.TryGetValue(directory, out var entry))
            {
                if (_entries.Count >= _limit) return null;
                entry = new Entry(directory);
                _entries.Add(directory, entry);
                try
                {
                    entry.Watcher = _create(directory,
                        (path, oldPath) => Changed(entry, path, oldPath), () => Failed(entry));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    entry.Watcher = null;
                }
                if (entry.Watcher is null || entry.Failed)
                {
                    _entries.Remove(directory);
                    entry.Watcher?.Dispose();
                    RememberFailure(directory);
                    return null;
                }
            }
            var lease = new WatchLease(this, entry, names, topologyOnly);
            entry.Leases.Add(lease);
            return lease;
        }
    }

    private void Changed(Entry entry, string? path, string? oldPath)
    {
        WatchLease[] leases;
        Entry[] relocated;
        lock (_gate)
        {
            if (_disposed || entry.Failed) return;
            leases = entry.Leases.Where(lease => (!lease.TopologyOnly || oldPath is not null)
                && (lease.Matches(path) || lease.Matches(oldPath))).ToArray();
            // A directory watch can follow the old directory handle after rename.
            // Retire it (and descendant watches), rather than reusing it for the new path.
            relocated = oldPath is null ? [] : _entries.Values.Where(candidate =>
                string.Equals(candidate.Directory, oldPath, StringComparison.OrdinalIgnoreCase)
                || candidate.Directory.StartsWith(Path.TrimEndingDirectorySeparator(oldPath)
                    + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        foreach (var candidate in relocated) Failed(candidate);
        foreach (var lease in leases) lease.Invalidate();
    }

    private void Failed(Entry entry)
    {
        WatchLease[] leases;
        IDisposable? watcher;
        lock (_gate)
        {
            if (_disposed || entry.Failed) return;
            entry.Failed = true;
            if (_entries.TryGetValue(entry.Directory, out var current) && ReferenceEquals(entry, current))
                _entries.Remove(entry.Directory);
            RememberFailure(entry.Directory);
            leases = entry.Leases.ToArray();
            watcher = entry.Watcher;
            entry.Watcher = null;
        }
        watcher?.Dispose();
        foreach (var lease in leases) lease.Invalidate();
    }

    private void RememberFailure(string directory)
    {
        if (_retryAfter.Count >= _limit && !_retryAfter.ContainsKey(directory))
            _retryAfter.Remove(_retryAfter.MinBy(pair => pair.Value).Key);
        _retryAfter[directory] = _time.GetUtcNow() + TimeSpan.FromSeconds(2);
    }

    private void Release(WatchLease lease)
    {
        IDisposable? watcher = null;
        lock (_gate)
        {
            var entry = lease.Owner;
            entry.Leases.Remove(lease);
            if (entry.Leases.Count == 0)
            {
                if (_entries.TryGetValue(entry.Directory, out var current) && ReferenceEquals(entry, current))
                    _entries.Remove(entry.Directory);
                watcher = entry.Watcher;
                entry.Watcher = null;
            }
        }
        watcher?.Dispose();
    }

    public void Dispose()
    {
        Entry[] entries;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            entries = _entries.Values.ToArray();
            _entries.Clear();
            _retryAfter.Clear();
        }
        foreach (var entry in entries)
        {
            WatchLease[] leases;
            IDisposable? watcher;
            lock (_gate)
            {
                leases = entry.Leases.ToArray();
                watcher = entry.Watcher;
                entry.Watcher = null;
            }
            watcher?.Dispose();
            foreach (var lease in leases) lease.Invalidate();
        }
    }

    private static FileSystemWatcher? CreateNative(string directory,
        Action<string?, string?> changed, Action failed)
    {
        // Remote/unavailable locations retain the short TTL instead of allocating watches.
        if (new Uri(directory).IsUnc
            || new DriveInfo(Path.GetPathRoot(directory)!).DriveType == DriveType.Network
            || !Directory.Exists(directory)) return null;
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Changed += (_, args) => changed(args.FullPath, null);
        watcher.Created += (_, args) => changed(args.FullPath, args.FullPath);
        watcher.Deleted += (_, args) => changed(null, args.FullPath);
        watcher.Renamed += (_, args) => changed(args.FullPath, args.OldFullPath);
        watcher.Error += (_, _) => failed();
        try { watcher.EnableRaisingEvents = true; }
        catch { watcher.Dispose(); throw; }
        return watcher;
    }

    internal sealed class Entry(string directory)
    {
        public string Directory { get; } = directory;
        public HashSet<WatchLease> Leases { get; } = [];
        public IDisposable? Watcher { get; set; }
        public bool Failed { get; set; }
    }

    internal sealed class WatchLease : IDisposable
    {
        private readonly SharedFileWatchRegistry _registry;
        private readonly HashSet<string> _names;
        private readonly CancellationTokenSource _changed = new();
        private readonly object _gate = new();
        private bool _disposed;
        private bool _invalidating;
        private bool _invalidated;
        internal Entry Owner { get; }
        internal bool TopologyOnly { get; }
        public IChangeToken Token { get; }

        internal WatchLease(SharedFileWatchRegistry registry, Entry owner, IEnumerable<string> names, bool topologyOnly)
        {
            _registry = registry;
            Owner = owner;
            TopologyOnly = topologyOnly;
            _names = new(names, StringComparer.OrdinalIgnoreCase);
            Token = new CancellationChangeToken(_changed.Token);
        }

        internal bool Matches(string? path) => path is not null && _names.Contains(Path.GetFileName(path));

        internal void Invalidate()
        {
            lock (_gate)
            {
                if (_disposed || _invalidated) return;
                _invalidated = true;
                _invalidating = true;
                try { _changed.Cancel(); }
                finally
                {
                    _invalidating = false;
                    if (_disposed) _changed.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (!_invalidating) _changed.Dispose();
            }
            _registry.Release(this);
        }
    }
}
