using Azunyan.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace Azunyan.WinUI;

/// <summary>
/// A render-neutral run supplied to the DirectWrite-backed text layout.
/// Ranges are expressed in UTF-16 code units, matching the editor snapshot.
/// </summary>
public sealed record DirectWriteTextRun
{
    public DirectWriteTextRun(string text, Color foreground, bool italic = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Foreground = foreground;
        Italic = italic;
    }

    public string Text { get; }

    public Color Foreground { get; }

    public bool Italic { get; }
}

/// <summary>
/// Owns one DirectWrite text layout and exposes the geometry needed by the
/// renderer for drawing, selection, caret placement, and future hit-testing.
/// Win2D's CanvasTextLayout maps to Direct2D IDWriteTextLayout3.
/// </summary>
public sealed class DirectWriteTextLayout : IDisposable
{
    private readonly CanvasTextLayout _layout;

    private DirectWriteTextLayout(CanvasTextLayout layout, string text)
    {
        _layout = layout;
        Text = text;
    }

    public string Text { get; }

    public double Width => _layout.LayoutBoundsIncludingTrailingWhitespace.Width;

    public double Height => _layout.LayoutBounds.Height;

    public static DirectWriteTextLayout Create(
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<DirectWriteTextRun> runs,
        string fontFamily,
        float fontSize,
        float requestedWidth,
        float requestedHeight,
        float lineHeight,
        float baseline,
        float incrementalTabStop = 0)
    {
        ArgumentNullException.ThrowIfNull(resourceCreator);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        if (!float.IsFinite(fontSize) || fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        if (!float.IsFinite(requestedWidth) || requestedWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedWidth));
        }

        if (!float.IsFinite(requestedHeight) || requestedHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedHeight));
        }

        if (!float.IsFinite(lineHeight) || lineHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineHeight));
        }

        if (!float.IsFinite(baseline) || baseline < 0 || baseline > lineHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(baseline));
        }

        if (!float.IsFinite(incrementalTabStop) || incrementalTabStop < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(incrementalTabStop));
        }

        var text = string.Concat(runs.Select(run => run.Text));
        using var format = new CanvasTextFormat
        {
            FontFamily = fontFamily,
            FontSize = fontSize,
            WordWrapping = CanvasWordWrapping.NoWrap,
            LineSpacing = lineHeight,
            LineSpacingBaseline = baseline,
            // The caller supplies device-independent pixels measured against
            // the projected row geometry. Proportional mode would multiply the
            // font's own metrics by those values and push the baseline far
            // below the row, so the absolute (uniform) mode is required.
            LineSpacingMode = CanvasLineSpacingMode.Uniform,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };
        if (incrementalTabStop > 0)
        {
            format.IncrementalTabStop = incrementalTabStop;
        }
        var layout = new CanvasTextLayout(
            resourceCreator,
            text,
            format,
            requestedWidth,
            requestedHeight);

        var textOffset = 0;
        foreach (var run in runs)
        {
            if (run.Text.Length == 0)
            {
                continue;
            }

            layout.SetColor(textOffset, run.Text.Length, run.Foreground);
            if (run.Italic)
            {
                layout.SetFontStyle(textOffset, run.Text.Length, FontStyle.Italic);
            }

            textOffset += run.Text.Length;
        }

        return new DirectWriteTextLayout(layout, text);
    }

    /// <summary>
    /// Measures hard-wrap boundaries using DirectWrite's actual clusters and
    /// advances. Returned values are UTF-16 visual-column ends, excluding the
    /// final line end because the caller supplies it from the source line.
    /// </summary>
    public static IReadOnlyList<int> MeasureWrapBreaks(
        ICanvasResourceCreator resourceCreator,
        IReadOnlyList<DirectWriteTextRun> runs,
        string fontFamily,
        float fontSize,
        float requestedWidth,
        float lineHeight,
        float baseline,
        float incrementalTabStop = 0)
    {
        ArgumentNullException.ThrowIfNull(resourceCreator);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        if (!float.IsFinite(fontSize) || fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        if (!float.IsFinite(requestedWidth) || requestedWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedWidth));
        }

        if (!float.IsFinite(lineHeight) || lineHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineHeight));
        }

        if (!float.IsFinite(baseline) || baseline < 0 || baseline > lineHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(baseline));
        }

        if (!float.IsFinite(incrementalTabStop) || incrementalTabStop < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(incrementalTabStop));
        }

        var text = string.Concat(runs.Select(run => run.Text));
        if (text.Length == 0)
        {
            return Array.Empty<int>();
        }

        using var format = new CanvasTextFormat
        {
            FontFamily = fontFamily,
            FontSize = fontSize,
            WordWrapping = CanvasWordWrapping.EmergencyBreak,
            LineSpacing = lineHeight,
            LineSpacingBaseline = baseline,
            // The caller supplies device-independent pixels measured against
            // the projected row geometry. Proportional mode would multiply the
            // font's own metrics by those values and push the baseline far
            // below the row, so the absolute (uniform) mode is required.
            LineSpacingMode = CanvasLineSpacingMode.Uniform,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };
        if (incrementalTabStop > 0)
        {
            format.IncrementalTabStop = incrementalTabStop;
        }
        using var layout = new CanvasTextLayout(
            resourceCreator,
            text,
            format,
            requestedWidth,
            Math.Min(1_000_000f, Math.Max(lineHeight, lineHeight * text.Length)));

        var textOffset = 0;
        foreach (var run in runs)
        {
            if (run.Text.Length > 0 && run.Italic)
            {
                layout.SetFontStyle(textOffset, run.Text.Length, FontStyle.Italic);
            }

            textOffset += run.Text.Length;
        }

        var boundaries = new List<int>();
        var covered = 0;
        foreach (var line in layout.LineMetrics)
        {
            var end = checked(covered + line.CharacterCount);
            if (end <= covered || end > text.Length)
            {
                break;
            }

            covered = end;
            if (covered < text.Length)
            {
                boundaries.Add(covered);
            }
        }

        return covered == text.Length
            ? boundaries
            : Array.Empty<int>();
    }

    public void Draw(CanvasDrawingSession drawingSession, float x, float y, Color fallbackColor)
    {
        ArgumentNullException.ThrowIfNull(drawingSession);
        drawingSession.DrawTextLayout(_layout, x, y, fallbackColor);
    }

    public Vector2 GetCaretPosition(int characterIndex, bool trailingSideOfCharacter = false) =>
        _layout.GetCaretPosition(characterIndex, trailingSideOfCharacter);

    /// <summary>
    /// Returns caret stops at extended grapheme boundaries. DirectWrite's
    /// character indices are UTF-16 based, but the editor must not offer a
    /// caret inside a surrogate pair, combining sequence, or joined emoji.
    /// </summary>
    public IReadOnlyList<int> GetGraphemeCaretStops() =>
        UnicodeText
            .GetTextElementStarts(Text)
            .Append(Text.Length)
            .ToArray();

    public IReadOnlyList<Rect> GetCharacterBounds(int characterIndex, int characterCount)
    {
        if (characterIndex < 0 || characterCount < 0 || characterIndex + characterCount > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(characterIndex));
        }

        return _layout
            .GetCharacterRegions(characterIndex, characterCount)
            .Select(region => region.LayoutBounds)
            .ToArray();
    }

    public void Dispose() => _layout.Dispose();
}
