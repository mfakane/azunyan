namespace Azunyan.Core;

/// <summary>
/// Classification names Azunyan itself understands. A provider owns its
/// taxonomy and may use any name; only the names listed here receive built-in
/// treatment from the editor beyond a palette lookup.
/// </summary>
public static class SyntaxClassifications
{
    /// <summary>
    /// A navigable link. The editor underlines this classification and offers
    /// Ctrl+Click navigation through its link-invoked event.
    /// </summary>
    public const string Link = "link";

    /// <summary>A line or text added in a change, such as a diff's <c>+</c> line.</summary>
    public const string Inserted = "inserted";

    /// <summary>A line or text removed in a change, such as a diff's <c>-</c> line.</summary>
    public const string Deleted = "deleted";
}
