using Azunyan.Core;

namespace Azunote;

// Capturing this object does not materialize text or perform filesystem discovery.
internal sealed record ExternalToolEvaluationInput(
    TextSnapshot Snapshot, TextSelection Selection, DocumentSessionState State, string LanguageId,
    IReadOnlyDictionary<ExternalToolSettings, PreparedExternalTool> Tools)
{
    public ExternalToolContext CreateContext(string? toolDirectory)
    {
        var selection = new TextSelection(Math.Clamp(Selection.Anchor, 0, Snapshot.Length),
            Math.Clamp(Selection.Active, 0, Snapshot.Length));
        var lines = Snapshot.Lines;
        var caret = lines.GetLineColumn(selection.CaretPosition);
        return new ExternalToolContext(State.FilePath, State.FilePath, Snapshot.Text,
            Snapshot.GetText(selection.Range), caret.Line + 1, caret.Column + 1,
            LanguageId, toolDirectory, State.Encoding, State.LineEnding, State.IsDirty,
            lines.GetLineColumn(selection.Start), lines.GetLineColumn(selection.End));
    }

    public Dictionary<ExternalToolSettings, ExternalToolMenuState> Evaluate(Func<bool> isCurrent)
    {
        using var measurement = ShellPerformance.Measure("tools.batch");
        var states = new Dictionary<ExternalToolSettings, ExternalToolMenuState>();
        ExternalToolContext? context = null;
        foreach (var (tool, prepared) in Tools)
        {
            if (!isCurrent()) break;
            try
            {
                context ??= CreateContext(null);
                var state = prepared.Evaluate(context with { ToolDirectory = prepared.Definition.DefinitionDirectory });
                states[tool] = State.IsReadOnly
                    ? state with { IsEnabled = false, DisabledReason = "The document is read-only." }
                    : state;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                states[tool] = new(true, false, exception.Message);
            }
        }
        return states;
    }
}
