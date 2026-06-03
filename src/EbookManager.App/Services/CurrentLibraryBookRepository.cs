using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;
using EbookManager.Infrastructure.Persistence;
using EbookManager.Infrastructure.Persistence.Repositories;
using EbookManager.Libraries;

namespace EbookManager.App.Services;

public sealed class CurrentLibraryBookRepository(
    CurrentLibrary currentLibrary,
    LibraryDbContextFactory contextFactory)
    : IBookRepository
{
    public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken)
    {
        var repository = TryCreateRepository();
        return repository is null
            ? Task.FromResult<IReadOnlyList<Book>>([])
            : repository.ListAsync(cancellationToken);
    }

    public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var repository = TryCreateRepository();
        return repository is null
            ? Task.FromResult<Book?>(null)
            : repository.GetAsync(id, cancellationToken);
    }

    public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) =>
        CreateRepository().HasHashAsync(sha256, cancellationToken);

    public Task<bool> HasNormalizedTitleAndAuthorAsync(
        string title,
        IReadOnlyList<string> authors,
        CancellationToken cancellationToken) =>
        CreateRepository().HasNormalizedTitleAndAuthorAsync(title, authors, cancellationToken);

    public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken) =>
        CreateRepository().AddAsync(book, file, cancellationToken);

    public Task UpdateAsync(Book book, CancellationToken cancellationToken) =>
        CreateRepository().UpdateAsync(book, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        CreateRepository().DeleteAsync(id, cancellationToken);

    public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var repository = TryCreateRepository();
        return repository is null
            ? Task.FromResult<IReadOnlyList<BookFile>>([])
            : repository.ListFilesAsync(bookId, cancellationToken);
    }

    public Task UpdateFileWriteBackAsync(
        Guid fileId,
        MetadataWriteResult result,
        CancellationToken cancellationToken) =>
        CreateRepository().UpdateFileWriteBackAsync(fileId, result, cancellationToken);

    private EfBookRepository CreateRepository() =>
        TryCreateRepository() ?? throw new InvalidOperationException("No active library is loaded.");

    private EfBookRepository? TryCreateRepository()
    {
        var library = currentLibrary.Current;
        return library is null ? null : new EfBookRepository(contextFactory, library.DirectoryPath);
    }
}
