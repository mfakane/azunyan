using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Azunote;

internal sealed class LineNumberRenderer : IAzunyanEditorRenderer
{
    private static readonly Brush Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x80, 0x80, 0x80));

    public void Render(AzunyanEditorRenderContext context)
    {
        if (!context.ShowLineNumbers)
        {
            return;
        }

        var lineCount = context.Snapshot.Lines.LineCount;
        var digits = Math.Max(1, lineCount.ToString().Length);
        for (var line = context.FirstVisibleLine; line <= context.LastVisibleLine; line++)
        {
            var number = new TextBlock
            {
                Text = (line + 1).ToString(),
                FontFamily = context.FontFamily,
                FontSize = context.FontSize,
                Foreground = Foreground,
                Height = context.LineHeight,
                TextAlignment = TextAlignment.Right
            };

            Canvas.SetLeft(number, Math.Max(0, context.GutterWidth - 8 - (digits * context.CharacterWidth)));
            Canvas.SetTop(number, context.ContentTop + (line * context.LineHeight) - context.VerticalOffset);
            context.GutterLayer.Children.Add(number);
        }
    }
}
