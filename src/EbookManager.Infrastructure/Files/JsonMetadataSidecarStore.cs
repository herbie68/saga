using System.Text.Json;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;

namespace EbookManager.Infrastructure.Files;

public sealed class JsonMetadataSidecarStore : IMetadataSidecarStore
{
    public const string FileName = "metadata.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<BookMetadata?> TryReadAsync(
        string bookFilePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sidecarPath = GetSidecarPath(bookFilePath);
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                sidecarPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });
            var sidecar = await JsonSerializer.DeserializeAsync<MetadataSidecar>(
                stream,
                JsonOptions,
                cancellationToken);

            return sidecar?.ToMetadata();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task WriteAsync(
        string bookFilePath,
        BookMetadata metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(metadata);

        var sidecarPath = GetSidecarPath(bookFilePath);
        var directory = Path.GetDirectoryName(sidecarPath)
            ?? throw new InvalidOperationException("The sidecar path does not have a directory.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Guid.NewGuid():N}.{FileName}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.CreateNew,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                }))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    MetadataSidecar.FromMetadata(metadata),
                    JsonOptions,
                    cancellationToken);
            }

            File.Move(tempPath, sidecarPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string GetSidecarPath(string bookFilePath)
    {
        if (string.IsNullOrWhiteSpace(bookFilePath))
        {
            throw new ArgumentException("The book file path must not be blank.", nameof(bookFilePath));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(bookFilePath))
            ?? throw new ArgumentException("The book file path must include a directory.", nameof(bookFilePath));
        return Path.Combine(directory, FileName);
    }

    private sealed record MetadataSidecar(
        int SchemaVersion,
        string Title,
        IReadOnlyList<string> Authors,
        string? Description,
        string? Language,
        string? Publisher,
        DateOnly? PublicationDate,
        IReadOnlyList<string>? Tags,
        string? Series,
        decimal? SeriesNumber,
        string? Isbn)
    {
        public static MetadataSidecar FromMetadata(BookMetadata metadata) =>
            new(
                1,
                metadata.Title,
                metadata.Authors,
                metadata.Description,
                metadata.Language,
                metadata.Publisher,
                metadata.PublicationDate,
                metadata.Tags,
                metadata.Series,
                metadata.SeriesNumber,
                metadata.Isbn);

        public BookMetadata? ToMetadata()
        {
            if (string.IsNullOrWhiteSpace(Title) || Authors.Count == 0)
            {
                return null;
            }

            var authors = Authors
                .Select(author => author.Trim())
                .Where(author => author.Length > 0)
                .ToArray();
            if (authors.Length == 0)
            {
                return null;
            }

            return new BookMetadata(
                Title.Trim(),
                authors,
                Description,
                Language,
                Publisher,
                PublicationDate,
                Tags,
                Series,
                SeriesNumber,
                Isbn);
        }
    }
}
