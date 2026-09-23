using Azunyan.Core;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private bool _viewportRenderScheduled;
    private bool _executingScheduledViewportRender;
    private long _viewportRenderTicket;

    private void RequestViewportRender()
    {
        if (_disposed || !IsLoaded || _viewportRenderScheduled)
        {
            return;
        }

        _viewportRenderScheduled = true;
        var ticket = checked(++_viewportRenderTicket);
        if (!DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.High,
                () => RunScheduledViewportRender(ticket)))
        {
            _viewportRenderScheduled = false;
        }
    }

    private void RunScheduledViewportRender(long ticket)
    {
        if (ticket != _viewportRenderTicket)
        {
            return;
        }

        _viewportRenderScheduled = false;
        if (_disposed || !IsLoaded)
        {
            return;
        }

        _executingScheduledViewportRender = true;
        try
        {
            RenderViewport();
        }
        finally
        {
            _executingScheduledViewportRender = false;
        }
    }

    private void InvalidateScheduledViewportRender()
    {
        _viewportRenderScheduled = false;
        _viewportRenderTicket = checked(_viewportRenderTicket + 1);
    }

    private void RenderViewport()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!_executingScheduledViewportRender)
        {
            InvalidateScheduledViewportRender();
        }

        var traceRender = IsDiagnosticEnabled(AzunyanDiagnosticCategory.Render);
        var renderStarted = traceRender ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (traceRender)
        {
            LogDiagnosticStage(
                AzunyanDiagnosticCategory.Render,
                "render-start",
                $"input={_latestInputSequence}; selection={Document.Selection}; "
                + $"caretCount={Document.CaretSet.Count}; wrapping={TextWrapping}");
        }

        var lineIndex = Snapshot.Lines;
        var lineCount = lineIndex.LineCount;
        var verticalOffset = GetVerticalOffset();
        var viewportWidth = Math.Max(1, ProjectedSurfaceHost.ActualWidth);
        var viewportHeight = Math.Max(1, ProjectedSurfaceHost.ActualHeight);
        var previousScrollMaximum = GetProjectedScrollMaximum(viewportHeight);
        var wasAtBottom = IsProjectedTextSurface
            && previousScrollMaximum > 0.5
            && verticalOffset >= previousScrollMaximum - 0.5;
        var viewportAnchor = DocumentAnchor.Before(0);
        var offsetWithinRow = 0d;
        (TextSnapshot Snapshot, DocumentAnchor Anchor, double OffsetWithinRow)? pendingAnchor =
            _pendingViewportAnchor is { } pendingForSnapshot
            && ReferenceEquals(pendingForSnapshot.Snapshot, Snapshot)
                ? pendingForSnapshot
                : null;
        var preserveViewport = pendingAnchor is not null
            || (!_projectedScrollInteraction
            && _selectionPointerId is null
            && !wasAtBottom
            && IsProjectedTextSurface
            && _defaultRenderer.TextRenderer.TryGetViewportAnchor(
                Snapshot,
                verticalOffset,
                out viewportAnchor,
                out offsetWithinRow));
        if (pendingAnchor is { } pending)
        {
            viewportAnchor = pending.Anchor;
            offsetWithinRow = pending.OffsetWithinRow;
        }
        var horizontalOffset = GetHorizontalOffset();
        var firstVisibleLine = Math.Clamp(
            (int)Math.Floor(verticalOffset / _lineHeight) - 1,
            0,
            Math.Max(0, lineCount - 1));
        var lastVisibleLine = Math.Clamp(
            (int)Math.Ceiling((verticalOffset + viewportHeight) / _lineHeight) + 1,
            0,
            Math.Max(0, lineCount - 1));

        var currentFrame = GetCurrentFrame();
        var digits = Math.Max(1, lineCount.ToString(CultureInfo.InvariantCulture).Length);
        var providerGutter = currentFrame?.Viewport?.Gutter;
        var supportsLogicalLineGutter = IsProjectedTextSurface;
        var hasProviderGutter = supportsLogicalLineGutter && providerGutter is { Count: > 0 };
        var providerGutterDigits = hasProviderGutter
            ? providerGutter!.Max(item => item.Text.Length)
            : 0;
        var showLogicalLineNumbers = supportsLogicalLineGutter && ShowLineNumbers;
        var folds = _foldStateTracker.Folds;
        var hasFoldGutter = supportsLogicalLineGutter && folds.Count > 0;
        var foldGutterWidth = hasFoldGutter ? 20 : 0;
        var gutterWidth = showLogicalLineNumbers || hasProviderGutter
            ? Math.Max(
                32,
                (Math.Max(digits, providerGutterDigits) * _characterWidth)
                    + 16
                    + foldGutterWidth)
            : foldGutterWidth;
        GutterColumn.Width = new GridLength(gutterWidth);
        GutterCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, gutterWidth, viewportHeight)
        };
        GutterDrawingSurface.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, gutterWidth, viewportHeight)
        };
        TextRenderLayer.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, viewportWidth, viewportHeight)
        };
        TextDrawingSurface.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, viewportWidth, viewportHeight)
        };
        RenderOverlay.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, viewportWidth, viewportHeight)
        };
        GutterCanvas.Children.Clear();
        TextRenderLayer.Children.Clear();
        RenderOverlay.Children.Clear();

        var frame = new AzunyanEditorRenderFrame(
            Snapshot,
            Document.Selection,
            Document.CaretSet.Primary.CaretAnchor,
            _blockSelection,
            Document.CaretSet.Count > 1 ? Document.CaretSet : null,
            CompositionRange,
            _colorScheme,
            _lineHeight,
            _characterWidth,
            verticalOffset,
            horizontalOffset,
            gutterWidth,
            viewportWidth,
            viewportHeight,
            firstVisibleLine,
            lastVisibleLine,
            InputWindow.NativeTextBoxControl.FontFamily,
            InputWindow.NativeTextBoxControl.FontSize,
            InputWindow.NativeTextBoxControl.Padding.Left,
            InputWindow.NativeTextBoxControl.Padding.Top,
            showLogicalLineNumbers,
            TextWrapping,
            TabDisplaySize,
            _foldStateTracker.CollapsedIds,
            folds,
            currentFrame);
        if (traceRender)
        {
            frame.DiagnosticInputSequence = _latestInputSequence;
            frame.DiagnosticInputStarted = _latestInputStarted;
            frame.DiagnosticSink = message => LogDiagnostic(
                AzunyanDiagnosticCategory.Render,
                message);
        }
        _renderer?.Render(frame);
        if (TryGetRendererCaretRect(
                Document.CaretSet.Primary.CaretAnchor,
                out var caretRect))
        {
            InputWindow.SetCaretRect(caretRect);
            DispatcherQueue.TryEnqueue(ReconcileInputWindowCaret);
        }
        CreateProjectedAutomationChildren();
        if (wasAtBottom
            && !_preservingViewport
            && GetProjectedScrollMaximum(viewportHeight) > GetVerticalOffset() + 0.5)
        {
            _pendingViewportAnchor = null;
            _projectedVerticalOffset = GetProjectedScrollMaximum(viewportHeight);
            _preservingViewport = true;
            try
            {
                RenderViewport();
            }
            finally
            {
                _preservingViewport = false;
            }

            return;
        }

        if (preserveViewport
            && !_preservingViewport
            && _defaultRenderer.TextRenderer.TryGetViewportOffset(
                viewportAnchor,
                offsetWithinRow,
                out var restoredOffset)
            && Math.Abs(restoredOffset - GetVerticalOffset()) > 0.5)
        {
            _pendingViewportAnchor = null;
            _projectedVerticalOffset = restoredOffset;
            _preservingViewport = true;
            try
            {
                RenderViewport();
            }
            finally
            {
                _preservingViewport = false;
            }

            return;
        }

        _pendingViewportAnchor = null;

        UpdateProjectedScrollExtent(viewportHeight);
        UpdateCompletionPopup();
        UpdateTooltipPopup();
        _automationPeer?.NotifyLayoutChanged();
        if (traceRender)
        {
            LogDiagnosticStage(
                AzunyanDiagnosticCategory.Render,
                "render-finished",
                $"input={_latestInputSequence}; elapsedMs="
                + $"{System.Diagnostics.Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds:F3}; "
                + DescribeDiagnosticState());
        }
    }

    private void ReconcileInputWindowCaret()
    {
        if (!IsLoaded
            || !TryGetRendererCaretRect(
                Document.CaretSet.Primary.CaretAnchor,
                out var desiredRect))
        {
            return;
        }

        try
        {
            var actualRect = InputWindow.GetCaretRect(EditorHost);
            if (actualRect.IsEmpty)
            {
                // Native caret geometry can be unavailable after selection
                // changes. Keep the projected position until the next render.
                return;
            }
            var deltaX = desiredRect.X - actualRect.X;
            var deltaY = desiredRect.Y - actualRect.Y;
            if (Math.Abs(deltaX) <= 0.5 && Math.Abs(deltaY) <= 0.5)
            {
                return;
            }

            InputWindow.SetCaretRect(
                new Rect(
                    Canvas.GetLeft(InputWindow) + deltaX,
                    Canvas.GetTop(InputWindow) + deltaY,
                    desiredRect.Width,
                    desiredRect.Height));
        }
        catch (InvalidOperationException)
        {
            // The native template may not have been laid out yet. The next
            // render pass will retry using the current caret and window.
        }
    }
}
