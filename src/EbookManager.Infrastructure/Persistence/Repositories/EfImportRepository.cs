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

    public Task<Guid> StartRunAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken) =>
        StartRunAsync(startedUtc, ImportRunContext.FileImport, cancellationToken);

    public async Task<Guid> StartRunAsync(
        DateTimeOffset startedUtc,
        ImportRunContext? runContext,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory.Create(libraryPath);
        runContext ??= ImportRunContext.FileImport;
        var run = new ImportRunEntity
        {
            Id = Guid.NewGuid(),
            StartedUtc = startedUtc,
            Kind = runContext.Kind.ToString(),
            SourcePath = runContext.SourcePath,
            IncludeSubdirectories = runContext.IncludeSubdirectories
        };
        context.ImportRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task RecordItemAsync(
        Guid runId,
        int sequence,
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
            Sequence = sequence,
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
            .OrderBy(x => x.Sequence)
            .ThenBy(x => x.Id)
            .Select(x => new ImportItemResult(x.SourcePath, x.Outcome, x.Message, x.BookId))
            .ToList()
            .AsReadOnly();

        return new ImportRunResult(run.Id, run.StartedUtc, run.CompletedUtc, items, ToContext(run));
    }

    public async Task<IReadOnlyList<ImportRunSummary>> ListRecentAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        await using var context = contextFactory.Create(libraryPath);
        var runs = await context.ImportRuns
            .AsNoTracking()
            .Include(x => x.Items)
            .ToListAsync(cancellationToken);

        return runs
            .OrderByDescending(x => x.StartedUtc)
            .ThenByDescending(x => x.Id)
            .Take(maxCount)
            .Select(x => new ImportRunSummary(
                x.Id,
                x.StartedUtc,
                x.CompletedUtc,
                x.Items.Count,
                x.Items.Count(item => item.Outcome == ImportOutcome.Added),
                x.Items.Count(item => item.Outcome == ImportOutcome.ExactDuplicate),
                x.Items.Count(item => item.Outcome == ImportOutcome.PossibleDuplicate),
                x.Items.Count(item => item.Outcome == ImportOutcome.Failed),
                ToContext(x)))
            .ToList()
            .AsReadOnly();
    }

    private static ImportRunContext ToContext(ImportRunEntity run)
    {
        var kind = Enum.TryParse<ImportRunKind>(run.Kind, out var parsedKind)
            ? parsedKind
            : ImportRunKind.FileImport;
        return new ImportRunContext(kind, run.SourcePath, run.IncludeSubdirectories);
    }
}
