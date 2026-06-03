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
    DirectoryScanner? directoryScanner = null,
    IAppSettingsStore? settingsStore = null)
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
    private readonly IAppSettingsStore? settingsStore = settingsStore;
    private IReadOnlyList<Book> books = [];
    private bool hasAppliedDefaultView;

    public ObservableCollection<BookRowViewModel> VisibleBooks { get; } = [];
    public ObservableCollection<FacetFilterViewModel> AuthorFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> CategoryFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> SeriesFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> StatusFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> EReaderFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> LanguageFilters { get; } = [];

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
        await ApplyDefaultViewAsync(cancellationToken);
        books = await bookRepository.ListAsync(cancellationToken);
        RefreshFacetFilters();
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

        var includeSubdirectories = settingsStore is null ||
            (await settingsStore.LoadAsync(cancellationToken)).IncludeScanSubdirectories;
        var files = directoryScanner.Scan(folder, includeSubdirectories, cancellationToken);
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
        currentLibrary.Set(library);
        RefreshLibraryDisplay();
        await databaseInitializer.InitializeAsync(library, cancellationToken);
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
        currentLibrary.Set(library);
        RefreshLibraryDisplay();
        await databaseInitializer.InitializeAsync(library, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedBook?.Id;
        var rows = ApplyLanguageFilter(ApplyEReaderFilter(ApplyStatusFilter(ApplySeriesFilter(
                ApplyCategoryFilter(ApplyAuthorFilter(searchService.Filter(books, SearchText)))))))
            .Select(book => new BookRowViewModel(book, SearchText))
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

    private IReadOnlyList<Book> ApplyAuthorFilter(IReadOnlyList<Book> source)
    {
        return ApplyFacetFilter(
            source,
            AuthorFilters,
            book => book.Metadata.Authors);
    }

    private IReadOnlyList<Book> ApplyCategoryFilter(IReadOnlyList<Book> source)
    {
        return ApplyFacetFilter(
            source,
            CategoryFilters,
            book => book.Metadata.Tags ?? []);
    }

    private IReadOnlyList<Book> ApplySeriesFilter(IReadOnlyList<Book> source)
    {
        return ApplyFacetFilter(
            source,
            SeriesFilters,
            book => SingleOptionalValue(book.Metadata.Series));
    }

    private IReadOnlyList<Book> ApplyStatusFilter(IReadOnlyList<Book> source)
    {
        return ApplyFacetFilter(
            source,
            StatusFilters,
            book => [book.ReadingStatus.ToString()]);
    }

    private IReadOnlyList<Book> ApplyEReaderFilter(IReadOnlyList<Book> source)
    {
        return ApplyFacetFilter(
            source,
            EReaderFilters,
            book => [new BookRowViewModel(book).EReader]);
    }

    private IReadOnlyList<Book> ApplyLanguageFilter(IReadOnlyList<Book> source)
    {
        return ApplyFacetFilter(
            source,
            LanguageFilters,
            book => SingleOptionalValue(book.Metadata.Language));
    }

    private static IReadOnlyList<Book> ApplyFacetFilter(
        IReadOnlyList<Book> source,
        IReadOnlyCollection<FacetFilterViewModel> filters,
        Func<Book, IEnumerable<string>> valueSelector)
    {
        if (filters.Count == 0)
        {
            return source;
        }

        var selectedValues = filters
            .Where(filter => filter.IsSelected)
            .Select(filter => filter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedValues.Count == filters.Count)
        {
            return source;
        }

        return source
            .Where(book => valueSelector(book).Any(selectedValues.Contains))
            .ToList();
    }

    private void RefreshFacetFilters()
    {
        RefreshFilters(
            AuthorFilters,
            books.SelectMany(book => book.Metadata.Authors));
        RefreshFilters(
            CategoryFilters,
            books.SelectMany(book => book.Metadata.Tags ?? []));
        RefreshFilters(
            SeriesFilters,
            books.SelectMany(book => SingleOptionalValue(book.Metadata.Series)));
        RefreshStatusFilters();
        RefreshFilters(
            EReaderFilters,
            books.Select(book => new BookRowViewModel(book).EReader));
        RefreshFilters(
            LanguageFilters,
            books.SelectMany(book => SingleOptionalValue(book.Metadata.Language)));
    }

    private void RefreshFilters(
        ObservableCollection<FacetFilterViewModel> filters,
        IEnumerable<string> values)
    {
        var existingSelections = filters.ToDictionary(
            filter => filter.Name,
            filter => filter.IsSelected,
            StringComparer.OrdinalIgnoreCase);
        var valueCounts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.First(),
                Count = group.Count()
            })
            .OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        filters.Clear();
        foreach (var value in valueCounts)
        {
            var isSelected = !existingSelections.TryGetValue(value.Name, out var existingSelection) || existingSelection;
            filters.Add(new FacetFilterViewModel(value.Name, value.Count, isSelected, ApplyFilter));
        }
    }

    private void RefreshStatusFilters()
    {
        var existingSelections = StatusFilters.ToDictionary(
            filter => filter.Name,
            filter => filter.IsSelected,
            StringComparer.OrdinalIgnoreCase);
        var statusCounts = books
            .GroupBy(book => book.ReadingStatus)
            .ToDictionary(group => group.Key, group => group.Count());

        StatusFilters.Clear();
        foreach (var status in Enum.GetValues<ReadingStatus>())
        {
            if (!statusCounts.TryGetValue(status, out var count))
            {
                continue;
            }

            var name = status.ToString();
            var isSelected = !existingSelections.TryGetValue(name, out var existingSelection) || existingSelection;
            StatusFilters.Add(new FacetFilterViewModel(name, count, isSelected, ApplyFilter));
        }
    }

    private static IEnumerable<string> SingleOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];

    partial void OnCurrentLibraryPathChanged(string? value) => OnPropertyChanged(nameof(HasActiveLibrary));

    private async Task ApplyDefaultViewAsync(CancellationToken cancellationToken)
    {
        if (hasAppliedDefaultView || settingsStore is null)
        {
            return;
        }

        hasAppliedDefaultView = true;
        var settings = await settingsStore.LoadAsync(cancellationToken);
        if (Enum.TryParse<LibraryView>(settings.DefaultView, ignoreCase: true, out var defaultView))
        {
            SelectedView = defaultView;
        }
    }

    private void RefreshLibraryDisplay()
    {
        CurrentLibraryName = currentLibrary?.Current?.Name ?? "No library selected";
        CurrentLibraryPath = currentLibrary?.Current?.DirectoryPath;
    }
}
