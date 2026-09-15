using System.Globalization;
using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Finds foldable JSON objects and arrays. A fold hides everything between a
/// bracket and its match, so a collapsed container still shows the brackets
/// that say what it is. A container written on one line has nothing to gain
/// from folding and is skipped.
/// </summary>
/// <remarks>
/// A fold keeps its identity across edits through the path of the container,
/// such as <c>$/items/2</c>, which is what lets a collapsed container stay
/// collapsed while the text above it changes.
/// </remarks>
public sealed class JsonFoldingProvider : IFoldingProvider
{
    public ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.Snapshot;
        var text = snapshot.Text;
        var folds = new List<FoldRange>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var frames = new List<JsonFrame>();
        var position = 0;

        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = text[position];
            switch (character)
            {
                case '"':
                    var stringEnd = SkipString(text, position);
                    if (frames.Count > 0
                        && frames[^1].IsObject
                        && IsKey(text, stringEnd))
                    {
                        frames[^1].PendingKey = text[(position + 1)..(stringEnd - 1)];
                    }

                    position = stringEnd;
                    continue;
                case '{':
                case '[':
                    frames.Add(new JsonFrame(
                        character == '{',
                        position,
                        CreatePath(frames)));
                    break;
                case '}':
                case ']':
                    if (frames.Count > 0)
                    {
                        var frame = frames[^1];
                        frames.RemoveAt(frames.Count - 1);
                        if (TryCreateFold(snapshot, frame, position, occurrences, out var fold))
                        {
                            folds.Add(fold);
                        }

                        if (frames.Count > 0)
                        {
                            frames[^1].PendingKey = null;
                        }
                    }

                    break;
                case ',':
                    if (frames.Count > 0)
                    {
                        frames[^1].ItemIndex++;
                        frames[^1].PendingKey = null;
                    }

                    break;
            }

            position++;
        }

        return ValueTask.FromResult<IReadOnlyList<FoldRange>>(folds);
    }

    private static bool TryCreateFold(
        TextSnapshot snapshot,
        JsonFrame frame,
        int closePosition,
        Dictionary<string, int> occurrences,
        out FoldRange fold)
    {
        fold = null!;
        var start = frame.OpenPosition + 1;
        if (closePosition <= start
            || snapshot.Lines.GetLine(frame.OpenPosition) == snapshot.Lines.GetLine(closePosition)
            || string.IsNullOrWhiteSpace(snapshot.GetText(TextRange.FromBounds(start, closePosition))))
        {
            return false;
        }

        var key = (frame.IsObject ? "object:" : "array:") + frame.Path;
        occurrences.TryGetValue(key, out var occurrence);
        occurrences[key] = occurrence + 1;
        fold = new FoldRange(
            $"json-{key}:{occurrence}",
            TextRange.FromBounds(start, closePosition),
            " … ");
        return true;
    }

    /// <summary>
    /// Names a container after its place in the one that holds it: the member
    /// name inside an object, and the element index inside an array.
    /// </summary>
    private static string CreatePath(List<JsonFrame> frames)
    {
        if (frames.Count == 0)
        {
            return "$";
        }

        var parent = frames[^1];
        var index = parent.ItemIndex.ToString(CultureInfo.InvariantCulture);
        var segment = parent.IsObject
            ? parent.PendingKey ?? index
            : index;
        return $"{parent.Path}/{segment}";
    }

    /// <summary>Reports the position after the closing quote.</summary>
    private static int SkipString(string text, int position)
    {
        position++;
        while (position < text.Length)
        {
            var character = text[position++];
            if (character == '\\')
            {
                position++;
                continue;
            }

            if (character == '"')
            {
                break;
            }
        }

        return position;
    }

    private static bool IsKey(string text, int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position < text.Length && text[position] == ':';
    }

    private sealed class JsonFrame
    {
        public JsonFrame(bool isObject, int openPosition, string path)
        {
            IsObject = isObject;
            OpenPosition = openPosition;
            Path = path;
        }

        public bool IsObject { get; }

        public int OpenPosition { get; }

        public string Path { get; }

        public string? PendingKey { get; set; }

        public int ItemIndex { get; set; }
    }
}
