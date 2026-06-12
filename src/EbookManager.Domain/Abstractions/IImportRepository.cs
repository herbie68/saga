using EbookManager.Domain.Importing;

namespace EbookManager.Domain.Abstractions;

public interface IImportRepository
{
    Task<Guid> StartRunAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken);

    Task<Guid> StartRunAsync(
        DateTimeOffset startedUtc,
        ImportRunContext? context,
        CancellationToken cancellationToken);

    Task RecordItemAsync(
        Guid runId,
        int sequence,
        string sourceDisplayName,
        ImportOutcome outcome,
        string message,
        Guid? bookId,
        CancellationToken cancellationToken);

    Task CompleteRunAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken);

    Task<ImportRunResult?> GetAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ImportRunSummary>> ListRecentAsync(int maxCount, CancellationToken cancellationToken);
}
