using System.Text;

namespace Azunyan.Core;

/// <summary>
/// Where a tab stop of an inserted snippet is, relative to the snippet's text.
/// Parent is the index of the placeholder whose default text holds this one,
/// or -1. Number 0 is the final tab stop.
/// </summary>
public readonly record struct SnippetPlaceholder(int Number, TextRange Range, int Parent = -1);

/// <summary>
/// The text of a snippet in the syntax of the Language Server Protocol and of
/// VS Code, with its placeholders in order of appearance, a placeholder before
/// those in its default text.
/// </summary>
public sealed class Snippet
{
    private Snippet(string text, IReadOnlyList<SnippetPlaceholder> placeholders)
    {
        Text = text;
        Placeholders = placeholders;
    }

    public string Text { get; }

    public IReadOnlyList<SnippetPlaceholder> Placeholders { get; }

    /// <summary>
    /// Reads <c>$1</c>, <c>${1}</c>, <c>${1:default}</c>, which can hold
    /// other placeholders, and <c>${1|one,two|}</c>, which inserts its first
    /// choice. A tab stop without a default gets the default of another with
    /// its number. Variables such as <c>$TM_SELECTED_TEXT</c> become their
    /// default text, and transforms are dropped. Anything else is text.
    /// </summary>
    public static Snippet Parse(string snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        var index = 0;
        var nodes = ParseNodes(snippet, ref index, inPlaceholder: false);
        var defaults = new Dictionary<int, List<Node>>();
        CollectDefaults(nodes, defaults);
        var text = new StringBuilder();
        var placeholders = new List<SnippetPlaceholder>();
        Render(nodes, defaults, text, placeholders, parent: -1, depth: 0);
        return new Snippet(text.ToString(), placeholders);
    }

    private static List<Node> ParseNodes(string snippet, ref int index, bool inPlaceholder)
    {
        var nodes = new List<Node>();
        var text = new StringBuilder();
        while (index < snippet.Length)
        {
            var c = snippet[index];
            if (c == '\\' && index + 1 < snippet.Length && snippet[index + 1] is '$' or '}' or '\\')
            {
                text.Append(snippet[index + 1]);
                index += 2;
                continue;
            }

            if (c == '}' && inPlaceholder)
            {
                break;
            }

            if (c == '$' && TryParseDollar(snippet, ref index) is { } node)
            {
                Flush();
                nodes.Add(node);
                continue;
            }

            text.Append(c);
            index++;
        }

        Flush();
        return nodes;

        void Flush()
        {
            if (text.Length > 0)
            {
                nodes.Add(new Node(null, text.ToString(), []));
                text.Clear();
            }
        }
    }

