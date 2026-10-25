using Azunote;
using Xunit;

namespace Azunote.Tests.FileSystem;

public sealed class WorkspaceFolderResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"azunote-workspace-{Guid.NewGuid():N}");

    public WorkspaceFolderResolverTests()
    {
        Directory.CreateDirectory(Path.Combine(Repository, ".git"));
        Directory.CreateDirectory(Path.Combine(Package, "src"));
        File.WriteAllText(Path.Combine(Package, "package.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "Solution.slnx"), "<Solution />");
    }

    private string Repository => Path.Combine(_root, "repo");

    private string Package => Path.Combine(Repository, "packages", "app");

    private string Document => Path.Combine(Package, "src", "index.ts");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Without_patterns_the_repository_is_the_workspace()
    {
        Assert.Equal(Repository, WorkspaceFolderResolver.FindForFile(Document));
    }

    [Fact]
    public void A_mode_pattern_found_first_makes_its_folder_the_workspace()
    {
        Assert.Equal(Package, WorkspaceFolderResolver.FindForFile(Document, null, ["tsconfig.json", "package.json"]));
    }

    [Fact]
    public void A_mode_pattern_above_the_repository_does_not_win_over_it()
    {
        Assert.Equal(Repository, WorkspaceFolderResolver.FindForFile(Document, null, ["*.sln|*.slnx"]));
    }

    [Fact]
    public void Mode_patterns_change_the_workspace_folder_but_not_an_explicit_pattern()
    {
        var context = new ExternalToolContext(
            Document,
            Document,
            string.Empty,
            string.Empty,
            workspacePatterns: ["package.json"]);

        Assert.Equal(Package, context.Expand("${workspaceFolder}"));
        Assert.Equal(_root, context.Expand("${workspaceFolder:*.slnx}"));
        Assert.Equal(Path.Combine("src", "index.ts"), context.Expand("${relativeFile}"));
    }
}
