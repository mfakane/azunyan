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

    [Fact]
    public void Selection_properties_notify_their_derived_radio_states()
    {
        var viewModel = new MainWindowViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.TabDisplaySize = 8;
        viewModel.IndentSize = 4;
        viewModel.IndentationInputMode = Azunyan.Core.IndentationInputMode.Spaces;

        Assert.True(viewModel.IsTabDisplaySize8);
        Assert.True(viewModel.IsIndentSize4);
        Assert.True(viewModel.IsIndentationModeSpaces);
        Assert.Contains(nameof(MainWindowViewModel.IsTabDisplaySize8), changed);
        Assert.Contains(nameof(MainWindowViewModel.IsIndentSize4), changed);
        Assert.Contains(nameof(MainWindowViewModel.IsIndentationModeSpaces), changed);
    }

    [Fact]
    public void Applying_status_updates_bound_status_values()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.ApplyStatus(new StatusBarState(
            "Ln 3, Col 5", "UTF-16 LE", "CRLF", "Tabs: 4", "C#", "file.cs", true));

        Assert.Equal("Ln 3, Col 5", viewModel.PositionStatus);
        Assert.Equal("file.cs", viewModel.FilePathStatus);
        Assert.True(viewModel.HasFilePath);
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
