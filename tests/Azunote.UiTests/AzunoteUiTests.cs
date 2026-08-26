using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Xunit;
using Xunit.Sdk;

namespace Azunote.UiTests;

/// <summary>
/// Windows UI Automation smoke tests for the unpackaged Azunote desktop app.
/// These tests are opt-in because they need an unlocked interactive desktop.
/// </summary>
public sealed class AzunoteUiTests : IClassFixture<AzunoteUiFixture>
{
    private readonly AzunoteUiFixture _fixture;

    public AzunoteUiTests(AzunoteUiFixture fixture)
    {
        _fixture = fixture;
    }

    [AzunoteUiFact]
    public void Projected_editor_exposes_snapshot_text_and_visible_ranges()
    {
        var editor = _fixture.Editor;
        var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
        var text = AzunoteUiFixture.WaitForDocumentText(
            textPattern,
            expected => expected.Contains("日本語", StringComparison.Ordinal));

        Assert.Contains("Azunyan UI verification", text);
        Assert.Contains("😀", text);

        var visibleRanges = textPattern.GetVisibleRanges();
        Assert.NotEmpty(visibleRanges);
        Assert.Contains(
            visibleRanges,
            range => range.GetText(-1).Length > 0);

        var rectangles = visibleRanges
            .SelectMany(range => range.GetBoundingRectangles())
            .ToArray();
        Assert.NotEmpty(rectangles);
        Assert.True(
            rectangles.Any(rect => rect.Width > 0 && rect.Height > 0),
            $"Expected visible geometry, got [{string.Join(", ", rectangles)}].");

        var editorBounds = editor.Current.BoundingRectangle;
        var firstTextBounds = rectangles
            .Where(rect => rect.Width > 0 && rect.Height > 0)
            .OrderBy(rect => rect.Top)
            .First();
        var topInset = firstTextBounds.Top - editorBounds.Top;
        Assert.InRange(topInset, 0, 64);
    }

    [AzunoteUiFact]
    public void Text_range_can_find_and_select_projected_document_text()
    {
        var editor = _fixture.Editor;
        var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
        var documentRange = textPattern.DocumentRange;
        var targetRange = documentRange.FindText("日本語", false, false);
        Assert.NotNull(targetRange);

        targetRange!.Select();
        var selectedText = AzunoteUiFixture.WaitForSelectionText(
            textPattern,
            expected => string.Equals(expected, "日本語", StringComparison.Ordinal));
        Assert.Equal("日本語", selectedText);
    }
}

public sealed class AzunoteUiFixture : IDisposable
{
    private const string EnabledVariable = "AZUNOTE_UI_TESTS";
    private const string ExecutableVariable = "AZUNOTE_EXE";
    private Process? _process;
    private AutomationElement? _window;

    public AutomationElement Editor
    {
        get
        {
            EnsureStarted();
            return FindRequired(
                _window!,
                AutomationElement.NameProperty,
                "Document editor");
        }
    }

