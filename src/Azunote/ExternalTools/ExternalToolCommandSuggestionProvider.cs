namespace Azunote;

internal sealed class ExternalToolCommandSuggestionProvider
{
    private const int MaximumSuggestionCount = 100;
    private static readonly string[] FallbackExecutableExtensions =
        [".COM", ".EXE", ".BAT", ".CMD", ".PS1"];

    private readonly string _path;
    private readonly string _currentDirectory;
    private readonly HashSet<string> _executableExtensions;
    private IReadOnlyList<string>? _pathCommands;

    public ExternalToolCommandSuggestionProvider(
        string? path = null,
        string? pathExtensions = null,
        string? currentDirectory = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var executableExtensions = pathExtensions
            ?? Environment.GetEnvironmentVariable("PATHEXT")
            ?? string.Join(Path.PathSeparator, FallbackExecutableExtensions);
        _currentDirectory = currentDirectory ?? Environment.CurrentDirectory;
        _executableExtensions = GetExecutableExtensions(executableExtensions);
    }

    public IReadOnlyList<string> GetSuggestions(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var trimmedQuery = query.Trim();
        var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddExistingPathSuggestion(trimmedQuery, suggestions);
        if (LooksLikePath(trimmedQuery))
        {
            AddPathSuggestions(trimmedQuery, suggestions);
        }
        else
        {
            AddPathCommandSuggestions(trimmedQuery, suggestions);
        }

        return suggestions
            .OrderBy(suggestion => suggestion, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSuggestionCount)
            .ToArray();
    }

    private void AddExistingPathSuggestion(
        string query,
        HashSet<string> suggestions)
    {
        try
        {
            if (File.Exists(query) && IsExecutablePath(query))
            {
                suggestions.Add(Path.GetFullPath(query));
            }
        }
        catch (ArgumentException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void AddPathSuggestions(
        string query,
        HashSet<string> suggestions)
    {
        string? directory;
        string filePrefix;
        try
        {
            directory = Path.GetDirectoryName(query);
            filePrefix = Path.GetFileName(query);
            if (string.IsNullOrEmpty(directory))
            {
                directory = Path.GetPathRoot(query);
            }

            directory = string.IsNullOrEmpty(directory)
                ? _currentDirectory
                : Path.GetFullPath(directory);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            foreach (var childDirectory in Directory.EnumerateDirectories(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var directoryName = Path.GetFileName(childDirectory);
                if (directoryName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    suggestions.Add(AppendDirectorySeparator(Path.GetFullPath(childDirectory)));
                }

                if (suggestions.Count >= MaximumSuggestionCount)
                {
                    return;
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase)
                    && IsExecutablePath(file))
                {
                    suggestions.Add(Path.GetFullPath(file));
                }

                if (suggestions.Count >= MaximumSuggestionCount)
                {
                    return;
                }
            }
        }
        catch (ArgumentException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void AddPathCommandSuggestions(
        string query,
        HashSet<string> suggestions)
    {
        _pathCommands ??= EnumeratePathCommands();
        foreach (var command in _pathCommands)
        {
            if (command.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add(command);
            }

            if (suggestions.Count >= MaximumSuggestionCount)
            {
                return;
            }
        }
    }

    private string[] EnumeratePathCommands()
    {
        var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawDirectory in _path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = rawDirectory.Trim('"');
            if (directory.Length == 0 || !Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var extension = Path.GetExtension(file);
                    if (_executableExtensions.Contains(extension))
                    {
                        commands.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return commands
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool IsExecutablePath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Path.GetExtension(path).Length > 0;
        }

        return _executableExtensions.Contains(Path.GetExtension(path));
    }

    private static HashSet<string> GetExecutableExtensions(string pathExtensions)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in pathExtensions.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = extension.StartsWith('.') ? extension : $".{extension}";
            extensions.Add(normalized);
        }

        foreach (var extension in FallbackExecutableExtensions)
        {
            extensions.Add(extension);
        }

        return extensions;
    }

    private static string AppendDirectorySeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static bool LooksLikePath(string query) =>
        Path.IsPathRooted(query)
        || query.Contains(Path.DirectorySeparatorChar)
        || query.Contains(Path.AltDirectorySeparatorChar)
        || query.StartsWith('.');
}
