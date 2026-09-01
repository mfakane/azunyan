using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Azunyan.WinUI;

/// <summary>
/// Immutable viewport information passed to an editor renderer. This public
/// boundary contains document, layout, and provider data only; WinUI control
/// surfaces remain an implementation detail of the built-in renderer.
/// </summary>
public sealed class AzunyanEditorRenderFrame
{
    internal AzunyanEditorRenderFrame(
        TextSnapshot snapshot,
        TextSelection selection,
        DocumentAnchor primaryCaretAnchor,
        TextBlockSelection? blockSelection,
        TextCaretSet? caretSet,
        TextRange? compositionRange,
        AzunyanColorScheme colorScheme,
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
        int tabDisplaySize,
        IReadOnlySet<string> collapsedFoldIds,
        EditorProviderFrame? providerFrame)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Selection = selection;
        PrimaryCaretAnchor = primaryCaretAnchor;
        BlockSelection = blockSelection;
        CaretSet = caretSet;
        CompositionRange = compositionRange;
        ColorScheme = colorScheme ?? throw new ArgumentNullException(nameof(colorScheme));
        LineHeight = lineHeight;
        CharacterWidth = characterWidth;
        VerticalOffset = verticalOffset;
        HorizontalOffset = horizontalOffset;
        GutterWidth = gutterWidth;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        FirstVisibleLine = firstVisibleLine;
        LastVisibleLine = lastVisibleLine;
        FontFamily = fontFamily ?? throw new ArgumentNullException(nameof(fontFamily));
        FontSize = fontSize;
        ContentLeft = contentLeft;
        ContentTop = contentTop;
        ShowLineNumbers = showLineNumbers;
        TextWrapping = textWrapping;
        TabDisplaySize = tabDisplaySize;
        CollapsedFoldIds = collapsedFoldIds ?? throw new ArgumentNullException(nameof(collapsedFoldIds));
        ProviderFrame = providerFrame;
        ProviderResults = providerFrame?.ToLegacyResults();
    }

    public TextSnapshot Snapshot { get; }

    public TextSelection Selection { get; }

    public DocumentAnchor PrimaryCaretAnchor { get; }

    /// <summary>
    /// The optional display-column selection owned by the projected editor.
    /// The regular <see cref="Selection"/> remains the native IME caret or
    /// linear selection for compatibility with existing renderers.
    /// </summary>
    public TextBlockSelection? BlockSelection { get; }

    public TextCaretSet? CaretSet { get; }

    /// <summary>
    /// The transient UTF-16 range currently owned by the native IME text
    /// service. It is not part of provider state and must not be persisted as
    /// document content.
    /// </summary>
    public TextRange? CompositionRange { get; }

    public AzunyanColorScheme ColorScheme { get; }

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

    public int TabDisplaySize { get; }

    public IReadOnlySet<string> CollapsedFoldIds { get; }

    public EditorProviderFrame? ProviderFrame { get; }

    public DocumentProviderResults? DocumentResults => ProviderFrame?.Document;

    public ViewportProviderResults? ViewportResults => ProviderFrame?.Viewport;

    public PositionProviderResults? PositionResults => ProviderFrame?.Position;

    public EditorProviderResults? ProviderResults { get; }

    public TextRange GetLineRange(int line) => Snapshot.Lines.GetLineRange(line);
}
