using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EbookManager.Application.Books;
using EbookManager.Application.Importing;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Libraries;
using EbookManager.Presentation.Abstractions;

namespace EbookManager.Presentation.ViewModels;

public sealed partial class LibraryViewModel : ObservableObject
{
    private const int LibraryLoadPageSize = 500;
    private const string MissingActiveLibraryMessage =
        "The active library folder no longer exists. Create or open a library to continue.";

    private readonly IBookRepository bookRepository;
    private readonly BookSearchService searchService;
    private readonly IUserInteractionService userInteraction;
    private readonly ImportService? importService;
    private readonly IImportAgent? importAgent;
    private readonly IImportRepository? importRepository;
    private readonly LibraryService? libraryService;
    private readonly CurrentLibrary? currentLibrary;
    private readonly BookService? bookService;
    private readonly ILibraryDatabaseInitializer? databaseInitializer;
    private readonly DirectoryScanner? directoryScanner;
    private readonly IAppSettingsStore? settingsStore;
    private IReadOnlyList<Book> books = [];
    private bool hasAppliedDefaultView;
    private int selectionVersion;

    public LibraryViewModel(
        IBookRepository bookRepository,
        BookSearchService searchService,
        BookDetailsViewModel details,
        IUserInteractionService userInteraction,
        ImportService? importService = null,
        IImportAgent? importAgent = null,
        IImportRepository? importRepository = null,
        LibraryService? libraryService = null,
        CurrentLibrary? currentLibrary = null,
        BookService? bookService = null,
        ILibraryDatabaseInitializer? databaseInitializer = null,
        DirectoryScanner? directoryScanner = null,
        IAppSettingsStore? settingsStore = null)
    {
        this.bookRepository = bookRepository;
        this.searchService = searchService;
        Details = details;
        this.userInteraction = userInteraction;
        this.importService = importService;
        this.importAgent = importAgent;
        this.importRepository = importRepository;
        this.libraryService = libraryService;
        this.currentLibrary = currentLibrary;
        this.bookService = bookService;
        this.databaseInitializer = databaseInitializer;
        this.directoryScanner = directoryScanner;
        this.settingsStore = settingsStore;
        currentLibraryName = currentLibrary?.Current?.Name ?? "No library selected";
        currentLibraryPath = currentLibrary?.Current?.DirectoryPath;

        details.BookSaved += OnDetailsBookSaved;
        details.BookDeleted += OnDetailsBookDeleted;
        if (importAgent is not null)
        {
            importAgent.Completed += OnImportAgentCompleted;
        }
    }

    public ObservableCollection<BookRowViewModel> VisibleBooks { get; } = [];
    public ObservableCollection<FacetFilterViewModel> AuthorFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> CategoryFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> SeriesFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> StatusFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> EReaderFilters { get; } = [];
    public ObservableCollection<FacetFilterViewModel> LanguageFilters { get; } = [];

    public BookDetailsViewModel Details { get; }

    public ImportJobViewModel? ImportJob => importAgent?.Job;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private LibraryView selectedView = LibraryView.Detailed;

    [ObservableProperty]
    private LibrarySortOption selectedSortOption = LibrarySortOption.None;

    [ObservableProperty]
    private BookRowViewModel? selectedBook;

    [ObservableProperty]
    private ImportResultViewModel? lastImportResult;

    [ObservableProperty]
    private string currentLibraryName = "No library selected";

    [ObservableProperty]
    private string? currentLibraryPath;

    [ObservableProperty]
    private string emptyStateMessage = "Create or open a library to get started.";

    [ObservableProperty]
    private bool isLoadingLibrary;

    [ObservableProperty]
    private int loadingLibraryTotalCount;

    [ObservableProperty]
    private int loadedLibraryCount;

    public double LoadingLibraryProgressValue =>
        LoadingLibraryTotalCount <= 0
            ? 0
            : Math.Min(100, LoadedLibraryCount * 100.0 / LoadingLibraryTotalCount);

    public bool IsLoadingLibraryProgressIndeterminate => LoadingLibraryTotalCount <= 0;

