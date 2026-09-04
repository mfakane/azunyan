using Azunyan.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Azunote;

/// <summary>
/// Supplies Azunyan's palette from WinUI system theme resources.
/// </summary>
internal static class AzunoteSystemColorScheme
{
    public static AzunyanColorScheme Create(ElementTheme theme)
    {
        var isDark = theme == ElementTheme.Dark;
        var background = GetColor(
            "TextControlBackgroundFocused",
            isDark
                ? Color.FromArgb(0xff, 0x1f, 0x1f, 0x1f)
                : Color.FromArgb(0xff, 0xff, 0xff, 0xff));
        var foreground = GetColor(
            "TextControlForegroundFocused",
            isDark
                ? Color.FromArgb(0xff, 0xf5, 0xf5, 0xf5)
                : Color.FromArgb(0xff, 0x1a, 0x1a, 0x1a));
        var mutedForeground = GetColor(
            "SystemControlForegroundBaseMediumBrush",
            isDark
                ? Color.FromArgb(0xff, 0xb3, 0xb3, 0xb3)
                : Color.FromArgb(0xff, 0x60, 0x60, 0x60));
        var accent = GetColor(
            "SystemControlForegroundAccentBrush",
            Color.FromArgb(0xff, 0x00, 0x66, 0xcc));
        var selection = GetColor(
            "TextControlSelectionHighlightColor",
            Color.FromArgb(0x66, 0x00, 0x66, 0xcc));
        var opaqueSelection = Color.FromArgb(
            0xff,
            selection.R,
            selection.G,
            selection.B);
        var popupBackground = GetColor(
            "SystemControlBackgroundChromeMediumLowBrush",
            isDark
                ? Color.FromArgb(0xff, 0x2b, 0x2b, 0x2b)
                : background);
        var border = GetColor(
            "SystemControlForegroundBaseMediumLowBrush",
            isDark
                ? Color.FromArgb(0xff, 0x9a, 0x9a, 0x9a)
                : Color.FromArgb(0xff, 0x80, 0x80, 0x80));

        return new AzunyanColorScheme
        {
            EditorBackground = background,
            EditorForeground = foreground,
            GutterBackground = background,
            GutterForeground = mutedForeground,
            SelectionBackground = opaqueSelection,
            SelectionForeground = Color.FromArgb(0xff, 0xff, 0xff, 0xff),
            CaretForeground = foreground,
            CompositionForeground = accent,
            FoldForeground = mutedForeground,
            InlayForeground = mutedForeground,
            HeadingForeground = accent,
            KeywordForeground = ThemeColor(
                isDark,
                Color.FromArgb(0xff, 0x7a, 0x3e, 0x9d),
                Color.FromArgb(0xff, 0xc5, 0x86, 0xc0)),
            StringForeground = ThemeColor(
                isDark,
                Color.FromArgb(0xff, 0xa3, 0x15, 0x15),
                Color.FromArgb(0xff, 0xce, 0x91, 0x78)),
            NumberForeground = ThemeColor(
                isDark,
                Color.FromArgb(0xff, 0x09, 0x86, 0x58),
                Color.FromArgb(0xff, 0xb5, 0xce, 0xa8)),
            CommentForeground = mutedForeground,
            TaskForeground = ThemeColor(
                isDark,
                Color.FromArgb(0xff, 0xc2, 0x41, 0x0c),
                Color.FromArgb(0xff, 0xd7, 0xba, 0x7d)),
            CodeForeground = ThemeColor(
                isDark,
                Color.FromArgb(0xff, 0x26, 0x7f, 0x99),
                Color.FromArgb(0xff, 0x4e, 0xc9, 0xb0)),
            VariableForeground = ThemeColor(
                isDark,
                Color.FromArgb(0xff, 0x79, 0x5e, 0x26),
                Color.FromArgb(0xff, 0x9c, 0xdc, 0xfe)),
            PopupBackground = popupBackground,
            PopupForeground = foreground,
            PopupBorder = border,
            TooltipBackground = popupBackground,
            TooltipForeground = foreground,
            TooltipBorder = border
        };
    }

    private static Color ThemeColor(bool isDark, Color light, Color dark) =>
        isDark ? dark : light;

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
