using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Domain.Metadata;

namespace EbookManager.Application.Importing;

public sealed class ImportService(
    IBookRepository bookRepository,
    IImportRepository importRepository,
    ILibraryFileStore fileStore,
    IFileHasher hasher,
    IMetadataAdapterResolver metadataAdapterResolver)
{
    private readonly IBookRepository bookRepository = bookRepository;
    private readonly IImportRepository importRepository = importRepository;
    private readonly ILibraryFileStore fileStore = fileStore;
    private readonly IFileHasher hasher = hasher;
    private readonly IMetadataAdapterResolver metadataAdapterResolver = metadataAdapterResolver;

    public async Task<ImportBatchResult> ImportAsync(
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var startedUtc = DateTimeOffset.UtcNow;
        var runId = await importRepository.StartRunAsync(startedUtc, cancellationToken);
        var results = new List<ImportItemResult>(sourcePaths.Count);

        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await ImportSingleAsync(runId, sourcePath, cancellationToken);
            results.Add(item);
        }

        try
        {
            await importRepository.CompleteRunAsync(runId, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }

        return new ImportBatchResult(runId, results);
    }

    private async Task<ImportItemResult> ImportSingleAsync(
        Guid runId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        var sourceDisplayName = GetSourceDisplayName(sourcePath);
        if (!TryResolveFormat(sourceDisplayName, out var format))
        {
            var unsupported = new ImportItemResult(sourcePath, ImportOutcome.Failed, "Unsupported ebook format.");
            await TryRecordItemAsync(runId, sourceDisplayName, unsupported, cancellationToken);
            return unsupported;
        }

        string sha256;
        try
        {
            sha256 = await hasher.ComputeSha256Async(sourcePath, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var unreadable = new ImportItemResult(sourcePath, ImportOutcome.Failed, "Source file is not readable.");
            await TryRecordItemAsync(runId, sourceDisplayName, unreadable, cancellationToken);
            return unreadable;
        }

        if (await bookRepository.HasHashAsync(sha256, cancellationToken))
        {
            var duplicate = new ImportItemResult(
                sourcePath,
                ImportOutcome.ExactDuplicate,
                "Skipped exact duplicate file.");
            await TryRecordItemAsync(runId, sourceDisplayName, duplicate, cancellationToken);
            return duplicate;
        }

        MetadataReadResult metadata;
        try
        {
            metadata = await metadataAdapterResolver.Resolve(format).ReadAsync(sourcePath, format, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failed = new ImportItemResult(
                sourcePath,
                ImportOutcome.Failed,
                $"Failed to read metadata: {exception.Message}");
            await TryRecordItemAsync(runId, sourceDisplayName, failed, cancellationToken);
            return failed;
        }

        if (await bookRepository.HasNormalizedTitleAndAuthorAsync(
                metadata.Metadata.Title,
                metadata.Metadata.Authors,
                cancellationToken))
        {
            var duplicate = new ImportItemResult(
                sourcePath,
                ImportOutcome.PossibleDuplicate,
                "Skipped possible duplicate by normalized title and author.");
            await TryRecordItemAsync(runId, sourceDisplayName, duplicate, cancellationToken);
            return duplicate;
        }

        var bookId = Guid.NewGuid();
        var bookPersisted = false;
        var copied = false;
        var completedSuccessfully = false;
        try
        {
            var copy = await fileStore.CopyIntoLibraryAsync(bookId, sourcePath, metadata.Metadata.CoverBytes, cancellationToken);
            copied = true;

            var now = DateTimeOffset.UtcNow;
            var book = new Book(
                bookId,
                metadata.Metadata,
                ReadingStatus.Unread,
                copy.RelativeCoverPath,
                now,
                now);
            var file = new BookFile(
                Guid.NewGuid(),
                bookId,
                format,
                copy.RelativeBookPath,
                sha256,
                new FileInfo(sourcePath).Length,
                MetadataWriteBackStatus.NotAttempted,
                null);

            await bookRepository.AddAsync(book, file, cancellationToken);
            bookPersisted = true;

            var message = metadata.Warning is null
                ? "Imported."
                : $"Imported with warning: {metadata.Warning}";
            var added = new ImportItemResult(sourcePath, ImportOutcome.Added, message, bookId);
            await TryRecordItemAsync(runId, sourceDisplayName, added, cancellationToken);
            completedSuccessfully = true;
            return added;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failed = new ImportItemResult(
                sourcePath,
                ImportOutcome.Failed,
                $"Failed to import '{sourceDisplayName}': {exception.Message}");
            await TryRecordItemAsync(runId, sourceDisplayName, failed, cancellationToken);
            return failed;
        }
        finally
        {
            if (!completedSuccessfully && (copied || bookPersisted))
            {
                await CleanupImportedBookAsync(bookId, bookPersisted);
            }
        }
    }

    private async Task TryRecordItemAsync(
        Guid runId,
        string sourceDisplayName,
        ImportItemResult item,
        CancellationToken cancellationToken)
    {
        try
        {
            await importRepository.RecordItemAsync(
                runId,
                sourceDisplayName,
                item.Outcome,
                item.Message,
                item.BookId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private async Task CleanupImportedBookAsync(Guid bookId, bool bookPersisted)
    {
        try
        {
            if (bookPersisted)
            {
                await bookRepository.DeleteAsync(bookId, CancellationToken.None);
            }
        }
        catch
        {
        }

        try
        {
            await fileStore.DeleteBookDirectoryAsync(bookId, CancellationToken.None);
        }
        catch
        {
        }
    }

    private static bool TryResolveFormat(string sourceDisplayName, out EbookFormat format) =>
        EbookFormatExtensions.TryFromFilename(sourceDisplayName, out format);

    private static string GetSourceDisplayName(string sourcePath)
    {
        var sourceDisplayName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceDisplayName))
        {
            throw new ArgumentException("The source path must include a file name.", nameof(sourcePath));
        }

        return sourceDisplayName;
    }
}
