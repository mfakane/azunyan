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

public static class ExternalToolOutputInterpreter
{
    public static IReadOnlyList<CompletionItem> CreateCompletionItems(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Regex.Split(text, "\\r\\n|\\r|\\n")
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new CompletionItem(line))
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
}
