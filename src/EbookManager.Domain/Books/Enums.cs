using System.Collections.Frozen;

namespace EbookManager.Domain.Books;

public enum ReadingStatus
{
    Unread,
    Reading,
    Read
}

public enum MetadataWriteBackStatus
{
    NotAttempted,
    Unsupported,
    Succeeded,
    Failed
}

public enum EbookFormat
{
    Epub,
    Kepub,
    Pdf,
    Cbr,
    Cbz,
    Mobi,
    Azw,
    Azw3,
    Kfx
}

public static class EbookFormatExtensions
{
    private static readonly FrozenDictionary<string, EbookFormat> FormatsByExtension =
        new Dictionary<string, EbookFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".epub"] = EbookFormat.Epub,
            [".kepub.epub"] = EbookFormat.Kepub,
            [".pdf"] = EbookFormat.Pdf,
            [".cbr"] = EbookFormat.Cbr,
            [".cbz"] = EbookFormat.Cbz,
            [".mobi"] = EbookFormat.Mobi,
            [".azw"] = EbookFormat.Azw,
            [".azw3"] = EbookFormat.Azw3,
            [".kfx"] = EbookFormat.Kfx
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<EbookFormat, string> ExtensionsByFormat =
        FormatsByExtension.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    public static FrozenSet<EbookFormat> Supported { get; } =
        ExtensionsByFormat.Keys.ToFrozenSet();

    public static bool TryFromFilename(string path, out EbookFormat format)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".kepub.epub", StringComparison.OrdinalIgnoreCase))
        {
            format = EbookFormat.Kepub;
            return true;
        }

        return FormatsByExtension.TryGetValue(Path.GetExtension(name), out format);
    }

    public static string ToExtension(this EbookFormat format)
    {
        if (ExtensionsByFormat.TryGetValue(format, out var extension))
        {
            return extension;
        }

        throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown ebook format.");
    }
}
