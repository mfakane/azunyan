using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Folds a unified diff: each file from its <c>diff</c> line, and each hunk
/// from its <c>@@</c> line, up to the next header of the same kind or above.
/// </summary>
public sealed class DiffFoldingProvider : IFoldingProvider
{
    public ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.Snapshot;
        var folds = new List<FoldRange>();
        (int Line, int End)? file = null;
        (int Line, int End)? hunk = null;
        var fileIndex = -1;
        var hunkIndex = 0;

        void Close(ref (int Line, int End)? header, string id, int end)
        {
            if (header is { } open
                && end > open.End
                && !string.IsNullOrWhiteSpace(snapshot.GetText(TextRange.FromBounds(open.End, end))))
            {
                folds.Add(new FoldRange(id, TextRange.FromBounds(open.End, end), " …"));
            }

            header = null;
        }

        for (var line = 0; line < snapshot.Lines.LineCount; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = snapshot.Lines.GetLineRange(line);
            var text = snapshot.GetText(range);
            if (text.StartsWith("diff ", StringComparison.Ordinal))
            {
                Close(ref hunk, $"diff-hunk:{fileIndex}:{hunkIndex}", range.Start);
                Close(ref file, $"diff-file:{fileIndex}", range.Start);
                fileIndex++;
                hunkIndex = 0;
                file = (line, range.End);
            }
            else if (text.StartsWith("@@", StringComparison.Ordinal))
            {
                Close(ref hunk, $"diff-hunk:{fileIndex}:{hunkIndex}", range.Start);
                hunkIndex++;
                hunk = (line, range.End);
            }
        }

        Close(ref hunk, $"diff-hunk:{fileIndex}:{hunkIndex}", snapshot.Length);
        Close(ref file, $"diff-file:{fileIndex}", snapshot.Length);
        return ValueTask.FromResult<IReadOnlyList<FoldRange>>(folds);
    }
}
