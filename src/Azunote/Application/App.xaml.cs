using Microsoft.UI.Xaml;

namespace Azunote;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AzunoteCommandLineOptions options;
        try
        {
            options = AzunoteCommandLine.Parse(Environment.GetCommandLineArgs().Skip(1));
        }
        catch (CommandLineParseException exception)
        {
            options = new AzunoteCommandLineOptions { ShowHelp = true };
            ErrorReporter.LogMessage("Invalid command line", exception.Message);
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();
        _ = MainWindow.InitializeSettingsAsync();

        if (options.ReadStandardInput)
        {
            _ = OpenStandardInputAsync(MainWindow, options);
        }
        else if (!string.IsNullOrWhiteSpace(options.FilePath))
        {
            _ = MainWindow.OpenStartupDocumentAsync(
                options.FilePath,
                options.Line,
                options.Column);
        }
        else if (options.ShowHelp)
        {
            _ = MainWindow.ShowCommandLineHelpAsync();
        }
    }

    private static async Task OpenStandardInputAsync(
        MainWindow window,
        AzunoteCommandLineOptions options)
    {
        try
        {
            var text = await Console.In.ReadToEndAsync();
            await window.OpenStartupTextAsync(text, options.Line, options.Column);
        }
        catch (Exception exception)
        {
            await window.ShowStartupErrorAsync("Could not read standard input", exception.Message);
        }
    }

    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        var logPath = ErrorReporter.LogException("Unhandled UI exception", args.Exception);
        args.Handled = true;
        MainWindow?.ShowUnhandledError(args.Exception, logPath);
    }

    private static void OnAppDomainUnhandledException(
        object? sender,
        System.UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            ErrorReporter.LogException("Unhandled AppDomain exception", exception);
        }
        else
        {
            ErrorReporter.LogMessage(
                "Unhandled AppDomain exception",
                args.ExceptionObject?.ToString() ?? "Unknown exception object.");
        }
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        var exception = args.Exception.Flatten();
        var logPath = ErrorReporter.LogException("Unobserved task exception", exception);
        args.SetObserved();
        MainWindow?.ShowUnhandledError(exception, logPath);
    }
}
