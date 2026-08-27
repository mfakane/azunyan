namespace Azunyan.Core;

public enum IndentationKind
{
    Spaces,
    Tabs,
}

public enum IndentationInputMode
{
    Auto,
    Tab,
    Spaces,
}

public readonly record struct IndentationSettings(IndentationKind Kind, int Size)
{
    public string DisplayName => Kind == IndentationKind.Tabs
        ? $"Tab Size: {Size}"
        : $"Spaces: {Size}";
}

/// <summary>
/// Small, UI-independent editing commands shared by text controls. The
/// commands keep the document's selection direction and operate on grapheme
/// cluster boundaries, so an emoji or combining sequence is never split by a
/// normal caret move or deletion.
/// </summary>
public static class TextEditorCommands
{
    private const string DefaultIndentation = "  ";
    private const string DefaultTabIndentation = "\t";
    private const int DefaultTabSize = 4;

    public static void MoveCaretByGrapheme(Document document, int count, bool extendSelection = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (count == 0)
        {
            return;
        }

        var selection = document.Selection;
        var position = extendSelection || selection.IsEmpty
            ? selection.CaretPosition
            : count < 0 ? selection.Start : selection.End;
        var target = UnicodeText.MoveByTextElements(document.Text, position, count);
        if (extendSelection)
        {
            document.SetSelection(selection.Anchor, target);
        }
        else
        {
            document.SetCaret(target);
        }
    }

    public static void MoveCaretByScalar(Document document, int count, bool extendSelection = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (count == 0)
        {
            return;
        }

        var selection = document.Selection;
        var position = extendSelection || selection.IsEmpty
            ? selection.CaretPosition
            : count < 0 ? selection.Start : selection.End;
        var target = UnicodeText.MoveByScalars(document.Text, position, count);
        if (extendSelection)
        {
            document.SetSelection(selection.Anchor, target);
        }
        else
        {
            document.SetCaret(target);
        }
    }

    public static TextChange DeleteBackward(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var range = document.Selection.IsEmpty
            ? UnicodeText.GetBackwardDeleteRange(document.Text, document.CaretPosition)
            : document.Selection.Range;
        return document.Delete(range);
    }

    public static TextChange DeleteForward(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var range = document.Selection.IsEmpty
            ? UnicodeText.GetForwardDeleteRange(document.Text, document.CaretPosition)
            : document.Selection.Range;
        return document.Delete(range);
    }

    public static TextChange DeleteBackwardByScalar(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var range = document.Selection.IsEmpty
            ? UnicodeText.GetBackwardScalarDeleteRange(document.Text, document.CaretPosition)
            : document.Selection.Range;
        return document.Delete(range);
    }

    public static TextChange DeleteForwardByScalar(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var range = document.Selection.IsEmpty
            ? UnicodeText.GetForwardScalarDeleteRange(document.Text, document.CaretPosition)
            : document.Selection.Range;
        return document.Delete(range);
    }

