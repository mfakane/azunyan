using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Azunote.Tests;

public sealed class WatchedAvailabilityCacheTests
{
    [Fact]
    public void Change_during_dependency_registration_cannot_publish_an_unexpired_cache_entry()
    {
        using var workspace = new Workspace();
        var path = Path.Combine(workspace.Child, ".env");
        File.WriteAllText(path, "VALUE=first");
        var native = new FakeDirectoryWatches();
        native.OnCreated = watch =>
        {
            if (watch.Directory == workspace.Root) Signal(native, workspace.Child, ".env");
        };
        using var registry = new SharedFileWatchRegistry(native.Create);
        using var cache = new ExternalToolAvailabilityCache(watches: registry);
        Assert.Equal("first", cache.DotEnv(workspace.Child)["VALUE"]);
        // No second notification: the token cancelled during construction must
        // already make the first entry unusable.
        native.OnCreated = null;
        File.WriteAllText(path, "VALUE=second");
        Assert.Equal("second", cache.DotEnv(workspace.Child)["VALUE"]);
    }

    [Fact]
    public void Watched_dotenv_reuses_value_beyond_short_TTL_and_observes_nearer_creation_edit_and_delete()
    {
        using var workspace = new Workspace();
        File.WriteAllText(Path.Combine(workspace.Root, ".env"), "VALUE=parent");
        var native = new FakeDirectoryWatches();
        var time = new FakeTimeProvider();
        using var registry = new SharedFileWatchRegistry(native.Create, time);
        var invalidations = 0;
        using var cache = new ExternalToolAvailabilityCache(time, watches: registry, invalidated: () => invalidations++);
        var initial = cache.DotEnv(workspace.Child);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Same(initial, cache.DotEnv(workspace.Child));
        Assert.Equal("parent", initial["VALUE"]);

        File.WriteAllText(Path.Combine(workspace.Child, ".env"), "VALUE=child1");
        Signal(native, workspace.Child, ".env");
        Assert.True(invalidations > 0);
        Assert.Equal("child1", cache.DotEnv(workspace.Child)["VALUE"]);
        File.WriteAllText(Path.Combine(workspace.Child, ".env"), "VALUE=child2");
        Signal(native, workspace.Child, ".env");
        Assert.Equal("child2", cache.DotEnv(workspace.Child)["VALUE"]);
        File.Delete(Path.Combine(workspace.Child, ".env"));
        Signal(native, workspace.Child, ".env");
        Assert.Equal("parent", cache.DotEnv(workspace.Child)["VALUE"]);
    }

    [Fact]
    public void Default_workspace_tracks_git_and_editorconfig_precedence_and_content()
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".git"));
        var native = new FakeDirectoryWatches();
        var time = new FakeTimeProvider();
        using var registry = new SharedFileWatchRegistry(native.Create, time);
        using var cache = new ExternalToolAvailabilityCache(time, watches: registry);
        var file = Path.Combine(workspace.Child, "document.txt");
        Assert.Equal(workspace.Root, cache.Workspace(file, null));
        File.WriteAllText(Path.Combine(workspace.Child, ".editorconfig"), "root = true");
        Signal(native, workspace.Child, ".editorconfig");
        Assert.Equal(workspace.Child, cache.Workspace(file, null));
        File.WriteAllText(Path.Combine(workspace.Child, ".editorconfig"), "root = false");
        Signal(native, workspace.Child, ".editorconfig");
        Assert.Equal(workspace.Root, cache.Workspace(file, null));
        // A .git marker may be a file, not just a directory (e.g. worktrees).
        File.WriteAllText(Path.Combine(workspace.Child, ".git"), "gitdir: elsewhere");
        Signal(native, workspace.Child, ".git");
        Assert.Equal(workspace.Child, cache.Workspace(file, null));
    }

    [Fact]
    public void Missing_dotenv_is_invalidated_by_creation_and_safety_TTL_repairs_a_missed_event()
    {
        using var workspace = new Workspace();
        var native = new FakeDirectoryWatches();
        var time = new FakeTimeProvider();
        using var registry = new SharedFileWatchRegistry(native.Create, time);
        using var cache = new ExternalToolAvailabilityCache(time, watches: registry);
        var missing = cache.DotEnv(workspace.Child);
        File.WriteAllText(Path.Combine(workspace.Child, ".env"), "VALUE=created");
        Signal(native, workspace.Child, ".env");
        Assert.Equal("created", cache.DotEnv(workspace.Child)["VALUE"]);
        File.WriteAllText(Path.Combine(workspace.Child, ".env"), "VALUE=missed-event");
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("missed-event", cache.DotEnv(workspace.Child)["VALUE"]);
        Assert.NotSame(missing, cache.DotEnv(workspace.Child));
    }

    [Fact]
    public void Unavailable_watch_uses_short_TTL_and_window_caches_share_watchers_until_disposal()
    {
        using var workspace = new Workspace();
        var time = new FakeTimeProvider();
        using (var unavailable = new SharedFileWatchRegistry((_, _, _) => null, time))
        using (var fallback = new ExternalToolAvailabilityCache(time, watches: unavailable))
        {
            fallback.DotEnv(workspace.Child);
            File.WriteAllText(Path.Combine(workspace.Child, ".env"), "VALUE=updated");
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal("updated", fallback.DotEnv(workspace.Child)["VALUE"]);
        }
        var native = new FakeDirectoryWatches();
        using var registry = new SharedFileWatchRegistry(native.Create, time);
        using var first = new ExternalToolAvailabilityCache(time, watches: registry);
        using var second = new ExternalToolAvailabilityCache(time, watches: registry);
        first.DotEnv(workspace.Child);
        var count = native.Watches.Count;
        second.DotEnv(workspace.Child);
        Assert.Equal(count, native.Watches.Count);
        first.Dispose();
        Assert.All(native.Watches, watch => Assert.False(watch.Disposed));
        second.Dispose();
        Assert.All(native.Watches, watch => Assert.True(watch.Disposed));
    }

    [Fact]
    public async Task Native_watcher_invalidates_dotenv_and_notifies_idle_menu_without_waiting_for_TTL()
    {
        using var workspace = new Workspace();
        using var registry = new SharedFileWatchRegistry();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = new ExternalToolAvailabilityCache(watches: registry,
            invalidated: () => changed.TrySetResult());
        cache.DotEnv(workspace.Child);
        File.WriteAllText(Path.Combine(workspace.Child, ".env"), "VALUE=native");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("native", cache.DotEnv(workspace.Child)["VALUE"]);
    }

    private static void Signal(FakeDirectoryWatches native, string directory, string name)
    {
        foreach (var watch in native.Watches.Where(watch => !watch.Disposed && watch.Directory == directory).ToArray())
            watch.Change(name);
    }

    private sealed class Workspace : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("azunote-watched-cache-");
        public string Root => _root.FullName;
        public string Child { get; }
        public Workspace() => Child = Directory.CreateDirectory(Path.Combine(Root, "child")).FullName;
        public void Dispose() => _root.Delete(recursive: true);
    }
}
