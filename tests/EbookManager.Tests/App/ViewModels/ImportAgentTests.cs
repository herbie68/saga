using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Importing;
using EbookManager.Presentation.Importing;
using EbookManager.Presentation.ViewModels;
using FluentAssertions;

namespace EbookManager.Tests.App.ViewModels;

public sealed class ImportAgentTests
{
    [Fact]
    public async Task StartImportAsync_runs_job_and_exposes_completion()
    {
        var job = new ImportJobViewModel();
        var runner = new FakeImportRunner();
        var agent = new ImportAgent(runner, job);

        await agent.StartImportAsync(["a.epub", "b.epub"], _ => Task.CompletedTask, CancellationToken.None);
        await agent.ActiveTask!;

        job.IsActive.Should().BeFalse();
        job.CanShowDetails.Should().BeTrue();
        job.LatestResult!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task CancelActiveJob_marks_job_cancelled()
    {
        var job = new ImportJobViewModel();
        var runner = new BlockingImportRunner();
        var agent = new ImportAgent(runner, job);

        await agent.StartImportAsync(["a.epub"], _ => Task.CompletedTask, CancellationToken.None);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        agent.CancelActiveJob();
        await agent.ActiveTask!;

        job.IsActive.Should().BeFalse();
        job.Title.Should().Be("Import cancelled");
    }

    [Fact]
    public async Task Cancelled_partial_result_enables_details()
    {
        var job = new ImportJobViewModel();
        var runner = new CancelledResultImportRunner();
        var agent = new ImportAgent(runner, job);

        await agent.StartImportAsync(["a.epub"], _ => Task.CompletedTask, CancellationToken.None);
        await agent.ActiveTask!;

        job.IsActive.Should().BeFalse();
        job.Title.Should().Be("Import cancelled");
        job.CanShowDetails.Should().BeTrue();
        job.LatestResult!.WasCancelled.Should().BeTrue();
        job.LatestResult.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task StartScanning_marks_agent_active_until_import_starts_or_is_cancelled()
    {
        var job = new ImportJobViewModel();
        var runner = new FakeImportRunner();
        var agent = new ImportAgent(runner, job);

        agent.StartScanning();

        agent.IsActive.Should().BeTrue();

        await agent.StartImportAsync(["a.epub"], _ => Task.CompletedTask, CancellationToken.None);
        await agent.ActiveTask!;

        agent.IsActive.Should().BeFalse();
    }

    [Fact]
    public void CancelActiveJob_marks_scanning_job_cancelled()
    {
        var job = new ImportJobViewModel();
        var runner = new FakeImportRunner();
        var agent = new ImportAgent(runner, job);

        agent.StartScanning();
        agent.CancelActiveJob();

        agent.IsActive.Should().BeFalse();
        job.Title.Should().Be("Import cancelled");
    }

    [Fact]
    public void Close_hides_inactive_job_card()
    {
        var job = new ImportJobViewModel();

        job.Cancelled();
        job.Close();

        job.IsVisible.Should().BeFalse();
    }

    private sealed class FakeImportRunner : IImportRunner
    {
        public Task<ImportBatchResult> ImportAsync(
            IReadOnlyList<string> sourcePaths,
            IProgress<ImportProgress>? progress,
            CancellationToken cancellationToken = default,
            ImportRunContext? context = null)
        {
            var runId = Guid.NewGuid();
            var items = sourcePaths
                .Select(path => new ImportItemResult(path, ImportOutcome.Added, "added"))
                .ToArray();
            for (var index = 0; index < items.Length; index++)
            {
                progress?.Report(ImportProgress.FromItems(runId, sourcePaths.Count, items.Take(index + 1).ToArray()));
            }

            return Task.FromResult(new ImportBatchResult(runId, items));
        }
    }

    private sealed class BlockingImportRunner : IImportRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ImportBatchResult> ImportAsync(
            IReadOnlyList<string> sourcePaths,
            IProgress<ImportProgress>? progress,
            CancellationToken cancellationToken = default,
            ImportRunContext? context = null)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The delay should be cancelled.");
        }
    }

    private sealed class CancelledResultImportRunner : IImportRunner
    {
        public Task<ImportBatchResult> ImportAsync(
            IReadOnlyList<string> sourcePaths,
            IProgress<ImportProgress>? progress,
            CancellationToken cancellationToken = default,
            ImportRunContext? context = null)
        {
            var result = new ImportBatchResult(
                Guid.NewGuid(),
                [new ImportItemResult(sourcePaths[0], ImportOutcome.Added, "added")],
                WasCancelled: true);
            return Task.FromResult(result);
        }
    }
}
