using System.Text.RegularExpressions;
using Azunyan.Core;

namespace Azunote;

public sealed partial record ExternalToolContext
{
    private readonly Func<string?, string?, string?> _findWorkspace;
    private const string WorkspaceFolderPatternPrefix = "workspaceFolder:";

    [GeneratedRegex(@"\$\{(?<name>[^{}]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    public ExternalToolContext(
        string? filePath,
        string document,
        string selection,
        int lineNumber = 1,
        int columnNumber = 1)
        : this(
            filePath,
            filePath,
            document,
            selection,
            lineNumber,
            columnNumber,
            selectionStart: new LineColumn(lineNumber - 1, columnNumber - 1),
            selectionEnd: new LineColumn(lineNumber - 1, columnNumber - 1))
    {
    }

    public ExternalToolContext(
        string? documentFilePath,
        string? executionFilePath,
        string document,
        string selection,
        int lineNumber = 1,
        int columnNumber = 1,
        string? languageId = null,
        string? toolDirectory = null,
        TextEncodingKind encoding = TextEncodingKind.Utf8,
        LineEndingKind lineEnding = LineEndingKind.Lf,
        bool isDirty = false,
        LineColumn? selectionStart = null,
        LineColumn? selectionEnd = null,
        Func<string?, string?, string?>? workspaceResolver = null,
        IReadOnlyList<string>? languageExtensions = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columnNumber, 1);

        DocumentFilePath = string.IsNullOrWhiteSpace(documentFilePath)
            ? null
            : Path.GetFullPath(documentFilePath);
        ExecutionFilePath = string.IsNullOrWhiteSpace(executionFilePath)
            ? null
            : Path.GetFullPath(executionFilePath);
        ToolDirectory = string.IsNullOrWhiteSpace(toolDirectory)
            ? null
            : Path.GetFullPath(toolDirectory);
        _findWorkspace = workspaceResolver ?? WorkspaceFolderResolver.FindForFile;
        WorkspaceFolder = _findWorkspace(DocumentFilePath ?? ExecutionFilePath, null);
        Document = document;
        Selection = selection;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        LanguageId = languageId ?? string.Empty;
        LanguageExtensions = languageExtensions ?? [];
        Encoding = encoding;
        LineEnding = lineEnding;
        IsDirty = isDirty;
        SelectionStart = selectionStart ?? new LineColumn(lineNumber - 1, columnNumber - 1);
        SelectionEnd = selectionEnd ?? SelectionStart;
        Cwd = Environment.CurrentDirectory;
        ExecPath = Environment.ProcessPath ?? string.Empty;
        PathSeparator = Path.DirectorySeparatorChar.ToString();
    }

    public string? FilePath => ExecutionFilePath;

    public string? ExecutionFilePath { get; }

    public string? FileName => FilePath is null ? null : Path.GetFileName(FilePath);

    public string? FileBasenameNoExtension => FileName is null ? null : Path.GetFileNameWithoutExtension(FileName);

    public string? FileExtension => FileName is null ? null : Path.GetExtension(FileName);

    public string? FileBasename => FileName;

    public string? FileExtname => FileExtension;

    public string? FileDirname => FilePath is null ? null : Path.GetDirectoryName(FilePath);

    public string? FileDirnameBasename => GetPathBasename(FileDirname);

    public string? DocumentFilePath { get; }

    public string? DocumentDirname => DocumentFilePath is null
        ? null
        : Path.GetDirectoryName(DocumentFilePath);

    public string? DocumentDirnameBasename => DocumentDirname is null
        ? null
        : GetPathBasename(DocumentDirname);

    public string? DocumentFileName => DocumentFilePath is null
        ? null
        : Path.GetFileName(DocumentFilePath);

    public string? DocumentBasenameNoExtension => DocumentFileName is null
        ? null
        : Path.GetFileNameWithoutExtension(DocumentFileName);

    public string? DocumentExtension => DocumentFileName is null
        ? null
        : Path.GetExtension(DocumentFileName);

    public string? TempFile => ExecutionFilePath is null ||
        string.Equals(DocumentFilePath, ExecutionFilePath, StringComparison.OrdinalIgnoreCase)
            ? null
            : ExecutionFilePath;

    public string? ToolDirectory { get; internal init; }

    public string? WorkspaceFolder { get; }

    public string? WorkspaceFolderBasename => GetPathBasename(WorkspaceFolder);

    public string? FileWorkspaceFolder => WorkspaceFolder;

    public string? RelativeFile => GetRelativeWorkspacePath(DocumentFilePath);

    public string? RelativeFileDirname => GetRelativeWorkspacePath(DocumentDirname);

    public string Document { get; }

    public string Selection { get; }

    /// <summary>
    /// The input payload for the current external-tool invocation. This is
    /// populated by <see cref="ExternalToolRunner"/> and changes for each
    /// partition when <see cref="ExternalToolDefinition.Per"/> is enabled.
    /// </summary>
    public string Input { get; init; } = string.Empty;

    /// <summary>
    /// Captured groups for the current regex <c>per</c> invocation. Numeric
    /// group names, including group zero, are included alongside named groups.
    /// </summary>
    public IReadOnlyDictionary<string, string> InputCaptures { get; init; } =
        EmptyInputCaptures;

    public int LineNumber { get; }

    public int ColumnNumber { get; }

    public string LanguageId { get; }

    /// <summary>
    /// The file extensions of the current language mode. An untitled document
    /// has no extension of its own, so `[when].extensions` reads these
    /// instead: choosing the JSON mode in an untitled document is what makes
    /// the tools for `.json` apply to it.
    /// </summary>
    public IReadOnlyList<string> LanguageExtensions { get; }

    public TextEncodingKind Encoding { get; }

