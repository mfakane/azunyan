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
        Current = settings;
        return settings;
    }
}
