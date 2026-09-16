using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.CommandLine.Parsing;

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

/// <summary>
/// The grammar shared by the editor and its console client, described with
/// System.CommandLine so that the parser, the help text, and shell completion
/// all come from one definition.
/// </summary>
/// <remarks>
/// Two short forms predate this grammar and cannot be written as options:
/// <c>+N[:M]</c> and a bare <c>-</c>. They are rewritten into the options they
/// stand for before parsing, which keeps them working without teaching the
/// rest of the grammar about them. Tokens after <c>--</c> are left alone.
/// </remarks>
public static class AzunoteCommandLine
{
    private const int HelpWidth = 100;

    private static readonly string[] ExtraUsageLines =
    [
        "Short forms:",
        "  +N[:M]        Open at one-based line N and optional column M.",
        "  -             Read the document from standard input.",
        "  --            Treat the remaining argument as a document path.",
    ];

    public static AzunoteCommandLineOptions Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var tokens = RewriteShortForms(arguments.ToArray(), out var rewriteError);
        if (rewriteError is not null)
        {
            throw new CommandLineParseException(rewriteError);
        }

        var grammar = CommandLineGrammar.Create();
        var result = grammar.Command.Parse(tokens);
        if (result.Errors.Count > 0)
        {
            throw new CommandLineParseException(
                string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        }

        return Bind(grammar, result, tokens);
    }

    /// <summary>
    /// Answers the shell completion request carried by the <c>[suggest]</c>
    /// directive, and reports whether the command line was one. The console
    /// client asks first, because a completion request is answered here and is
    /// never forwarded to the editor.
    /// </summary>
    public static bool TryCompleteArguments(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var grammar = CommandLineGrammar.Create();
        var result = grammar.Command.Parse(arguments);
        if (result.GetResult(grammar.Suggest) is null)
        {
            exitCode = 0;
            return false;
        }

        exitCode = result.Invoke(new InvocationConfiguration
        {
            Output = output,
            Error = error
        });
        return true;
    }

    /// <summary>
    /// The help text, rendered at a fixed width so that the console client and
    /// the editor's dialog show the same lines.
    /// </summary>
    public static string Usage => RenderUsage();

    private static string RenderUsage()
    {
        var grammar = CommandLineGrammar.Create();
        using var writer = new StringWriter();
        grammar.Command.Parse(["--help"]).Invoke(new InvocationConfiguration
        {
            Output = writer,
            Error = writer
        });
        return string.Join(
            Environment.NewLine,
            writer.ToString().TrimEnd(),
            string.Empty,
            string.Join(Environment.NewLine, ExtraUsageLines));
    }

    private static AzunoteCommandLineOptions Bind(
        CommandLineGrammar grammar,
        ParseResult result,
        IReadOnlyList<string> tokens)
    {
        // A positional value is the document path, so System.CommandLine has no
        // reason to reject an unknown option. Everything before a "--" that
        // looks like one still is one.
        var path = result.GetValue(grammar.Path);
        if (path is not null
            && path.StartsWith('-')
            && !tokens.Contains("--", StringComparer.Ordinal))
        {
            throw new CommandLineParseException($"Unknown option: {path}");
        }

        var readStandardInput = result.GetValue(grammar.StandardInput);
        if (path is not null && readStandardInput)
        {
            throw new CommandLineParseException(
                "A document path and standard input cannot be used together.");
        }

        var output = result.GetValue(grammar.Output);
        return new AzunoteCommandLineOptions
        {
            FilePath = path,
            Line = result.GetResult(grammar.Line) is null ? null : result.GetValue(grammar.Line),
            Column = result.GetResult(grammar.Column) is null ? null : result.GetValue(grammar.Column),
            ReadStandardInput = readStandardInput,
            ShowHelp = result.GetResult(grammar.Help) is not null,
            Output = output,
            // Output can only be produced once the document closes, so asking
            // for it always waits.
            WaitForExit = result.GetValue(grammar.Wait) || output != CommandLineOutputTarget.None
        };
    }

