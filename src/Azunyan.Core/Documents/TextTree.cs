using System.Text;

namespace Azunyan.Core;

// An immutable implicit treap. Each node owns one text piece and the tree
// stores character counts, which keeps edits proportional to the number of
// pieces touched instead of copying the whole document.
internal sealed class TextTree
{
    private readonly Node? _root;

    private TextTree(Node? root)
    {
        _root = root;
    }

    public TextTree(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _root = text.Length == 0 ? null : new Node(new TextPiece(text), Random.Shared.Next());
    }

    public int Length => Node.GetLength(_root);

    public TextTree Insert(int position, string text)
    {
        ValidatePosition(position);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return this;
        }

        var (left, right) = Split(_root, position);
        var inserted = new Node(new TextPiece(text), Random.Shared.Next());
        return new TextTree(Merge(Merge(left, inserted), right));
    }

    public TextTree Delete(TextRange range)
    {
        ValidateRange(range);
        if (range.IsEmpty)
        {
            return this;
        }

        var (left, remainder) = Split(_root, range.Start);
        var (_, right) = Split(remainder, range.Length);
        return new TextTree(Merge(left, right));
    }

    public TextTree Replace(TextRange range, string text)
    {
        ValidateRange(range);
        ArgumentNullException.ThrowIfNull(text);

        var (left, remainder) = Split(_root, range.Start);
        var (_, right) = Split(remainder, range.Length);
        Node? replacement = text.Length == 0 ? null : new Node(new TextPiece(text), Random.Shared.Next());
        return new TextTree(Merge(Merge(left, replacement), right));
    }

    public char GetCharAt(int position)
    {
        ValidatePosition(position);
        if (position == Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var node = _root;
        var offset = position;
        while (node is not null)
        {
            var leftLength = Node.GetLength(node.Left);
            if (offset < leftLength)
            {
                node = node.Left;
            }
            else if (offset < leftLength + node.Piece.Length)
            {
                return node.Piece[offset - leftLength];
            }
            else
            {
                offset -= leftLength + node.Piece.Length;
                node = node.Right;
            }
        }

        throw new InvalidOperationException("The text tree did not contain the requested position.");
    }

    public string GetText(TextRange range)
    {
        ValidateRange(range);
        if (range.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(range.Length);
        AppendRange(_root, range.Start, range.Length, builder);
        return builder.ToString();
    }

    public string Materialize()
    {
        if (Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Length);
        AppendAll(_root, builder);
        return builder.ToString();
    }

    private void ValidatePosition(int position)
    {
        if (position < 0 || position > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
    }

    private void ValidateRange(TextRange range)
    {
        if (range.Start > Length || range.End > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }
    }

    private static (Node? Left, Node? Right) Split(Node? root, int leftLength)
    {
        if (root is null)
        {
            return (null, null);
        }

        var rootLeftLength = Node.GetLength(root.Left);
        var rootEnd = rootLeftLength + root.Piece.Length;

        if (leftLength < rootLeftLength)
        {
            var (left, right) = Split(root.Left, leftLength);
            return (left, new Node(root.Piece, root.Priority, right, root.Right));
        }

        if (leftLength > rootEnd)
        {
            var (left, right) = Split(root.Right, leftLength - rootEnd);
            return (new Node(root.Piece, root.Priority, root.Left, left), right);
        }

        if (leftLength == rootLeftLength)
        {
            var rightRoot = new Node(root.Piece, Random.Shared.Next());
            return (root.Left, Merge(rightRoot, root.Right));
        }

        if (leftLength == rootEnd)
        {
            var leftRoot = new Node(root.Piece, Random.Shared.Next());
            return (Merge(root.Left, leftRoot), root.Right);
        }

        var pieceOffset = leftLength - rootLeftLength;
        var leftPiece = root.Piece.Slice(0, pieceOffset);
        var rightPiece = root.Piece.Slice(pieceOffset, root.Piece.Length - pieceOffset);
        return (
            Merge(root.Left, new Node(leftPiece, Random.Shared.Next())),
            Merge(new Node(rightPiece, Random.Shared.Next()), root.Right));
    }

    private static Node? Merge(Node? left, Node? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        if (left.Priority <= right.Priority)
        {
            return new Node(left.Piece, left.Priority, left.Left, Merge(left.Right, right));
        }

        return new Node(right.Piece, right.Priority, Merge(left, right.Left), right.Right);
    }

    private static void AppendAll(Node? node, StringBuilder builder)
    {
        if (node is null)
        {
            return;
        }

        AppendAll(node.Left, builder);
        node.Piece.AppendTo(builder);
        AppendAll(node.Right, builder);
    }

    private static void AppendRange(Node? node, int start, int length, StringBuilder builder)
    {
        if (node is null || length == 0)
        {
            return;
        }

        var leftLength = Node.GetLength(node.Left);
        var nodeStart = leftLength;
        var nodeEnd = nodeStart + node.Piece.Length;

        if (start < nodeStart)
        {
            var leftCount = Math.Min(length, nodeStart - start);
            AppendRange(node.Left, start, leftCount, builder);
        }

        var pieceStart = Math.Max(0, start - nodeStart);
        var pieceEnd = Math.Min(node.Piece.Length, start + length - nodeStart);
        if (pieceEnd > pieceStart)
        {
            node.Piece.AppendTo(builder, pieceStart, pieceEnd - pieceStart);
        }

        var rightStart = Math.Max(0, start - nodeEnd);
        var consumedBeforeRight = Math.Max(0, nodeEnd - start);
        var rightCount = length - consumedBeforeRight;
        if (rightCount > 0)
        {
            AppendRange(node.Right, rightStart, rightCount, builder);
        }
    }

    private sealed class Node
    {
        public Node(TextPiece piece, int priority, Node? left = null, Node? right = null)
        {
            Piece = piece;
            Priority = priority;
            Left = left;
            Right = right;
            Length = GetLength(left) + piece.Length + GetLength(right);
        }

        public TextPiece Piece { get; }

        public int Priority { get; }

        public Node? Left { get; }

        public Node? Right { get; }

        public int Length { get; }

        public static int GetLength(Node? node) => node?.Length ?? 0;
    }

    private readonly struct TextPiece
    {
        public TextPiece(string source)
            : this(source, 0, source.Length)
        {
        }

        private TextPiece(string source, int start, int length)
        {
            Source = source;
            Start = start;
            Length = length;
        }

        private string Source { get; }

        private int Start { get; }

        public int Length { get; }

        public char this[int offset] => Source[Start + offset];

        public TextPiece Slice(int start, int length) => new(Source, Start + start, length);

        public void AppendTo(StringBuilder builder)
        {
            builder.Append(Source, Start, Length);
        }

        public void AppendTo(StringBuilder builder, int start, int length)
        {
            builder.Append(Source, Start + start, length);
        }
    }
}
