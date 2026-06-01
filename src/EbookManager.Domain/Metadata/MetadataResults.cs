using EbookManager.Domain.Books;

namespace EbookManager.Domain.Metadata;

public sealed record MetadataReadResult(BookMetadata Metadata, string? Warning = null);

public sealed record MetadataWriteResult(MetadataWriteBackStatus Status, string? Message = null);
