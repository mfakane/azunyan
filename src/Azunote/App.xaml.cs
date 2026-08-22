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
        MainWindow = new MainWindow();
        MainWindow.Activate();

        var startupPath = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(argument => !argument.StartsWith("-", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(startupPath))
        {
            _ = MainWindow.OpenStartupDocumentAsync(startupPath);
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