    /// <summary>
    /// Inserts one indentation unit at a caret, or indents/dedents every line
    /// touched by a selection. A selection is expanded to complete lines so
    /// that a multi-line Tab operation behaves like a normal code editor. When
    /// the active line uses spaces, <paramref name="indentSize"/> controls the
    /// indentation unit used by Tab and Shift+Tab. Auto mode uses a literal
    /// tab when no indentation style can be inferred.
    /// </summary>
    public static TextChange IndentSelection(
        Document document,
        bool dedent = false,
        int? indentSize = null,
        IndentationInputMode inputMode = IndentationInputMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (indentSize is { } configuredIndentSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(configuredIndentSize, 1);
        }
        if (!Enum.IsDefined(inputMode))
        {
            throw new ArgumentOutOfRangeException(nameof(inputMode));
        }

        var selection = document.Selection;
        if (selection.IsEmpty)
        {
            var line = document.Snapshot.Lines.GetLine(selection.CaretPosition);
            var lineStart = document.Snapshot.Lines.GetLineStart(line);
            var defaultIndentation = inputMode == IndentationInputMode.Auto
                ? DefaultTabIndentation
                : DefaultIndentation;
            var indentationUnit = GetIndentationUnit(
                document.Snapshot,
                line,
                indentSize,
                inputMode,
                defaultIndentation);
            var spaceIndentationUnitLength = inputMode == IndentationInputMode.Tab
                ? GetIndentationUnit(
                    document.Snapshot,
                    line,
                    indentSize,
                    IndentationInputMode.Spaces).Length
                : 1;
            if (!dedent)
            {
                return document.Insert(selection.CaretPosition, indentationUnit);
            }

            var indentation = GetLineIndentation(document.Snapshot, selection.CaretPosition);
            var removalLength = GetIndentationRemovalLength(
                indentation,
                indentationUnit,
                spaceIndentationUnitLength);
            if (removalLength == 0)
            {
                return document.Replace(TextRange.Empty(selection.CaretPosition), string.Empty);
            }

            var dedentChange = document.Replace(
                TextRange.FromBounds(lineStart, lineStart + removalLength),
                string.Empty);
            document.SetCaret(selection.CaretPosition - Math.Min(
                selection.CaretPosition - lineStart,
                removalLength));
            return dedentChange;
        }

        var snapshot = document.Snapshot;
        var firstLine = snapshot.Lines.GetLine(selection.Start);
        var lastLine = snapshot.Lines.GetLine(selection.End - 1);
        var firstLineStart = snapshot.Lines.GetLineStart(firstLine);
        var lastLineEnd = snapshot.Lines.GetLineEnd(lastLine);
        var defaultIndentationForSelection = inputMode == IndentationInputMode.Auto
            ? DefaultTabIndentation
            : DefaultIndentation;
        var indentationUnitForSelection = GetIndentationUnit(
            snapshot,
            firstLine,
            indentSize,
            inputMode,
            defaultIndentationForSelection);
        var spaceIndentationUnitLengthForSelection = inputMode == IndentationInputMode.Tab
            ? GetIndentationUnit(
                snapshot,
                firstLine,
                indentSize,
                IndentationInputMode.Spaces).Length
            : 1;
        var edits = new List<IndentationEdit>();
        var replacement = new System.Text.StringBuilder();

        for (var line = firstLine; line <= lastLine; line++)
        {
            var lineStart = snapshot.Lines.GetLineStart(line);
            var lineEnd = snapshot.Lines.GetLineEnd(line);
            var lineText = snapshot.Text[lineStart..lineEnd];

            if (dedent)
            {
                var removalLength = GetIndentationRemovalLength(
                    GetLineIndentation(snapshot, lineStart),
                    indentationUnitForSelection,
                    spaceIndentationUnitLengthForSelection);
                if (removalLength > 0)
                {
                    edits.Add(new IndentationEdit(lineStart, -removalLength));
                }

                replacement.Append(lineText[removalLength..]);
            }
            else
            {
                edits.Add(new IndentationEdit(lineStart, indentationUnitForSelection.Length));
                replacement.Append(indentationUnitForSelection);
                replacement.Append(lineText);
            }

            if (line < lastLine)
            {
                var nextLineStart = snapshot.Lines.GetLineStart(line + 1);
                replacement.Append(snapshot.Text[lineEnd..nextLineStart]);
            }
        }

        var range = TextRange.FromBounds(firstLineStart, lastLineEnd);
        var change = document.Replace(range, replacement.ToString());
        document.SetSelection(new TextSelection(
            MapIndentationPosition(selection.Anchor, edits),
            MapIndentationPosition(selection.Active, edits)));
        return change;
    }

    /// <summary>
    /// Inserts a line break at the current selection and carries the current
    /// line's leading spaces and tabs onto the new line. The existing document
    /// line-ending style is preserved when one is present.
    /// </summary>
    public static TextChange InsertNewLineWithAutoIndent(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var selection = document.Selection;
        var replacementRange = GetNewLineReplacementRange(
            document.Snapshot,
            selection.Range);
        return document.Replace(
            replacementRange,
            GetNewLineWithAutoIndentation(document.Snapshot, selection.Start));
    }

