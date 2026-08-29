using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class SlidingInputWindowCalculatorTests
{
    [Fact]
    public void Includes_context_around_the_selection()
    {
        var snapshot = new TextSnapshot(new string('a', 10000));
        var calculator = new SlidingInputWindowCalculator(10, 20, 5);

        var window = calculator.Calculate(snapshot, new TextSelection(5000, 5000));

        Assert.Equal(new TextRange(4990, 30), window);
    }

    [Fact]
    public void Includes_the_composition_even_when_it_is_larger_than_the_selection()
    {
        var snapshot = new TextSnapshot(new string('a', 1000));
        var calculator = new SlidingInputWindowCalculator(10, 10, 5);

        var window = calculator.Calculate(
            snapshot,
            new TextSelection(500, 500),
            new TextRange(450, 100));

        Assert.Equal(new TextRange(440, 120), window);
    }

    [Fact]
    public void Aligns_window_edges_to_text_element_boundaries()
    {
        var snapshot = new TextSnapshot("012a\u0301😀xyz");
        var calculator = new SlidingInputWindowCalculator(1, 1, 1);

        var window = calculator.Calculate(snapshot, TextSelection.Caret(5));

        Assert.Equal(new TextRange(3, 4), window);
        Assert.True(snapshot.IsTextElementBoundary(window.Start));
        Assert.True(snapshot.IsTextElementBoundary(window.End));
        Assert.Equal("a\u0301😀", snapshot.GetText(window));
    }

    [Fact]
    public void Keeps_existing_window_until_the_caret_reaches_hysteresis_edge()
    {
        var snapshot = new TextSnapshot(new string('a', 1000));
        var calculator = new SlidingInputWindowCalculator(100, 100, 20);
        var existing = new TextRange(300, 400);

        Assert.Equal(
            existing,
            calculator.Calculate(snapshot, TextSelection.Caret(500), currentWindow: existing));
        Assert.Equal(
            new TextRange(590, 200),
            calculator.Calculate(snapshot, TextSelection.Caret(690), currentWindow: existing));
    }

    [Fact]
    public void Keeps_document_edges_and_expands_for_a_large_selection()
    {
        var snapshot = new TextSnapshot(new string('a', 100));
        var calculator = new SlidingInputWindowCalculator(10, 10, 5);

        Assert.Equal(
            new TextRange(0, 100),
            calculator.Calculate(snapshot, new TextSelection(5, 95)));
    }

    [Fact]
    public void Rejects_negative_configuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SlidingInputWindowCalculator(beforeContextLength: -1));
    }
}
