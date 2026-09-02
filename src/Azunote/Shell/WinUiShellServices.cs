using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using UiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Azunote;

internal sealed class DispatcherQueueUiDispatcher : IUiDispatcher
{
    private readonly UiDispatcherQueue _dispatcherQueue;

    public DispatcherQueueUiDispatcher(UiDispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _dispatcherQueue.TryEnqueue(() => action());
    }
}

internal sealed class WinUiSettingsFolderOpener : ISettingsFolderOpener
{
    public async Task OpenAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await Launcher.LaunchFolderAsync(folder))
        {
            throw new InvalidOperationException("Windows could not open the settings folder.");
        }
    }
}

internal sealed class WinUiFilePathActions : IFilePathActions
{
    public void CopyFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var dataPackage = new DataPackage();
        dataPackage.SetText(filePath);
        Clipboard.SetContent(dataPackage);
    }

    public Task OpenExplorerAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = string.Join(" ", arguments.Select(x => x.Contains(' ') ? QuoteCommandShellArgument(x) : x)),
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException($"Windows could not open command '{command}'.");
        }

        return Task.CompletedTask;
    }

    public Task OpenTerminalAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = string.Join(" ", arguments.Select(x => x.Contains(' ') ? QuoteCommandShellArgument(x) : x)),
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException($"Windows could not open terminal '{command}'.");
        }

        return Task.CompletedTask;
    }

    private static string QuoteCommandShellArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

internal sealed class WinUiMessageDialog : IMessageDialog
{
    private readonly Func<XamlRoot?> _xamlRoot;

    public WinUiMessageDialog(Func<XamlRoot?> xamlRoot)
    {
        _xamlRoot = xamlRoot ?? throw new ArgumentNullException(nameof(xamlRoot));
    }

    public async Task ShowAsync(string title, string message)
    {
        var root = _xamlRoot();
        if (root is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = root
        };
        await dialog.ShowAsync();
    }
}

internal sealed class WinUiGoToLineDialog : IGoToLineDialog
{
    private readonly Func<XamlRoot?> _xamlRoot;

    public WinUiGoToLineDialog(Func<XamlRoot?> xamlRoot)
    {
        _xamlRoot = xamlRoot ?? throw new ArgumentNullException(nameof(xamlRoot));
    }

    public async Task<GoToLineTarget?> ShowAsync(string initialText)
    {
        ArgumentNullException.ThrowIfNull(initialText);

        var root = _xamlRoot();
        if (root is null)
        {
            return null;
        }

        var inputBox = new TextBox
        {
            Header = "Type line number or line:column",
            PlaceholderText = "12 or 12:4",
            Text = initialText,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false
        };
        var errorText = new TextBlock
        {
            Text = "Enter a positive line number or line:column.",
            Visibility = Visibility.Collapsed
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(inputBox);
        panel.Children.Add(errorText);

        GoToLineTarget? target = null;
        var dialog = new ContentDialog
        {
            Title = "Go to Line",
            Content = panel,
            PrimaryButtonText = "Go",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root
        };
        dialog.Opened += (_, _) =>
        {
            inputBox.Focus(FocusState.Programmatic);
            inputBox.SelectAll();
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!GoToLineService.TryParse(inputBox.Text, out var parsed))
            {
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            errorText.Visibility = Visibility.Collapsed;
            target = parsed;
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? target
            : null;
    }
}

internal sealed class WinUiExternalToolDialog : IExternalToolDialog
{
    private readonly Func<XamlRoot?> _xamlRoot;

    public WinUiExternalToolDialog(Func<XamlRoot?> xamlRoot)
    {
        _xamlRoot = xamlRoot ?? throw new ArgumentNullException(nameof(xamlRoot));
    }

    public async Task<ExternalToolDefinition?> ShowAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var commandBox = new TextBox
        {
            Header = "Command",
            PlaceholderText = "clang-format, prettier, powershell..."
        };
        var argumentsBox = new TextBox
        {
            Header = "Arguments",
            PlaceholderText = "Use ${file}, ${fileBasename}, or ${env:USERNAME}"
        };
        var inputModeBox = new ComboBox
        {
            Header = "Input",
            ItemsSource = Enum.GetNames<ExternalToolInputMode>(),
            SelectedIndex = 0
        };
        var perBox = new TextBox
        {
            Header = "Per",
            Text = "none",
            PlaceholderText = "none, line, or regex:<pattern>"
        };
        var stdinBox = new TextBox
        {
            Header = "Stdin",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "${input}"
        };

        static ComboBox CreateOutputActionBox(
            string header,
            ExternalToolOutputMode selected)
        {
            return new ComboBox
            {
                Header = header,
                ItemsSource = Enum.GetNames<ExternalToolOutputMode>(),
                SelectedItem = selected.ToString()
            };
        }

        static ExternalToolOutputMode ReadOutputAction(ComboBox box) =>
            Enum.Parse<ExternalToolOutputMode>(
                box.SelectedItem?.ToString() ?? nameof(ExternalToolOutputMode.Ignore));

        var outputSuccessBox = CreateOutputActionBox(
            "Output (zero)",
            ExternalToolOutputMode.Ignore);
        var outputFailureBox = CreateOutputActionBox(
            "Output (non-zero)",
            ExternalToolOutputMode.Ignore);
        var stdoutSuccessBox = CreateOutputActionBox(
            "Stdout (zero)",
            ExternalToolOutputMode.Ignore);
        var stdoutFailureBox = CreateOutputActionBox(
            "Stdout (non-zero)",
            ExternalToolOutputMode.Ignore);
        var stderrSuccessBox = CreateOutputActionBox(
            "Stderr (zero)",
            ExternalToolOutputMode.Ignore);
        var stderrFailureBox = CreateOutputActionBox(
            "Stderr (non-zero)",
            ExternalToolOutputMode.Ignore);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(commandBox);
        panel.Children.Add(argumentsBox);
        panel.Children.Add(inputModeBox);
        panel.Children.Add(perBox);
        panel.Children.Add(stdinBox);
        panel.Children.Add(outputSuccessBox);
        panel.Children.Add(outputFailureBox);
        panel.Children.Add(stdoutSuccessBox);
        panel.Children.Add(stdoutFailureBox);
        panel.Children.Add(stderrSuccessBox);
        panel.Children.Add(stderrFailureBox);

        var root = _xamlRoot();
        if (root is null)
        {
            return null;
        }

        var dialog = new ContentDialog
        {
            Title = "Run External Tool",
            Content = panel,
            PrimaryButtonText = "Run",
            CloseButtonText = "Cancel",
            XamlRoot = root
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || string.IsNullOrWhiteSpace(commandBox.Text))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var inputMode = Enum.Parse<ExternalToolInputMode>(
            inputModeBox.SelectedItem?.ToString() ?? nameof(ExternalToolInputMode.None));
        return new ExternalToolDefinition(
            commandBox.Text,
            ExternalToolDefinition.ParseArguments(argumentsBox.Text),
            inputMode,
            perBox.Text,
            stdinBox.Text,
            new ExternalToolOutputActions(
                ReadOutputAction(outputSuccessBox),
                ReadOutputAction(outputFailureBox)),
            new ExternalToolOutputActions(
                ReadOutputAction(stdoutSuccessBox),
                ReadOutputAction(stdoutFailureBox)),
            new ExternalToolOutputActions(
                ReadOutputAction(stderrSuccessBox),
                ReadOutputAction(stderrFailureBox)));
    }
}
