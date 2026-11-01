using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class GoToLineServiceTests
{
    [Theory]
    [InlineData("12", 12, null)]
    [InlineData("12:4", 12, 4)]
    [InlineData(" 12:4 ", 12, 4)]
    public void Try_parse_accepts_line_and_line_column_forms(
        string input,
        int expectedLine,
        int? expectedColumn)
    {
        Assert.True(GoToLineService.TryParse(input, out var target));
        Assert.Equal(new GoToLineTarget(expectedLine, expectedColumn), target);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("12:0")]
    [InlineData("-1")]
    [InlineData("12:-4")]
    [InlineData("12 : 4")]
    [InlineData("12:4:8")]
    [InlineData("line")]
    [InlineData("999999999999999999999")]
    public void Try_parse_rejects_invalid_input(string input)
    {
        Assert.False(GoToLineService.TryParse(input, out _));
    }

    [Fact]
    public void Resolve_uses_column_one_when_only_a_line_is_given()
    {
        var snapshot = new TextSnapshot("first\nsecond");

        var position = GoToLineService.Resolve(
            snapshot,
            new GoToLineTarget(2));

        Assert.Equal(new LineColumn(1, 0), position);
    }

    [Fact]
    public void Resolve_clamps_line_and_column_to_the_document()
    {
        var snapshot = new TextSnapshot("first\nsecond");

        var position = GoToLineService.Resolve(
            snapshot,
            new GoToLineTarget(int.MaxValue, int.MaxValue));

        Assert.Equal(new LineColumn(1, 6), position);
    }
}
