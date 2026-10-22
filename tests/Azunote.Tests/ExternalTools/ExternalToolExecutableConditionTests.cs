using Azunote;
using Xunit;

namespace Azunote.Tests.ExternalTools;

public sealed class ExternalToolExecutableConditionTests : IDisposable
{
    private const string Missing = "azunote-missing-executable-4f1c";
    private readonly string _toolDirectory = Path.Combine(Path.GetTempPath(), $"azunote-executable-{Guid.NewGuid():N}");

    public ExternalToolExecutableConditionTests()
    {
        Directory.CreateDirectory(_toolDirectory);
        File.WriteAllText(Path.Combine(_toolDirectory, "azunote-lint.cmd"), "@echo off");
    }

    public void Dispose() => Directory.Delete(_toolDirectory, recursive: true);

    [Theory]
    // An executable on PATH, named without its extension.
    [InlineData(true, "cmd")]
    [InlineData(false, Missing)]
    // ! holds when the executable is missing; | needs one alternative.
    [InlineData(true, "!" + Missing)]
    [InlineData(false, "!cmd")]
    [InlineData(true, Missing + "|cmd")]
    // Every entry of an array must hold.
    [InlineData(false, "cmd", Missing)]
    // * and ? match names, with or without the extension.
    [InlineData(true, "cm?")]
    [InlineData(true, "azunote-li*.cmd")]
    // The tool's own search path is searched as well as PATH.
    [InlineData(true, "azunote-lint")]
    public void A_tool_is_available_only_when_its_executables_are_found(bool expected, params string[] executables)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var state = ExternalToolAvailability.Evaluate(
            new ExternalToolSettings
            {
                Name = "Lint",
                Visibility = "whenAvailable",
                Launch = new ExternalToolLaunchSettings
                {
                    Cmd = new ExternalToolCommand("azunote-lint"),
                    Path = new ExternalToolSearchPath([_toolDirectory])
                },
                When = new ExternalToolWhenSettings { Executable = new ExternalToolSearchPath(executables) }
            },
            new ExternalToolContext(null, "document", string.Empty));

        Assert.Equal(expected, state.IsVisible);
        if (!expected)
        {
            Assert.StartsWith("Executable condition", state.DisabledReason, StringComparison.Ordinal);
        }
    }
}
