using Azunyan.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Azunote;

internal sealed class LineNumberRenderer : IAzunyanEditorRenderer
{
    private static readonly Brush Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x80, 0x80, 0x80));
    private static readonly Brush KeywordForeground = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));
    private static readonly Brush HeadingForeground = new SolidColorBrush(Color.FromArgb(0xFF, 0x7A, 0x3E, 0x9D));
    private static readonly Brush CommentForeground = new SolidColorBrush(Color.FromArgb(0xFF, 0x6A, 0x99, 0x55));
    private static readonly Brush StringForeground = new SolidColorBrush(Color.FromArgb(0xFF, 0xA3, 0x15, 0x15));
    private static readonly Brush NumberForeground = new SolidColorBrush(Color.FromArgb(0xFF, 0x09, 0x6A, 0x95));

    public void Render(AzunyanEditorRenderContext context)
    {
        var providerGutter = context.ProviderResults?.Gutter;
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
                    Foreground = Foreground,
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
                    Foreground = Foreground,
                    Height = context.LineHeight,
                    TextAlignment = TextAlignment.Right
                };

                Canvas.SetLeft(number, Math.Max(0, context.GutterWidth - 8 - (digits * context.CharacterWidth)));
                Canvas.SetTop(number, context.ContentTop + (line * context.LineHeight) - context.VerticalOffset);
                context.GutterLayer.Children.Add(number);
            }
        }

        RenderSyntax(context);
    }

    private static void RenderSyntax(AzunyanEditorRenderContext context)
    {
        if (context.ProviderResults?.Syntax is not { Count: > 0 } spans)
        {
            return;
        }

        var lines = context.Snapshot.Lines;
        foreach (var span in spans)
        {
            var startLine = Math.Max(context.FirstVisibleLine, lines.GetLine(span.Range.Start));
            var endLine = Math.Min(context.LastVisibleLine, lines.GetLine(Math.Min(span.Range.End, context.Snapshot.Length)));
            for (var line = startLine; line <= endLine; line++)
            {
                var lineRange = lines.GetLineRange(line);
                var start = Math.Max(span.Range.Start, lineRange.Start);
                var end = Math.Min(span.Range.End, lineRange.End);
                if (end <= start)
                {
                    continue;
                }

                var token = new TextBlock
                {
                    Text = context.Snapshot.GetText(TextRange.FromBounds(start, end)),
                    FontFamily = context.FontFamily,
                    FontSize = context.FontSize,
                    Foreground = GetSyntaxForeground(span.Classification),
                    Height = context.LineHeight,
                    IsHitTestVisible = false
                };
                var column = start - lineRange.Start;
                Canvas.SetLeft(token, 8 + (column * context.CharacterWidth) - context.HorizontalOffset);
                Canvas.SetTop(token, context.ContentTop + (line * context.LineHeight) - context.VerticalOffset);
                context.TextLayer.Children.Add(token);
            }
        }
    }

    private static Brush GetSyntaxForeground(string classification) => classification switch
    {
        "keyword" => KeywordForeground,
        "heading" => HeadingForeground,
        "comment" => CommentForeground,
        "string" => StringForeground,
        "number" => NumberForeground,
        _ => Foreground
};
}
