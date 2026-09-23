using Azunyan.Core;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private static readonly TimeSpan DocumentProviderEditDelay = TimeSpan.FromMilliseconds(50);
    private CancellationTokenSource? _documentProviderDelayCancellation;

    private void RequestProviderResults(
        bool requestDocument,
        bool requestViewport,
        bool requestPosition,
        bool requestCompletion = false,
        DocumentChangedEventArgs? documentChange = null)
    {
        if (_disposed || !IsLoaded)
        {
            return;
        }

        var snapshot = Snapshot;
        var selection = Document.Selection;
        var previousFrame = _providerFrame;

        if (requestDocument
            && _providers.Syntax is null
            && _providers.Decorations is null
            && _providers.Folding is null)
        {
            NextProviderGeneration(ref _documentProviderGeneration);
            _documentProviderDelayCancellation?.Cancel();
            _documentProviderDelayCancellation?.Dispose();
            _documentProviderDelayCancellation = null;
            _providerFrame = new EditorProviderFrame(snapshot, selection);
            RequestViewportRender();
            requestDocument = false;
        }

        requestViewport &= _providers.Gutter is not null
            || _providers.Inlay is not null
            || _providers.BlockAdornment is not null;
        requestPosition &= _providers.Tooltip is not null
            || requestCompletion && _providers.Completion is not null;

        if (requestDocument)
        {
            var generation = NextProviderGeneration(ref _documentProviderGeneration);
            var canUseDocumentChange = documentChange is { }
                && ReferenceEquals(documentChange.NewSnapshot, snapshot);
            var previousDocument = canUseDocumentChange
                && previousFrame?.Document is { } previousResults
                && !previousResults.IsProvisional
                && ReferenceEquals(previousResults.Snapshot, documentChange!.OldSnapshot)
                    ? previousResults
                    : null;
            var provisionalRange = GetProvisionalDocumentRange(snapshot, selection.CaretPosition);
            _providerFrame = new EditorProviderFrame(
                snapshot,
                selection,
                previousDocument?.MapUnchangedRanges(
                    snapshot,
                    documentChange!.Change,
                    provisionalRange));
            RequestViewportRender();
            _ = ApplyDocumentProviderResultAsync(
                RequestDocumentProviderResultAsync(
                    snapshot,
                    selection,
                    previousDocument,
                    canUseDocumentChange ? documentChange!.Change : null),
                generation);
        }

        if (requestViewport)
        {
            var generation = NextProviderGeneration(ref _viewportProviderGeneration);
            _ = ApplyViewportProviderResultAsync(
                _providerScheduler.RequestViewportAsync(
                    snapshot,
                    GetVisibleDocumentRange(),
                    selection),
                generation);
        }

        if (requestPosition)
        {
            var generation = NextProviderGeneration(ref _positionProviderGeneration);
            if (!requestDocument)
            {
                var frame = GetCurrentFrame();
                _providerFrame = new EditorProviderFrame(
                    snapshot,
                    selection,
                    frame?.Document,
                    frame?.Viewport);
                RequestViewportRender();
            }
            _ = ApplyPositionProviderResultAsync(
                _providerScheduler.RequestPositionAsync(
                    snapshot,
                    selection.CaretPosition,
                    selection,
                    includeCompletion: requestCompletion),
                generation);
        }
    }

    private static TextRange GetProvisionalDocumentRange(TextSnapshot snapshot, int position)
    {
        const int contextLines = 256;
        var lines = snapshot.Lines;
        var line = lines.GetLine(position);
        var first = Math.Max(0, line - contextLines);
        var last = Math.Min(lines.LineCount - 1, line + contextLines);
        return TextRange.FromBounds(lines.GetLineStart(first), lines.GetLineEnd(last));
    }

    private Task<DocumentProviderResults?> RequestDocumentProviderResultAsync(
        TextSnapshot snapshot,
        TextSelection selection,
        DocumentProviderResults? previousResults,
        TextChange? change)
    {
        _documentProviderDelayCancellation?.Cancel();
        _documentProviderDelayCancellation?.Dispose();
        _documentProviderDelayCancellation = null;
        if (change is null)
        {
            return _providerScheduler.RequestDocumentAsync(
                snapshot,
                selection,
                previousResults: null,
                change: null);
        }

        var cancellation = new CancellationTokenSource();
        _documentProviderDelayCancellation = cancellation;
        return RequestAfterTypingPauseAsync(
            snapshot,
            selection,
            previousResults,
            change.Value,
            cancellation.Token);
    }

    private async Task<DocumentProviderResults?> RequestAfterTypingPauseAsync(
        TextSnapshot snapshot,
        TextSelection selection,
        DocumentProviderResults? previousResults,
        TextChange change,
        CancellationToken cancellationToken)
    {
        await Task.Delay(DocumentProviderEditDelay, cancellationToken).ConfigureAwait(false);
        return await _providerScheduler.RequestDocumentAsync(
            snapshot,
            selection,
            previousResults,
            change,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyDocumentProviderResultAsync(
        Task<DocumentProviderResults?> request,
        long generation)
    {
        DocumentProviderResults? result;
        try
        {
            result = await request.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        if (result is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!IsLoaded
                    || generation != _documentProviderGeneration
                    || !ReferenceEquals(result.Snapshot, Snapshot))
                {
                    return;
                }

                _foldStateTracker.ApplyProviderFolds(
                    result.Snapshot,
                    result.Folds,
                    result.FoldsAreComplete);
                var currentFrame = GetCurrentFrame();
                PublishProviderFrame(new EditorProviderFrame(
                    result.Snapshot,
                    Document.Selection,
                    result,
                    currentFrame?.Viewport,
                    currentFrame?.Position));
            }
            catch (Exception exception)
            {
                ReportDiagnosticException("DocumentProviderResult", exception);
            }
        });
    }

    private async Task ApplyViewportProviderResultAsync(
        Task<ViewportProviderResults?> request,
        long generation)
    {
        ViewportProviderResults? result;
        try
        {
            result = await request.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        if (result is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!IsLoaded
                    || generation != _viewportProviderGeneration
                    || !ReferenceEquals(result.Context.Snapshot, Snapshot))
                {
                    return;
                }

                var currentFrame = GetCurrentFrame();
                PublishProviderFrame(new EditorProviderFrame(
                    result.Context.Snapshot,
                    Document.Selection,
                    currentFrame?.Document,
                    result,
                    currentFrame?.Position));
            }
            catch (Exception exception)
            {
                ReportDiagnosticException("ViewportProviderResult", exception);
            }
        });
    }

    private async Task ApplyPositionProviderResultAsync(
        Task<PositionProviderResults?> request,
        long generation)
    {
        PositionProviderResults? result;
        try
        {
            result = await request.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        if (result is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!IsLoaded
                    || generation != _positionProviderGeneration
                    || !ReferenceEquals(result.Context.Snapshot, Snapshot))
                {
                    return;
                }

                var currentFrame = GetCurrentFrame();
                PublishProviderFrame(new EditorProviderFrame(
                    result.Context.Snapshot,
                    Document.Selection,
                    currentFrame?.Document,
                    currentFrame?.Viewport,
                    result));
            }
            catch (Exception exception)
            {
                ReportDiagnosticException("PositionProviderResult", exception);
            }
        });
    }

    private void PublishProviderFrame(EditorProviderFrame frame)
    {
        // Provider channels complete independently. A partial result must not
        // erase channels that were already computed for this snapshot (most
        // importantly syntax when only the caret changed).
        if (GetCurrentFrame() is { } currentFrame)
        {
            frame = new EditorProviderFrame(
                frame.Snapshot,
                frame.Selection,
                frame.Document ?? currentFrame.Document,
                frame.Viewport ?? currentFrame.Viewport,
                frame.Position ?? currentFrame.Position);
        }

        _providerFrame = frame;
        RequestViewportRender();
        ProviderFrameChanged?.Invoke(this, new EditorProviderFrameEventArgs(frame));
        ProviderResultsChanged?.Invoke(
            this,
            new EditorProviderResultsEventArgs(frame.ToLegacyResults()));
    }

    private void InvalidateProviderGenerations()
    {
        NextProviderGeneration(ref _documentProviderGeneration);
        NextProviderGeneration(ref _viewportProviderGeneration);
        NextProviderGeneration(ref _positionProviderGeneration);
    }

    private static long NextProviderGeneration(ref long generation) =>
        generation = checked(generation + 1);

    private EditorProviderFrame? GetCurrentFrame() =>
        _providerFrame is { } frame
        && ReferenceEquals(frame.Snapshot, Snapshot)
            ? frame
            : null;
}
