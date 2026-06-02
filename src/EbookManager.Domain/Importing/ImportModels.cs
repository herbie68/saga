namespace EbookManager.Domain.Importing;

public enum ImportOutcome
{
    Added,
    ExactDuplicate,
    PossibleDuplicate,
    Failed
}

public sealed record ImportItemResult(
    string SourcePath,
    ImportOutcome Outcome,
    string Message,
    Guid? BookId = null);

public sealed record ImportBatchResult(Guid RunId, IReadOnlyList<ImportItemResult> Items);

public sealed record ImportRunResult(
    Guid Id,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    IReadOnlyList<ImportItemResult> Items);
