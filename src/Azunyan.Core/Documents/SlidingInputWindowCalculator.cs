namespace Azunyan.Core;

/// <summary>
/// Calculates the document range exposed to a native text input control.
/// The range is deliberately independent of the document renderer: it is only
/// a UTF-16 window around the active selection and composition.
/// </summary>
public sealed class SlidingInputWindowCalculator
{
    public const int DefaultContextLength = 2048;
    public const int DefaultHysteresisLength = 512;

    public SlidingInputWindowCalculator(
        int beforeContextLength = DefaultContextLength,
        int afterContextLength = DefaultContextLength,
        int hysteresisLength = DefaultHysteresisLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(beforeContextLength);
        ArgumentOutOfRangeException.ThrowIfNegative(afterContextLength);
        ArgumentOutOfRangeException.ThrowIfNegative(hysteresisLength);

        BeforeContextLength = beforeContextLength;
        AfterContextLength = afterContextLength;
        HysteresisLength = hysteresisLength;
    }

    public int BeforeContextLength { get; }

    public int AfterContextLength { get; }

    public int HysteresisLength { get; }

    /// <summary>
    /// Reuses a window that the native control has already kept text-element
    /// aligned while applying <paramref name="change"/>. The supplied range
    /// is the post-change native window; callers must only use this for a
    /// validated event from the generation that produced that window.
    /// </summary>
    public static bool TryReuseAlignedWindow(
        TextRange trustedWindow,
        TextChange change,
        int snapshotLength,
        out TextRange window)
    {
        window = default;
        ArgumentOutOfRangeException.ThrowIfNegative(snapshotLength);
        if (!IsUsable(trustedWindow, snapshotLength))
        {
            return false;
        }

        var oldWindowEnd = trustedWindow.End
            - (change.NewText.Length - change.OldRange.Length);
        if (oldWindowEnd < trustedWindow.Start)
        {
            return false;
        }

        var oldWindow = TextRange.FromBounds(trustedWindow.Start, oldWindowEnd);
        if (!oldWindow.Contains(change.OldRange)
            || !trustedWindow.Contains(change.NewRange)
            || change.OldRange.Start <= oldWindow.Start
            || change.OldRange.End >= oldWindow.End
            || change.NewRange.Start <= trustedWindow.Start
            || change.NewRange.End >= trustedWindow.End)
        {
            return false;
        }

        window = trustedWindow;
        return true;
    }

    /// <summary>
    /// Maps an already aligned pre-change window through an interior edit.
    /// Edits touching either edge require normal text-element alignment.
    /// </summary>
    public static bool TryMapAlignedWindow(
        TextRange alignedWindow,
        TextChange change,
        int snapshotLength,
        out TextRange window)
    {
        window = default;
        ArgumentOutOfRangeException.ThrowIfNegative(snapshotLength);
        if (!alignedWindow.Contains(change.OldRange)
            || change.OldRange.Start <= alignedWindow.Start
            || change.OldRange.End >= alignedWindow.End)
        {
            return false;
        }

        var length = alignedWindow.Length + change.NewText.Length - change.OldRange.Length;
        if (length < 0 || alignedWindow.Start + length > snapshotLength)
        {
            return false;
        }

        window = new TextRange(alignedWindow.Start, length);
        return true;
    }

    /// <summary>
    /// Returns a text-element-aligned window that contains the selection and,
    /// when present, the composition range. A valid existing window is kept
    /// until the required range reaches one of its hysteresis edges.
    /// </summary>
    public TextRange Calculate(
        TextSnapshot snapshot,
        TextSelection selection,
        TextRange? compositionRange = null,
        TextRange? currentWindow = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var selectionRange = Clamp(selection.Range, snapshot.Length);
        var requiredStart = selectionRange.Start;
        var requiredEnd = selectionRange.End;

        if (compositionRange is { } composition)
        {
            var clampedComposition = Clamp(composition, snapshot.Length);
            requiredStart = Math.Min(requiredStart, clampedComposition.Start);
            requiredEnd = Math.Max(requiredEnd, clampedComposition.End);
        }

        if (currentWindow is { } existing
            && IsUsable(existing, snapshot.Length)
            && existing.Contains(TextRange.FromBounds(requiredStart, requiredEnd))
            && IsAwayFromHysteresisEdges(existing, requiredStart, requiredEnd, snapshot.Length))
        {
            return AlignToTextElements(snapshot, existing);
        }

        var start = Math.Max(0, requiredStart - BeforeContextLength);
        var end = Math.Min(snapshot.Length, requiredEnd + AfterContextLength);
        return AlignToTextElements(snapshot, TextRange.FromBounds(start, end));
    }

    private static bool IsUsable(TextRange range, int snapshotLength) =>
        range.Start <= snapshotLength && range.End <= snapshotLength;

    private bool IsAwayFromHysteresisEdges(
        TextRange window,
        int requiredStart,
        int requiredEnd,
        int snapshotLength)
    {
        var awayFromStart = window.Start == 0
            || requiredStart >= Math.Min(window.End, window.Start + HysteresisLength);
        var awayFromEnd = window.End == snapshotLength
            || requiredEnd <= Math.Max(window.Start, window.End - HysteresisLength);
        return awayFromStart && awayFromEnd;
    }

    private static TextRange AlignToTextElements(TextSnapshot snapshot, TextRange range)
    {
        var start = snapshot.IsTextElementBoundary(range.Start)
            ? range.Start
            : snapshot.GetPreviousTextElementPosition(range.Start);
        var end = snapshot.IsTextElementBoundary(range.End)
            ? range.End
            : snapshot.GetNextTextElementPosition(range.End);
        return TextRange.FromBounds(start, end);
    }

    private static TextRange Clamp(TextRange range, int snapshotLength)
    {
        var start = Math.Clamp(range.Start, 0, snapshotLength);
        var end = Math.Clamp(range.End, start, snapshotLength);
        return TextRange.FromBounds(start, end);
    }
}
