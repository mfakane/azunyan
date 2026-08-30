namespace Azunyan.Core;

/// <summary>
/// One caret and its independent selection. The preferred display column is
/// retained for vertical navigation, just like a normal editor caret.
/// </summary>
public readonly record struct TextCaretState
{
    public TextCaretState(TextSelection selection, int preferredDisplayColumn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(preferredDisplayColumn);

        Selection = selection;
        PreferredDisplayColumn = preferredDisplayColumn;
    }

    public TextSelection Selection { get; }

    public int PreferredDisplayColumn { get; }

    public int CaretPosition => Selection.CaretPosition;

    public TextCaretState WithSelection(TextSelection selection) =>
        new(selection, PreferredDisplayColumn);

    public TextCaretState WithPreferredDisplayColumn(int column) =>
        new(Selection, column);
}

/// <summary>
/// Immutable ordered caret collection. The primary caret is the one exposed
/// through the legacy Document.Selection API and synchronized to the native
/// text input window.
/// </summary>
public sealed class TextCaretSet : IReadOnlyList<TextCaretState>, IEquatable<TextCaretSet>
{
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<TextCaretState> _carets;

    public TextCaretSet(IEnumerable<TextCaretState> carets, int primaryIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(carets);

        var copy = carets.ToArray();
        if (copy.Length == 0)
        {
            throw new ArgumentException("At least one caret is required.", nameof(carets));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(primaryIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(primaryIndex, copy.Length);
        _carets = Array.AsReadOnly(copy);
        PrimaryIndex = primaryIndex;
    }

    public int Count => _carets.Count;

    public int PrimaryIndex { get; }

    public TextCaretState Primary => _carets[PrimaryIndex];

    public TextCaretState this[int index] => _carets[index];

    public IEnumerator<TextCaretState> GetEnumerator() => _carets.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    public TextCaretSet WithPrimaryIndex(int primaryIndex) =>
        new(_carets, primaryIndex);

    public bool Equals(TextCaretSet? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || PrimaryIndex != other.PrimaryIndex || Count != other.Count)
        {
            return false;
        }

        return this.SequenceEqual(other);
    }

    public override bool Equals(object? obj) => Equals(obj as TextCaretSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PrimaryIndex);
        foreach (var caret in _carets)
        {
            hash.Add(caret);
        }

        return hash.ToHashCode();
    }
}
