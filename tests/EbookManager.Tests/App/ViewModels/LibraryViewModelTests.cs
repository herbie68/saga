using EbookManager.Application.Books;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Domain.Libraries;
using EbookManager.Domain.Metadata;
using EbookManager.Libraries;
using EbookManager.Presentation.Abstractions;
using EbookManager.Presentation.ViewModels;
using EbookManager.Tests.TestSupport;
using FluentAssertions;

namespace EbookManager.Tests.App.ViewModels;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task Refresh_loads_books_and_search_filters_visible_books()
    {
        var first = CreateBook("The Hobbit", ["Tolkien"]);
        var second = CreateBook("Dune", ["Frank Herbert"]);
        var viewModel = CreateViewModel([second, first]);

        await viewModel.RefreshAsync();
        viewModel.SearchText = "tolkien";

        viewModel.VisibleBooks.Should().ContainSingle();
        viewModel.VisibleBooks[0].Title.Should().Be("The Hobbit");
        viewModel.VisibleBookCount.Should().Be(1);
    }

    [Fact]
    public async Task First_refresh_applies_default_view_from_settings()
    {
        var settingsStore = new InMemoryAppSettingsStore();
        await settingsStore.SaveAsync(
            settingsStore.Settings with { DefaultView = nameof(LibraryView.Bookshelf) },
            default);
        var viewModel = CreateViewModel([], settingsStore: settingsStore);

        await viewModel.RefreshAsync();

        viewModel.SelectedView.Should().Be(LibraryView.Bookshelf);
    }

    [Fact]
    public async Task SearchText_is_exposed_on_visible_rows_for_highlighting()
    {
        var book = CreateBook("The Hobbit", ["Tolkien"]);
        var viewModel = CreateViewModel([book]);

        await viewModel.RefreshAsync();
        viewModel.SearchText = "hob";

        viewModel.VisibleBooks.Should().ContainSingle()
            .Which.SearchText.Should().Be("hob");
    }

    [Fact]
    public async Task Author_filters_are_built_from_books_and_filter_visible_rows()
    {
        var hobbit = CreateBook("The Hobbit", ["Tolkien"]);
        var dune = CreateBook("Dune", ["Frank Herbert"]);
        var viewModel = CreateViewModel([hobbit, dune]);

        await viewModel.RefreshAsync();

        viewModel.AuthorFilters.Select(filter => filter.DisplayName)
            .Should().Equal("Frank Herbert (1)", "Tolkien (1)");

        viewModel.AuthorFilters.Single(filter => filter.Name == "Tolkien").IsSelected = false;

        viewModel.VisibleBooks.Should().ContainSingle()
            .Which.Title.Should().Be("Dune");
    }

    [Fact]
    public async Task Category_filters_are_built_from_tags_and_filter_visible_rows()
    {
        var fantasy = CreateBook("The Hobbit", ["Tolkien"], tags: ["Fantasy"]);
        var scienceFiction = CreateBook("Dune", ["Frank Herbert"], tags: ["Science fiction"]);
        var viewModel = CreateViewModel([fantasy, scienceFiction]);

        await viewModel.RefreshAsync();

        viewModel.CategoryFilters.Select(filter => filter.DisplayName)
            .Should().Equal("Fantasy (1)", "Science fiction (1)");

        viewModel.CategoryFilters.Single(filter => filter.Name == "Fantasy").IsSelected = false;

        viewModel.VisibleBooks.Should().ContainSingle()
            .Which.Title.Should().Be("Dune");
    }

    [Fact]
    public async Task Metadata_filters_are_built_from_series_status_ereader_and_language()
    {
        var fantasy = CreateBook(
            "The Hobbit",
            ["Tolkien"],
            language: "en",
            series: "Middle-earth",
            readingStatus: ReadingStatus.Read);
        var scienceFiction = CreateBook(
            "Dune",
            ["Frank Herbert"],
            language: "nl",
            series: "Dune",
            readingStatus: ReadingStatus.Unread);
        var viewModel = CreateViewModel([fantasy, scienceFiction]);

        await viewModel.RefreshAsync();

        viewModel.SeriesFilters.Select(filter => filter.DisplayName)
            .Should().Equal("Dune (1)", "Middle-earth (1)");
        viewModel.StatusFilters.Select(filter => filter.DisplayName)
            .Should().Equal("Unread (1)", "Read (1)");
        viewModel.EReaderFilters.Select(filter => filter.DisplayName)
            .Should().Equal("Unavailable (2)");
        viewModel.LanguageFilters.Select(filter => filter.DisplayName)
            .Should().Equal("en (1)", "nl (1)");

        viewModel.SeriesFilters.Single(filter => filter.Name == "Middle-earth").IsSelected = false;
        viewModel.VisibleBooks.Should().ContainSingle()
            .Which.Title.Should().Be("Dune");
    }

    [Fact]
    public async Task Sort_option_orders_visible_rows_by_metadata()
    {
        var dune = CreateBook("Dune", ["Frank Herbert"], tags: ["Science fiction"]);
        var hobbit = CreateBook("The Hobbit", ["Tolkien"], tags: ["Fantasy"]);
        var alpha = CreateBook("Alpha", ["Zed"], tags: ["Mystery"]);
        var viewModel = CreateViewModel([dune, hobbit, alpha]);

        await viewModel.RefreshAsync();

        viewModel.SelectedSortOption = LibrarySortOption.Title;
        viewModel.VisibleBooks.Select(book => book.Title)
            .Should().Equal("Alpha", "Dune", "The Hobbit");

        viewModel.SelectedSortOption = LibrarySortOption.Author;
        viewModel.VisibleBooks.Select(book => book.Title)
            .Should().Equal("Dune", "The Hobbit", "Alpha");

        viewModel.SelectedSortOption = LibrarySortOption.Category;
        viewModel.VisibleBooks.Select(book => book.Title)
            .Should().Equal("The Hobbit", "Alpha", "Dune");
    }

    [Fact]
    public async Task Selecting_a_book_loads_details()
    {
        var book = CreateBook("Selected", ["Author"]);
        var viewModel = CreateViewModel([book]);

        await viewModel.RefreshAsync();
        viewModel.SelectedBook = viewModel.VisibleBooks.Single();

        viewModel.Details.BookId.Should().Be(book.Id);
        viewModel.Details.Title.Should().Be("Selected");
    }

    [Theory]
    [InlineData(LibraryView.Bookshelf)]
    [InlineData(LibraryView.Detailed)]
    [InlineData(LibraryView.List)]
    public void SelectedView_switches_between_supported_views(LibraryView selectedView)
    {
        var viewModel = CreateViewModel([]);

        viewModel.SelectedView = selectedView;

        viewModel.SelectedView.Should().Be(selectedView);
    }

    [Fact]
    public async Task CreateLibraryCommand_creates_default_elibrary_sets_current_library_and_refreshes()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var selectedParent = temporaryDirectory.CreateSubdirectory("Selected").FullName;
        var settingsStore = new InMemoryAppSettingsStore();
        var currentLibrary = new CurrentLibrary();
        var initializer = new RecordingLibraryDatabaseInitializer();
        var interaction = new ScriptedUserInteractionService { LibraryDirectory = selectedParent };
        var viewModel = CreateViewModel(
            [],
            interaction,
            new LibraryService(settingsStore),
            currentLibrary,
            initializer);

        await viewModel.CreateLibraryCommand.ExecuteAsync(null);

        var expectedLibraryPath = Path.Combine(selectedParent, "ELibrary");
        currentLibrary.Current.Should().NotBeNull();
        currentLibrary.Current!.DirectoryPath.Should().Be(Path.GetFullPath(expectedLibraryPath));
        Directory.Exists(Path.Combine(expectedLibraryPath, "books")).Should().BeTrue();
        initializer.InitializedLibraries.Should().ContainSingle()
            .Which.DirectoryPath.Should().Be(Path.GetFullPath(expectedLibraryPath));
        viewModel.CurrentLibraryName.Should().Be("ELibrary");
        viewModel.HasActiveLibrary.Should().BeTrue();
    }

    [Fact]
    public async Task OpenLibraryCommand_opens_existing_library_sets_current_library_and_refreshes()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var libraryPath = temporaryDirectory.CreateSubdirectory("MyLibrary").FullName;
        var settingsStore = new InMemoryAppSettingsStore();
        var currentLibrary = new CurrentLibrary();
        var initializer = new RecordingLibraryDatabaseInitializer();
        var interaction = new ScriptedUserInteractionService { LibraryDirectory = libraryPath };
        var viewModel = CreateViewModel(
            [],
            interaction,
            new LibraryService(settingsStore),
            currentLibrary,
            initializer);

        await viewModel.OpenLibraryCommand.ExecuteAsync(null);

        currentLibrary.Current.Should().NotBeNull();
        currentLibrary.Current!.Name.Should().Be("MyLibrary");
        currentLibrary.Current.DirectoryPath.Should().Be(Path.GetFullPath(libraryPath));
        initializer.InitializedLibraries.Should().ContainSingle()
            .Which.DirectoryPath.Should().Be(Path.GetFullPath(libraryPath));
        viewModel.CurrentLibraryName.Should().Be("MyLibrary");
    }

    [Fact]
    public async Task AddBooksCommand_without_active_library_updates_empty_state_without_prompting_for_files()
    {
        var interaction = new ScriptedUserInteractionService();
        var viewModel = CreateViewModel([], interaction);

        await viewModel.AddBooksCommand.ExecuteAsync(null);

        interaction.PickBookFilesCalls.Should().Be(0);
        viewModel.EmptyStateMessage.Should().Be("Create or open a library before adding books.");
    }

    [Fact]
    public async Task ScanFolderCommand_without_active_library_updates_empty_state_without_prompting_for_folder()
    {
        var interaction = new ScriptedUserInteractionService();
        var viewModel = CreateViewModel([], interaction);

        await viewModel.ScanFolderCommand.ExecuteAsync(null);

        interaction.PickScanFolderCalls.Should().Be(0);
        viewModel.EmptyStateMessage.Should().Be("Create or open a library before scanning folders.");
    }

    private static LibraryViewModel CreateViewModel(
        IReadOnlyList<Book> books,
        IUserInteractionService? userInteraction = null,
        LibraryService? libraryService = null,
        CurrentLibrary? currentLibrary = null,
        ILibraryDatabaseInitializer? databaseInitializer = null,
        IAppSettingsStore? settingsStore = null)
    {
        var repository = new StaticBookRepository(books);
        var details = new BookDetailsViewModel(new BookService(
            repository,
            new NoopLibraryFileStore(),
            new NoopMetadataAdapterResolver()));
        return new LibraryViewModel(
            repository,
            new BookSearchService(),
            details,
            userInteraction ?? new ScriptedUserInteractionService(),
            libraryService: libraryService,
            currentLibrary: currentLibrary,
            databaseInitializer: databaseInitializer,
            settingsStore: settingsStore);
    }

    private static Book CreateBook(
        string title,
        IReadOnlyList<string> authors,
        IReadOnlyList<string>? tags = null,
        string? language = null,
        string? series = null,
        ReadingStatus readingStatus = ReadingStatus.Unread)
    {
        var now = DateTimeOffset.UtcNow;
        return new Book(
            Guid.NewGuid(),
            new BookMetadata(title, authors, Language: language, Tags: tags, Series: series),
            readingStatus,
            null,
            now,
            now);
    }

    private sealed class StaticBookRepository(IReadOnlyList<Book> books) : IBookRepository
    {
        public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(books);
        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(books.SingleOrDefault(book => book.Id == id));
        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> HasNormalizedTitleAndAuthorAsync(string title, IReadOnlyList<string> authors, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(Book book, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BookFile>>([]);
        public Task UpdateFileWriteBackAsync(Guid fileId, MetadataWriteResult result, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopLibraryFileStore : ILibraryFileStore
    {
        public Task<(string RelativeBookPath, string? RelativeCoverPath)> CopyIntoLibraryAsync(
            Guid bookId,
            string sourcePath,
            byte[]? coverBytes,
            CancellationToken cancellationToken) =>
            Task.FromResult(($"books/{bookId:N}/book.epub", (string?)null));

        public Task DeleteBookDirectoryAsync(Guid bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public string GetAbsolutePath(string relativePath) => relativePath;
    }

    private sealed class NoopMetadataAdapterResolver : IMetadataAdapterResolver
    {
        public IMetadataAdapter Resolve(EbookFormat format) => new NoopMetadataAdapter();
    }

    private sealed class NoopMetadataAdapter : IMetadataAdapter
    {
        public bool CanHandle(EbookFormat format) => true;

        public Task<MetadataReadResult> ReadAsync(string path, EbookFormat format, CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataReadResult(new BookMetadata("Title", ["Author"])));

        public Task<MetadataWriteResult> WriteAsync(
            string path,
            EbookFormat format,
            BookMetadata metadata,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataWriteResult(MetadataWriteBackStatus.Unsupported));
    }

    private sealed class ScriptedUserInteractionService : IUserInteractionService
    {
        public string? LibraryDirectory { get; init; }
        public int PickBookFilesCalls { get; private set; }
        public int PickScanFolderCalls { get; private set; }

        public Task<IReadOnlyList<string>> PickBookFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(RecordPickBookFiles());

        public Task<string?> PickScanFolderAsync(CancellationToken cancellationToken)
        {
            PickScanFolderCalls++;
            return Task.FromResult<string?>(null);
        }
        public Task<string?> PickLibraryDirectoryAsync(string title, CancellationToken cancellationToken) =>
            Task.FromResult(LibraryDirectory);

        public Task<bool> ConfirmDeleteAsync(string title, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task ShowImportResultAsync(ImportResultViewModel result, CancellationToken cancellationToken) => Task.CompletedTask;

        private IReadOnlyList<string> RecordPickBookFiles()
        {
            PickBookFilesCalls++;
            return [];
        }
    }

    private sealed class RecordingLibraryDatabaseInitializer : ILibraryDatabaseInitializer
    {
        public List<LibraryDescriptor> InitializedLibraries { get; } = [];

        public Task InitializeAsync(LibraryDescriptor library, CancellationToken cancellationToken)
        {
            InitializedLibraries.Add(library);
            return Task.CompletedTask;
        }
    }
}
