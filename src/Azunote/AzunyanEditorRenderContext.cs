using Azunyan.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Azunote;

/// <summary>
/// Immutable viewport information passed to an editor renderer. Renderers may
/// populate the gutter, text, and overlay layers without owning input,
/// document mutation, or IME state.
/// </summary>
public sealed class AzunyanEditorRenderContext
{
    internal AzunyanEditorRenderContext(
        TextSnapshot snapshot,
        TextSelection selection,
        Canvas gutterLayer,
        Canvas textLayer,
        Canvas overlayLayer,
        double lineHeight,
        double characterWidth,
        double verticalOffset,
        double horizontalOffset,
        double gutterWidth,
        int firstVisibleLine,
        int lastVisibleLine,
        FontFamily fontFamily,
        double fontSize,
        double contentTop,
        bool showLineNumbers)
    {
        Snapshot = snapshot;
        Selection = selection;
        GutterLayer = gutterLayer;
        TextLayer = textLayer;
        OverlayLayer = overlayLayer;
        LineHeight = lineHeight;
        CharacterWidth = characterWidth;
        VerticalOffset = verticalOffset;
        HorizontalOffset = horizontalOffset;
        GutterWidth = gutterWidth;
        FirstVisibleLine = firstVisibleLine;
        LastVisibleLine = lastVisibleLine;
        FontFamily = fontFamily;
        FontSize = fontSize;
        ContentTop = contentTop;
        ShowLineNumbers = showLineNumbers;
    }

    public TextSnapshot Snapshot { get; }

    public TextSelection Selection { get; }

    public Canvas GutterLayer { get; }

    public Canvas TextLayer { get; }

    public Canvas OverlayLayer { get; }

    public double LineHeight { get; }

    public double CharacterWidth { get; }

    public double VerticalOffset { get; }

    public double HorizontalOffset { get; }

    public double GutterWidth { get; }

    public int FirstVisibleLine { get; }

    public int LastVisibleLine { get; }

    public FontFamily FontFamily { get; }

    public double FontSize { get; }

    public double ContentTop { get; }

    public bool ShowLineNumbers { get; }

    public TextRange GetLineRange(int line) => Snapshot.Lines.GetLineRange(line);
}

public interface IAzunyanEditorRenderer
{
    void Render(AzunyanEditorRenderContext context);
}
