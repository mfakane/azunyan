using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class AzunoteSyntaxProviderTests
{
    [Fact]
    public async Task Rule_based_provider_preserves_azunote_classifications()
    {
        const string text = "# TODO 12\n[x] \"http://x\" // DONE";
        var snapshot = new TextSnapshot(text);
        var provider = new AzunoteSyntaxProvider();

        var spans = await provider.GetSyntaxAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        Assert.Equal(
            [
                "# TODO 12:heading",
                "[x]:task-marker",
                "\"http://x\":string",
                "// DONE:comment"
            ],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }
}
