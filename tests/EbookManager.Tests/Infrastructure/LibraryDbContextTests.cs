using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Infrastructure.Persistence;
using EbookManager.Infrastructure.Persistence.Entities;
using EbookManager.Infrastructure.Persistence.Repositories;
using EbookManager.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EbookManager.Tests.Infrastructure;

public sealed class LibraryDbContextTests
{
    [Fact]
    public async Task Migration_and_repository_round_trip_full_metadata_to_library_database()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = new LibraryDbContextFactory();
        await using (var context = factory.Create(libraryPath))
        {
            await context.Database.MigrateAsync();
        }

        var repository = new EfBookRepository(factory, libraryPath);
        var book = CreateBook(
            title: "Test Book",
            authors: ["First Author", "Second Author"],
            tags: ["SQLite", "Reference"],
            publicationDate: new DateOnly(2026, 6, 1),
            coverBytes: [1, 2, 3]);
        var file = CreateFile(book.Id, sha256: Hash('A'));

        await repository.AddAsync(book, file, default);

        File.Exists(Path.Combine(libraryPath, "library.db")).Should().BeTrue();
        (await repository.ListAsync(default)).Should().ContainSingle().Which.Should().Be(book);
        (await repository.GetAsync(book.Id, default)).Should().Be(book);
        (await repository.HasHashAsync(Hash('A'), default)).Should().BeTrue();
        (await repository.HasNormalizedTitleAndAuthorAsync(
            "  TEST BOOK ",
            [" first author ", "SECOND AUTHOR"],
            default)).Should().BeTrue();

        var firstRead = await repository.GetAsync(book.Id, default);
        firstRead!.Metadata.CoverBytes![0] = 99;
        (await repository.GetAsync(book.Id, default))!.Metadata.CoverBytes.Should().Equal(1, 2, 3);

        await using var verificationContext = factory.Create(libraryPath);
        (await verificationContext.Authors.OrderBy(x => x.Name).Select(x => x.Name).ToListAsync())
            .Should().Equal("First Author", "Second Author");
        (await verificationContext.Tags.OrderBy(x => x.Name).Select(x => x.Name).ToListAsync())
            .Should().Equal("Reference", "SQLite");

