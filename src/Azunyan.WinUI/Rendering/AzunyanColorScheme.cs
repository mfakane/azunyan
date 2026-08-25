using Windows.UI;

namespace Azunyan.WinUI;

/// <summary>
/// Immutable colors used by the editor surface and its transient UI. Azunyan
/// owns the shape of this contract; applications own the actual palette.
/// </summary>
public sealed record AzunyanColorScheme
{
    public static AzunyanColorScheme Default { get; } = new()
    {
        EditorBackground = Color.FromArgb(0xff, 0xff, 0xff, 0xff),
        EditorForeground = Color.FromArgb(0xff, 0x1a, 0x1a, 0x1a),
        GutterBackground = Color.FromArgb(0xff, 0xff, 0xff, 0xff),
        GutterForeground = Color.FromArgb(0xff, 0x60, 0x60, 0x60),
        SelectionBackground = Color.FromArgb(0x66, 0x00, 0x66, 0xcc),
        CaretForeground = Color.FromArgb(0xff, 0x1a, 0x1a, 0x1a),
        CompositionForeground = Color.FromArgb(0xff, 0x00, 0x66, 0xcc),
        FoldForeground = Color.FromArgb(0xff, 0x60, 0x60, 0x60),
        InlayForeground = Color.FromArgb(0xff, 0x60, 0x60, 0x60),
        HeadingForeground = Color.FromArgb(0xff, 0x00, 0x66, 0xcc),
        KeywordForeground = Color.FromArgb(0xff, 0x00, 0x66, 0xcc),
        StringForeground = Color.FromArgb(0xff, 0x1a, 0x1a, 0x1a),
        NumberForeground = Color.FromArgb(0xff, 0x1a, 0x1a, 0x1a),
        CommentForeground = Color.FromArgb(0xff, 0x60, 0x60, 0x60),
        TaskForeground = Color.FromArgb(0xff, 0x00, 0x66, 0xcc),
        PopupBackground = Color.FromArgb(0xff, 0xf4, 0xf4, 0xf4),
        PopupForeground = Color.FromArgb(0xff, 0x1a, 0x1a, 0x1a),
        PopupBorder = Color.FromArgb(0xff, 0x80, 0x80, 0x80),
        TooltipBackground = Color.FromArgb(0xff, 0xf4, 0xf4, 0xf4),
        TooltipForeground = Color.FromArgb(0xff, 0x1a, 0x1a, 0x1a),
        TooltipBorder = Color.FromArgb(0xff, 0x80, 0x80, 0x80)
    };

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

    /// <summary>
    /// Optionally resolves a foreground for any syntax classification. Returning
    /// null retains Azunyan's built-in classification mapping and fallback.
    /// </summary>
    public Func<string, Color?>? SyntaxForegroundResolver { get; init; }

    public Color PopupBackground { get; init; }

    public Color PopupForeground { get; init; }

    public Color PopupBorder { get; init; }

    public Color TooltipBackground { get; init; }

    public Color TooltipForeground { get; init; }

    public Color TooltipBorder { get; init; }

    public Color ResolveSyntaxForeground(string? classification)
    {
        if (!string.IsNullOrEmpty(classification)
            && SyntaxForegroundResolver?.Invoke(classification) is { } resolved)
        {
            return resolved;
        }

        return classification switch
        {
            "heading" => HeadingForeground,
            "keyword" => KeywordForeground,
            "string" => StringForeground,
            "code" => StringForeground,
            "number" => NumberForeground,
            "comment" => CommentForeground,
            "task-marker" => TaskForeground,
            _ => EditorForeground
        };
    }
}
