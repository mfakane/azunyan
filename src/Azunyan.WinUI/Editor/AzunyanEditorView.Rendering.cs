using Azunyan.Core;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private void RenderViewport()
    {
        if (!IsLoaded)
        {
            return;
        }

        LogDiagnosticStage(
            AzunyanDiagnosticCategory.Render,
            "render-start",
            $"selection={Document.Selection}; caretCount={Document.CaretSet.Count}; "
            + $"wrapping={TextWrapping}");
        UpdateTextMetrics();

        var lineIndex = Snapshot.Lines;
        var lineCount = lineIndex.LineCount;
        var verticalOffset = GetVerticalOffset();
        var viewportAnchor = DocumentAnchor.Before(0);
        var offsetWithinRow = 0d;
        var preserveViewport = IsProjectedTextSurface
            && _defaultRenderer.TextRenderer.TryGetViewportAnchor(
                Snapshot,
                verticalOffset,
                out viewportAnchor,
                out offsetWithinRow);
        var horizontalOffset = GetHorizontalOffset();
        var viewportWidth = Math.Max(1, ProjectedSurfaceHost.ActualWidth);
        var viewportHeight = Math.Max(1, ProjectedSurfaceHost.ActualHeight);
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
        var gutterWidth = showLogicalLineNumbers || hasProviderGutter
            ? Math.Max(32, (Math.Max(digits, providerGutterDigits) * _characterWidth) + 16)
            : 0;
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
            _collapsedFoldIds,
            currentFrame);
        _renderer?.Render(frame);
        if (TryGetRendererCaretRect(
                Document.CaretSet.Primary.CaretAnchor,
                out var caretRect))
        {
            InputWindow.SetCaretRect(caretRect);
            DispatcherQueue.TryEnqueue(ReconcileInputWindowCaret);
        }
        CreateProjectedAutomationChildren();
        if (preserveViewport
            && !_preservingViewport
            && _defaultRenderer.TextRenderer.TryGetViewportOffset(
                viewportAnchor,
                offsetWithinRow,
                out var restoredOffset)
            && Math.Abs(restoredOffset - GetVerticalOffset()) > 0.5)
        {
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

        UpdateProjectedScrollExtent(viewportHeight);
        UpdateCompletionPopup();
        UpdateTooltipPopup();
        _automationPeer?.NotifyLayoutChanged();
        LogDiagnosticStage(
            AzunyanDiagnosticCategory.Render,
            "render-finished",
            DescribeDiagnosticState());
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
