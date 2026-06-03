using EbookManager.Domain.Importing;
using EbookManager.Presentation.ViewModels;

namespace EbookManager.Presentation.Abstractions;

public interface IUserInteractionService
{
    Task<IReadOnlyList<string>> PickBookFilesAsync(CancellationToken cancellationToken);
    Task<string?> PickScanFolderAsync(CancellationToken cancellationToken);
    Task<bool> ConfirmDeleteAsync(string title, CancellationToken cancellationToken);
    Task ShowImportResultAsync(ImportResultViewModel result, CancellationToken cancellationToken);
}
