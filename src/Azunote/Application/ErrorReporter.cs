using System.Diagnostics;
using System.Text;

namespace Azunote;

/// <summary>
/// Writes diagnostic entries to a per-user log without allowing logging
/// failures to become a second application failure.
/// </summary>
internal static class ErrorReporter
{
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string LogException(string source, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);

        var path = GetLogPath();
        var entry = $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}"
            + exception
            + Environment.NewLine
            + Environment.NewLine;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, entry, Utf8);
            }
        }
        catch (Exception loggingException)
        {
            Debug.WriteLine($"Azunote logging failed: {loggingException}");
            Debug.WriteLine(entry);
        }

        return path;
    }

    public static string LogMessage(string source, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(message);

        var path = GetLogPath();
        var entry = $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}"
            + message
            + Environment.NewLine
            + Environment.NewLine;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, entry, Utf8);
            }
        }
        catch (Exception loggingException)
        {
            Debug.WriteLine($"Azunote logging failed: {loggingException}");
            Debug.WriteLine(entry);
        }

        return path;
    }

    private static string GetLogPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(
            localAppData,
            "Azunote",
            "logs",
            $"azunote-{DateTime.Now:yyyyMMdd}.log");
    }
}
