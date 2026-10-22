using System.Text;

namespace Azunote;

/// <summary>
/// Writes the unified diff, as <c>diff -u</c> prints it, between two texts
/// compared line by line.
/// </summary>
public static class UnifiedDiff
{
    private const int Context = 3;

    /// <summary>
    /// Returns the diff from <paramref name="oldText"/> to
    /// <paramref name="newText"/>, or an empty string when their lines are the
    /// same. <paramref name="firstLine"/> is the one-based number the hunk
    /// headers give the first line, so a diff of part of a document numbers its
    /// lines as the document does.
    /// </summary>
    public static string Create(string oldText, string newText, string oldName, string newName, int firstLine = 1)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);
        var edits = Compare(oldLines, newLines);
        if (edits.All(edit => edit.Kind == ' '))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("--- ").Append(oldName).Append('\n');
        builder.Append("+++ ").Append(newName).Append('\n');
        var index = 0;
        while (index < edits.Count)
        {
            var change = edits.FindIndex(index, edit => edit.Kind != ' ');
            if (change < 0)
            {
                break;
            }

            // A hunk takes the changes whose unchanged gap is at most twice the
            // context, and the context lines on either side.
            var start = Math.Max(index, change - Context);
            var end = change;
            for (var next = change; next < edits.Count; next++)
            {
                if (edits[next].Kind != ' ')
                {
                    end = next;
                }
                else if (next - end > Context * 2)
                {
                    break;
                }
            }

            end = Math.Min(edits.Count - 1, end + Context);
            var hunk = edits.GetRange(start, end - start + 1);
            var oldStart = hunk.FirstOrDefault(edit => edit.Kind != '+').OldLine;
            var newStart = hunk.FirstOrDefault(edit => edit.Kind != '-').NewLine;
            var oldCount = hunk.Count(edit => edit.Kind != '+');
            var newCount = hunk.Count(edit => edit.Kind != '-');
            builder.Append("@@ -").Append(Range(oldStart, oldCount, edits, start, '+', firstLine))
                .Append(" +").Append(Range(newStart, newCount, edits, start, '-', firstLine))
                .Append(" @@\n");
            foreach (var edit in hunk)
            {
                builder.Append(edit.Kind).Append(edit.Text).Append('\n');
            }

            index = end + 1;
        }

        return builder.ToString();
    }

    // An empty side is numbered after the line before it, as diff does.
    private static string Range(int start, int count, List<Edit> edits, int hunkStart, char other, int firstLine)
    {
        if (count == 0)
        {
            var before = edits.Take(hunkStart).Count(edit => edit.Kind != other);
            return $"{before + firstLine - 1},0";
        }

        return count == 1 ? $"{start + firstLine}" : $"{start + firstLine},{count}";
    }

    private static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    private readonly record struct Edit(char Kind, string Text, int OldLine, int NewLine);

    /// <summary>
    /// The shortest edit script by Myers' algorithm, as one entry per line:
    /// <c>' '</c> kept, <c>'-'</c> removed, <c>'+'</c> added. The lines the
    /// texts start and end with alike are set aside first, and texts that
    /// differ in more than <see cref="MaxDifferences"/> lines are compared as
    /// one removal and one addition.
    /// </summary>
    private static List<Edit> Compare(string[] oldLines, string[] newLines)
    {
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix
            && suffix < newLines.Length - prefix
            && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)])
        {
            suffix++;
        }

        var edits = new List<Edit>();
        for (var line = 0; line < prefix; line++)
        {
            edits.Add(new Edit(' ', oldLines[line], line, line));
        }

        var oldMiddle = oldLines[prefix..^suffix];
        var newMiddle = newLines[prefix..^suffix];
        foreach (var edit in Myers(oldMiddle, newMiddle) ?? Replace(oldMiddle, newMiddle))
        {
            edits.Add(edit with { OldLine = edit.OldLine + prefix, NewLine = edit.NewLine + prefix });
        }

        for (var line = suffix; line > 0; line--)
        {
            edits.Add(new Edit(' ', oldLines[^line], oldLines.Length - line, newLines.Length - line));
        }

        return edits;
    }

    private const int MaxDifferences = 4000;

    private static IEnumerable<Edit> Replace(string[] oldLines, string[] newLines) =>
        oldLines.Select((text, line) => new Edit('-', text, line, 0))
            .Concat(newLines.Select((text, line) => new Edit('+', text, oldLines.Length, line)));

    private static List<Edit>? Myers(string[] oldLines, string[] newLines)
    {
        var n = oldLines.Length;
        var m = newLines.Length;
        var max = n + m;
        var v = new int[2 * max + 2];
        // trace[d] holds v for diagonals -d..d before step d.
        var trace = new List<int[]>();
        for (var d = 0; d <= Math.Min(max, MaxDifferences); d++)
        {
            trace.Add(v.AsSpan(max - d, 2 * d + 1).ToArray());
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[k - 1 + max] < v[k + 1 + max])
                    ? v[k + 1 + max]
                    : v[k - 1 + max] + 1;
                var y = x - k;
                while (x < n && y < m && oldLines[x] == newLines[y])
                {
                    x++;
                    y++;
                }

                v[k + max] = x;
                if (x >= n && y >= m)
                {
                    return Backtrack(trace, oldLines, newLines, d);
                }
            }
        }

        return null;
    }

    private static List<Edit> Backtrack(List<int[]> trace, string[] oldLines, string[] newLines, int depth)
    {
        var edits = new List<Edit>();
        var x = oldLines.Length;
        var y = newLines.Length;
        for (var d = depth; d >= 0; d--)
        {
            var v = trace[d];
            int At(int k) => v[k + d];
            var k = x - y;
            var previousK = k == -d || (k != d && At(k - 1) < At(k + 1)) ? k + 1 : k - 1;
            var previousX = d == 0 ? 0 : At(previousK);
            var previousY = previousX - previousK;
            while (x > previousX && y > previousY)
            {
                x--;
                y--;
                edits.Add(new Edit(' ', oldLines[x], x, y));
            }

            if (d > 0)
            {
                if (x == previousX)
                {
                    y--;
                    edits.Add(new Edit('+', newLines[y], x, y));
                }
                else
                {
                    x--;
                    edits.Add(new Edit('-', oldLines[x], x, y));
                }
            }
        }

        edits.Reverse();
        return edits;
    }
}