        var connection = (SqliteConnection)verificationContext.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PublicationDate, ReadingStatus, Format, WriteBackStatus
            FROM Books
            JOIN BookFiles ON Books.Id = BookFiles.BookId
            WHERE Books.Id = $id
            """;
        command.Parameters.AddWithValue("$id", book.Id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("2026-06-01");
        reader.GetString(1).Should().Be(nameof(ReadingStatus.Reading));
        reader.GetString(2).Should().Be(nameof(EbookFormat.Epub));
        reader.GetString(3).Should().Be(nameof(MetadataWriteBackStatus.NotAttempted));
    }

    [Fact]
    public async Task Factory_creates_library_database_when_directory_contains_a_semicolon()
    {
        using var library = new TemporaryLibrary("ELibrary;Portable");
        var factory = new LibraryDbContextFactory();

        await using (var context = factory.Create(library.DirectoryPath))
        {
            await context.Database.MigrateAsync();
        }

        File.Exists(Path.Combine(library.DirectoryPath, "library.db")).Should().BeTrue();
    }

    [Fact]
    public async Task Add_filters_blank_metadata_and_deduplicates_normalized_values_in_first_seen_order()
    {
        using var library = new TemporaryLibrary();
        var factory = await CreateMigratedFactoryAsync(library.DirectoryPath);
        var repository = new EfBookRepository(factory, library.DirectoryPath);
        var book = CreateBook(
            "Normalized Metadata",
            [" First Author ", "", "first author", " Second Author ", "   "],
            [" Third Tag ", "", "third tag", " First Tag ", "  "]);

        await repository.AddAsync(book, CreateFile(book.Id), default);

        var reloaded = await repository.GetAsync(book.Id, default);
        reloaded!.Metadata.Authors.Should().Equal("First Author", "Second Author");
        reloaded.Metadata.Tags.Should().Equal("Third Tag", "First Tag");
    }

    [Fact]
    public async Task Update_preserves_metadata_order_and_refreshes_shared_display_casing()
    {
        using var library = new TemporaryLibrary();
        var factory = await CreateMigratedFactoryAsync(library.DirectoryPath);
        var repository = new EfBookRepository(factory, library.DirectoryPath);
        var firstBook = CreateBook("First", ["john doe"], ["science fiction", "Mystery"]);
        var secondBook = CreateBook("Second", ["john doe"], ["science fiction"]);
        await repository.AddAsync(firstBook, CreateFile(firstBook.Id, Hash('A')), default);
        await repository.AddAsync(secondBook, CreateFile(secondBook.Id, Hash('B')), default);
        var updated = firstBook with
        {
            Metadata = new BookMetadata(
                "First",
                ["", "John Doe", "john doe", " Alice "],
                Tags: ["", "Science Fiction", "science fiction", " New Tag "])
        };

        await repository.UpdateAsync(updated, default);

        var reloadedFirst = await repository.GetAsync(firstBook.Id, default);
        reloadedFirst!.Metadata.Authors.Should().Equal("John Doe", "Alice");
        reloadedFirst.Metadata.Tags.Should().Equal("Science Fiction", "New Tag");
        var reloadedSecond = await repository.GetAsync(secondBook.Id, default);
        reloadedSecond!.Metadata.Authors.Should().Equal("John Doe");
        reloadedSecond.Metadata.Tags.Should().Equal("Science Fiction");

        await using var context = factory.Create(library.DirectoryPath);
        (await context.Authors.OrderBy(x => x.NormalizedName).Select(x => x.Name).ToListAsync())
            .Should().Equal("Alice", "John Doe");
        (await context.Tags.OrderBy(x => x.NormalizedName).Select(x => x.Name).ToListAsync())
            .Should().Equal("New Tag", "Science Fiction");
    }

    [Fact]
    public async Task Repository_preserves_author_order_updates_metadata_and_deletes_books()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var repository = new EfBookRepository(factory, libraryPath);
        var book = CreateBook("Ordered Authors", ["Second", "First"], ["Old"]);

        await repository.AddAsync(book, CreateFile(book.Id), default);

        (await repository.GetAsync(book.Id, default))!.Metadata.Authors.Should().Equal("Second", "First");

        var updated = book with
        {
            Metadata = new BookMetadata(
                "Updated",
                ["Third", "Second", "First"],
                Tags: ["New"]),
            ReadingStatus = ReadingStatus.Read,
            UpdatedUtc = book.UpdatedUtc.AddMinutes(1)
        };
        await repository.UpdateAsync(updated, default);

        var reloaded = await repository.GetAsync(book.Id, default);
        reloaded.Should().Be(updated);
        reloaded!.Metadata.Authors.Should().Equal("Third", "Second", "First");
        reloaded.Metadata.Tags.Should().Equal("New");

        await repository.DeleteAsync(book.Id, default);

        (await repository.ListAsync(default)).Should().BeEmpty();
        (await repository.GetAsync(book.Id, default)).Should().BeNull();
        await using var context = factory.Create(libraryPath);
        (await context.BookFiles.AnyAsync()).Should().BeFalse();
        (await context.BookAuthors.AnyAsync()).Should().BeFalse();
        (await context.BookTags.AnyAsync()).Should().BeFalse();
        (await context.Authors.AnyAsync()).Should().BeFalse();
        (await context.Tags.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Hashes_are_canonicalized_and_duplicate_add_rolls_back_all_rows()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var repository = new EfBookRepository(factory, libraryPath);
        var firstBook = CreateBook("First", ["First Author"], ["First Tag"]);
        var secondBook = CreateBook("Second", ["Second Author"], ["Second Tag"]);
        var canonicalHash = Hash('C');

        await repository.AddAsync(firstBook, CreateFile(firstBook.Id, sha256: canonicalHash.ToLowerInvariant()), default);
        (await repository.HasHashAsync(canonicalHash, default)).Should().BeTrue();
        (await repository.HasHashAsync(canonicalHash.ToLowerInvariant(), default)).Should().BeTrue();
        var addDuplicate = () => repository.AddAsync(
            secondBook,
            CreateFile(secondBook.Id, sha256: canonicalHash.ToLowerInvariant()),
            default);

        await addDuplicate.Should().ThrowAsync<DbUpdateException>();

        await using var context = factory.Create(libraryPath);
        (await context.Books.Select(x => x.Id).ToListAsync()).Should().Equal(firstBook.Id);
        (await context.Authors.Select(x => x.Name).ToListAsync()).Should().Equal("First Author");
        (await context.Tags.Select(x => x.Name).ToListAsync()).Should().Equal("First Tag");
        (await context.BookFiles.SingleAsync()).Sha256.Should().Be(canonicalHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public async Task Invalid_hashes_are_rejected_without_persisting_book_rows(string hash)
    {
        using var library = new TemporaryLibrary();
        var factory = await CreateMigratedFactoryAsync(library.DirectoryPath);
        var repository = new EfBookRepository(factory, library.DirectoryPath);
        var book = CreateBook("Invalid Hash", ["Author"], ["Tag"]);

        var hasHash = () => repository.HasHashAsync(hash, default);
        var add = () => repository.AddAsync(book, CreateFile(book.Id, hash), default);

        await hasHash.Should().ThrowAsync<ArgumentException>();
        await add.Should().ThrowAsync<ArgumentException>();
        await using var context = factory.Create(library.DirectoryPath);
        (await context.Books.AnyAsync()).Should().BeFalse();
        (await context.Authors.AnyAsync()).Should().BeFalse();
        (await context.Tags.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Import_entities_are_persisted_by_relational_schema()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var repository = new EfBookRepository(factory, libraryPath);
        var firstBook = CreateBook("First", ["Author"]);
        await repository.AddAsync(firstBook, CreateFile(firstBook.Id), default);

        await using var context = factory.Create(libraryPath);
        var importRun = new ImportRunEntity
        {
            Id = Guid.NewGuid(),
            StartedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };
        importRun.Items.Add(new ImportItemEntity
        {
            Id = Guid.NewGuid(),
            SourcePath = "source.epub",
            Outcome = ImportOutcome.Added,
            Message = "Imported",
            BookId = firstBook.Id
        });
        context.ImportRuns.Add(importRun);
        await context.SaveChangesAsync();

        (await context.ImportItems.SingleAsync()).Outcome.Should().Be(ImportOutcome.Added);
    }

    [Fact]
    public void Design_time_factory_uses_a_temporary_sqlite_database()
    {
        var factory = new DesignTimeLibraryDbContextFactory();

        using var context = factory.CreateDbContext([]);

        context.Database.GetDbConnection().DataSource.Should().StartWith(Path.GetTempPath());
        context.Database.GetDbConnection().DataSource.Should().EndWith("library.db");
    }

    [Fact]
    public async Task Model_indexes_normalized_titles_for_duplicate_lookup()
    {
        using var library = new TemporaryLibrary();
        var factory = await CreateMigratedFactoryAsync(library.DirectoryPath);
        await using var context = factory.Create(library.DirectoryPath);

        var indexes = context.Model.FindEntityType(typeof(BookEntity))!.GetIndexes();

        indexes.Should().Contain(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[] { nameof(BookEntity.NormalizedTitle) }));
    }

    [Fact]
    public async Task Metadata_hardening_migration_backfills_order_for_existing_book_tags()
    {
        using var library = new TemporaryLibrary();
        var factory = new LibraryDbContextFactory();
        await using var context = factory.Create(library.DirectoryPath);
        await context.Database.MigrateAsync("20260602065847_InitialLibrarySchema");
        var bookId = Guid.NewGuid();
        var firstTagId = Guid.NewGuid();
        var secondTagId = Guid.NewGuid();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Books (Id, Title, NormalizedTitle, ReadingStatus, CreatedUtc, UpdatedUtc)
                VALUES ($bookId, 'Existing', 'existing', 'Unread', $now, $now);
                INSERT INTO Tags (Id, Name, NormalizedName)
                VALUES ($firstTagId, 'First', 'first'), ($secondTagId, 'Second', 'second');
                INSERT INTO BookTags (BookId, TagId)
                VALUES ($bookId, $firstTagId), ($bookId, $secondTagId);
                """;
            command.Parameters.AddWithValue("$bookId", bookId);
            command.Parameters.AddWithValue("$firstTagId", firstTagId);
            command.Parameters.AddWithValue("$secondTagId", secondTagId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync();
        }

