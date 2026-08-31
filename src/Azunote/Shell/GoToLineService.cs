using System.Globalization;
using Azunyan.Core;

namespace Azunote;

/// <summary>
/// A one-based line and optional one-based column requested by the Go to Line
/// command.
/// </summary>
public readonly record struct GoToLineTarget
{
    public GoToLineTarget(int line, int? column = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        if (column is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        }

        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int? Column { get; }
}

/// <summary>
/// Parses Go to Line input and resolves it against a document snapshot.
/// </summary>
public static class GoToLineService
{
    public static bool TryParse(string input, out GoToLineTarget target)
    {
        ArgumentNullException.ThrowIfNull(input);

        target = default;
        var value = input.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator < 0)
        {
            return TryParsePositive(value, out var line)
                && TryCreateTarget(line, null, out target);
        }

        if (separator == 0 || separator == value.Length - 1
            || value.IndexOf(':', separator + 1) >= 0)
        {
            return false;
        }

        return TryParsePositive(value[..separator], out var parsedLine)
            && TryParsePositive(value[(separator + 1)..], out var parsedColumn)
            && TryCreateTarget(parsedLine, parsedColumn, out target);
    }

    public static LineColumn Resolve(TextSnapshot snapshot, GoToLineTarget target)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var line = Math.Clamp(
            target.Line - 1,
            0,
            Math.Max(0, snapshot.Lines.LineCount - 1));
        var requestedColumn = (target.Column ?? 1) - 1;
        var column = Math.Clamp(
            requestedColumn,
            0,
            snapshot.Lines.GetLineLength(line));
        return new LineColumn(line, column);
    }

    private static bool TryParsePositive(string value, out int number) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number)
        && number >= 1;

    private static bool TryCreateTarget(
        int line,
        int? column,
        out GoToLineTarget target)
    {
        if (line < 1 || column is <= 0)
        {
            target = default;
            return false;
        }

        target = new GoToLineTarget(line, column);
        return true;
    }
}
