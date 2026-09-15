using Azunyan.Core;
using Azunyan.Syntax;

namespace Azunote;

/// <summary>
/// Link support shared by every language mode. URLs are highlighted on top of
/// the selected mode, so a link inside a comment or a string keeps both its
/// own appearance and the surrounding syntax.
/// </summary>
internal static class DocumentLinks
{
    private static readonly string[] NavigableSchemes =
    [
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeFtp,
        Uri.UriSchemeFtps,
        Uri.UriSchemeFile,
        Uri.UriSchemeMailto
    ];

    /// <summary>
    /// Layers URL highlighting over a language mode's own syntax. A mode
    /// without syntax, such as plain text, still highlights URLs.
    /// </summary>
    public static ISyntaxProvider Attach(ISyntaxProvider? syntax) =>
        syntax is null
            ? new UrlSyntaxRule()
            : new OverlaySyntaxProvider(syntax, new UrlSyntaxRule());

    /// <summary>
    /// Accepts the link text only when it is an absolute URL with a scheme
    /// Windows can be asked to open.
    /// </summary>
    public static bool TryCreateNavigableUri(string? text, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)
            || !NavigableSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
