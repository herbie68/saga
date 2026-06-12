using EbookManager.Domain.Importing;

namespace EbookManager.Domain.Abstractions;

public interface IImportRunner
{
    Task<ImportBatchResult> ImportAsync(
        IReadOnlyList<string> sourcePaths,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken = default,
        ImportRunContext? context = null);
}
