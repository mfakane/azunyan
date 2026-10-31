using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using WindowsFileActivation = Windows.ApplicationModel.Activation.IFileActivatedEventArgs;

namespace Azunote;

public partial class App : Application, IDisposable
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconError = 0x00000010;
    private const uint MessageBoxTaskModal = 0x00002000;
    private const int UnhandledExceptionExitCode = 1;
    private static int _reportingThreadId;
    private readonly ApplicationCoordinator _application = new();
    private readonly SingleInstanceHost _singleInstance = new();

    public App()
    {
        var dumpPath = NativeCrashReporter.Install();
        var previousDumpPath = NativeCrashReporter.FindLatestPreviousDump();
        if (dumpPath is not null)
        {
            ErrorReporter.LogMessage(
                "Native crash reporter installed",
                $"Crash dump reserved at: {dumpPath}");
        }

        if (previousDumpPath is not null)
        {
            ErrorReporter.LogMessage(
                "Previous native crash dump",
                previousDumpPath);
        }

        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        // WinUI initialization may install its own process-level exception
        // handling. Reassert ours after the framework has initialized.
        NativeCrashReporter.Install();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // A packaged file activation carries its payload through the Windows
        // App SDK activation model. Keep ordinary command-line launches on
        // the existing raw-argument path.
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var arguments = GetLaunchArguments(activation);
        if (!_singleInstance.TryAcquire())
        {
            var exitCode = 0;
            try
            {
                var standardInput = ReadStandardInputForForwarding(arguments);
                exitCode = (int)_singleInstance
                    .ForwardAsync(arguments, standardInput)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                exitCode = (int)SingleInstanceStatus.Failed;
                var logPath = ErrorReporter.LogException(
                    "Could not contact the running Azunote instance",
                    exception);
                ShowFallbackErrorDialog(
                    "Azunote could not be opened",
                    $"The running Azunote instance could not process the command.{Environment.NewLine}"
                    + $"{Environment.NewLine}Details were written to:{Environment.NewLine}{logPath}");
            }

            Environment.Exit(exitCode);
            return;
        }

        _application.Launch(arguments);
        NativeCrashReporter.Install();
        _singleInstance.Start(_application.HandleForwardedCommandLineAsync);
    }

    private static string[] GetLaunchArguments(
        AppActivationArguments? activation)
    {
        if (activation?.Kind == ExtendedActivationKind.File
            && activation.Data is WindowsFileActivation fileActivation)
        {
            if (fileActivation.Files.Count > 0
                && !string.IsNullOrWhiteSpace(fileActivation.Files[0].Path))
            {
                return [fileActivation.Files[0].Path];
            }

            return [];
        }

        return Environment.GetCommandLineArgs().Skip(1).ToArray();
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
        ReportUnhandledException(
            "Unhandled AppDomain exception",
            args.ExceptionObject as Exception
                ?? new InvalidOperationException(args.ExceptionObject?.ToString() ?? "Unknown exception object."));
    }

    /// <summary>
    /// A task nobody awaited failed. It surfaces only when the task is
    /// collected, long after the work that started it, so it is logged and
    /// the application goes on.
    /// </summary>
    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        ErrorReporter.LogException("Unobserved task exception", args.Exception.Flatten());
        args.SetObserved();
    }

    /// <summary>
    /// Logs the exception, backs up unsaved changes, tells what happened, and
    /// ends the process, whose state can no longer be trusted.
    /// </summary>
    private void ReportUnhandledException(string source, Exception exception)
    {
        var dumpPath = NativeCrashReporter.WriteSnapshotDump();
        var logPath = ErrorReporter.LogException(source, exception);
        if (dumpPath is not null)
        {
            ErrorReporter.LogMessage("Exception dump created", dumpPath);
        }

        // Another exception while the dialog is up is only logged. On the
        // dialog's thread its message loop has to go on; another thread waits
        // for the process to end.
        var threadId = Environment.CurrentManagedThreadId;
        var reportingThreadId = Interlocked.CompareExchange(ref _reportingThreadId, threadId, 0);
        if (reportingThreadId == threadId)
        {
            return;
        }

        if (reportingThreadId != 0)
        {
            Thread.Sleep(Timeout.Infinite);
        }

        // The windows' documents can be read only on their thread.
        if (Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() is not null)
        {
            try
            {
                _application.PrepareForSessionEnd();
            }
            catch (Exception backupException)
            {
                ErrorReporter.LogException("Back up documents before closing", backupException);
            }
        }

        var detail = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        ShowFallbackErrorDialog(
            "Azunote encountered an unexpected error",
            $"Azunote has to close.{Environment.NewLine}{Environment.NewLine}"
            + $"{detail}{Environment.NewLine}{Environment.NewLine}"
            + $"Details were written to:{Environment.NewLine}{logPath}");
        Environment.Exit(UnhandledExceptionExitCode);
    }

    public void Dispose()
    {
        _singleInstance.Dispose();
        _application.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string? ReadStandardInputForForwarding(IReadOnlyList<string> arguments)
    {
        try
        {
            var options = AzunoteCommandLine.Parse(arguments);
            return options.ReadStandardInput ? Console.In.ReadToEnd() : null;
        }
        catch (CommandLineParseException)
        {
            // Let the primary instance parse and report the original arguments.
            return null;
        }
    }

    private static void ShowFallbackErrorDialog(string title, string message)
    {
        try
        {
            MessageBoxW(
                nint.Zero,
                message,
                title,
                MessageBoxOk | MessageBoxIconError | MessageBoxTaskModal);
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
