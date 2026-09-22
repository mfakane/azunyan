using Azunyan.Core;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
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
        var currentFrame = GetCurrentFrame();

        if (requestDocument)
        {
            var generation = NextProviderGeneration(ref _documentProviderGeneration);
            _providerFrame = new EditorProviderFrame(snapshot, selection);
            RenderViewport();
            var canUseDocumentChange = documentChange is { }
                && ReferenceEquals(documentChange.NewSnapshot, snapshot);
            _ = ApplyDocumentProviderResultAsync(
                _providerScheduler.RequestDocumentAsync(
                    snapshot,
                    selection,
                    previousResults: canUseDocumentChange
                        && previousFrame?.Document is { } previousResults
                        && ReferenceEquals(previousResults.Snapshot, documentChange!.OldSnapshot)
                            ? previousResults
                            : null,
                    change: canUseDocumentChange
                        ? documentChange!.Change
                        : null),
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
            _providerFrame = new EditorProviderFrame(
                snapshot,
                selection,
                currentFrame?.Document,
                currentFrame?.Viewport);
            RenderViewport();
            _ = ApplyPositionProviderResultAsync(
                _providerScheduler.RequestPositionAsync(
                    snapshot,
                    selection.CaretPosition,
                    selection,
                    includeCompletion: requestCompletion),
                generation);
        }
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
        RenderViewport();
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
