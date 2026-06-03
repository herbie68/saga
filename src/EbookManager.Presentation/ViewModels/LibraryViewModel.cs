using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EbookManager.Application.Books;
using EbookManager.Application.Importing;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Presentation.Abstractions;

namespace EbookManager.Presentation.ViewModels;

public sealed partial class LibraryViewModel(
    IBookRepository bookRepository,
    BookSearchService searchService,
    BookDetailsViewModel details,
    IUserInteractionService userInteraction,
    ImportService? importService = null)
    : ObservableObject
{
    private readonly IBookRepository bookRepository = bookRepository;
    private readonly BookSearchService searchService = searchService;
    private readonly IUserInteractionService userInteraction = userInteraction;
    private readonly ImportService? importService = importService;
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

    public int VisibleBookCount => VisibleBooks.Count;

    public IAsyncRelayCommand RefreshCommand => refreshCommand ??= new AsyncRelayCommand(RefreshAsync);
    public IAsyncRelayCommand AddBooksCommand => addBooksCommand ??= new AsyncRelayCommand(AddBooksAsync);
    public IAsyncRelayCommand ScanFolderCommand => scanFolderCommand ??= new AsyncRelayCommand(ScanFolderAsync);
    public IAsyncRelayCommand CreateLibraryCommand => createLibraryCommand ??= new AsyncRelayCommand(() => Task.CompletedTask);
    public IAsyncRelayCommand OpenLibraryCommand => openLibraryCommand ??= new AsyncRelayCommand(() => Task.CompletedTask);

    private AsyncRelayCommand? refreshCommand;
    private AsyncRelayCommand? addBooksCommand;
    private AsyncRelayCommand? scanFolderCommand;
    private AsyncRelayCommand? createLibraryCommand;
    private AsyncRelayCommand? openLibraryCommand;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        books = await bookRepository.ListAsync(cancellationToken);
        ApplyFilter();
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
        var paths = await userInteraction.PickBookFilesAsync(cancellationToken);
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
        await userInteraction.PickScanFolderAsync(cancellationToken);
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
    }
}
