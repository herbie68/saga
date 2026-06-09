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
    IMetadataSourceResolver metadataSourceResolver,
    IImportExceptionClassifier exceptionClassifier)
{
    private const string InvalidSourceDisplayName = "(invalid source)";

    private readonly IBookRepository bookRepository = bookRepository;
    private readonly IImportRepository importRepository = importRepository;
    private readonly ILibraryFileStore fileStore = fileStore;
    private readonly IFileHasher hasher = hasher;
    private readonly IImportExceptionClassifier exceptionClassifier = exceptionClassifier;
    private readonly IMetadataSourceResolver metadataSourceResolver = metadataSourceResolver;

    public async Task<ImportBatchResult> ImportAsync(
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var startedUtc = DateTimeOffset.UtcNow;
        var runId = await importRepository.StartRunAsync(startedUtc, cancellationToken);
        var results = new List<ImportItemResult>(sourcePaths.Count);

        try
        {
            for (var sequence = 0; sequence < sourcePaths.Count; sequence++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = await ImportSingleAsync(runId, sequence, sourcePaths[sequence], cancellationToken);
                results.Add(item);
            }
        }
        finally
        {
            try
            {
                await importRepository.CompleteRunAsync(runId, DateTimeOffset.UtcNow, CancellationToken.None);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        return new ImportBatchResult(runId, results);
    }

    private async Task<ImportItemResult> ImportSingleAsync(
        Guid runId,
        int sequence,
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        var sourceDisplayName = GetSafeSourceDisplayName(sourcePath);
        ImportItemResult? result = null;

        if (sourceDisplayName == InvalidSourceDisplayName)
        {
            result = CreateFailedResult(sourcePath, sourceDisplayName, SafeImportMessages.InvalidSourcePath);
        }
        else if (!TryResolveFormat(sourceDisplayName, out var format))
        {
            result = CreateFailedResult(sourcePath, sourceDisplayName, SafeImportMessages.UnsupportedFormat);
        }
        else
        {
            var bookId = Guid.NewGuid();
            var copied = false;
            var bookPersisted = false;
            var shouldCleanup = false;

            try
            {
                string sha256;
                try
                {
                    sha256 = await hasher.ComputeSha256Async(sourcePath!, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    result = CreateFailedResult(sourcePath, sourceDisplayName, SafeImportMessages.SourceUnreadable);
                    return await RecordAndReturnAsync(runId, sequence, sourceDisplayName, result, cancellationToken);
                }

                if (await bookRepository.HasHashAsync(sha256, cancellationToken))
                {
                    result = CreateSuccessResult(
                        sourcePath,
                        ImportOutcome.ExactDuplicate,
                        SafeImportMessages.ExactDuplicate);
                    return await RecordAndReturnAsync(runId, sequence, sourceDisplayName, result, cancellationToken);
                }

                MetadataReadResult metadata;
                try
                {
                    metadata = await metadataSourceResolver.ReadAsync(sourcePath!, format, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    result = CreateFailedResult(sourcePath, sourceDisplayName, SafeImportMessages.MetadataReadFailed);
                    return await RecordAndReturnAsync(runId, sequence, sourceDisplayName, result, cancellationToken);
                }

                if (await bookRepository.HasNormalizedTitleAndAuthorAsync(
                        metadata.Metadata.Title,
                        metadata.Metadata.Authors,
                        cancellationToken))
                {
                    result = CreateSuccessResult(
                        sourcePath,
                        ImportOutcome.PossibleDuplicate,
                        SafeImportMessages.PossibleDuplicate);
                    return await RecordAndReturnAsync(runId, sequence, sourceDisplayName, result, cancellationToken);
                }

                var copy = await fileStore.CopyIntoLibraryAsync(
                    bookId,
                    sourcePath!,
                    metadata.Metadata.CoverBytes,
                    cancellationToken);
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
                    new FileInfo(sourcePath!).Length,
                    MetadataWriteBackStatus.NotAttempted,
                    null);

                try
                {
                    await bookRepository.AddAsync(book, file, cancellationToken);
                    bookPersisted = true;
                    result = CreateAddedResult(
                        sourcePath,
                        metadata.Warning,
                        bookId);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exceptionClassifier.IsDuplicateKeyViolation(exception))
                {
                    result = CreateSuccessResult(
                        sourcePath,
                        ImportOutcome.PossibleDuplicate,
                        SafeImportMessages.PossibleDuplicate);
                    shouldCleanup = true;
                }
                catch
                {
                    result = CreateFailedResult(sourcePath, sourceDisplayName, SafeImportMessages.ImportFailed);
                    shouldCleanup = true;
                }
            }
            finally
            {
                if (shouldCleanup || (!bookPersisted && copied))
                {
                    var cleanupIncomplete = await CleanupImportedBookAsync(bookId);
                    if (
                        cleanupIncomplete
                        && result is not null
                        && result.Outcome is ImportOutcome.Failed or ImportOutcome.PossibleDuplicate)
                    {
                        result = result with { Message = AppendCleanupIncomplete(result.Message) };
                    }
                }
            }
        }

        return await RecordAndReturnAsync(runId, sequence, sourceDisplayName, result!, cancellationToken);
    }

    private async Task<ImportItemResult> RecordAndReturnAsync(
        Guid runId,
        int sequence,
        string sourceDisplayName,
        ImportItemResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await importRepository.RecordItemAsync(
                runId,
                sequence,
                sourceDisplayName,
                result.Outcome,
                result.Message,
                result.BookId,
                CancellationToken.None);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var cleanupIncomplete = result.BookId is { } bookId && await CleanupImportedBookAsync(bookId);
            var message = cleanupIncomplete
                ? $"{SafeImportMessages.CannotPersistResult}; {SafeImportMessages.CleanupIncomplete}"
                : SafeImportMessages.CannotPersistResult;
            throw new ImportPersistenceException(message, exception);
        }
    }

    private static ImportItemResult CreateFailedResult(
        string? sourcePath,
        string sourceDisplayName,
        string message) =>
        new(IsBlank(sourcePath) ? sourceDisplayName : sourcePath!, ImportOutcome.Failed, message);

    private static ImportItemResult CreateSuccessResult(
        string? sourcePath,
        ImportOutcome outcome,
        string message) =>
        new(IsBlank(sourcePath) ? InvalidSourceDisplayName : sourcePath!, outcome, message);

    private static ImportItemResult CreateAddedResult(
        string? sourcePath,
        string? warning,
        Guid bookId)
    {
        var message = warning is null
            ? SafeImportMessages.Added
            : $"{SafeImportMessages.Added}; {SafeImportMessages.MetadataWarning}: {warning}";
        return new(IsBlank(sourcePath) ? InvalidSourceDisplayName : sourcePath!, ImportOutcome.Added, message, bookId);
    }

    private async Task<bool> CleanupImportedBookAsync(Guid bookId)
    {
        var cleanupIncomplete = false;

        try
        {
            await bookRepository.DeleteAsync(bookId, CancellationToken.None);
        }
        catch
        {
            cleanupIncomplete = true;
        }

        try
        {
            await fileStore.DeleteBookDirectoryAsync(bookId, CancellationToken.None);
        }
        catch
        {
            cleanupIncomplete = true;
        }

        return cleanupIncomplete;
    }

    private static bool TryResolveFormat(string sourceDisplayName, out EbookFormat format) =>
        EbookFormatExtensions.TryFromFilename(sourceDisplayName, out format);

    private static string GetSafeSourceDisplayName(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return InvalidSourceDisplayName;
        }

        try
        {
            var fileName = Path.GetFileName(sourcePath);
            return string.IsNullOrWhiteSpace(fileName) ? InvalidSourceDisplayName : fileName;
        }
        catch
        {
            return InvalidSourceDisplayName;
        }
    }

    private static string AppendCleanupIncomplete(string message) =>
        $"{message}; {SafeImportMessages.CleanupIncomplete}";

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static class SafeImportMessages
    {
        public const string Added = "added";
        public const string CleanupIncomplete = "cleanup incomplete";
        public const string ExactDuplicate = "exact duplicate skipped";
        public const string ImportFailed = "import failed";
        public const string CannotPersistResult = "cannot persist result";
        public const string InvalidSourcePath = "invalid source path";
        public const string MetadataReadFailed = "metadata read failed";
        public const string MetadataWarning = "metadata warning";
        public const string PossibleDuplicate = "possible duplicate";
        public const string SourceUnreadable = "source unreadable; make sure the file is available locally";
        public const string UnsupportedFormat = "unsupported format";
    }
}
