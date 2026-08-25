using Xunit;

namespace Azunote.Tests;

public sealed class FileDialogFilterTests
{
    [Fact]
    public void Specification_converts_extensions_to_native_patterns()
    {
        var filter = new FileDialogFilter("csharp", "C#", [".cs", ".csx"]);

        Assert.Equal("*.cs;*.csx", filter.Specification);
    }

    [Fact]
    public void Empty_or_wildcard_extensions_use_all_files_pattern()
    {
        Assert.Equal(
            "*.*",
            new FileDialogFilter("plain-text", "Plain Text", []).Specification);
        Assert.Equal(
            "*.*",
            new FileDialogFilter("all", "All files", ["*"]).Specification);
    }
}
