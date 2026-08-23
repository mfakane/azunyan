using Azunyan.Core;
using Azunyan.WinUI;
using Microsoft.UI.Xaml;
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
        TextRange? compositionRange,
        AzunyanColorScheme colorScheme,
        Canvas gutterLayer,
        Canvas textLayer,
        Canvas overlayLayer,
        double lineHeight,
        double characterWidth,
        double verticalOffset,
        double horizontalOffset,
        double gutterWidth,
        double viewportWidth,
        double viewportHeight,
        int firstVisibleLine,
        int lastVisibleLine,
        FontFamily fontFamily,
        double fontSize,
        double contentLeft,
        double contentTop,
        bool showLineNumbers,
        TextWrapping textWrapping,
        IReadOnlySet<string> collapsedFoldIds,
        EditorProviderFrame? providerFrame)
    {
        Snapshot = snapshot;
        Selection = selection;
        CompositionRange = compositionRange;
        ColorScheme = colorScheme ?? throw new ArgumentNullException(nameof(colorScheme));
        GutterLayer = gutterLayer;
        TextLayer = textLayer;
        OverlayLayer = overlayLayer;
        LineHeight = lineHeight;
        CharacterWidth = characterWidth;
        VerticalOffset = verticalOffset;
        HorizontalOffset = horizontalOffset;
        GutterWidth = gutterWidth;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        FirstVisibleLine = firstVisibleLine;
        LastVisibleLine = lastVisibleLine;
        FontFamily = fontFamily;
        FontSize = fontSize;
        ContentLeft = contentLeft;
        ContentTop = contentTop;
        ShowLineNumbers = showLineNumbers;
        TextWrapping = textWrapping;
        CollapsedFoldIds = collapsedFoldIds;
        ProviderFrame = providerFrame;
        ProviderResults = providerFrame?.ToLegacyResults();
    }

    public TextSnapshot Snapshot { get; }

    public TextSelection Selection { get; }

    /// <summary>
    /// The transient UTF-16 range currently owned by the native IME text
    /// service. It is not part of provider state and must not be persisted as
    /// document content.
    /// </summary>
    public TextRange? CompositionRange { get; }

    public AzunyanColorScheme ColorScheme { get; }

    public Canvas GutterLayer { get; }

    public Canvas TextLayer { get; }

    public Canvas OverlayLayer { get; }

    public double LineHeight { get; }

    public double CharacterWidth { get; }

    public double VerticalOffset { get; }

    public double HorizontalOffset { get; }

    public double GutterWidth { get; }

    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public int FirstVisibleLine { get; }

    public int LastVisibleLine { get; }

    public FontFamily FontFamily { get; }

    public double FontSize { get; }

    public double ContentLeft { get; }

    public double ContentTop { get; }

    public bool ShowLineNumbers { get; }

    public TextWrapping TextWrapping { get; }

    public IReadOnlySet<string> CollapsedFoldIds { get; }

    /// <summary>
    /// The immutable provider channels used to build this render pass. A
    /// channel may be absent while its independent request is still running.
    /// </summary>
    public EditorProviderFrame? ProviderFrame { get; }

    public DocumentProviderResults? DocumentResults => ProviderFrame?.Document;

    public ViewportProviderResults? ViewportResults => ProviderFrame?.Viewport;

    public PositionProviderResults? PositionResults => ProviderFrame?.Position;

    /// <summary>
    /// The newest provider results for this viewport request. A renderer may
    /// use the syntax, decoration, gutter, tooltip, or completion data as
    /// appropriate for its own visual layer.
    /// </summary>
    public EditorProviderResults? ProviderResults { get; }

    public TextRange GetLineRange(int line) => Snapshot.Lines.GetLineRange(line);
}

public interface IAzunyanEditorRenderer
{
    void Render(AzunyanEditorRenderContext context);
}
