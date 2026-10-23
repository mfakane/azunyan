using System.Globalization;
using System.Text;

namespace Azunote;

/// <summary>
/// The encoding of a text file: a code page, and for UTF-8 whether the file
/// starts with a byte order mark.
/// </summary>
public sealed record TextEncoding
{
    private const int Utf8CodePage = 65001;
    private const int Utf16LittleEndianCodePage = 1200;
    private const int Utf16BigEndianCodePage = 1201;
    private const int Latin1CodePage = 28591;
    private const int EucJpCodePage = 51932;

    // Encodings that Encoding.GetEncodings does not list but can be used.
    private static readonly int[] UnlistedCodePages = [50220, 50225, EucJpCodePage, 51949, 52936, 54936];

    static TextEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private TextEncoding(int codePage, bool byteOrderMark)
    {
        CodePage = codePage;
        ByteOrderMark = byteOrderMark;
    }

    public static TextEncoding Utf8 { get; } = new(Utf8CodePage, byteOrderMark: false);

    public static TextEncoding Utf8Bom { get; } = new(Utf8CodePage, byteOrderMark: true);

    public static TextEncoding Utf16LittleEndian { get; } = new(Utf16LittleEndianCodePage, byteOrderMark: false);

    public static TextEncoding Utf16BigEndian { get; } = new(Utf16BigEndianCodePage, byteOrderMark: false);

    public static TextEncoding Latin1 { get; } = new(Latin1CodePage, byteOrderMark: false);

    /// <summary>
    /// The ANSI code page of the system, or Latin-1 when the system uses
    /// UTF-8 for it, so text that is not UTF-8 still reads without loss.
    /// </summary>
    public static TextEncoding SystemDefault
    {
        get
        {
            var codePage = Encoding.GetEncoding(0).CodePage;
            return codePage == Utf8CodePage ? Latin1 : FromCodePage(codePage);
        }
    }

    /// <summary>The Unicode encodings, which are always offered first.</summary>
    public static IReadOnlyList<TextEncoding> Unicode { get; } =
        [Utf8, Utf8Bom, Utf16LittleEndian, Utf16BigEndian];

    public int CodePage { get; }

    /// <summary>Whether a UTF-8 file starts with a byte order mark.</summary>
    public bool ByteOrderMark { get; }

    /// <summary>
    /// The name used in settings and by <c>${encoding}</c>, such as
    /// <c>utf-8</c> or <c>shift_jis</c>.
    /// </summary>
    public string Name => this switch
    {
        { CodePage: Utf8CodePage, ByteOrderMark: true } => "utf-8-bom",
        { CodePage: Utf16LittleEndianCodePage } => "utf-16le",
        { CodePage: Utf16BigEndianCodePage } => "utf-16be",
        _ => GetEncoding().WebName
    };

    public string DisplayName => this switch
    {
        { CodePage: Utf8CodePage, ByteOrderMark: true } => "UTF-8 BOM",
        { CodePage: Utf8CodePage } => "UTF-8",
        { CodePage: Utf16LittleEndianCodePage } => "UTF-16 LE",
        { CodePage: Utf16BigEndianCodePage } => "UTF-16 BE",
        // .NET calls it "Japanese (EUC)", which does not say which EUC.
        { CodePage: EucJpCodePage } => "Japanese (EUC-JP)",
        _ => GetEncoding().EncodingName
    };

    public static TextEncoding FromCodePage(int codePage) => new(codePage, byteOrderMark: false);

    /// <summary>
    /// Reads a name such as <c>utf-8-bom</c>, <c>shift_jis</c>, or another
    /// name or code page number that .NET knows for an encoding.
    /// </summary>
    public static bool TryParse(string? name, out TextEncoding encoding)
    {
        encoding = Utf8;
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (Unicode.FirstOrDefault(unicode =>
                string.Equals(unicode.Name, name, StringComparison.OrdinalIgnoreCase)) is { } match)
        {
            encoding = match;
            return true;
        }

        try
        {
            var codePage = int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? Encoding.GetEncoding(number).CodePage
                : Encoding.GetEncoding(name).CodePage;
            encoding = FromCodePage(codePage);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The encodings the menus offer: the Unicode ones, the system's, and
    /// the ones named in settings.
    /// </summary>
    public static IReadOnlyList<TextEncoding> GetOffered(IEnumerable<string> names) =>
    [
        .. Unicode
            .Append(SystemDefault)
            .Concat(names.Select(name => TryParse(name, out var encoding) ? encoding : null).OfType<TextEncoding>())
            .Distinct()
    ];

    /// <summary>
    /// Every encoding that can be chosen: the Unicode ones first, then the
    /// others by name.
    /// </summary>
    public static IReadOnlyList<TextEncoding> GetAll()
    {
        var codePages = Encoding.GetEncodings()
            .Select(info => info.CodePage)
            .Concat(UnlistedCodePages)
            .Where(codePage => codePage is not (Utf8CodePage or Utf16LittleEndianCodePage or Utf16BigEndianCodePage))
            .Distinct();
        return
        [
            .. Unicode,
            .. codePages
                .Select(FromCodePage)
                .OrderBy(encoding => encoding.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>
    /// The encoding that reads and writes the file. UTF-8 rejects invalid
    /// bytes, and UTF-16 writes a byte order mark.
    /// </summary>
    public Encoding GetEncoding() => this switch
    {
        { CodePage: Utf8CodePage } => new UTF8Encoding(ByteOrderMark, throwOnInvalidBytes: true),
        { CodePage: Utf16LittleEndianCodePage } => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        { CodePage: Utf16BigEndianCodePage } => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        _ => Encoding.GetEncoding(CodePage)
    };

    public override string ToString() => Name;
}
