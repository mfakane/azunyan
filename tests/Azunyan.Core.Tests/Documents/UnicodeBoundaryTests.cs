using System.Globalization;
using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class UnicodeBoundaryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("abc\r\ndef\n\tghi\0\u007f")]
    [InlineData("a\u0301bc\u0301d")]
    [InlineData("\u0600ab\u0301cd")]
    [InlineData("日本語a\r\nbカ\u3099")]
    [InlineData("x👩🏽‍💻ab🇯🇵🇺🇸c1️⃣d")]
    [InlineData("xक्‍षab각cd")]
    [InlineData("a\ud800b\udc00c")]
    public void Boundaries_and_deletion_preserve_framework_grapheme_clusters(string text)
    {
        Verify(text);
    }

    [Fact]
    public void All_ascii_pairs_preserve_boundaries_even_next_to_unicode()
    {
        for (var before = 0; before < 128; before++)
        for (var after = 0; after < 128; after++)
            Verify("\u0600" + (char)before + (char)after + "\u0301");
    }

    [Fact]
    public void Snapshot_boundary_validation_rejects_out_of_range_positions()
    {
        var snapshot = new TextSnapshot("ab");
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsTextElementBoundary(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.IsTextElementBoundary(3));
    }

    private static void Verify(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var snapshot = new TextSnapshot(text);
        for (var position = 0; position <= text.Length; position++)
        {
            Assert.Equal(position == text.Length || starts.Contains(position),
                snapshot.IsTextElementBoundary(position));
            if (position == text.Length) continue;
            var start = starts.Last(value => value <= position);
            var end = starts.FirstOrDefault(value => value > position, text.Length);
            var expected = TextRange.FromBounds(start, end);
            Assert.Equal(expected, UnicodeText.GetTextElementRange(text, position));
            Assert.Equal(expected, UnicodeText.GetForwardDeleteRange(text, position));
            Assert.Equal(expected, UnicodeText.GetBackwardDeleteRange(text, position + 1));
        }
    }
}
