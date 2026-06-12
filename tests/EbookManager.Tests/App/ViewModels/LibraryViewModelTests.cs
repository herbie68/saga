using EbookManager.Application.Books;
using EbookManager.Application.Importing;
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
    public async Task Refresh_sets_loading_state_while_library_is_loading()
    {
        var repository = new BlockingBookRepository();
        var viewModel = CreateViewModel(
            [],
            repository: repository,
            currentLibrary: CreateActiveLibrary());

        var refresh = viewModel.RefreshAsync();
        await repository.ListStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.IsLoadingLibrary.Should().BeTrue();
        viewModel.EmptyStateMessage.Should().Be("Loading library...");

        repository.Release([]);
        await refresh;

        viewModel.IsLoadingLibrary.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_reports_paged_loading_progress_while_library_is_loading()
    {
        var books = Enumerable.Range(1, 1_200)
            .Select(index => CreateBook($"Book {index:0000}", ["Author"]))
            .ToList();
        var repository = new BlockingPagedBookRepository(books);
        var viewModel = CreateViewModel(
            [],
            repository: repository,
            currentLibrary: CreateActiveLibrary());

        var refresh = viewModel.RefreshAsync();
        await repository.FirstPageLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.IsLoadingLibrary.Should().BeTrue();
        viewModel.LoadingLibraryTotalCount.Should().Be(1_200);
        viewModel.LoadedLibraryCount.Should().Be(500);
        viewModel.LoadingLibraryProgressValue.Should().BeApproximately(41.67, 0.01);
        viewModel.LoadingLibraryProgressText.Should().Be("500 / 1200");

        repository.ReleaseRemainingPages();
        await refresh;

        viewModel.IsLoadingLibrary.Should().BeFalse();
        viewModel.VisibleBookCount.Should().Be(1_200);
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

        viewModel.AuthorFilters.Should().OnlyContain(filter => !filter.IsSelected);
        viewModel.AuthorFilters.Single(filter => filter.Name == "Frank Herbert").IsSelected = true;

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

        viewModel.CategoryFilters.Should().OnlyContain(filter => !filter.IsSelected);
        viewModel.CategoryFilters.Single(filter => filter.Name == "Science fiction").IsSelected = true;

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

        viewModel.SeriesFilters.Should().OnlyContain(filter => !filter.IsSelected);
        viewModel.SeriesFilters.Single(filter => filter.Name == "Dune").IsSelected = true;
        viewModel.VisibleBooks.Should().ContainSingle()
            .Which.Title.Should().Be("Dune");
    }

    [Fact]
    public async Task Selected_filters_expand_results_across_facets()
    {
        var art = CreateBook("Art Book", ["Art Jefferson"]);
        var kim = CreateBook("Kim Book", ["Kim Maurits"]);
        var arendsoog = CreateBook("Arendsoog", ["N. Nowee"], series: "Arendsoog");
        var unrelated = CreateBook("Other", ["Other Author"], series: "Other Series");
        var viewModel = CreateViewModel([art, kim, arendsoog, unrelated]);

        await viewModel.RefreshAsync();

        viewModel.VisibleBooks.Select(book => book.Title)
            .Should().BeEquivalentTo("Art Book", "Kim Book", "Arendsoog", "Other");

        viewModel.AuthorFilters.Single(filter => filter.Name == "Art Jefferson").IsSelected = true;
        viewModel.VisibleBooks.Select(book => book.Title)
            .Should().Equal("Art Book");

        viewModel.AuthorFilters.Single(filter => filter.Name == "Kim Maurits").IsSelected = true;
        viewModel.VisibleBooks.Select(book => book.Title)
            .Should().BeEquivalentTo("Art Book", "Kim Book");

        viewModel.SeriesFilters.Single(filter => filter.Name == "Arendsoog").IsSelected = true;
        viewModel.VisibleBooks.Select(book => book.Title)
            .Should().BeEquivalentTo("Art Book", "Kim Book", "Arendsoog");
    }

    [Fact]
    public async Task Saved_details_refresh_visible_rows_and_filters()
    {
        var book = CreateBook("Original", ["Author"]);
        var repository = new StaticBookRepository([book]);
        var details = new BookDetailsViewModel(new BookService(
            repository,
            new NoopLibraryFileStore(),
            new NoopMetadataAdapterResolver()));
        var viewModel = CreateViewModel([book], repository: repository, details: details);

        await viewModel.RefreshAsync();
        viewModel.SelectedBook = viewModel.VisibleBooks.Single();
        viewModel.Details.Title = "Updated";
        viewModel.Details.Series = "New Series";

        await viewModel.Details.SaveCommand.ExecuteAsync(null);

        viewModel.VisibleBooks.Should().ContainSingle()
            .Which.Title.Should().Be("Updated");
        viewModel.SeriesFilters.Should().ContainSingle(filter => filter.Name == "New Series");
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

    [Fact]
    public async Task ScanFolderCommand_starts_import_with_directory_scan_context()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.DirectoryPath, "book.epub");
        File.WriteAllText(source, "book");
        var interaction = new ScriptedUserInteractionService { ScanFolder = directory.DirectoryPath };
        var agent = new ScriptedImportAgent();
        var viewModel = CreateViewModel(
            [],
            interaction,
            currentLibrary: CreateActiveLibrary(),
            importAgent: agent,
            directoryScanner: new DirectoryScanner(),
            settingsStore: new InMemoryAppSettingsStore());

        await viewModel.ScanFolderCommand.ExecuteAsync(null);

        agent.StartScanningCalled.Should().BeTrue();
        agent.StartedSourcePaths.Should().Equal(source);
        agent.ImportContext.Should().Be(new ImportRunContext(
            ImportRunKind.DirectoryScan,
            directory.DirectoryPath,
            IncludeSubdirectories: true));
    }

    [Fact]
    public async Task ImportFilesAsync_starts_background_import_without_refreshing_during_progress()
    {
        var initial = CreateBook("Existing", ["Author"]);
        var imported = CreateBook("Imported", ["Author"]);
        var repository = new RefreshingBookRepository([initial], [initial, imported]);
        var agent = new ScriptedImportAgent();
        var viewModel = CreateViewModel(
            [initial],
            repository: repository,
            currentLibrary: CreateActiveLibrary(),
            importAgent: agent);

        await viewModel.RefreshAsync();
        await viewModel.ImportFilesAsync(["book.epub"]);
        agent.IsActive.Should().BeTrue();
        await agent.ReportProgressAsync(25);

        repository.ListCalls.Should().Be(1);
        viewModel.VisibleBooks.Select(book => book.Title).Should().NotContain("Imported");
    }

    [Fact]
    public async Task Import_completion_updates_last_result_and_refreshes_library()
    {
        var initial = CreateBook("Existing", ["Author"]);
        var imported = CreateBook("Imported", ["Author"]);
        var repository = new RefreshingBookRepository([initial], [initial, imported]);
        var agent = new ScriptedImportAgent();
        var viewModel = CreateViewModel(
            [initial],
            repository: repository,
            currentLibrary: CreateActiveLibrary(),
            importAgent: agent);
        var result = new ImportBatchResult(Guid.NewGuid(), [new ImportItemResult("book.epub", ImportOutcome.Added, "added")]);

        await viewModel.RefreshAsync();
        await viewModel.ImportFilesAsync(["book.epub"]);
        await agent.CompleteAsync(result);

        viewModel.LastImportResult.Should().NotBeNull();
        viewModel.LastImportResult!.TotalCount.Should().Be(1);
        viewModel.VisibleBooks.Select(book => book.Title).Should().Contain("Imported");
    }

    [Fact]
    public async Task Import_history_command_opens_selected_run_details()
    {
        var runId = Guid.NewGuid();
        var interaction = new ScriptedUserInteractionService { SelectedImportRunId = runId };
        var importRepository = new StaticImportRepository(
            [
                new ImportRunSummary(
                    runId,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    TotalCount: 2,
                    AddedCount: 1,
                    ExactDuplicateCount: 1,
                    PossibleDuplicateCount: 0,
                    FailedCount: 0)
            ],
            new ImportRunResult(
                runId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [
                    new ImportItemResult("added.epub", ImportOutcome.Added, "added"),
                    new ImportItemResult("duplicate.epub", ImportOutcome.ExactDuplicate, "duplicate")
                ]));
        var viewModel = CreateViewModel(
            [],
            interaction,
            currentLibrary: CreateActiveLibrary(),
            importRepository: importRepository);

        await viewModel.ShowImportHistoryCommand.ExecuteAsync(null);

        interaction.ImportHistory.Should().NotBeNull();
        interaction.ShownImportResult.Should().NotBeNull();
        interaction.ShownImportResult!.RunId.Should().Be(runId);
        interaction.ShownImportResult.TotalCount.Should().Be(2);
        viewModel.LastImportResult.Should().BeSameAs(interaction.ShownImportResult);
    }

    private static LibraryViewModel CreateViewModel(
        IReadOnlyList<Book> books,
        IUserInteractionService? userInteraction = null,
        LibraryService? libraryService = null,
        CurrentLibrary? currentLibrary = null,
        ILibraryDatabaseInitializer? databaseInitializer = null,
        IAppSettingsStore? settingsStore = null,
        IBookRepository? repository = null,
        BookDetailsViewModel? details = null,
        IImportAgent? importAgent = null,
        IImportRepository? importRepository = null,
        DirectoryScanner? directoryScanner = null)
    {
        repository ??= new StaticBookRepository(books);
        details ??= new BookDetailsViewModel(new BookService(
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
            directoryScanner: directoryScanner,
            settingsStore: settingsStore,
            importAgent: importAgent,
            importRepository: importRepository);
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

    private static CurrentLibrary CreateActiveLibrary()
    {
        var currentLibrary = new CurrentLibrary();
        currentLibrary.Set(new LibraryDescriptor("Test", Path.GetTempPath(), DateTimeOffset.UtcNow));
        return currentLibrary;
    }

    private class StaticBookRepository(IReadOnlyList<Book> books) : IBookRepository
    {
        protected readonly List<Book> Books = [.. books];

        public virtual Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Book>>([.. Books]);
        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Books.SingleOrDefault(book => book.Id == id));
        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> HasNormalizedTitleAndAuthorAsync(string title, IReadOnlyList<string> authors, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(Book book, CancellationToken cancellationToken)
        {
            var index = Books.FindIndex(existing => existing.Id == book.Id);
            if (index >= 0)
            {
                Books[index] = book;
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Books.RemoveAll(book => book.Id == id);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<BookFile>> ListFilesAsync(Guid bookId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BookFile>>([]);
        public Task UpdateFileWriteBackAsync(Guid fileId, MetadataWriteResult result, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RefreshingBookRepository : StaticBookRepository
    {
        private readonly IReadOnlyList<Book> firstRefreshBooks;
        private readonly IReadOnlyList<Book> laterRefreshBooks;
        public int ListCalls { get; private set; }

        public RefreshingBookRepository(
            IReadOnlyList<Book> firstRefreshBooks,
            IReadOnlyList<Book> laterRefreshBooks) : base(firstRefreshBooks)
        {
            this.firstRefreshBooks = firstRefreshBooks;
            this.laterRefreshBooks = laterRefreshBooks;
        }

        public override Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<Book>>(ListCalls == 1 ? firstRefreshBooks : laterRefreshBooks);
        }
    }

    private sealed class BlockingBookRepository : StaticBookRepository
    {
        private readonly TaskCompletionSource<IReadOnlyList<Book>> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingBookRepository() : base([])
        {
        }

        public TaskCompletionSource ListStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken)
        {
            ListStarted.TrySetResult();
            return release.Task;
        }

        public void Release(IReadOnlyList<Book> books) => release.TrySetResult(books);
    }

    private sealed class BlockingPagedBookRepository(IReadOnlyList<Book> books)
        : StaticBookRepository(books), IBookPagedRepository
    {
        private readonly TaskCompletionSource releaseRemainingPages =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int pageCalls;

        public TaskCompletionSource FirstPageLoaded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Books.Count);

        public async Task<IReadOnlyList<Book>> ListPageAsync(
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            var page = Books
                .OrderBy(book => book.Metadata.Title)
                .ThenBy(book => book.Id)
                .Skip(skip)
                .Take(take)
                .ToList();
            if (Interlocked.Increment(ref pageCalls) == 1)
            {
                FirstPageLoaded.TrySetResult();
                return page;
            }

            await releaseRemainingPages.Task.WaitAsync(cancellationToken);
            return page;
        }

        public void ReleaseRemainingPages() => releaseRemainingPages.TrySetResult();
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
        public string? ScanFolder { get; init; }
        public Guid? SelectedImportRunId { get; init; }
        public int PickBookFilesCalls { get; private set; }
        public int PickScanFolderCalls { get; private set; }
        public ImportHistoryViewModel? ImportHistory { get; private set; }
        public ImportResultViewModel? ShownImportResult { get; private set; }

        public Task<IReadOnlyList<string>> PickBookFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(RecordPickBookFiles());

        public Task<string?> PickScanFolderAsync(CancellationToken cancellationToken)
        {
            PickScanFolderCalls++;
            return Task.FromResult(ScanFolder);
        }
        public Task<string?> PickLibraryDirectoryAsync(string title, CancellationToken cancellationToken) =>
            Task.FromResult(LibraryDirectory);

        public Task<bool> ConfirmDeleteAsync(string title, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task ShowImportResultAsync(ImportResultViewModel result, CancellationToken cancellationToken)
        {
            ShownImportResult = result;
            return Task.CompletedTask;
        }

        public Task<Guid?> PickImportRunAsync(ImportHistoryViewModel history, CancellationToken cancellationToken)
        {
            ImportHistory = history;
            return Task.FromResult(SelectedImportRunId);
        }

        private IReadOnlyList<string> RecordPickBookFiles()
        {
            PickBookFilesCalls++;
            return [];
        }
    }

    private sealed class StaticImportRepository(
        IReadOnlyList<ImportRunSummary> summaries,
        ImportRunResult? run = null) : IImportRepository
    {
        public Task<Guid> StartRunAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task<Guid> StartRunAsync(
            DateTimeOffset startedUtc,
            ImportRunContext? context,
            CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task RecordItemAsync(
            Guid runId,
            int sequence,
            string sourceDisplayName,
            ImportOutcome outcome,
            string message,
            Guid? bookId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CompleteRunAsync(Guid runId, DateTimeOffset completedUtc, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ImportRunResult?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult(run?.Id == runId ? run : null);

        public Task<IReadOnlyList<ImportRunSummary>> ListRecentAsync(int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ImportRunSummary>>(summaries.Take(maxCount).ToList());
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

    private sealed class ScriptedImportAgent : IImportAgent
    {
        private Func<ImportProgress, Task>? onProgress;

        public event EventHandler<ImportBatchResult>? Completed;

        public ImportJobViewModel Job { get; } = new();

        public bool IsActive { get; private set; }

        public bool StartScanningCalled { get; private set; }
        public IReadOnlyList<string> StartedSourcePaths { get; private set; } = [];
        public ImportRunContext? ImportContext { get; private set; }

        public void StartScanning()
        {
            StartScanningCalled = true;
            Job.StartScanning();
        }

        public Task StartImportAsync(
            IReadOnlyList<string> sourcePaths,
            Func<ImportProgress, Task> onProgress,
            CancellationToken cancellationToken,
            ImportRunContext? context = null)
        {
            IsActive = true;
            StartedSourcePaths = sourcePaths;
            ImportContext = context;
            this.onProgress = onProgress;
            Job.StartImport(Guid.NewGuid(), sourcePaths.Count);
            return Task.CompletedTask;
        }

        public void CancelActiveJob() => IsActive = false;

        public async Task ReportProgressAsync(int processedCount)
        {
            var progress = new ImportProgress(
                Guid.NewGuid(),
                Math.Max(processedCount, 1),
                processedCount,
                processedCount,
                0,
                0,
                0,
                new ImportItemResult("book.epub", ImportOutcome.Added, "added"));
            Job.ApplyProgress(progress);
            if (onProgress is not null)
            {
                await onProgress(progress);
            }
        }

        public async Task CompleteAsync(ImportBatchResult result)
        {
            IsActive = false;
            Job.Complete(result);
            Completed?.Invoke(this, result);
            await Task.CompletedTask;
        }
    }
}
