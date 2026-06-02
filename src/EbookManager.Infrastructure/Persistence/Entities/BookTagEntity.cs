namespace EbookManager.Infrastructure.Persistence.Entities;

public sealed class BookTagEntity
{
    public Guid BookId { get; set; }
    public BookEntity Book { get; set; } = null!;
    public Guid TagId { get; set; }
    public TagEntity Tag { get; set; } = null!;
    public int Order { get; set; }
}