    public LineEndingKind LineEnding { get; }

    public bool IsDirty { get; }

    public LineColumn SelectionStart { get; }

    public LineColumn SelectionEnd { get; }

    public string Cwd { get; }

    public string ExecPath { get; }

    public string PathSeparator { get; }

    public static string UserHome
    {
        get
        {
            var specialFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return !string.IsNullOrWhiteSpace(specialFolder)
                ? specialFolder
                : Environment.GetEnvironmentVariable("USERPROFILE")
                    ?? Environment.GetEnvironmentVariable("HOME")
                    ?? string.Empty;
        }
    }

    public string Expand(string value) => Expand(value, environment: null);

    public string Expand(
        string value,
        IReadOnlyDictionary<string, string>? environment)
    {
        ArgumentNullException.ThrowIfNull(value);

        return PlaceholderPattern().Replace(value, match =>
        {
            var name = match.Groups["name"].Value;
            if (name.StartsWith("env:", StringComparison.Ordinal))
            {
                if (environment is not null)
                {
                    return environment.TryGetValue(name[4..], out var environmentValue)
                        ? environmentValue
                        : string.Empty;
                }

                return Environment.GetEnvironmentVariable(name[4..]) ?? string.Empty;
            }

            if (name.StartsWith("input:", StringComparison.Ordinal))
            {
                var captureName = name[6..];
                return InputCaptures.TryGetValue(captureName, out var capture)
                    ? capture
                    : string.Empty;
            }

            if (name.StartsWith(WorkspaceFolderPatternPrefix, StringComparison.Ordinal))
            {
                return _findWorkspace(
                    DocumentFilePath ?? ExecutionFilePath,
                    name[WorkspaceFolderPatternPrefix.Length..]) ?? string.Empty;
            }

            const string workspaceFolderBasenamePatternPrefix = "workspaceFolderBasename:";
            if (name.StartsWith(workspaceFolderBasenamePatternPrefix, StringComparison.Ordinal))
            {
                return GetPathBasename(
                    _findWorkspace(
                        DocumentFilePath ?? ExecutionFilePath,
                        name[workspaceFolderBasenamePatternPrefix.Length..])) ?? string.Empty;
            }

            return name switch
            {
                "file" => FilePath ?? string.Empty,
                "workspaceFolderBasename" => WorkspaceFolderBasename ?? string.Empty,
                "fileWorkspaceFolder" => FileWorkspaceFolder ?? string.Empty,
                "relativeFile" => RelativeFile ?? string.Empty,
                "relativeFileDirname" => RelativeFileDirname ?? string.Empty,
                "fileBasename" => FileBasename ?? string.Empty,
                "fileBasenameNoExtension" => FileBasenameNoExtension ?? string.Empty,
                "fileExtname" => FileExtname ?? string.Empty,
                "fileDirname" => FileDirname ?? string.Empty,
                "fileDirnameBasename" => FileDirnameBasename ?? string.Empty,
                "documentFile" => DocumentFilePath ?? string.Empty,
                "documentDirname" => DocumentDirname ?? string.Empty,
                "documentDirnameBasename" => DocumentDirnameBasename ?? string.Empty,
                "documentName" => DocumentFileName ?? string.Empty,
                "documentBasenameNoExtension" => DocumentBasenameNoExtension ?? string.Empty,
                "documentExtension" => DocumentExtension ?? string.Empty,
                "tempFile" => TempFile ?? string.Empty,
                "toolFolder" => ToolDirectory ?? string.Empty,
                "workspaceFolder" => WorkspaceFolder ?? string.Empty,
                "document" => Document,
                "selectedText" => Selection,
                "input" => Input,
                "userHome" => UserHome,
                "languageId" => LanguageId,
                "encoding" => Encoding.ToString(),
                "lineEnding" => LineEnding.ToString(),
                "platform" => OperatingSystem.IsWindows() ? "windows" :
                    OperatingSystem.IsLinux() ? "linux" :
                    OperatingSystem.IsMacOS() ? "macos" : "unknown",
                "architecture" => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                "cwd" => Cwd,
                "execPath" => ExecPath,
                "pathSeparator" => PathSeparator,
                "/" => PathSeparator,
                "lineNumber" => LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "columnNumber" => ColumnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selectionStartLine" => (SelectionStart.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selectionStartColumn" => (SelectionStart.Column + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selectionEndLine" => (SelectionEnd.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selectionEndColumn" => (SelectionEnd.Column + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => match.Value
            };
        });
    }

    public string[] Expand(string[] arguments) => Expand(arguments, environment: null);

    public string[] Expand(
        string[] arguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return [.. arguments.Select(argument => Expand(argument, environment))];
    }

    public string GetInput(ExternalToolInputMode inputMode) => inputMode switch
    {
        ExternalToolInputMode.None => string.Empty,
        ExternalToolInputMode.FilePath => FilePath
            ?? throw new InvalidOperationException(
                "The external tool requires a file-backed document."),
        ExternalToolInputMode.Document => Document,
        ExternalToolInputMode.Selection => Selection,
        _ => throw new ArgumentOutOfRangeException(nameof(inputMode))
    };

    public ExternalToolContext WithInput(
        string input,
        IReadOnlyDictionary<string, string>? captures = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        return this with
        {
            Input = input,
            InputCaptures = captures ?? EmptyInputCaptures
        };
    }

    private static IReadOnlyDictionary<string, string> EmptyInputCaptures { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private string? GetRelativeWorkspacePath(string? path)
    {
        if (path is null || WorkspaceFolder is null)
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(WorkspaceFolder, path);
        return relativePath == "." ? string.Empty : relativePath;
    }

    private static string? GetPathBasename(string? path)
    {
        if (path is null)
        {
            return null;
        }

        return Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
    }

}
