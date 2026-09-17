using Xunit;

namespace Azunote.Tests;

public sealed class FileSystemScanPolicyTests
{
    [Fact]
    public void A_path_below_a_hidden_directory_is_skipped()
    {
        using var root = new TemporaryDirectory();
        var repository = root.CreateDirectory(".git", FileAttributes.Hidden);
        var objects = Directory.CreateDirectory(Path.Combine(repository, "objects", "ab")).FullName;
        var objectPath = Path.Combine(objects, "cdef");
        File.WriteAllText(objectPath, string.Empty);

        Assert.True(FileSystemScanPolicy.IsSkipped(objectPath, root.Path));
        Assert.True(FileSystemScanPolicy.IsSkipped(repository, root.Path));
    }

    [Fact]
    public void A_deleted_path_below_a_hidden_directory_is_still_skipped()
    {
        using var root = new TemporaryDirectory();
        var repository = root.CreateDirectory(".git", FileAttributes.Hidden);

        // A deletion names a path that is already gone, which is the shape a
        // checkout produces in bulk.
        Assert.True(FileSystemScanPolicy.IsSkipped(
            Path.Combine(repository, "index.lock"),
            root.Path));
    }

    [Fact]
    public void A_hidden_file_is_skipped_and_an_ordinary_one_is_not()
    {
        using var root = new TemporaryDirectory();
        var hidden = Path.Combine(root.Path, "Hidden.tool.toml");
        var visible = Path.Combine(root.Path, "Visible.tool.toml");
        File.WriteAllText(hidden, string.Empty);
        File.WriteAllText(visible, string.Empty);
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

        Assert.True(FileSystemScanPolicy.IsSkipped(hidden, root.Path));
        Assert.False(FileSystemScanPolicy.IsSkipped(visible, root.Path));
    }

    [Fact]
    public void The_root_itself_is_never_skipped()
    {
        using var root = new TemporaryDirectory();
        File.SetAttributes(root.Path, File.GetAttributes(root.Path) | FileAttributes.Hidden);

        // The settings folder is monitored because it was named, whatever its
        // attributes say.
        Assert.False(FileSystemScanPolicy.IsSkipped(root.Path, root.Path));
        Assert.False(FileSystemScanPolicy.IsSkipped(root.Path + Path.DirectorySeparatorChar, root.Path));
    }

    [Fact]
    public void An_unrelated_or_empty_path_is_not_skipped()
    {
        using var root = new TemporaryDirectory();

        Assert.False(FileSystemScanPolicy.IsSkipped(null, root.Path));
        Assert.False(FileSystemScanPolicy.IsSkipped("   ", root.Path));
        Assert.False(FileSystemScanPolicy.IsSkipped(
            Path.Combine(Path.GetTempPath(), $"azunyan-absent-{Guid.NewGuid():N}", "x.toml"),
            root.Path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() =>
            Path = Directory.CreateDirectory(
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"azunyan-scan-{Guid.NewGuid():N}")).FullName;

        public string Path { get; }

        public string CreateDirectory(string name, FileAttributes attributes)
        {
            var created = Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;
            File.SetAttributes(created, File.GetAttributes(created) | attributes);
            return created;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
