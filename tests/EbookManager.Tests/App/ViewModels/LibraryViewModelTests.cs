using EbookManager.Application.Books;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Domain.Metadata;
using EbookManager.Presentation.Abstractions;
using EbookManager.Presentation.ViewModels;
using FluentAssertions;

namespace EbookManager.Tests.App.ViewModels;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task Refresh_loads_books_and_search_filters_visible_books()
    {
        var first = CreateBook("The Hobbit", ["Tolkien"]);
        var second = CreateBook("Dune", ["Frank Herbert"]);
        var viewModel = CreateViewModel([second, first]);

        await viewModel.RefreshAsync();
        viewModel.SearchText = "tolkien";

        viewModel.VisibleBooks.Should().ContainSingle();
        viewModel.VisibleBooks[0].Title.Should().Be("The Hobbit");
        viewModel.VisibleBookCount.Should().Be(1);
    }

    [Fact]
    public async Task Selecting_a_book_loads_details()
    {
        var book = CreateBook("Selected", ["Author"]);
        var viewModel = CreateViewModel([book]);

        await viewModel.RefreshAsync();
        viewModel.SelectedBook = viewModel.VisibleBooks.Single();

        viewModel.Details.BookId.Should().Be(book.Id);
        viewModel.Details.Title.Should().Be("Selected");
    }

    [Theory]
    [InlineData(LibraryView.Bookshelf)]
    [InlineData(LibraryView.Detailed)]
    [InlineData(LibraryView.List)]
    public void SelectedView_switches_between_supported_views(LibraryView selectedView)
    {
        var viewModel = CreateViewModel([]);

        viewModel.SelectedView = selectedView;

        viewModel.SelectedView.Should().Be(selectedView);
    }

    private static LibraryViewModel CreateViewModel(IReadOnlyList<Book> books)
    {
        var repository = new StaticBookRepository(books);
        var details = new BookDetailsViewModel(new BookService(
            repository,
            new NoopLibraryFileStore(),
            new NoopMetadataAdapterResolver()));
        return new LibraryViewModel(
            repository,
            new BookSearchService(),
            details,
            new NoopUserInteractionService());
    }

    private static Book CreateBook(string title, IReadOnlyList<string> authors)
    {
        var now = DateTimeOffset.UtcNow;
        return new Book(
            Guid.NewGuid(),
            new BookMetadata(title, authors),
            ReadingStatus.Unread,
            null,
            now,
            now);
    }

    private sealed class StaticBookRepository(IReadOnlyList<Book> books) : IBookRepository
    {
        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(books);
        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(books.SingleOrDefault(book => book.Id == id));
        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> HasNormalizedTitleAndAuthorAsync(string title, IReadOnlyList<string> authors, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(Book book, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BookFile>>([]);
        public Task UpdateFileWriteBackAsync(Guid fileId, MetadataWriteResult result, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopLibraryFileStore : ILibraryFileStore
    {
        public Task<(string RelativeBookPath, string? RelativeCoverPath)> CopyIntoLibraryAsync(
            Guid bookId,
            string sourcePath,
            byte[]? coverBytes,
            CancellationToken cancellationToken) =>
            Task.FromResult(($"books/{bookId:N}/book.epub", (string?)null));

        public Task DeleteBookDirectoryAsync(Guid bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public string GetAbsolutePath(string relativePath) => relativePath;
    }

    private sealed class NoopMetadataAdapterResolver : IMetadataAdapterResolver
    {
        public IMetadataAdapter Resolve(EbookFormat format) => new NoopMetadataAdapter();
    }

    private sealed class NoopMetadataAdapter : IMetadataAdapter
    {
        public bool CanHandle(EbookFormat format) => true;

        public Task<MetadataReadResult> ReadAsync(string path, EbookFormat format, CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataReadResult(new BookMetadata("Title", ["Author"])));

        public Task<MetadataWriteResult> WriteAsync(
            string path,
            EbookFormat format,
            BookMetadata metadata,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataWriteResult(MetadataWriteBackStatus.Unsupported));
    }

    private sealed class NoopUserInteractionService : IUserInteractionService
    {
        public Task<IReadOnlyList<string>> PickBookFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickScanFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmDeleteAsync(string title, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task ShowImportResultAsync(ImportResultViewModel result, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
