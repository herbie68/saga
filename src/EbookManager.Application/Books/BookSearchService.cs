using EbookManager.Domain.Books;

namespace EbookManager.Application.Books;

public sealed class BookSearchService
{
    public IReadOnlyList<Book> Filter(IReadOnlyList<Book> books, string? searchText)
    {
        ArgumentNullException.ThrowIfNull(books);

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return books;
        }

        var normalizedSearchText = searchText.Trim();
        return books.Where(book => Matches(book, normalizedSearchText)).ToList();
    }

    private static bool Matches(Book book, string searchText) =>
        Contains(book.Metadata.Title, searchText) ||
        book.Metadata.Authors.Any(author => Contains(author, searchText)) ||
        Contains(book.Metadata.Description, searchText) ||
        Contains(book.Metadata.Language, searchText) ||
        Contains(book.Metadata.Publisher, searchText) ||
        (book.Metadata.Tags?.Any(tag => Contains(tag, searchText)) ?? false) ||
        Contains(book.Metadata.Series, searchText) ||
        Contains(book.Metadata.Isbn, searchText) ||
        MatchesReadingStatus(book.ReadingStatus, searchText);

    private static bool Contains(string? value, string searchText) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(searchText, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesReadingStatus(ReadingStatus status, string searchText) =>
        status.ToString().Equals(searchText, StringComparison.OrdinalIgnoreCase);
}
