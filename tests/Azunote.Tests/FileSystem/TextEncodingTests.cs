using Xunit;

namespace Azunote.Tests.FileSystem;

public sealed class TextEncodingTests
{
    [Theory]
    [InlineData("utf-8-bom", "utf-8-bom")]
    [InlineData("UTF-16LE", "utf-16le")]
    [InlineData("sjis", "shift_jis")]
    [InlineData("932", "shift_jis")]
    [InlineData("latin1", "iso-8859-1")]
    public void Names_and_aliases_read_as_the_encoding_of_one_name(string name, string expected)
    {
        Assert.True(TextEncoding.TryParse(name, out var encoding));
        Assert.Equal(expected, encoding.Name);
    }

    [Fact]
    public void Euc_jp_is_shown_by_that_name()
    {
        Assert.True(TextEncoding.TryParse("euc-jp", out var encoding));
        Assert.Equal("Japanese (EUC-JP)", encoding.DisplayName);
    }

    [Fact]
    public void An_unknown_name_is_not_an_encoding()
    {
        Assert.False(TextEncoding.TryParse("no-such-encoding", out _));
    }

    [Fact]
    public void The_offered_encodings_start_with_unicode_and_name_each_once()
    {
        var offered = TextEncoding.GetOffered(["euc-jp", "utf-8", "EUC-JP"]);

        Assert.Equal(TextEncoding.Unicode, offered.Take(4));
        Assert.Equal(offered.Distinct(), offered);
        Assert.Contains(offered, encoding => encoding.Name == "euc-jp");
    }

    [Fact]
    public void Every_encoding_lists_unicode_first_and_includes_those_not_listed_by_dotnet()
    {
        var all = TextEncoding.GetAll();

        Assert.Equal(TextEncoding.Unicode, all.Take(4));
        Assert.Contains(all, encoding => encoding.Name == "iso-2022-jp");
        Assert.Contains(all, encoding => encoding.Name == "gb18030");
        Assert.Equal(all.Distinct(), all);
    }
}
