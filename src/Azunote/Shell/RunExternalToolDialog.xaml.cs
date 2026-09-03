using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Azunote;

internal sealed partial class RunExternalToolDialog : ContentDialog
{
    private readonly ExternalToolCommandSuggestionProvider _commandSuggestionProvider = new();

    public RunExternalToolDialog()
    {
        InitializeComponent();

        InputModeBox.ItemsSource = Enum.GetNames<ExternalToolInputMode>();
        InputModeBox.SelectedIndex = 0;
        PerModeBox.ItemsSource = new[] { "none", "line", "regex" };
        PerModeBox.SelectedIndex = 0;
        OutputBox.ItemsSource = Enum.GetNames<ExternalToolOutputMode>();
        OutputBox.SelectedItem = nameof(ExternalToolOutputMode.Ignore);

        ModeSelector.SelectedItem = ExecutableModeItem;
        ModeSelector.SelectionChanged += (_, _) => UpdateCommandModeFields();
        PerModeBox.SelectionChanged += (_, _) => UpdatePerFields();
        CommandBox.TextChanged += CommandBox_TextChanged;
        CommandBox.SuggestionChosen += CommandBox_SuggestionChosen;

        UpdateCommandModeFields();
        UpdatePerFields();
    }

    internal async Task<ExternalToolDefinition?> ShowAndGetDefinitionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await base.ShowAsync() != ContentDialogResult.Primary
            || string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return CreateDefinition();
    }

    private void CommandBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var suggestions = _commandSuggestionProvider.GetSuggestions(sender.Text);
        sender.ItemsSource = suggestions;
        sender.IsSuggestionListOpen = suggestions.Count > 0;
    }

    private static void CommandBox_SuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string suggestion)
        {
            sender.Text = suggestion;
            sender.IsSuggestionListOpen = false;
        }
    }

    private void UpdateCommandModeFields()
    {
        var mode = ReadCommandMode();
        CommandBox.PlaceholderText = mode switch
        {
            ExternalToolCommandMode.Cmd => "echo Hello",
            ExternalToolCommandMode.Pwsh => "Write-Output Hello",
            _ => "clang-format, prettier, powershell..."
        };
        ArgumentsBox.Visibility = mode == ExternalToolCommandMode.Executable
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdatePerFields()
    {
        PerRegexBox.Visibility = string.Equals(
            PerModeBox.SelectedItem?.ToString(),
            "regex",
            StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private ExternalToolDefinition CreateDefinition()
    {
        var commandMode = ReadCommandMode();
        var inputMode = Enum.Parse<ExternalToolInputMode>(
            InputModeBox.SelectedItem?.ToString() ?? nameof(ExternalToolInputMode.None));
        var per = PerModeBox.SelectedItem?.ToString() switch
        {
            "line" => "line",
            "regex" => $"regex:{PerRegexBox.Text}",
            _ => "none"
        };
        var outputMode = Enum.Parse<ExternalToolOutputMode>(
            OutputBox.SelectedItem?.ToString() ?? nameof(ExternalToolOutputMode.Ignore));

        return new ExternalToolDefinition(
            CommandBox.Text,
            commandMode == ExternalToolCommandMode.Executable
                ? ExternalToolDefinition.ParseArguments(ArgumentsBox.Text)
                : [],
            inputMode,
            per,
            StdinBox.Text,
            new ExternalToolOutputActions(outputMode, outputMode),
            commandMode: commandMode);
    }

    private ExternalToolCommandMode ReadCommandMode() =>
        ModeSelector.SelectedItem == CmdModeItem
            ? ExternalToolCommandMode.Cmd
            : ModeSelector.SelectedItem == PowerShellModeItem
                ? ExternalToolCommandMode.Pwsh
                : ExternalToolCommandMode.Executable;
}