    /// <summary>Reads a tab stop, placeholder, choice or variable at a <c>$</c>; null leaves the <c>$</c> as text.</summary>
    private static Node? TryParseDollar(string snippet, ref int index)
    {
        var start = index;
        index++;
        if (index < snippet.Length && char.IsAsciiDigit(snippet[index]))
        {
            return new Node(ReadNumber(snippet, ref index), null, []);
        }

        if (index < snippet.Length && IsVariableStart(snippet[index]))
        {
            ReadName(snippet, ref index);
            return new Node(null, null, []);
        }

        if (index >= snippet.Length || snippet[index] != '{')
        {
            index = start;
            return null;
        }

        index++;
        int? number = null;
        if (index < snippet.Length && char.IsAsciiDigit(snippet[index]))
        {
            number = ReadNumber(snippet, ref index);
        }
        else if (index < snippet.Length && IsVariableStart(snippet[index]))
        {
            ReadName(snippet, ref index);
        }
        else
        {
            index = start;
            return null;
        }

        if (index >= snippet.Length)
        {
            index = start;
            return null;
        }

        switch (snippet[index])
        {
            case '}':
                index++;
                return new Node(number, null, []);
            case ':':
                index++;
                var children = ParseNodes(snippet, ref index, inPlaceholder: true);
                if (index >= snippet.Length)
                {
                    index = start;
                    return null;
                }

                index++;
                return new Node(number, null, children);
            case '|' when number is not null:
                index++;
                var choice = new StringBuilder();
                var first = true;
                while (index < snippet.Length && snippet[index] != '|')
                {
                    if (snippet[index] == '\\' && index + 1 < snippet.Length && snippet[index + 1] is ',' or '|' or '\\' or '$' or '}')
                    {
                        if (first)
                        {
                            choice.Append(snippet[index + 1]);
                        }

                        index += 2;
                        continue;
                    }

                    if (snippet[index] == ',')
                    {
                        first = false;
                    }
                    else if (first)
                    {
                        choice.Append(snippet[index]);
                    }

                    index++;
                }

                if (index + 1 >= snippet.Length || snippet[index + 1] != '}')
                {
                    index = start;
                    return null;
                }

                index += 2;
                return new Node(number, null, choice.Length == 0 ? [] : [new Node(null, choice.ToString(), [])]);
            case '/':
                // A transform: /regex/format/options}, whose result is not shown.
                // The format can hold ${1:/upcase} and the like.
                var slashes = 0;
                var depth = 0;
                while (index < snippet.Length && !(slashes == 3 && depth == 0 && snippet[index] == '}'))
                {
                    if (snippet[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (snippet[index] == '$' && index + 1 < snippet.Length && snippet[index + 1] == '{')
                    {
                        depth++;
                        index++;
                    }
                    else if (snippet[index] == '}' && depth > 0)
                    {
                        depth--;
                    }
                    else if (snippet[index] == '/' && depth == 0)
                    {
                        slashes++;
                    }

                    index++;
                }

                if (index >= snippet.Length)
                {
                    index = start;
                    return null;
                }

                index++;
                return new Node(number, null, []);
            default:
                index = start;
                return null;
        }
    }

    private static int ReadNumber(string snippet, ref int index)
    {
        var start = index;
        while (index < snippet.Length && char.IsAsciiDigit(snippet[index]))
        {
            index++;
        }

        return int.TryParse(snippet.AsSpan(start, index - start), out var number) ? number : int.MaxValue;
    }

    private static bool IsVariableStart(char c) => c == '_' || char.IsAsciiLetter(c);

    private static void ReadName(string snippet, ref int index)
    {
        while (index < snippet.Length && (snippet[index] == '_' || char.IsAsciiLetterOrDigit(snippet[index])))
        {
            index++;
        }
    }

    private static void CollectDefaults(List<Node> nodes, Dictionary<int, List<Node>> defaults)
    {
        foreach (var node in nodes)
        {
            if (node.Number is { } number && node.Children.Count > 0)
            {
                defaults.TryAdd(number, node.Children);
            }

            CollectDefaults(node.Children, defaults);
        }
    }

    private static void Render(
        List<Node> nodes,
        Dictionary<int, List<Node>> defaults,
        StringBuilder text,
        List<SnippetPlaceholder> placeholders,
        int parent,
        int depth)
    {
        foreach (var node in nodes)
        {
            if (node.Text is { } literal)
            {
                text.Append(literal);
                continue;
            }

            var children = node.Children.Count > 0 || node.Number is not { } shared
                ? node.Children
                : defaults.GetValueOrDefault(shared, []);
            if (node.Number is not { } number)
            {
                // A variable shows its default text.
                Render(children, defaults, text, placeholders, parent, depth + 1);
                continue;
            }

            var start = text.Length;
            var index = placeholders.Count;
            placeholders.Add(default);

            // A default that repeats its own tab stop would never end.
            if (depth < 8)
            {
                Render(children, defaults, text, placeholders, index, depth + 1);
            }

            placeholders[index] = new SnippetPlaceholder(number, TextRange.FromBounds(start, text.Length), parent);
        }
    }

    /// <summary>A run of text, or a tab stop or variable (Number null) with its default.</summary>
    private sealed record Node(int? Number, string? Text, List<Node> Children);
}
