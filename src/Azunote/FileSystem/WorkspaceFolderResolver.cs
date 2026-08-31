using System.Text;

namespace Azunote;

internal static class WorkspaceFolderResolver
{
    private const string GitDirectoryName = ".git";
    private const string EditorConfigFileName = ".editorconfig";
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string? FindForFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        for (var current = directory;
             !string.IsNullOrWhiteSpace(current);
             current = GetParentDirectory(current))
        {
            if (Directory.Exists(Path.Combine(current, GitDirectoryName))
                || File.Exists(Path.Combine(current, GitDirectoryName))
                || IsRootEditorConfig(Path.Combine(current, EditorConfigFileName)))
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsRootEditorConfig(string path)
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
