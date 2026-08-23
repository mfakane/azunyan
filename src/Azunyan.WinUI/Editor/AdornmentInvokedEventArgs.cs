using Azunyan.Core;

namespace Azunyan.WinUI;

/// <summary>
/// Describes an interactive inlay or block adornment hit. The editor reports
/// the hit to the host; the host decides which provider action or command to
/// execute.
/// </summary>
public sealed class AdornmentInvokedEventArgs : EventArgs
{
    public AdornmentInvokedEventArgs(
        string id,
        string kind,
        DocumentAnchor anchor,
        AdornmentContent content,
        bool isBlock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(content);
        Id = id;
        Kind = kind;
        Anchor = anchor;
        Content = content;
        IsBlock = isBlock;
    }

    public string Id { get; }

    public string Kind { get; }

    public DocumentAnchor Anchor { get; }

    public AdornmentContent Content { get; }

    public bool IsBlock { get; }
}
