using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class BuiltInSyntaxLanguagesTests
{
    public static IEnumerable<object[]> RepresentativeSnippets()
    {
        yield return new object[]
        {
            BuiltInSyntaxLanguages.CSharp,
            "public var s = @\"a\"\"b\"; // note\nint n = 42L;",
            new[] { "public:keyword", "var:keyword", "@\"a\"\"b\":string", "// note:comment", "int:keyword", "42L:number" }
        };
        yield return new object[]
        {
            BuiltInSyntaxLanguages.JavaScript,
            "const s = `http://${x}`; /* note */ let n = 0x2a;",
            new[] { "const:keyword", "`http://${x}`:string", "/* note */:comment", "let:keyword", "0x2a:number" }
        };
        yield return new object[]
        {
            BuiltInSyntaxLanguages.TypeScript,
            "interface Item { value: string; }",
            new[] { "interface:keyword", "string:keyword" }
        };
        yield return new object[]
        {
            BuiltInSyntaxLanguages.Python,
            "def f():\n    value = f\"\"\"hello\nworld\"\"\" # note",
            new[] { "def:keyword", "f\"\"\"hello\nworld\"\"\":string", "# note:comment" }
        };
        yield return new object[]
        {
            BuiltInSyntaxLanguages.Json,
            "{\"ok\": true, \"value\": -1.5e2}",
            new[] { "\"ok\":string", "true:keyword", "\"value\":string", "-1.5e2:number" }
        };
        yield return new object[]
        {
            BuiltInSyntaxLanguages.Toml,
            "# note\nname = \"azunote\"\n[tool]\nactive = true\ncount = 42",
            new[] { "# note:comment", "name:keyword", "\"azunote\":string", "[tool]:heading", "active:keyword", "true:keyword", "count:keyword", "42:number" }
        };
        yield return new object[]
        {
            BuiltInSyntaxLanguages.Markdown,
            "# Title\n`code`\n<!-- note -->",
            new[] { "# Title:heading", "`code`:code", "<!-- note -->:comment" }
        };
        yield return new object[]
        {
            BuiltInSyntaxLanguages.PowerShell,
            "function Get-X { $value = \"a`\"b\" # note\n}",
            new[] { "function:keyword", "$value:variable", "\"a`\"b\":string", "# note:comment" }
        };
    }

    [Theory]
    [MemberData(nameof(RepresentativeSnippets))]
    public async Task Built_in_language_classifies_representative_syntax(
        SyntaxLanguageDefinition language,
        string text,
        string[] expected)
    {
        var snapshot = new TextSnapshot(text);

        var spans = await language.GetSyntaxAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        Assert.Equal(
            expected,
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public void Definitions_expose_normalized_extensions()
    {
        Assert.Contains(".cs", BuiltInSyntaxLanguages.CSharp.FileExtensions);
        Assert.Contains(".tsx", BuiltInSyntaxLanguages.TypeScript.FileExtensions);
        Assert.Contains("*.toml", BuiltInSyntaxLanguages.Toml.Patterns);
        Assert.Equal([".", "(", "{", "[", "->"], BuiltInSyntaxLanguages.CSharp.CompletionTriggerCharacters);
        Assert.Equal(8, BuiltInSyntaxLanguages.All.Count);
    }

    [Fact]
    public void Definitions_normalize_completion_triggers_and_keep_multi_character_values()
    {
        var definition = new SyntaxLanguageDefinition(
            "example",
            "Example",
            [],
            [],
            ["->", ".", "->"]);

        Assert.Equal(["->", "."], definition.CompletionTriggerCharacters);
    }

    [Fact]
    public void Path_patterns_prefer_the_most_specific_suffix()
    {
        var definition = new SyntaxLanguageDefinition(
            "example",
            "Example",
            ["*.toml", "modes/*.toml", "settings.toml"],
            []);

        Assert.True(
            definition.GetPatternMatchScore(@"C:\Users\test\settings.toml")
                > definition.GetPatternMatchScore(@"C:\Users\test\modes\custom.toml"));
        Assert.True(
            definition.GetPatternMatchScore(@"C:\Users\test\modes\custom.toml")
                > definition.GetPatternMatchScore(@"C:\Users\test\other.toml"));
    }
}
