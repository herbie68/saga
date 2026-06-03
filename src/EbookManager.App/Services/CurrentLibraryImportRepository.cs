using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Importing;
using EbookManager.Infrastructure.Persistence;
using EbookManager.Infrastructure.Persistence.Repositories;
using EbookManager.Libraries;

namespace EbookManager.App.Services;

public sealed class CurrentLibraryImportRepository(
    CurrentLibrary currentLibrary,
    LibraryDbContextFactory contextFactory)
    : IImportRepository
{
    public Task<Guid> StartRunAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken) =>
        CreateRepository().StartRunAsync(startedUtc, cancellationToken);

    public Task RecordItemAsync(
        Guid runId,
        int sequence,
        string sourcePath,
        ImportOutcome outcome,
        string message,
        Guid? bookId,
        CancellationToken cancellationToken) =>
        CreateRepository().RecordItemAsync(runId, sequence, sourcePath, outcome, message, bookId, cancellationToken);

    public Task CompleteRunAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken) =>
        CreateRepository().CompleteRunAsync(runId, completedUtc, cancellationToken);

    public Task<ImportRunResult?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
        CreateRepository().GetAsync(runId, cancellationToken);

    private EfImportRepository CreateRepository()
    {
        var library = currentLibrary.Current ?? throw new InvalidOperationException("No active library is loaded.");
        return new EfImportRepository(contextFactory, library.DirectoryPath);
    }
}
