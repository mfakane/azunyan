using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Classifies the destination of a Markdown inline link or image as a link,
/// including the relative ones a URL scan cannot recognize on its own:
/// <c>[note](./other.md)</c>, <c>[up](../index.md)</c>, and <c>[a](#section)</c>.
/// The label keeps whatever classification surrounds it; only the destination
/// inside the parentheses is reported, without the angle brackets of the
/// <c>&lt;...&gt;</c> form and without a following title.
/// An inline link never spans a line, so an edit only rescans the lines it
/// touched.
/// </summary>
public sealed class MarkdownLinkSyntaxRule : IIncrementalSyntaxProvider
{
    public static string Classification => SyntaxClassifications.Link;

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        GetSyntaxAsync(context, window: null, cancellationToken);

    public async ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var spans = await GetSyntaxAsync(context, window: null, cancellationToken)
            .ConfigureAwait(false);
        return new SyntaxAnalysis(spans);
    }

    public async ValueTask<SyntaxAnalysis> GetSyntaxAsync(
        EditorProviderContext context,
        TextSnapshot previousSnapshot,
        TextChange change,
        SyntaxAnalysis previousAnalysis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(previousSnapshot);
        ArgumentNullException.ThrowIfNull(previousAnalysis);
        if (change.OldRange.End > previousSnapshot.Length
            || change.NewRange.End > context.Snapshot.Length)
        {
            return await GetSyntaxAnalysisAsync(context, cancellationToken).ConfigureAwait(false);
        }

        var previousWindow = IncrementalSyntax.ExpandToLine(
            previousSnapshot,
            change.OldRange);
        var currentWindow = IncrementalSyntax.ExpandToLine(
            context.Snapshot,
            IncrementalSyntax.MapAfterChange(
                previousWindow,
                change,
                context.Snapshot.Length));
        var spans = await GetSyntaxAsync(context, currentWindow, cancellationToken)
            .ConfigureAwait(false);
        return IncrementalSyntax.Merge(
            previousAnalysis,
            change,
            previousWindow,
            currentWindow,
            spans);
    }

    private static ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        TextRange? window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var scanRange = window ?? TextRange.FromBounds(0, text.Length);
        var spans = new List<SyntaxSpan>();
        var position = scanRange.Start;
        while (position < scanRange.End)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = text.IndexOf("](", position, StringComparison.Ordinal);
            if (label < 0 || label >= scanRange.End)
            {
                break;
            }

            position = label + 2;
            if (IsEscaped(text, label)
                || !HasLabelStart(text, label, scanRange.Start)
                || !TryReadDestination(text, label + 1, scanRange.End, out var destination, out var end))
            {
                continue;
            }

            spans.Add(new SyntaxSpan(destination, Classification));
            position = end;
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }

    /// <summary>
    /// Requires an opening bracket for the label on the same line. It keeps a
    /// stray "](" in ordinary prose from being read as a link.
    /// </summary>
    private static bool HasLabelStart(string text, int labelEnd, int limit)
    {
        for (var index = labelEnd - 1; index >= limit; index--)
        {
            if (text[index] is '\r' or '\n')
            {
                return false;
            }

            if (text[index] == '[' && !IsEscaped(text, index))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the destination that follows the opening parenthesis and reports
    /// the position after the closing one. A destination without a closing
    /// parenthesis on the same line is not a link.
    /// </summary>
    private static bool TryReadDestination(
        string text,
        int open,
        int limit,
        out TextRange destination,
        out int end)
    {
        destination = TextRange.Empty(open);
        end = open + 1;
        var position = SkipSpaces(text, open + 1, limit);
        if (position >= limit)
        {
            return false;
        }

        int start;
        int stop;
        if (text[position] == '<')
        {
            start = position + 1;
            stop = start;
            while (stop < limit && text[stop] is not '>' and not '\r' and not '\n')
            {
                stop++;
            }

            if (stop >= limit || text[stop] != '>')
            {
                return false;
            }

            if (!TryReadClosingParenthesis(text, stop + 1, limit, out end))
            {
                return false;
            }
        }
        else
        {
            start = position;
            stop = position;
            var depth = 0;
            while (stop < limit)
            {
                var value = text[stop];
                if (value is '\r' or '\n' or ' ' or '\t')
                {
                    break;
                }

                if (value == '(')
                {
                    depth++;
                }
                else if (value == ')')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }

                stop++;
            }

            if (!TryReadClosingParenthesis(text, stop, limit, out end))
            {
                return false;
            }
        }

        if (stop <= start)
        {
            return false;
        }

        destination = TextRange.FromBounds(start, stop);
        return true;
    }

    /// <summary>
    /// Accepts the optional title that may follow a destination and requires
    /// the closing parenthesis on the same line.
    /// </summary>
    private static bool TryReadClosingParenthesis(
        string text,
        int position,
        int limit,
        out int end)
    {
        end = position;
        position = SkipSpaces(text, position, limit);
        if (position < limit && text[position] is '"' or '\'')
        {
            var quote = text[position];
            position++;
            while (position < limit && text[position] != quote && text[position] is not '\r' and not '\n')
            {
                position++;
            }

            if (position >= limit || text[position] != quote)
            {
                return false;
            }

            position = SkipSpaces(text, position + 1, limit);
        }

        if (position >= limit || text[position] != ')')
        {
            return false;
        }

        end = position + 1;
        return true;
    }

    private static int SkipSpaces(string text, int position, int limit)
    {
        while (position < limit && text[position] is ' ' or '\t')
        {
            position++;
        }

        return position;
    }

    private static bool IsEscaped(string text, int position)
    {
        var backslashes = 0;
        for (var index = position - 1; index >= 0 && text[index] == '\\'; index--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }
}
