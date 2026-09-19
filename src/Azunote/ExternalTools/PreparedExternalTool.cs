namespace Azunote;

/// <summary>A private copy of availability inputs, prepared when settings are loaded.</summary>
internal sealed class PreparedExternalTool
{
    private readonly ExternalToolSettings _conditions;
    internal ExternalToolDefinition Definition { get; }

    public PreparedExternalTool(ExternalToolSettings settings)
    {
        var definition = settings.ToDefinition();
        Definition = new ExternalToolDefinition(definition.FileName, [.. definition.Arguments],
            definition.InputMode, settings.Launch.Per, definition.Stdin, definition.Output,
            definition.Stdout, definition.Stderr, definition.WorkingDirectory,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(definition.Environment, StringComparer.OrdinalIgnoreCase)),
            definition.DefinitionDirectory, definition.CommandMode, definition.Stream);
        var when = settings.When;
        _conditions = new ExternalToolSettings
        {
            Visibility = settings.Visibility,
            When = new()
            {
                Extensions = [.. when.Extensions], Patterns = [.. when.Patterns],
                Languages = [.. when.Languages], Os = [.. when.Os],
                File = when.File, Selection = when.Selection, Document = when.Document
            }
        };
    }

    public ExternalToolMenuState Evaluate(ExternalToolContext context, ExternalToolAvailabilityCache? cache = null) =>
        ExternalToolAvailability.Evaluate(_conditions, context, Definition, cache);
}
