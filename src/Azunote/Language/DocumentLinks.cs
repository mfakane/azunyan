using Azunyan.Core;
using Azunyan.Syntax;

namespace Azunote;

/// <summary>How Azunote opens one invoked link.</summary>
internal enum DocumentLinkAction
{
    /// <summary>The link has no target Azunote can open.</summary>
    None,

    /// <summary>Hand the absolute URL to the Windows default handler.</summary>
    OpenUri,

    /// <summary>Open the file in an Azunote window.</summary>
    OpenInEditor,

    /// <summary>Hand the file to the Windows default handler.</summary>
    OpenWithShell
}

internal readonly record struct DocumentLinkTarget(
    DocumentLinkAction Action,
    Uri? Uri = null,
    string? Path = null)
{
    public static DocumentLinkTarget None { get; } = new(DocumentLinkAction.None);
}

/// <summary>
/// Link support shared by every language mode. URLs are highlighted on top of
/// the selected mode, so a link inside a comment or a string keeps both its
/// own appearance and the surrounding syntax. Markdown adds the destination of
/// an inline link, which is how a relative <c>./</c> or <c>../</c> target
/// becomes navigable.
/// </summary>
internal static class DocumentLinks
{
    private static readonly string[] NavigableSchemes =
    [
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeFtp,
        Uri.UriSchemeFtps,
        Uri.UriSchemeMailto
    ];

    /// <summary>
    /// Layers link highlighting over a language mode's own syntax. A mode
    /// without syntax, such as plain text, still highlights URLs.
    /// </summary>
    public static ISyntaxProvider Attach(ISyntaxProvider? syntax, string modeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeId);
        ISyntaxProvider overlay = string.Equals(modeId, "markdown", StringComparison.OrdinalIgnoreCase)
            ? new CompositeSyntaxProvider(
                [new MarkdownLinkSyntaxRule(), new UrlSyntaxRule()])
            : new UrlSyntaxRule();
        return syntax is null
            ? overlay
            : new OverlaySyntaxProvider(syntax, overlay);
    }

    /// <summary>
    /// Decides what an invoked link means. An absolute URL with a scheme
    /// Windows can open is handed to the shell. Anything else is read as a
    /// path, resolved against the folder of the document that contains it, and
    /// opened only when it exists: in Azunote when the file is one of its
    /// language modes, and with the Windows default handler otherwise.
    /// </summary>
    public static DocumentLinkTarget Resolve(
        string? text,
        string? documentPath,
        Func<string, bool> isEditableDocument,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(isEditableDocument);
        var exists = fileExists ?? File.Exists;
        if (string.IsNullOrWhiteSpace(text))
        {
            return DocumentLinkTarget.None;
        }

        var target = text.Trim();
        if (TryCreateNavigableUri(target, out var uri))
        {
            return new DocumentLinkTarget(DocumentLinkAction.OpenUri, Uri: uri);
        }

        if (!TryResolvePath(target, documentPath, out var path) || !exists(path))
        {
            return DocumentLinkTarget.None;
        }

        return new DocumentLinkTarget(
            isEditableDocument(path)
                ? DocumentLinkAction.OpenInEditor
                : DocumentLinkAction.OpenWithShell,
            Path: path);
    }

    /// <summary>
    /// Accepts the link text only when it is an absolute URL with a scheme
    /// Windows can be asked to open. A file URL is left to path resolution so
    /// that it takes the same route as a relative target.
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

    private static bool TryResolvePath(
        string target,
        string? documentPath,
        out string path)
    {
        path = string.Empty;
        if (Uri.TryCreate(target, UriKind.Absolute, out var parsed))
        {
            if (!parsed.IsFile)
            {
                return false;
            }

            path = parsed.LocalPath;
            return true;
        }

        // A Markdown destination carries the fragment and query of a URL even
        // when it points at a file, and percent-encodes the characters a URL
        // cannot hold literally.
        var trimmed = target.Split('#', 2)[0].Split('?', 2)[0];
        if (trimmed.Length == 0)
        {
            return false;
        }

        try
        {
            trimmed = Uri.UnescapeDataString(trimmed).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathFullyQualified(trimmed))
            {
                path = Path.GetFullPath(trimmed);
                return true;
            }

            if (documentPath is null
                || Path.GetDirectoryName(Path.GetFullPath(documentPath)) is not { } directory)
            {
                return false;
            }

            path = Path.GetFullPath(Path.Combine(directory, trimmed));
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }
}
