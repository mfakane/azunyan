using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Azunote;

internal sealed class LineNumberRenderer : IAzunyanEditorRenderer
{
    public void Render(AzunyanEditorRenderContext context)
    {
        var providerGutter = context.ViewportResults?.Gutter ?? context.ProviderResults?.Gutter;
        if (providerGutter is { Count: > 0 })
        {
            foreach (var item in providerGutter)
            {
                if (item.Line < context.FirstVisibleLine || item.Line > context.LastVisibleLine)
                {
                    continue;
                }

                var content = new TextBlock
                {
                    Text = item.Text,
                    FontFamily = context.FontFamily,
                    FontSize = context.FontSize,
                    Foreground = new SolidColorBrush(context.ColorScheme.GutterForeground),
                    Height = context.LineHeight,
                    TextAlignment = TextAlignment.Right
                };

                Canvas.SetLeft(content, 8);
                Canvas.SetTop(content, context.ContentTop + (item.Line * context.LineHeight) - context.VerticalOffset);
                context.GutterLayer.Children.Add(content);
            }
        }
        else if (context.ShowLineNumbers)
        {
            var lineCount = context.Snapshot.Lines.LineCount;
            var digits = Math.Max(1, lineCount.ToString().Length);
            for (var line = context.FirstVisibleLine; line <= context.LastVisibleLine; line++)
            {
                var number = new TextBlock
                {
                    Text = (line + 1).ToString(),
                    FontFamily = context.FontFamily,
                    FontSize = context.FontSize,
                    Foreground = new SolidColorBrush(context.ColorScheme.GutterForeground),
                    Height = context.LineHeight,
                    TextAlignment = TextAlignment.Right
                };

                Canvas.SetLeft(number, Math.Max(0, context.GutterWidth - 8 - (digits * context.CharacterWidth)));
                Canvas.SetTop(number, context.ContentTop + (line * context.LineHeight) - context.VerticalOffset);
                context.GutterLayer.Children.Add(number);
            }
        }

    }
}
