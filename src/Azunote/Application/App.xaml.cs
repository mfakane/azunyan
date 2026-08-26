using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace Azunote;

public partial class App : Application, IDisposable
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconError = 0x00000010;
    private static int _unhandledDialogShown;
    private readonly ApplicationCoordinator _application = new();

    public App()
    {
        InitializeComponent();
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

        _application.Launch(options);
    }

    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        ReportUnhandledException("Unhandled UI exception", args.Exception);
        args.Handled = true;
    }

    private void OnAppDomainUnhandledException(
        object? sender,
        System.UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            ReportUnhandledException("Unhandled AppDomain exception", exception);
        }
        else
        {
            var logPath = ErrorReporter.LogMessage(
                "Unhandled AppDomain exception",
                args.ExceptionObject?.ToString() ?? "Unknown exception object.");
            ShowFallbackErrorDialog(
                "Azunote encountered an unexpected error",
                "An unexpected error occurred."
                + Environment.NewLine
                + Environment.NewLine
                + $"Details were written to:{Environment.NewLine}{logPath}");
        }
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        var exception = args.Exception.Flatten();
        ReportUnhandledException("Unobserved task exception", exception);
        args.SetObserved();
    }

    private void ReportUnhandledException(string source, Exception exception)
    {
        var logPath = ErrorReporter.LogException(source, exception);
        if (Interlocked.Exchange(ref _unhandledDialogShown, 1) != 0)
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        var message = $"{detail}{Environment.NewLine}{Environment.NewLine}"
            + $"Details were written to:{Environment.NewLine}{logPath}";

        if (!_application.TryShowUnhandledError(exception, logPath))
        {
            ShowFallbackErrorDialog("Azunote encountered an unexpected error", message);
        }
    }

    public void Dispose()
    {
        _application.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void ShowFallbackErrorDialog(string title, string message)
    {
        try
        {
            MessageBoxW(
                nint.Zero,
                message,
                title,
                MessageBoxOk | MessageBoxIconError);
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException("Fallback error dialog failure", exception);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(
        nint hWnd,
        string text,
        string caption,
        uint type);
}
