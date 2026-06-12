using EbookManager.Domain.Importing;
using EbookManager.Presentation.ViewModels;

namespace EbookManager.Presentation.Abstractions;

public interface IUserInteractionService
{
    Task<IReadOnlyList<string>> PickBookFilesAsync(CancellationToken cancellationToken);
    Task<string?> PickScanFolderAsync(CancellationToken cancellationToken);
    Task<string?> PickLibraryDirectoryAsync(string title, CancellationToken cancellationToken);
    Task<bool> ConfirmDeleteAsync(string title, CancellationToken cancellationToken);
    Task<string?> PromptTextAsync(
        string title,
        string message,
        string initialValue,
        CancellationToken cancellationToken);
    Task<bool> ConfirmMetadataValueRemovalAsync(
        string value,
        int affectedBookCount,
        CancellationToken cancellationToken);
    Task ShowImportResultAsync(ImportResultViewModel result, CancellationToken cancellationToken);
    Task<Guid?> PickImportRunAsync(ImportHistoryViewModel history, CancellationToken cancellationToken);
}
