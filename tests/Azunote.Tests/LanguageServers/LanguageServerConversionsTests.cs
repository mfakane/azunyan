using System.Text.Json.Nodes;
using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.LanguageServers;

public sealed class LanguageServerConversionsTests
{
    [Fact]
    public void Completion_keeps_the_items_matching_the_word_before_the_caret_in_sort_order()
    {
        var context = Context("console.lo", "console.lo".Length);
        var completion = JsonNode.Parse("""
            {
              "isIncomplete": false,
              "items": [
                { "label": "log", "sortText": "2", "detail": "(method) log()" },
                { "label": "error", "sortText": "0" },
                { "label": "LocalThing", "sortText": "1", "documentation": { "kind": "markdown", "value": "A thing." } }
              ]
            }
            """);

        var result = LanguageServerConversions.ToCompletionResult(completion, context)!;

        Assert.Equal(TextRange.FromBounds("console.".Length, "console.lo".Length), result.ReplacementRange);
        Assert.Equal(["LocalThing", "log"], result.Items.Select(item => item.Label));
        Assert.Equal("A thing.", result.Items[0].Documentation);
        Assert.Equal("(method) log()", result.Items[1].Detail);
        Assert.Equal([0, 1], result.Items.Select(item => item.SortOrder));
    }

    [Fact]
    public void Completion_adjusts_an_edit_that_replaces_more_than_the_word()
    {
        var context = Context("@Ht", 3);
        var completion = JsonNode.Parse("""
            [
              {
                "label": "@Html",
                "filterText": "Html",
                "textEdit": {
                  "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 3 } },
                  "newText": "@Html"
                }
              },
              {
                "label": "Snippet",
                "filterText": "Html",
                "insertTextFormat": 2,
                "insertText": "Html(${1:value})$0"
              },
              {
                "label": "Escaped",
                "filterText": "Html",
                "insertTextFormat": 2,
                "insertText": "Html(\\$a, ${1:\\$b\\}}, ${2|c,d|})"
              }
            ]
            """);

        var result = LanguageServerConversions.ToCompletionResult(completion, context)!;

        Assert.Equal(new TextRange(1, 2), result.ReplacementRange);
        Assert.Equal("Html", result.Items.Single(item => item.Label == "@Html").InsertText);
        Assert.Equal("Html(value)", result.Items.Single(item => item.Label == "Snippet").InsertText);
        Assert.Equal("Html($a, $b}, c)", result.Items.Single(item => item.Label == "Escaped").InsertText);
    }

    [Fact]
    public void Completion_filters_by_the_text_the_edit_replaces()
    {
        // As vscode-json-language-server answers inside a string value.
        var context = Context("""{ "mode": "f""", 12);
        var completion = JsonNode.Parse("""
            [
              {
                "label": "\"fast\"",
                "insertTextFormat": 2,
                "textEdit": {
                  "range": { "start": { "line": 0, "character": 10 }, "end": { "line": 0, "character": 12 } },
                  "newText": "\"fast\""
                }
              },
              {
                "label": "\"slow\"",
                "insertTextFormat": 2,
                "textEdit": {
                  "range": { "start": { "line": 0, "character": 10 }, "end": { "line": 0, "character": 12 } },
                  "newText": "\"slow\""
                }
              }
            ]
            """);

        var result = LanguageServerConversions.ToCompletionResult(completion, context)!;

        Assert.Equal(new TextRange(11, 1), result.ReplacementRange);
        Assert.Equal("fast\"", Assert.Single(result.Items).InsertText);
    }

    [Fact]
    public void Diagnostics_map_positions_severities_and_empty_ranges()
    {
        var snapshot = new TextSnapshot("let value = 1;\r\nvalue.foo();\r\n");
        var diagnostics = JsonNode.Parse("""
            [
              {
                "range": { "start": { "line": 1, "character": 6 }, "end": { "line": 1, "character": 9 } },
                "severity": 1,
                "source": "ts",
                "message": "Property 'foo' does not exist."
              },
              {
                "range": { "start": { "line": 0, "character": 6 }, "end": { "line": 0, "character": 6 } },
                "severity": 4,
                "message": "Unused."
              },
              {
                "range": { "start": { "line": 9, "character": 0 }, "end": { "line": 9, "character": 4 } },
                "severity": 2,
                "message": "Past the end."
              }
            ]
            """)!.AsArray();

        var result = LanguageServerConversions.ToDiagnostics(diagnostics, snapshot, "key", "server");

        Assert.Equal(new TextRange(22, 3), result[0].Range);
        Assert.Equal(DiagnosticSeverity.Error, result[0].Severity);
        Assert.Equal("ts", result[0].Source);
        Assert.Equal(TextRange.FromBounds(4, 9), result[1].Range);
        Assert.Equal(DiagnosticSeverity.Information, result[1].Severity);
        Assert.Equal("server", result[1].Source);
        Assert.Equal(DiagnosticSeverity.Warning, result[2].Severity);
        Assert.True(result[2].Range.End <= snapshot.Length);
    }

