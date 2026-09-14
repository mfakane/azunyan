using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Azunote.Tests;

public sealed class ExternalToolAvailabilityCacheTests
{
    [Fact]
    public void Dotenv_creation_edit_and_deletion_are_seen_after_expiration()
    {
        var directory = Directory.CreateTempSubdirectory("azunote-cache-");
        try
        {
            var time = new FakeTimeProvider();
            using var cache = new ExternalToolAvailabilityCache(time);
            var path = Path.Combine(directory.FullName, ".env");
            Assert.Empty(cache.DotEnv(directory.FullName));
            File.WriteAllText(path, "VALUE=one");
            Assert.Empty(cache.DotEnv(directory.FullName));
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal("one", cache.DotEnv(directory.FullName)["VALUE"]);
            File.WriteAllText(path, "VALUE=two");
            Assert.Equal("one", cache.DotEnv(directory.FullName)["VALUE"]);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal("two", cache.DotEnv(directory.FullName)["VALUE"]);
            File.Delete(path);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Empty(cache.DotEnv(directory.FullName));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Missing_command_is_cached_but_execution_resolution_stays_fresh()
    {
        var directory = Directory.CreateTempSubdirectory("azunote-command-cache-");
        try
        {
            var time = new FakeTimeProvider();
            using var cache = new ExternalToolAvailabilityCache(time);
            var path = Path.Combine(directory.FullName, "tool.exe");
            Assert.Null(cache.Launch(path, ExternalToolCommandMode.Executable, null));
            File.WriteAllText(path, "");
            Assert.Null(cache.Launch(path, ExternalToolCommandMode.Executable, null));
            Assert.NotNull(ExternalToolLaunchResolver.Resolve(path, null));
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.NotNull(cache.Launch(path, ExternalToolCommandMode.Executable, null));
            File.Delete(path);
            Assert.Null(ExternalToolLaunchResolver.Resolve(path, null));
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Null(cache.Launch(path, ExternalToolCommandMode.Executable, null));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Environment_snapshot_expires_and_tool_overrides_do_not_leak_between_tools()
    {
        var variable = "AZUNOTE_CACHE_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(variable, "before");
            var time = new FakeTimeProvider();
            using var cache = new ExternalToolAvailabilityCache(time);
            var context = new ExternalToolContext(null, "", "");
            var overridden = new ExternalToolDefinition("tool", environment:
                new Dictionary<string, string> { [variable] = "override" });
            var plain = new ExternalToolDefinition("tool");
            Assert.Equal("override", ExternalToolEnvironmentResolver.Resolve(overridden, context, cache).Values[variable]);
            Assert.Equal("before", ExternalToolEnvironmentResolver.Resolve(plain, context, cache).Values[variable]);
            Environment.SetEnvironmentVariable(variable, "after");
            Assert.Equal("after", ExternalToolEnvironmentResolver.Resolve(plain, context).Values[variable]);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal("after", ExternalToolEnvironmentResolver.Resolve(plain, context, cache).Values[variable]);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Fact]
    public void Cached_resources_do_not_cache_selection_conditions_or_mutable_settings()
    {
        var settings = new ExternalToolSettings
        {
            Name = "Selection tool", Launch = new() { Command = Environment.ProcessPath! },
            When = new() { Selection = "nonEmpty" },
            Environment = new() { ["VALUE"] = "before" }
        };
        var prepared = new PreparedExternalTool(settings);
        settings.When.Selection = "empty";
        settings.Environment["VALUE"] = "after";
        using var cache = new ExternalToolAvailabilityCache();
        Assert.False(prepared.Evaluate(new(null, "text", ""), cache).IsEnabled);
        Assert.True(prepared.Evaluate(new(null, "text", "text"), cache).IsEnabled);
        Assert.Equal("before", prepared.Definition.Environment["VALUE"]);
    }
}
