namespace Azunyan.Core;

public static class TextChangeMapper
{
    public static int MapPosition(TextSnapshot oldSnapshot, TextSnapshot newSnapshot, TextChange change, int position, AnchorAffinity affinity = AnchorAffinity.Before)
    {
        ArgumentNullException.ThrowIfNull(oldSnapshot);
        ArgumentNullException.ThrowIfNull(newSnapshot);
        Validate(change, oldSnapshot, newSnapshot);
        if (position < 0 || position > oldSnapshot.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return MapPosition(change, position, affinity);
    }

    public static TextRange MapRange(TextSnapshot oldSnapshot, TextSnapshot newSnapshot, TextChange change, TextRange range)
    {
        ArgumentNullException.ThrowIfNull(oldSnapshot);
        ArgumentNullException.ThrowIfNull(newSnapshot);
        Validate(change, oldSnapshot, newSnapshot);
        if (range.Start > oldSnapshot.Length || range.End > oldSnapshot.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        return MapRange(change, range);
    }

    internal static TextRange MapRange(TextChange change, TextRange range)
    {
        var start = MapPosition(change, range.Start, AnchorAffinity.Before);
        var end = MapPosition(change, range.End, AnchorAffinity.After);
        return TextRange.FromBounds(start, end);
    }

    public static DocumentAnchor MapAnchor(TextSnapshot oldSnapshot, TextSnapshot newSnapshot, TextChange change, DocumentAnchor anchor) =>
        new(new DocumentPosition(MapPosition(oldSnapshot, newSnapshot, change, anchor.Position.Offset, anchor.Affinity)), anchor.Affinity);

    internal static void Validate(TextChange change, TextSnapshot oldSnapshot, TextSnapshot newSnapshot)
    {
        if (change.OldRange.End > oldSnapshot.Length || change.NewRange.End > newSnapshot.Length)
        {
            throw new ArgumentException("The change does not belong to the supplied snapshots.", nameof(change));
        }

        if (!string.Equals(oldSnapshot.GetText(change.OldRange), change.OldText, StringComparison.Ordinal)
            || !string.Equals(newSnapshot.GetText(change.NewRange), change.NewText, StringComparison.Ordinal))
        {
            throw new ArgumentException("The change text does not match the supplied snapshots.", nameof(change));
        }
    }

    private static int MapPosition(
        TextChange change,
        int position,
        AnchorAffinity affinity)
    {
        if (position < change.OldRange.Start)
        {
            return position;
        }

        if (position > change.OldRange.End)
        {
            return checked(position + change.NewText.Length - change.OldRange.Length);
        }

        return affinity == AnchorAffinity.After
            ? change.NewRange.End
            : change.NewRange.Start;
    }
}
