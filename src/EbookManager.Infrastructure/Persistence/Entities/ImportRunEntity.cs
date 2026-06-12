namespace EbookManager.Infrastructure.Persistence.Entities;

public sealed class ImportRunEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string Kind { get; set; } = "FileImport";
    public string? SourcePath { get; set; }
    public bool? IncludeSubdirectories { get; set; }
    public ICollection<ImportItemEntity> Items { get; set; } = [];
}
