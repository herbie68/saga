using System.Globalization;
using System.Windows.Data;
using EbookManager.App.Localization;

namespace EbookManager.App.Converters;

public sealed class ImportMessageToLocalizedStringConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, string> ExactMessageKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["added"] = "ImportMessageAdded",
            ["exact duplicate skipped"] = "ImportMessageExactDuplicateSkipped",
            ["possible duplicate"] = "ImportMessagePossibleDuplicate",
            ["managed copy failed"] = "ImportMessageManagedCopyFailed",
            ["metadata read failed"] = "ImportMessageMetadataReadFailed",
            ["source unreadable; make sure the file is available locally"] = "ImportMessageSourceUnreadable",
            ["unsupported format"] = "ImportMessageUnsupportedFormat",
            ["invalid source path"] = "ImportMessageInvalidSourcePath",
            ["import failed"] = "ImportMessageImportFailed",
            ["cannot persist result"] = "ImportMessageCannotPersistResult",
            ["cleanup incomplete"] = "ImportMessageCleanupIncomplete"
        };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string message || string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var parts = message.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(LocalizePart);
        return string.Join("; ", parts);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static string LocalizePart(string part)
    {
        if (ExactMessageKeys.TryGetValue(part, out var key))
        {
            return LocalizedStrings.Current[key];
        }

        const string metadataWarningPrefix = "metadata warning:";
        if (part.StartsWith(metadataWarningPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"{LocalizedStrings.Current["ImportMessageMetadataWarning"]}: {LocalizeMetadataWarning(part[metadataWarningPrefix.Length..].Trim())}";
        }

        return LocalizeMetadataWarning(part);
    }

    private static string LocalizeMetadataWarning(string warning)
    {
        const string calibreOpfSource = "metadata source: calibre opf";
        if (string.Equals(warning, calibreOpfSource, StringComparison.OrdinalIgnoreCase))
        {
            return LocalizedStrings.Current["ImportMetadataSourceCalibreOpf"];
        }

        const string metadataJsonSource = "metadata source: metadata json";
        if (string.Equals(warning, metadataJsonSource, StringComparison.OrdinalIgnoreCase))
        {
            return LocalizedStrings.Current["ImportMetadataSourceJson"];
        }

        return warning
            .Replace("Malformed EPUB metadata:", LocalizedStrings.Current["ImportMalformedEpubMetadataPrefix"], StringComparison.OrdinalIgnoreCase)
            .Replace("Malformed CBZ archive:", LocalizedStrings.Current["ImportMalformedCbzArchivePrefix"], StringComparison.OrdinalIgnoreCase)
            .Replace("Calibre OPF ignored:", LocalizedStrings.Current["ImportCalibreOpfIgnoredPrefix"], StringComparison.OrdinalIgnoreCase);
    }
}
