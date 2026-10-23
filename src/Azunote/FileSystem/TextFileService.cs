using System.Text;

namespace Azunote;

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
    TextEncoding Encoding,
    LineEndingKind LineEnding);

public static class TextFileService
{
    public static Task<TextFileData> ReadAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReadAsync(path, encodingHint: null, cancellationToken);

    public static async Task<TextFileData> ReadAsync(
        string path,
        TextEncoding? encodingHint,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var (encoding, body) = DetectEncoding(bytes, encodingHint);
        var text = encoding.GetEncoding().GetString(body);

        return new TextFileData(text, encoding, DetectLineEnding(text));
    }

    /// <summary>
    /// Decodes a file with <paramref name="encoding"/> instead of detecting
    /// one. A byte order mark for that encoding is skipped; bytes that are not
    /// valid UTF-8 make a UTF-8 read fail rather than change the text.
    /// </summary>
    public static async Task<TextFileData> ReadWithEncodingAsync(
        string path,
        TextEncoding encoding,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var decoder = encoding.GetEncoding();
        var byteOrderMark = encoding.CodePage == TextEncoding.Utf8.CodePage
            ? TextEncoding.Utf8Bom.GetEncoding().GetPreamble()
            : decoder.GetPreamble();
        var body = bytes.AsSpan().StartsWith(byteOrderMark)
            ? bytes.AsSpan(byteOrderMark.Length)
            : bytes;
        var text = decoder.GetString(body);
        return new TextFileData(text, encoding, DetectLineEnding(text));
    }

    public static async Task WriteAsync(
        string path,
        string text,
        TextEncoding encodingKind,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(path, text, encodingKind, lineEnding: null, cancellationToken);
    }

    public static async Task WriteAsync(
        string path,
        string text,
        TextEncoding encodingKind,
        LineEndingKind lineEnding,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(path, text, encodingKind, (LineEndingKind?)lineEnding, cancellationToken);
    }

    private static async Task WriteAsync(
        string path,
        string text,
        TextEncoding encodingKind,
        LineEndingKind? lineEnding,
        CancellationToken cancellationToken)
    {
        var encoding = encodingKind.GetEncoding();
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

    public static string GetLineEndingDisplayName(LineEndingKind lineEnding) => lineEnding switch
    {
        LineEndingKind.CrLf => "CRLF",
        LineEndingKind.Lf => "LF",
        LineEndingKind.Cr => "CR",
        LineEndingKind.Mixed => "Mixed",
        _ => "None"
    };

    private static (TextEncoding Encoding, byte[] Body) DetectEncoding(
        byte[] bytes,
        TextEncoding? encodingHint)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return (TextEncoding.Utf8Bom, bytes[3..]);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return (TextEncoding.Utf16LittleEndian, bytes[2..]);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return (TextEncoding.Utf16BigEndian, bytes[2..]);
        }

        if (encodingHint is { } hintedEncoding)
        {
            try
            {
                _ = hintedEncoding.GetEncoding().GetString(bytes);
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
            TextEncoding.Utf8.GetEncoding().GetString(bytes);
            return (TextEncoding.Utf8, bytes);
        }
        catch (DecoderFallbackException)
        {
            return (TextEncoding.SystemDefault, bytes);
        }
    }
}
