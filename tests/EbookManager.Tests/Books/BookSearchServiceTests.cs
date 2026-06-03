using EbookManager.Application.Books;
using EbookManager.Domain.Books;
using FluentAssertions;

namespace EbookManager.Tests.Books;

public sealed class BookSearchServiceTests
{
    [Theory]
    [InlineData("HOBBIT", "The Hobbit")]
    [InlineData("TOLKIEN", "The Silmarillion")]
    [InlineData("DRAGON", "Dragon Tales")]
    [InlineData("DUTCH", "Nederlands Boek")]
    [InlineData("PENGUIN", "Publisher Match")]
    [InlineData("SPACE", "Tagged Book")]
    [InlineData("FOUNDATION", "Series Match")]
    [InlineData("9780000000000", "ISBN Match")]
    [InlineData("READ", "Status Book")]
    public void Filter_matches_each_supported_field_case_insensitively(string searchText, string expectedTitle)
    {
        var service = new BookSearchService();
        var books = CreateBooks();

        var result = service.Filter(books, searchText);

        result.Should().ContainSingle(book => book.Metadata.Title == expectedTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_search_returns_all_books(string searchText)
    {
        var service = new BookSearchService();
        var books = CreateBooks();

        var result = service.Filter(books, searchText);

        result.Should().HaveCount(books.Count);
    }

    [Fact]
    public void Reading_status_search_matches_the_exact_status_name()
    {
        var service = new BookSearchService();
        var books = CreateBooks();

        var result = service.Filter(books, "read");

        result.Should().ContainSingle(book => book.ReadingStatus == ReadingStatus.Read);
    }

    private static IReadOnlyList<Book> CreateBooks()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new(Guid.NewGuid(), new BookMetadata("The Hobbit", ["Bilbo Baggins"]), ReadingStatus.Unread, null, now, now),
            new(Guid.NewGuid(), new BookMetadata("The Silmarillion", ["Tolkien"]), ReadingStatus.Unread, null, now, now),
            new(Guid.NewGuid(), new BookMetadata("Dragon Tales", ["Author"], Description: "A dragon adventure"), ReadingStatus.Unread, null, now, now),
            new(Guid.NewGuid(), new BookMetadata("Nederlands Boek", ["Auteur"], Language: "Dutch"), ReadingStatus.Unread, null, now, now),
            new(Guid.NewGuid(), new BookMetadata("Publisher Match", ["Author"], Publisher: "Penguin House"), ReadingStatus.Unread, null, now, now),
            new(Guid.NewGuid(), new BookMetadata("Tagged Book", ["Author"], Tags: ["Space", "History"]), ReadingStatus.Unread, null, now, now),
            new(Guid.NewGuid(), new BookMetadata("Series Match", ["Author"], Series: "Foundation"), ReadingStatus.Unread, null, now, now),
            new(Guid.NewGuid(), new BookMetadata("ISBN Match", ["Author"], Isbn: "9780000000000"), ReadingStatus.Unread, null, now, now),
            new(Guid.NewGuid(), new BookMetadata("Status Book", ["Author"]), ReadingStatus.Read, null, now, now)
        ];
    }
}
