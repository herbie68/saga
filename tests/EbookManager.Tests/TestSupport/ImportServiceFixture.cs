using System.Security.Cryptography;
using EbookManager.Application.Importing;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Infrastructure.Files;
using EbookManager.Infrastructure.Metadata;
using EbookManager.Infrastructure.Persistence;
using EbookManager.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EbookManager.Tests.TestSupport;

public sealed class ImportServiceFixture : IAsyncDisposable
{
    private readonly TemporaryDirectory temporaryDirectory;

    private ImportServiceFixture(
        TemporaryDirectory temporaryDirectory,
        string libraryPath,
        LibraryDbContextFactory contextFactory,
        EfBookRepository bookRepository,
        EfImportRepository importRepository,
        ManagedLibraryFileStore fileStore,
        Sha256FileHasher fileHasher,
        MetadataAdapterResolver metadataAdapterResolver)
    {
        this.temporaryDirectory = temporaryDirectory;
        LibraryPath = libraryPath;
        ContextFactory = contextFactory;
        BookRepository = bookRepository;
        ImportRepository = importRepository;
        FileStore = fileStore;
        FileHasher = fileHasher;
        MetadataAdapterResolver = metadataAdapterResolver;
    }

    public string LibraryPath { get; }

    public LibraryDbContextFactory ContextFactory { get; }

    public EfBookRepository BookRepository { get; }

    public EfImportRepository ImportRepository { get; }

    public ManagedLibraryFileStore FileStore { get; }

    public Sha256FileHasher FileHasher { get; }

    public MetadataAdapterResolver MetadataAdapterResolver { get; }

    public static async Task<ImportServiceFixture> CreateAsync()
    {
        var temporaryDirectory = new TemporaryDirectory();
        var libraryPath = temporaryDirectory.CreateSubdirectory("Library").FullName;
        var contextFactory = new LibraryDbContextFactory();

        await using (var context = contextFactory.Create(libraryPath))
        {
            await context.Database.MigrateAsync();
        }

        var bookRepository = new EfBookRepository(contextFactory, libraryPath);
        var importRepository = new EfImportRepository(contextFactory, libraryPath);
        var fileStore = new ManagedLibraryFileStore(libraryPath);
        var fileHasher = new Sha256FileHasher();
        var metadataAdapterResolver = new MetadataAdapterResolver(
            [new FallbackMetadataAdapter(), new EpubMetadataAdapter(), new CbzMetadataAdapter()]);

        return new ImportServiceFixture(
            temporaryDirectory,
            libraryPath,
            contextFactory,
            bookRepository,
            importRepository,
            fileStore,
            fileHasher,
            metadataAdapterResolver);
    }

    public ImportService CreateService(IBookRepository? bookRepository = null) =>
        new(
            bookRepository ?? BookRepository,
            ImportRepository,
            FileStore,
            FileHasher,
            MetadataAdapterResolver);

    public string WriteSourceFile(string relativePath, string content) =>
        WriteBytesFile(relativePath, System.Text.Encoding.UTF8.GetBytes(content));

    public string WriteBytesFile(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(temporaryDirectory.DirectoryPath, relativePath);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
        return fullPath;
    }

    public async Task<Book> SeedBookAsync(
        string title,
        string author,
        string sourceFileName,
        byte[] contentBytes,
        EbookFormat format = EbookFormat.Pdf)
    {
        var bookId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var book = new Book(
            bookId,
            new BookMetadata(title, [author]),
            ReadingStatus.Unread,
            null,
            now,
            now);
        var file = new BookFile(
            Guid.NewGuid(),
            bookId,
            format,
            $"books/{bookId:N}/{sourceFileName}",
            Convert.ToHexString(SHA256.HashData(contentBytes)),
            contentBytes.LongLength,
            MetadataWriteBackStatus.NotAttempted,
            null);

        await BookRepository.AddAsync(book, file, default);
        return book;
    }

    public async Task<ImportRunResult?> LoadImportRunAsync(Guid runId) =>
        await ImportRepository.GetAsync(runId, default);

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        temporaryDirectory.Dispose();
        return ValueTask.CompletedTask;
    }
}
