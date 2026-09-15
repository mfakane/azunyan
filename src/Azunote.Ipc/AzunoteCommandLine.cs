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

    /// <summary>
    /// What the command line asks the editor to write to standard output when
    /// the document window closes. The vocabulary matches the external-tool
    /// <c>input</c> field so that both sides name the same values.
    /// </summary>
    public CommandLineOutputTarget Output { get; init; }
}

/// <summary>
/// The value selected by <c>--output</c>. The names mirror the external-tool
/// <c>[launch].input</c> values.
/// </summary>
public enum CommandLineOutputTarget
{
    None,
    FilePath,
    Document,
    Selection
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
    private static readonly string[] UsageLines =
    [
        "Usage: Azunote [options] [path]",
        "",
        "Options:",
        "  -h, --help              Show this help and exit.",
        "  -w, --wait              Wait until the opened document window closes.",
        "  -l, --line N            Open at one-based line N.",
        "      --line=N            Equivalent form of --line N.",
        "  -c, --column N          Open at one-based column N.",
        "      --column=N          Equivalent form of --column N.",
        "      --stdin, -          Read the document from standard input.",
        "  -o, --output TARGET     Write TARGET to standard output when the",
        "                          document closes, and wait for it. TARGET is",
        "                          none, filePath, document, or selection.",
        "      --output=TARGET     Equivalent form of --output TARGET.",
        "      +N[:M]              Open at one-based line N and optional column M.",
        "      --                  Treat remaining arguments as a document path.",
        "  path                    Open one document path.",
    ];

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

            if (positionalsAllowed && TryReadOptionValue(argument, "--output", out var outputValue))
            {
                options = options with { Output = ParseOutputTarget(outputValue) };
                continue;
            }

            if (positionalsAllowed && (argument == "--output" || argument == "-o"))
            {
                if (++index >= values.Length)
                {
                    throw new CommandLineParseException($"Missing value for {argument}.");
                }

                options = options with { Output = ParseOutputTarget(values[index]) };
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

        // Output can only be produced once the document closes, so asking for
        // it always waits.
        return options.Output == CommandLineOutputTarget.None
            ? options
            : options with { WaitForExit = true };
    }

    private static CommandLineOutputTarget ParseOutputTarget(string value) => value switch
    {
        "none" => CommandLineOutputTarget.None,
        "filePath" => CommandLineOutputTarget.FilePath,
        "document" => CommandLineOutputTarget.Document,
        "selection" => CommandLineOutputTarget.Selection,
        _ => throw new CommandLineParseException(
            $"The output must be none, filePath, document, or selection: {value}")
    };

    public static string Usage => string.Join(Environment.NewLine, UsageLines);

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
