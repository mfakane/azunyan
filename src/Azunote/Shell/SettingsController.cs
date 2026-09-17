namespace Azunote;

/// <summary>
/// Owns the current application settings snapshot and its persistence
/// location. Applying menus and language providers remains a shell concern.
/// </summary>
public sealed class SettingsController
{
    public SettingsController(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    public string Directory { get; }

    public AzunoteSettings Current { get; private set; } = new();

    public Task EnsureExistsAsync(CancellationToken cancellationToken = default) =>
        SettingsFileService.EnsureExistsAsync(Directory, cancellationToken);

    public async Task<AzunoteSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await SettingsFileService.LoadAsync(Directory, cancellationToken);

        // A load that has been superseded must not become the current
        // settings: the one that superseded it read a newer folder.
        cancellationToken.ThrowIfCancellationRequested();
        Current = settings;
        return settings;
    }
}
