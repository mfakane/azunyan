using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class DocumentDiagnosticsTests
{
    [Fact]
    public void A_run_replaces_only_the_problems_of_its_own_tool()
    {
        var diagnostics = new DocumentDiagnostics();
        diagnostics.Replace("a", [Problem("a", 10, 2), Problem("a", 0, 1)]);
        diagnostics.Replace("b", [Problem("b", 5, 1)]);

        diagnostics.Replace("a", [Problem("a", 7, 1)]);

        Assert.Equal([5, 7], diagnostics.Items.Select(item => item.Range.Start));
    }

    [Fact]
    public void Edits_move_later_problems_and_drop_the_ones_they_touch()
    {
        var diagnostics = new DocumentDiagnostics();
        diagnostics.Replace("a", [Problem("a", 2, 3), Problem("a", 10, 2), Problem("a", 20, 1)]);

        // Typing right before a problem moves it; text after it does not.
        diagnostics.Apply(new TextChange(new TextRange(10, 0), string.Empty, "xx"));
        diagnostics.Apply(new TextChange(new TextRange(5, 0), string.Empty, "y"));
        // Deleting into the last one drops it.
        diagnostics.Apply(new TextChange(new TextRange(22, 2), "zz", string.Empty));

        Assert.Equal([(2, 3), (13, 2)], diagnostics.Items.Select(item => (item.Range.Start, item.Range.Length)));
    }

    [Fact]
    public void Next_and_previous_wrap_around()
    {
        var diagnostics = new DocumentDiagnostics();
        diagnostics.Replace("a", [Problem("a", 4, 1), Problem("a", 9, 1)]);

        Assert.Equal(9, diagnostics.Next(4)!.Range.Start);
        Assert.Equal(4, diagnostics.Next(9)!.Range.Start);
        Assert.Equal(9, diagnostics.Previous(4)!.Range.Start);
        Assert.Equal(4, diagnostics.Previous(5)!.Range.Start);
    }

    private static DocumentDiagnostic Problem(string source, int start, int length) =>
        new(source, source, new TextRange(start, length), DiagnosticSeverity.Error, "m");
}
