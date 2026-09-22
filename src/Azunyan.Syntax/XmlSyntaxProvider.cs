using Azunyan.Core;

namespace Azunyan.Syntax;

public sealed class XmlSyntaxProvider : ISyntaxProvider
{
    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        foreach (var token in XmlScanner.Scan(text, cancellationToken).Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (token.Kind)
            {
                case XmlTokenKind.Comment:
                    Add(spans, token.Start, token.End, "comment");
                    break;
                case XmlTokenKind.Cdata:
                    Add(spans, token.Start, token.End, "code");
                    break;
                case XmlTokenKind.ProcessingInstruction:
                    Add(spans, token.Start, token.End, "keyword");
                    break;
                case XmlTokenKind.Doctype:
                    Add(spans, token.Start, token.End, "keyword");
                    break;
                case XmlTokenKind.StartElement:
                case XmlTokenKind.EndElement:
                    Add(spans, token.Start, token.NameStart, "keyword");
                    Add(spans, token.NameStart, token.NameStart + token.NameLength, "keyword");
                    foreach (var attribute in token.Attributes)
                    {
                        Add(spans, attribute.NameStart, attribute.NameStart + attribute.NameLength, "variable");
                        Add(spans, attribute.ValueStart, attribute.ValueStart + attribute.ValueLength, "string");
                    }

                    if (token.IsClosed)
                    {
                        var closeStart = token.IsSelfClosing ? token.CloseStart - 1 : token.CloseStart;
                        Add(spans, closeStart, token.End, "keyword");
                    }

                    break;
            }
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }

    private static void Add(List<SyntaxSpan> spans, int start, int end, string classification)
    {
        if (end > start)
        {
            spans.Add(new SyntaxSpan(TextRange.FromBounds(start, end), classification));
        }
    }
}
