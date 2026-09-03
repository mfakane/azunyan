using Azunyan.Core;

namespace Azunote;

internal sealed record AzunotePlaceholderDefinition(
    string Name,
    string Detail,
    string Documentation,
    string Example);

internal static class AzunotePlaceholderCatalog
{
    public static IReadOnlyList<AzunotePlaceholderDefinition> BuiltIn { get; } =
    [
        new("userHome", "Placeholder", "Path of the user's home folder.", "${userHome} -> C:\\Users\\user"),
        new("workspaceFolder", "Placeholder", "Path of the workspace folder containing the current document.", "${workspaceFolder} -> C:\\work"),
        new("workspaceFolderBasename", "Placeholder", "Name of the workspace folder without any slashes.", "${workspaceFolderBasename} -> work"),
        new("file", "Placeholder", "Path passed to the external process for the current document.", "${file} -> C:\\work\\notes\\current.azunote"),
        new("fileWorkspaceFolder", "Placeholder", "Workspace folder containing the current document.", "${fileWorkspaceFolder} -> C:\\work"),
        new("relativeFile", "Placeholder", "Current document path relative to the workspace folder.", "${relativeFile} -> notes\\current.azunote"),
        new("relativeFileDirname", "Placeholder", "Current document directory relative to the workspace folder.", "${relativeFileDirname} -> notes"),
        new("fileBasename", "Placeholder", "Name of the execution file.", "${fileBasename} -> current.azunote"),
        new("fileBasenameNoExtension", "Placeholder", "Execution file name without its extension.", "${fileBasenameNoExtension} -> current"),
        new("fileExtname", "Placeholder", "Extension of the execution file.", "${fileExtname} -> .azunote"),
        new("fileDirname", "Placeholder", "Directory containing the execution file.", "${fileDirname} -> C:\\work\\notes"),
        new("fileDirnameBasename", "Placeholder", "Name of the execution file's containing folder.", "${fileDirnameBasename} -> notes"),
        new("cwd", "Placeholder", "The current working directory of the Azunote process.", "${cwd} -> C:\\work"),
        new("lineNumber", "Placeholder", "One-based line number of the caret.", "${lineNumber} -> 42"),
        new("columnNumber", "Placeholder", "One-based column number of the caret.", "${columnNumber} -> 7"),
        new("selectedText", "Placeholder", "The currently selected text.", "${selectedText} -> selected text"),
        new("input", "Placeholder", "The input payload for the current external-tool invocation.", "${input} -> selected text"),
        new("input:1", "Placeholder", "The first capture group for the current regex per invocation.", "${input:1} -> captured text"),
        new("input:groupname", "Placeholder", "A named capture group for the current regex per invocation.", "${input:groupname} -> captured text"),
        new("execPath", "Placeholder", "Path to the running Azunote executable.", "${execPath} -> C:\\Program Files\\Azunote\\Azunote.exe"),
        new("pathSeparator", "Placeholder", "Character used by the operating system to separate path components.", "${pathSeparator} -> <separator>"),
        new("/", "Placeholder", "Shorthand for pathSeparator.", "${/} -> <separator>"),
        new("documentFile", "Placeholder", "Path of the document file on disk, when available.", "${documentFile} -> C:\\work\\notes\\current.azunote"),
        new("documentDirname", "Placeholder", "Directory containing the document file.", "${documentDirname} -> C:\\work\\notes"),
        new("documentName", "Placeholder", "Name of the document file.", "${documentName} -> current.azunote"),
        new("documentBasenameNoExtension", "Placeholder", "Document file name without its extension.", "${documentBasenameNoExtension} -> current"),
        new("documentExtension", "Placeholder", "Extension of the document file.", "${documentExtension} -> .azunote"),
        new("tempFile", "Placeholder", "Temporary execution-file path used when the document is dirty.", "${tempFile} -> C:\\Users\\user\\AppData\\Local\\Temp\\azunote\\current.azunote"),
        new("toolFolder", "Placeholder", "Directory containing the external-tool definition.", "${toolFolder} -> C:\\work\\tools"),
        new("document", "Placeholder", "The complete document text.", "${document} -> # Heading"),
        new("languageId", "Placeholder", "Identifier of the active language mode.", "${languageId} -> azunote.lang"),
        new("encoding", "Placeholder", "Encoding of the current document.", "${encoding} -> Utf8"),
        new("lineEnding", "Placeholder", "Line-ending style of the current document.", "${lineEnding} -> Lf"),
        new("platform", "Placeholder", "Operating-system identifier, such as windows or linux.", "${platform} -> windows"),
        new("architecture", "Placeholder", "Process architecture, such as x64 or arm64.", "${architecture} -> x64"),
        new("selectionStartLine", "Placeholder", "One-based line number where the selection starts.", "${selectionStartLine} -> 10"),
        new("selectionStartColumn", "Placeholder", "One-based column number where the selection starts.", "${selectionStartColumn} -> 3"),
        new("selectionEndLine", "Placeholder", "One-based line number where the selection ends.", "${selectionEndLine} -> 10"),
        new("selectionEndColumn", "Placeholder", "One-based column number where the selection ends.", "${selectionEndColumn} -> 15")
    ];
}

/// <summary>Completes substitution variables in arbitrary text input.</summary>
internal static class ExternalToolPlaceholderCompletionProvider
{
    public static CompletionResult? GetCompletions(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (caretPosition < 0 || caretPosition > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(caretPosition));
        }

        var opening = text[..caretPosition].LastIndexOf("${", StringComparison.Ordinal);
        if (opening < 0)
        {
            return null;
        }

        var prefix = text[(opening + 2)..caretPosition];
        if (prefix.Contains('{') || prefix.Contains('}'))
        {
            return null;
        }

        var preserveClosingBrace = caretPosition < text.Length && text[caretPosition] == '}';
        var items = GetItems(
            prefix,
            preserveClosingBrace,
            includePlaceholderSyntax: false);
        return new CompletionResult(
            TextRange.FromBounds(
                opening + 2,
                caretPosition),
            items);
    }

    internal static CompletionItem[] GetItems(
        string prefix,
        bool preserveClosingBrace,
        bool includePlaceholderSyntax = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = new List<CompletionItem>();
        if (prefix.StartsWith("env:", StringComparison.Ordinal))
        {
            var environmentPrefix = prefix[4..];
            var environmentNames = Environment.GetEnvironmentVariables()
                .Keys
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            foreach (var environmentName in environmentNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!environmentName.StartsWith(environmentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(CreateItem(
                    $"env:{environmentName}",
                    "Environment variable",
                    $"${{env:{environmentName}}} -> <value of {environmentName}>",
                    "Expands to the value of the named OS environment variable.",
                    preserveClosingBrace,
                    includePlaceholderSyntax));
            }

            return items.ToArray();
        }

        foreach (var placeholder in AzunotePlaceholderCatalog.BuiltIn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (placeholder.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(CreateItem(
                    placeholder.Name,
                    placeholder.Detail,
                    placeholder.Example,
                    placeholder.Documentation,
                    preserveClosingBrace,
                    includePlaceholderSyntax));
            }
        }

        return items.ToArray();
    }

    private static CompletionItem CreateItem(
        string name,
        string detail,
        string example,
        string documentation,
        bool preserveClosingBrace,
        bool includePlaceholderSyntax) =>
        new(
            $"${{{name}}}",
            includePlaceholderSyntax
                ? preserveClosingBrace ? name : $"${{{name}}}"
                : preserveClosingBrace ? name : $"{name}}}",
            detail,
            $"{documentation}{Environment.NewLine}{Environment.NewLine}Example: {example}");
}
