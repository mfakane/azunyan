using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Azunote;

internal static class WorkspaceFolderResolver
{
    private const string GitDirectoryName = ".git";
    private const string EditorConfigFileName = ".editorconfig";
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string? FindForFile(
        string? filePath,
        string? patternExpression = null) => FindForFile(filePath, patternExpression, null);

    internal static string? FindForFile(string? filePath, string? patternExpression, Action<string>? observeDirectory,
        Action? readFailed = null)
    {
        using var measurement = ShellPerformance.Measure("workspace.search");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        var matcher = patternExpression is null
            ? null
            : CreateMatcher(patternExpression);
        if (patternExpression is not null && matcher is null)
        {
            return null;
        }

        for (var current = directory;
             !string.IsNullOrWhiteSpace(current);
             current = GetParentDirectory(current))
        {
            observeDirectory?.Invoke(current);
            if (matcher is not null
                ? HasMatchingFile(matcher, current)
                : IsDefaultWorkspaceFolder(current, readFailed))
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsDefaultWorkspaceFolder(string directory, Action? readFailed) =>
        Directory.Exists(Path.Combine(directory, GitDirectoryName))
            || File.Exists(Path.Combine(directory, GitDirectoryName))
            || IsRootEditorConfig(Path.Combine(directory, EditorConfigFileName), readFailed);

    private static Matcher? CreateMatcher(string patternExpression)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        var hasPattern = false;
        foreach (var pattern in patternExpression.Split(
                     '|',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                matcher.AddInclude(pattern);
                hasPattern = true;
            }
            catch (ArgumentException)
            {
                // Ignore malformed alternatives while preserving any valid
                // alternatives in the same OR expression.
            }
        }

        return hasPattern ? matcher : null;
    }

    private static bool HasMatchingFile(Matcher matcher, string directory)
    {
        try
        {
            return matcher.Execute(
                new DirectoryInfoWrapper(new DirectoryInfo(directory))).HasMatches;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            // Pattern-based workspace discovery is advisory. An inaccessible
            // directory must not prevent external tools from running.
            return false;
        }
    }

    private static bool IsRootEditorConfig(string path, Action? readFailed)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(path, Utf8);
            var inSection = false;
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var rawLine = lineIndex == 0
                    ? lines[lineIndex].TrimStart('\uFEFF')
                    : lines[lineIndex];
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] is '#' or ';')
                {
                    continue;
                }

                if (line[0] == '[' && line[^1] == ']')
                {
                    inSection = true;
                    continue;
                }

                if (inSection)
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = RemoveInlineComment(line[(separator + 1)..].Trim());
                if (string.Equals(key, "root", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or ArgumentException)
        {
            readFailed?.Invoke();
            // Workspace discovery is advisory. An unreadable configuration
            // file must not prevent external tools from running.
        }

        return false;
    }

    private static string RemoveInlineComment(string value)
    {
        for (var index = 1; index < value.Length; index++)
        {
            if (value[index] is '#' or ';' && char.IsWhiteSpace(value[index - 1]))
            {
                return value[..index].TrimEnd();
            }
        }

        return value;
    }

    private static string? GetParentDirectory(string directory)
    {
        var parent = Directory.GetParent(directory)?.FullName;
        return string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)
            ? null
            : parent;
    }
}
