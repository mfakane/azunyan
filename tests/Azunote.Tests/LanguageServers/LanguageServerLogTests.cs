using Azunote;
using Xunit;

namespace Azunote.Tests.LanguageServers;

public sealed class LanguageServerLogTests
{
    [Fact]
    public void A_subscriber_gets_the_kept_lines_and_then_each_new_line_until_disposed()
    {
        var log = new LanguageServerLog();
        log.Write("first");
        var appended = new List<string>();

        var subscription = log.Subscribe(appended.Add, out var kept);
        log.Write("second\r\ncontinued");
        subscription.Dispose();
        log.Write("third");

        Assert.EndsWith("] first" + Environment.NewLine, kept, StringComparison.Ordinal);
        var line = Assert.Single(appended);
        Assert.EndsWith("] second continued" + Environment.NewLine, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_last_lines_are_kept_and_long_lines_are_cut_short()
    {
        var log = new LanguageServerLog();
        for (var index = 0; index < 2001; index++)
        {
            log.Write($"line {index}");
        }

        log.Write(new string('x', 5000));

        log.Subscribe(_ => { }, out var kept).Dispose();
        var lines = kept.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2000, lines.Length);
        Assert.EndsWith("] line 2", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("… (1000 more characters)", lines[^1], StringComparison.Ordinal);
    }
}
