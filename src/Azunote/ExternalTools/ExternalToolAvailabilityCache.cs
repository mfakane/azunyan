using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace Azunote;

/// <summary>Window-owned cache used only for advisory menu evaluation, never execution.</summary>
internal sealed class ExternalToolAvailabilityCache : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _watchedLifetime;
    private readonly SharedFileWatchRegistry? _watches;
    private readonly Action? _invalidated;
    private readonly object _dependenciesGate = new();
    private readonly HashSet<FileWatchDependencies> _dependencies = [];
    private sealed record Cached<T>(T Value);

    public ExternalToolAvailabilityCache(TimeProvider? time = null, TimeSpan? lifetime = null,
        SharedFileWatchRegistry? watches = null, Action? invalidated = null, TimeSpan? watchedLifetime = null)
    {
        _lifetime = lifetime ?? TimeSpan.FromSeconds(1);
        _watchedLifetime = watchedLifetime ?? TimeSpan.FromMinutes(5);
        _watches = watches;
        _invalidated = invalidated;
#pragma warning disable CS0618 // MemoryCache's clock abstraction has not yet migrated to TimeProvider.
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 512,
            Clock = new CacheClock(time ?? TimeProvider.System)
        });
#pragma warning restore CS0618
    }

    internal T Get<T>(object key, Func<T> factory)
    {
        if (_cache.TryGetValue(key, out Cached<T>? cached)) return cached!.Value;
        // Only the single menu worker populates this instance, so no concurrent factories.
        var value = factory();
        _cache.Set(key, new Cached<T>(value), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _lifetime,
            Size = 1
        });
        return value;
    }

    public IReadOnlyDictionary<string, string> ProcessEnvironment() =>
        Get("process-environment", ExternalToolEnvironmentResolver.LoadProcessEnvironment);

    public IReadOnlyDictionary<string, string> DotEnv(string? directory) =>
        GetWatched(("dotenv", directory), (observe, failed) => DotEnvFileLoader.Load(directory, observe, failed), [".env"]);

    public string? Workspace(string? path, string? pattern) =>
        pattern is not null
            ? Get(("workspace", path, pattern, Environment.CurrentDirectory),
                () => WorkspaceFolderResolver.FindForFile(path, pattern))
            : GetWatched(("workspace", path, pattern, Environment.CurrentDirectory),
                (observe, failed) => WorkspaceFolderResolver.FindForFile(path, null, observe, failed), [".git", ".editorconfig"]);

    private T GetWatched<T>(object key, Func<Action<string>?, Action?, T> factory, string[] names)
    {
        if (_cache.TryGetValue(key, out Cached<T>? cached)) return cached!.Value;
        if (_watches is null) return Get(key, () => factory(null, null));
        var dependencies = new FileWatchDependencies(_watches, _invalidated, Released);
        lock (_dependenciesGate) _dependencies.Add(dependencies);
        try
        {
            // Each resolver registers dependencies BEFORE probing that directory.
            // A concurrent change expires the token even if insertion happens later.
            var value = factory(directory => dependencies.ObserveDirectory(directory, names), dependencies.MarkIncomplete);
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = dependencies.FullyMonitored ? _watchedLifetime : _lifetime,
                Size = 1
            };
            dependencies.Attach(options);
            _cache.Set(key, new Cached<T>(value), options);
            return value;
        }
        catch { dependencies.Dispose(); throw; }
    }

    private void Released(FileWatchDependencies dependencies)
    {
        lock (_dependenciesGate) _dependencies.Remove(dependencies);
    }

    public ExternalToolLaunchPlan? Launch(
        string command,
        ExternalToolCommandMode mode,
        string? directory,
        IReadOnlyList<string>? searchPath = null)
    {
        // Expanded commands can contain document/selection text. Do not retain large payloads as keys.
        var searchKey = searchPath is null || searchPath.Count == 0
            ? null
            : string.Join('\0', searchPath);
        if (command.Length > 4096)
        {
            return ExternalToolLaunchResolver.Resolve(command, mode, directory, searchPath);
        }

        return Get(("launch", command, mode, directory, searchKey, Environment.CurrentDirectory,
                Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT")),
            () => ExternalToolLaunchResolver.Resolve(command, mode, directory, searchPath));
    }

    public void Dispose()
    {
        _cache.Dispose();
        // MemoryCache.Dispose does not run eviction callbacks for all remaining entries.
        FileWatchDependencies[] dependencies;
        lock (_dependenciesGate) dependencies = _dependencies.ToArray();
        foreach (var dependency in dependencies) dependency.Dispose();
    }

#pragma warning disable CS0618
    private sealed class CacheClock(TimeProvider time) : ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
#pragma warning restore CS0618
}
