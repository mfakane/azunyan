namespace Azunyan.WinUI;

/// <summary>
/// Describes a Ctrl+Click on text that is no link. The host that goes to the
/// definition of the name there sets <see cref="Handled"/>, and the click only
/// places the caret; otherwise it is an ordinary click.
/// </summary>
public sealed class DefinitionRequestedEventArgs : EventArgs
{
    public DefinitionRequestedEventArgs(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        Position = position;
    }

    /// <summary>The document position clicked.</summary>
    public int Position { get; }

    public bool Handled { get; set; }
}
