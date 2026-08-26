using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            PlaceholderText = "Use ${file}, ${fileDir}, or ${env:USERNAME}"
        };
        var inputModeBox = new ComboBox
        {
            Header = "Input",
            ItemsSource = Enum.GetNames<ExternalToolInputMode>(),
            SelectedIndex = 0
        };
        var outputModeBox = new ComboBox
        {
            Header = "Output",
            ItemsSource = Enum.GetNames<ExternalToolOutputMode>(),
            SelectedIndex = 0
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(commandBox);
        panel.Children.Add(argumentsBox);
        panel.Children.Add(inputModeBox);
        panel.Children.Add(outputModeBox);

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
        var outputMode = Enum.Parse<ExternalToolOutputMode>(
            outputModeBox.SelectedItem?.ToString() ?? nameof(ExternalToolOutputMode.Ignore));
        return new ExternalToolDefinition(
            commandBox.Text,
            ExternalToolDefinition.ParseArguments(argumentsBox.Text),
            inputMode,
            outputMode);
    }
}
