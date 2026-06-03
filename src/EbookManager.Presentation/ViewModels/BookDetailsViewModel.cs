using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EbookManager.Application.Books;
using EbookManager.Domain.Books;

namespace EbookManager.Presentation.ViewModels;

public sealed partial class BookDetailsViewModel(BookService bookService) : ObservableObject
{
    private readonly BookService bookService = bookService;
    private Book? originalBook;
    private bool isApplyingBook;

    [ObservableProperty]
    private Guid? bookId;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string authorsText = string.Empty;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private string? language;

    [ObservableProperty]
    private string? publisher;

    [ObservableProperty]
    private DateOnly? publicationDate;

    [ObservableProperty]
    private string? tagsText;

    [ObservableProperty]
    private string? series;

    [ObservableProperty]
    private decimal? seriesNumber;

    [ObservableProperty]
    private string? isbn;

    [ObservableProperty]
    private ReadingStatus readingStatus;

    [ObservableProperty]
    private byte[]? coverBytes;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    [ObservableProperty]
    private BookSaveResult? lastSaveResult;

    [ObservableProperty]
    private BookDeleteResult? lastDeleteResult;

    public IAsyncRelayCommand SaveCommand => saveCommand ??= new AsyncRelayCommand(SaveAsync, CanEdit);
    public IAsyncRelayCommand DeleteCommand => deleteCommand ??= new AsyncRelayCommand(DeleteAsync, CanEdit);
    public IRelayCommand UndoCommand => undoCommand ??= new RelayCommand(Undo, CanEdit);

    private AsyncRelayCommand? saveCommand;
    private AsyncRelayCommand? deleteCommand;
    private RelayCommand? undoCommand;

    public void Load(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        originalBook = book;
        Apply(book);
        LastSaveResult = null;
        LastDeleteResult = null;
        RefreshDirtyState();
        NotifyCommandState();
    }

    public void Clear()
    {
        originalBook = null;
        BookId = null;
        Title = string.Empty;
        AuthorsText = string.Empty;
        Description = null;
        Language = null;
        Publisher = null;
        PublicationDate = null;
        TagsText = null;
        Series = null;
        SeriesNumber = null;
        Isbn = null;
        ReadingStatus = ReadingStatus.Unread;
        CoverBytes = null;
        LastSaveResult = null;
        LastDeleteResult = null;
        RefreshDirtyState();
        NotifyCommandState();
    }

    public Book? ToBook()
    {
        if (originalBook is null)
        {
            return null;
        }

        return originalBook with
        {
            Metadata = new BookMetadata(
                Title.Trim(),
                SplitList(AuthorsText),
                NormalizeBlank(Description),
                NormalizeBlank(Language),
                NormalizeBlank(Publisher),
                PublicationDate,
                SplitNullableList(TagsText),
                NormalizeBlank(Series),
                SeriesNumber,
                NormalizeBlank(Isbn),
                CoverBytes),
            ReadingStatus = ReadingStatus,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var book = ToBook();
        if (book is null)
        {
            return;
        }

        LastSaveResult = await bookService.SaveAsync(book, cancellationToken);
        if (LastSaveResult.Status == BookSaveStatus.Succeeded)
        {
            originalBook = book;
            RefreshDirtyState();
        }
    }

    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (originalBook is null)
        {
            return;
        }

        LastDeleteResult = await bookService.DeleteAsync(originalBook.Id, cancellationToken);
        if (LastDeleteResult.Status == BookDeleteStatus.Deleted)
        {
            Clear();
        }
    }

    private void Undo()
    {
        if (originalBook is null)
        {
            return;
        }

        Apply(originalBook);
        LastSaveResult = null;
        RefreshDirtyState();
    }

    partial void OnTitleChanged(string value) => RefreshDirtyState();
    partial void OnAuthorsTextChanged(string value) => RefreshDirtyState();
    partial void OnDescriptionChanged(string? value) => RefreshDirtyState();
    partial void OnLanguageChanged(string? value) => RefreshDirtyState();
    partial void OnPublisherChanged(string? value) => RefreshDirtyState();
    partial void OnPublicationDateChanged(DateOnly? value) => RefreshDirtyState();
    partial void OnTagsTextChanged(string? value) => RefreshDirtyState();
    partial void OnSeriesChanged(string? value) => RefreshDirtyState();
    partial void OnSeriesNumberChanged(decimal? value) => RefreshDirtyState();
    partial void OnIsbnChanged(string? value) => RefreshDirtyState();
    partial void OnReadingStatusChanged(ReadingStatus value) => RefreshDirtyState();
    partial void OnCoverBytesChanged(byte[]? value) => RefreshDirtyState();

    private void Apply(Book book)
    {
        ApplyValues(() =>
        {
            BookId = book.Id;
            Title = book.Metadata.Title;
            AuthorsText = JoinList(book.Metadata.Authors);
            Description = book.Metadata.Description;
            Language = book.Metadata.Language;
            Publisher = book.Metadata.Publisher;
            PublicationDate = book.Metadata.PublicationDate;
            TagsText = book.Metadata.Tags is null ? null : JoinList(book.Metadata.Tags);
            Series = book.Metadata.Series;
            SeriesNumber = book.Metadata.SeriesNumber;
            Isbn = book.Metadata.Isbn;
            ReadingStatus = book.ReadingStatus;
            CoverBytes = book.Metadata.CoverBytes;
        });
    }

    private void RefreshDirtyState()
    {
        if (isApplyingBook)
        {
            return;
        }

        var editedBook = ToBook();
        HasUnsavedChanges = originalBook is not null &&
            editedBook is not null &&
            !BooksEquivalentForEditing(originalBook, editedBook);
    }

    private void ApplyValues(Action apply)
    {
        isApplyingBook = true;
        try
        {
            apply();
        }
        finally
        {
            isApplyingBook = false;
        }
    }

    private bool CanEdit() => originalBook is not null;

    private void NotifyCommandState()
    {
        saveCommand?.NotifyCanExecuteChanged();
        deleteCommand?.NotifyCanExecuteChanged();
        undoCommand?.NotifyCanExecuteChanged();
    }

    private static bool BooksEquivalentForEditing(Book first, Book second) =>
        NormalizeForEditing(first).Metadata == NormalizeForEditing(second).Metadata &&
        first.ReadingStatus == second.ReadingStatus;

    private static Book NormalizeForEditing(Book book) =>
        book with
        {
            Metadata = new BookMetadata(
                book.Metadata.Title.Trim(),
                SplitList(JoinList(book.Metadata.Authors)),
                NormalizeBlank(book.Metadata.Description),
                NormalizeBlank(book.Metadata.Language),
                NormalizeBlank(book.Metadata.Publisher),
                book.Metadata.PublicationDate,
                SplitNullableList(book.Metadata.Tags is null ? null : JoinList(book.Metadata.Tags)),
                NormalizeBlank(book.Metadata.Series),
                book.Metadata.SeriesNumber,
                NormalizeBlank(book.Metadata.Isbn),
                book.Metadata.CoverBytes)
        };

    private static string JoinList(IReadOnlyList<string> values) => string.Join("; ", values);

    private static IReadOnlyList<string> SplitList(string? value) =>
        (value ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyList<string>? SplitNullableList(string? value)
    {
        var values = SplitList(value);
        return values.Count == 0 ? null : values;
    }

    private static string? NormalizeBlank(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
