using CommunityToolkit.Mvvm.ComponentModel;
using EbookManager.Domain.Importing;

namespace EbookManager.Presentation.ViewModels;

public sealed partial class ImportJobViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool isIndeterminate;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private int processedCount;

    [ObservableProperty]
    private int addedCount;

    [ObservableProperty]
    private int duplicateCount;

    [ObservableProperty]
    private int possibleDuplicateCount;

    [ObservableProperty]
    private int failedCount;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private ImportBatchResult? latestResult;

    public bool CanShowDetails => LatestResult is not null;

    public double ProgressValue => TotalCount <= 0 ? 0 : ProcessedCount * 100.0 / TotalCount;

    public string ProgressText => TotalCount <= 0 ? StatusText : $"Processed {ProcessedCount} of {TotalCount}";

    public void StartScanning()
    {
        IsVisible = true;
        IsActive = true;
        IsIndeterminate = true;
        Title = "Scanning folder...";
        StatusText = "Finding ebook files...";
        ClearCounts();
    }

    public void StartImport(Guid runId, int totalCount)
    {
        IsVisible = true;
        IsActive = true;
        IsIndeterminate = false;
        Title = "Importing books...";
        TotalCount = totalCount;
        ProcessedCount = 0;
        RefreshProgressProperties();
    }

    public void ApplyProgress(ImportProgress progress)
    {
        TotalCount = progress.TotalCount;
        ProcessedCount = progress.ProcessedCount;
        AddedCount = progress.AddedCount;
        DuplicateCount = progress.ExactDuplicateCount;
        PossibleDuplicateCount = progress.PossibleDuplicateCount;
        FailedCount = progress.FailedCount;
        StatusText = progress.LatestItem is null
            ? string.Empty
            : Path.GetFileName(progress.LatestItem.SourcePath);
        RefreshProgressProperties();
    }

    public void Complete(ImportBatchResult result)
    {
        LatestResult = result;
        IsActive = false;
        IsIndeterminate = false;
        Title = "Import complete";
        OnPropertyChanged(nameof(CanShowDetails));
    }

    public void Cancelled()
    {
        IsActive = false;
        IsIndeterminate = false;
        Title = "Import cancelled";
    }

    public void Failed(string message)
    {
        IsVisible = true;
        IsActive = false;
        IsIndeterminate = false;
        Title = "Import failed";
        StatusText = message;
        RefreshProgressProperties();
    }

    private void ClearCounts()
    {
        TotalCount = 0;
        ProcessedCount = 0;
        AddedCount = 0;
        DuplicateCount = 0;
        PossibleDuplicateCount = 0;
        FailedCount = 0;
        LatestResult = null;
        OnPropertyChanged(nameof(CanShowDetails));
        RefreshProgressProperties();
    }

    private void RefreshProgressProperties()
    {
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressText));
    }
}