    [Fact]
    public void Hover_text_drops_markdown_code_fences()
    {
        var context = Context("value.foo", 2);
        var hover = JsonNode.Parse("""
            {
              "contents": { "kind": "markdown", "value": "```typescript\nlet value: number\n```\nThe value." }
            }
            """);

        var tooltip = LanguageServerConversions.ToTooltip(hover, context)!;

        Assert.Equal($"let value: number{Environment.NewLine}The value.", tooltip.Content);
        Assert.Equal(new TextRange(0, 5), tooltip.Range);
    }

    [Fact]
    public void Hover_reads_marked_strings()
    {
        var hover = JsonNode.Parse("""
            { "contents": [ { "language": "python", "value": "def run()" }, "Runs it." ] }
            """);

        var tooltip = LanguageServerConversions.ToTooltip(hover, Context("run()", 1))!;

        Assert.Equal($"def run(){Environment.NewLine}{Environment.NewLine}Runs it.", tooltip.Content);
    }

    [Fact]
    public void Formatting_edits_come_last_first_and_keep_the_order_of_those_at_one_position()
    {
        var edits = JsonNode.Parse("""
            [
              { "range": { "start": { "line": 0, "character": 1 }, "end": { "line": 0, "character": 2 } }, "newText": "" },
              { "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 1, "character": 0 } }, "newText": "a" },
              { "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 1, "character": 0 } }, "newText": "b" }
            ]
            """);

        var result = LanguageServerConversions.ToTextEdits(edits, new TextSnapshot("{ \n}"));

        Assert.Equal(
            [(new TextRange(3, 0), "b"), (new TextRange(3, 0), "a"), (new TextRange(1, 1), "")],
            result);
        Assert.Empty(LanguageServerConversions.ToTextEdits(null, new TextSnapshot("{}")));
    }

    [Fact]
    public void Inlay_hints_join_their_label_parts_and_padding()
    {
        var hints = JsonNode.Parse("""
            [
              { "position": { "line": 0, "character": 6 }, "label": "x:", "kind": 2, "paddingRight": true },
              { "position": { "line": 0, "character": 3 }, "label": [{ "value": ": " }, { "value": "int" }], "kind": 1 },
              { "position": { "line": 0, "character": 1 }, "label": "" }
            ]
            """);

        var inlays = LanguageServerConversions.ToInlays(hints, new TextSnapshot("let a(1)"));

        Assert.Equal(
            [(DocumentAnchor.Before(6), "x: "), (DocumentAnchor.After(3), ": int")],
            inlays.Select(inlay => (inlay.Anchor, inlay.Content.Text)));
    }

    [Fact]
    public void Signature_help_finds_parameters_by_text_or_offsets()
    {
        var help = JsonNode.Parse("""
            {
              "activeSignature": 1,
              "activeParameter": 1,
              "signatures": [
                { "label": "Add(int a)", "parameters": [{ "label": "int a" }] },
                {
                  "label": "Add(int a, int a2)",
                  "documentation": { "kind": "markdown", "value": "Adds." },
                  "parameters": [
                    { "label": "int a" },
                    { "label": "int a2", "documentation": "The second." }
                  ]
                },
                { "label": "Add(long x)", "parameters": [{ "label": [4, 10] }], "activeParameter": 0 },
                { "label": "" }
              ]
            }
            """);

        var result = LanguageServerConversions.ToSignatureHelp(help)!;

        Assert.Equal(1, result.ActiveSignature);
        Assert.Equal(3, result.Signatures.Count);
        Assert.Equal(-1, result.Signatures[0].ActiveParameter);
        var second = result.Signatures[1];
        Assert.Equal([new TextRange(4, 5), new TextRange(11, 6)], second.Parameters.Select(parameter => parameter.Label));
        Assert.Equal(1, second.ActiveParameter);
        Assert.Equal("The second.", second.Parameters[1].Documentation);
        Assert.Equal("Adds.", second.Documentation);
        Assert.Equal(TextRange.FromBounds(4, 10), Assert.Single(result.Signatures[2].Parameters).Label);
        Assert.Equal(0, result.Signatures[2].ActiveParameter);
        Assert.Null(LanguageServerConversions.ToSignatureHelp(JsonNode.Parse("""{ "signatures": [] }""")));
        Assert.Null(LanguageServerConversions.ToSignatureHelp(null));
    }

    private static EditorProviderContext Context(string text, int position) =>
        new(new TextSnapshot(text), position, TextSelection.Caret(position));
}
