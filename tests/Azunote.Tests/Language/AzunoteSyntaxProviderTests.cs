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
}
