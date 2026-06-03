using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EbookManager.Application.Books;
using EbookManager.Application.Importing;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Libraries;
using EbookManager.Presentation.Abstractions;

namespace EbookManager.Presentation.ViewModels;

public sealed partial class LibraryViewModel(
    IBookRepository bookRepository,
    BookSearchService searchService,
    BookDetailsViewModel details,
    IUserInteractionService userInteraction,
    ImportService? importService = null,
    LibraryService? libraryService = null,
    CurrentLibrary? currentLibrary = null,
    ILibraryDatabaseInitializer? databaseInitializer = null,
    DirectoryScanner? directoryScanner = null)
    : ObservableObject
{
    private readonly IBookRepository bookRepository = bookRepository;
    private readonly BookSearchService searchService = searchService;
    private readonly IUserInteractionService userInteraction = userInteraction;
    private readonly ImportService? importService = importService;
    private readonly LibraryService? libraryService = libraryService;
    private readonly CurrentLibrary? currentLibrary = currentLibrary;
    private readonly ILibraryDatabaseInitializer? databaseInitializer = databaseInitializer;
    private readonly DirectoryScanner? directoryScanner = directoryScanner;
    private IReadOnlyList<Book> books = [];

    public ObservableCollection<BookRowViewModel> VisibleBooks { get; } = [];

    public BookDetailsViewModel Details { get; } = details;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private LibraryView selectedView = LibraryView.Detailed;

    [ObservableProperty]
    private BookRowViewModel? selectedBook;

    [ObservableProperty]
    private ImportResultViewModel? lastImportResult;

    [ObservableProperty]
    private string currentLibraryName = currentLibrary?.Current?.Name ?? "No library selected";

    [ObservableProperty]
    private string? currentLibraryPath = currentLibrary?.Current?.DirectoryPath;

    [ObservableProperty]
    private string emptyStateMessage = "Create or open a library to get started.";

    [ObservableProperty]
    private bool includeScanSubdirectories = true;

    public bool HasActiveLibrary => CurrentLibraryPath is not null;

    public int VisibleBookCount => VisibleBooks.Count;

    public IAsyncRelayCommand RefreshCommand => refreshCommand ??= new AsyncRelayCommand(RefreshAsync);
    public IAsyncRelayCommand AddBooksCommand => addBooksCommand ??= new AsyncRelayCommand(AddBooksAsync);
    public IAsyncRelayCommand ScanFolderCommand => scanFolderCommand ??= new AsyncRelayCommand(ScanFolderAsync);
    public IAsyncRelayCommand CreateLibraryCommand => createLibraryCommand ??= new AsyncRelayCommand(CreateLibraryAsync);
    public IAsyncRelayCommand OpenLibraryCommand => openLibraryCommand ??= new AsyncRelayCommand(OpenLibraryAsync);

    private AsyncRelayCommand? refreshCommand;
    private AsyncRelayCommand? addBooksCommand;
    private AsyncRelayCommand? scanFolderCommand;
    private AsyncRelayCommand? createLibraryCommand;
    private AsyncRelayCommand? openLibraryCommand;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        books = await bookRepository.ListAsync(cancellationToken);
        ApplyFilter();
        RefreshLibraryDisplay();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedBookChanged(BookRowViewModel? value)
    {
        if (value is null)
        {
            Details.Clear();
        }
        else
        {
            Details.Load(value.Book);
        }
    }

    private async Task AddBooksAsync(CancellationToken cancellationToken)
    {
        if (!HasActiveLibrary)
        {
            EmptyStateMessage = "Create or open a library before adding books.";
            return;
        }

        var paths = await userInteraction.PickBookFilesAsync(cancellationToken);
        if (paths.Count == 0 || importService is null)
        {
            return;
        }

        await ImportFilesAsync(paths, cancellationToken);
    }

    public async Task ImportFilesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        if (!HasActiveLibrary)
        {
            EmptyStateMessage = "Create or open a library before adding books.";
            return;
        }

        if (paths.Count == 0 || importService is null)
        {
            return;
        }

        var result = await importService.ImportAsync(paths, cancellationToken);
        LastImportResult = new ImportResultViewModel(result);
        await userInteraction.ShowImportResultAsync(LastImportResult, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    private async Task ScanFolderAsync(CancellationToken cancellationToken)
    {
        if (!HasActiveLibrary)
        {
            EmptyStateMessage = "Create or open a library before scanning folders.";
            return;
        }

        if (directoryScanner is null || importService is null)
        {
            return;
        }

        var folder = await userInteraction.PickScanFolderAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var files = directoryScanner.Scan(folder, IncludeScanSubdirectories, cancellationToken);
        await ImportFilesAsync(files, cancellationToken);
    }

    private async Task CreateLibraryAsync(CancellationToken cancellationToken)
    {
        if (libraryService is null || currentLibrary is null || databaseInitializer is null)
        {
            return;
        }

        var directoryPath = await userInteraction.PickLibraryDirectoryAsync("Create ELibrary", cancellationToken);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var selectedDirectoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(directoryPath));
        var libraryRoot = string.Equals(selectedDirectoryName, "ELibrary", StringComparison.OrdinalIgnoreCase)
            ? directoryPath
            : Path.Combine(directoryPath, "ELibrary");
        var library = await libraryService.CreateAsync("ELibrary", libraryRoot, cancellationToken);
        await databaseInitializer.InitializeAsync(library, cancellationToken);
        currentLibrary.Set(library);
        await RefreshAsync(cancellationToken);
    }

    private async Task OpenLibraryAsync(CancellationToken cancellationToken)
    {
        if (libraryService is null || currentLibrary is null || databaseInitializer is null)
        {
            return;
        }

        var directoryPath = await userInteraction.PickLibraryDirectoryAsync("Open library", cancellationToken);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var library = await libraryService.OpenAsync(directoryPath, cancellationToken);
        await databaseInitializer.InitializeAsync(library, cancellationToken);
        currentLibrary.Set(library);
        await RefreshAsync(cancellationToken);
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedBook?.Id;
        var rows = searchService.Filter(books, SearchText)
            .Select(book => new BookRowViewModel(book))
            .ToList();

        VisibleBooks.Clear();
        foreach (var row in rows)
        {
            VisibleBooks.Add(row);
        }

        OnPropertyChanged(nameof(VisibleBookCount));
        SelectedBook = selectedId is null
            ? VisibleBooks.FirstOrDefault()
            : VisibleBooks.FirstOrDefault(row => row.Id == selectedId.Value);
        EmptyStateMessage = HasActiveLibrary
            ? "This library is empty. Add books or scan a folder to begin."
            : "Create or open a library to get started.";
    }

    partial void OnCurrentLibraryPathChanged(string? value) => OnPropertyChanged(nameof(HasActiveLibrary));

    private void RefreshLibraryDisplay()
    {
        CurrentLibraryName = currentLibrary?.Current?.Name ?? "No library selected";
        CurrentLibraryPath = currentLibrary?.Current?.DirectoryPath;
    }
}
