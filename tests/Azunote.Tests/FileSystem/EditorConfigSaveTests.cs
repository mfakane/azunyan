using System.Text;
using Azunote;
using Xunit;

namespace Azunote.Tests.FileSystem;

public sealed class EditorConfigSaveTests
{
    [Fact]
    public void NormalizeForSave_trims_trailing_whitespace_and_adds_final_newline()
    {
        var settings = new EditorConfigSettings(
            InsertFinalNewline: true,
            TrimTrailingWhitespace: true);

        var result = EditorConfigTextNormalizer.NormalizeForSave(
            "first  \n  \nlast \t",
            settings,
            LineEndingKind.CrLf);

        Assert.Equal("first\n\nlast\r\n", result);
    }

    [Fact]
    public void NormalizeForSave_does_not_remove_an_existing_final_newline_when_disabled()
    {
        var settings = new EditorConfigSettings(
            InsertFinalNewline: false,
            TrimTrailingWhitespace: false);

        var result = EditorConfigTextNormalizer.NormalizeForSave(
            "text",
            settings,
            LineEndingKind.Lf);

        Assert.Equal("text", result);
    }

    [Fact]
    public async Task ReadAsync_uses_a_configured_encoding_for_a_bomless_file()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote-encoding-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "document.txt");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(path, Encoding.Unicode.GetBytes("日本語"));

            var result = await TextFileService.ReadAsync(
                path,
                encodingHint: TextEncodingKind.Utf16LittleEndian);

            Assert.Equal("日本語", result.Text);
            Assert.Equal(TextEncodingKind.Utf16LittleEndian, result.Encoding);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteAsync_and_ReadAsync_support_latin1()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote-encoding-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "document.txt");
        try
        {
            Directory.CreateDirectory(root);
            await TextFileService.WriteAsync(
                path,
                "café",
                TextEncodingKind.Latin1);

            var result = await TextFileService.ReadAsync(
                path,
                encodingHint: TextEncodingKind.Latin1);

            Assert.Equal("café", result.Text);
            Assert.Equal(TextEncodingKind.Latin1, result.Encoding);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_prefers_a_bom_over_the_editorconfig_encoding_hint()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote-encoding-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "document.txt");
        try
        {
            Directory.CreateDirectory(root);
            await TextFileService.WriteAsync(
                path,
                "日本語",
                TextEncodingKind.Utf8Bom);

            var result = await TextFileService.ReadAsync(
                path,
                encodingHint: TextEncodingKind.Utf16LittleEndian);

            Assert.Equal("日本語", result.Text);
            Assert.Equal(TextEncodingKind.Utf8Bom, result.Encoding);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
