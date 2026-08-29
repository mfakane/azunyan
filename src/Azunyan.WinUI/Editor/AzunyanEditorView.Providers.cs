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

        var snapshot = InputEditor.Snapshot;
        var selection = InputEditor.Document.Selection;
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
                        && currentFrame?.Document is { } previousResults
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
            if (!IsLoaded
                || generation != _documentProviderGeneration
                || !ReferenceEquals(result.Snapshot, InputEditor.Snapshot))
            {
                return;
            }

            var currentFrame = GetCurrentFrame();
            PublishProviderFrame(new EditorProviderFrame(
                result.Snapshot,
                InputEditor.Document.Selection,
                result,
                currentFrame?.Viewport,
                currentFrame?.Position));
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
            if (!IsLoaded
                || generation != _viewportProviderGeneration
                || !ReferenceEquals(result.Context.Snapshot, InputEditor.Snapshot))
            {
                return;
            }

            var currentFrame = GetCurrentFrame();
            PublishProviderFrame(new EditorProviderFrame(
                result.Context.Snapshot,
                InputEditor.Document.Selection,
                currentFrame?.Document,
                result,
                currentFrame?.Position));
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
            if (!IsLoaded
                || generation != _positionProviderGeneration
                || !ReferenceEquals(result.Context.Snapshot, InputEditor.Snapshot))
            {
                return;
            }

            var currentFrame = GetCurrentFrame();
            PublishProviderFrame(new EditorProviderFrame(
                result.Context.Snapshot,
                InputEditor.Document.Selection,
                currentFrame?.Document,
                currentFrame?.Viewport,
                result));
        });
    }

    private void PublishProviderFrame(EditorProviderFrame frame)
    {
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
        && ReferenceEquals(frame.Snapshot, InputEditor.Snapshot)
            ? frame
            : null;
}
