using System.Text.RegularExpressions;
using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>Common lexical language definitions supplied with Azunyan.</summary>
public static class BuiltInSyntaxLanguages
{
    private const string CommonNumberPattern =
        @"\b(?:0[xX][0-9a-fA-F](?:_?[0-9a-fA-F])*|0[bB][01](?:_?[01])*|\d(?:_?\d)*(?:\.\d(?:_?\d)*)?(?:[eE][+-]?\d(?:_?\d)*)?)";

    public static SyntaxLanguageDefinition CSharp => CreateCSharp();

    public static SyntaxLanguageDefinition JavaScript => CreateJavaScript(false);

    public static SyntaxLanguageDefinition TypeScript => CreateJavaScript(true);

    public static SyntaxLanguageDefinition Python => CreatePython();

    public static SyntaxLanguageDefinition Json => CreateJson();

    public static SyntaxLanguageDefinition Markdown => CreateMarkdown();

    public static SyntaxLanguageDefinition PowerShell => CreatePowerShell();

    public static IReadOnlyList<SyntaxLanguageDefinition> All =>
    [
        CSharp,
        JavaScript,
        TypeScript,
        Python,
        Json,
        Markdown,
        PowerShell
    ];

    private static SyntaxLanguageDefinition CreateCSharp()
    {
        var keywords = new[]
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach",
            "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
            "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "record", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
            "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while", "async", "await", "yield"
        };
        return new SyntaxLanguageDefinition("csharp", "C#", [".cs", ".csx"],
        [
            new DelimitedSyntaxRule("/*", "*/", "comment"),
            new LineRemainderSyntaxRule("//", "comment"),
            new DelimitedSyntaxRule("$@\"", "\"", "string", escapedEndToken: "\"\""),
            new DelimitedSyntaxRule("@$\"", "\"", "string", escapedEndToken: "\"\""),
            new DelimitedSyntaxRule("@\"", "\"", "string", escapedEndToken: "\"\""),
            new DelimitedSyntaxRule("$\"", "\"", "string", allowLineBreaks: false, escapePrefix: "\\"),
            new DelimitedSyntaxRule("\"", "\"", "string", allowLineBreaks: false, escapePrefix: "\\"),
            new DelimitedSyntaxRule("'", "'", "string", allowLineBreaks: false, escapePrefix: "\\"),
            new KeywordSyntaxRule(keywords),
            new RegexSyntaxRule(CommonNumberPattern + @"(?:[uUlLfFdDmM]+)?\b", "number")
        ]);
    }

    private static SyntaxLanguageDefinition CreateJavaScript(bool typeScript)
    {
        var keywords = new List<string>
        {
            "as", "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger",
            "default", "delete", "do", "else", "export", "extends", "false", "finally", "for", "from", "function",
            "get", "if", "import", "in", "instanceof", "let", "new", "null", "of", "return", "set", "static",
            "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while",
            "with", "yield"
        };
        if (typeScript)
        {
            keywords.AddRange(
            [
                "abstract", "any", "boolean", "declare", "enum", "implements", "interface", "keyof", "namespace",
                "never", "number", "private", "protected", "public", "readonly", "string", "symbol", "type", "unknown"
            ]);
        }

        return new SyntaxLanguageDefinition(
            typeScript ? "typescript" : "javascript",
            typeScript ? "TypeScript" : "JavaScript",
            typeScript ? [".ts", ".tsx", ".mts", ".cts"] : [".js", ".jsx", ".mjs", ".cjs"],
            [
                new DelimitedSyntaxRule("/*", "*/", "comment"),
                new LineRemainderSyntaxRule("//", "comment"),
                new DelimitedSyntaxRule("`", "`", "string", escapePrefix: "\\"),
                new DelimitedSyntaxRule("\"", "\"", "string", allowLineBreaks: false, escapePrefix: "\\"),
                new DelimitedSyntaxRule("'", "'", "string", allowLineBreaks: false, escapePrefix: "\\"),
                new KeywordSyntaxRule(keywords),
                new RegexSyntaxRule(CommonNumberPattern + @"n?\b", "number")
            ]);
    }

    private static SyntaxLanguageDefinition CreatePython()
    {
        var keywords = new[]
        {
            "and", "as", "assert", "async", "await", "break", "case", "class", "continue", "def", "del", "elif",
            "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda",
            "match", "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while", "with", "yield"
        };
        var strings = new List<ISyntaxProvider>();
        foreach (var prefix in new[] { "f", "r", "b", "u", "fr", "rf", "br", "rb", string.Empty })
        {
            strings.Add(new DelimitedSyntaxRule(prefix + "\"\"\"", "\"\"\"", "string", escapePrefix: "\\", comparison: StringComparison.OrdinalIgnoreCase));
            strings.Add(new DelimitedSyntaxRule(prefix + "'''", "'''", "string", escapePrefix: "\\", comparison: StringComparison.OrdinalIgnoreCase));
        }

        strings.Add(new DelimitedSyntaxRule("\"", "\"", "string", allowLineBreaks: false, escapePrefix: "\\"));
        strings.Add(new DelimitedSyntaxRule("'", "'", "string", allowLineBreaks: false, escapePrefix: "\\"));
        var sources = new List<ISyntaxProvider> { new LineRemainderSyntaxRule("#", "comment") };
        sources.AddRange(strings);
        sources.Add(new KeywordSyntaxRule(keywords));
        sources.Add(new RegexSyntaxRule(CommonNumberPattern + @"[jJ]?\b", "number"));
        return new SyntaxLanguageDefinition("python", "Python", [".py", ".pyw", ".pyi"], sources);
    }

    private static SyntaxLanguageDefinition CreateJson() =>
        new("json", "JSON", [".json"],
        [
            new DelimitedSyntaxRule("\"", "\"", "string", allowLineBreaks: false, escapePrefix: "\\"),
            new KeywordSyntaxRule(["true", "false", "null"]),
            new RegexSyntaxRule(@"(?<![\w.])-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?(?![\w.])", "number")
        ]);

    private static SyntaxLanguageDefinition CreateMarkdown() =>
        new("markdown", "Markdown", [".md", ".markdown", ".mdown"],
        [
            new DelimitedSyntaxRule("<!--", "-->", "comment"),
            new DelimitedSyntaxRule("```", "```", "code"),
            new DelimitedSyntaxRule("~~~", "~~~", "code"),
            new DelimitedSyntaxRule("`", "`", "code", allowLineBreaks: false),
            new LineRemainderSyntaxRule("###### ", "heading", requireLineStart: true),
            new LineRemainderSyntaxRule("##### ", "heading", requireLineStart: true),
            new LineRemainderSyntaxRule("#### ", "heading", requireLineStart: true),
            new LineRemainderSyntaxRule("### ", "heading", requireLineStart: true),
            new LineRemainderSyntaxRule("## ", "heading", requireLineStart: true),
            new LineRemainderSyntaxRule("# ", "heading", requireLineStart: true)
        ]);

    private static SyntaxLanguageDefinition CreatePowerShell()
    {
        var keywords = new[]
        {
            "begin", "break", "catch", "class", "continue", "data", "define", "do", "dynamicparam", "else",
            "elseif", "end", "enum", "exit", "filter", "finally", "for", "foreach", "from", "function", "if",
            "in", "param", "process", "return", "switch", "throw", "trap", "try", "until", "using", "var", "while",
            "workflow"
        };
        return new SyntaxLanguageDefinition("powershell", "PowerShell", [".ps1", ".psm1", ".psd1"],
        [
            new DelimitedSyntaxRule("<#", "#>", "comment"),
            new LineRemainderSyntaxRule("#", "comment"),
            new DelimitedSyntaxRule("@\"", "\"@", "string", escapePrefix: "`"),
            new DelimitedSyntaxRule("@'", "'@", "string", escapedEndToken: "''"),
            new DelimitedSyntaxRule("\"", "\"", "string", allowLineBreaks: false, escapePrefix: "`"),
            new DelimitedSyntaxRule("'", "'", "string", allowLineBreaks: false, escapedEndToken: "''"),
            new KeywordSyntaxRule(keywords, comparer: StringComparer.OrdinalIgnoreCase),
            new RegexSyntaxRule(@"\$[A-Za-z_?^][\w:?^]*", "variable", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new RegexSyntaxRule(CommonNumberPattern + @"\b", "number")
        ]);
    }
}
