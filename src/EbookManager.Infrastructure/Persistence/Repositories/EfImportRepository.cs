using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EbookManager.Infrastructure.Persistence.Repositories;

public sealed class EfImportRepository(
    LibraryDbContextFactory contextFactory,
    string libraryPath) : IImportRepository
{
    private readonly LibraryDbContextFactory contextFactory = contextFactory;
    private readonly string libraryPath = libraryPath;

    public async Task<Guid> StartRunAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        var run = new ImportRunEntity
        {
            Id = Guid.NewGuid(),
            StartedUtc = startedUtc
        };
        context.ImportRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task RecordItemAsync(
        Guid runId,
        string sourceDisplayName,
        ImportOutcome outcome,
        string message,
        Guid? bookId,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        context.ImportItems.Add(new ImportItemEntity
        {
            Id = Guid.NewGuid(),
            ImportRunId = runId,
            SourcePath = sourceDisplayName,
            Outcome = outcome,
            Message = message,
            BookId = bookId
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        var run = await context.ImportRuns.SingleAsync(x => x.Id == runId, cancellationToken);
        run.CompletedUtc = completedUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ImportRunResult?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        var run = await context.ImportRuns
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);

        if (run is null)
        {
            return null;
        }

        var items = run.Items
            .OrderBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .Select(x => new ImportItemResult(x.SourcePath, x.Outcome, x.Message, x.BookId))
            .ToList()
            .AsReadOnly();

        return new ImportRunResult(run.Id, run.StartedUtc, run.CompletedUtc, items);
    }
}
