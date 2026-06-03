using EbookManager.Domain.Books;

namespace EbookManager.Presentation.ViewModels;

public sealed class BookRowViewModel(Book book, string searchText = "")
{
    public Book Book { get; } = book;
    public Guid Id => Book.Id;
    public string Title => Book.Metadata.Title;
    public string Authors => string.Join(", ", Book.Metadata.Authors);
    public ReadingStatus ReadingStatus => Book.ReadingStatus;
    public string EReader => "Unavailable";
    public byte[]? CoverBytes => Book.Metadata.CoverBytes;
    public string? CoverRelativePath => Book.CoverRelativePath;
    public string SearchText { get; } = searchText;
}
