using Xunit;

namespace Azunote.Tests;

public sealed class SingleInstanceHostTests
{
    [Fact]
    public async Task A_later_launch_forwards_arguments_to_the_owner()
    {
        var instanceName = $"Azunote.Tests.{Guid.NewGuid():N}";
        using var owner = new SingleInstanceHost(instanceName);
        Assert.True(owner.TryAcquire());

        var received = new TaskCompletionSource<SingleInstanceCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Start(command =>
        {
            received.TrySetResult(command);
            return Task.CompletedTask;
        });

        using var secondary = new SingleInstanceHost(instanceName);
        Assert.False(await Task.Run(secondary.TryAcquire));
        await secondary.ForwardAsync(["--stdin", "--line", "4"], "stdin payload");

        var command = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["--stdin", "--line", "4"], command.Arguments);
        Assert.Equal(Environment.CurrentDirectory, command.WorkingDirectory);
        Assert.Equal("stdin payload", command.StandardInput);
    }

    [Fact]
    public async Task Forwarding_waits_until_the_owner_finishes_processing()
    {
        var instanceName = $"Azunote.Tests.{Guid.NewGuid():N}";
        using var owner = new SingleInstanceHost(instanceName);
        Assert.True(owner.TryAcquire());

        var processingStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Start(async _ =>
        {
            processingStarted.SetResult(null);
            await release.Task;
        });

        using var secondary = new SingleInstanceHost(instanceName);
        var forwarding = secondary.ForwardAsync(["--wait", "notes.txt"], null);
        await processingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(forwarding.IsCompleted);
        release.SetResult(null);
        await forwarding;
    }
}