        await context.Database.MigrateAsync();

        await using var verificationCommand = connection.CreateCommand();
        verificationCommand.CommandText = """
            SELECT "Order"
            FROM BookTags
            WHERE BookId = $bookId
            ORDER BY "Order"
            """;
        verificationCommand.Parameters.AddWithValue("$bookId", bookId);
        await using var reader = await verificationCommand.ExecuteReaderAsync();
        var orders = new List<long>();
        while (await reader.ReadAsync())
        {
            orders.Add(reader.GetInt64(0));
        }

        orders.Should().Equal(0, 1);
    }

    private static async Task<LibraryDbContextFactory> CreateMigratedFactoryAsync(string libraryPath)
    {
        var factory = new LibraryDbContextFactory();
        await using var context = factory.Create(libraryPath);
        await context.Database.MigrateAsync();
        return factory;
    }

    private static Book CreateBook(
        string title,
        IReadOnlyList<string> authors,
        IReadOnlyList<string>? tags = null,
        DateOnly? publicationDate = null,
        byte[]? coverBytes = null)
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
                PublicationDate: publicationDate,
                Tags: tags,
                Series: "Series",
                SeriesNumber: 1.5m,
                Isbn: "9780000000000",
                CoverBytes: coverBytes),
            ReadingStatus.Reading,
            "books/cover.jpg",
            now,
            now);
    }

    private static BookFile CreateFile(Guid bookId, string? sha256 = null) =>
        new(
            Guid.NewGuid(),
            bookId,
            EbookFormat.Epub,
            $"books/{bookId:N}/book.epub",
            sha256 ?? Hash('F'),
            123,
            MetadataWriteBackStatus.NotAttempted,
            null);

    private static string Hash(char character) => new(character, 64);

    private sealed class TemporaryLibrary : IDisposable
    {
        private readonly TemporaryDirectory _temporaryDirectory = new();

        public TemporaryLibrary(string name = "ELibrary")
        {
            DirectoryPath = _temporaryDirectory.CreateSubdirectory(name).FullName;
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            _temporaryDirectory.Dispose();
        }
    }
}
