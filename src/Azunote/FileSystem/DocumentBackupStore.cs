using System.Text;

namespace Azunote;

/// <summary>An unsaved document kept by <see cref="DocumentBackupStore"/>.</summary>
public sealed record DocumentBackup(string Id, string? FilePath, string Text);

/// <summary>
/// Keeps the text of unsaved documents in the <c>backups</c> directory so that
/// it can be restored on the next launch. Each backup is one UTF-8 file: a
/// header line, the document path (empty for an untitled document), an empty
/// line, and the text. Files are replaced atomically, so a crash while writing
/// leaves the previous backup in place.
/// </summary>
public sealed class DocumentBackupStore
{
    private const string Header = "Azunote backup 1";
    private const string Extension = ".backup";

    public DocumentBackupStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>Where the backups are kept, inside the settings directory.</summary>
    public static string GetDirectory(string settingsDirectory) =>
        Path.Combine(settingsDirectory, "backups");

    public void Write(DocumentBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        System.IO.Directory.CreateDirectory(Directory);
        var path = GetPath(backup.Id);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            $"{Header}\n{backup.FilePath}\n\n{backup.Text}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void Delete(string id)
    {
        var path = GetPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Reads every backup, oldest first. Files that are not backups are skipped.
    /// </summary>
    public IReadOnlyList<DocumentBackup> ReadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var backups = new List<DocumentBackup>();
        foreach (var file in new DirectoryInfo(Directory)
            .EnumerateFiles("*" + Extension)
            .OrderBy(file => file.LastWriteTimeUtc))
        {
            var content = File.ReadAllText(file.FullName, Encoding.UTF8);
            var headerEnd = content.IndexOf('\n');
            var pathEnd = headerEnd < 0 ? -1 : content.IndexOf('\n', headerEnd + 1);
            if (headerEnd < 0
                || pathEnd < 0
                || content[..headerEnd] != Header
                || pathEnd + 1 >= content.Length
                || content[pathEnd + 1] != '\n')
            {
                continue;
            }

            var filePath = content[(headerEnd + 1)..pathEnd];
            backups.Add(new DocumentBackup(
                Path.GetFileNameWithoutExtension(file.Name),
                filePath.Length == 0 ? null : filePath,
                content[(pathEnd + 2)..]));
        }

        return backups;
    }

    private string GetPath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("A backup id must be a valid file name.", nameof(id));
        }

        return Path.Combine(Directory, id + Extension);
    }
}
