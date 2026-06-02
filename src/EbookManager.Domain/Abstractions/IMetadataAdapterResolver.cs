using EbookManager.Domain.Books;

namespace EbookManager.Domain.Abstractions;

public interface IMetadataAdapterResolver
{
    IMetadataAdapter Resolve(EbookFormat format);
}