    public string LoadingLibraryProgressText =>
        LoadingLibraryTotalCount <= 0
            ? string.Empty
            : $"{LoadedLibraryCount} / {Math.Max(LoadingLibraryTotalCount, LoadedLibraryCount)}";

    public bool HasActiveLibrary => CurrentLibraryPath is not null;

    public bool HasActiveImport => importAgent?.IsActive == true;

    public int VisibleBookCount => VisibleBooks.Count;

    public IAsyncRelayCommand RefreshCommand => refreshCommand ??= new AsyncRelayCommand(RefreshAsync);
    public IAsyncRelayCommand AddBooksCommand => addBooksCommand ??= new AsyncRelayCommand(AddBooksAsync);
    public IAsyncRelayCommand ScanFolderCommand => scanFolderCommand ??= new AsyncRelayCommand(ScanFolderAsync);
    public IAsyncRelayCommand CreateLibraryCommand => createLibraryCommand ??= new AsyncRelayCommand(CreateLibraryAsync);
    public IAsyncRelayCommand OpenLibraryCommand => openLibraryCommand ??= new AsyncRelayCommand(OpenLibraryAsync);
    public IRelayCommand CancelImportCommand => cancelImportCommand ??= new RelayCommand(() => importAgent?.CancelActiveJob());
    public IAsyncRelayCommand ShowImportDetailsCommand => showImportDetailsCommand ??= new AsyncRelayCommand(ShowImportDetailsAsync);
    public IAsyncRelayCommand ShowImportHistoryCommand => showImportHistoryCommand ??= new AsyncRelayCommand(ShowImportHistoryAsync);
    public IRelayCommand CloseImportJobCommand => closeImportJobCommand ??= new RelayCommand(() => importAgent?.Job.Close());
    public IAsyncRelayCommand<FacetFilterViewModel> RenameAuthorFilterCommand =>
        renameAuthorFilterCommand ??= new AsyncRelayCommand<FacetFilterViewModel>(filter => RenameFilterValueAsync(filter, MetadataFilterKind.Author));
    public IAsyncRelayCommand<FacetFilterViewModel> RemoveAuthorFilterCommand =>
        removeAuthorFilterCommand ??= new AsyncRelayCommand<FacetFilterViewModel>(filter => RemoveFilterValueAsync(filter, MetadataFilterKind.Author));
    public IAsyncRelayCommand<FacetFilterViewModel> RenameSeriesFilterCommand =>
        renameSeriesFilterCommand ??= new AsyncRelayCommand<FacetFilterViewModel>(filter => RenameFilterValueAsync(filter, MetadataFilterKind.Series));
    public IAsyncRelayCommand<FacetFilterViewModel> RemoveSeriesFilterCommand =>
        removeSeriesFilterCommand ??= new AsyncRelayCommand<FacetFilterViewModel>(filter => RemoveFilterValueAsync(filter, MetadataFilterKind.Series));
    public IAsyncRelayCommand<FacetFilterViewModel> RenameTagFilterCommand =>
        renameTagFilterCommand ??= new AsyncRelayCommand<FacetFilterViewModel>(filter => RenameFilterValueAsync(filter, MetadataFilterKind.Tag));
    public IAsyncRelayCommand<FacetFilterViewModel> RemoveTagFilterCommand =>
        removeTagFilterCommand ??= new AsyncRelayCommand<FacetFilterViewModel>(filter => RemoveFilterValueAsync(filter, MetadataFilterKind.Tag));
    public IAsyncRelayCommand<FacetFilterViewModel> RenameLanguageFilterCommand =>
        renameLanguageFilterCommand ??= new AsyncRelayCommand<FacetFilterViewModel>(filter => RenameFilterValueAsync(filter, MetadataFilterKind.Language));
    public IAsyncRelayCommand<FacetFilterViewModel> RemoveLanguageFilterCommand =>
        removeLanguageFilterCommand ??= new AsyncRelayCommand<FacetFilterViewModel>(filter => RemoveFilterValueAsync(filter, MetadataFilterKind.Language));

