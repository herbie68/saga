using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;

namespace EbookManager.Domain.Abstractions;

public interface IMetadataAdapter
{
    bool CanHandle(EbookFormat format);
    Task<MetadataReadResult> ReadAsync(
        string path,
        EbookFormat format,
        CancellationToken cancellationToken);
    Task<MetadataWriteResult> WriteAsync(
        string path,
        EbookFormat format,
        BookMetadata metadata,
        CancellationToken cancellationToken);
}
