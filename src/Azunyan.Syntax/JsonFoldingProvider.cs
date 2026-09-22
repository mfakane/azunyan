using System.Globalization;
using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Finds foldable JSON objects and arrays. A collapsed container reads as one
/// line, <c>"items": [ … ],</c>, because the fold takes in the closing bracket
/// and the line it sits on and puts them back through its placeholder. A
/// container written on one line has nothing to gain from folding and is
/// skipped.
/// </summary>
/// <remarks>
/// A fold keeps its identity across edits through the path of the container,
/// such as <c>$/items/2</c>, which is what lets a collapsed container stay
/// collapsed while the text above it changes.
/// </remarks>
public sealed class JsonFoldingProvider : IFoldingAnalysisProvider
{
    public async ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
        => (await GetFoldingAnalysisAsync(context, cancellationToken).ConfigureAwait(false)).Folds;

    public ValueTask<FoldingAnalysis> GetFoldingAnalysisAsync(
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
        var isComplete = true;

        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = text[position];
            if (character == '/' && position + 1 < text.Length)
            {
                if (text[position + 1] == '/')
                {
                    position = SkipLineComment(text, position);
                    continue;
                }

                if (text[position + 1] == '*')
                {
                    var commentEnd = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                    if (commentEnd < 0)
                    {
                        isComplete = false;
                        break;
                    }

                    position = commentEnd + 2;
                    continue;
                }
            }

            if (frames.Count > 0 && frames[^1].IsObject && IsBareKeyStart(character))
            {
                var bareEnd = SkipBareKey(text, position);
                if (IsKey(text, bareEnd))
                {
                    frames[^1].PendingKey = text[position..bareEnd];
                    position = bareEnd;
                    continue;
                }
            }

            switch (character)
            {
                case '"':
                case '\'':
                    var stringEnd = SkipString(text, position, character, out var stringClosed);
                    if (!stringClosed)
                    {
                        isComplete = false;
                        position = stringEnd;
                        continue;
                    }

                    if (frames.Count > 0
                        && frames[^1].IsObject
                        && stringEnd > position
                        && text[stringEnd - 1] == character
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
                    if (frames.Count == 0)
                    {
                        isComplete = false;
                        break;
                    }

                    var frame = frames[^1];
                    if (frame.IsObject != (character == '}'))
                    {
                        isComplete = false;
                        frames.Clear();
                        break;
                    }

                    frames.RemoveAt(frames.Count - 1);
                    if (TryCreateFold(snapshot, frame, position, occurrences, out var fold))
                    {
                        folds.Add(fold);
                    }

                    if (frames.Count > 0)
                    {
                        frames[^1].PendingKey = null;
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

        isComplete &= frames.Count == 0;
        return ValueTask.FromResult(new FoldingAnalysis(folds, isComplete));
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

        var (end, placeholder) = MeasureClosingLine(snapshot, closePosition);
        var key = (frame.IsObject ? "object:" : "array:") + frame.Path;
        occurrences.TryGetValue(key, out var occurrence);
        occurrences[key] = occurrence + 1;
        fold = new FoldRange(
            $"json-{key}:{occurrence}",
            TextRange.FromBounds(start, end),
            placeholder);
        return true;
    }

    /// <summary>
    /// Decides how much of the closing bracket's line the fold takes in. When
    /// only a comma follows the bracket, the fold swallows the whole line,
    /// including its line break, and the placeholder restores what it hid so
    /// the collapsed container stays on one line. Anything else on that line,
    /// such as the <c>{</c> of the next element, belongs to another fold and
    /// is left visible on its own line.
    /// </summary>
    private static (int End, string Placeholder) MeasureClosingLine(
        TextSnapshot snapshot,
        int closePosition)
    {
        var line = snapshot.Lines.GetLine(closePosition);
        var lineEnd = snapshot.Lines.GetLineEnd(line);
        var tail = snapshot
            .GetText(TextRange.FromBounds(closePosition + 1, lineEnd))
            .Trim();
        if (tail.Length > 0 && tail != ",")
        {
            return (closePosition, " … ");
        }

        var end = line + 1 < snapshot.Lines.LineCount
            ? snapshot.Lines.GetLineStart(line + 1)
            : snapshot.Length;
        return (end, $" … {snapshot.GetText(new TextRange(closePosition, 1))}{tail}");
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
    private static int SkipString(string text, int position, char quote, out bool closed)
    {
        closed = false;
        position++;
        while (position < text.Length)
        {
            var character = text[position++];
            if (character == '\\')
            {
                position++;
                continue;
            }

            if (character == quote)
            {
                closed = true;
                break;
            }
        }

        return position;
    }

    private static int SkipLineComment(string text, int position)
    {
        var newline = text.IndexOfAny(['\r', '\n'], position + 2);
        return newline < 0 ? text.Length : newline;
    }

    private static int SkipBlockComment(string text, int position)
    {
        var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + 2;
    }

    private static int SkipBareKey(string text, int position)
    {
        position++;
        while (position < text.Length && IsBareKeyPart(text[position]))
        {
            position++;
        }

        return position;
    }

    private static bool IsBareKeyStart(char character) =>
        character == '_' || character == '$' || char.IsLetter(character);

    private static bool IsBareKeyPart(char character) =>
        character == '_' || character == '$' || char.IsLetterOrDigit(character);

    private static bool IsKey(string text, int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            if (text[position] == '/' && position + 1 < text.Length)
            {
                if (text[position + 1] == '/')
                {
                    position = SkipLineComment(text, position);
                    continue;
                }

                if (text[position + 1] == '*')
                {
                    position = SkipBlockComment(text, position);
                    continue;
                }
            }

            break;
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