    private AsyncRelayCommand? refreshCommand;
    private AsyncRelayCommand? addBooksCommand;
    private AsyncRelayCommand? scanFolderCommand;
    private AsyncRelayCommand? createLibraryCommand;
    private AsyncRelayCommand? openLibraryCommand;
    private RelayCommand? cancelImportCommand;
    private AsyncRelayCommand? showImportDetailsCommand;
    private AsyncRelayCommand? showImportHistoryCommand;
    private RelayCommand? closeImportJobCommand;
    private AsyncRelayCommand<FacetFilterViewModel>? renameAuthorFilterCommand;
    private AsyncRelayCommand<FacetFilterViewModel>? removeAuthorFilterCommand;
    private AsyncRelayCommand<FacetFilterViewModel>? renameSeriesFilterCommand;
    private AsyncRelayCommand<FacetFilterViewModel>? removeSeriesFilterCommand;
    private AsyncRelayCommand<FacetFilterViewModel>? renameTagFilterCommand;
    private AsyncRelayCommand<FacetFilterViewModel>? removeTagFilterCommand;
    private AsyncRelayCommand<FacetFilterViewModel>? renameLanguageFilterCommand;
    private AsyncRelayCommand<FacetFilterViewModel>? removeLanguageFilterCommand;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoadingLibrary = true;
        ResetLoadingLibraryProgress();
        EmptyStateMessage = HasActiveLibrary
            ? "Loading library..."
            : "Create or open a library to get started.";
        try
        {
            await ApplyDefaultViewAsync(cancellationToken);
            if (currentLibrary is not null &&
                !EnsureActiveLibraryStillExists("Create or open a library to get started."))
            {
                return;
            }

            books = await LoadBooksAsync(cancellationToken);
            RefreshFacetFilters();
            ApplyFilter();
            RefreshLibraryDisplay();
        }
        finally
        {
            IsLoadingLibrary = false;
        }
    }

    partial void OnLoadingLibraryTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(LoadingLibraryProgressValue));
        OnPropertyChanged(nameof(IsLoadingLibraryProgressIndeterminate));
        OnPropertyChanged(nameof(LoadingLibraryProgressText));
    }

    partial void OnLoadedLibraryCountChanged(int value)
    {
        OnPropertyChanged(nameof(LoadingLibraryProgressValue));
        OnPropertyChanged(nameof(LoadingLibraryProgressText));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedSortOptionChanged(LibrarySortOption value) => ApplyFilter();

    async partial void OnSelectedBookChanged(BookRowViewModel? value)
    {
        var version = ++selectionVersion;
        if (value is null)
        {
            Details.Clear();
            return;
        }

        Details.Load(value.Book);
        var fullBook = await bookRepository.GetAsync(value.Id, CancellationToken.None);
        if (version == selectionVersion && fullBook is not null)
        {
            Details.Load(fullBook);
        }
    }

    private async Task AddBooksAsync(CancellationToken cancellationToken)
    {
        if (!EnsureActiveLibraryStillExists("Create or open a library before adding books."))
        {
            return;
        }

        var paths = await userInteraction.PickBookFilesAsync(cancellationToken);
        if (paths.Count == 0 || (importService is null && importAgent is null))
        {
            return;
        }

        await ImportFilesAsync(paths, cancellationToken);
    }

    public async Task ImportFilesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default,
        ImportRunContext? context = null)
    {
        if (!EnsureActiveLibraryStillExists("Create or open a library before adding books."))
        {
            return;
        }

        if (paths.Count == 0 || (importService is null && importAgent is null))
        {
            return;
        }

        if (importAgent is not null)
        {
            await importAgent.StartImportAsync(paths, OnImportProgressAsync, cancellationToken, context ?? ImportRunContext.FileImport);
            OnPropertyChanged(nameof(HasActiveImport));
            return;
        }

        var result = await importService!.ImportAsync(paths, progress: null, cancellationToken, context ?? ImportRunContext.FileImport);
        LastImportResult = new ImportResultViewModel(result);
        await userInteraction.ShowImportResultAsync(LastImportResult, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    private async Task ScanFolderAsync(CancellationToken cancellationToken)
    {
        if (!EnsureActiveLibraryStillExists("Create or open a library before scanning folders."))
        {
            return;
        }

        if (directoryScanner is null || (importService is null && importAgent is null))
        {
            return;
        }

        var folder = await userInteraction.PickScanFolderAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        importAgent?.StartScanning();
        var includeSubdirectories = settingsStore is null ||
            (await settingsStore.LoadAsync(cancellationToken)).IncludeScanSubdirectories;
        var files = await Task.Run(
            () => directoryScanner.Scan(folder, includeSubdirectories, cancellationToken),
            cancellationToken);
        await ImportFilesAsync(
            files,
            cancellationToken,
            new ImportRunContext(ImportRunKind.DirectoryScan, folder, includeSubdirectories));
    }

    private async Task<IReadOnlyList<Book>> LoadBooksAsync(CancellationToken cancellationToken)
    {
        var progress = new Progress<LibraryLoadProgress>(snapshot =>
        {
            LoadingLibraryTotalCount = snapshot.TotalCount;
            LoadedLibraryCount = snapshot.LoadedCount;
        });

        return await Task.Run(
            () => LoadBooksInBackgroundAsync(progress, cancellationToken),
            cancellationToken);
    }

    private async Task<IReadOnlyList<Book>> LoadBooksInBackgroundAsync(
        IProgress<LibraryLoadProgress> progress,
        CancellationToken cancellationToken)
    {
        if (HasActiveLibrary && bookRepository is IBookPagedRepository pagedRepository)
        {
            var totalCount = await pagedRepository.CountAsync(cancellationToken);
            progress.Report(new LibraryLoadProgress(totalCount, 0));
            if (totalCount == 0)
            {
                return [];
            }

            var loadedBooks = new List<Book>(totalCount);
            for (var skip = 0; skip < totalCount; skip += LibraryLoadPageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await pagedRepository.ListPageAsync(
                    skip,
                    LibraryLoadPageSize,
                    cancellationToken);
                if (page.Count == 0)
                {
                    break;
                }

                loadedBooks.AddRange(page);
                progress.Report(new LibraryLoadProgress(totalCount, loadedBooks.Count));
            }

            return loadedBooks.AsReadOnly();
        }

        var allBooks = await bookRepository.ListAsync(cancellationToken);
        progress.Report(new LibraryLoadProgress(allBooks.Count, allBooks.Count));
        return allBooks;
    }

    private void ResetLoadingLibraryProgress()
    {
        LoadingLibraryTotalCount = 0;
        LoadedLibraryCount = 0;
    }

    private sealed record LibraryLoadProgress(int TotalCount, int LoadedCount);

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
        var filteredBooks = ApplyFacetFilters(searchService.Filter(books, SearchText));
        var rows = ApplySort(
                filteredBooks.Select(book => new BookRowViewModel(book, SearchText, CurrentLibraryPath)),
                SelectedSortOption)
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

    private IReadOnlyList<Book> ApplyFacetFilters(IReadOnlyList<Book> source)
    {
        var selectedFilters = new[]
            {
                (Filters: AuthorFilters, ValueSelector: (Func<Book, IEnumerable<string>>)(book => book.Metadata.Authors)),
                (Filters: CategoryFilters, ValueSelector: (Func<Book, IEnumerable<string>>)(book => book.Metadata.Tags ?? [])),
                (Filters: SeriesFilters, ValueSelector: (Func<Book, IEnumerable<string>>)(book => SingleOptionalValue(book.Metadata.Series))),
                (Filters: StatusFilters, ValueSelector: (Func<Book, IEnumerable<string>>)(book => [book.ReadingStatus.ToString()])),
                (Filters: EReaderFilters, ValueSelector: (Func<Book, IEnumerable<string>>)(book => [new BookRowViewModel(book).EReader])),
                (Filters: LanguageFilters, ValueSelector: (Func<Book, IEnumerable<string>>)(book => SingleOptionalValue(LanguageFilterKey(book.Metadata.Language))))
            }
            .Select(group => (
                group.ValueSelector,
                Values: group.Filters
                    .Where(filter => filter.IsSelected)
                    .Select(filter => filter.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .Where(group => group.Values.Count > 0)
            .ToArray();

        if (selectedFilters.Length == 0)
        {
            return source;
        }

        return source
            .Where(book => selectedFilters.Any(group => group.ValueSelector(book).Any(group.Values.Contains)))
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
            books.SelectMany(book => SingleOptionalValue(LanguageFilterKey(book.Metadata.Language))),
            LanguageDisplayName);
    }

    private void RefreshFilters(
        ObservableCollection<FacetFilterViewModel> filters,
        IEnumerable<string> values,
        Func<string, string>? displayNameSelector = null)
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
            var isSelected = existingSelections.TryGetValue(value.Name, out var existingSelection) && existingSelection;
            filters.Add(new FacetFilterViewModel(
                value.Name,
                value.Count,
                isSelected,
                ApplyFilter,
                displayNameSelector?.Invoke(value.Name)));
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
            var isSelected = existingSelections.TryGetValue(name, out var existingSelection) && existingSelection;
            StatusFilters.Add(new FacetFilterViewModel(name, count, isSelected, ApplyFilter));
        }
    }

    private static IEnumerable<string> SingleOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];

    private static string LanguageDisplayName(string value)
    {
        var normalized = LanguageFilterKey(value) ?? value.Trim();
        if (normalized.Length == 0)
        {
            return normalized;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            var languageOnly = CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName);
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(languageOnly.DisplayName);
        }
        catch (CultureNotFoundException)
        {
            return value;
        }
    }

    private static string? LanguageFilterKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Equals("eng", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        try
        {
            return CultureInfo.GetCultureInfo(normalized).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return normalized;
        }
    }

    private async Task RenameFilterValueAsync(
        FacetFilterViewModel? filter,
        MetadataFilterKind kind)
    {
        if (filter is null)
        {
            return;
        }

        var newValue = await userInteraction.PromptTextAsync(
            "Rename metadata value",
            $"Rename '{filter.Name}' to:",
            filter.Name,
            CancellationToken.None);
        if (string.IsNullOrWhiteSpace(newValue) ||
            (kind != MetadataFilterKind.Language &&
                string.Equals(filter.Name, newValue.Trim(), StringComparison.Ordinal)))
        {
            return;
        }

        await ApplyMetadataValueEditAsync(
            kind,
            filter.Name,
            replacementValue: newValue.Trim(),
            remove: false,
            CancellationToken.None);
    }

    private async Task RemoveFilterValueAsync(
        FacetFilterViewModel? filter,
        MetadataFilterKind kind)
    {
        if (filter is null)
        {
            return;
        }

        if (!await userInteraction.ConfirmMetadataValueRemovalAsync(
                filter.Name,
                filter.Count,
                CancellationToken.None))
        {
            return;
        }

        await ApplyMetadataValueEditAsync(
            kind,
            filter.Name,
            replacementValue: null,
            remove: true,
            CancellationToken.None);
    }

    private async Task ApplyMetadataValueEditAsync(
        MetadataFilterKind kind,
        string oldValue,
        string? replacementValue,
        bool remove,
        CancellationToken cancellationToken)
    {
        var updatedBooks = new List<Book>(books.Count);
        var changedBooks = new List<Book>();
        foreach (var book in books)
        {
            var updated = TryEditMetadataValue(book, kind, oldValue, replacementValue, remove);
            updatedBooks.Add(updated);
            if (!ReferenceEquals(updated, book))
            {
                changedBooks.Add(updated);
            }
        }

        if (changedBooks.Count == 0)
        {
            return;
        }

        var persistedBooks = new List<Book>(changedBooks.Count);
        foreach (var changedBook in changedBooks)
        {
            try
            {
                if (bookService is not null)
                {
                    var result = await bookService.SaveAsync(changedBook, cancellationToken);
                    if (result.Status == BookSaveStatus.Succeeded)
                    {
                        persistedBooks.Add(changedBook);
                    }
                }
                else
                {
                    await bookRepository.UpdateAsync(changedBook, cancellationToken);
                    persistedBooks.Add(changedBook);
                }
            }
            catch (BookConflictException)
            {
                // Keep the original book unchanged when a bulk cleanup would create a duplicate.
            }
        }

        if (persistedBooks.Count == 0)
        {
            return;
        }

        var persistedById = persistedBooks.ToDictionary(book => book.Id);
        books = books
            .Select(book => persistedById.GetValueOrDefault(book.Id) ?? book)
            .ToList();
        if (SelectedBook is { } selected &&
            persistedById.GetValueOrDefault(selected.Id) is { } selectedChangedBook)
        {
            Details.Load(selectedChangedBook);
        }

        RefreshFacetFilters();
        ApplyFilter();
    }

    private static Book TryEditMetadataValue(
        Book book,
        MetadataFilterKind kind,
        string oldValue,
        string? replacementValue,
        bool remove)
    {
        var metadata = book.Metadata;
        return kind switch
        {
            MetadataFilterKind.Author => ReplaceListValue(
                    metadata.Authors,
                    oldValue,
                    replacementValue,
                    remove,
                    out var authors)
                ? book with { Metadata = CopyMetadata(metadata, authors, metadata.Tags, metadata.Series, metadata.Language) }
                : book,
            MetadataFilterKind.Tag => ReplaceListValue(
                    metadata.Tags ?? [],
                    oldValue,
                    replacementValue,
                    remove,
                    out var tags)
                ? book with { Metadata = CopyMetadata(metadata, metadata.Authors, tags.Count == 0 ? null : tags, metadata.Series, metadata.Language) }
                : book,
            MetadataFilterKind.Series => ReplaceScalarValue(
                    metadata.Series,
                    oldValue,
                    replacementValue,
                    remove,
                    out var series)
                ? book with { Metadata = CopyMetadata(metadata, metadata.Authors, metadata.Tags, series, metadata.Language) }
                : book,
            MetadataFilterKind.Language => ReplaceScalarValue(
                    metadata.Language,
                    oldValue,
                    replacementValue,
                    remove,
                    out var language,
                    LanguageFilterKey)
                ? book with { Metadata = CopyMetadata(metadata, metadata.Authors, metadata.Tags, metadata.Series, language) }
                : book,
            _ => book
        };
    }

    private static bool ReplaceListValue(
        IReadOnlyList<string> source,
        string oldValue,
        string? replacementValue,
        bool remove,
        out IReadOnlyList<string> updated)
    {
        var changed = false;
        var values = new List<string>();
        foreach (var value in source)
        {
            if (string.Equals(value, oldValue, StringComparison.OrdinalIgnoreCase))
            {
                changed = true;
                if (!remove && !string.IsNullOrWhiteSpace(replacementValue))
                {
                    values.Add(replacementValue.Trim());
                }
            }
            else
            {
                values.Add(value);
            }
        }

        updated = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return changed;
    }

    private static bool ReplaceScalarValue(
        string? source,
        string oldValue,
        string? replacementValue,
        bool remove,
        out string? updated,
        Func<string?, string?>? comparisonKeySelector = null)
    {
        updated = source;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var sourceKey = comparisonKeySelector?.Invoke(source) ?? source.Trim();
        var oldKey = comparisonKeySelector?.Invoke(oldValue) ?? oldValue.Trim();
        if (!string.Equals(sourceKey, oldKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        updated = remove ? null : replacementValue?.Trim();
        return true;
    }

    private static BookMetadata CopyMetadata(
        BookMetadata metadata,
        IReadOnlyList<string> authors,
        IReadOnlyList<string>? tags,
        string? series,
        string? language) =>
        new(
            metadata.Title,
            authors,
            metadata.Description,
            language,
            metadata.Publisher,
            metadata.PublicationDate,
            tags,
            series,
            metadata.SeriesNumber,
            metadata.Isbn,
            metadata.CoverBytes);

    private static IEnumerable<BookRowViewModel> ApplySort(
        IEnumerable<BookRowViewModel> rows,
        LibrarySortOption sortOption)
    {
        return sortOption switch
        {
            LibrarySortOption.Title => rows
                .OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.Authors, StringComparer.CurrentCultureIgnoreCase),
            LibrarySortOption.Author => rows
                .OrderBy(row => row.Authors, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase),
            LibrarySortOption.EReader => rows
                .OrderBy(row => row.EReader, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase),
            LibrarySortOption.Category => rows
                .OrderBy(row => row.Book.Metadata.Tags?.FirstOrDefault() ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => rows
        };
    }

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

    private bool EnsureActiveLibraryStillExists(string noActiveLibraryMessage)
    {
        if (!HasActiveLibrary)
        {
            EmptyStateMessage = noActiveLibraryMessage;
            return false;
        }

        if (CurrentLibraryPath is not { Length: > 0 } currentPath || Directory.Exists(currentPath))
        {
            return true;
        }

        currentLibrary?.Clear();
        books = [];
        VisibleBooks.Clear();
        AuthorFilters.Clear();
        CategoryFilters.Clear();
        SeriesFilters.Clear();
        StatusFilters.Clear();
        EReaderFilters.Clear();
        LanguageFilters.Clear();
        Details.Clear();
        RefreshLibraryDisplay();
        EmptyStateMessage = MissingActiveLibraryMessage;
        OnPropertyChanged(nameof(VisibleBookCount));
        OnPropertyChanged(nameof(HasActiveLibrary));
        return false;
    }

    private void OnDetailsBookSaved(object? sender, Book savedBook)
    {
        var mutableBooks = books.ToList();
        var index = mutableBooks.FindIndex(book => book.Id == savedBook.Id);
        if (index >= 0)
        {
            mutableBooks[index] = savedBook;
        }
        else
        {
            mutableBooks.Add(savedBook);
        }

        books = mutableBooks;
        RefreshFacetFilters();
        ApplyFilter();
    }

    private void OnDetailsBookDeleted(object? sender, Guid bookId)
    {
        books = books.Where(book => book.Id != bookId).ToList();
        RefreshFacetFilters();
        ApplyFilter();
    }

    private static Task OnImportProgressAsync(ImportProgress progress) => Task.CompletedTask;

    private async void OnImportAgentCompleted(object? sender, ImportBatchResult result)
    {
        LastImportResult = new ImportResultViewModel(result);
        OnPropertyChanged(nameof(HasActiveImport));
        await RefreshAsync(CancellationToken.None);
    }

    private async Task ShowImportDetailsAsync(CancellationToken cancellationToken)
    {
        var result = importAgent?.Job.LatestResult;
        if (result is null)
        {
            return;
        }

        LastImportResult = new ImportResultViewModel(result);
        await userInteraction.ShowImportResultAsync(LastImportResult, cancellationToken);
    }

    private async Task ShowImportHistoryAsync(CancellationToken cancellationToken)
    {
        if (!EnsureActiveLibraryStillExists("Create or open a library to get started.") || importRepository is null)
        {
            return;
        }

        var summaries = await importRepository.ListRecentAsync(50, cancellationToken);
        var history = new ImportHistoryViewModel(summaries);
        var selectedRunId = await userInteraction.PickImportRunAsync(history, cancellationToken);
        if (selectedRunId is null)
        {
            return;
        }

        var run = await importRepository.GetAsync(selectedRunId.Value, cancellationToken);
        if (run is null)
        {
            return;
        }

        LastImportResult = new ImportResultViewModel(run);
        await userInteraction.ShowImportResultAsync(LastImportResult, cancellationToken);
    }

    private enum MetadataFilterKind
    {
        Author,
        Series,
        Tag,
        Language
    }
}
