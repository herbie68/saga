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
        await WaitUntilAsync(() => viewModel.LoadedLibraryCount == 500);

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
            language: "eng",
            series: "Middle-earth",
            readingStatus: ReadingStatus.Read);
        var scienceFiction = CreateBook(
            "Dune",
            ["Frank Herbert"],
            language: "nl-NL",
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
            .Should().Equal("Engels (1)", "Nederlands (1)");

        viewModel.SeriesFilters.Should().OnlyContain(filter => !filter.IsSelected);
        viewModel.SeriesFilters.Single(filter => filter.Name == "Dune").IsSelected = true;
        viewModel.VisibleBooks.Should().ContainSingle()
            .Which.Title.Should().Be("Dune");
    }

    [Fact]
    public async Task Latvian_language_filter_can_be_selected_without_crashing()
    {
        var latvian = CreateBook("Latvian Book", ["Author"], language: "lv");
        var dutch = CreateBook("Dutch Book", ["Author"], language: "nl");
        var viewModel = CreateViewModel([latvian, dutch]);

        await viewModel.RefreshAsync();

        var filter = viewModel.LanguageFilters.Single(filter => filter.Name == "lv");
        filter.IsSelected = true;

        viewModel.VisibleBooks.Should().ContainSingle()
            .Which.Title.Should().Be("Latvian Book");
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
    public async Task Rename_author_filter_updates_all_matching_books_and_refreshes_filters()
    {
        var first = CreateBook("First", ["Ake Edwardson"]);
        var second = CreateBook("Second", ["Ake Edwardson", "Other"]);
        var repository = new StaticBookRepository([first, second]);
        var interaction = new ScriptedUserInteractionService { PromptTextResult = "Åke Edwardson" };
        var viewModel = CreateViewModel([first, second], interaction, repository: repository);

        await viewModel.RefreshAsync();
        await viewModel.RenameAuthorFilterCommand.ExecuteAsync(
            viewModel.AuthorFilters.Single(filter => filter.Name == "Ake Edwardson"));

        repository.BooksSnapshot.SelectMany(book => book.Metadata.Authors)
            .Should().Contain("Åke Edwardson")
            .And.NotContain("Ake Edwardson");
        viewModel.AuthorFilters.Should().ContainSingle(filter => filter.Name == "Åke Edwardson" && filter.Count == 2);
        viewModel.VisibleBooks.Should().HaveCount(2);
    }

    [Fact]
    public async Task Remove_tag_filter_removes_value_from_all_matching_books_and_refreshes_filters()
    {
        var first = CreateBook("First", ["Author"], tags: ["Keep", "RemoveMe"]);
        var second = CreateBook("Second", ["Author"], tags: ["RemoveMe"]);
        var repository = new StaticBookRepository([first, second]);
        var interaction = new ScriptedUserInteractionService { ConfirmMetadataValueRemovalResult = true };
        var viewModel = CreateViewModel([first, second], interaction, repository: repository);

        await viewModel.RefreshAsync();
        await viewModel.RemoveTagFilterCommand.ExecuteAsync(
            viewModel.CategoryFilters.Single(filter => filter.Name == "RemoveMe"));

        repository.BooksSnapshot.SelectMany(book => book.Metadata.Tags ?? [])
            .Should().NotContain("RemoveMe");
        repository.BooksSnapshot.Single(book => book.Metadata.Title == "First").Metadata.Tags
            .Should().Equal("Keep");
        repository.BooksSnapshot.Single(book => book.Metadata.Title == "Second").Metadata.Tags
            .Should().BeNull();
        viewModel.CategoryFilters.Should().NotContain(filter => filter.Name == "RemoveMe");
    }

    [Fact]
    public async Task Rename_series_filter_updates_matching_books()
    {
        var first = CreateBook("First", ["Author"], series: "Old Series");
        var second = CreateBook("Second", ["Author"], series: "Other Series");
        var repository = new StaticBookRepository([first, second]);
        var interaction = new ScriptedUserInteractionService { PromptTextResult = "New Series" };
        var viewModel = CreateViewModel([first, second], interaction, repository: repository);

        await viewModel.RefreshAsync();
        await viewModel.RenameSeriesFilterCommand.ExecuteAsync(
            viewModel.SeriesFilters.Single(filter => filter.Name == "Old Series"));

        repository.BooksSnapshot.Single(book => book.Metadata.Title == "First").Metadata.Series
            .Should().Be("New Series");
        repository.BooksSnapshot.Single(book => book.Metadata.Title == "Second").Metadata.Series
            .Should().Be("Other Series");
        viewModel.SeriesFilters.Should().ContainSingle(filter => filter.Name == "New Series");
    }

    [Fact]
    public async Task Rename_language_filter_updates_all_values_in_the_same_language_group()
    {
        var first = CreateBook("First", ["Author"], language: "eng");
        var second = CreateBook("Second", ["Author"], language: "en-US");
        var repository = new BulkScalarMetadataRepository([first, second]);
        var interaction = new ScriptedUserInteractionService { PromptTextResult = "en" };
        var viewModel = CreateViewModel([first, second], interaction, repository: repository);

        await viewModel.RefreshAsync();
        await viewModel.RenameLanguageFilterCommand.ExecuteAsync(
            viewModel.LanguageFilters.Single(filter => filter.Name == "en"));

        repository.BulkUpdateCalls.Should().Be(1);
        repository.UpdateCalls.Should().Be(0);
        repository.BooksSnapshot.Select(book => book.Metadata.Language)
            .Should().Equal("en", "en");
        viewModel.LanguageFilters.Should().ContainSingle(filter => filter.Name == "en" && filter.Count == 2);
        viewModel.IsCleaningMetadata.Should().BeFalse();
    }

    [Fact]
    public async Task Rename_filter_shows_metadata_cleanup_busy_state_until_update_completes()
    {
        var first = CreateBook("First", ["Author"], language: "eng");
        var repository = new BlockingBulkScalarMetadataRepository([first]);
        var interaction = new ScriptedUserInteractionService { PromptTextResult = "nl" };
        var viewModel = CreateViewModel([first], interaction, repository: repository);

        await viewModel.RefreshAsync();
        var rename = viewModel.RenameLanguageFilterCommand.ExecuteAsync(
            viewModel.LanguageFilters.Single(filter => filter.Name == "en"));
        await repository.BeforeBulkUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.IsCleaningMetadata.Should().BeTrue();
        viewModel.MetadataCleanupStatusText.Should().Be("Updating metadata...");
        repository.ReleaseBeforeBulkUpdate();
        await repository.BulkUpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        repository.ReleaseBulkUpdate();
        await rename;

        viewModel.IsCleaningMetadata.Should().BeFalse();
    }

    [Fact]
    public async Task Rename_filter_skips_books_that_conflict_with_existing_metadata()
    {
        var first = CreateBook("Same Title", ["Old Author"]);
        var second = CreateBook("Other Title", ["Old Author"]);
        var repository = new ConflictingBookRepository([first, second], first.Id);
        var interaction = new ScriptedUserInteractionService { PromptTextResult = "New Author" };
        var viewModel = CreateViewModel([first, second], interaction, repository: repository);

        await viewModel.RefreshAsync();
        await viewModel.RenameAuthorFilterCommand.ExecuteAsync(
            viewModel.AuthorFilters.Single(filter => filter.Name == "Old Author"));

        repository.BooksSnapshot.Single(book => book.Id == first.Id).Metadata.Authors
            .Should().Equal("Old Author");
        repository.BooksSnapshot.Single(book => book.Id == second.Id).Metadata.Authors
            .Should().Equal("New Author");
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
    public async Task Refresh_clears_active_library_when_library_folder_was_deleted_outside_the_app()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var libraryPath = temporaryDirectory.CreateSubdirectory("DeletedLibrary").FullName;
        var currentLibrary = CreateActiveLibrary(libraryPath);
        var repository = new ThrowingBookRepository();
        var viewModel = CreateViewModel(
            [CreateBook("Ghost", ["Author"])],
            repository: repository,
            currentLibrary: currentLibrary);

        Directory.Delete(libraryPath, recursive: true);

        await viewModel.RefreshAsync();

        currentLibrary.Current.Should().BeNull();
        viewModel.HasActiveLibrary.Should().BeFalse();
        viewModel.CurrentLibraryPath.Should().BeNull();
        viewModel.CurrentLibraryName.Should().Be("No library selected");
        viewModel.VisibleBooks.Should().BeEmpty();
        viewModel.EmptyStateMessage.Should().Be(
            "The active library folder no longer exists. Create or open a library to continue.");
    }

    [Fact]
    public async Task ScanFolderCommand_does_not_prompt_when_active_library_folder_was_deleted_outside_the_app()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var libraryPath = temporaryDirectory.CreateSubdirectory("DeletedLibrary").FullName;
        var currentLibrary = CreateActiveLibrary(libraryPath);
        var interaction = new ScriptedUserInteractionService { ScanFolder = temporaryDirectory.DirectoryPath };
        var viewModel = CreateViewModel(
            [],
            interaction,
            currentLibrary: currentLibrary,
            directoryScanner: new DirectoryScanner(),
            settingsStore: new InMemoryAppSettingsStore());

        Directory.Delete(libraryPath, recursive: true);

        await viewModel.ScanFolderCommand.ExecuteAsync(null);

        interaction.PickScanFolderCalls.Should().Be(0);
        currentLibrary.Current.Should().BeNull();
        viewModel.EmptyStateMessage.Should().Be(
            "The active library folder no longer exists. Create or open a library to continue.");
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
        await WaitUntilAsync(() => VisibleBookTitles(viewModel).Contains("Imported", StringComparer.Ordinal));

        viewModel.LastImportResult.Should().NotBeNull();
        viewModel.LastImportResult!.TotalCount.Should().Be(1);
        VisibleBookTitles(viewModel).Should().Contain("Imported");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                condition().Should().BeTrue();
                return;
            }

            await Task.Delay(25);
        }
    }

    private static IReadOnlyList<string> VisibleBookTitles(LibraryViewModel viewModel)
    {
        try
        {
            return viewModel.VisibleBooks.Select(book => book.Title).ToArray();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
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

    private static CurrentLibrary CreateActiveLibrary(string directoryPath)
    {
        var currentLibrary = new CurrentLibrary();
        currentLibrary.Set(new LibraryDescriptor(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(directoryPath)),
            directoryPath,
            DateTimeOffset.UtcNow));
        return currentLibrary;
    }

    private class StaticBookRepository(IReadOnlyList<Book> books) : IBookRepository
    {
        protected readonly List<Book> Books = [.. books];

        public IReadOnlyList<Book> BooksSnapshot => [.. Books];
        public int UpdateCalls { get; private set; }

        public virtual Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Book>>([.. Books]);
        public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Books.SingleOrDefault(book => book.Id == id));
        public Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> HasNormalizedTitleAndAuthorAsync(string title, IReadOnlyList<string> authors, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task UpdateAsync(Book book, CancellationToken cancellationToken)
        {
            UpdateCalls++;
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

    private class BulkScalarMetadataRepository(IReadOnlyList<Book> books)
        : StaticBookRepository(books), IBookBulkMetadataRepository
    {
        public int BulkUpdateCalls { get; private set; }

        public virtual Task<int> UpdateScalarMetadataAsync(
            IReadOnlyCollection<Guid> bookIds,
            BookScalarMetadataField field,
            string? value,
            CancellationToken cancellationToken)
        {
            BulkUpdateCalls++;
            var idSet = bookIds.ToHashSet();
            var updated = 0;
            for (var index = 0; index < Books.Count; index++)
            {
                var book = Books[index];
                if (!idSet.Contains(book.Id))
                {
                    continue;
                }

                Books[index] = book with
                {
                    Metadata = new BookMetadata(
                        book.Metadata.Title,
                        book.Metadata.Authors,
                        book.Metadata.Description,
                        field == BookScalarMetadataField.Language ? value : book.Metadata.Language,
                        book.Metadata.Publisher,
                        book.Metadata.PublicationDate,
                        book.Metadata.Tags,
                        field == BookScalarMetadataField.Series ? value : book.Metadata.Series,
                        book.Metadata.SeriesNumber,
                        book.Metadata.Isbn,
                        book.Metadata.CoverBytes)
                };
                updated++;
            }

            return Task.FromResult(updated);
        }
    }

    private sealed class BlockingBulkScalarMetadataRepository(IReadOnlyList<Book> books)
        : BulkScalarMetadataRepository(books)
    {
        private readonly TaskCompletionSource releaseBeforeBulkUpdate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BeforeBulkUpdate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BulkUpdateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<int> UpdateScalarMetadataAsync(
            IReadOnlyCollection<Guid> bookIds,
            BookScalarMetadataField field,
            string? value,
            CancellationToken cancellationToken)
        {
            BeforeBulkUpdate.TrySetResult();
            await releaseBeforeBulkUpdate.Task.WaitAsync(cancellationToken);
            BulkUpdateStarted.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await base.UpdateScalarMetadataAsync(bookIds, field, value, cancellationToken);
        }

        public void ReleaseBeforeBulkUpdate() => releaseBeforeBulkUpdate.TrySetResult();
        public void ReleaseBulkUpdate() => release.TrySetResult();
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

    private sealed class ConflictingBookRepository(
        IReadOnlyList<Book> books,
        Guid conflictingBookId) : StaticBookRepository(books)
    {
        public override Task UpdateAsync(Book book, CancellationToken cancellationToken)
        {
            if (book.Id == conflictingBookId)
            {
                throw new BookConflictException();
            }

            return base.UpdateAsync(book, cancellationToken);
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

    private sealed class ThrowingBookRepository : StaticBookRepository
    {
        public ThrowingBookRepository() : base([])
        {
        }

        public override Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The repository should not be called when the library folder is missing.");
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
        public string? PromptTextResult { get; init; }
        public bool ConfirmMetadataValueRemovalResult { get; init; }
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
        public Task<string?> PromptTextAsync(
            string title,
            string message,
            string initialValue,
            CancellationToken cancellationToken) =>
            Task.FromResult(PromptTextResult);

        public Task<bool> ConfirmMetadataValueRemovalAsync(
            string value,
            int affectedBookCount,
            CancellationToken cancellationToken) =>
            Task.FromResult(ConfirmMetadataValueRemovalResult);

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
