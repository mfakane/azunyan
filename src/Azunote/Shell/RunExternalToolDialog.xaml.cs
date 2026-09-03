using Azunyan.Core;
using Azunyan.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Azunote;

internal sealed partial class RunExternalToolDialog : ContentDialog
{
    private readonly ExternalToolCommandSuggestionProvider _commandSuggestionProvider = new();
    private readonly List<TextBoxCompletionController> _completionControllers = [];

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

        _completionControllers.Add(new TextBoxCompletionController(
            CommandBox,
            CommandCompletionPopup,
            DialogRoot,
            GetCommandCompletions));
        _completionControllers.Add(new TextBoxCompletionController(
            ArgumentsBox,
            ArgumentsCompletionPopup,
            DialogRoot,
            ExternalToolPlaceholderCompletionProvider.GetCompletions));
        _completionControllers.Add(new TextBoxCompletionController(
            StdinBox,
            StdinCompletionPopup,
            DialogRoot,
            ExternalToolPlaceholderCompletionProvider.GetCompletions));

        UpdateCommandModeFields();
        UpdatePerFields();
    }

    internal async Task<ExternalToolDefinition?> ShowAndGetDefinitionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (await base.ShowAsync() != ContentDialogResult.Primary
                || string.IsNullOrWhiteSpace(CommandBox.Text))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return CreateDefinition();
        }
        finally
        {
            DisposeCompletionControllers();
        }
    }

    private CompletionResult? GetCommandCompletions(string text, int position)
    {
        var placeholderResult = ExternalToolPlaceholderCompletionProvider.GetCompletions(text, position);
        if (placeholderResult is not null
            || (position == text.Length
                && (text.EndsWith('$')
                    || text.Contains("${", StringComparison.Ordinal))))
        {
            return placeholderResult;
        }

        if (position != text.Length || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var items = _commandSuggestionProvider.GetSuggestions(text)
            .Select(suggestion => new CompletionItem(suggestion))
            .ToArray();
        return new CompletionResult(
            TextRange.FromBounds(0, text.Length),
            items);
    }

    private void UpdateCommandModeFields()
    {
        var mode = ReadCommandMode();
        ArgumentsCompletionPopup.Hide();
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

    private void DisposeCompletionControllers()
    {
        foreach (var controller in _completionControllers)
        {
            controller.Dispose();
        }

        _completionControllers.Clear();
    }
}