    public static TextPattern WaitForTextPattern(AutomationElement editor)
    {
        return WaitFor(
            () => editor.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)
                && pattern is TextPattern textPattern
                    ? textPattern
                    : null,
            "The projected editor did not expose TextPattern.");
    }

    public static string WaitForDocumentText(
        TextPattern textPattern,
        Func<string, bool> predicate)
    {
        return WaitFor(
            () =>
            {
                try
                {
                    var text = textPattern.DocumentRange.GetText(-1);
                    return predicate(text) ? text : null;
                }
                catch (ElementNotAvailableException)
                {
                    return null;
                }
            },
            "The projected document did not reach the expected text.");
    }

    public static string WaitForSelectionText(
        TextPattern textPattern,
        Func<string, bool> predicate)
    {
        return WaitFor(
            () =>
            {
                try
                {
                    var selection = textPattern.GetSelection();
                    var text = selection.Length == 0
                        ? string.Empty
                        : selection[0].GetText(-1);
                    return predicate(text) ? text : null;
                }
                catch (ElementNotAvailableException)
                {
                    return null;
                }
            },
            "The projected selection did not reach the expected text.");
    }

    public void Dispose()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                if (!_process.WaitForExit(2000) && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _window = null;
        }
    }

    private void EnsureStarted()
    {
        if (_window is not null)
        {
            return;
        }

        RequireEnabled();
        if (!OperatingSystem.IsWindows())
        {
            throw new XunitException("Azunote UI tests require Windows.");
        }

        var executablePath = ResolveExecutablePath();
        var verificationDocument = ResolveVerificationDocument();
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };
        startInfo.ArgumentList.Add(verificationDocument);

        _process = Process.Start(startInfo)
            ?? throw new XunitException($"Could not start Azunote: {executablePath}");

        _window = WaitFor(
            FindWindow,
            "Azunote did not expose a top-level UI Automation window.");
    }

    private AutomationElement? FindWindow()
    {
        if (_process is null || _process.HasExited)
        {
            return null;
        }

        try
        {
            _process.Refresh();
            if (_process.MainWindowHandle != IntPtr.Zero)
            {
                var mainWindow = AutomationElement.FromHandle(_process.MainWindowHandle);
                if (mainWindow is not null && FindEditor(mainWindow) is not null)
                {
                    return mainWindow;
                }
            }

            var windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                Condition.TrueCondition);
            foreach (AutomationElement window in windows)
            {
                try
                {
                    if (window.Current.ProcessId == _process.Id
                        && FindEditor(window) is not null)
                    {
                        return window;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // The window was recreated while the app was starting.
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            // The UI tree is still being created.
        }

        return null;
    }

    private static AutomationElement? FindEditor(AutomationElement window) =>
        window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.NameProperty,
                "Document editor"));

    private static AutomationElement FindRequired(
        AutomationElement root,
        AutomationProperty property,
        string value) =>
        root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(property, value))
        ?? throw new XunitException(
            $"Could not find UI Automation element {property.ProgrammaticName}={value}.");

    private static string ResolveExecutablePath()
    {
        var configured = Environment.GetEnvironmentVariable(ExecutableVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Path.GetFullPath(configured);
            if (File.Exists(path))
            {
                return path;
            }

            throw new XunitException($"AZUNOTE_EXE does not exist: {path}");
        }

        var repositoryRoot = FindRepositoryRoot();
        var defaultPath = Path.Combine(
            repositoryRoot,
            "src",
            "Azunote",
            "bin",
            "Debug",
            "net8.0-windows10.0.19041.0",
            "win-x64",
            "Azunote.exe");
        return File.Exists(defaultPath)
            ? defaultPath
            : throw new XunitException(
                $"Azunote.exe was not found at {defaultPath}. Build Azunote first or set AZUNOTE_EXE.");
    }

    private static string ResolveVerificationDocument()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Azunote.UiTests",
            "TestAssets",
            "verification.txt");
        return File.Exists(path)
            ? path
            : throw new XunitException($"Verification document was not found: {path}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Azunyan.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new XunitException(
            "Could not locate the repository root containing Azunyan.slnx.");
    }

    private static void RequireEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable(EnabledVariable);
        if (!string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new XunitException(
                $"Set {EnabledVariable}=1 to run Windows UI tests on an interactive desktop.");
        }
    }

    private static T WaitFor<T>(Func<T?> action, string message)
        where T : class
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var result = action();
            if (result is not null)
            {
                return result;
            }

            Thread.Sleep(100);
        }

        throw new XunitException(message);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class AzunoteUiFactAttribute : FactAttribute
{
    public AzunoteUiFactAttribute()
    {
        if (!AzunoteUiTestEnvironment.IsEnabled)
        {
            Skip = "Set AZUNOTE_UI_TESTS=1 on an unlocked Windows desktop to run this test.";
        }
    }
}

internal static class AzunoteUiTestEnvironment
{
    public static bool IsEnabled =>
        OperatingSystem.IsWindows()
        && (string.Equals(
                Environment.GetEnvironmentVariable("AZUNOTE_UI_TESTS"),
                "1",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                Environment.GetEnvironmentVariable("AZUNOTE_UI_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase));
}
