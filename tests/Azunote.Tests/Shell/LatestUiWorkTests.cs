using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class LatestUiWorkTests
{
    [Fact]
    public async Task Rejected_dispatcher_stops_worker_and_later_requests_are_harmless()
    {
        var time = new ObservedTime();
        using var worker = new LatestUiWork<int, int>(new RejectedDispatcher(),
            () => throw new InvalidOperationException("Capture must not run"),
            (value, _) => value, _ => Assert.True(false, "Apply must not run"),
            exception => throw exception, time);
        worker.Request();
        await time.WaitForDelay();
        time.Advance();
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        worker.Request();
        worker.Dispose();
        Assert.True(worker.Completion.IsCompletedSuccessfully);
    }

    private sealed class RejectedDispatcher : IUiDispatcher
    {
        public bool TryEnqueue(Action action) => false;
    }

    [Fact]
    public async Task Request_during_evaluation_invalidates_work_and_keeps_only_latest_input()
    {
        var time = new ObservedTime();
        var dispatcher = new QueuedDispatcher();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var input = 1;
        var applied = new List<int>();
        using var worker = new LatestUiWork<int, int>(dispatcher, () => input,
            (value, isCurrent) =>
            {
                if (value == 1)
                {
                    started.SetResult();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                    Assert.False(isCurrent());
                }
                return value;
            }, applied.Add, exception => throw exception, time);
        worker.Request();
        await time.WaitForDelay();
        time.Advance();
        await dispatcher.RunNext();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        input = 2;
        worker.Request();
        release.Set();
        await time.WaitForDelay();
        time.Advance();
        await dispatcher.RunNext();
        await dispatcher.RunNext();
        Assert.Equal(new[] { 2 }, applied);
    }

    [Fact]
    public async Task Burst_captures_latest_input_once_and_applies_on_dispatcher()
    {
        var time = new ObservedTime();
        var dispatcher = new QueuedDispatcher();
        var input = 0;
        var captures = 0;
        var evaluations = 0;
        var applied = new List<int>();
        using var worker = new LatestUiWork<int, int>(dispatcher,
            () => { captures++; return input; },
            (value, _) => { Interlocked.Increment(ref evaluations); return value; },
            applied.Add, exception => throw exception, time);

        worker.Request();
        await time.WaitForDelay();
        for (input = 1; input <= 100; input++) worker.Request();
        input = 100;
        Assert.Equal(0, captures);
        time.Advance();
        await dispatcher.RunNext(); // capture
        var apply = await dispatcher.TakeNext();
        Assert.Empty(applied);
        apply();

        Assert.Equal(new[] { 100 }, applied);
        Assert.Equal(1, captures);
        Assert.Equal(1, evaluations);
        worker.Dispose();
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Change_while_UI_apply_is_pending_discards_old_result_and_does_not_queue_more_callbacks()
    {
        var time = new ObservedTime();
        var dispatcher = new QueuedDispatcher();
        var input = 1;
        var applied = new List<int>();
        using var worker = new LatestUiWork<int, int>(dispatcher, () => input,
            (value, _) => value, applied.Add, exception => throw exception, time);
        worker.Request();
        await time.WaitForDelay();
        time.Advance();
        await dispatcher.RunNext();
        var oldApply = await dispatcher.TakeNext();
        input = 2;
        for (var i = 0; i < 100; i++) worker.Request();
        Assert.Equal(0, dispatcher.PendingCount);
        oldApply();
        Assert.Empty(applied);

        await time.WaitForDelay();
        time.Advance();
        await dispatcher.RunNext();
        await dispatcher.RunNext();
        Assert.Equal(new[] { 2 }, applied);
    }

    [Fact]
    public async Task Closing_window_drops_pending_capture_and_stops_without_UI_pump()
    {
        var time = new ObservedTime();
        var dispatcher = new QueuedDispatcher();
        var captures = 0;
        using var worker = new LatestUiWork<int, int>(dispatcher, () => ++captures,
            (value, _) => value, _ => Assert.True(false, "Disposed work was applied"),
            exception => throw exception, time);
        worker.Request();
        await time.WaitForDelay();
        time.Advance();
        var capture = await dispatcher.TakeNext();
        worker.Dispose();
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        worker.Request();
        capture();
        Assert.Equal(0, captures);
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>();
        public int PendingCount => _queue.Reader.Count;
        public bool TryEnqueue(Action action) => _queue.Writer.TryWrite(action);
        public async Task<Action> TakeNext() => await _queue.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public async Task RunNext() => (await TakeNext())();
    }

    private sealed class ObservedTime : TimeProvider
    {
        private readonly FakeTimeProvider _time = new();
        private readonly Channel<bool> _delays = Channel.CreateUnbounded<bool>();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _time.CreateTimer(callback, state, dueTime, period);
            _delays.Writer.TryWrite(true);
            return timer;
        }
        public async Task WaitForDelay() => await _delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public void Advance() => _time.Advance(TimeSpan.FromMilliseconds(50));
    }
}
