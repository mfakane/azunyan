namespace Azunote;

internal sealed class ApplicationCoordinator : IDisposable
{
    private MainWindow? _window;
    private bool _disposed;

    public MainWindow? Window => _window;

    public void Launch(AzunoteCommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, nameof(ApplicationCoordinator));

        _window = new MainWindow();
        _window.Activate();
        var runtime = _window.Runtime;
        _ = runtime.InitializeSettingsAsync();

        if (options.ReadStandardInput)
        {
            _ = OpenStandardInputAsync(runtime, options);
        }
        else if (!string.IsNullOrWhiteSpace(options.FilePath))
        {
            _ = runtime.OpenStartupDocumentAsync(
                options.FilePath,
                options.Line,
                options.Column);
        }
        else if (options.ShowHelp)
        {
            _ = runtime.ShowCommandLineHelpAsync();
        }
    }

    public bool TryShowUnhandledError(Exception exception, string logPath)
    {
        if (_window is null)
        {
            return false;
        }

        _window.Runtime.ShowUnhandledError(exception, logPath);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window?.Dispose();
        _window = null;
    }

    private static async Task OpenStandardInputAsync(
        MainWindowRuntime runtime,
        AzunoteCommandLineOptions options)
    {
        try
        {
            var text = await Console.In.ReadToEndAsync();
            await runtime.OpenStartupTextAsync(text, options.Line, options.Column);
        }
        catch (Exception exception)
        {
            await runtime.ShowStartupErrorAsync("Could not read standard input", exception.Message);
        }
    }
}
