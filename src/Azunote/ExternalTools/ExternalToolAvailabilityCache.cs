using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace Azunote;

/// <summary>Window-owned cache used only for advisory menu evaluation, never execution.</summary>
internal sealed class ExternalToolAvailabilityCache : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly TimeSpan _lifetime;
    private sealed record Cached<T>(T Value);

    public ExternalToolAvailabilityCache(TimeProvider? time = null, TimeSpan? lifetime = null)
    {
        _lifetime = lifetime ?? TimeSpan.FromSeconds(1);
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
        Get(("dotenv", directory), () => DotEnvFileLoader.Load(directory));

    public string? Workspace(string? path, string? pattern) =>
        Get(("workspace", path, pattern, Environment.CurrentDirectory),
            () => WorkspaceFolderResolver.FindForFile(path, pattern));

    public ExternalToolLaunchPlan? Launch(string command, ExternalToolCommandMode mode, string? directory)
    {
        // Expanded commands can contain document/selection text. Do not retain large payloads as keys.
        if (command.Length > 4096) return ExternalToolLaunchResolver.Resolve(command, mode, directory);
        return Get(("launch", command, mode, directory, Environment.CurrentDirectory,
                Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT")),
            () => ExternalToolLaunchResolver.Resolve(command, mode, directory));
    }

    public void Dispose() => _cache.Dispose();

#pragma warning disable CS0618
    private sealed class CacheClock(TimeProvider time) : ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
#pragma warning restore CS0618
}
