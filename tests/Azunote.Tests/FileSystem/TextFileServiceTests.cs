using System.Text;
using Xunit;

namespace Azunote.Tests.FileSystem;

public sealed class TextFileServiceTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task Reading_with_an_encoding_overrides_detection_and_skips_its_byte_order_mark()
    {
        File.WriteAllBytes(_path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é\r\n")]);

        var asUtf8 = await TextFileService.ReadWithEncodingAsync(_path, TextEncoding.Utf8);
        var asLatin1 = await TextFileService.ReadWithEncodingAsync(_path, TextEncoding.Latin1);

        Assert.Equal(new TextFileData("é\r\n", TextEncoding.Utf8, LineEndingKind.CrLf), asUtf8);
        Assert.Equal("ï»¿Ã©\r\n", asLatin1.Text);
        Assert.Equal(TextEncoding.Latin1, asLatin1.Encoding);
    }

    [Fact]
    public async Task Reading_invalid_bytes_as_utf8_fails_instead_of_replacing_them()
    {
        File.WriteAllBytes(_path, [0x82, 0xA0]);

        await Assert.ThrowsAsync<DecoderFallbackException>(
            () => TextFileService.ReadWithEncodingAsync(_path, TextEncoding.Utf8));
    }

    [Fact]
    public async Task A_code_page_encoding_writes_and_reads_its_own_bytes()
    {
        Assert.True(TextEncoding.TryParse("shift_jis", out var shiftJis));

        await TextFileService.WriteAsync(_path, "あ\n", shiftJis);
        var read = await TextFileService.ReadWithEncodingAsync(_path, shiftJis);

        Assert.Equal([0x82, 0xA0, 0x0A], File.ReadAllBytes(_path));
        Assert.Equal(new TextFileData("あ\n", shiftJis, LineEndingKind.Lf), read);
    }
}
