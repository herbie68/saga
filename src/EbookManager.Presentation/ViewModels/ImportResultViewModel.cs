using EbookManager.Domain.Importing;

namespace EbookManager.Presentation.ViewModels;

public sealed class ImportResultViewModel
{
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
    public int AddedCount => Count(ImportOutcome.Added);
    public int ExactDuplicateCount => Count(ImportOutcome.ExactDuplicate);
    public int PossibleDuplicateCount => Count(ImportOutcome.PossibleDuplicate);
    public int FailedCount => Count(ImportOutcome.Failed);

    private int Count(ImportOutcome outcome) => Items.Count(item => item.Outcome == outcome);
}

public sealed class ImportResultItemViewModel(ImportItemResult item)
{
    public string SourcePath { get; } = item.SourcePath;
    public ImportOutcome Outcome { get; } = item.Outcome;
    public string Message { get; } = item.Message;
    public Guid? BookId { get; } = item.BookId;
}
