using EbookManager.Domain.Importing;

namespace EbookManager.Presentation.ViewModels;

public sealed class ImportResultViewModel
{
    public ImportResultViewModel(ImportRunResult result)
        : this(new ImportBatchResult(result.Id, result.Items))
    {
    }

    public ImportResultViewModel(ImportBatchResult result)
    {
        RunId = result.RunId;
        Items = result.Items
            .Select(item => new ImportResultItemViewModel(item))
            .ToList()
            .AsReadOnly();
    }

    public Guid RunId { get; }
    public IReadOnlyList<ImportResultItemViewModel> Items { get; }
    public int TotalCount => Items.Count;
    public int AddedCount => Count(ImportOutcome.Added);
    public int ExactDuplicateCount => Count(ImportOutcome.ExactDuplicate);
    public int PossibleDuplicateCount => Count(ImportOutcome.PossibleDuplicate);
    public int SkippedCount => ExactDuplicateCount + PossibleDuplicateCount;
    public int FailedCount => Count(ImportOutcome.Failed);
    public bool HasProblems => SkippedCount > 0 || FailedCount > 0;
    public string SummaryText =>
        $"{TotalCount} files processed: {AddedCount} added, {SkippedCount} skipped, {FailedCount} failed.";

    private int Count(ImportOutcome outcome) => Items.Count(item => item.Outcome == outcome);
}

public sealed class ImportHistoryViewModel(IEnumerable<ImportRunSummary> summaries)
{
    public IReadOnlyList<ImportRunSummaryViewModel> Items { get; } = summaries
        .Select(summary => new ImportRunSummaryViewModel(summary))
        .ToList()
        .AsReadOnly();

    public bool HasItems => Items.Count > 0;
}

public sealed class ImportRunSummaryViewModel(ImportRunSummary summary)
{
    public Guid RunId { get; } = summary.Id;
    public DateTimeOffset StartedUtc { get; } = summary.StartedUtc;
    public DateTimeOffset? CompletedUtc { get; } = summary.CompletedUtc;
    public ImportRunContext? Context { get; } = summary.Context;
    public int TotalCount { get; } = summary.TotalCount;
    public int AddedCount { get; } = summary.AddedCount;
    public int SkippedCount { get; } = summary.SkippedCount;
    public int FailedCount { get; } = summary.FailedCount;
    public string StartedText { get; } = summary.StartedUtc.ToLocalTime().ToString("g");
    public string CompletedText { get; } = summary.CompletedUtc?.ToLocalTime().ToString("g") ?? string.Empty;
    public string KindText { get; } = summary.Context?.Kind.ToString() ?? ImportRunKind.FileImport.ToString();
    public string SourceText { get; } = string.IsNullOrWhiteSpace(summary.Context?.SourcePath)
        ? string.Empty
        : summary.Context.SourcePath;
    public string DurationText { get; } = summary.CompletedUtc is null
        ? string.Empty
        : FormatDuration(summary.CompletedUtc.Value - summary.StartedUtc);

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
}

public sealed class ImportResultItemViewModel(ImportItemResult item)
{
    public string SourcePath { get; } = item.SourcePath;
    public string FileName { get; } = Path.GetFileName(item.SourcePath);
    public ImportOutcome Outcome { get; } = item.Outcome;
    public string OutcomeLabel { get; } = item.Outcome switch
    {
        ImportOutcome.Added => "Added",
        ImportOutcome.ExactDuplicate => "Skipped duplicate",
        ImportOutcome.PossibleDuplicate => "Possible duplicate",
        ImportOutcome.Failed => "Failed",
        _ => item.Outcome.ToString()
    };
    public string Message { get; } = item.Message;
    public Guid? BookId { get; } = item.BookId;
}
