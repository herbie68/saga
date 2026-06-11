using EbookManager.Domain.Importing;
using EbookManager.Presentation.ViewModels;
using FluentAssertions;

namespace EbookManager.Tests.App.ViewModels;

public sealed class ImportJobViewModelTests
{
    [Fact]
    public void Start_and_progress_update_visible_status()
    {
        var viewModel = new ImportJobViewModel();
        var runId = Guid.NewGuid();

        viewModel.StartScanning();
        viewModel.StartImport(runId, totalCount: 100);
        viewModel.ApplyProgress(new ImportProgress(
            runId,
            100,
            25,
            20,
            3,
            1,
            1,
            new ImportItemResult("book.epub", ImportOutcome.Added, "added")));

        viewModel.IsVisible.Should().BeTrue();
        viewModel.IsActive.Should().BeTrue();
        viewModel.IsIndeterminate.Should().BeFalse();
        viewModel.ProgressValue.Should().Be(25);
        viewModel.ProgressText.Should().Be("Processed 25 of 100");
        viewModel.AddedCount.Should().Be(20);
        viewModel.DuplicateCount.Should().Be(3);
        viewModel.PossibleDuplicateCount.Should().Be(1);
        viewModel.FailedCount.Should().Be(1);
    }

    [Fact]
    public void Complete_keeps_card_visible_and_enables_details()
    {
        var viewModel = new ImportJobViewModel();
        var result = new ImportBatchResult(Guid.NewGuid(), [new ImportItemResult("book.epub", ImportOutcome.Added, "added")]);

        viewModel.StartImport(result.RunId, totalCount: 1);
        viewModel.Complete(result);

        viewModel.IsVisible.Should().BeTrue();
        viewModel.IsActive.Should().BeFalse();
        viewModel.CanShowDetails.Should().BeTrue();
        viewModel.LatestResult.Should().BeSameAs(result);
    }
}
