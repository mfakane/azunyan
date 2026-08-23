using Windows.UI;

namespace Azunyan.WinUI;

/// <summary>
/// Immutable colors used by the editor surface and its transient UI. Azunyan
/// owns the shape of this contract; applications own the actual palette.
/// </summary>
public sealed record AzunyanColorScheme
{
    public Color EditorBackground { get; init; }

    public Color EditorForeground { get; init; }

    public Color GutterBackground { get; init; }

    public Color GutterForeground { get; init; }

    public Color SelectionBackground { get; init; }

    public Color CaretForeground { get; init; }

    public Color CompositionForeground { get; init; }

    public Color FoldForeground { get; init; }

    public Color InlayForeground { get; init; }

    public Color HeadingForeground { get; init; }

    public Color KeywordForeground { get; init; }

    public Color StringForeground { get; init; }

    public Color NumberForeground { get; init; }

    public Color CommentForeground { get; init; }

    public Color TaskForeground { get; init; }

    public Color PopupBackground { get; init; }

    public Color PopupForeground { get; init; }

    public Color PopupBorder { get; init; }

    public Color TooltipBackground { get; init; }

    public Color TooltipForeground { get; init; }

    public Color TooltipBorder { get; init; }
}
