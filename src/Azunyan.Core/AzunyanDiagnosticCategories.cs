namespace Azunyan.Core;

/// <summary>
/// Categories for the editor's opt-in detailed operation log.
/// </summary>
[Flags]
public enum AzunyanDiagnosticCategory
{
    None = 0,
    Render = 1 << 0,
    Clipboard = 1 << 1,
    Key = 1 << 2,
    Input = 1 << 3,
    All = Render | Clipboard | Key | Input
}

/// <summary>
/// Parses the stable category names used by Azunote's debug settings.
/// </summary>
public static class AzunyanDiagnosticCategories
{
    public const string AllName = "all";

    public static IReadOnlyList<string> Names { get; } =
    [
        "render",
        "clipboard",
        "key",
        "input"
    ];

    public static bool TryParse(
        string? name,
        out AzunyanDiagnosticCategory category)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "all":
                category = AzunyanDiagnosticCategory.All;
                return true;
            case "render":
                category = AzunyanDiagnosticCategory.Render;
                return true;
            case "clipboard":
                category = AzunyanDiagnosticCategory.Clipboard;
                return true;
            case "key":
                category = AzunyanDiagnosticCategory.Key;
                return true;
            case "input":
                category = AzunyanDiagnosticCategory.Input;
                return true;
            default:
                category = AzunyanDiagnosticCategory.None;
                return false;
        }
    }

    public static IReadOnlyList<string> ToNames(AzunyanDiagnosticCategory categories)
    {
        if ((categories & AzunyanDiagnosticCategory.All)
            == AzunyanDiagnosticCategory.All)
        {
            return [AllName];
        }

        var names = new List<string>();
        foreach (var name in Names)
        {
            if (TryParse(name, out var category)
                && (categories & category) != 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    public static string GetName(AzunyanDiagnosticCategory category) => category switch
    {
        AzunyanDiagnosticCategory.Render => "render",
        AzunyanDiagnosticCategory.Clipboard => "clipboard",
        AzunyanDiagnosticCategory.Key => "key",
        AzunyanDiagnosticCategory.Input => "input",
        AzunyanDiagnosticCategory.All => AllName,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown diagnostic category.")
    };
}
