using System.Security.Cryptography;
using System.Text;
using EbookManager.Application.Importing;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Domain.Metadata;
using EbookManager.Infrastructure.Metadata;
using EbookManager.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EbookManager.Tests.Importing;

public sealed class ImportServiceTests
{
    [Fact]
    public async Task Import_async_copies_files_and_preserves_the_session_source_path()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var service = fixture.CreateService();
        var sourceBytes = Encoding.UTF8.GetBytes("the-hobbit");
        var source = fixture.WriteBytesFile(
            @"incoming\The Hobbit - J.R.R. Tolkien.pdf",
            sourceBytes);

        var result = await service.ImportAsync([source], default);

        result.Items.Should().ContainSingle();
        var item = result.Items.Single();
        item.SourcePath.Should().Be(source);
        item.Outcome.Should().Be(ImportOutcome.Added);
        item.BookId.Should().NotBeNull();

        var book = await fixture.BookRepository.GetAsync(item.BookId!.Value, default);
        book.Should().NotBeNull();
        book!.Metadata.Title.Should().Be("The Hobbit");
        book.Metadata.Authors.Should().Equal("J.R.R. Tolkien");

        await using var context = fixture.ContextFactory.Create(fixture.LibraryPath);
        var file = await context.BookFiles.SingleAsync();
        file.RelativePath.Should().Be($"books/{item.BookId:N}/The Hobbit - J.R.R. Tolkien.pdf");
        file.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(sourceBytes)));
        Directory.Exists(Path.Combine(fixture.LibraryPath, "books", item.BookId.Value.ToString("N")))
            .Should()
            .BeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, file.RelativePath)).Should().BeTrue();
    }

    [Fact]
    public async Task Import_async_prefers_sidecar_metadata_when_available_next_to_source_file()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var sidecars = new ReturningMetadataSidecarStore(
            new BookMetadata("Corrected Title", ["Corrected Author"], Tags: ["Imported tag"]));
        var service = new ImportService(
            fixture.BookRepository,
            fixture.ImportRepository,
            fixture.FileStore,
            fixture.FileHasher,
            fixture.MetadataAdapterResolver,
            fixture.ExceptionClassifier,
            sidecars);
        var source = fixture.WriteBytesFile(
            @"incoming\Wrong Title - Wrong Author.pdf",
            Encoding.UTF8.GetBytes("sidecar-import"));

        var result = await service.ImportAsync([source], default);

        var bookId = result.Items.Single().BookId!.Value;
        var book = await fixture.BookRepository.GetAsync(bookId, default);
        book!.Metadata.Title.Should().Be("Corrected Title");
        book.Metadata.Authors.Should().Equal("Corrected Author");
        book.Metadata.Tags.Should().Equal("Imported tag");
        sidecars.ReadPaths.Should().Equal(source);
    }

    [Fact]
    public async Task Import_async_skips_exact_duplicates_without_copying_them()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var duplicateBytes = Encoding.UTF8.GetBytes("duplicate-bytes");
        await fixture.SeedBookAsync(
            "Existing",
            "Author",
            "existing.pdf",
            duplicateBytes);
        var service = fixture.CreateService();
        var source = fixture.WriteBytesFile(@"incoming\Different Title - Someone Else.pdf", duplicateBytes);

        var result = await service.ImportAsync([source], default);

        result.Items.Should().ContainSingle().Which.Outcome.Should().Be(ImportOutcome.ExactDuplicate);
        result.Items.Single().BookId.Should().BeNull();
        Directory.Exists(Path.Combine(fixture.LibraryPath, "books")).Should().BeFalse();
        (await fixture.BookRepository.ListAsync(default)).Should().ContainSingle();
        await using var context = fixture.ContextFactory.Create(fixture.LibraryPath);
        (await context.BookFiles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Import_async_reports_possible_duplicates_without_copying_them()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        await fixture.SeedBookAsync(
            "The Hobbit",
            "J.R.R. Tolkien",
            "existing.pdf",
            Encoding.UTF8.GetBytes("existing-bytes"));
        var service = fixture.CreateService();
        var source = fixture.WriteBytesFile(
            @"incoming\the hobbit - j.r.r. tolkien.pdf",
            Encoding.UTF8.GetBytes("different-bytes"));

        var result = await service.ImportAsync([source], default);

        result.Items.Should().ContainSingle().Which.Outcome.Should().Be(ImportOutcome.PossibleDuplicate);
        Directory.Exists(Path.Combine(fixture.LibraryPath, "books")).Should().BeFalse();
        (await fixture.BookRepository.ListAsync(default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Import_async_continues_after_a_failure_and_cleans_up_the_copied_directory()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var failingRepository = new ThrowOnFirstAddBookRepository(fixture.BookRepository);
        var service = fixture.CreateService(failingRepository);
        var firstSource = fixture.WriteBytesFile(
            @"incoming\Broken - Author.pdf",
            Encoding.UTF8.GetBytes("broken-bytes"));
        var secondSource = fixture.WriteBytesFile(
            @"incoming\Working - Author.pdf",
            Encoding.UTF8.GetBytes("working-bytes"));

        var result = await service.ImportAsync([firstSource, secondSource], default);

        result.Items.Should().HaveCount(2);
        result.Items[0].Outcome.Should().Be(ImportOutcome.Failed);
        result.Items[1].Outcome.Should().Be(ImportOutcome.Added);
        result.Items[1].BookId.Should().NotBeNull();
        Directory.Exists(Path.Combine(fixture.LibraryPath, "books")).Should().BeTrue();
        Directory.EnumerateDirectories(Path.Combine(fixture.LibraryPath, "books"))
            .Select(Path.GetFileName)
            .Should()
            .Equal(result.Items[1].BookId!.Value.ToString("N"));

        var loaded = await fixture.LoadImportRunAsync(result.RunId);
        loaded.Should().NotBeNull();
        loaded!.Items.Should().HaveCount(2);
        loaded.Items.Select(item => item.SourcePath)
            .Should()
            .Equal("Broken - Author.pdf", "Working - Author.pdf");
    }

    [Fact]
    public async Task Import_async_persists_sanitized_source_display_names_for_restart_recovery()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var service = fixture.CreateService();
        var source = fixture.WriteBytesFile(
            @"incoming\Portable Library Source.pdf",
            Encoding.UTF8.GetBytes("portable-bytes"));

        var result = await service.ImportAsync([source], default);

        var loaded = await fixture.LoadImportRunAsync(result.RunId);
        loaded.Should().NotBeNull();
        loaded!.CompletedUtc.Should().NotBeNull();
        loaded.Items.Single().SourcePath.Should().Be("Portable Library Source.pdf");
        Path.IsPathRooted(loaded.Items.Single().SourcePath).Should().BeFalse();
    }

    [Fact]
    public async Task Import_async_treats_blank_and_root_only_paths_as_failed_items_and_continues_the_batch()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var service = fixture.CreateService();
        var validSource = fixture.WriteBytesFile(
            @"incoming\Valid - Author.pdf",
            Encoding.UTF8.GetBytes("valid-bytes"));

        var result = await service.ImportAsync(["", @"C:\", validSource], default);

        result.Items.Should().HaveCount(3);
        result.Items[0].Outcome.Should().Be(ImportOutcome.Failed);
        result.Items[1].Outcome.Should().Be(ImportOutcome.Failed);
        result.Items[2].Outcome.Should().Be(ImportOutcome.Added);

        var loaded = await fixture.LoadImportRunAsync(result.RunId);
        loaded.Should().NotBeNull();
        loaded!.Items.Should().HaveCount(3);
        loaded.Items[0].SourcePath.Should().Be("(invalid source)");
        loaded.Items[1].SourcePath.Should().Be("(invalid source)");
        loaded.Items[2].SourcePath.Should().Be("Valid - Author.pdf");
    }

    [Fact]
    public async Task Import_async_completes_the_run_when_cancellation_happens_after_first_success()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var repository = new CancelAfterFirstSuccessBookRepository(
            fixture.BookRepository,
            () => cancellation.Cancel());
        var importRepository = new DurableImportRepositoryDecorator(fixture.ImportRepository);
        var service = new ImportService(
            repository,
            importRepository,
            fixture.FileStore,
            fixture.FileHasher,
            fixture.MetadataAdapterResolver,
            fixture.ExceptionClassifier);

        var first = fixture.WriteBytesFile(@"incoming\One - Author.pdf", Encoding.UTF8.GetBytes("one"));
        var second = fixture.WriteBytesFile(@"incoming\Two - Author.pdf", Encoding.UTF8.GetBytes("two"));

        var act = () => service.ImportAsync([first, second], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var run = await fixture.LoadImportRunAsync(importRepository.LastRunId);
        run.Should().NotBeNull();
        run!.CompletedUtc.Should().NotBeNull();
        run.Items.Should().HaveCount(1);
        run.Items.Single().SourcePath.Should().Be("One - Author.pdf");
    }

    [Fact]
    public async Task Import_async_records_items_in_execution_order_even_when_display_names_match()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var service = new ImportService(
            fixture.BookRepository,
            fixture.ImportRepository,
            fixture.FileStore,
            fixture.FileHasher,
            new DirectoryTitleMetadataAdapterResolver(),
            fixture.ExceptionClassifier);

        var first = fixture.WriteBytesFile(@"incoming\b\Shared.pdf", Encoding.UTF8.GetBytes("first"));
        var second = fixture.WriteBytesFile(@"incoming\a\Shared.pdf", Encoding.UTF8.GetBytes("second"));

        var result = await service.ImportAsync([first, second], default);
        var loaded = await fixture.LoadImportRunAsync(result.RunId);

        result.Items.Select(item => item.BookId).Should().HaveCount(2);
        loaded.Should().NotBeNull();
        loaded!.Items.Select(item => item.BookId).Should().Equal(result.Items.Select(item => item.BookId));
        loaded.Items.Select(item => item.SourcePath).Should().Equal("Shared.pdf", "Shared.pdf");
    }

    [Fact]
    public async Task Import_async_does_not_persist_absolute_paths_from_downstream_failures()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var source = fixture.WriteBytesFile(@"incoming\Leaky - Author.pdf", Encoding.UTF8.GetBytes("leaky"));
        var service = new ImportService(
            fixture.BookRepository,
            fixture.ImportRepository,
            fixture.FileStore,
            fixture.FileHasher,
            new ThrowingMetadataResolver(message: source),
            fixture.ExceptionClassifier);

        var result = await service.ImportAsync([source], default);
        result.Items.Single().Message.Should().NotContain(source);

        var loaded = await fixture.LoadImportRunAsync(result.RunId);
        loaded.Should().NotBeNull();
        loaded!.Items.Single().Message.Should().NotContain(source);
    }

    [Fact]
    public async Task Import_async_reports_cleanup_failures_without_leaking_the_primary_exception_message()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var source = fixture.WriteBytesFile(@"incoming\Cleanup - Author.pdf", Encoding.UTF8.GetBytes("cleanup"));
        var cleanupStore = new TrackingCleanupStore(fixture.FileStore);
        var cleanupRepository = new TrackingCleanupBookRepository(fixture.BookRepository);
        var service = new ImportService(
            cleanupRepository,
            fixture.ImportRepository,
            cleanupStore,
            fixture.FileHasher,
            fixture.MetadataAdapterResolver,
            fixture.ExceptionClassifier);

        var result = await service.ImportAsync([source], default);

        var item = result.Items.Single();
        item.Outcome.Should().Be(ImportOutcome.Failed);
        item.Message.Should().Contain("cleanup incomplete");
        item.Message.Should().NotContain(source);
        item.Message.Should().NotContain("throw after copy");
        cleanupRepository.DeleteAsyncCalled.Should().BeTrue();
        cleanupStore.DeleteBookDirectoryCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Import_async_appends_cleanup_incomplete_to_possible_duplicate_race_outcomes_when_cleanup_fails()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var first = fixture.WriteBytesFile(@"incoming\shared\First.pdf", Encoding.UTF8.GetBytes("one"));
        var second = fixture.WriteBytesFile(@"incoming\shared\Second.pdf", Encoding.UTF8.GetBytes("two"));
        var racingRepository = new RacingDuplicateBookRepository(fixture.BookRepository);
        var trackingRepository = new TrackingDeleteBookRepository(racingRepository);
        var cleanupStore = new TrackingCleanupStore(fixture.FileStore);
        var service = new ImportService(
            trackingRepository,
            fixture.ImportRepository,
            cleanupStore,
            fixture.FileHasher,
            new DirectoryTitleMetadataAdapterResolver(),
            new DuplicateKeyRaceClassifier());

        var results = await Task.WhenAll(
            service.ImportAsync([first], default),
            service.ImportAsync([second], default));

        var possibleDuplicate = results.SelectMany(result => result.Items)
            .Single(item => item.Outcome == ImportOutcome.PossibleDuplicate);
        possibleDuplicate.Message.Should().Be("possible duplicate; cleanup incomplete");
        trackingRepository.DeleteAsyncCalled.Should().BeTrue();
        cleanupStore.DeleteBookDirectoryCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Import_async_compensates_when_recording_the_result_fails()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var source = fixture.WriteBytesFile(@"incoming\Persisted - Author.pdf", Encoding.UTF8.GetBytes("persisted"));
        var trackingRepository = new TrackingDeleteBookRepository(fixture.BookRepository);
        var recordFailureRepository = new ThrowingRecordItemImportRepository(fixture.ImportRepository);
        var service = new ImportService(
            trackingRepository,
            recordFailureRepository,
            fixture.FileStore,
            fixture.FileHasher,
            fixture.MetadataAdapterResolver,
            fixture.ExceptionClassifier);

        var act = () => service.ImportAsync([source], default);

        var exception = await act.Should().ThrowAsync<ImportPersistenceException>();
        exception.Which.Message.Should().Be("cannot persist result");
        exception.Which.Message.Should().NotContain(source);
        trackingRepository.DeleteAsyncCalled.Should().BeTrue();

        (await fixture.BookRepository.ListAsync(default)).Should().BeEmpty();
        await using var context = fixture.ContextFactory.Create(fixture.LibraryPath);
        (await context.BookFiles.AnyAsync()).Should().BeFalse();
        if (Directory.Exists(Path.Combine(fixture.LibraryPath, "books")))
        {
            Directory.EnumerateFileSystemEntries(Path.Combine(fixture.LibraryPath, "books")).Should().BeEmpty();
        }

        var run = await fixture.LoadImportRunAsync(recordFailureRepository.LastRunId);
        run.Should().NotBeNull();
        run!.CompletedUtc.Should().NotBeNull();
        run.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_async_reports_cleanup_incomplete_when_recording_the_result_fails_and_compensation_is_partial()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var source = fixture.WriteBytesFile(@"incoming\Partial - Author.pdf", Encoding.UTF8.GetBytes("partial"));
        var trackingRepository = new TrackingDeleteBookRepository(fixture.BookRepository);
        var cleanupStore = new TrackingCleanupStore(fixture.FileStore);
        var recordFailureRepository = new ThrowingRecordItemImportRepository(fixture.ImportRepository);
        var service = new ImportService(
            trackingRepository,
            recordFailureRepository,
            cleanupStore,
            fixture.FileHasher,
            fixture.MetadataAdapterResolver,
            fixture.ExceptionClassifier);

        var act = () => service.ImportAsync([source], default);

        var exception = await act.Should().ThrowAsync<ImportPersistenceException>();
        exception.Which.Message.Should().Be("cannot persist result; cleanup incomplete");
        exception.Which.Message.Should().NotContain(source);
        trackingRepository.DeleteAsyncCalled.Should().BeTrue();
        cleanupStore.DeleteBookDirectoryCalled.Should().BeTrue();

        var run = await fixture.LoadImportRunAsync(recordFailureRepository.LastRunId);
        run.Should().NotBeNull();
        run!.CompletedUtc.Should().NotBeNull();
        run.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_async_races_can_fall_back_to_possible_duplicate_after_unique_key_violation()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var first = fixture.WriteBytesFile(@"incoming\shared\First.pdf", Encoding.UTF8.GetBytes("one"));
        var second = fixture.WriteBytesFile(@"incoming\shared\Second.pdf", Encoding.UTF8.GetBytes("two"));
        var duplicateBookRepository = new RacingDuplicateBookRepository(fixture.BookRepository);
        var duplicateClassifier = new DuplicateKeyRaceClassifier();
        var service = new ImportService(
            duplicateBookRepository,
            fixture.ImportRepository,
            fixture.FileStore,
            fixture.FileHasher,
            new DirectoryTitleMetadataAdapterResolver(),
            duplicateClassifier);

        var act1 = Task.Run(async () =>
        {
            return await service.ImportAsync([first], default);
        });
        var act2 = Task.Run(async () =>
        {
            return await service.ImportAsync([second], default);
        });

        var results = await Task.WhenAll(act1, act2);
        results.SelectMany(result => result.Items).Should().Contain(item => item.Outcome == ImportOutcome.Added);
        results.SelectMany(result => result.Items).Should().Contain(item => item.Outcome == ImportOutcome.PossibleDuplicate);
    }

    private sealed class CancelAfterFirstSuccessBookRepository(
        IBookRepository inner,
        Action? onSuccess) : IBookRepository
    {
        private bool canceled;

        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) =>
            inner.ListAsync(cancellationToken);

        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            inner.GetAsync(id, cancellationToken);

        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) =>
            inner.HasHashAsync(sha256, cancellationToken);

        public Task<bool> HasNormalizedTitleAndAuthorAsync(
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken) =>
            inner.HasNormalizedTitleAndAuthorAsync(title, authors, cancellationToken);

        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken)
        {
            var add = inner.AddAsync(book, file, cancellationToken);
            if (!canceled)
            {
                canceled = true;
                onSuccess?.Invoke();
            }

            return add;
        }

        public Task UpdateAsync(Book book, CancellationToken cancellationToken) =>
            inner.UpdateAsync(book, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) =>
            inner.ListFilesAsync(bookId, cancellationToken);

        public Task UpdateFileWriteBackAsync(
            Guid fileId,
            MetadataWriteResult result,
            CancellationToken cancellationToken) =>
            inner.UpdateFileWriteBackAsync(fileId, result, cancellationToken);
    }

    private sealed class ThrowOnFirstAddBookRepository(IBookRepository inner) : IBookRepository
    {
        private bool thrown;

        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);

        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => inner.HasHashAsync(sha256, cancellationToken);

        public Task<bool> HasNormalizedTitleAndAuthorAsync(
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken) =>
            inner.HasNormalizedTitleAndAuthorAsync(title, authors, cancellationToken);

        public async Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken)
        {
            if (!thrown)
            {
                thrown = true;
                throw new InvalidOperationException("boom");
            }

            await inner.AddAsync(book, file, cancellationToken);
        }

        public Task UpdateAsync(Book book, CancellationToken cancellationToken) => inner.UpdateAsync(book, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);

        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) =>
            inner.ListFilesAsync(bookId, cancellationToken);

        public Task UpdateFileWriteBackAsync(
            Guid fileId,
            MetadataWriteResult result,
            CancellationToken cancellationToken) =>
            inner.UpdateFileWriteBackAsync(fileId, result, cancellationToken);
    }

    private sealed class DurableImportRepositoryDecorator(IImportRepository inner) : IImportRepository
    {
        public Guid LastRunId { get; private set; }

        public Task<Guid> StartRunAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken)
        {
            return inner.StartRunAsync(startedUtc, cancellationToken);
        }

        public async Task RecordItemAsync(
            Guid runId,
            int sequence,
            string sourceDisplayName,
            ImportOutcome outcome,
            string message,
            Guid? bookId,
            CancellationToken cancellationToken)
        {
            LastRunId = runId;
            cancellationToken.CanBeCanceled.Should().BeFalse();
            await inner.RecordItemAsync(runId, sequence, sourceDisplayName, outcome, message, bookId, cancellationToken);
        }

        public async Task CompleteRunAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken)
        {
            LastRunId = runId;
            cancellationToken.CanBeCanceled.Should().BeFalse();
            await inner.CompleteRunAsync(runId, completedUtc, cancellationToken);
        }

        public Task<ImportRunResult?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
            inner.GetAsync(runId, cancellationToken);
    }

    private sealed class DirectoryTitleMetadataAdapterResolver : IMetadataAdapterResolver
    {
        private readonly IMetadataAdapter adapter = new DirectoryTitleMetadataAdapter();

        public IMetadataAdapter Resolve(EbookFormat format) => adapter;
    }

    private sealed class DirectoryTitleMetadataAdapter : IMetadataAdapter
    {
        public bool CanHandle(EbookFormat format) => true;

        public Task<MetadataReadResult> ReadAsync(string path, EbookFormat format, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetFileName(Path.GetDirectoryName(path)) ?? "Unknown";
            return Task.FromResult(new MetadataReadResult(new BookMetadata(directory, ["Author"])));
        }

        public Task<MetadataWriteResult> WriteAsync(string path, EbookFormat format, BookMetadata metadata, CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataWriteResult(MetadataWriteBackStatus.Unsupported));
    }

    private sealed class ThrowingMetadataResolver(string message) : IMetadataAdapterResolver
    {
        public IMetadataAdapter Resolve(EbookFormat format) => new ThrowingMetadataAdapter(message);
    }

    private sealed class ThrowingMetadataAdapter(string message) : IMetadataAdapter
    {
        public bool CanHandle(EbookFormat format) => true;

        public Task<MetadataReadResult> ReadAsync(string path, EbookFormat format, CancellationToken cancellationToken) =>
            throw new IOException($"downstream failed on {message}");

        public Task<MetadataWriteResult> WriteAsync(string path, EbookFormat format, BookMetadata metadata, CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataWriteResult(MetadataWriteBackStatus.Unsupported));
    }

    private sealed class TrackingCleanupBookRepository(IBookRepository inner) : IBookRepository
    {
        public bool DeleteAsyncCalled { get; private set; }

        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);

        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => inner.HasHashAsync(sha256, cancellationToken);

        public Task<bool> HasNormalizedTitleAndAuthorAsync(string title, IReadOnlyList<string> authors, CancellationToken cancellationToken) =>
            inner.HasNormalizedTitleAndAuthorAsync(title, authors, cancellationToken);

        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("throw after copy");

        public Task UpdateAsync(Book book, CancellationToken cancellationToken) => inner.UpdateAsync(book, cancellationToken);

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            DeleteAsyncCalled = true;
            await inner.DeleteAsync(id, cancellationToken);
        }

        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) =>
            inner.ListFilesAsync(bookId, cancellationToken);

        public Task UpdateFileWriteBackAsync(
            Guid fileId,
            MetadataWriteResult result,
            CancellationToken cancellationToken) =>
            inner.UpdateFileWriteBackAsync(fileId, result, cancellationToken);
    }

    private sealed class TrackingCleanupStore(ILibraryFileStore inner) : ILibraryFileStore
    {
        public bool DeleteBookDirectoryCalled { get; private set; }

        public string GetAbsolutePath(string relativePath) => inner.GetAbsolutePath(relativePath);

        public async Task<(string RelativeBookPath, string? RelativeCoverPath)> CopyIntoLibraryAsync(
            Guid bookId,
            string sourcePath,
            byte[]? coverBytes,
            CancellationToken cancellationToken)
        {
            return await inner.CopyIntoLibraryAsync(bookId, sourcePath, coverBytes, cancellationToken);
        }

        public async Task DeleteBookDirectoryAsync(Guid bookId, CancellationToken cancellationToken)
        {
            DeleteBookDirectoryCalled = true;
            throw new IOException("cleanup store failure");
        }
    }

    private sealed class TrackingDeleteBookRepository(IBookRepository inner) : IBookRepository
    {
        public bool DeleteAsyncCalled { get; private set; }

        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);

        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => inner.HasHashAsync(sha256, cancellationToken);

        public Task<bool> HasNormalizedTitleAndAuthorAsync(
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken) =>
            inner.HasNormalizedTitleAndAuthorAsync(title, authors, cancellationToken);

        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken) =>
            inner.AddAsync(book, file, cancellationToken);

        public Task UpdateAsync(Book book, CancellationToken cancellationToken) => inner.UpdateAsync(book, cancellationToken);

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            DeleteAsyncCalled = true;
            await inner.DeleteAsync(id, cancellationToken);
        }

        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) =>
            inner.ListFilesAsync(bookId, cancellationToken);

        public Task UpdateFileWriteBackAsync(
            Guid fileId,
            MetadataWriteResult result,
            CancellationToken cancellationToken) =>
            inner.UpdateFileWriteBackAsync(fileId, result, cancellationToken);
    }

    private sealed class RacingDuplicateBookRepository(IBookRepository inner) : IBookRepository
    {
        private readonly Barrier checkBarrier = new(2);
        private int addCount;

        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);

        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => inner.HasHashAsync(sha256, cancellationToken);

        public Task<bool> HasNormalizedTitleAndAuthorAsync(
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken) =>
            HasNormalizedTitleAndAuthorAsyncCore(title, authors, cancellationToken);

        private async Task<bool> HasNormalizedTitleAndAuthorAsyncCore(
            string title,
            IReadOnlyList<string> authors,
            CancellationToken cancellationToken)
        {
            checkBarrier.SignalAndWait(cancellationToken);
            var exists = await inner.HasNormalizedTitleAndAuthorAsync(title, authors, cancellationToken);
            checkBarrier.SignalAndWait(cancellationToken);
            return exists;
        }

        public async Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref addCount) == 2)
            {
                throw new DuplicateKeyRaceException();
            }

            await inner.AddAsync(book, file, cancellationToken);
        }

        public Task UpdateAsync(Book book, CancellationToken cancellationToken) => inner.UpdateAsync(book, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);

        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) =>
            inner.ListFilesAsync(bookId, cancellationToken);

        public Task UpdateFileWriteBackAsync(
            Guid fileId,
            MetadataWriteResult result,
            CancellationToken cancellationToken) =>
            inner.UpdateFileWriteBackAsync(fileId, result, cancellationToken);
    }

    private sealed class ThrowingRecordItemImportRepository(IImportRepository inner) : IImportRepository
    {
        public Guid LastRunId { get; private set; }

        public async Task<Guid> StartRunAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken)
        {
            var runId = await inner.StartRunAsync(startedUtc, cancellationToken);
            LastRunId = runId;
            return runId;
        }

        public Task RecordItemAsync(
            Guid runId,
            int sequence,
            string sourceDisplayName,
            ImportOutcome outcome,
            string message,
            Guid? bookId,
            CancellationToken cancellationToken)
        {
            LastRunId = runId;
            throw new InvalidOperationException("record item failed");
        }

        public Task CompleteRunAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken) =>
            inner.CompleteRunAsync(runId, completedUtc, cancellationToken);

        public Task<ImportRunResult?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
            inner.GetAsync(runId, cancellationToken);
    }

    private sealed class DuplicateKeyRaceException : Exception;

    private sealed class DuplicateKeyRaceClassifier : IImportExceptionClassifier
    {
        public bool IsDuplicateKeyViolation(Exception exception) => exception is DuplicateKeyRaceException;
    }

    private sealed class ReturningMetadataSidecarStore(BookMetadata metadata) : IMetadataSidecarStore
    {
        public List<string> ReadPaths { get; } = [];

        public Task<BookMetadata?> TryReadAsync(string bookFilePath, CancellationToken cancellationToken)
        {
            ReadPaths.Add(bookFilePath);
            return Task.FromResult<BookMetadata?>(metadata);
        }

        public Task WriteAsync(string bookFilePath, BookMetadata metadata, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
