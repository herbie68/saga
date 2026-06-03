using EbookManager.Domain.Books;

namespace EbookManager.Domain.Abstractions;

public interface IMetadataSidecarStore
{
    Task<BookMetadata?> TryReadAsync(
        string bookFilePath,
        CancellationToken cancellationToken);

    Task WriteAsync(
        string bookFilePath,
        BookMetadata metadata,
        CancellationToken cancellationToken);
}
