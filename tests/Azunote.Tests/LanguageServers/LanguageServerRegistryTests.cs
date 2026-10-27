using Azunote;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Azunote.Tests.LanguageServers;

public sealed class LanguageServerRegistryTests
{
    private static readonly string Workspace = Path.GetFullPath("workspace");
    private static readonly LanguageServerDefinition Definition =
        new("fake-server", ["--stdio"], [], null, null, null, null);

    [Fact]
    public async Task Documents_of_one_folder_share_one_server_until_it_idles_out()
    {
        await using var server = new FakeLanguageServer();
        var time = new FakeTimeProvider();
        var starts = 0;
        using var registry = new LanguageServerRegistry(
            (_, _) => true,
            (_, _, _) => { },
            (_, _) =>
            {
                starts++;
                return server.CreateClient(Workspace);
            },
            time);

        var first = registry.Acquire(Launch());
        var second = registry.Acquire(Launch());
        await WaitForStatusAsync(first.Instance, LanguageServerStatus.Running);

        Assert.Same(first.Instance, second.Instance);
        Assert.Equal(1, starts);
        Assert.NotNull(first.Instance.Client);

        first.Dispose();
        second.Dispose();
        time.Advance(LanguageServerRegistry.DefaultIdleTimeout - TimeSpan.FromSeconds(1));
        Assert.Empty(server.ReceivedOf("shutdown"));
        time.Advance(TimeSpan.FromSeconds(1));

        await server.WaitForAsync("shutdown");
    }

    [Fact]
    public async Task A_document_acquiring_again_keeps_an_idle_server()
    {
        await using var server = new FakeLanguageServer();
        var time = new FakeTimeProvider();
        using var registry = new LanguageServerRegistry(
            (_, _) => true,
            (_, _, _) => { },
            (_, _) => server.CreateClient(Workspace),
            time);
        var first = registry.Acquire(Launch());
        await WaitForStatusAsync(first.Instance, LanguageServerStatus.Running);

        first.Dispose();
        time.Advance(TimeSpan.FromSeconds(10));
        using var second = registry.Acquire(Launch());
        time.Advance(LanguageServerRegistry.DefaultIdleTimeout);

        Assert.Same(first.Instance, second.Instance);
        Assert.Equal(LanguageServerStatus.Running, second.Instance.Status);
        Assert.Empty(server.ReceivedOf("shutdown"));
    }

    [Fact]
    public async Task Turning_a_server_off_records_it_and_stops_it_at_once()
    {
        await using var server = new FakeLanguageServer();
        var recorded = new List<(string Workspace, string Command, bool Enabled)>();
        using var registry = new LanguageServerRegistry(
            (_, _) => true,
            (workspace, command, enabled) => recorded.Add((workspace, command, enabled)),
            (_, _) => server.CreateClient(Workspace));
        var changed = 0;
        registry.EnablementChanged += (_, _) => changed++;
        using var lease = registry.Acquire(Launch());
        await WaitForStatusAsync(lease.Instance, LanguageServerStatus.Running);

        registry.SetEnabled(Workspace, Definition, enabled: false);

        Assert.Equal([(Workspace, "fake-server", false)], recorded);
        Assert.Equal(1, changed);
        await server.WaitForAsync("shutdown");
    }

    [Fact]
    public async Task A_server_that_cannot_start_fails_and_starts_anew_when_acquired_again()
    {
        var attempts = 0;
        using var registry = new LanguageServerRegistry(
            (_, _) => true,
            (_, _, _) => { },
            (_, _) =>
            {
                attempts++;
                throw new FileNotFoundException("No such server.");
            });

        var lease = registry.Acquire(Launch());
        await WaitForStatusAsync(lease.Instance, LanguageServerStatus.Failed);
        Assert.Contains("No such server.", lease.Instance.FailureMessage, StringComparison.Ordinal);
        lease.Instance.Log.Subscribe(_ => { }, out var log).Dispose();
        Assert.Contains("No such server.", log, StringComparison.Ordinal);
        lease.Dispose();

        using var again = registry.Acquire(Launch());
        await WaitForStatusAsync(again.Instance, LanguageServerStatus.Failed);

        Assert.NotSame(lease.Instance, again.Instance);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task A_server_that_stops_by_itself_fails_with_why()
    {
        var server = new FakeLanguageServer();
        using var registry = new LanguageServerRegistry(
            (_, _) => true,
            (_, _, _) => { },
            (_, _) => server.CreateClient(Workspace));
        using var lease = registry.Acquire(Launch());
        await WaitForStatusAsync(lease.Instance, LanguageServerStatus.Running);

        server.Close();

        await WaitForStatusAsync(lease.Instance, LanguageServerStatus.Failed);
        Assert.Null(lease.Instance.Client);
        Assert.Contains("stopped", lease.Instance.FailureMessage, StringComparison.Ordinal);
        await server.DisposeAsync();
    }

    private static LanguageServerLaunch Launch() =>
        new(
            Definition,
            new ExternalToolLaunchPlan("fake-server", "fake-server.exe", "fake-server.exe", ExternalToolLaunchKind.Direct),
            ["--stdio"],
            Workspace);

    private static async Task WaitForStatusAsync(LanguageServerInstance instance, LanguageServerStatus status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (instance.Status != status)
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
