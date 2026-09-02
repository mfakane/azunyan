using Azunyan.Syntax;

namespace Azunote;

public sealed record ExternalToolMenuState(
    bool IsVisible,
    bool IsEnabled,
    string? DisabledReason = null);


public static class ExternalToolAvailability
{
    public static ExternalToolMenuState Evaluate(
        ExternalToolSettings settings,
        ExternalToolContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        var conditionFailure = CheckConditions(settings.When ?? new(), context);
        var visibility = ParseVisibility(settings.Visibility);
        if (conditionFailure is not null)
        {
            return visibility == ExternalToolVisibility.Always
                ? new ExternalToolMenuState(true, false, conditionFailure)
                : new ExternalToolMenuState(false, false, conditionFailure);
        }

        var definition = settings.ToDefinition();
        ExternalToolContext invocationContext;
        try
        {
            invocationContext = context.WithInput(
                context.GetInput(definition.InputMode));
        }
        catch (InvalidOperationException exception)
        {
            return visibility == ExternalToolVisibility.Always
                ? new ExternalToolMenuState(true, false, exception.Message)
                : new ExternalToolMenuState(false, false, exception.Message);
        }

        var environment = ExternalToolEnvironmentResolver.Resolve(definition, invocationContext);
        var command = invocationContext.Expand(definition.FileName, environment.Values);
        var launchPlan = ExternalToolLaunchResolver.Resolve(
            command,
            definition.CommandMode,
            definition.DefinitionDirectory);
        if (launchPlan is null)
        {
            var reason = $"Command '{command}' was not found.";
            return visibility == ExternalToolVisibility.Always
                ? new ExternalToolMenuState(true, false, reason)
                : new ExternalToolMenuState(false, false, reason);
        }

        return new ExternalToolMenuState(true, true);
    }

    private static string? CheckConditions(
        ExternalToolWhenSettings when,
        ExternalToolContext context)
    {
        var documentPath = context.DocumentFilePath;
        var extensions = when.Extensions ?? [];
        var patterns = when.Patterns ?? [];

        if (extensions.Length > 0
            && (documentPath is null || !extensions.Any(extension =>
                string.Equals(
                    NormalizeExtension(extension),
                    context.DocumentExtension,
                    StringComparison.OrdinalIgnoreCase))))
        {
            return "The current document has an unsupported file extension.";
        }

        if (patterns.Length > 0
            && (documentPath is null
                || SyntaxLanguageDefinition.GetPatternMatchScore(documentPath, patterns) < 0))
        {
            return "The current document does not match the configured file pattern.";
        }

        if (when.Languages is { Length: > 0 }
            && !when.Languages.Contains(context.LanguageId, StringComparer.OrdinalIgnoreCase))
        {
            return "The current language mode is not supported by this tool.";
        }

        var fileCondition = ExternalToolEnumValues.Parse<ExternalToolFileCondition>(
            when.File,
            "when.file");
        if (fileCondition == ExternalToolFileCondition.Backed && documentPath is null)
        {
            return "This tool requires a file-backed document.";
        }

        if (fileCondition == ExternalToolFileCondition.Untitled && documentPath is not null)
        {
            return "This tool is only available for untitled documents.";
        }

        var selectionCondition = ExternalToolEnumValues.Parse<ExternalToolSelectionCondition>(
            when.Selection,
            "when.selection");
        if (selectionCondition == ExternalToolSelectionCondition.Empty
            && context.Selection.Length > 0)
        {
            return "This tool requires an empty selection.";
        }

        if (selectionCondition == ExternalToolSelectionCondition.NonEmpty
            && context.Selection.Length == 0)
        {
            return "This tool requires a selection.";
        }

        var documentCondition = ExternalToolEnumValues.Parse<ExternalToolDocumentCondition>(
            when.Document,
            "when.document");
        if (documentCondition == ExternalToolDocumentCondition.Clean && context.IsDirty)
        {
            return "This tool requires a clean document.";
        }

        if (documentCondition == ExternalToolDocumentCondition.Dirty && !context.IsDirty)
        {
            return "This tool requires unsaved document changes.";
        }

        if (when.Os is { Length: > 0 }
            && !when.Os.Contains(GetOperatingSystemName(), StringComparer.OrdinalIgnoreCase))
        {
            return "This tool is not available on the current operating system.";
        }

        return null;
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        if (extension.StartsWith('*'))
        {
            extension = extension[1..];
        }

        return extension.StartsWith('.') ? extension : $".{extension}";
    }

    private static ExternalToolVisibility ParseVisibility(string? value) =>
        ExternalToolEnumValues.Parse<ExternalToolVisibility>(
            value,
            nameof(ExternalToolVisibility));

    private static string GetOperatingSystemName() =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsMacOS() ? "macos" :
        "unknown";
}
