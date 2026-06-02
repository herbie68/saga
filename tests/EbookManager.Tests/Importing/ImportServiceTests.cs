using System.Security.Cryptography;
using System.Text;
using EbookManager.Application.Importing;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
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
        var failingRepository = new ThrowingBookRepository(
            fixture.BookRepository,
            new InvalidOperationException("boom"));
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
    public async Task Import_async_propagates_cancellation_and_removes_partially_copied_files()
    {
        await using var fixture = await ImportServiceFixture.CreateAsync();
        var failingRepository = new ThrowingBookRepository(
            fixture.BookRepository,
            new OperationCanceledException("cancelled"));
        var service = fixture.CreateService(failingRepository);
        var source = fixture.WriteBytesFile(
            @"incoming\Cancelable - Author.pdf",
            Encoding.UTF8.GetBytes("cancelable-bytes"));

        var act = () => service.ImportAsync([source], default);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateDirectories(Path.Combine(fixture.LibraryPath, "books"))
            .Should()
            .BeEmpty();
        await using var context = fixture.ContextFactory.Create(fixture.LibraryPath);
        (await context.BookFiles.AnyAsync()).Should().BeFalse();
        (await context.ImportItems.AnyAsync()).Should().BeFalse();
    }

    private sealed class ThrowingBookRepository(
        IBookRepository inner,
        Exception exceptionToThrow) : IBookRepository
    {
        private bool thrown;

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
            if (!thrown)
            {
                thrown = true;
                throw exceptionToThrow;
            }

            return inner.AddAsync(book, file, cancellationToken);
        }

        public Task UpdateAsync(Book book, CancellationToken cancellationToken) =>
            inner.UpdateAsync(book, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            inner.DeleteAsync(id, cancellationToken);
    }
}
