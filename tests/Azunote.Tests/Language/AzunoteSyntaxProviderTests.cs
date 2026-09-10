using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class AzunoteSyntaxProviderTests
{
    [Fact]
    public async Task Configuration_provider_uses_toml_syntax()
    {
        const string text = "# note\nname = \"azunote\"\n[tool]\nactive = true";
        var snapshot = new TextSnapshot(text);
        var provider = new AzunoteSyntaxProvider();

        var spans = await provider.GetSyntaxAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        Assert.Equal(
            [
                "# note:comment",
                "name:keyword",
                "\"azunote\":string",
                "[tool]:heading",
                "active:keyword",
                "true:keyword"
            ],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Configuration_provider_folds_toml_sections()
    {
        const string text = "[debug]\nlogging = []\n[terminal]\ncommand = \"wt.exe\"";
        var snapshot = new TextSnapshot(text);
        var provider = new AzunoteFoldingProvider();

        var folds = await provider.GetFoldsAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        var fold = Assert.Single(folds.Where(item => item.Id.Contains("debug", StringComparison.Ordinal)));
        Assert.Equal("\nlogging = []\n", snapshot.GetText(fold.Range));
    }
}
