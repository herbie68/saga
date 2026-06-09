using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;

namespace EbookManager.Domain.Abstractions;

public interface IMetadataSourceResolver
{
    Task<MetadataReadResult> ReadAsync(
        string sourcePath,
        EbookFormat format,
        CancellationToken cancellationToken);
}
