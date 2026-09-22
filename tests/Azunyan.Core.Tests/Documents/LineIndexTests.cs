using Azunyan.Core;
using System.Reflection;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class LineIndexTests
{
    [Fact]
    public void Line_breaks_remain_correct_when_edits_split_both_sides_of_crlf()
    {
        var document = new Document("left\r\nright");
        document.Replace(new TextRange(4, 1), string.Empty);
        document.Insert(4, "\r");
        document.Insert(5, "\n");

        AssertLinesMatchReference(document.Snapshot);
    }

    [Fact]
    public void Random_edits_match_the_reference_line_parser()
    {
        var random = new Random(0x51A7);
        var document = new Document();
        var expected = string.Empty;
        var insertions = new[] { "a", "b", "\r", "\n", "\r\n", "ab\r", "\na" };

        for (var iteration = 0; iteration < 250; iteration++)
        {
            var start = random.Next(expected.Length + 1);
            var length = random.Next(expected.Length - start + 1);
            var inserted = insertions[random.Next(insertions.Length)];
            document.Replace(new TextRange(start, length), inserted);
            expected = expected.Remove(start, length).Insert(start, inserted);

            Assert.Equal(expected, document.Text);
            AssertLinesMatchReference(document.Snapshot);
        }
    }

    [Fact]
    public void Editing_does_not_materialize_the_document_to_compute_the_caret_column()
    {
        var document = new Document(new string('x', 100_000));
        var oldSnapshot = document.Snapshot;

        document.Insert(50_000, "y");

        var textField = typeof(TextSnapshot).GetField(
            "_text",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Null(textField.GetValue(oldSnapshot));
        Assert.Null(textField.GetValue(document.Snapshot));
    }

    private static void AssertLinesMatchReference(TextSnapshot snapshot)
    {
        var expected = ParseLines(snapshot.Text);
        var actual = snapshot.Lines;

        Assert.Equal(expected.Count, actual.LineCount);
        for (var line = 0; line < expected.Count; line++)
        {
            Assert.Equal(expected[line].Start, actual.GetLineStart(line));
            Assert.Equal(expected[line].End, actual.GetLineEnd(line));
            Assert.Equal(TextRange.FromBounds(expected[line].Start, expected[line].End), actual.GetLineRange(line));
            Assert.Equal(expected[line].End - expected[line].Start, actual.GetLineLength(line));
            Assert.Equal(expected[line].Start, actual.GetPosition(new LineColumn(line, 0)));
            Assert.Equal(expected[line].End, actual.GetPosition(new LineColumn(line, expected[line].End - expected[line].Start)));
        }

        for (var position = 0; position <= snapshot.Length; position++)
        {
            var line = 0;
            while (line + 1 < expected.Count && expected[line + 1].Start <= position)
            {
                line++;
            }

            Assert.Equal(line, actual.GetLine(position));
            Assert.Equal(position - expected[line].Start, actual.GetColumn(position));
            Assert.Equal(new LineColumn(line, position - expected[line].Start), actual.GetLineColumn(position));
        }
    }

    private static List<(int Start, int End)> ParseLines(string text)
    {
        var lines = new List<(int Start, int End)>();
        var start = 0;
        for (var position = 0; position < text.Length; position++)
        {
            if (text[position] == '\r')
            {
                lines.Add((start, position));
                position += position + 1 < text.Length && text[position + 1] == '\n' ? 1 : 0;
                start = position + 1;
            }
            else if (text[position] == '\n')
            {
                lines.Add((start, position));
                start = position + 1;
            }
        }

        lines.Add((start, text.Length));
        return lines;
    }
}
