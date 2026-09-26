using System.Threading.Channels;

namespace Azunote;

/// <summary>
/// Coalesces UI invalidations. There is at most one worker and one outstanding UI
/// callback, including when the UI is busy. Only the current generation is applied.
/// </summary>
internal sealed class LatestUiWork<TInput, TResult> : IDisposable
{
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _shutdownGate = new();
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<TInput> _capture;
    private readonly Func<TInput, Func<bool>, TResult> _evaluate;
    private readonly Action<TResult> _apply;
    private readonly Action<Exception> _onError;
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private long _generation;
    private int _disposed;

    public LatestUiWork(IUiDispatcher dispatcher, Func<TInput> capture,
        Func<TInput, Func<bool>, TResult> evaluate, Action<TResult> apply,
        Action<Exception> onError, TimeProvider? time = null, TimeSpan? interval = null)
    {
        _dispatcher = dispatcher;
        _capture = capture;
        _evaluate = evaluate;
        _apply = apply;
        _onError = onError;
        _time = time ?? TimeProvider.System;
        _interval = interval ?? TimeSpan.FromMilliseconds(50);
        Completion = Task.Run(RunAsync);
    }

    internal Task Completion { get; }

    public void Request()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Increment(ref _generation);
        _requests.Writer.TryWrite(true);
    }

    public void DiscardPending()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Increment(ref _generation);
    }

    private async Task RunAsync()
    {
        var token = _shutdown.Token;
        try
        {
            while (await _requests.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                // Throttle, rather than restarting the delay on every keystroke.
                await Task.Delay(_interval, _time, token).ConfigureAwait(false);
                while (_requests.Reader.TryRead(out _)) { }
                try
                {
                    var captured = await OnUiAsync(() =>
                    {
                        while (_requests.Reader.TryRead(out _)) { }
                        return (Generation: Volatile.Read(ref _generation), Input: _capture());
                    }, token).ConfigureAwait(false);
                    bool IsCurrent() => !token.IsCancellationRequested
                        && captured.Generation == Volatile.Read(ref _generation);
                    if (!IsCurrent()) continue;
                    var result = _evaluate(captured.Input, IsCurrent);
                    if (!IsCurrent()) continue;
                    await OnUiAsync(() =>
                    {
                        if (IsCurrent()) _apply(result);
                        return true;
                    }, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _onError(exception);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            // Cancellation can resume this worker before Cancel returns on the UI
            // thread. Do not dispose the source concurrently with that call.
            lock (_shutdownGate)
            {
                Volatile.Write(ref _disposed, 1);
                _shutdown.Dispose();
            }
        }
    }

    private async Task<T> OnUiAsync<T>(Func<T> action, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
            try { completion.TrySetResult(action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
        {
            Dispose();
            token.ThrowIfCancellationRequested();
        }
        return await completion.Task.WaitAsync(token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_shutdownGate)
        {
            if (_disposed != 0) return;
            Volatile.Write(ref _disposed, 1);
            _shutdown.Cancel();
            _requests.Writer.TryComplete();
        }
    }
}
