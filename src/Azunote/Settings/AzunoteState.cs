using System.Text;
using Tomlyn;
using Tomlyn.Serialization;

namespace Azunote;

/// <summary>
/// Data that Azunote changes while it is running. Unlike
/// <see cref="AzunoteSettings"/>, this file is application-owned and should
/// not be edited by users.
/// </summary>
public sealed class AzunoteState
{
    public WindowLayoutState Window { get; set; } = new();

    public string[] RecentFiles { get; set; } = [];

    public bool WordWrap { get; set; }

    public bool StatusBarVisible { get; set; } = true;
}

public sealed class WindowLayoutState
{
    public const int DefaultWidth = 1024;
    public const int DefaultHeight = 768;
    public const int MinimumWidth = 320;
    public const int MinimumHeight = 200;

    public int Width { get; set; } = DefaultWidth;

    public int Height { get; set; } = DefaultHeight;
}

internal static class AzunoteStateNormalization
{
    public const int MaximumRecentFiles = 20;

    public static AzunoteState Normalize(AzunoteState? state)
    {
        state ??= new AzunoteState();
        state.Window ??= new WindowLayoutState();
        if (state.Window.Width < WindowLayoutState.MinimumWidth)
        {
            state.Window.Width = WindowLayoutState.DefaultWidth;
        }

        if (state.Window.Height < WindowLayoutState.MinimumHeight)
        {
            state.Window.Height = WindowLayoutState.DefaultHeight;
        }

        state.RecentFiles = state.RecentFiles?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumRecentFiles)
            .ToArray() ?? [];
        return state;
    }

    public static AzunoteState Clone(AzunoteState state) =>
        new()
        {
            Window = new WindowLayoutState
            {
                Width = state.Window.Width,
                Height = state.Window.Height
            },
            RecentFiles = [.. state.RecentFiles],
            WordWrap = state.WordWrap,
            StatusBarVisible = state.StatusBarVisible
        };
}

public static class StateFileService
{
    public const string StateFileName = "state.toml";

    public static string GetStateFilePath(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        return Path.Combine(Path.GetFullPath(settingsDirectory), StateFileName);
    }

    public static async Task EnsureExistsAsync(
        string settingsDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = GetStateFilePath(settingsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            await SaveAsync(settingsDirectory, new AzunoteState(), cancellationToken);
        }
    }

    public static async Task<AzunoteState> LoadAsync(
        string settingsDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = GetStateFilePath(settingsDirectory);
        if (!File.Exists(path))
        {
            return new AzunoteState();
        }

        var state = await SettingsFileService.DeserializeAsync(
            path,
            AzunoteTomlSerializerContext.Default.AzunoteState,
            cancellationToken);
        return AzunoteStateNormalization.Normalize(state);
    }

    public static async Task SaveAsync(
        string settingsDirectory,
        AzunoteState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var path = GetStateFilePath(settingsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var toml = TomlSerializer.Serialize(
            AzunoteStateNormalization.Normalize(state),
            AzunoteTomlSerializerContext.Default.AzunoteState);
        await File.WriteAllTextAsync(
            path,
            "# Azunote application state. This file is managed automatically.\n" + toml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }
}

