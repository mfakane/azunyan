using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Finds foldable YAML blocks. YAML states its structure through indentation,
/// so a line that is followed by more deeply indented lines opens a block, and
/// the block ends at the last line that stays inside it. The fold takes in the
/// line break of that last line, so a collapsed block leaves no empty row
/// behind. Blank lines and comment-only lines do not open or close a block,
/// and a block that ends in blank lines keeps them outside the fold.
/// </summary>
/// <remarks>
/// A fold keeps its identity across edits through the path of its block, such
/// as <c>/jobs/build/[0]</c>, which is what lets a collapsed block stay
/// collapsed while the text above it changes.
/// </remarks>
public sealed class YamlFoldingProvider : IFoldingProvider
{
    public ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.Snapshot;
        var folds = new List<FoldRange>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var frames = new List<YamlFrame>
        {
            new(int.MinValue, string.Empty, TextRange.Empty(0))
        };

        for (var line = 0; line < snapshot.Lines.LineCount; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = snapshot.Lines.GetLineRange(line);
            var text = snapshot.GetText(range);
            if (!TryGetContentIndent(text, out var indent))
            {
                continue;
            }

            while (frames[^1].Indent >= indent)
            {
                Close(snapshot, frames, folds, occurrences);
            }

            var parent = frames[^1];
            foreach (var frame in frames)
            {
                frame.LastDescendantEnd = range.End;
            }

            frames.Add(new YamlFrame(
                indent,
                $"{parent.Path}/{CreateSegment(text, indent, parent)}",
                range));
        }

        while (frames.Count > 1)
        {
            Close(snapshot, frames, folds, occurrences);
        }

        folds.Sort(static (left, right) => left.Range.Start.CompareTo(right.Range.Start));
        return ValueTask.FromResult<IReadOnlyList<FoldRange>>(folds);
    }

    private static void Close(
        TextSnapshot snapshot,
        List<YamlFrame> frames,
        List<FoldRange> folds,
        Dictionary<string, int> occurrences)
    {
        var frame = frames[^1];
        frames.RemoveAt(frames.Count - 1);
        var start = frame.Header.End;
        if (frame.LastDescendantEnd <= start)
        {
            return;
        }

        occurrences.TryGetValue(frame.Path, out var occurrence);
        occurrences[frame.Path] = occurrence + 1;
        folds.Add(new FoldRange(
            $"yaml-block:{frame.Path}:{occurrence}",
            TextRange.FromBounds(start, EndOfBlock(snapshot, frame.LastDescendantEnd)),
            " …"));
    }

    /// <summary>
    /// Ends the fold after the line break of the block's last line. Leaving
    /// that break visible would draw the emptied line as a blank row under the
    /// collapsed block.
    /// </summary>
    private static int EndOfBlock(TextSnapshot snapshot, int lastDescendantEnd)
    {
        var line = snapshot.Lines.GetLine(lastDescendantEnd);
        return line + 1 < snapshot.Lines.LineCount
            ? snapshot.Lines.GetLineStart(line + 1)
            : snapshot.Length;
    }

    /// <summary>
    /// Names a block after its key, or after its position in the sequence that
    /// holds it. A sequence entry that carries a key on the same line, such as
    /// <c>- name: build</c>, is still named by its position, so inserting a
    /// key into it does not move the fold.
    /// </summary>
    private static string CreateSegment(string text, int indent, YamlFrame parent)
    {
        if (indent < text.Length && text[indent] == '-'
            && (indent + 1 >= text.Length || text[indent + 1] is ' ' or '\t'))
        {
            return $"[{parent.SequenceCount++}]";
        }

        return TryGetKey(text, indent, out var key)
            ? key
            : $"({parent.SequenceCount++})";
    }

    /// <summary>
    /// Reports the indentation of a line that takes part in the structure.
    /// Blank lines and comment-only lines do not.
    /// </summary>
    private static bool TryGetContentIndent(string text, out int indent)
    {
        indent = 0;
        while (indent < text.Length && text[indent] is ' ' or '\t')
        {
            indent++;
        }

        return indent < text.Length && text[indent] != '#';
    }

    private static bool TryGetKey(string text, int indent, out string key)
    {
        key = string.Empty;
        var quote = '\0';
        for (var index = indent; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (character == '#')
            {
                return false;
            }

            if (character != ':')
            {
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] is not ' ' and not '\t')
            {
                continue;
            }

            key = text[indent..index].Trim();
            return key.Length > 0;
        }

        return false;
    }

    private sealed class YamlFrame
    {
        public YamlFrame(int indent, string path, TextRange header)
        {
            Indent = indent;
            Path = path;
            Header = header;
        }

        public int Indent { get; }

        public string Path { get; }

        public TextRange Header { get; }

        public int LastDescendantEnd { get; set; }

        public int SequenceCount { get; set; }
    }
}
