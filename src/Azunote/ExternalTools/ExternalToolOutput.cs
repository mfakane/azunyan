using System.Text.RegularExpressions;
using Azunyan.Core;

namespace Azunote;

public enum ExternalToolOutputChannel
{
    Mixed,
    Stdout,
    Stderr
}

public sealed record ExternalToolOutputAction(
    ExternalToolOutputChannel Stream,
    ExternalToolOutputMode Mode,
    string Text);

public sealed record ExternalToolOutput(
    IReadOnlyList<ExternalToolOutputAction> Actions)
{
    public bool IsEmpty => Actions.Count == 0;
}

public static partial class ExternalToolOutputInterpreter
{
    public static IReadOnlyList<CompletionItem> CreateCompletionItems(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // split by "  " to get label and documentation
        // empty and whitespace lines are ignored
        return LineEndingRegex().Split(text)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim().Split(["  "], 2, StringSplitOptions.TrimEntries))
            .Select(line => new CompletionItem(line[0], documentation: line.ElementAtOrDefault(1)))
            .ToArray();
    }

    public static ExternalToolOutput Interpret(
        ExternalToolDefinition definition,
        ExternalToolResult result)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(result);

        if (result.InvocationCount == 0)
        {
            return new ExternalToolOutput([]);
        }

        var actions = new[]
        {
            new ExternalToolOutputAction(
                ExternalToolOutputChannel.Mixed,
                definition.Output.Select(result.Succeeded),
                result.MixedOutput),
            new ExternalToolOutputAction(
                ExternalToolOutputChannel.Stdout,
                definition.Stdout.Select(result.Succeeded),
                result.StandardOutput),
            new ExternalToolOutputAction(
                ExternalToolOutputChannel.Stderr,
                definition.Stderr.Select(result.Succeeded),
                result.StandardError)
        };

        return new ExternalToolOutput(
            actions
                .Where(action => action.Mode != ExternalToolOutputMode.Ignore)
                .ToArray());
    }

    [GeneratedRegex("\\r\\n|\\r|\\n")]
    private static partial Regex LineEndingRegex();
}
