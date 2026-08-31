using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.FileSystem;

public sealed class EditorConfigResolverTests
{
    [Fact]
    public async Task Resolve_merges_ancestor_sections_and_nested_overrides()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, ".editorconfig"),
                "root = true\n"
                + "[*]\n"
                + "end_of_line = lf\n"
                + "charset = utf-8-bom\n"
                + "[*.cs]\n"
                + "indent_style = space\n"
                + "indent_size = 2\n"
                + "[tests/**/*.cs]\n"
                + "tab_width = 6\n"
                + "insert_final_newline = true\n");

            var nested = Path.Combine(root, "tests", "nested");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(nested, ".editorconfig"),
                "[*.cs]\n"
                + "indent_style = tab\n"
                + "trim_trailing_whitespace = true\n");

            var path = Path.Combine(nested, "sample.cs");
            var settings = await new EditorConfigResolver().ResolveAsync(path);

            Assert.Equal(IndentationInputMode.Tab, settings.IndentationInputMode);
            Assert.Equal(2, settings.IndentSize);
            Assert.Equal(6, settings.TabWidth);
            Assert.Equal(LineEndingKind.Lf, settings.LineEnding);
            Assert.Equal(TextEncodingKind.Utf8Bom, settings.Encoding);
            Assert.True(settings.InsertFinalNewline);
            Assert.True(settings.TrimTrailingWhitespace);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Resolve_supports_unset_and_stops_at_root()
    {
        var parent = CreateRoot();
        var root = Path.Combine(parent, "project");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(parent, ".editorconfig"),
                "[*]\nend_of_line = crlf\n");
            await File.WriteAllTextAsync(
                Path.Combine(root, ".editorconfig"),
                "root = true\n"
                + "[*]\nend_of_line = lf\n"
                + "[*.txt]\nindent_size = 8\n"
                + "[notes.txt]\nindent_size = unset\n");

            var settings = await new EditorConfigResolver().ResolveAsync(
                Path.Combine(root, "notes.txt"));

            Assert.Equal(LineEndingKind.Lf, settings.LineEnding);
            Assert.Null(settings.IndentSize);
        }
        finally
        {
            DeleteRoot(parent);
        }
    }

    [Fact]
    public async Task Resolve_ignores_invalid_properties_and_unreadable_values()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, ".editorconfig"),
                "root = true\n"
                + "[*.txt]\n"
                + "indent_style = unknown\n"
                + "indent_size = nope\n"
                + "end_of_line = unknown\n"
                + "charset = unknown\n"
                + "insert_final_newline = maybe\n");

            var settings = await new EditorConfigResolver().ResolveAsync(
                Path.Combine(root, "notes.txt"));

            Assert.Equal(EditorConfigSettings.Empty, settings);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote-editorconfig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
