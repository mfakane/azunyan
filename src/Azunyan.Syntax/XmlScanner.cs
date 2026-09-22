using Azunyan.Core;

namespace Azunyan.Syntax;

internal enum XmlTokenKind
{
    StartElement,
    EndElement,
    ProcessingInstruction,
    Doctype,
    Comment,
    Cdata
}

internal readonly record struct XmlAttributeToken(int NameStart, int NameLength, int ValueStart, int ValueLength);

internal sealed class XmlToken
{
    public XmlTokenKind Kind { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public int NameStart { get; init; }
    public int NameLength { get; init; }
    public int OpenEnd { get; init; }
    public int CloseStart { get; init; }
    public bool IsClosed { get; init; }
    public bool IsSelfClosing { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<XmlAttributeToken> Attributes { get; init; } = Array.Empty<XmlAttributeToken>();
}

internal sealed record XmlScanResult(IReadOnlyList<XmlToken> Tokens, bool IsComplete);

internal static class XmlScanner
{
    public static XmlScanResult Scan(string text, CancellationToken cancellationToken)
    {
        var tokens = new List<XmlToken>();
        var isComplete = true;
        var position = 0;
        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text[position] != '<')
            {
                position++;
                continue;
            }

            if (position + 1 >= text.Length)
            {
                isComplete = false;
                break;
            }

            XmlToken? token = text[position + 1] switch
            {
                '!' when text.AsSpan(position).StartsWith("<!--", StringComparison.Ordinal) => ScanComment(text, position),
                '!' when text.AsSpan(position).StartsWith("<![CDATA[", StringComparison.Ordinal) => ScanCdata(text, position),
                '!' when text.AsSpan(position).StartsWith("<!DOCTYPE", StringComparison.Ordinal) => ScanDoctype(text, position),
                '?' => ScanProcessingInstruction(text, position),
                '/' => ScanElement(text, position, true),
                _ => IsNameStart(text[position + 1]) ? ScanElement(text, position, false) : null
            };

            if (token is null)
            {
                isComplete = false;
                position++;
                continue;
            }

            tokens.Add(token);
            isComplete &= token.IsClosed;
            position = Math.Max(position + 1, token.End);
        }

        return new XmlScanResult(tokens, isComplete);
    }

    private static XmlToken ScanComment(string text, int start)
    {
        var end = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
        var closed = end >= 0;
        end = closed ? end + 3 : text.Length;
        return Opaque(XmlTokenKind.Comment, start, end, closed);
    }

    private static XmlToken ScanCdata(string text, int start)
    {
        var end = text.IndexOf("]]>", start + 9, StringComparison.Ordinal);
        var closed = end >= 0;
        end = closed ? end + 3 : text.Length;
        return Opaque(XmlTokenKind.Cdata, start, end, closed);
    }

    private static XmlToken ScanProcessingInstruction(string text, int start)
    {
        var end = text.IndexOf("?>", start + 2, StringComparison.Ordinal);
        var closed = end >= 0;
        end = closed ? end + 2 : text.Length;
        return Opaque(XmlTokenKind.ProcessingInstruction, start, end, closed);
    }

    private static XmlToken ScanDoctype(string text, int start)
    {
        var quote = '\0';
        var subsetDepth = 0;
        var closed = false;
        var position = start + 2;
        while (position < text.Length)
        {
            var character = text[position++];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '[')
            {
                subsetDepth++;
            }
            else if (character == ']' && subsetDepth > 0)
            {
                subsetDepth--;
            }
            else if (character == '>' && subsetDepth == 0)
            {
                closed = true;
                break;
            }
        }

        return new XmlToken
        {
            Kind = XmlTokenKind.Doctype,
            Start = start,
            End = position,
            NameStart = start + 2,
            NameLength = "DOCTYPE".Length,
            IsClosed = closed
        };
    }

    private static XmlToken ScanElement(string text, int start, bool endElement)
    {
        var nameStart = start + (endElement ? 2 : 1);
        var nameEnd = ReadName(text, nameStart);
        if (nameEnd == nameStart)
        {
            return new XmlToken { Kind = XmlTokenKind.EndElement, Start = start, End = start + 1 };
        }

        var attributes = new List<XmlAttributeToken>();
        var position = nameEnd;
        var quote = '\0';
        var closeStart = text.Length;
        var closed = false;
        while (position < text.Length)
        {
            var character = text[position];
            if (character == '<')
            {
                return CreateElementToken(text, start, endElement, nameStart, nameEnd, attributes, closeStart, false, false, position);
            }

            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                position++;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                position++;
                continue;
            }

            if (character == '>')
            {
                closeStart = position;
                closed = true;
                position++;
                break;
            }

            if (!endElement && IsNameStart(character))
            {
                var attributeStart = position;
                var attributeEnd = ReadName(text, position);
                position = attributeEnd;
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                if (position < text.Length && text[position] == '=')
                {
                    position++;
                    while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                    if (position < text.Length && text[position] is '\'' or '"')
                    {
                        var valueQuote = text[position++];
                        var valueStart = position - 1;
                        while (position < text.Length && text[position] != valueQuote)
                        {
                            if (text[position] == '<')
                            {
                                return CreateElementToken(
                                    text,
                                    start,
                                    endElement,
                                    nameStart,
                                    nameEnd,
                                    attributes,
                                    closeStart,
                                    false,
                                    false,
                                    position);
                            }

                            position++;
                        }
                        if (position < text.Length) position++;
                        attributes.Add(new XmlAttributeToken(attributeStart, attributeEnd - attributeStart, valueStart, position - valueStart));
                    }
                }

                continue;
            }

            position++;
        }

        var selfClosing = !endElement && closed && closeStart > start && text[closeStart - 1] == '/';
        return CreateElementToken(text, start, endElement, nameStart, nameEnd, attributes, closeStart, closed, selfClosing, position);
    }

    private static XmlToken CreateElementToken(
        string text,
        int start,
        bool endElement,
        int nameStart,
        int nameEnd,
        List<XmlAttributeToken> attributes,
        int closeStart,
        bool closed,
        bool selfClosing,
        int end) => new()
        {
            Kind = endElement ? XmlTokenKind.EndElement : XmlTokenKind.StartElement,
            Start = start,
            End = end,
            NameStart = nameStart,
            NameLength = nameEnd - nameStart,
            OpenEnd = nameEnd,
            CloseStart = closeStart,
            IsClosed = closed,
            IsSelfClosing = selfClosing,
            Name = text[nameStart..nameEnd],
            Attributes = attributes,
        };

    private static XmlToken Opaque(XmlTokenKind kind, int start, int end, bool closed) =>
        new() { Kind = kind, Start = start, End = end, IsClosed = closed };

    private static int ReadName(string text, int position)
    {
        while (position < text.Length && IsNamePart(text[position])) position++;
        return position;
    }

    private static bool IsNameStart(char character) => character == ':' || character == '_' || char.IsLetter(character);

    private static bool IsNamePart(char character) =>
        IsNameStart(character) || character == '-' || character == '.' || char.IsDigit(character);
}
