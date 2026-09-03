using Xunit;

namespace Azunote.Tests;

public sealed class ExternalToolCommandSuggestionProviderTests
{
    [Fact]
    public void Suggestions_include_commands_from_path_without_executable_extension()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "prettier.cmd"), string.Empty);
            File.WriteAllText(Path.Combine(root, "prettier.exe"), string.Empty);
            File.WriteAllText(Path.Combine(root, "readme.txt"), string.Empty);

            var provider = new ExternalToolCommandSuggestionProvider(
                path: root,
                pathExtensions: ".CMD;.EXE",
                currentDirectory: root);

            var suggestions = provider.GetSuggestions("pre");

            Assert.Equal(["prettier"], suggestions);
            Assert.DoesNotContain("readme", suggestions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Suggestions_include_matching_executables_as_absolute_paths()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var tools = Path.Combine(root, "tools");
            Directory.CreateDirectory(tools);
            var formatter = Path.Combine(tools, "formatter.exe");
            File.WriteAllText(formatter, string.Empty);
            File.WriteAllText(Path.Combine(tools, "formatter.txt"), string.Empty);

            var provider = new ExternalToolCommandSuggestionProvider(
                path: string.Empty,
                pathExtensions: ".EXE",
                currentDirectory: root);

            var suggestions = provider.GetSuggestions(Path.Combine(tools, "form"));

            Assert.Equal([Path.GetFullPath(formatter)], suggestions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Suggestions_include_matching_directories_as_paths_with_a_separator()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var tools = Path.Combine(root, "tools");
            Directory.CreateDirectory(tools);

            var provider = new ExternalToolCommandSuggestionProvider(
                path: string.Empty,
                pathExtensions: ".EXE",
                currentDirectory: root);

            var suggestions = provider.GetSuggestions(Path.Combine(root, "too"));

            Assert.Equal(
                [Path.GetFullPath(tools) + Path.DirectorySeparatorChar],
                suggestions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Suggestions_for_a_drive_root_do_not_use_the_current_directory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var child = Path.Combine(root, "child");
            Directory.CreateDirectory(child);
            var driveRoot = Path.GetPathRoot(root)!;
            var provider = new ExternalToolCommandSuggestionProvider(
                path: string.Empty,
                pathExtensions: ".EXE",
                currentDirectory: root);

            var suggestions = provider.GetSuggestions(driveRoot);

            Assert.DoesNotContain(
                suggestions,
                suggestion => suggestion.StartsWith(
                    Path.GetFullPath(child),
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-command-suggestions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
