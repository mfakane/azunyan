using Xunit;

namespace Azunote.Tests.Shell;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Radio_group_names_are_unique_within_and_between_windows()
    {
        var first = GetGroupNames(new MainWindowViewModel());
        var second = GetGroupNames(new MainWindowViewModel());

        Assert.Equal(7, first.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(first.Intersect(second, StringComparer.Ordinal));
    }

    [Fact]
    public void Synchronous_command_forwards_its_parameter()
    {
        object? received = null;
        var command = new MainWindowCommand(parameter => received = parameter);

        command.Execute("value");

        Assert.Equal("value", received);
    }

    private static string[] GetGroupNames(MainWindowViewModel viewModel) =>
    [
        viewModel.Groups.StatusTabDisplaySize,
        viewModel.Groups.StatusIndentSize,
        viewModel.Groups.TabDisplaySize,
        viewModel.Groups.IndentSize,
        viewModel.Groups.IndentationInputMode,
        viewModel.Groups.LanguageModes,
        viewModel.Groups.OpenWindows
    ];
}