    /// <summary>
    /// Expands <c>+N[:M]</c> and a bare <c>-</c> into the options they stand
    /// for. Tokens after <c>--</c> are a document path and are left untouched.
    /// </summary>
    private static string[] RewriteShortForms(string[] arguments, out string? error)
    {
        error = null;
        var rewritten = new List<string>(arguments.Length);
        var optionsAllowed = true;
        foreach (var argument in arguments)
        {
            if (!optionsAllowed)
            {
                rewritten.Add(argument);
                continue;
            }

            if (argument == "--")
            {
                optionsAllowed = false;
                rewritten.Add(argument);
                continue;
            }

            if (argument == "-")
            {
                rewritten.Add("--stdin");
                continue;
            }

            if (argument.Length < 2 || argument[0] != '+' || argument[1] is < '0' or > '9')
            {
                rewritten.Add(argument);
                continue;
            }

            var parts = argument[1..].Split(':');
            if (parts.Length > 2 || Array.Exists(parts, part => !int.TryParse(part, out _)))
            {
                error = $"Invalid document position: {argument}";
                return arguments;
            }

            rewritten.Add("--line");
            rewritten.Add(parts[0]);
            if (parts.Length == 2)
            {
                rewritten.Add("--column");
                rewritten.Add(parts[1]);
            }
        }

        return [.. rewritten];
    }

    /// <summary>
    /// One instance of the grammar. Each caller builds its own, so that help
    /// rendering and completion cannot observe another parse in progress.
    /// </summary>
    private sealed class CommandLineGrammar
    {
        private static readonly Dictionary<string, CommandLineOutputTarget> OutputTargetNames =
            new(StringComparer.Ordinal)
            {
                ["none"] = CommandLineOutputTarget.None,
                ["filePath"] = CommandLineOutputTarget.FilePath,
                ["document"] = CommandLineOutputTarget.Document,
                ["selection"] = CommandLineOutputTarget.Selection
            };

        private CommandLineGrammar(
            RootCommand command,
            SuggestDirective suggest,
            Argument<string> path,
            Option<bool> wait,
            Option<bool> standardInput,
            HelpOption help,
            Option<int> line,
            Option<int> column,
            Option<CommandLineOutputTarget> output)
        {
            Command = command;
            Suggest = suggest;
            Path = path;
            Wait = wait;
            StandardInput = standardInput;
            Help = help;
            Line = line;
            Column = column;
            Output = output;
        }

        public RootCommand Command { get; }

        public SuggestDirective Suggest { get; }

        public Argument<string> Path { get; }

        public Option<bool> Wait { get; }

        public Option<bool> StandardInput { get; }

        public HelpOption Help { get; }

        public Option<int> Line { get; }

        public Option<int> Column { get; }

        public Option<CommandLineOutputTarget> Output { get; }

        public static CommandLineGrammar Create()
        {
            var path = new Argument<string>("path")
            {
                Arity = ArgumentArity.ZeroOrOne,
                Description = "Open one document path."
            };
            var wait = new Option<bool>("--wait", "-w")
            {
                Description = "Wait until the opened document window closes."
            };
            var standardInput = new Option<bool>("--stdin")
            {
                Description = "Read the document from standard input."
            };
            var line = CreatePositionOption("--line", "-l", "line", "Open at one-based line N.");
            var column = CreatePositionOption(
                "--column",
                "-c",
                "column",
                "Open at one-based column N.");
            var output = new Option<CommandLineOutputTarget>("--output", "-o")
            {
                Description = "Write this to standard output when the document closes, "
                    + "and wait for it.",
                CustomParser = ParseOutputTarget
            };
            output.CompletionSources.Clear();
            output.CompletionSources.Add([.. OutputTargetNames.Keys]);

            var command = new RootCommand("Azunote, a Windows text editor.")
            {
                path,
                wait,
                standardInput,
                line,
                column,
                output
            };
            // The editor reports its version in its about dialog, and the
            // command line has nothing of its own to add.
            command.Options.Remove(command.Options.OfType<VersionOption>().Single());
            var help = command.Options.OfType<HelpOption>().Single();
            help.Action = new HelpAction { MaxWidth = HelpWidth };

            return new CommandLineGrammar(
                command,
                command.Directives.OfType<SuggestDirective>().Single(),
                path,
                wait,
                standardInput,
                help,
                line,
                column,
                output);
        }

        private static Option<int> CreatePositionOption(
            string name,
            string alias,
            string label,
            string description) =>
            new(name, alias)
            {
                Description = description,
                HelpName = "N",
                CustomParser = result =>
                {
                    var value = result.Tokens[0].Value;
                    if (!int.TryParse(value, out var parsed) || parsed < 1)
                    {
                        result.AddError($"The {label} must be a positive integer: {value}");
                        return 0;
                    }

                    return parsed;
                }
            };

        private static CommandLineOutputTarget ParseOutputTarget(ArgumentResult result)
        {
            var value = result.Tokens[0].Value;
            if (OutputTargetNames.TryGetValue(value, out var target))
            {
                return target;
            }

            result.AddError(
                $"The output must be none, filePath, document, or selection: {value}");
            return CommandLineOutputTarget.None;
        }
    }
}
