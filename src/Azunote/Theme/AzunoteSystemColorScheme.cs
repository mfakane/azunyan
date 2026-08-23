using Azunyan.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Azunote;

/// <summary>
/// Supplies Azunyan's palette from WinUI system theme resources. The first
/// Azunote palette intentionally targets the light system theme; callers can
/// replace the scheme on <see cref="AzunyanEditorView"/> later.
/// </summary>
internal static class AzunoteSystemColorScheme
{
    public static AzunyanColorScheme CreateLight()
    {
        var background = GetColor(
            "SystemControlBackgroundChromeWhiteBrush",
            Color.FromArgb(0xff, 0xff, 0xff, 0xff));
        var foreground = GetColor(
            "SystemControlForegroundBaseHighBrush",
            Color.FromArgb(0xff, 0x1a, 0x1a, 0x1a));
        var mutedForeground = GetColor(
            "SystemControlForegroundBaseMediumBrush",
            Color.FromArgb(0xff, 0x60, 0x60, 0x60));
        var accent = GetColor(
            "SystemControlForegroundAccentBrush",
            Color.FromArgb(0xff, 0x00, 0x66, 0xcc));
        var selection = GetColor(
            "SystemControlHighlightListAccentLowBrush",
            Color.FromArgb(0x66, 0x00, 0x66, 0xcc));
        var popupBackground = GetColor(
            "SystemControlBackgroundChromeMediumLowBrush",
            background);
        var border = GetColor(
            "SystemControlForegroundBaseMediumLowBrush",
            Color.FromArgb(0xff, 0x80, 0x80, 0x80));

        return new AzunyanColorScheme
        {
            EditorBackground = background,
            EditorForeground = foreground,
            GutterBackground = background,
            GutterForeground = mutedForeground,
            SelectionBackground = selection,
            CaretForeground = foreground,
            CompositionForeground = accent,
            FoldForeground = mutedForeground,
            InlayForeground = mutedForeground,
            HeadingForeground = accent,
            KeywordForeground = accent,
            StringForeground = foreground,
            NumberForeground = foreground,
            CommentForeground = mutedForeground,
            TaskForeground = accent,
            PopupBackground = popupBackground,
            PopupForeground = foreground,
            PopupBorder = border,
            TooltipBackground = popupBackground,
            TooltipForeground = foreground,
            TooltipBorder = border
        };
    }

    private static Color GetColor(string resourceKey, Color fallback)
    {
        var resources = Application.Current?.Resources;
        if (resources is null
            || !resources.TryGetValue(resourceKey, out var value)
            || value is not SolidColorBrush brush)
        {
            return fallback;
        }

        var color = brush.Color;
        var alpha = (byte)Math.Clamp(
            Math.Round(color.A * brush.Opacity),
            0,
            byte.MaxValue);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
