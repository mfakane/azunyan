namespace Azunyan.Core;

/// <summary>
/// An immutable view of a document at one point in time. A snapshot retains
/// the persistent text tree, so taking a snapshot does not copy the document.
/// </summary>
public sealed class TextSnapshot
{
    private readonly TextTree _tree;
    private string? _text;
    private LineIndex? _lineIndex;

    public TextSnapshot(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _tree = new TextTree(text);
    }

    internal TextSnapshot(TextTree tree)
    {
        _tree = tree;
    }

    public static TextSnapshot Empty { get; } = new(string.Empty);

    public int Length => _tree.Length;

    public string Text
    {
        get
        {
            var text = Volatile.Read(ref _text);
            if (text is not null)
            {
                return text;
            }

            var materialized = _tree.Materialize();
            Interlocked.CompareExchange(ref _text, materialized, null);
            return _text!;
        }
    }

    public char this[int position] => _tree.GetCharAt(position);

    public LineIndex Lines
    {
        get
        {
            var lineIndex = Volatile.Read(ref _lineIndex);
            if (lineIndex is not null)
            {
                return lineIndex;
            }

            var created = new LineIndex(this);
            Interlocked.CompareExchange(ref _lineIndex, created, null);
            return _lineIndex!;
        }
    }

    public LineIndex LineIndex => Lines;

    public string GetText(TextRange range) => _tree.GetText(range);

    public string GetText(int start, int length) => GetText(new TextRange(start, length));

    public TextRange? Find(
        string query,
        int startPosition = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateSearchPosition(startPosition);

        if (query.Length == 0)
        {
            return TextRange.Empty(startPosition);
        }

        var index = Text.IndexOf(query, startPosition, comparison);
        return index < 0 ? null : new TextRange(index, query.Length);
    }

    public IReadOnlyList<TextRange> FindAll(
        string query,
        StringComparison comparison = StringComparison.Ordinal,
        bool allowOverlapping = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0 || query.Length > Length)
        {
            return Array.Empty<TextRange>();
        }

        var matches = new List<TextRange>();
        var position = 0;
        while (position <= Length - query.Length)
        {
            var index = Text.IndexOf(query, position, comparison);
            if (index < 0)
            {
                break;
            }

            matches.Add(new TextRange(index, query.Length));
            position = index + (allowOverlapping ? 1 : query.Length);
        }

        return matches;
    }

    internal TextTree Tree => _tree;

    private void ValidateSearchPosition(int position)
    {
        if (position < 0 || position > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
    }
}
