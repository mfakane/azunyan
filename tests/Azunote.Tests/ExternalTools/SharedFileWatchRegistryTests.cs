using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Azunote.Tests;

public sealed class SharedFileWatchRegistryTests
{
    [Fact]
    public void Parent_dependency_ignores_content_notifications_but_observes_directory_replacement()
    {
        var native = new FakeDirectoryWatches();
        using var registry = new SharedFileWatchRegistry(native.Create);
        using var parent = registry.TryWatch(Path.GetTempPath(), true, "child")!;
        native.Watches[0].Change("child");
        Assert.False(parent.Token.HasChanged);
        native.Watches[0].Change("renamed", "child");
        Assert.True(parent.Token.HasChanged);
    }

    [Fact]
    public void Parent_rename_retires_watches_bound_to_the_old_directory_handle()
    {
        var native = new FakeDirectoryWatches();
        using var registry = new SharedFileWatchRegistry(native.Create);
        using var parent = registry.TryWatch(Path.GetTempPath(), "child")!;
        using var child = registry.TryWatch(Path.Combine(Path.GetTempPath(), "child"), ".env")!;
        native.Watches[0].Change("renamed", "child");
        Assert.True(parent.Token.HasChanged);
        Assert.True(child.Token.HasChanged);
        Assert.True(native.Watches[1].Disposed);
    }

    [Fact]
    public void Directory_watch_is_shared_and_closed_after_last_subscriber()
    {
        var native = new FakeDirectoryWatches();
        using var registry = new SharedFileWatchRegistry(native.Create);
        using var first = registry.TryWatch(Path.GetTempPath(), ".env")!;
        using var second = registry.TryWatch(Path.GetTempPath(), ".git")!;
        Assert.Single(native.Watches);
        first.Dispose();
        Assert.False(native.Watches[0].Disposed);
        native.Watches[0].Change(".git");
        Assert.True(second.Token.HasChanged);
        second.Dispose();
        Assert.True(native.Watches[0].Disposed);
    }

    [Fact]
    public void Rename_invalidates_old_and_new_names_without_losing_other_changes()
    {
        var native = new FakeDirectoryWatches();
        using var registry = new SharedFileWatchRegistry(native.Create);
        using var env = registry.TryWatch(Path.GetTempPath(), ".env")!;
        using var config = registry.TryWatch(Path.GetTempPath(), ".editorconfig")!;
        native.Watches[0].Change("unrelated.txt");
        Assert.False(env.Token.HasChanged);
        native.Watches[0].Change("removed.env", ".env");
        native.Watches[0].Change(".editorconfig", "temp.txt");
        Assert.True(env.Token.HasChanged);
        Assert.True(config.Token.HasChanged);
    }

    [Fact]
    public void Watcher_error_invalidates_all_subscribers_and_retries_after_backoff()
    {
        var native = new FakeDirectoryWatches();
        var time = new FakeTimeProvider();
        using var registry = new SharedFileWatchRegistry(native.Create, time);
        using var lease = registry.TryWatch(Path.GetTempPath(), ".env")!;
        native.Watches[0].Fail();
        Assert.True(lease.Token.HasChanged);
        Assert.True(native.Watches[0].Disposed);
        Assert.Null(registry.TryWatch(Path.GetTempPath(), ".env"));
        time.Advance(TimeSpan.FromSeconds(2));
        using var replacement = registry.TryWatch(Path.GetTempPath(), ".env")!;
        Assert.NotNull(replacement);
        Assert.False(replacement.Token.HasChanged);
        Assert.Equal(2, native.Watches.Count);
        lease.Dispose();
        Assert.False(native.Watches[1].Disposed);
    }

    [Fact]
    public void Watch_limit_falls_back_and_capacity_is_reused_after_release()
    {
        var native = new FakeDirectoryWatches();
        using var registry = new SharedFileWatchRegistry(native.Create, limit: 1);
        using var lease = registry.TryWatch(Path.GetTempPath(), ".env")!;
        Assert.Null(registry.TryWatch(Path.Combine(Path.GetTempPath(), "child"), ".env"));
        lease.Dispose();
        using var other = registry.TryWatch(Path.Combine(Path.GetTempPath(), "child"), ".env");
        Assert.NotNull(other);
    }
}

internal sealed class FakeDirectoryWatches
{
    public List<Watch> Watches { get; } = [];
    public Action<Watch>? OnCreated { get; set; }
    public IDisposable Create(string directory, Action<string?, string?> change, Action fail)
    {
        var watch = new Watch(directory, change, fail);
        Watches.Add(watch);
        OnCreated?.Invoke(watch);
        return watch;
    }
    internal sealed class Watch(string directory, Action<string?, string?> change, Action fail) : IDisposable
    {
        public string Directory { get; } = directory;
        public bool Disposed { get; private set; }
        public void Change(string name, string? oldName = null) =>
            change(Path.Combine(Directory, name), oldName is null ? null : Path.Combine(Directory, oldName));
        public void Fail() => fail();
        public void Dispose() => Disposed = true;
    }
}
