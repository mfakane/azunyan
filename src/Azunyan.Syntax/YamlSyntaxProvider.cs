using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Highlights YAML's context-sensitive lexical constructs. This is deliberately
/// a tolerant scanner rather than a validating parser because documents are
/// commonly incomplete while they are being edited.
/// </summary>
public sealed class YamlSyntaxProvider : ISyntaxProvider
{
    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scanner = new Scanner(context.Snapshot.Text);
        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(
            scanner.Scan(cancellationToken));
    }

    private sealed class Scanner
    {
        private const int StringPriority = 100;
        private const int CommentPriority = 90;
        private const int DocumentPriority = 85;
        private const int KeyPriority = 80;
        private const int TagPriority = 70;
        private const int ScalarPriority = 50;

        private readonly string _text;
        private readonly List<Candidate> _candidates = [];
        private int _candidateOrder;

        public Scanner(string text)
        {
            _text = text;
        }

        public List<SyntaxSpan> Scan(CancellationToken cancellationToken)
        {
            var lineStart = 0;
            while (lineStart < _text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lineEnd = GetLineEnd(lineStart);
                var nextLineStart = ScanLine(lineStart, lineEnd, cancellationToken);
                lineStart = nextLineStart > lineEnd
                    ? nextLineStart
                    : GetNextLineStart(lineEnd);
            }

            return ResolveCandidates();
        }

        private int ScanLine(
            int lineStart,
            int lineEnd,
            CancellationToken cancellationToken)
        {
            var contentStart = SkipWhitespace(lineStart, lineEnd);
            if (contentStart >= lineEnd)
            {
                return lineEnd;
            }

            if (_text[contentStart] == '%')
            {
                var commentStart = FindCommentStart(contentStart, lineStart, lineEnd);
                var directiveEnd = commentStart >= 0 ? commentStart : lineEnd;
                Add(contentStart, directiveEnd, "heading", DocumentPriority);
                if (commentStart >= 0)
                {
                    Add(commentStart, lineEnd, "comment", CommentPriority);
                }

                return lineEnd;
            }

            var position = contentStart;
            var flowDepth = 0;
            var keyStart = contentStart;
            var canDetectKey = true;

            if (IsDocumentMarker(position, lineEnd, out var markerEnd))
            {
                Add(position, markerEnd, "heading", DocumentPriority);
                position = markerEnd;
                keyStart = position;
                canDetectKey = false;
            }
            else if (IsSequenceIndicator(position, contentStart, lineEnd))
            {
                position++;
                keyStart = position;
            }

            while (position < lineEnd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var character = _text[position];

                if (char.IsWhiteSpace(character))
                {
                    position++;
                    continue;
                }

                if (character == '#' && IsCommentStart(position, lineStart))
                {
                    Add(position, lineEnd, "comment", CommentPriority);
                    break;
                }

                if (character is '"' or '\'')
                {
                    var end = ScanQuotedScalar(position, character);
                    Add(position, end, "string", StringPriority);
                    if (end > lineEnd)
                    {
                        ScanQuotedTail(end, cancellationToken);
                        return GetLineEndAfter(end);
                    }

                    position = end;
                    continue;
                }

                if (TryGetBlockScalarHeader(
                    position,
                    lineStart,
                    lineEnd,
                    flowDepth,
                    out var blockHeaderEnd,
                    out var explicitIndent))
                {
                    Add(position, blockHeaderEnd, "string", StringPriority);
                    var commentStart = FindCommentStart(blockHeaderEnd, lineStart, lineEnd);
                    if (commentStart >= 0)
                    {
                        Add(commentStart, lineEnd, "comment", CommentPriority);
                    }

                    var nextLineStart = GetNextLineStart(lineEnd);
                    return ScanBlockScalar(
                        nextLineStart,
                        GetIndentation(lineStart, lineEnd),
                        explicitIndent,
                        cancellationToken);
                }

                if (character is '{' or '[')
                {
                    flowDepth++;
                    keyStart = position + 1;
                    canDetectKey = character == '{';
                    position++;
                    continue;
                }

                if (character is '}' or ']')
                {
                    flowDepth = Math.Max(0, flowDepth - 1);
                    keyStart = position + 1;
                    canDetectKey = false;
                    position++;
                    continue;
                }

                if (character == ',')
                {
                    keyStart = position + 1;
                    canDetectKey = flowDepth > 0;
                    position++;
                    continue;
                }

                if (character == ':' && IsMappingColon(position, lineEnd))
                {
                    if (canDetectKey)
                    {
                        AddPlainKey(keyStart, position);
                    }

                    canDetectKey = false;
                    keyStart = position + 1;
                    position++;
                    continue;
                }

                if (character is ('&' or '*' or '!')
                    && IsTokenStart(position, lineStart))
                {
                    var end = ScanTagOrAnchor(position, lineEnd);
                    if (end > position + 1)
                    {
                        Add(position, end, "variable", TagPriority);
                        position = end;
                        continue;
                    }
                }

                if (IsSequenceIndicator(position, contentStart, lineEnd))
                {
                    position++;
                    keyStart = position;
                    canDetectKey = true;
                    continue;
                }

                var tokenEnd = ReadPlainToken(position, lineEnd);
                if (tokenEnd <= position)
                {
                    position++;
                    continue;
                }

                var token = _text[position..tokenEnd];
                if (IsYamlKeyword(token))
                {
                    Add(position, tokenEnd, "keyword", ScalarPriority);
                }
                else if (IsYamlNumber(token))
                {
                    Add(position, tokenEnd, "number", ScalarPriority);
                }

                position = tokenEnd;
            }

            return lineEnd;
        }

        private void ScanQuotedTail(int end, CancellationToken cancellationToken)
        {
            if (end >= _text.Length)
            {
                return;
            }

            var lineStart = GetLineStart(end);
            var lineEnd = GetLineEnd(lineStart);
            var position = end;
            while (position < lineEnd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (char.IsWhiteSpace(_text[position]))
                {
                    position++;
                    continue;
                }

                if (_text[position] == '#' && IsCommentStart(position, lineStart))
                {
                    Add(position, lineEnd, "comment", CommentPriority);
                    return;
                }

                if (_text[position] is '"' or '\'')
                {
                    var quotedEnd = ScanQuotedScalar(position, _text[position]);
                    Add(position, quotedEnd, "string", StringPriority);
                    return;
                }

                var tokenEnd = ReadPlainToken(position, lineEnd);
                if (tokenEnd <= position)
                {
                    position++;
                    continue;
                }

                var token = _text[position..tokenEnd];
                if (IsYamlKeyword(token))
                {
                    Add(position, tokenEnd, "keyword", ScalarPriority);
                }
                else if (IsYamlNumber(token))
                {
                    Add(position, tokenEnd, "number", ScalarPriority);
                }

                position = tokenEnd;
            }
        }

        private int ScanBlockScalar(
            int lineStart,
            int parentIndent,
            int explicitIndent,
            CancellationToken cancellationToken)
        {
            if (lineStart >= _text.Length)
            {
                return lineStart;
            }

            var contentIndent = explicitIndent > 0
                ? parentIndent + explicitIndent
                : FindImplicitBlockIndent(lineStart, parentIndent);
            if (contentIndent <= parentIndent)
            {
                return lineStart;
            }

            var contentStart = lineStart;
            var contentEnd = -1;
            var position = lineStart;
            while (position < _text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lineEnd = GetLineEnd(position);
                if (IsBlankLine(position, lineEnd))
                {
                    contentEnd = lineEnd;
                    position = GetNextLineStart(lineEnd);
                    continue;
                }

                if (GetIndentation(position, lineEnd) < contentIndent)
                {
                    break;
                }

                contentEnd = lineEnd;
                position = GetNextLineStart(lineEnd);
            }

            if (contentEnd >= contentStart)
            {
                Add(contentStart, contentEnd, "string", StringPriority);
            }

            return position;
        }

        private int FindImplicitBlockIndent(int lineStart, int parentIndent)
        {
            var position = lineStart;
            while (position < _text.Length)
            {
                var lineEnd = GetLineEnd(position);
                if (!IsBlankLine(position, lineEnd))
                {
                    var indentation = GetIndentation(position, lineEnd);
                    return indentation > parentIndent ? indentation : -1;
                }

                position = GetNextLineStart(lineEnd);
            }

            return -1;
        }

        private void AddPlainKey(int start, int end)
        {
            while (start < end && char.IsWhiteSpace(_text[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(_text[end - 1]))
            {
                end--;
            }

            if (start >= end
                || _text[start] is '"' or '\'' or '&' or '*' or '!' or '?'
                || ContainsQuote(start, end))
            {
                return;
            }

            Add(start, end, "keyword", KeyPriority);
        }

        private int ScanQuotedScalar(int start, char quote)
        {
            var position = start + 1;
            while (position < _text.Length)
            {
                if (quote == '\'' && _text[position] == '\'')
                {
                    if (position + 1 < _text.Length && _text[position + 1] == '\'')
                    {
                        position += 2;
                        continue;
                    }

                    return position + 1;
                }

                if (quote == '"' && _text[position] == '\\')
                {
                    position = Math.Min(_text.Length, position + 2);
                    continue;
                }

                if (_text[position] == quote)
                {
                    return position + 1;
                }

                position++;
            }

            return _text.Length;
        }

        private int ScanTagOrAnchor(int start, int lineEnd)
        {
            var position = start + 1;
            if (_text[start] == '!' && position < lineEnd && _text[position] == '<')
            {
                position++;
                while (position < lineEnd && _text[position] != '>')
                {
                    position++;
                }

                return position < lineEnd ? position + 1 : position;
            }

            while (position < lineEnd
                && !char.IsWhiteSpace(_text[position])
                && _text[position] is not '[' and not ']' and not '{' and not '}' and not ','
                && !(_text[position] == '#' && IsCommentStart(position, start)))
            {
                position++;
            }

            return position;
        }

        private int ReadPlainToken(int start, int lineEnd)
        {
            var position = start;
            while (position < lineEnd)
            {
                var character = _text[position];
                if (char.IsWhiteSpace(character)
                    || character is '"' or '\'' or '{' or '}' or '[' or ']' or ',')
                {
                    break;
                }

                if (character == '#'
                    && IsCommentStart(position, start))
                {
                    break;
                }

                if (character == ':' && IsMappingColon(position, lineEnd))
                {
                    break;
                }

                position++;
            }

            return position;
        }

        private bool TryGetBlockScalarHeader(
            int position,
            int lineStart,
            int lineEnd,
            int flowDepth,
            out int headerEnd,
            out int explicitIndent)
        {
            headerEnd = position;
            explicitIndent = 0;
            if (flowDepth != 0 || _text[position] is not '|' and not '>')
            {
                return false;
            }

            if (position > lineStart
                && !char.IsWhiteSpace(_text[position - 1])
                && _text[position - 1] != ':')
            {
                return false;
            }

            var hasChomping = false;
            var hasIndent = false;
            var suffixPosition = position + 1;
            while (suffixPosition < lineEnd)
            {
                var character = _text[suffixPosition];
                if (character is '+' or '-')
                {
                    if (hasChomping)
                    {
                        return false;
                    }

                    hasChomping = true;
                }
                else if (character is >= '1' and <= '9')
                {
                    if (hasIndent)
                    {
                        return false;
                    }

                    hasIndent = true;
                    explicitIndent = character - '0';
                }
                else
                {
                    break;
                }

                suffixPosition++;
            }

            if (suffixPosition < lineEnd
                && !char.IsWhiteSpace(_text[suffixPosition])
                && !(_text[suffixPosition] == '#'
                    && IsCommentStart(suffixPosition, lineStart)))
            {
                return false;
            }

            headerEnd = suffixPosition;
            return true;
        }

        private bool IsDocumentMarker(int position, int lineEnd, out int markerEnd)
        {
            markerEnd = position;
            if (position + 3 > lineEnd
                || (_text[position..(position + 3)] is not "---" and not "..."))
            {
                return false;
            }

            if (position + 3 < lineEnd
                && !char.IsWhiteSpace(_text[position + 3])
                && _text[position + 3] != '#')
            {
                return false;
            }

            markerEnd = position + 3;
            return true;
        }

        private bool IsSequenceIndicator(int position, int contentStart, int lineEnd) =>
            _text[position] is '-' or '?'
            && (position == contentStart
                || char.IsWhiteSpace(_text[position - 1])
                || _text[position - 1] is '[' or '{' or ',')
            && (position + 1 >= lineEnd || char.IsWhiteSpace(_text[position + 1]));

        private bool IsMappingColon(int position, int lineEnd) =>
            _text[position] == ':'
            && (position + 1 >= lineEnd
                || char.IsWhiteSpace(_text[position + 1])
                || _text[position + 1] is '[' or ']' or '{' or '}' or ',');

        private bool IsTokenStart(int position, int lineStart) =>
            position == lineStart
            || char.IsWhiteSpace(_text[position - 1])
            || _text[position - 1] is '[' or '{' or ',' or '-' or '?';

        private bool IsCommentStart(int position, int lineStart) =>
            position == lineStart
            || (position > lineStart && char.IsWhiteSpace(_text[position - 1]));

        private int FindCommentStart(int start, int lineStart, int lineEnd)
        {
            for (var position = start; position < lineEnd; position++)
            {
                if (_text[position] == '#' && IsCommentStart(position, lineStart))
                {
                    return position;
                }
            }

            return -1;
        }

        private bool ContainsQuote(int start, int end)
        {
            for (var position = start; position < end; position++)
            {
                if (_text[position] is '"' or '\'')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsYamlKeyword(string token) =>
            token.Equals("true", StringComparison.OrdinalIgnoreCase)
            || token.Equals("false", StringComparison.OrdinalIgnoreCase)
            || token.Equals("null", StringComparison.OrdinalIgnoreCase)
            || token == "~";

        private static bool IsYamlNumber(string token)
        {
            if (token.Equals(".nan", StringComparison.OrdinalIgnoreCase)
                || token.Equals(".inf", StringComparison.OrdinalIgnoreCase)
                || token.Equals("+.inf", StringComparison.OrdinalIgnoreCase)
                || token.Equals("-.inf", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalized = token.Replace("_", string.Empty, StringComparison.Ordinal);
            var position = 0;
            if (normalized.StartsWith('+') || normalized.StartsWith('-'))
            {
                position++;
            }

            if (position >= normalized.Length)
            {
                return false;
            }

            if (normalized[position..].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return AllDigits(normalized, position + 2, IsHexDigit);
            }

            if (normalized[position..].StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                return AllDigits(normalized, position + 2, IsOctalDigit);
            }

            if (normalized[position..].StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                return AllDigits(normalized, position + 2, character => character is '0' or '1');
            }

            var digitsBeforeDecimal = 0;
            while (position < normalized.Length && char.IsDigit(normalized[position]))
            {
                digitsBeforeDecimal++;
                position++;
            }

            var digitsAfterDecimal = 0;
            if (position < normalized.Length && normalized[position] == '.')
            {
                position++;
                while (position < normalized.Length && char.IsDigit(normalized[position]))
                {
                    digitsAfterDecimal++;
                    position++;
                }
            }

            if (digitsBeforeDecimal == 0 && digitsAfterDecimal == 0)
            {
                return false;
            }

            if (position < normalized.Length && normalized[position] is 'e' or 'E')
            {
                position++;
                if (position < normalized.Length
                    && normalized[position] is '+' or '-')
                {
                    position++;
                }

                var exponentStart = position;
                while (position < normalized.Length && char.IsDigit(normalized[position]))
                {
                    position++;
                }

                if (position == exponentStart)
                {
                    return false;
                }
            }

            return position == normalized.Length;
        }

        private static bool AllDigits(
            string value,
            int start,
            Func<char, bool> predicate)
        {
            if (start >= value.Length)
            {
                return false;
            }

            for (var position = start; position < value.Length; position++)
            {
                if (!predicate(value[position]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsHexDigit(char value) =>
            char.IsAsciiDigit(value)
            || value is >= 'a' and <= 'f'
            || value is >= 'A' and <= 'F';

        private static bool IsOctalDigit(char value) => value is >= '0' and <= '7';

        private bool IsBlankLine(int lineStart, int lineEnd) =>
            SkipWhitespace(lineStart, lineEnd) >= lineEnd;

        private int GetLineEnd(int lineStart)
        {
            var position = lineStart;
            while (position < _text.Length && _text[position] is not '\r' and not '\n')
            {
                position++;
            }

            return position;
        }

        private int GetNextLineStart(int lineEnd)
        {
            if (lineEnd >= _text.Length)
            {
                return lineEnd;
            }

            return _text[lineEnd] == '\r'
                && lineEnd + 1 < _text.Length
                && _text[lineEnd + 1] == '\n'
                ? lineEnd + 2
                : lineEnd + 1;
        }

        private int GetLineStart(int position)
        {
            var lineStart = Math.Min(position, _text.Length);
            while (lineStart > 0 && _text[lineStart - 1] is not '\r' and not '\n')
            {
                lineStart--;
            }

            return lineStart;
        }

        private int GetLineEndAfter(int position) =>
            GetLineEnd(GetLineStart(position));

        private int SkipWhitespace(int start, int end)
        {
            while (start < end && char.IsWhiteSpace(_text[start]))
            {
                start++;
            }

            return start;
        }

        private int GetIndentation(int lineStart, int lineEnd)
        {
            var position = lineStart;
            while (position < lineEnd && _text[position] == ' ')
            {
                position++;
            }

            return position - lineStart;
        }

        private List<SyntaxSpan> ResolveCandidates()
        {
            var ordered = _candidates
                .OrderBy(candidate => candidate.Start)
                .ThenByDescending(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.Length)
                .ThenBy(candidate => candidate.Order)
                .ToArray();
            var spans = new List<SyntaxSpan>();
            var index = 0;
            var position = 0;
            while (index < ordered.Length)
            {
                while (index < ordered.Length && ordered[index].Start < position)
                {
                    index++;
                }

                if (index >= ordered.Length)
                {
                    break;
                }

                if (ordered[index].Start > position)
                {
                    position = ordered[index].Start;
                }

                var selected = ordered[index++];
                spans.Add(new SyntaxSpan(
                    new TextRange(selected.Start, selected.Length),
                    selected.Classification));
                position = selected.End;
            }

            return spans;
        }

        private void Add(int start, int end, string classification, int priority)
        {
            if (end <= start)
            {
                return;
            }

            _candidates.Add(new Candidate(
                start,
                end - start,
                classification,
                priority,
                _candidateOrder++));
        }

        private readonly record struct Candidate(
            int Start,
            int Length,
            string Classification,
            int Priority,
            int Order)
        {
            public int End => Start + Length;
        }
    }
}
