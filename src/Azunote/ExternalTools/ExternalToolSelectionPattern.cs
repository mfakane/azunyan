using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Azunote;

internal static class ExternalToolSelectionPattern
{
    internal const int MaxLength = 16 * 1024;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);
    private const string Prefix = "regex:";
    private static readonly ConcurrentDictionary<string, Regex> Compiled = new(StringComparer.Ordinal);

    public static bool IsRegex(string? value) =>
        value != null && value.Trim().StartsWith(Prefix, StringComparison.Ordinal);

    public static void Validate(string? value)
    {
        if (!IsRegex(value))
        {
            _ = ExternalToolEnumValues.Parse<ExternalToolSelectionCondition>(value, "when.selection");
            return;
        }

        var pattern = value!.Trim()[Prefix.Length..];
        if (pattern.Length == 0)
        {
            throw new SettingsFileException("when.selection regex pattern must not be empty.");
        }

        try
        {
            _ = Compile(pattern);
        }
        catch (ArgumentException exception)
        {
            throw new SettingsFileException(
                $"Invalid when.selection regex: {exception.Message}",
                exception);
        }
    }

    public static bool IsMatch(string? value, string selection)
    {
        if (!IsRegex(value) || selection.Length > MaxLength)
        {
            return false;
        }

        try
        {
            return Compile(value!.Trim()[Prefix.Length..]).IsMatch(selection);
        }
        catch (Exception exception) when (
            exception is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex Compile(string pattern) =>
        Compiled.GetOrAdd(
            pattern,
            static pattern => new Regex(
                pattern,
                RegexOptions.CultureInvariant,
                MatchTimeout));
}
