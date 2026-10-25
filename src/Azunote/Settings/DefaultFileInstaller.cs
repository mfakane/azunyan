using System.Security.Cryptography;
using System.Text;
using Tomlyn;

namespace Azunote;

/// <summary>
/// The default content Azunote last offered for each path below the tools or
/// modes directory, as a hash of the text.
/// </summary>
public sealed class DefaultFilesRecord
{
    public Dictionary<string, string> Offered { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Installs the default tools and mode definitions and keeps them current
/// without overwriting the user's work. A file that still holds the content
/// last offered for its path is updated with the bundle; a changed file is left
/// alone; a file that was offered and is gone was deleted by the user and stays
/// deleted.
/// </summary>
internal static class DefaultFileInstaller
{
    public const string ToolsRecordFileName = "default-tools.toml";
    public const string ModesRecordFileName = "default-modes.toml";

    // The versions released before Azunote kept the record, by path, for the
    // files they installed: such a file still holding one of these was never
    // changed, so it is updated. Versions released with the record are tracked
    // by it and need no entry here.
    private static readonly Dictionary<string, string[]> EarlierToolHashes = new(StringComparer.Ordinal)
    {
        ["Edit/SortSelectedLines.tool.toml"] =
        [
            "45a8502921ac781661681ae28def8d0e52cfd83b00c89df8943378a363e601c2"
        ],
        ["Git/ShowFileDiff.tool.toml"] =
        [
            "688b8c5ec6ea4346e7caa34e895ad27eeced7f697cbe655b07f9cda362d29c20"
        ],
        ["Format/Whitespace.tool.toml"] =
        [
            "ba62145555e0b7995ef7ad60219c4b02e92290057834096a72ea4eeb4ee821e8"
        ],
        ["Format/Prettier.tool.toml"] =
        [
            "0dded6ebe3090095375e9f92ff3b9667f357275f4296634565e816062ffa299a",
            "0c7ae3baebb728271021164c040e71818e70a957ae6b04aaa284127aa2db55a4",
            "cdd77ca11dd481e2aa4771a721ad9e3c9c8f58c8209451261c6e31afcc842248",
            "e1ec8edb7637080fc5b3c1fda2ae3fcc39f83d3a57e534be0e86905e1996bf5f",
            "24eba7ea56b642ea23dc550d2d195b37a20e4b038aed9ba8ac6b6bb81afe7126",
            "9c81c33875f4055f20764d227014307a44f4f1c52c032aa1685148370f6ca6c8"
        ]
    };

    public static Task InstallToolsAsync(
        string settingsDirectory,
        string toolsDirectoryName,
        CancellationToken cancellationToken) =>
        InstallAsync(
            settingsDirectory,
            toolsDirectoryName,
            ToolsRecordFileName,
            "Default tools",
            EarlierToolHashes,
            cancellationToken);

    public static Task InstallModesAsync(
        string settingsDirectory,
        string modesDirectoryName,
        CancellationToken cancellationToken) =>
        InstallAsync(
            settingsDirectory,
            modesDirectoryName,
            ModesRecordFileName,
            "Default language mode definitions",
            new Dictionary<string, string[]>(),
            cancellationToken);

    private static async Task InstallAsync(
        string settingsDirectory,
        string directoryName,
        string recordFileName,
        string recordDescription,
        IReadOnlyDictionary<string, string[]> earlierVersionHashes,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(settingsDirectory, directoryName);
        var recordPath = Path.Combine(settingsDirectory, recordFileName);
        var previous = File.Exists(recordPath)
            ? (await SettingsFileService.DeserializeAsync(
                recordPath,
                AzunoteTomlSerializerContext.Default.DefaultFilesRecord,
                cancellationToken))?.Offered ?? []
            : [];
        var offered = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var relativePath in BundledDefaultAppData.GetFiles(directoryName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bundled = await ReadBundledAsync(Path.Combine(directoryName, relativePath), cancellationToken);
            if (bundled is null)
            {
                continue;
            }

            var key = relativePath.Replace('\\', '/');
            var bundledHash = Hash(bundled);
            var destinationPath = Path.Combine(directory, relativePath);
            var currentHash = File.Exists(destinationPath)
                ? Hash(await File.ReadAllBytesAsync(destinationPath, cancellationToken))
                : null;
            var install = currentHash is null
                ? !previous.ContainsKey(key)
                : currentHash != bundledHash
                    && (previous.TryGetValue(key, out var offeredHash)
                        ? currentHash == offeredHash
                        : earlierVersionHashes.GetValueOrDefault(key)?.Contains(currentHash) == true);
            if (install)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await File.WriteAllBytesAsync(destinationPath, bundled, cancellationToken);
            }

            offered[key] = bundledHash;
        }

        if (offered.Count != previous.Count
            || offered.Any(entry => previous.GetValueOrDefault(entry.Key) != entry.Value))
        {
            var toml = TomlSerializer.Serialize(
                new DefaultFilesRecord { Offered = offered },
                AzunoteTomlSerializerContext.Default.DefaultFilesRecord);
            await File.WriteAllTextAsync(
                recordPath,
                $"# {recordDescription} Azunote has offered. This file is managed automatically.\n" + toml,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }
    }

    private static async Task<byte[]?> ReadBundledAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using var source = BundledDefaultAppData.OpenRead(relativePath);
        if (source is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>
    /// Hashes the text with LF line endings and no BOM, so the same file hashes
    /// alike whichever line endings the build or an editor gave the file.
    /// </summary>
    internal static string Hash(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