    /// <summary>
    /// Returns the range to replace for an auto-indented line break. A
    /// whitespace-only line is normalized to a true empty line before the
    /// new indented line is inserted.
    /// </summary>
    public static TextRange GetNewLineReplacementRange(
        TextSnapshot snapshot,
        TextRange selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!selection.IsEmpty)
        {
            return selection;
        }

        var line = snapshot.Lines.GetLine(selection.Start);
        var lineStart = snapshot.Lines.GetLineStart(line);
        var lineEnd = snapshot.Lines.GetLineEnd(line);
        var indentation = GetLineIndentation(snapshot, selection.Start);
        if (selection.Start == lineEnd
            && lineStart + indentation.Length == lineEnd)
        {
            return TextRange.FromBounds(lineStart, lineEnd);
        }

        return selection;
    }

    /// <summary>
    /// Returns a line break followed by the indentation that should be copied
    /// from the line containing <paramref name="position"/> and adjusted for
    /// the surrounding bracket structure.
    /// </summary>
    public static string GetNewLineWithAutoIndentation(TextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return GetPreferredLineEnding(snapshot.Text)
            + GetAutoIndentation(snapshot, position);
    }

    /// <summary>
    /// If <paramref name="position"/> is at the end of a line's indentation,
    /// returns the range that should be replaced when a closing delimiter is
    /// typed there. This keeps a closing JSON/object or array delimiter at its
    /// parent indentation level.
    /// </summary>
    public static bool TryGetClosingDelimiterDedent(
        TextSnapshot snapshot,
        int position,
        char delimiter,
        out TextRange indentationRange)
    {
        return TryGetClosingDelimiterDedent(
            snapshot,
            position,
            delimiter,
            out indentationRange,
            out _);
    }

    /// <summary>
    /// If a closing delimiter is about to be typed at the end of a line's
    /// indentation, returns both the indentation range and the indentation
    /// that belongs to the matching parent delimiter.
    /// </summary>
    public static bool TryGetClosingDelimiterDedent(
        TextSnapshot snapshot,
        int position,
        char delimiter,
        out TextRange indentationRange,
        out string targetIndentation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        indentationRange = default;
        targetIndentation = string.Empty;

        if (!IsClosingDelimiter(delimiter))
        {
            return false;
        }

        var line = snapshot.Lines.GetLine(position);
        var lineStart = snapshot.Lines.GetLineStart(line);
        var indentation = GetLineIndentation(snapshot, position);
        var indentationEnd = lineStart + indentation.Length;
        if (position != indentationEnd || indentation.Length == 0)
        {
            return false;
        }

        var stack = GetBracketStack(snapshot.Text, position);
        if (stack.Count == 0 || !IsMatchingDelimiter(stack[^1], delimiter))
        {
            return false;
        }

        stack.RemoveAt(stack.Count - 1);
        targetIndentation = Repeat(GetIndentationUnit(snapshot, line), stack.Count);
        if (targetIndentation.Length >= indentation.Length)
        {
            targetIndentation = string.Empty;
            return false;
        }

        indentationRange = TextRange.FromBounds(lineStart, indentationEnd);
        return true;
    }

    /// <summary>
    /// Returns the indentation convention inferred from the document near
    /// <paramref name="position"/>. An explicit <paramref name="indentSize"/>
    /// overrides the inferred width for spaces, while
    /// <paramref name="tabDisplaySize"/> controls the reported width for tabs.
    /// When no indentation style can be inferred, tabs are the default.
    /// </summary>
    public static IndentationSettings GetIndentationSettings(
        TextSnapshot snapshot,
        int position,
        int? indentSize = null,
        int tabDisplaySize = DefaultTabSize,
        IndentationInputMode inputMode = IndentationInputMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (indentSize is { } configuredIndentSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(configuredIndentSize, 1);
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(tabDisplaySize, 1);
        if (!Enum.IsDefined(inputMode))
        {
            throw new ArgumentOutOfRangeException(nameof(inputMode));
        }

        var line = snapshot.Lines.GetLine(position);
        var unit = GetIndentationUnit(
            snapshot,
            line,
            indentSize,
            inputMode,
            inputMode == IndentationInputMode.Auto
                ? DefaultTabIndentation
                : DefaultIndentation);
        return unit.Contains('\t')
            ? new IndentationSettings(
                IndentationKind.Tabs,
                tabDisplaySize)
            : new IndentationSettings(
                IndentationKind.Spaces,
                indentSize ?? Math.Max(unit.Length, 1));
    }

    /// <summary>
    /// Returns the leading spaces and tabs of the line containing
    /// <paramref name="position"/>.
    /// </summary>
    public static string GetLineIndentation(TextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var line = snapshot.Lines.GetLine(position);
        var start = snapshot.Lines.GetLineStart(line);
        var end = snapshot.Lines.GetLineEnd(line);
        var text = snapshot.Text;
        var indentationEnd = start;
        while (indentationEnd < end
            && (text[indentationEnd] == ' ' || text[indentationEnd] == '\t'))
        {
            indentationEnd++;
        }

        return text[start..indentationEnd];
    }

    private static string GetPreferredLineEnding(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '\r':
                    return index + 1 < text.Length && text[index + 1] == '\n'
                        ? "\r\n"
                        : "\r";
                case '\n':
                    return "\n";
            }
        }

        return Environment.NewLine;
    }

    private static string GetAutoIndentation(TextSnapshot snapshot, int position)
    {
        var line = snapshot.Lines.GetLine(position);
        var existingIndentation = GetLineIndentation(snapshot, position);

        var lineStart = snapshot.Lines.GetLineStart(line);
        var lineEnd = snapshot.Lines.GetLineEnd(line);
        var firstCodeCharacter = FindFirstCodeCharacter(
            snapshot.Text,
            lineStart + existingIndentation.Length,
            Math.Min(position, lineEnd));

        var sourceLine = line;
        if (firstCodeCharacter < 0)
        {
            sourceLine = FindPreviousNonBlankLine(snapshot, line);
            if (sourceLine < 0)
            {
                return existingIndentation;
            }
        }

        var sourceLineStart = snapshot.Lines.GetLineStart(sourceLine);
        var sourceLineEnd = snapshot.Lines.GetLineEnd(sourceLine);
        var sourceIndentation = GetLineIndentation(snapshot, sourceLineStart);
        var sourceFirstCodeCharacter = FindFirstCodeCharacter(
            snapshot.Text,
            sourceLineStart + sourceIndentation.Length,
            sourceLineEnd);
        var stackAtSourceEnd = GetBracketStack(snapshot.Text, sourceLineEnd);

        if (sourceFirstCodeCharacter >= 0
            && IsClosingDelimiter(snapshot.Text[sourceFirstCodeCharacter]))
        {
            var structuralIndentation = Repeat(
                GetIndentationUnit(snapshot, sourceLine),
                stackAtSourceEnd.Count);
            return structuralIndentation.Length < sourceIndentation.Length
                ? structuralIndentation
                : sourceIndentation;
        }

        var stackAtSourceStart = GetBracketStack(snapshot.Text, sourceLineStart);
        if (stackAtSourceEnd.Count > stackAtSourceStart.Count)
        {
            return sourceIndentation + GetIndentationUnit(snapshot, sourceLine);
        }

        // A normal line carries its own indentation forward. This preserves
        // an intentional manual dedent instead of restoring the indentation
        // implied by an outer bracket.
        return sourceIndentation;
    }

    private static int FindPreviousNonBlankLine(TextSnapshot snapshot, int line)
    {
        for (var candidate = line - 1; candidate >= 0; candidate--)
        {
            var lineStart = snapshot.Lines.GetLineStart(candidate);
            var lineEnd = snapshot.Lines.GetLineEnd(candidate);
            if (FindFirstCodeCharacter(snapshot.Text, lineStart, lineEnd) >= 0)
            {
                return candidate;
            }
        }

        return -1;
    }

    private static string GetIndentationUnit(
        TextSnapshot snapshot,
        int line,
        int? indentSize = null,
        IndentationInputMode inputMode = IndentationInputMode.Auto,
        string defaultIndentation = DefaultIndentation)
    {
        var currentIndentation = GetLineIndentation(
            snapshot,
            snapshot.Lines.GetLineStart(line));
        if (inputMode == IndentationInputMode.Tab
            || (inputMode == IndentationInputMode.Auto
                && currentIndentation.Contains('\t')))
        {
            return "\t";
        }

        if (indentSize is { } configuredIndentSize)
        {
            return new string(' ', configuredIndentSize);
        }

        var greatestCommonDivisor = 0;
        for (var candidateLine = 0; candidateLine <= line; candidateLine++)
        {
            var indentation = GetLineIndentation(
                snapshot,
                snapshot.Lines.GetLineStart(candidateLine));
            if (indentation.Contains('\t'))
            {
                continue;
            }

            var width = indentation.Length;
            if (width == 0)
            {
                continue;
            }

            greatestCommonDivisor = greatestCommonDivisor == 0
                ? width
                : GreatestCommonDivisor(greatestCommonDivisor, width);
        }

        return greatestCommonDivisor == 0
            ? defaultIndentation
            : new string(' ', greatestCommonDivisor);
    }

    private static int GetIndentationRemovalLength(
        string indentation,
        string indentationUnit,
        int spaceIndentationUnitLength)
    {
        if (indentation.Length == 0)
        {
            return 0;
        }

        if (indentationUnit == "\t")
        {
            if (indentation[0] == '\t')
            {
                return 1;
            }

            var leadingSpaces = 0;
            while (leadingSpaces < indentation.Length
                && indentation[leadingSpaces] == ' ')
            {
                leadingSpaces++;
            }

            return leadingSpaces == 0
                ? 0
                : Math.Min(leadingSpaces, Math.Max(spaceIndentationUnitLength, 1));
        }

        var spaces = 0;
        while (spaces < indentation.Length && indentation[spaces] == ' ')
        {
            spaces++;
        }

        return spaces > 0
            ? Math.Min(spaces, indentationUnit.Length)
            : indentation[0] == '\t' ? 1 : 0;
    }

    private static int MapIndentationPosition(
        int position,
        IReadOnlyList<IndentationEdit> edits)
    {
        var mapped = position;
        foreach (var edit in edits)
        {
            if (edit.Delta > 0)
            {
                if (position >= edit.Position)
                {
                    mapped += edit.Delta;
                }

                continue;
            }

            if (position > edit.Position)
            {
                mapped -= Math.Min(position - edit.Position, -edit.Delta);
            }
        }

        return mapped;
    }

    private readonly record struct IndentationEdit(int Position, int Delta);

    private static List<char> GetBracketStack(string text, int position)
    {
        var stack = new List<char>();
        var quote = '\0';
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < position; index++)
        {
            var current = text[index];
            var next = index + 1 < position ? text[index + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (quote != '\0')
            {
                if (current == '\\')
                {
                    index++;
                }
                else if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if ((current is '\'' or '"') && quote == '\0')
            {
                quote = current;
                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            if (IsOpeningDelimiter(current))
            {
                stack.Add(current);
            }
            else if (IsClosingDelimiter(current)
                && stack.Count > 0
                && IsMatchingDelimiter(stack[^1], current))
            {
                stack.RemoveAt(stack.Count - 1);
            }
        }

        return stack;
    }

    private static int FindFirstCodeCharacter(string text, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Repeat(string value, int count)
    {
        if (count == 0)
        {
            return string.Empty;
        }

        return string.Concat(Enumerable.Repeat(value, count));
    }

    private static bool IsOpeningDelimiter(char value) => value is '{' or '[' or '(';

    private static bool IsClosingDelimiter(char value) => value is '}' or ']' or ')';

    private static bool IsMatchingDelimiter(char opening, char closing) =>
        (opening, closing) is ('{', '}') or ('[', ']') or ('(', ')');

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }
}
