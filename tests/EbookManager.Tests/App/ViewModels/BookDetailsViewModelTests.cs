using EbookManager.Application.Books;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;
using EbookManager.Presentation.ViewModels;
using FluentAssertions;

namespace EbookManager.Tests.App.ViewModels;

public sealed class BookDetailsViewModelTests
{
    [Fact]
    public void Editing_metadata_sets_dirty_state_and_undo_restores_original_values()
    {
        var viewModel = CreateViewModel(out _);
        var book = CreateBook("Original", ["First Author"]);

        viewModel.Load(book);
        viewModel.Title = "Changed";

        viewModel.HasUnsavedChanges.Should().BeTrue();
        viewModel.UndoCommand.Execute(null);

        viewModel.Title.Should().Be("Original");
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Save_updates_metadata_and_clears_dirty_state()
    {
        var viewModel = CreateViewModel(out var repository);
        var book = CreateBook("Original", ["First Author"]);
        viewModel.Load(book);
        viewModel.Title = "Changed";
        viewModel.AuthorsText = "Second Author; Third Author";
        viewModel.ReadingStatus = ReadingStatus.Read;

        await viewModel.SaveCommand.ExecuteAsync(null);

        repository.UpdatedBook.Should().NotBeNull();
        repository.UpdatedBook!.Metadata.Title.Should().Be("Changed");
        repository.UpdatedBook.Metadata.Authors.Should().Equal("Second Author", "Third Author");
        repository.UpdatedBook.ReadingStatus.Should().Be(ReadingStatus.Read);
        viewModel.LastSaveResult!.Status.Should().Be(BookSaveStatus.Succeeded);
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_removes_loaded_book_and_clears_details()
    {
        var viewModel = CreateViewModel(out var repository);
        var book = CreateBook("Original", ["First Author"]);
        viewModel.Load(book);

        await viewModel.DeleteCommand.ExecuteAsync(null);

        repository.DeletedBookId.Should().Be(book.Id);
        viewModel.BookId.Should().BeNull();
        viewModel.LastDeleteResult.Should().BeNull();
        viewModel.HasUnsavedChanges.Should().BeFalse();
    }

    private static BookDetailsViewModel CreateViewModel(out RecordingBookRepository repository)
    {
        repository = new RecordingBookRepository();
        var service = new BookService(
            repository,
            new NoopLibraryFileStore(),
            new NoopMetadataAdapterResolver());
        return new BookDetailsViewModel(service);
    }

    private static Book CreateBook(string title, IReadOnlyList<string> authors)
    {
        var now = DateTimeOffset.UtcNow;
        return new Book(
            Guid.NewGuid(),
            new BookMetadata(
                title,
                authors,
                Description: "Description",
                Language: "en",
                Publisher: "Publisher",
                Tags: ["Tag"],
                Isbn: "9780000000000"),
            ReadingStatus.Unread,
            null,
            now,
            now);
    }

    private sealed class RecordingBookRepository : IBookRepository
    {
        public Book? UpdatedBook { get; private set; }
        public Guid? DeletedBookId { get; private set; }

        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Book>>([]);
        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Book?>(null);
        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> HasNormalizedTitleAndAuthorAsync(string title, IReadOnlyList<string> authors, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateAsync(Book book, CancellationToken cancellationToken)
        {
            UpdatedBook = book;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            DeletedBookId = id;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BookFile>>([]);

        public Task UpdateFileWriteBackAsync(Guid fileId, MetadataWriteResult result, CancellationToken cancellationToken) =>
            Task.CompletedTask;
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
}
