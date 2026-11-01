using System.Diagnostics;
using Azunyan.WinUI;
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
        return _dispatcherQueue.TryEnqueueReporting(() => action());
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

    public Task OpenWithDefaultApplicationAsync(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        };

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException($"Windows could not open '{target}'.");
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

        var root = _xamlRoot();
        if (root is null)
        {
            return null;
        }

        var dialog = new RunExternalToolDialog
        {
            XamlRoot = root
        };
        return await dialog.ShowAndGetDefinitionAsync(cancellationToken);
    }
}
