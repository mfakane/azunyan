namespace Azunote;

/// <summary>
/// The small command-line contract used by the Azunote executable. Line and
/// column are one-based because that is the convention used by external
/// editor integrations; the editor converts them to its zero-based model.
/// </summary>
public sealed record AzunoteCommandLineOptions
{
    public string? FilePath { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public bool WaitForExit { get; init; }

    public bool ReadStandardInput { get; init; }

    public bool ShowHelp { get; init; }
}

public sealed class CommandLineParseException : ArgumentException
{
    public CommandLineParseException(string message)
        : base(message)
    {
    }
}

public static class AzunoteCommandLine
{
    public static AzunoteCommandLineOptions Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var values = arguments.ToArray();
        var options = new AzunoteCommandLineOptions();
        var positionalsAllowed = true;

        for (var index = 0; index < values.Length; index++)
        {
            var argument = values[index];
            if (positionalsAllowed && argument == "--")
            {
                positionalsAllowed = false;
                continue;
            }

            if (positionalsAllowed && (argument == "-h" || argument == "--help"))
            {
                options = options with { ShowHelp = true };
                continue;
            }

            if (positionalsAllowed && (argument == "-w" || argument == "--wait"))
            {
                options = options with { WaitForExit = true };
                continue;
            }

            if (positionalsAllowed && (argument == "--stdin" || argument == "-"))
            {
                options = options with { ReadStandardInput = true };
                continue;
            }

            if (positionalsAllowed && TryReadOptionValue(argument, "--line", out var lineValue))
            {
                options = options with { Line = ParsePositiveValue("line", lineValue) };
                continue;
            }

            if (positionalsAllowed && argument == "--line")
            {
                options = options with { Line = ParseFollowingValue("line", values, ref index) };
                continue;
            }

            if (positionalsAllowed && TryReadOptionValue(argument, "--column", out var columnValue))
            {
                options = options with { Column = ParsePositiveValue("column", columnValue) };
                continue;
            }

            if (positionalsAllowed && argument == "--column")
            {
                options = options with { Column = ParseFollowingValue("column", values, ref index) };
                continue;
            }

            if (positionalsAllowed && (argument == "-l" || argument == "-c"))
            {
                if (++index >= values.Length)
                {
                    throw new CommandLineParseException($"Missing value for {argument}.");
                }

                var name = argument == "-l" ? "line" : "column";
                var value = ParsePositiveValue(name, values[index]);
                options = argument == "-l"
                    ? options with { Line = value }
                    : options with { Column = value };
                continue;
            }

            if (positionalsAllowed && TryParseGoto(argument, out var gotoLine, out var gotoColumn))
            {
                options = options with { Line = gotoLine, Column = gotoColumn };
                continue;
            }

            if (positionalsAllowed && argument.StartsWith('-'))
            {
                throw new CommandLineParseException($"Unknown option: {argument}");
            }

            if (options.FilePath is not null)
            {
                throw new CommandLineParseException(
                    "Only one document path can be supplied.");
            }

            options = options with { FilePath = argument };
        }

        if (options.FilePath is not null && options.ReadStandardInput)
        {
            throw new CommandLineParseException(
                "A document path and standard input cannot be used together.");
        }

        return options;
    }

    public static string Usage =>
        "Usage: Azunote [--wait] [--line N] [--column N] [--stdin | path]";

    private static bool TryReadOptionValue(
        string argument,
        string option,
        out string value)
    {
        var prefix = option + "=";
        if (argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = argument[prefix.Length..];
            return true;
        }

        if (argument == option)
        {
            value = string.Empty;
            return false;
        }

        value = string.Empty;
        return false;
    }

    private static int ParsePositiveValue(string name, string value)
    {
        if (!int.TryParse(value, out var parsed) || parsed < 1)
        {
            throw new CommandLineParseException(
                $"The {name} must be a positive integer: {value}");
        }

        return parsed;
    }

    private static int ParseFollowingValue(
        string name,
        string[] values,
        ref int index)
    {
        if (++index >= values.Length)
        {
            throw new CommandLineParseException($"Missing value for --{name}.");
        }

        return ParsePositiveValue(name, values[index]);
    }

    private static bool TryParseGoto(
        string argument,
        out int line,
        out int? column)
    {
        line = 0;
        column = null;
        if (argument.Length < 2 || argument[0] != '+' || argument[1] is < '0' or > '9')
        {
            return false;
        }

        var parts = argument[1..].Split(':', StringSplitOptions.None);
        if (parts.Length is < 1 or > 2
            || !int.TryParse(parts[0], out line)
            || line < 1)
        {
            throw new CommandLineParseException($"Invalid document position: {argument}");
        }

        if (parts.Length == 2)
        {
            column = ParsePositiveValue("column", parts[1]);
        }

        return true;
    }
}
