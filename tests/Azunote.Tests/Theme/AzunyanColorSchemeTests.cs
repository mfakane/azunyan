using Azunyan.Core;
using Azunyan.WinUI;
using Microsoft.UI.Xaml;
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

    [Fact]
    public void Default_scheme_distinguishes_each_builtin_syntax_classification()
    {
        var colors = AzunyanColorScheme.Default;
        var syntaxColors = new[]
        {
            colors.ResolveSyntaxForeground("heading"),
            colors.ResolveSyntaxForeground("keyword"),
            colors.ResolveSyntaxForeground("string"),
            colors.ResolveSyntaxForeground("code"),
            colors.ResolveSyntaxForeground("number"),
            colors.ResolveSyntaxForeground("comment"),
            colors.ResolveSyntaxForeground("task-marker"),
            colors.ResolveSyntaxForeground("variable"),
            colors.ResolveSyntaxForeground(SyntaxClassifications.Link)
        };

        Assert.Equal(syntaxColors.Length, syntaxColors.Distinct().Count());
        Assert.DoesNotContain(colors.EditorForeground, syntaxColors);
    }

    [Fact]
    public void Default_scheme_uses_readable_selection_palette()
    {
        var colors = AzunyanColorScheme.Default;

        Assert.Equal(Color.FromArgb(0xff, 0xff, 0xff, 0xff), colors.SelectionForeground);
        Assert.Equal(byte.MaxValue, colors.SelectionBackground.A);
    }

    [Fact]
    public void Dark_system_scheme_uses_dark_editor_colors()
    {
        var light = AzunoteSystemColorScheme.Create(ElementTheme.Light);
        var dark = AzunoteSystemColorScheme.Create(ElementTheme.Dark);

        Assert.NotEqual(light.EditorBackground, dark.EditorBackground);
        Assert.NotEqual(light.EditorForeground, dark.EditorForeground);
        Assert.NotEqual(light.KeywordForeground, dark.KeywordForeground);
        Assert.Equal(byte.MaxValue, light.SelectionBackground.A);
        Assert.Equal(byte.MaxValue, dark.SelectionBackground.A);
        Assert.Equal(light.SelectionForeground, dark.SelectionForeground);
    }
}
