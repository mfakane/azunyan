using Azunyan.Core;

namespace Azunyan.WinUI;

/// <summary>
/// Describes a Ctrl+Click on text a syntax provider classified as a link. The
/// editor never navigates by itself; the host decides what a link target means
/// and how to open it.
/// </summary>
public sealed class LinkInvokedEventArgs : EventArgs
{
    public LinkInvokedEventArgs(string text, TextRange range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
        Range = range;
    }

    /// <summary>The document text of the link.</summary>
    public string Text { get; }

    /// <summary>The document range the link occupies.</summary>
    public TextRange Range { get; }
}
