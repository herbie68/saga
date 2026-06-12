using System.IO.Compression;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;

namespace EbookManager.Infrastructure.Metadata;

public sealed class CbzMetadataAdapter : IMetadataAdapter
{
    private const int MaxEntryCount = 2000;
    private const long MaxSelectedImageSizeBytes = 10 * 1024 * 1024;
    private const long DefaultMaxArchiveSizeForCoverExtractionBytes = 64 * 1024 * 1024;

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private readonly FallbackMetadataAdapter fallback = new();
    private readonly long maxArchiveSizeForCoverExtractionBytes;

    public CbzMetadataAdapter(long maxArchiveSizeForCoverExtractionBytes = DefaultMaxArchiveSizeForCoverExtractionBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxArchiveSizeForCoverExtractionBytes);
        this.maxArchiveSizeForCoverExtractionBytes = maxArchiveSizeForCoverExtractionBytes;
    }

    public bool CanHandle(EbookFormat format) => format == EbookFormat.Cbz;

    public async Task<MetadataReadResult> ReadAsync(
        string path,
        EbookFormat format,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fallbackResult = await fallback.ReadAsync(path, format, cancellationToken);
        var fileInfo = new FileInfo(Path.GetFullPath(path));
        if (fileInfo.Length > maxArchiveSizeForCoverExtractionBytes)
        {
            return fallbackResult with
            {
                Warning = "CBZ cover extraction skipped for large archive."
            };
        }

        try
        {
            await using var stream = new FileStream(
                fileInfo.FullName,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count > MaxEntryCount)
            {
                throw new InvalidDataException($"CBZ archive contains more than {MaxEntryCount} entries.");
            }

            var selectedEntry = SelectCoverEntry(archive, cancellationToken);
            if (selectedEntry is null)
            {
                return fallbackResult;
            }

            if (selectedEntry.Length > MaxSelectedImageSizeBytes)
            {
                throw new InvalidDataException(
                    $"CBZ cover image exceeds the {MaxSelectedImageSizeBytes} byte size limit.");
            }

            var coverBytes = await ReadEntryBytesAsync(selectedEntry, cancellationToken);
            return new MetadataReadResult(
                new BookMetadata(
                    fallbackResult.Metadata.Title,
                    fallbackResult.Metadata.Authors,
                    fallbackResult.Metadata.Description,
                    fallbackResult.Metadata.Language,
                    fallbackResult.Metadata.Publisher,
                    fallbackResult.Metadata.PublicationDate,
                    fallbackResult.Metadata.Tags,
                    fallbackResult.Metadata.Series,
                    fallbackResult.Metadata.SeriesNumber,
                    fallbackResult.Metadata.Isbn,
                    coverBytes),
                fallbackResult.Warning);
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException)
        {
            return fallbackResult with
            {
                Warning = $"Malformed CBZ archive: {exception.Message}"
            };
        }
    }

    public Task<MetadataWriteResult> WriteAsync(
        string path,
        EbookFormat format,
        BookMetadata metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MetadataWriteResult(
            MetadataWriteBackStatus.Unsupported,
            "CBZ write-back is not supported."));
    }

    private static ZipArchiveEntry? SelectCoverEntry(ZipArchive archive, CancellationToken cancellationToken)
    {
        ZipArchiveEntry? selectedEntry = null;
        foreach (var entry in archive.Entries
                     .Where(entry => !string.IsNullOrEmpty(entry.Name))
                     .Where(entry => SupportedImageExtensions.Contains(Path.GetExtension(entry.FullName)))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (selectedEntry is null)
            {
                selectedEntry = entry;
            }
        }

        return selectedEntry;
    }

    private static async Task<byte[]> ReadEntryBytesAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var entryStream = entry.Open();
        await using var memoryStream = new MemoryStream(
            entry.Length is > 0 and <= int.MaxValue ? (int)entry.Length : 0);
        var buffer = new byte[81920];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await entryStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return memoryStream.ToArray();
    }
}
