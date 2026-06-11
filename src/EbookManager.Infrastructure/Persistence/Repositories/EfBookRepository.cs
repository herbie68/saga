using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;
using EbookManager.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace EbookManager.Infrastructure.Persistence.Repositories;

public sealed class EfBookRepository(
    LibraryDbContextFactory contextFactory,
    string libraryPath) : IBookRepository
{
    public async Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        var books = await context.Books
            .AsNoTracking()
            .OrderBy(x => x.Title)
            .ThenBy(x => x.Id)
            .Select(x => new BookListProjection(
                x.Id,
                x.Title,
                x.Description,
                x.Language,
                x.Publisher,
                x.PublicationDate,
                x.Series,
                x.SeriesNumber,
                x.Isbn,
                x.ReadingStatus,
                x.CoverRelativePath,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.BookAuthors
                    .OrderBy(bookAuthor => bookAuthor.Order)
                    .Select(bookAuthor => bookAuthor.Author.Name)
                    .ToList(),
                x.BookTags
                    .OrderBy(bookTag => bookTag.Order)
                    .Select(bookTag => bookTag.Tag.Name)
                    .ToList()))
            .ToListAsync(cancellationToken);
        return books.Select(ToDomain).ToList().AsReadOnly();
    }

    public async Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        var book = await BooksWithMetadata(context)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return book is null ? null : ToDomain(book, includeCoverBytes: true);
    }

    public async Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken)
    {
        var canonicalSha256 = CanonicalizeSha256(sha256);
        await using var context = contextFactory.Create(libraryPath);
        return await context.BookFiles.AnyAsync(x => x.Sha256 == canonicalSha256, cancellationToken);
    }

    public async Task<bool> HasNormalizedTitleAndAuthorAsync(
        string title,
        IReadOnlyList<string> authors,
        CancellationToken cancellationToken)
    {
        var duplicateKey = DuplicateKeyNormalizer.BuildDuplicateKey(title, authors);
        await using var context = contextFactory.Create(libraryPath);
        return await context.Books
            .AsNoTracking()
            .AnyAsync(x => x.DuplicateKey == duplicateKey, cancellationToken);
    }

    public async Task AddAsync(
        Book book,
        BookFile file,
        CancellationToken cancellationToken)
    {
        var fileEntity = ToEntity(file);
        await using var context = contextFactory.Create(libraryPath);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var entity = ToEntity(book);
        context.Books.Add(entity);
        await AddAuthorsAsync(context, entity, book.Metadata.Authors, cancellationToken);
        await AddTagsAsync(context, entity, book.Metadata.Tags, cancellationToken);
        entity.Files.Add(fileEntity);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(Book book, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = contextFactory.Create(libraryPath);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var entity = await context.Books
                .SingleOrDefaultAsync(x => x.Id == book.Id, cancellationToken)
                ?? throw new KeyNotFoundException($"Book '{book.Id}' does not exist.");

            Apply(book, entity);
            await context.SaveChangesAsync(cancellationToken);
            await context.BookAuthors
                .Where(x => x.BookId == book.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await context.BookTags
                .Where(x => x.BookId == book.Id)
                .ExecuteDeleteAsync(cancellationToken);
            context.ChangeTracker.Clear();
            entity = await context.Books.SingleAsync(x => x.Id == book.Id, cancellationToken);
            await AddAuthorsAsync(context, entity, book.Metadata.Authors, cancellationToken);
            await AddTagsAsync(context, entity, book.Metadata.Tags, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await RemoveOrphanedMetadataAsync(context, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsDuplicateKeyViolation(exception))
        {
            throw new BookConflictException();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var entity = await context.Books.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        context.Books.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        await RemoveOrphanedMetadataAsync(context, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BookFile>> ListFilesAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        var files = await context.BookFiles
            .AsNoTracking()
            .Where(x => x.BookId == bookId)
            .OrderBy(x => x.RelativePath)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return files.Select(ToDomain).ToList().AsReadOnly();
    }

    public async Task UpdateFileWriteBackAsync(
        Guid fileId,
        MetadataWriteResult result,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        var entity = await context.BookFiles
            .SingleOrDefaultAsync(x => x.Id == fileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Book file '{fileId}' does not exist.");

        entity.WriteBackStatus = result.Status;
        entity.WriteBackMessage = result.Message;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<BookEntity> BooksWithMetadata(LibraryDbContext context) =>
        context.Books
            .Include(x => x.BookAuthors)
                .ThenInclude(x => x.Author)
            .Include(x => x.BookTags)
                .ThenInclude(x => x.Tag);

    private static async Task AddAuthorsAsync(
        LibraryDbContext context,
        BookEntity book,
        IReadOnlyList<string> authors,
        CancellationToken cancellationToken)
    {
        var normalizedAuthors = NormalizeMetadataNames(authors);
        for (var order = 0; order < normalizedAuthors.Count; order++)
        {
            var (name, normalizedName) = normalizedAuthors[order];
            var author = context.Authors.Local
                .SingleOrDefault(x => x.NormalizedName == normalizedName)
                ?? await context.Authors
                    .SingleOrDefaultAsync(x => x.NormalizedName == normalizedName, cancellationToken);
            if (author is null)
            {
                author = new AuthorEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    NormalizedName = normalizedName
                };
                context.Authors.Add(author);
            }
            else
            {
                author.Name = name;
            }

            context.BookAuthors.Add(new BookAuthorEntity
            {
                BookId = book.Id,
                AuthorId = author.Id,
                Order = order
            });
        }
    }

    private static async Task AddTagsAsync(
        LibraryDbContext context,
        BookEntity book,
        IReadOnlyList<string>? tags,
        CancellationToken cancellationToken)
    {
        if (tags is null)
        {
            return;
        }

        var normalizedTags = NormalizeMetadataNames(tags);
        for (var order = 0; order < normalizedTags.Count; order++)
        {
            var (name, normalizedName) = normalizedTags[order];
            var tag = context.Tags.Local
                .SingleOrDefault(x => x.NormalizedName == normalizedName)
                ?? await context.Tags
                    .SingleOrDefaultAsync(x => x.NormalizedName == normalizedName, cancellationToken);
            if (tag is null)
            {
                tag = new TagEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    NormalizedName = normalizedName
                };
                context.Tags.Add(tag);
            }
            else
            {
                tag.Name = name;
            }

            context.BookTags.Add(new BookTagEntity
            {
                BookId = book.Id,
                TagId = tag.Id,
                Order = order
            });
        }
    }

    private static async Task RemoveOrphanedMetadataAsync(
        LibraryDbContext context,
        CancellationToken cancellationToken)
    {
        var authors = await context.Authors
            .Where(x => !x.BookAuthors.Any())
            .ToListAsync(cancellationToken);
        var tags = await context.Tags
            .Where(x => !x.BookTags.Any())
            .ToListAsync(cancellationToken);
        context.Authors.RemoveRange(authors);
        context.Tags.RemoveRange(tags);
    }

    private static BookEntity ToEntity(Book book)
    {
        var entity = new BookEntity();
        Apply(book, entity);
        return entity;
    }

    private static void Apply(Book book, BookEntity entity)
    {
        entity.Id = book.Id;
        entity.Title = book.Metadata.Title;
        entity.NormalizedTitle = Normalize(book.Metadata.Title);
        entity.DuplicateKey = DuplicateKeyNormalizer.BuildDuplicateKey(book.Metadata.Title, book.Metadata.Authors);
        entity.Description = book.Metadata.Description;
        entity.Language = book.Metadata.Language;
        entity.Publisher = book.Metadata.Publisher;
        entity.PublicationDate = book.Metadata.PublicationDate;
        entity.Series = book.Metadata.Series;
        entity.SeriesNumber = book.Metadata.SeriesNumber;
        entity.Isbn = book.Metadata.Isbn;
        entity.CoverBytes = book.Metadata.CoverBytes;
        entity.ReadingStatus = book.ReadingStatus;
        entity.CoverRelativePath = book.CoverRelativePath;
        entity.CreatedUtc = book.CreatedUtc;
        entity.UpdatedUtc = book.UpdatedUtc;
    }

    private static BookFileEntity ToEntity(BookFile file) =>
        new()
        {
            Id = file.Id,
            BookId = file.BookId,
            Format = file.Format,
            RelativePath = file.RelativePath,
            Sha256 = CanonicalizeSha256(file.Sha256),
            SizeBytes = file.SizeBytes,
            WriteBackStatus = file.WriteBackStatus,
            WriteBackMessage = file.WriteBackMessage
        };

    private static Book ToDomain(BookEntity entity, bool includeCoverBytes) =>
        new(
            entity.Id,
            new BookMetadata(
                entity.Title,
                entity.BookAuthors
                    .OrderBy(x => x.Order)
                    .Select(x => x.Author.Name)
                    .ToList(),
                entity.Description,
                entity.Language,
                entity.Publisher,
                entity.PublicationDate,
                entity.BookTags
                    .OrderBy(x => x.Order)
                    .Select(x => x.Tag.Name)
                    .ToList(),
                entity.Series,
                entity.SeriesNumber,
                entity.Isbn,
                includeCoverBytes ? entity.CoverBytes : null),
            entity.ReadingStatus,
            entity.CoverRelativePath,
            entity.CreatedUtc,
            entity.UpdatedUtc);

    private static Book ToDomain(BookListProjection projection) =>
        new(
            projection.Id,
            new BookMetadata(
                projection.Title,
                projection.Authors,
                projection.Description,
                projection.Language,
                projection.Publisher,
                projection.PublicationDate,
                projection.Tags,
                projection.Series,
                projection.SeriesNumber,
                projection.Isbn),
            projection.ReadingStatus,
            projection.CoverRelativePath,
            projection.CreatedUtc,
            projection.UpdatedUtc);

    private static BookFile ToDomain(BookFileEntity entity) =>
        new(
            entity.Id,
            entity.BookId,
            entity.Format,
            entity.RelativePath,
            entity.Sha256,
            entity.SizeBytes,
            entity.WriteBackStatus,
            entity.WriteBackMessage);

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static IReadOnlyList<NormalizedMetadataName> NormalizeMetadataNames(
        IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        var normalizedNames = new List<NormalizedMetadataName>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var name = value.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var normalizedName = Normalize(name);
            if (seen.Add(normalizedName))
            {
                normalizedNames.Add(new NormalizedMetadataName(name, normalizedName));
            }
        }

        return normalizedNames;
    }

    private static string CanonicalizeSha256(string sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 hashes must contain exactly 64 hexadecimal characters.", nameof(sha256));
        }

        return sha256.ToUpperInvariant();
    }

    private static bool IsDuplicateKeyViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqliteException &&
                sqliteException.SqliteErrorCode == 19 &&
                sqliteException.SqliteExtendedErrorCode == 2067)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record NormalizedMetadataName(string Name, string NormalizedName);

    private sealed record BookListProjection(
        Guid Id,
        string Title,
        string? Description,
        string? Language,
        string? Publisher,
        DateOnly? PublicationDate,
        string? Series,
        decimal? SeriesNumber,
        string? Isbn,
        ReadingStatus ReadingStatus,
        string? CoverRelativePath,
        DateTimeOffset CreatedUtc,
        DateTimeOffset UpdatedUtc,
        IReadOnlyList<string> Authors,
        IReadOnlyList<string> Tags);
}
