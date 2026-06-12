using EbookManager.Domain.Importing;
using EbookManager.Presentation.ViewModels;

namespace EbookManager.Presentation.Abstractions;

public interface IImportAgent
{
    event EventHandler<ImportBatchResult>? Completed;

    ImportJobViewModel Job { get; }

    bool IsActive { get; }

    void StartScanning();

    Task StartImportAsync(
        IReadOnlyList<string> sourcePaths,
        Func<ImportProgress, Task> onProgress,
        CancellationToken cancellationToken,
        ImportRunContext? context = null);

    void CancelActiveJob();
}
