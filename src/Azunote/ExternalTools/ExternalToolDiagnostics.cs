using System.Text.RegularExpressions;
using Azunyan.Core;

namespace Azunote;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Information
}

/// <summary>
/// One problem reported by an external tool. Lines and columns are one based,
/// as tools print them; <see cref="File"/> is null when the line names none.
/// </summary>
public sealed record ExternalToolDiagnostic(
    string? File,
    int Line,
    int? Column,
    int? EndLine,
    int? EndColumn,
    DiagnosticSeverity Severity,
    string Message);

/// <summary>
/// Reads the problems an external tool printed, one per line. Without a
/// pattern of its own, a tool is read in the MSBuild form,
/// <c>file(line,col): error CODE: message</c>, and the GNU form,
/// <c>file:line:col: error: message</c>, that most compilers and linters
/// print.
/// </summary>
public static partial class ExternalToolDiagnostics
{
    private static readonly string[] Groups = ["line", "message"];

    [GeneratedRegex(
        @"^\s*(?<file>.+?)\((?<line>\d+)(?:,(?<column>\d+)(?:,(?<endLine>\d+),(?<endColumn>\d+))?)?\)\s*:\s*(?<severity>fatal error|error|warning|info|message)\b\s*(?<code>[^\s:]+)?\s*:\s*(?<message>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MsBuildPattern();

    [GeneratedRegex(
        @"^\s*(?<file>.+?):(?<line>\d+)(?::(?<column>\d+))?:\s*(?:(?<severity>fatal error|error|warning|warn|info|information|note|hint)\s*:\s*)?(?<message>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GnuPattern();

    /// <summary>
    /// Checks a pattern a tool definition supplies. It must name the
    /// <c>line</c> and <c>message</c> groups; <c>file</c>, <c>column</c>,
    /// <c>endLine</c>, <c>endColumn</c>, and <c>severity</c> are optional.
    /// </summary>
    public static Regex CreatePattern(string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        var missing = Groups.Where(group => regex.GroupNumberFromName(group) < 0).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"The diagnostics pattern needs the named group{(missing.Length > 1 ? "s" : string.Empty)} "
                + string.Join(" and ", missing.Select(group => $"(?<{group}>...)")) + ".");
        }

        return regex;
    }

    public static IReadOnlyList<ExternalToolDiagnostic> Parse(string output, string? pattern = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        var custom = string.IsNullOrWhiteSpace(pattern) ? null : CreatePattern(pattern);
        var diagnostics = new List<ExternalToolDiagnostic>();
        foreach (var line in output.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            var match = custom is not null
                ? custom.Match(text)
                : MsBuildPattern().Match(text) is { Success: true } msBuild
                    ? msBuild
                    : GnuPattern().Match(text);
            if (match.Success && ToDiagnostic(match) is { } diagnostic)
            {
                diagnostics.Add(diagnostic);
            }
        }

        return diagnostics;
    }

    private static ExternalToolDiagnostic? ToDiagnostic(Match match)
    {
        if (Number(match, "line") is not { } line || match.Groups["message"].Value.Trim() is not { Length: > 0 } message)
        {
            return null;
        }

        return new ExternalToolDiagnostic(
            match.Groups["file"] is { Success: true, Value: var file } && file.Trim().Length > 0 ? file.Trim() : null,
            line,
            Number(match, "column"),
            Number(match, "endLine"),
            Number(match, "endColumn"),
            ParseSeverity(match.Groups["severity"].Value),
            message);
    }

    private static int? Number(Match match, string group) =>
        match.Groups[group] is { Success: true } value && int.TryParse(value.Value, out var number) && number > 0
            ? number
            : null;

    // A line that names no severity is an error, as in VS Code problem matchers.
    private static DiagnosticSeverity ParseSeverity(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "warning" or "warn" => DiagnosticSeverity.Warning,
            "info" or "information" or "note" or "hint" or "message" => DiagnosticSeverity.Information,
            _ => DiagnosticSeverity.Error
        };

    /// <summary>
    /// Whether a diagnostic belongs to the document whose text the tool read:
    /// it names no file, names standard input, or names the document or the
    /// temporary copy the tool was given. A relative name matches the end of
    /// either path, whichever folder the tool ran in.
    /// </summary>
    public static bool IsForDocument(string? file, params string?[] documentPaths)
    {
        if (file is null or "-" or "<stdin>" or "stdin")
        {
            return true;
        }

        var normalized = file.Replace('/', '\\');
        foreach (var path in documentPaths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            var full = Path.GetFullPath(path);
            if (Path.IsPathFullyQualified(normalized)
                    ? string.Equals(Path.GetFullPath(normalized), full, StringComparison.OrdinalIgnoreCase)
                    : full.EndsWith("\\" + normalized.TrimStart('.', '\\'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The range a diagnostic marks in <paramref name="text"/>. Without an end
    /// it covers the word at its column, or the line without its indentation
    /// when it names no column. Positions past the text are clamped to it.
    /// </summary>
    public static TextRange GetRange(string text, ExternalToolDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (lineStart, lineEnd) = GetLineBounds(text, diagnostic.Line);
        if (diagnostic.Column is not { } column)
        {
            var contentStart = lineStart;
            while (contentStart < lineEnd && char.IsWhiteSpace(text[contentStart]))
            {
                contentStart++;
            }

            return TextRange.FromBounds(contentStart, lineEnd);
        }

        var start = Math.Min(lineStart + column - 1, lineEnd);
        if (diagnostic.EndLine is { } endLine && diagnostic.EndColumn is { } endColumn)
        {
            var (endLineStart, endLineEnd) = GetLineBounds(text, endLine);
            var end = Math.Min(endLineStart + endColumn - 1, endLineEnd);
            if (end > start)
            {
                return TextRange.FromBounds(start, end);
            }
        }

        var wordEnd = start;
        while (wordEnd < lineEnd && IsWordCharacter(text[wordEnd]))
        {
            wordEnd++;
        }

        if (wordEnd > start)
        {
            return TextRange.FromBounds(start, wordEnd);
        }

        // Not on a word: mark the character there, or the one before it at
        // the end of a line.
        return start < lineEnd
            ? TextRange.FromBounds(start, start + 1)
            : TextRange.FromBounds(Math.Max(lineStart, start - 1), start);
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static (int Start, int End) GetLineBounds(string text, int line)
    {
        var start = 0;
        for (var current = 1; current < line; current++)
        {
            var next = text.IndexOf('\n', start);
            if (next < 0)
            {
                break;
            }

            start = next + 1;
        }

        var end = start;
        while (end < text.Length && text[end] is not ('\r' or '\n'))
        {
            end++;
        }

        return (start, end);
    }
}
