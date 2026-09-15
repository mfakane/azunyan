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
            return Task.FromResult(SingleInstanceResponse.Completed("payload"));
        });

        using var secondary = new SingleInstanceHost(instanceName);
        Assert.False(await Task.Run(secondary.TryAcquire));
        var status = await secondary.ForwardAsync(
            ["--stdin", "--line", "4"],
            "stdin payload");

        Assert.Equal(SingleInstanceStatus.Completed, status);
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
            return SingleInstanceResponse.Completed();
        });

        using var secondary = new SingleInstanceHost(instanceName);
        var forwarding = secondary.ForwardAsync(["--wait", "notes.txt"], null);
        await processingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(forwarding.IsCompleted);
        release.SetResult(null);
        await forwarding;
    }
    [Fact]
    public async Task Disposing_the_owner_waits_for_an_answer_in_flight()
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
            return SingleInstanceResponse.Completed("late answer");
        });

        using var secondary = new SingleInstanceHost(instanceName);
        Assert.False(await Task.Run(secondary.TryAcquire));
        var forwarding = secondary.ForwardAsync(["--output", "document", "-"], "text");
        await processingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposing = Task.Run(owner.Dispose);
        await Task.Delay(100);
        Assert.False(disposing.IsCompleted);

        release.SetResult(null);
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(forwarding.IsCompleted);
        Assert.Equal(SingleInstanceStatus.Completed, await forwarding);
    }
}
