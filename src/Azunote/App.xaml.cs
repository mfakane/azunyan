using Microsoft.UI.Xaml;

namespace Azunote;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

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
}
