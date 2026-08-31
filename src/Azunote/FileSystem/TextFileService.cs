using System.Text;

namespace Azunote;

public enum TextEncodingKind
{
    Utf8,
    Utf8Bom,
    Utf16LittleEndian,
    Utf16BigEndian,
    Latin1,
    SystemDefault
}

public enum LineEndingKind
{
    Lf,
    CrLf,
    Cr,
    Mixed,
    None
}

public sealed record TextFileData(
    string Text,
    TextEncodingKind Encoding,
    LineEndingKind LineEnding);

public static class TextFileService
{
    static TextFileService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding Utf16LittleEndian = new(bigEndian: false, byteOrderMark: true);
    private static readonly UnicodeEncoding Utf16BigEndian = new(bigEndian: true, byteOrderMark: true);
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static Task<TextFileData> ReadAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReadAsync(path, encodingHint: null, cancellationToken);

    public static async Task<TextFileData> ReadAsync(
        string path,
        TextEncodingKind? encodingHint,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var (encoding, body) = DetectEncoding(bytes, encodingHint);
        var text = encoding switch
        {
            TextEncodingKind.Utf8 => Utf8.GetString(body),
            TextEncodingKind.Utf8Bom => Utf8Bom.GetString(body),
            TextEncodingKind.Utf16LittleEndian => Utf16LittleEndian.GetString(body),
            TextEncodingKind.Utf16BigEndian => Utf16BigEndian.GetString(body),
            TextEncodingKind.Latin1 => Latin1.GetString(body),
            _ => GetSystemDefaultEncoding().GetString(body)
        };

        return new TextFileData(text, encoding, DetectLineEnding(text));
    }

    public static async Task WriteAsync(
        string path,
        string text,
        TextEncodingKind encodingKind,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(path, text, encodingKind, lineEnding: null, cancellationToken);
    }

    public static async Task WriteAsync(
        string path,
        string text,
        TextEncodingKind encodingKind,
        LineEndingKind lineEnding,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(path, text, encodingKind, (LineEndingKind?)lineEnding, cancellationToken);
    }

    private static async Task WriteAsync(
        string path,
        string text,
        TextEncodingKind encodingKind,
        LineEndingKind? lineEnding,
        CancellationToken cancellationToken)
    {
        var encoding = GetEncoding(encodingKind);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(NormalizeLineEndings(text, lineEnding));
        var bytes = new byte[preamble.Length + body.Length];

        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    private static string NormalizeLineEndings(string text, LineEndingKind? lineEnding)
    {
        if (lineEnding is null)
        {
            if (text.IndexOf('\r') < 0)
            {
                return text;
            }

            var normalizedOutput = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '\r'
                    && index + 1 < text.Length
                    && text[index + 1] == '\n')
                {
                    normalizedOutput.Append("\r\n");
                    index++;
                }
                else if (text[index] == '\r')
                {
                    normalizedOutput.Append(Environment.NewLine);
                }
                else
                {
                    normalizedOutput.Append(text[index]);
                }
            }

            return normalizedOutput.ToString();
        }

        var target = lineEnding.Value switch
        {
            LineEndingKind.Lf => "\n",
            LineEndingKind.CrLf => "\r\n",
            LineEndingKind.Cr => "\r",
            _ => Environment.NewLine
        };

        var output = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                output.Append(target);
            }
            else if (text[index] == '\n')
            {
                output.Append(target);
            }
            else
            {
                output.Append(text[index]);
            }
        }

        return output.ToString();
    }

    public static LineEndingKind DetectLineEnding(string text)
    {
        var hasCrLf = false;
        var hasLf = false;
        var hasCr = false;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    hasCrLf = true;
                    index++;
                }
                else
                {
                    hasCr = true;
                }
            }
            else if (text[index] == '\n')
            {
                hasLf = true;
            }
        }

        var count = (hasCrLf ? 1 : 0) + (hasLf ? 1 : 0) + (hasCr ? 1 : 0);
        return count switch
        {
            0 => LineEndingKind.None,
            1 when hasCrLf => LineEndingKind.CrLf,
            1 when hasLf => LineEndingKind.Lf,
            1 => LineEndingKind.Cr,
            _ => LineEndingKind.Mixed
        };
    }

    public static string GetEncodingDisplayName(TextEncodingKind encoding) => encoding switch
    {
        TextEncodingKind.Utf8 => "UTF-8",
        TextEncodingKind.Utf8Bom => "UTF-8 BOM",
        TextEncodingKind.Utf16LittleEndian => "UTF-16 LE",
        TextEncodingKind.Utf16BigEndian => "UTF-16 BE",
        TextEncodingKind.Latin1 => "Latin-1",
        _ => "System default"
    };

    public static string GetLineEndingDisplayName(LineEndingKind lineEnding) => lineEnding switch
    {
        LineEndingKind.CrLf => "CRLF",
        LineEndingKind.Lf => "LF",
        LineEndingKind.Cr => "CR",
        LineEndingKind.Mixed => "Mixed",
        _ => "None"
    };

    private static (TextEncodingKind Encoding, byte[] Body) DetectEncoding(
        byte[] bytes,
        TextEncodingKind? encodingHint)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return (TextEncodingKind.Utf8Bom, bytes[3..]);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return (TextEncodingKind.Utf16LittleEndian, bytes[2..]);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return (TextEncodingKind.Utf16BigEndian, bytes[2..]);
        }

        if (encodingHint is { } hintedEncoding
            && hintedEncoding != TextEncodingKind.SystemDefault)
        {
            try
            {
                _ = GetEncoding(hintedEncoding).GetString(bytes);
                return (hintedEncoding, bytes);
            }
            catch (DecoderFallbackException)
            {
                // Fall back to the existing byte-based detection when the
                // configured encoding cannot decode this file.
            }
        }

        try
        {
            Utf8.GetString(bytes);
            return (TextEncodingKind.Utf8, bytes);
        }
        catch (DecoderFallbackException)
        {
            return (TextEncodingKind.SystemDefault, bytes);
        }
    }

    private static Encoding GetEncoding(TextEncodingKind encoding) => encoding switch
    {
        TextEncodingKind.Utf8 => Utf8,
        TextEncodingKind.Utf8Bom => Utf8Bom,
        TextEncodingKind.Utf16LittleEndian => Utf16LittleEndian,
        TextEncodingKind.Utf16BigEndian => Utf16BigEndian,
        TextEncodingKind.Latin1 => Latin1,
        _ => GetSystemDefaultEncoding()
    };

    private static Encoding GetSystemDefaultEncoding() => Encoding.GetEncoding(0);
}
