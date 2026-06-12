using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Domain.Metadata;
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
        var listedBook = (await repository.ListAsync(default)).Should().ContainSingle().Which;
        listedBook.Should().Be(book with
        {
            Metadata = new BookMetadata(
                book.Metadata.Title,
                book.Metadata.Authors,
                book.Metadata.Description,
                book.Metadata.Language,
                book.Metadata.Publisher,
                book.Metadata.PublicationDate,
                book.Metadata.Tags,
                book.Metadata.Series,
                book.Metadata.SeriesNumber,
                book.Metadata.Isbn)
        });
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
    public async Task ListAsync_omits_cover_bytes_for_fast_library_overviews()
    {
        using var library = new TemporaryLibrary();
        var factory = await CreateMigratedFactoryAsync(library.DirectoryPath);
        var repository = new EfBookRepository(factory, library.DirectoryPath);
        var book = CreateBook("Cover Heavy", ["Author"], coverBytes: [1, 2, 3, 4]);

        await repository.AddAsync(book, CreateFile(book.Id), default);

        var listed = (await repository.ListAsync(default)).Should().ContainSingle().Which;
        var full = await repository.GetAsync(book.Id, default);

        listed.Metadata.CoverBytes.Should().BeNull();
        full!.Metadata.CoverBytes.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task ListPageAsync_orders_by_normalized_title_for_indexed_library_loading()
    {
        using var library = new TemporaryLibrary();
        var factory = await CreateMigratedFactoryAsync(library.DirectoryPath);
        var repository = new EfBookRepository(factory, library.DirectoryPath);
        var first = CreateBook("apple", ["Author"], coverBytes: [1, 2, 3]);
        var second = CreateBook("Banana", ["Author"], coverBytes: [4, 5, 6]);

        await repository.AddAsync(second, CreateFile(second.Id, Hash('B')), default);
        await repository.AddAsync(first, CreateFile(first.Id, Hash('A')), default);

        var page = await repository.ListPageAsync(0, 10, default);

        page.Select(x => x.Metadata.Title).Should().Equal("apple", "Banana");
        page.Select(x => x.Metadata.CoverBytes).Should().OnlyContain(coverBytes => coverBytes == null);
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
    public async Task Repository_lists_files_in_stable_order_and_persists_writeback_results()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var repository = new EfBookRepository(factory, libraryPath);
        var book = CreateBook("Files", ["Author"]);
        var firstFile = CreateFile(book.Id, Hash('A')) with
        {
            RelativePath = $"books/{book.Id:N}/z.epub"
        };
        var secondFile = CreateFile(book.Id, Hash('B')) with
        {
            RelativePath = $"books/{book.Id:N}/a.epub"
        };
        await repository.AddAsync(book, firstFile, default);
        await using (var context = factory.Create(libraryPath))
        {
            context.BookFiles.Add(new BookFileEntity
            {
                Id = secondFile.Id,
                BookId = secondFile.BookId,
                Format = secondFile.Format,
                RelativePath = secondFile.RelativePath,
                Sha256 = secondFile.Sha256,
                SizeBytes = secondFile.SizeBytes,
                WriteBackStatus = secondFile.WriteBackStatus,
                WriteBackMessage = secondFile.WriteBackMessage
            });
            await context.SaveChangesAsync();
        }

        var files = await repository.ListFilesAsync(book.Id, default);
        await repository.UpdateFileWriteBackAsync(
            secondFile.Id,
            new MetadataWriteResult(MetadataWriteBackStatus.Failed, "metadata write failed"),
            default);

        files.Select(x => x.RelativePath).Should().Equal(secondFile.RelativePath, firstFile.RelativePath);
        await using var verificationContext = factory.Create(libraryPath);
        var reloadedSecondFile = await verificationContext.BookFiles.SingleAsync(x => x.Id == secondFile.Id);
        reloadedSecondFile.WriteBackStatus.Should().Be(MetadataWriteBackStatus.Failed);
        reloadedSecondFile.WriteBackMessage.Should().Be("metadata write failed");
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

    [Fact]
    public async Task Duplicate_snapshot_lists_existing_hashes_and_duplicate_keys()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var repository = new EfBookRepository(factory, libraryPath);
        var book = CreateBook("Snapshot Title", ["Snapshot Author"]);
        var hash = Hash('D');

        await repository.AddAsync(book, CreateFile(book.Id, sha256: hash), default);

        var snapshot = await repository.CreateDuplicateSnapshotAsync(default);

        snapshot.FileHashes.Should().Contain(hash);
        snapshot.DuplicateKeys.Should().Contain(
            DuplicateKeyNormalizer.BuildDuplicateKey(book.Metadata.Title, book.Metadata.Authors));
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
    public async Task Import_items_preserve_sequence_order_when_reloaded_from_the_repository()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var importRepository = new EfImportRepository(factory, libraryPath);
        var runId = await importRepository.StartRunAsync(DateTimeOffset.UtcNow, default);

        await importRepository.RecordItemAsync(runId, 0, "zeta.pdf", ImportOutcome.Added, "Imported", null, default);
        await importRepository.RecordItemAsync(runId, 1, "alpha.pdf", ImportOutcome.Added, "Imported", null, default);

        await using var context = factory.Create(libraryPath);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Sequence", SourcePath
            FROM ImportItems
            ORDER BY "Sequence"
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(long Sequence, string SourcePath)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        rows.Should().Equal((0, "zeta.pdf"), (1, "alpha.pdf"));
        (await importRepository.GetAsync(runId, default))!.Items.Select(x => x.SourcePath)
            .Should()
            .Equal("zeta.pdf", "alpha.pdf");
    }

    [Fact]
    public async Task Import_repository_lists_recent_runs_with_summary_counts()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var importRepository = new EfImportRepository(factory, libraryPath);
        var olderRunId = await importRepository.StartRunAsync(
            new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero),
            default);
        var newerRunId = await importRepository.StartRunAsync(
            new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero),
            default);

        await importRepository.RecordItemAsync(olderRunId, 0, "older.epub", ImportOutcome.Added, "Imported", null, default);
        await importRepository.CompleteRunAsync(olderRunId, new DateTimeOffset(2026, 6, 10, 12, 5, 0, TimeSpan.Zero), default);
        await importRepository.RecordItemAsync(newerRunId, 0, "duplicate.epub", ImportOutcome.ExactDuplicate, "Duplicate", null, default);
        await importRepository.RecordItemAsync(newerRunId, 1, "possible.epub", ImportOutcome.PossibleDuplicate, "Possible", null, default);
        await importRepository.RecordItemAsync(newerRunId, 2, "failed.epub", ImportOutcome.Failed, "Failed", null, default);

        var recentRuns = await importRepository.ListRecentAsync(10, default);

        recentRuns.Select(run => run.Id).Should().Equal(newerRunId, olderRunId);
        recentRuns[0].TotalCount.Should().Be(3);
        recentRuns[0].AddedCount.Should().Be(0);
        recentRuns[0].ExactDuplicateCount.Should().Be(1);
        recentRuns[0].PossibleDuplicateCount.Should().Be(1);
        recentRuns[0].SkippedCount.Should().Be(2);
        recentRuns[0].FailedCount.Should().Be(1);
        recentRuns[0].CompletedUtc.Should().BeNull();
        recentRuns[1].TotalCount.Should().Be(1);
        recentRuns[1].AddedCount.Should().Be(1);
        recentRuns[1].CompletedUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_repository_persists_run_context_for_scan_history()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var importRepository = new EfImportRepository(factory, libraryPath);
        var context = new ImportRunContext(
            ImportRunKind.DirectoryScan,
            @"C:\Books",
            IncludeSubdirectories: true);

        var runId = await importRepository.StartRunAsync(DateTimeOffset.UtcNow, context, default);
        await importRepository.RecordItemAsync(runId, 0, "book.epub", ImportOutcome.Added, "Imported", null, default);

        var run = await importRepository.GetAsync(runId, default);
        var summary = (await importRepository.ListRecentAsync(1, default)).Single();

        run!.Context.Should().Be(context);
        summary.Context.Should().Be(context);
    }

    [Fact]
    public async Task Books_reject_duplicate_logical_title_and_author_combinations()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var repository = new EfBookRepository(factory, libraryPath);
        var firstBook = CreateBook("Same", ["Author"], coverBytes: null);
        var secondBook = CreateBook("Same", ["Author"], coverBytes: null);

        await repository.AddAsync(firstBook, CreateFile(firstBook.Id, Hash('A')), default);

        var addDuplicate = () => repository.AddAsync(secondBook, CreateFile(secondBook.Id, Hash('B')), default);

        await addDuplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Books_with_pipe_characters_in_titles_and_authors_remain_distinct_under_duplicate_key_encoding()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var repository = new EfBookRepository(factory, libraryPath);
        var firstBook = CreateBook("Alpha|Beta", ["Gamma"], coverBytes: null);
        var secondBook = CreateBook("Alpha", ["Beta|Gamma"], coverBytes: null);

        await repository.AddAsync(firstBook, CreateFile(firstBook.Id, Hash('A')), default);

        var addSecond = () => repository.AddAsync(secondBook, CreateFile(secondBook.Id, Hash('B')), default);

        await addSecond.Should().NotThrowAsync();
        (await repository.ListAsync(default)).Should().HaveCount(2);
        (await repository.HasNormalizedTitleAndAuthorAsync(" Alpha|Beta ", [" Gamma "], default)).Should().BeTrue();
        (await repository.HasNormalizedTitleAndAuthorAsync(" Alpha ", [" Beta|Gamma "], default)).Should().BeTrue();
    }

    [Fact]
    public void Duplicate_key_normalizer_lowercases_ascii_only_and_preserves_non_ascii()
    {
        DuplicateKeyNormalizer.NormalizeSqliteText("  AbC ÄÖß  ").Should().Be("abc ÄÖß");
        DuplicateKeyNormalizer.NormalizeSqliteText("  ÅBC  ").Should().Be("Åbc");
        DuplicateKeyNormalizer.NormalizeSqliteText("Ä").Should().Be("Ä");
        DuplicateKeyNormalizer.NormalizeSqliteText("ä").Should().Be("ä");
        DuplicateKeyNormalizer.NormalizeSqliteText("Ä").Should().NotBe(DuplicateKeyNormalizer.NormalizeSqliteText("ä"));
    }

    [Fact]
    public async Task Migration_backfilled_duplicate_key_matches_runtime_key_for_non_ascii_values()
    {
        using var library = new TemporaryLibrary();
        var factory = new LibraryDbContextFactory();
        await using var context = factory.Create(library.DirectoryPath);
        await context.Database.MigrateAsync("20260602065847_InitialLibrarySchema");

        var bookId = Guid.NewGuid();
        var firstAuthorId = Guid.NewGuid();
        var secondAuthorId = Guid.NewGuid();
        var title = "  ÅBC  ";
        var firstAuthor = "ÉXAMPLE";
        var secondAuthor = "ASCII";
        var expectedKey = DuplicateKeyNormalizer.BuildDuplicateKey(title, [firstAuthor, secondAuthor]);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Books (Id, Title, NormalizedTitle, ReadingStatus, CreatedUtc, UpdatedUtc)
                VALUES ($bookId, $title, $normalizedTitle, 'Unread', $now, $now);
                INSERT INTO Authors (Id, Name, NormalizedName)
                VALUES ($firstAuthorId, $firstAuthor, $firstNormalizedName),
                       ($secondAuthorId, $secondAuthor, $secondNormalizedName);
                INSERT INTO BookAuthors (BookId, AuthorId, "Order")
                VALUES ($bookId, $firstAuthorId, 0), ($bookId, $secondAuthorId, 1);
                """;
            command.Parameters.AddWithValue("$bookId", bookId);
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$normalizedTitle", DuplicateKeyNormalizer.NormalizeSqliteText(title));
            command.Parameters.AddWithValue("$firstAuthorId", firstAuthorId);
            command.Parameters.AddWithValue("$secondAuthorId", secondAuthorId);
            command.Parameters.AddWithValue("$firstAuthor", firstAuthor);
            command.Parameters.AddWithValue("$secondAuthor", secondAuthor);
            command.Parameters.AddWithValue("$firstNormalizedName", DuplicateKeyNormalizer.NormalizeSqliteText(firstAuthor));
            command.Parameters.AddWithValue("$secondNormalizedName", DuplicateKeyNormalizer.NormalizeSqliteText(secondAuthor));
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync();
        }

        await context.Database.MigrateAsync();

        var runtimeKey = await context.Books.AsNoTracking()
            .Where(x => x.Id == bookId)
            .Select(x => x.DuplicateKey)
            .SingleAsync();

        runtimeKey.Should().Be(expectedKey);
    }

    [Fact]
    public async Task Duplicate_key_lookup_folds_ascii_case_but_keeps_non_ascii_case_stable()
    {
        using var library = new TemporaryLibrary();
        var libraryPath = library.DirectoryPath;
        var factory = await CreateMigratedFactoryAsync(libraryPath);
        var repository = new EfBookRepository(factory, libraryPath);
        var book = CreateBook("  ÅBC  ", ["ÉXAMPLE"]);

        await repository.AddAsync(book, CreateFile(book.Id), default);

        (await repository.HasNormalizedTitleAndAuthorAsync("  Åbc  ", ["Éxample"], default)).Should().BeTrue();
        (await repository.HasNormalizedTitleAndAuthorAsync("  åbc  ", ["éxample"], default)).Should().BeFalse();
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
    public async Task Model_indexes_library_list_sort_key_for_paged_loading()
    {
        using var library = new TemporaryLibrary();
        var factory = await CreateMigratedFactoryAsync(library.DirectoryPath);
        await using var context = factory.Create(library.DirectoryPath);

        var indexes = context.Model.FindEntityType(typeof(BookEntity))!.GetIndexes();

        indexes.Should().Contain(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[] { nameof(BookEntity.NormalizedTitle), nameof(BookEntity.Id) }));
    }

    [Fact]
    public async Task Migration_creates_library_list_sort_index_on_disk()
    {
        using var library = new TemporaryLibrary();
        var factory = await CreateMigratedFactoryAsync(library.DirectoryPath);
        await using var context = factory.Create(library.DirectoryPath);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sql
            FROM sqlite_master
            WHERE type = 'index' AND name = 'IX_Books_NormalizedTitle_Id'
            """;

        var indexSql = (string?)await command.ExecuteScalarAsync();

        indexSql.Should().Be(
            "CREATE INDEX \"IX_Books_NormalizedTitle_Id\" ON \"Books\" (\"NormalizedTitle\", \"Id\")");
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

    [Fact]
    public async Task Metadata_hardening_migration_canonicalizes_existing_hashes_and_blocks_casing_duplicates()
    {
        using var library = new TemporaryLibrary();
        var factory = new LibraryDbContextFactory();
        await using var context = factory.Create(library.DirectoryPath);
        await context.Database.MigrateAsync("20260602065847_InitialLibrarySchema");
        var firstBookId = Guid.NewGuid();
        var secondBookId = Guid.NewGuid();
        var lowercaseHash = Hash('a');
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Books (Id, Title, NormalizedTitle, ReadingStatus, CreatedUtc, UpdatedUtc)
                VALUES ($firstBookId, 'Existing', 'existing', 'Unread', $now, $now);
                INSERT INTO BookFiles (Id, BookId, Format, RelativePath, Sha256, SizeBytes, WriteBackStatus)
                VALUES ($fileId, $firstBookId, 'Epub', 'books/existing.epub', $sha256, 123, 'NotAttempted');
                """;
            command.Parameters.AddWithValue("$firstBookId", firstBookId);
            command.Parameters.AddWithValue("$fileId", Guid.NewGuid());
            command.Parameters.AddWithValue("$sha256", lowercaseHash);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync();
        }

        await context.Database.MigrateAsync();

        (await context.BookFiles.SingleAsync()).Sha256.Should().Be(Hash('A'));
        var repository = new EfBookRepository(factory, library.DirectoryPath);
        (await repository.HasHashAsync(lowercaseHash, default)).Should().BeTrue();
        (await repository.HasHashAsync(Hash('A'), default)).Should().BeTrue();

        await using var duplicateCommand = connection.CreateCommand();
        duplicateCommand.CommandText = """
            INSERT INTO Books (Id, Title, NormalizedTitle, ReadingStatus, CreatedUtc, UpdatedUtc)
            VALUES ($secondBookId, 'Duplicate', 'duplicate', 'Unread', $now, $now);
            INSERT INTO BookFiles (Id, BookId, Format, RelativePath, Sha256, SizeBytes, WriteBackStatus)
            VALUES ($fileId, $secondBookId, 'Epub', 'books/duplicate.epub', $sha256, 123, 'NotAttempted');
            """;
        duplicateCommand.Parameters.AddWithValue("$secondBookId", secondBookId);
        duplicateCommand.Parameters.AddWithValue("$fileId", Guid.NewGuid());
        duplicateCommand.Parameters.AddWithValue("$sha256", lowercaseHash);
        duplicateCommand.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);

        var insertDuplicate = () => duplicateCommand.ExecuteNonQueryAsync();

        await insertDuplicate.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task Metadata_hardening_migration_rejects_malformed_legacy_hash_and_rolls_back()
    {
        using var library = new TemporaryLibrary();
        var factory = new LibraryDbContextFactory();
        await using var context = factory.Create(library.DirectoryPath);
        await context.Database.MigrateAsync("20260602065847_InitialLibrarySchema");
        var malformedHash = "not-a-sha256";
        await SeedLegacyBookFileAsync(context, malformedHash);

        var migrate = () => context.Database.MigrateAsync();

        var exception = await migrate.Should().ThrowAsync<SqliteException>();
        exception.Which.Message.Should()
            .Contain("REPAIR_REQUIRED_LEGACY_BOOKFILES_SHA256_MALFORMED_EXPECTED_64_HEX_CHARS");
        await AssertFailedHardeningMigrationLeftLegacyHashRecoverableAsync(context, malformedHash);
    }

    [Fact]
    public async Task Metadata_hardening_migration_rejects_case_insensitive_legacy_hash_collision_and_rolls_back()
    {
        using var library = new TemporaryLibrary();
        var factory = new LibraryDbContextFactory();
        await using var context = factory.Create(library.DirectoryPath);
        await context.Database.MigrateAsync("20260602065847_InitialLibrarySchema");
        var lowercaseHash = Hash('a');
        var uppercaseHash = Hash('A');
        await SeedLegacyBookFileAsync(context, lowercaseHash);
        await SeedLegacyBookFileAsync(context, uppercaseHash);

        var migrate = () => context.Database.MigrateAsync();

        var exception = await migrate.Should().ThrowAsync<SqliteException>();
        exception.Which.Message.Should()
            .Contain("REPAIR_REQUIRED_LEGACY_BOOKFILES_SHA256_CASE_INSENSITIVE_DUPLICATES");
        await AssertFailedHardeningMigrationLeftLegacyHashRecoverableAsync(
            context,
            lowercaseHash,
            uppercaseHash);
    }

    private static async Task SeedLegacyBookFileAsync(
        LibraryDbContext context,
        string sha256)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Books (Id, Title, NormalizedTitle, ReadingStatus, CreatedUtc, UpdatedUtc)
            VALUES ($bookId, $title, $normalizedTitle, 'Unread', $now, $now);
            INSERT INTO BookFiles (Id, BookId, Format, RelativePath, Sha256, SizeBytes, WriteBackStatus)
            VALUES ($fileId, $bookId, 'Epub', $relativePath, $sha256, 123, 'NotAttempted');
            """;
        var bookId = Guid.NewGuid();
        command.Parameters.AddWithValue("$bookId", bookId);
        command.Parameters.AddWithValue("$title", bookId.ToString());
        command.Parameters.AddWithValue("$normalizedTitle", bookId.ToString());
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("$fileId", Guid.NewGuid());
        command.Parameters.AddWithValue("$relativePath", $"books/{bookId:N}/book.epub");
        command.Parameters.AddWithValue("$sha256", sha256);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertFailedHardeningMigrationLeftLegacyHashRecoverableAsync(
        LibraryDbContext context,
        params string[] expectedHashes)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var migrations = await context.Database.GetAppliedMigrationsAsync();
        migrations.Should().Equal("20260602065847_InitialLibrarySchema");
        (await context.BookFiles.OrderBy(x => x.Sha256).Select(x => x.Sha256).ToListAsync())
            .Should().Equal(expectedHashes.OrderBy(x => x, StringComparer.Ordinal));

        await using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = """SELECT name FROM pragma_table_info('BookTags') WHERE name = 'Order'""";
        (await columnsCommand.ExecuteScalarAsync()).Should().BeNull();

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_BookFiles_Sha256'""";
        var indexSql = (string?)await indexCommand.ExecuteScalarAsync();
        indexSql.Should().NotBeNull();
        indexSql.Should().NotContain("NOCASE");
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
