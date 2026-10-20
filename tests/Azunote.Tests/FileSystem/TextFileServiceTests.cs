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

        var asUtf8 = await TextFileService.ReadWithEncodingAsync(_path, TextEncodingKind.Utf8);
        var asLatin1 = await TextFileService.ReadWithEncodingAsync(_path, TextEncodingKind.Latin1);

        Assert.Equal(new TextFileData("é\r\n", TextEncodingKind.Utf8, LineEndingKind.CrLf), asUtf8);
        Assert.Equal("ï»¿Ã©\r\n", asLatin1.Text);
        Assert.Equal(TextEncodingKind.Latin1, asLatin1.Encoding);
    }

    [Fact]
    public async Task Reading_invalid_bytes_as_utf8_fails_instead_of_replacing_them()
    {
        File.WriteAllBytes(_path, [0x82, 0xA0]);

        await Assert.ThrowsAsync<DecoderFallbackException>(
            () => TextFileService.ReadWithEncodingAsync(_path, TextEncodingKind.Utf8));
    }
}
