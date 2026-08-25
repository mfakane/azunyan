using Azunyan.WinUI;
using Windows.UI;
using Xunit;

namespace Azunote.Tests;

public sealed class AzunyanColorSchemeTests
{
    [Fact]
    public void Syntax_resolver_overrides_known_and_custom_classifications()
    {
        var custom = Color.FromArgb(0xff, 1, 2, 3);
        var colors = AzunyanColorScheme.Default with
        {
            SyntaxForegroundResolver = classification =>
                classification is "keyword" or "variable" ? custom : null
        };

        Assert.Equal(custom, colors.ResolveSyntaxForeground("keyword"));
        Assert.Equal(custom, colors.ResolveSyntaxForeground("variable"));
        Assert.Equal(colors.CommentForeground, colors.ResolveSyntaxForeground("comment"));
        Assert.Equal(colors.EditorForeground, colors.ResolveSyntaxForeground("unknown"));
    }
}
