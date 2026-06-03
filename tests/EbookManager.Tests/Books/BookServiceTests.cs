using System.Security.Cryptography;
using System.Text;
using EbookManager.Application.Books;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;
using EbookManager.Infrastructure.Files;
using EbookManager.Tests.TestSupport;
using FluentAssertions;

namespace EbookManager.Tests.Books;

public sealed class BookServiceTests : IAsyncLifetime
{
    private readonly TemporaryDirectory temporaryDirectory = new();

    [Fact]
    public async Task Save_updates_sqlite_before_attempting_write_back_and_persists_each_file_result()
    {
        var libraryRoot = temporaryDirectory.CreateSubdirectory("Library").FullName;
        var bookId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var updatedBook = new Book(
            bookId,
            new BookMetadata(
                "Updated Title",
                ["J.R.R. Tolkien"],
                Description: "Updated description",
                Language: "en",
                Publisher: "Allen & Unwin",
                Tags: ["Fantasy"],
                Series: "Middle-earth",
                Isbn: "9780000000000"),
            ReadingStatus.Read,
            null,
            now,
            now);

        var repo = new InMemoryBookRepository(
            updatedBook,
            [
                new BookFile(
                    Guid.NewGuid(),
                    bookId,
                    EbookFormat.Epub,
                    $"books/{bookId:N}/first.epub",
                    Convert.ToHexString(SHA256.HashData("first"u8.ToArray())),
                    5,
                    MetadataWriteBackStatus.NotAttempted,
                    null),
                new BookFile(
                    Guid.NewGuid(),
                    bookId,
                    EbookFormat.Pdf,
                    $"books/{bookId:N}/second.pdf",
                    Convert.ToHexString(SHA256.HashData("second"u8.ToArray())),
                    6,
                    MetadataWriteBackStatus.NotAttempted,
                    null),
                new BookFile(
                    Guid.NewGuid(),
                    bookId,
                    EbookFormat.Cbz,
                    $"books/{bookId:N}/third.cbz",
                    Convert.ToHexString(SHA256.HashData("third"u8.ToArray())),
                    5,
                    MetadataWriteBackStatus.NotAttempted,
                    null)
            ]);
        var firstPath = Path.Combine(libraryRoot, $"books/{bookId:N}/first.epub");
        var secondPath = Path.Combine(libraryRoot, $"books/{bookId:N}/second.pdf");
        var thirdPath = Path.Combine(libraryRoot, $"books/{bookId:N}/third.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        File.WriteAllText(firstPath, "first");
        File.WriteAllText(secondPath, "second");
        File.WriteAllText(thirdPath, "third");

        var expectedFirstPath = Path.GetFullPath(firstPath);
        var expectedSecondPath = Path.GetFullPath(secondPath);
        var expectedThirdPath = Path.GetFullPath(thirdPath);
        var epubAdapter = new CapturingMetadataAdapter(expectedFirstPath, MetadataWriteBackStatus.Unsupported);
        var pdfAdapter = new CapturingMetadataAdapter(expectedSecondPath, MetadataWriteBackStatus.Succeeded);
        var cbzAdapter = new CapturingMetadataAdapter(expectedThirdPath, MetadataWriteBackStatus.Failed, "cover mismatch");
        var adapters = new Dictionary<EbookFormat, IMetadataAdapter>
        {
            [EbookFormat.Epub] = epubAdapter,
            [EbookFormat.Pdf] = pdfAdapter,
            [EbookFormat.Cbz] = cbzAdapter
        };
        var service = new BookService(
            repo,
            new ManagedLibraryFileStore(libraryRoot),
            new DictionaryMetadataAdapterResolver(adapters));

        var result = await service.SaveAsync(updatedBook, default);

        result.Status.Should().Be(BookSaveStatus.Succeeded);
        result.FileResults.Select(file => file.Result.Status).Should().Equal(
            MetadataWriteBackStatus.Unsupported,
            MetadataWriteBackStatus.Succeeded,
            MetadataWriteBackStatus.Failed);
        repo.UpdateAsyncCalled.Should().BeTrue();
        repo.ListFilesAsyncCalled.Should().BeTrue();
        repo.UpdateFileWriteBackCalls.Should().HaveCount(3);
        repo.UpdateFileWriteBackCalls.Select(call => call.Result.Status).Should().Equal(
            MetadataWriteBackStatus.Unsupported,
            MetadataWriteBackStatus.Succeeded,
            MetadataWriteBackStatus.Failed);
        repo.StoredFiles.Select(file => file.WriteBackStatus).Should().Equal(
            MetadataWriteBackStatus.Unsupported,
            MetadataWriteBackStatus.Succeeded,
            MetadataWriteBackStatus.Failed);
        epubAdapter.CapturedPaths.Should().Equal(expectedFirstPath);
        pdfAdapter.CapturedPaths.Should().Equal(expectedSecondPath);
        cbzAdapter.CapturedPaths.Should().Equal(expectedThirdPath);
        repo.StoredBook!.Metadata.Should().Be(updatedBook.Metadata);
    }

    [Fact]
    public async Task Save_writes_one_sidecar_metadata_file_per_book_directory()
    {
        var libraryRoot = temporaryDirectory.CreateSubdirectory("Library").FullName;
        var bookId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var updatedBook = new Book(
            bookId,
            new BookMetadata("Corrected Title", ["Corrected Author"], Tags: ["Thriller"]),
            ReadingStatus.Read,
            null,
            now,
            now);
        var firstFile = CreateBookFile(bookId, "books", "book.epub", EbookFormat.Epub);
        var secondFile = CreateBookFile(bookId, "books", "book.pdf", EbookFormat.Pdf);
        var repo = new InMemoryBookRepository(updatedBook, [firstFile, secondFile]);
        var bookDirectory = Path.Combine(libraryRoot, "books", bookId.ToString("N"));
        Directory.CreateDirectory(bookDirectory);
        File.WriteAllText(Path.Combine(bookDirectory, "book.epub"), "epub");
        File.WriteAllText(Path.Combine(bookDirectory, "book.pdf"), "pdf");
        var sidecars = new RecordingMetadataSidecarStore();
        var service = new BookService(
            repo,
            new ManagedLibraryFileStore(libraryRoot),
            new DictionaryMetadataAdapterResolver(new Dictionary<EbookFormat, IMetadataAdapter>
            {
                [EbookFormat.Epub] = new CapturingMetadataAdapter(Path.Combine(bookDirectory, "book.epub"), MetadataWriteBackStatus.Unsupported),
                [EbookFormat.Pdf] = new CapturingMetadataAdapter(Path.Combine(bookDirectory, "book.pdf"), MetadataWriteBackStatus.Unsupported)
            }),
            sidecars);

        var result = await service.SaveAsync(updatedBook, default);

        result.Status.Should().Be(BookSaveStatus.Succeeded);
        sidecars.Writes.Should().ContainSingle();
        sidecars.Writes.Single().BookFilePath.Should().Be(Path.GetFullPath(Path.Combine(bookDirectory, "book.epub")));
        sidecars.Writes.Single().Metadata.Should().Be(updatedBook.Metadata);
    }

    [Fact]
    public async Task Save_returns_conflict_when_the_repository_rejects_duplicate_metadata()
    {
        var repo = new InMemoryBookRepository();
        repo.SetUpdateConflict();
        var service = new BookService(
            repo,
            new ThrowingLibraryFileStore(),
            new DictionaryMetadataAdapterResolver(new Dictionary<EbookFormat, IMetadataAdapter>()));
        var book = CreateBook();

        var result = await service.SaveAsync(book, default);

        result.Status.Should().Be(BookSaveStatus.Conflict);
        result.FileResults.Should().BeEmpty();
        repo.UpdateAsyncCalled.Should().BeTrue();
        repo.ListFilesAsyncCalled.Should().BeFalse();
        repo.UpdateFileWriteBackCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_removes_the_directory_before_deleting_the_database_record()
    {
        var libraryRoot = temporaryDirectory.CreateSubdirectory("Library").FullName;
        var book = CreateBook();
        var bookDirectory = Path.Combine(libraryRoot, "books", book.Id.ToString("N"));
        Directory.CreateDirectory(bookDirectory);
        File.WriteAllText(Path.Combine(bookDirectory, "book.epub"), "content");

        var deleteSawMissingDirectory = false;
        var repo = new InMemoryBookRepository(
            book,
            [CreateBookFile(book.Id, "books", "book.epub", EbookFormat.Epub)],
            _ => deleteSawMissingDirectory = !Directory.Exists(bookDirectory));
        var service = new BookService(
            repo,
            new ManagedLibraryFileStore(libraryRoot),
            new DictionaryMetadataAdapterResolver(new Dictionary<EbookFormat, IMetadataAdapter>()));

        var result = await service.DeleteAsync(book.Id, default);

        result.Status.Should().Be(BookDeleteStatus.Deleted);
        Directory.Exists(bookDirectory).Should().BeFalse();
        repo.DeleteAsyncCalled.Should().BeTrue();
        deleteSawMissingDirectory.Should().BeTrue();
        repo.ContainsBook(book.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_returns_a_cleanup_warning_and_leaves_the_record_intact_when_filesystem_deletion_fails()
    {
        var book = CreateBook();
        var repo = new InMemoryBookRepository(book, [CreateBookFile(book.Id, "books", "book.epub", EbookFormat.Epub)]);
        var service = new BookService(
            repo,
            new ThrowingLibraryFileStore(),
            new DictionaryMetadataAdapterResolver(new Dictionary<EbookFormat, IMetadataAdapter>()));

        var result = await service.DeleteAsync(book.Id, default);

        result.Status.Should().Be(BookDeleteStatus.CleanupWarning);
        repo.DeleteAsyncCalled.Should().BeFalse();
        repo.ContainsBook(book.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Save_propagates_cancellation_before_work_starts()
    {
        var repo = new InMemoryBookRepository();
        var service = new BookService(
            repo,
            new ThrowingLibraryFileStore(),
            new DictionaryMetadataAdapterResolver(new Dictionary<EbookFormat, IMetadataAdapter>()));
        var book = CreateBook();

        var act = () => service.SaveAsync(book, new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        repo.UpdateAsyncCalled.Should().BeFalse();
        repo.ListFilesAsyncCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_propagates_cancellation_before_work_starts()
    {
        var repo = new InMemoryBookRepository(CreateBook(), Array.Empty<BookFile>());
        var service = new BookService(
            repo,
            new ThrowingLibraryFileStore(),
            new DictionaryMetadataAdapterResolver(new Dictionary<EbookFormat, IMetadataAdapter>()));

        var act = () => service.DeleteAsync(Guid.NewGuid(), new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        repo.DeleteAsyncCalled.Should().BeFalse();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        temporaryDirectory.Dispose();
        return Task.CompletedTask;
    }

    private static Book CreateBook()
    {
        var now = DateTimeOffset.UtcNow;
        return new Book(
            Guid.NewGuid(),
            new BookMetadata("Title", ["Author"]),
            ReadingStatus.Unread,
            null,
            now,
            now);
    }

    private static BookFile CreateBookFile(Guid bookId, string folder, string fileName, EbookFormat format)
    {
        var relativePath = $"{folder}/{bookId:N}/{fileName}";
        return new BookFile(
            Guid.NewGuid(),
            bookId,
            format,
            relativePath,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileName))),
            Encoding.UTF8.GetByteCount(fileName),
            MetadataWriteBackStatus.NotAttempted,
            null);
    }

    private sealed class InMemoryBookRepository : IBookRepository
    {
        private readonly Dictionary<Guid, Book> books = new();
        private readonly Dictionary<Guid, List<BookFile>> filesByBookId = new();
        private readonly Action<Guid>? onDelete;
        private bool updateConflict;

        public InMemoryBookRepository()
        {
        }

        public InMemoryBookRepository(Book book, IReadOnlyList<BookFile> files, Action<Guid>? onDelete = null)
        {
            this.onDelete = onDelete;
            SeedBook(book);
            SeedFiles(book.Id, files);
        }

        public bool UpdateAsyncCalled { get; private set; }
        public bool ListFilesAsyncCalled { get; private set; }
        public bool DeleteAsyncCalled { get; private set; }
        public List<(Guid FileId, MetadataWriteResult Result)> UpdateFileWriteBackCalls { get; } = [];
        public List<BookFile> StoredFiles { get; } = [];
        public Book? StoredBook => books.Values.SingleOrDefault();

        public void SeedBook(Book book) => books[book.Id] = book;

        public void SeedFiles(Guid bookId, IReadOnlyList<BookFile>? files = null)
        {
            var bookFiles = files is null
                ? StoredFiles.Where(file => file.BookId == bookId).ToList()
                : [.. files];

            filesByBookId[bookId] = bookFiles;
            if (files is not null)
            {
                StoredFiles.Clear();
                StoredFiles.AddRange(bookFiles);
            }
        }

        public void SetUpdateConflict() => updateConflict = true;

        public bool ContainsBook(Guid bookId) => books.ContainsKey(bookId);

        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Book>>(books.Values.ToList());

        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(books.TryGetValue(id, out var book) ? book : null);

        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasNormalizedTitleAndAuthorAsync(
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken)
        {
            books[book.Id] = book;
            filesByBookId[book.Id] = [file];
            StoredFiles.Clear();
            StoredFiles.Add(file);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Book book, CancellationToken cancellationToken)
        {
            UpdateAsyncCalled = true;
            if (updateConflict)
            {
                throw new BookConflictException();
            }

            books[book.Id] = book;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            DeleteAsyncCalled = true;
            onDelete?.Invoke(id);
            books.Remove(id);
            filesByBookId.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken)
        {
            ListFilesAsyncCalled = true;
            return Task.FromResult<IReadOnlyList<BookFile>>(filesByBookId.TryGetValue(bookId, out var files) ? [.. files] : []);
        }

        public Task UpdateFileWriteBackAsync(
            Guid fileId,
            MetadataWriteResult result,
            CancellationToken cancellationToken)
        {
            var file = StoredFiles.Single(x => x.Id == fileId);
            var updated = file with
            {
                WriteBackStatus = result.Status,
                WriteBackMessage = result.Message
            };
            var index = StoredFiles.FindIndex(x => x.Id == fileId);
            StoredFiles[index] = updated;
            UpdateFileWriteBackCalls.Add((fileId, result));
            if (filesByBookId.TryGetValue(file.BookId, out var files))
            {
                var fileIndex = files.FindIndex(x => x.Id == fileId);
                files[fileIndex] = updated;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DictionaryMetadataAdapterResolver(IReadOnlyDictionary<EbookFormat, IMetadataAdapter> adapters) : IMetadataAdapterResolver
    {
        public IMetadataAdapter Resolve(EbookFormat format) => adapters[format];
    }

    private sealed class CapturingMetadataAdapter(
        string expectedPath,
        MetadataWriteBackStatus status,
        string? message = null) : IMetadataAdapter
    {
        public List<string> CapturedPaths { get; } = [];

        public bool CanHandle(EbookFormat format) => true;

        public Task<MetadataReadResult> ReadAsync(string path, EbookFormat format, CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataReadResult(new BookMetadata("Unused", ["Unused"])));

        public Task<MetadataWriteResult> WriteAsync(
            string path,
            EbookFormat format,
            BookMetadata metadata,
            CancellationToken cancellationToken)
        {
            CapturedPaths.Add(path);
            path.Should().Be(expectedPath);
            return Task.FromResult(new MetadataWriteResult(status, message));
        }
    }

    private sealed class ThrowingLibraryFileStore : ILibraryFileStore
    {
        public string GetAbsolutePath(string relativePath) =>
            throw new NotSupportedException();

        public Task<(string RelativeBookPath, string? RelativeCoverPath)> CopyIntoLibraryAsync(
            Guid bookId,
            string sourcePath,
            byte[]? coverBytes,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteBookDirectoryAsync(Guid bookId, CancellationToken cancellationToken) =>
            throw new IOException("cleanup failed");
    }

    private sealed class RecordingMetadataSidecarStore : IMetadataSidecarStore
    {
        public List<(string BookFilePath, BookMetadata Metadata)> Writes { get; } = [];

        public Task<BookMetadata?> TryReadAsync(string bookFilePath, CancellationToken cancellationToken) =>
            Task.FromResult<BookMetadata?>(null);

        public Task WriteAsync(string bookFilePath, BookMetadata metadata, CancellationToken cancellationToken)
        {
            Writes.Add((bookFilePath, metadata));
            return Task.CompletedTask;
        }
    }
}
