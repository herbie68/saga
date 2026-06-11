namespace EbookManager.Domain.Importing;

public sealed record ImportProgress(
    Guid RunId,
    int TotalCount,
    int ProcessedCount,
    int AddedCount,
    int ExactDuplicateCount,
    int PossibleDuplicateCount,
    int FailedCount,
    ImportItemResult? LatestItem)
{
    public int SkippedCount => ExactDuplicateCount + PossibleDuplicateCount;

    public static ImportProgress FromItems(
        Guid runId,
        int totalCount,
        IReadOnlyList<ImportItemResult> processedItems) =>
        new(
            runId,
            totalCount,
            processedItems.Count,
            processedItems.Count(item => item.Outcome == ImportOutcome.Added),
            processedItems.Count(item => item.Outcome == ImportOutcome.ExactDuplicate),
            processedItems.Count(item => item.Outcome == ImportOutcome.PossibleDuplicate),
            processedItems.Count(item => item.Outcome == ImportOutcome.Failed),
            processedItems.LastOrDefault());
}
