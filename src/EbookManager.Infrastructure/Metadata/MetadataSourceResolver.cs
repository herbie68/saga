using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;

namespace EbookManager.Infrastructure.Metadata;

public sealed class MetadataSourceResolver(
    IMetadataAdapterResolver metadataAdapterResolver,
    IMetadataSidecarStore? jsonSidecarStore,
    CalibreOpfMetadataSidecarStore calibreOpfSidecarStore) : IMetadataSourceResolver
{
    public async Task<MetadataReadResult> ReadAsync(
        string sourcePath,
        EbookFormat format,
        CancellationToken cancellationToken)
    {
        var embedded = await metadataAdapterResolver.Resolve(format).ReadAsync(sourcePath, format, cancellationToken);
        var opf = await calibreOpfSidecarStore.TryReadAsync(sourcePath, cancellationToken);
        var json = jsonSidecarStore is null
            ? null
            : await jsonSidecarStore.TryReadAsync(sourcePath, cancellationToken);

        var merged = embedded.Metadata;
        var message = embedded.Warning;

        if (opf is not null)
        {
            merged = Merge(embedded.Metadata, opf.Metadata);
            message = AppendMessage(message, opf.Warning ?? "metadata source: calibre opf");
        }

        if (json is not null)
        {
            merged = Merge(merged, json);
            message = AppendMessage(message, "metadata source: metadata json");
        }

        return new MetadataReadResult(BookMetadataCleaner.Clean(merged), message);
    }

    private static BookMetadata Merge(BookMetadata lowerPriority, BookMetadata higherPriority) =>
        new(
            Prefer(higherPriority.Title, lowerPriority.Title),
            higherPriority.Authors.Count > 0 ? higherPriority.Authors : lowerPriority.Authors,
            Prefer(higherPriority.Description, lowerPriority.Description),
            Prefer(higherPriority.Language, lowerPriority.Language),
            Prefer(higherPriority.Publisher, lowerPriority.Publisher),
            higherPriority.PublicationDate ?? lowerPriority.PublicationDate,
            higherPriority.Tags is { Count: > 0 } ? higherPriority.Tags : lowerPriority.Tags,
            Prefer(higherPriority.Series, lowerPriority.Series),
            higherPriority.SeriesNumber ?? lowerPriority.SeriesNumber,
            Prefer(higherPriority.Isbn, lowerPriority.Isbn),
            higherPriority.CoverBytes ?? lowerPriority.CoverBytes);

    private static string Prefer(string? higherPriority, string? lowerPriority) =>
        string.IsNullOrWhiteSpace(higherPriority)
            ? lowerPriority ?? string.Empty
            : higherPriority.Trim();

    private static string? AppendMessage(string? current, string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return current;
        }

        return string.IsNullOrWhiteSpace(current)
            ? next
            : $"{current}; {next}";
    }
}
