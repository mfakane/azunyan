using Xunit;

namespace Azunote.Tests.FileSystem;

public sealed class DocumentBackupStoreTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("azunote-backups-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Backups_round_trip_paths_and_text_exactly()
    {
        var store = new DocumentBackupStore(Path.Combine(_directory.FullName, "backups"));

        store.Write(new DocumentBackup("a", @"C:\docs\note.txt", "日本語\r\n\nline\r"));
        store.Write(new DocumentBackup("b", null, ""));

        var backups = store.ReadAll();
        Assert.Contains(new DocumentBackup("a", @"C:\docs\note.txt", "日本語\r\n\nline\r"), backups);
        Assert.Contains(new DocumentBackup("b", null, ""), backups);
    }

    [Fact]
    public void Writing_replaces_and_deleting_removes_a_backup()
    {
        var store = new DocumentBackupStore(_directory.FullName);

        store.Write(new DocumentBackup("a", null, "first"));
        store.Write(new DocumentBackup("a", null, "second"));
        Assert.Equal("second", Assert.Single(store.ReadAll()).Text);

        store.Delete("a");
        store.Delete("missing");
        Assert.Empty(store.ReadAll());
        Assert.Empty(_directory.GetFiles());
    }

    [Fact]
    public void Unrecognized_files_and_a_missing_directory_are_ignored()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "other.backup"), "not a backup");
        File.WriteAllText(Path.Combine(_directory.FullName, "note.txt"), "Azunote backup 1\n\n\ntext");

        Assert.Empty(new DocumentBackupStore(_directory.FullName).ReadAll());
        Assert.Empty(new DocumentBackupStore(Path.Combine(_directory.FullName, "none")).ReadAll());
    }

    [Fact]
    public void Tracker_backs_up_only_while_the_document_is_dirty()
    {
        var store = new DocumentBackupStore(_directory.FullName);
        var session = new DocumentSession();
        var text = "draft";
        using var tracker = new DocumentBackupTracker(store, session, () => text);

        tracker.Flush();
        Assert.Empty(store.ReadAll());

        session.SetUntitled(text, TextEncodingKind.Utf8, LineEndingKind.CrLf);
        tracker.Flush();
        Assert.Equal(new DocumentBackup(session.BackupId, null, "draft"), Assert.Single(store.ReadAll()));

        session.MarkSaved(Path.Combine(_directory.FullName, "draft.txt"), text, TextEncodingKind.Utf8, LineEndingKind.CrLf);
        Assert.Empty(store.ReadAll());
    }

    [Fact]
    public void Disabled_tracker_removes_its_backup_and_writes_no_more()
    {
        var store = new DocumentBackupStore(_directory.FullName);
        var session = new DocumentSession();
        session.SetUntitled("piped", TextEncodingKind.Utf8, LineEndingKind.CrLf);
        using var tracker = new DocumentBackupTracker(store, session, () => "piped");
        tracker.Flush();

        tracker.Disable();
        tracker.Flush();

        Assert.False(tracker.IsEnabled);
        Assert.Empty(store.ReadAll());
    }
}
