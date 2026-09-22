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
            BuiltInSyntaxLanguages.Xml,
            "<root id=\"1\"><child /></root>",
            new[]
            {
                "<:keyword", "root:keyword", "id:variable", "\"1\":string", ">:keyword",
                "<:keyword", "child:keyword", "/>:keyword",
                "</:keyword", "root:keyword", ">:keyword"
            }
        };
        yield return new object[]
        {
            BuiltInSyntaxLanguages.Yaml,
            "---\n"
            + "name: app\n"
            + "enabled: true\n"
            + "ports:\n"
            + "  - 8080\n"
            + "message: \"hello # yaml\"\n"
            + "nested:\n"
            + "  value: null # note\n"
            + "literal: |\n"
            + "  line # content\n"
            + "  next\n"
            + "...",
            new[]
            {
                "---:heading",
                "name:keyword",
                "enabled:keyword",
                "true:keyword",
                "ports:keyword",
                "8080:number",
                "message:keyword",
                "\"hello # yaml\":string",
                "nested:keyword",
                "value:keyword",
                "null:keyword",
                "# note:comment",
                "literal:keyword",
                "|:string",
                "  line # content\n  next:string",
                "...:heading"
            }
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
    public async Task Json_mode_classifies_jsonc_and_json5_syntax()
    {
        const string text =
            "/* true 0x1 */\n"
            + "// false 0x1\n"
            + "{ unquoted: 'text', true: Infinity, value: -.5, trailing: 1., hex: +0xFF, nan: NaN, 日本語: 2 }";

        var spans = await BuiltInSyntaxLanguages.Json.GetSyntaxAsync(
            new EditorProviderContext(new TextSnapshot(text), 0, TextSelection.Caret(0)));

        Assert.Equal(
            [
                "/* true 0x1 */:comment",
                "// false 0x1:comment",
                "unquoted:property",
                "'text':string",
                "true:property",
                "Infinity:number",
                "value:property",
                "-.5:number",
                "trailing:property",
                "1.:number",
                "hex:property",
                "+0xFF:number",
                "nan:property",
                "NaN:number",
                "日本語:property",
                "2:number"
            ],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public void Definitions_expose_normalized_extensions()
    {
        Assert.Contains(".cs", BuiltInSyntaxLanguages.CSharp.FileExtensions);
        Assert.Contains(".tsx", BuiltInSyntaxLanguages.TypeScript.FileExtensions);
        Assert.Contains(".yaml", BuiltInSyntaxLanguages.Yaml.FileExtensions);
        Assert.Contains(".yml", BuiltInSyntaxLanguages.Yaml.FileExtensions);
        Assert.Contains(".jsonc", BuiltInSyntaxLanguages.Json.FileExtensions);
        Assert.Contains(".json5", BuiltInSyntaxLanguages.Json.FileExtensions);
        Assert.Contains(".xml", BuiltInSyntaxLanguages.Xml.FileExtensions);
        Assert.Equal(["*.xml"], BuiltInSyntaxLanguages.Xml.Patterns);
        Assert.IsType<XmlSyntaxProvider>(Assert.Single(BuiltInSyntaxLanguages.Xml.Sources));
        Assert.IsType<XmlFoldingProvider>(BuiltInSyntaxLanguages.Xml.FoldingProvider);
        Assert.Contains("*.toml", BuiltInSyntaxLanguages.Toml.Patterns);
        Assert.IsType<TomlFoldingProvider>(BuiltInSyntaxLanguages.Toml.FoldingProvider);
        Assert.Equal([".", "(", "{", "[", "->"], BuiltInSyntaxLanguages.CSharp.CompletionTriggerCharacters);
        Assert.Equal(10, BuiltInSyntaxLanguages.All.Count);
    }

    [Fact]
    public async Task Yaml_scanner_handles_flow_collections_tags_anchors_and_comments()
    {
        const string text =
            "defaults: &base {name: 'app''s', enabled: false, count: 0x2a}\n"
            + "service: !!str *base # reference\n"
            + "url: https://example.test/a#b\n";

        var snapshot = new TextSnapshot(text);
        var spans = await BuiltInSyntaxLanguages.Yaml.GetSyntaxAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        Assert.Equal(
            [
                "defaults:keyword",
                "&base:variable",
                "name:keyword",
                "'app''s':string",
                "enabled:keyword",
                "false:keyword",
                "count:keyword",
                "0x2a:number",
                "service:keyword",
                "!!str:variable",
                "*base:variable",
                "# reference:comment",
                "url:keyword"
            ],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Yaml_block_scalar_stops_at_less_indented_lines_and_keeps_hashes_as_text()
    {
        const string text =
            "message: >-2\n"
            + "    first # text\n"
            + "    second\n"
            + "next: true\n";

        var snapshot = new TextSnapshot(text);
        var spans = await BuiltInSyntaxLanguages.Yaml.GetSyntaxAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        Assert.Equal(
            [
                "message:keyword",
                ">-2:string",
                "    first # text\n    second:string",
                "next:keyword",
                "true:keyword"
            ],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Yaml_quoted_scalars_can_cross_lines_without_starting_comments()
    {
        const string text =
            "message: \"first\n"
            + "  second # still text\"\n"
            + "next: 1\n";

        var snapshot = new TextSnapshot(text);
        var spans = await BuiltInSyntaxLanguages.Yaml.GetSyntaxAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        Assert.Equal(
            [
                "message:keyword",
                "\"first\n  second # still text\":string",
                "next:keyword",
                "1:number"
            ],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Toml_folding_provider_folds_tables_and_arrays_of_tables()
    {
        const string text =
            "# document\n"
            + "[owner]\n"
            + "name = \"Tom\"\n"
            + "[database] # connection settings\n"
            + "ports = [8001, 8001]\n"
            + "[[products]]\n"
            + "name = \"Hammer\"\n"
            + "[[products]]\n"
            + "name = \"Nail\"";
        var snapshot = new TextSnapshot(text);
        var provider = Assert.IsType<TomlFoldingProvider>(
            BuiltInSyntaxLanguages.Toml.FoldingProvider);

        var folds = await provider.GetFoldsAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        Assert.Equal(
            [
                "\nname = \"Tom\"\n",
                "\nports = [8001, 8001]\n",
                "\nname = \"Hammer\"\n",
                "\nname = \"Nail\""
            ],
            folds.Select(fold => snapshot.GetText(fold.Range)));
        Assert.Equal(
            [
                "toml-section:table:owner:0",
                "toml-section:table:database:0",
                "toml-section:array:products:0",
                "toml-section:array:products:1"
            ],
            folds.Select(fold => fold.Id));
        Assert.All(folds, fold => Assert.Equal(" …", fold.Placeholder));
    }

    [Fact]
    public async Task Toml_folding_provider_ignores_comments_and_multiline_strings()
    {
        const string text =
            "message = \"\"\"\n"
            + "[not-a-section]\n"
            + "still = \"inside the string\"\n"
            + "\"\"\"\n"
            + "# [also-not-a-section]\n"
            + "[real]\n"
            + "value = true";
        var snapshot = new TextSnapshot(text);
        var provider = new TomlFoldingProvider();

        var folds = await provider.GetFoldsAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        var fold = Assert.Single(folds);
        Assert.Equal("toml-section:table:real:0", fold.Id);
        Assert.Equal("\nvalue = true", snapshot.GetText(fold.Range));
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

    [Fact]
    public void Path_patterns_use_segment_aware_globbing()
    {
        var path = @"C:\Users\test\src\nested\custom.toml";

        Assert.True(
            SyntaxLanguageDefinition.GetPatternMatchScore(path, ["src/**/*.toml"]) >= 0);
        Assert.Equal(
            -1,
            SyntaxLanguageDefinition.GetPatternMatchScore(path, ["src/*.toml"]));
    }
}
