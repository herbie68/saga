using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;

namespace EbookManager.Application.Books;

public sealed class BookService(
    IBookRepository bookRepository,
    ILibraryFileStore fileStore,
    IMetadataAdapterResolver metadataAdapterResolver,
    IMetadataSidecarStore? metadataSidecarStore = null)
{
    private readonly IBookRepository bookRepository = bookRepository;
    private readonly ILibraryFileStore fileStore = fileStore;
    private readonly IMetadataAdapterResolver metadataAdapterResolver = metadataAdapterResolver;
    private readonly IMetadataSidecarStore? metadataSidecarStore = metadataSidecarStore;

    public async Task<BookSaveResult> SaveAsync(Book book, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await bookRepository.UpdateAsync(book, cancellationToken);
        }
        catch (BookConflictException exception)
        {
            return new BookSaveResult(BookSaveStatus.Conflict, [], exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new BookSaveResult(BookSaveStatus.Failed, [], exception.Message);
        }

        var fileResults = new List<BookFileWriteBackResult>();

        try
        {
            var files = await bookRepository.ListFilesAsync(book.Id, cancellationToken);
            await WriteSidecarMetadataAsync(book, files, cancellationToken);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var adapter = metadataAdapterResolver.Resolve(file.Format);
                MetadataWriteResult writeResult;
                try
                {
                    writeResult = await adapter.WriteAsync(
                        fileStore.GetAbsolutePath(file.RelativePath),
                        file.Format,
                        book.Metadata,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    writeResult = new MetadataWriteResult(MetadataWriteBackStatus.Failed, exception.Message);
                }

                await bookRepository.UpdateFileWriteBackAsync(file.Id, writeResult, cancellationToken);
                fileResults.Add(new BookFileWriteBackResult(file.Id, writeResult));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new BookSaveResult(BookSaveStatus.Failed, fileResults, exception.Message);
        }

        return new BookSaveResult(BookSaveStatus.Succeeded, fileResults);
    }

    private async Task WriteSidecarMetadataAsync(
        Book book,
        IReadOnlyList<BookFile> files,
        CancellationToken cancellationToken)
    {
        if (metadataSidecarStore is null)
        {
            return;
        }

        var writtenDirectories = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        foreach (var file in files)
        {
            var absolutePath = fileStore.GetAbsolutePath(file.RelativePath);
            var directory = Path.GetDirectoryName(absolutePath);
            if (directory is null || !writtenDirectories.Add(directory))
            {
                continue;
            }

            await metadataSidecarStore.WriteAsync(
                absolutePath,
                book.Metadata,
                cancellationToken);
        }
    }

    public async Task<BookDeleteResult> DeleteAsync(
        Guid bookId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await fileStore.DeleteBookDirectoryAsync(bookId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new BookDeleteResult(BookDeleteStatus.CleanupWarning, exception.Message);
        }

        try
        {
            await bookRepository.DeleteAsync(bookId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new BookDeleteResult(BookDeleteStatus.CleanupWarning, exception.Message);
        }

        return new BookDeleteResult(BookDeleteStatus.Deleted);
    }
}

public enum BookSaveStatus
{
    Succeeded,
    Conflict,
    Failed
}

public sealed record BookFileWriteBackResult(Guid FileId, MetadataWriteResult Result);

public sealed record BookSaveResult(
    BookSaveStatus Status,
    IReadOnlyList<BookFileWriteBackResult> FileResults,
    string? Message = null);

public enum BookDeleteStatus
{
    Deleted,
    CleanupWarning
}

public sealed record BookDeleteResult(BookDeleteStatus Status, string? Message = null);
