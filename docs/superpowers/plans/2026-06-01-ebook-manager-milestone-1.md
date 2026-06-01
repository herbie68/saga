# Ebook Manager Milestone 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a native Windows-testable .NET MAUI vertical slice that creates portable ebook libraries, imports and scans managed ebook files, stores metadata in SQLite, and exposes the approved three-view library workspace with editable details.

**Architecture:** Keep domain rules and use cases independent from MAUI and EF Core. Infrastructure owns SQLite, filesystem, hashing, and format adapters. The MAUI app composes services, hosts Windows-specific picker implementations behind interfaces, and uses Syncfusion `SfDataGrid` only in the detailed view.

**Tech Stack:** .NET 10, .NET MAUI XAML, C#, EF Core SQLite `10.0.8`, CommunityToolkit.Mvvm `8.4.2`, Syncfusion.Maui.DataGrid `33.2.8`, xUnit `2.9.3`, FluentAssertions `8.8.0`

---

## Scope Guard

This plan implements Milestone 1 only. It deliberately excludes drag-and-drop, faceted filters, search-term highlighting, extra color themes, active e-reader communication, custom fields, user-defined views, and format conversion.

Metadata extraction is intentionally useful but conservative:

- EPUB and KEPUB: read OPF metadata and embedded cover where available.
- CBZ: use the first supported image as cover and filename fallback for text metadata.
- PDF, CBR, MOBI, AZW, AZW3, and KFX: recognize and import safely with filename fallback in Milestone 1.
- Write-back: expose adapter outcomes and return `Unsupported` until a format-specific implementation is proven reliable. SQLite remains authoritative.

This boundary keeps the first executable release honest while preserving clear adapter extension points for Milestone 2.

## File Map

```text
EbookManager.sln
Directory.Build.props
Directory.Packages.props
.config/
  dotnet-tools.json
src/
  EbookManager.Domain/
    Books/Book.cs
    Books/BookFile.cs
    Books/BookMetadata.cs
    Books/Enums.cs
    Importing/ImportModels.cs
    Libraries/LibraryDescriptor.cs
    Abstractions/IAppSettingsStore.cs
    Abstractions/IBookRepository.cs
    Abstractions/IFileHasher.cs
    Abstractions/ILibraryDatabaseInitializer.cs
    Abstractions/ILibraryFileStore.cs
    Abstractions/IMetadataAdapter.cs
  EbookManager.Application/
    Libraries/LibraryService.cs
    Importing/ImportService.cs
    Importing/DirectoryScanner.cs
    Books/BookService.cs
    Books/BookSearchService.cs
  EbookManager.Presentation/
    ViewModels/BookDetailsViewModel.cs
    ViewModels/ImportResultViewModel.cs
    ViewModels/LibraryViewModel.cs
    ViewModels/SettingsViewModel.cs
  EbookManager.Infrastructure/
    Persistence/LibraryDbContext.cs
    Persistence/LibraryDbContextFactory.cs
    Persistence/Entities/BookEntity.cs
    Persistence/Entities/AuthorEntity.cs
    Persistence/Entities/BookAuthorEntity.cs
    Persistence/Entities/TagEntity.cs
    Persistence/Entities/BookTagEntity.cs
    Persistence/Entities/BookFileEntity.cs
    Persistence/Entities/ImportRunEntity.cs
    Persistence/Entities/ImportItemEntity.cs
    Persistence/Migrations/<generated migration files>
    Persistence/Repositories/EfBookRepository.cs
    Files/ManagedLibraryFileStore.cs
    Files/Sha256FileHasher.cs
    Metadata/CbzMetadataAdapter.cs
    Metadata/EpubMetadataAdapter.cs
    Metadata/FallbackMetadataAdapter.cs
    Metadata/MetadataAdapterResolver.cs
    Settings/JsonAppSettingsStore.cs
  EbookManager.App/
    MauiProgram.cs
    App.xaml
    App.xaml.cs
    MainPage.xaml
    MainPage.xaml.cs
    Views/BookDetailsView.xaml
    Views/BookshelfView.xaml
    Views/DetailedGridView.xaml
    Views/ImportResultPage.xaml
    Views/ListView.xaml
    Views/SettingsPage.xaml
    Services/DeleteConfirmationService.cs
    Services/FilePickerService.cs
    Services/IFolderPicker.cs
    Services/LocalizationService.cs
    Services/ThemeService.cs
    Platforms/Windows/WindowsFolderPicker.cs
    Resources/Strings/AppResources*.resx
    Resources/Styles/Themes/DarkTheme.xaml
    Resources/Styles/Themes/LightTheme.xaml
tests/
  EbookManager.Tests/
    Libraries/LibraryServiceTests.cs
    Importing/ImportPrimitivesTests.cs
    Importing/ImportServiceTests.cs
    Books/BookServiceTests.cs
    Books/BookSearchServiceTests.cs
    Books/DomainModelTests.cs
    Infrastructure/JsonAppSettingsStoreTests.cs
    Infrastructure/LibraryDbContextTests.cs
    TestSupport/InMemoryAppSettingsStore.cs
    TestSupport/TemporaryDirectory.cs
```

## Task 1: Scaffold the Solution and Pin Dependencies

**Files:**
- Create: `EbookManager.sln`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.config/dotnet-tools.json`
- Create: `src/EbookManager.Domain/EbookManager.Domain.csproj`
- Create: `src/EbookManager.Application/EbookManager.Application.csproj`
- Create: `src/EbookManager.Infrastructure/EbookManager.Infrastructure.csproj`
- Create: `src/EbookManager.Presentation/EbookManager.Presentation.csproj`
- Create: `src/EbookManager.App/EbookManager.App.csproj`
- Create: `tests/EbookManager.Tests/EbookManager.Tests.csproj`

- [ ] **Step 1: Scaffold the projects**

Run:

```powershell
dotnet new sln -n EbookManager
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.8
dotnet new classlib -n EbookManager.Domain -o src/EbookManager.Domain -f net10.0
dotnet new classlib -n EbookManager.Application -o src/EbookManager.Application -f net10.0
dotnet new classlib -n EbookManager.Infrastructure -o src/EbookManager.Infrastructure -f net10.0
dotnet new classlib -n EbookManager.Presentation -o src/EbookManager.Presentation -f net10.0
dotnet new maui -n EbookManager.App -o src/EbookManager.App
dotnet new xunit -n EbookManager.Tests -o tests/EbookManager.Tests -f net10.0
dotnet sln EbookManager.sln add src/EbookManager.Domain src/EbookManager.Application src/EbookManager.Infrastructure src/EbookManager.Presentation src/EbookManager.App tests/EbookManager.Tests
dotnet add src/EbookManager.Application reference src/EbookManager.Domain
dotnet add src/EbookManager.Infrastructure reference src/EbookManager.Domain src/EbookManager.Application
dotnet add src/EbookManager.Presentation reference src/EbookManager.Domain src/EbookManager.Application
dotnet add src/EbookManager.App reference src/EbookManager.Domain src/EbookManager.Application src/EbookManager.Infrastructure src/EbookManager.Presentation
dotnet add tests/EbookManager.Tests reference src/EbookManager.Domain src/EbookManager.Application src/EbookManager.Infrastructure src/EbookManager.Presentation
```

Expected: six projects are added to `EbookManager.sln` and `.config/dotnet-tools.json` contains repository-local `dotnet-ef`.

- [ ] **Step 2: Pin shared build settings and package versions**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Create `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageVersion Include="FluentAssertions" Version="8.8.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.8" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.1" />
    <PackageVersion Include="Syncfusion.Maui.DataGrid" Version="33.2.8" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>
</Project>
```

Add package references without inline versions:

```powershell
dotnet add src/EbookManager.App package CommunityToolkit.Mvvm
dotnet add src/EbookManager.Presentation package CommunityToolkit.Mvvm
dotnet add src/EbookManager.App package Syncfusion.Maui.DataGrid
dotnet add src/EbookManager.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/EbookManager.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add tests/EbookManager.Tests package FluentAssertions
```

Replace the package references generated by `dotnet new xunit` with versionless references to `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `coverlet.collector` so central package management owns every version.

- [ ] **Step 3: Remove generated placeholder classes and verify restore**

Delete generated starter `Class1.cs` files from the four class libraries. Run:

```powershell
dotnet restore EbookManager.sln
dotnet build src/EbookManager.App/EbookManager.App.csproj -f net10.0-windows10.0.19041.0
```

Expected: restore and build succeed.

- [ ] **Step 4: Commit**

```powershell
git add EbookManager.sln Directory.Build.props Directory.Packages.props src tests
git commit -m "Scaffold the Ebook Manager solution"
```

## Task 2: Define Domain Models and Contracts

**Files:**
- Create: `src/EbookManager.Domain/Books/Enums.cs`
- Create: `src/EbookManager.Domain/Books/BookMetadata.cs`
- Create: `src/EbookManager.Domain/Books/Book.cs`
- Create: `src/EbookManager.Domain/Books/BookFile.cs`
- Create: `src/EbookManager.Domain/Libraries/LibraryDescriptor.cs`
- Create: `src/EbookManager.Domain/Importing/ImportModels.cs`
- Create: `src/EbookManager.Domain/Abstractions/IAppSettingsStore.cs`
- Create: `src/EbookManager.Domain/Abstractions/IBookRepository.cs`
- Create: `src/EbookManager.Domain/Abstractions/IFileHasher.cs`
- Create: `src/EbookManager.Domain/Abstractions/ILibraryFileStore.cs`
- Create: `src/EbookManager.Domain/Abstractions/IMetadataAdapter.cs`
- Test: `tests/EbookManager.Tests/Books/DomainModelTests.cs`

- [ ] **Step 1: Write failing domain tests**

Create `tests/EbookManager.Tests/Books/DomainModelTests.cs`:

```csharp
using EbookManager.Domain.Books;
using FluentAssertions;

namespace EbookManager.Tests.Books;

public sealed class DomainModelTests
{
    [Fact]
    public void Supported_formats_include_kobo_and_kindle_variants()
    {
        EbookFormatExtensions.Supported.Should().Contain([
            EbookFormat.Epub, EbookFormat.Kepub, EbookFormat.Pdf, EbookFormat.Cbr,
            EbookFormat.Cbz, EbookFormat.Mobi, EbookFormat.Azw, EbookFormat.Azw3, EbookFormat.Kfx
        ]);
    }

    [Theory]
    [InlineData("book.epub", EbookFormat.Epub)]
    [InlineData("book.kepub.epub", EbookFormat.Kepub)]
    [InlineData("BOOK.AZW3", EbookFormat.Azw3)]
    public void Filename_maps_to_expected_format(string filename, EbookFormat expected)
    {
        EbookFormatExtensions.TryFromFilename(filename, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~DomainModelTests
```

Expected: FAIL because the domain types do not exist.

- [ ] **Step 3: Implement enums, records, and service contracts**

Create `src/EbookManager.Domain/Books/Enums.cs`:

```csharp
namespace EbookManager.Domain.Books;

public enum ReadingStatus { Unread, Reading, Read }
public enum MetadataWriteBackStatus { NotAttempted, Unsupported, Succeeded, Failed }
public enum EbookFormat { Epub, Kepub, Pdf, Cbr, Cbz, Mobi, Azw, Azw3, Kfx }

public static class EbookFormatExtensions
{
    public static readonly IReadOnlySet<EbookFormat> Supported = Enum.GetValues<EbookFormat>().ToHashSet();

    public static bool TryFromFilename(string path, out EbookFormat format)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".kepub.epub", StringComparison.Ordinal))
        {
            format = EbookFormat.Kepub;
            return true;
        }

        return Enum.TryParse(Path.GetExtension(name).TrimStart('.'), true, out format);
    }

    public static string ToExtension(this EbookFormat format) => format switch
    {
        EbookFormat.Kepub => ".kepub.epub",
        _ => $".{format.ToString().ToLowerInvariant()}"
    };
}
```

Create `src/EbookManager.Domain/Books/BookMetadata.cs`:

```csharp
namespace EbookManager.Domain.Books;

public sealed record BookMetadata(
    string Title,
    IReadOnlyList<string> Authors,
    string? Description = null,
    string? Language = null,
    string? Publisher = null,
    DateOnly? PublicationDate = null,
    IReadOnlyList<string>? Tags = null,
    string? Series = null,
    decimal? SeriesNumber = null,
    string? Isbn = null,
    byte[]? CoverBytes = null);
```

Create `src/EbookManager.Domain/Books/Book.cs` and `BookFile.cs`:

```csharp
namespace EbookManager.Domain.Books;

public sealed record Book(
    Guid Id, BookMetadata Metadata, ReadingStatus ReadingStatus,
    string? CoverRelativePath, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

public sealed record BookFile(
    Guid Id, Guid BookId, EbookFormat Format, string RelativePath, string Sha256,
    long SizeBytes, MetadataWriteBackStatus WriteBackStatus, string? WriteBackMessage);
```

Create the remaining records and interfaces:

```csharp
// Libraries/LibraryDescriptor.cs
namespace EbookManager.Domain.Libraries;
public sealed record LibraryDescriptor(string Name, string DirectoryPath, DateTimeOffset LastOpenedUtc);

// Importing/ImportModels.cs
using EbookManager.Domain.Books;
namespace EbookManager.Domain.Importing;
public enum ImportOutcome { Added, ExactDuplicate, PossibleDuplicate, Failed }
public sealed record ImportItemResult(string SourcePath, ImportOutcome Outcome, string Message, Guid? BookId = null);
public sealed record ImportBatchResult(IReadOnlyList<ImportItemResult> Items);
public sealed record MetadataReadResult(BookMetadata Metadata, string? Warning = null);
public sealed record MetadataWriteResult(MetadataWriteBackStatus Status, string? Message = null);
```

Add contracts under `src/EbookManager.Domain/Abstractions/`:

```csharp
// IBookRepository.cs
using EbookManager.Domain.Books;
namespace EbookManager.Domain.Abstractions;
public interface IBookRepository
{
    Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken);
    Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasHashAsync(string sha256, CancellationToken cancellationToken);
    Task<bool> HasNormalizedTitleAndAuthorAsync(string title, IReadOnlyList<string> authors, CancellationToken cancellationToken);
    Task AddAsync(Book book, BookFile file, CancellationToken cancellationToken);
    Task UpdateAsync(Book book, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

// IFileHasher.cs, ILibraryFileStore.cs, IMetadataAdapter.cs, IAppSettingsStore.cs
using EbookManager.Domain.Books;
using EbookManager.Domain.Importing;
using EbookManager.Domain.Libraries;
namespace EbookManager.Domain.Abstractions;
public interface IFileHasher { Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken); }
public interface ILibraryFileStore
{
    Task<(string RelativeBookPath, string? RelativeCoverPath)> CopyIntoLibraryAsync(Guid bookId, string sourcePath, byte[]? coverBytes, CancellationToken cancellationToken);
    Task DeleteBookDirectoryAsync(Guid bookId, CancellationToken cancellationToken);
}
public interface IMetadataAdapter
{
    bool CanHandle(EbookFormat format);
    Task<MetadataReadResult> ReadAsync(string path, EbookFormat format, CancellationToken cancellationToken);
    Task<MetadataWriteResult> WriteAsync(string path, EbookFormat format, BookMetadata metadata, CancellationToken cancellationToken);
}
public sealed record AppSettings(string? LastLibraryPath, string Culture, string Theme, string DefaultView, bool ConfirmDelete);
public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
    Task<IReadOnlyList<LibraryDescriptor>> ListLibrariesAsync(CancellationToken cancellationToken);
    Task SaveLibrariesAsync(IReadOnlyList<LibraryDescriptor> libraries, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~DomainModelTests
git add src/EbookManager.Domain tests/EbookManager.Tests/Books
git commit -m "Define ebook domain models and contracts"
```

Expected: PASS.

## Task 3: Add Portable Library Creation and Global Settings

**Files:**
- Create: `src/EbookManager.Application/Libraries/LibraryService.cs`
- Create: `src/EbookManager.Infrastructure/Settings/JsonAppSettingsStore.cs`
- Test: `tests/EbookManager.Tests/Libraries/LibraryServiceTests.cs`
- Create: `tests/EbookManager.Tests/TestSupport/InMemoryAppSettingsStore.cs`
- Create: `tests/EbookManager.Tests/TestSupport/TemporaryDirectory.cs`

- [ ] **Step 1: Write failing library service tests**

Create `tests/EbookManager.Tests/Libraries/LibraryServiceTests.cs` with tests that:

```csharp
[Fact]
public async Task Create_creates_books_directory_and_remembers_library()
{
    var root = temporaryDirectory.CreateSubdirectory("ELibrary").FullName;
    var store = new InMemoryAppSettingsStore();
    var service = new LibraryService(store);

    var library = await service.CreateAsync("ELibrary", root, CancellationToken.None);

    Directory.Exists(Path.Combine(root, "books")).Should().BeTrue();
    (await store.ListLibrariesAsync(default)).Should().ContainSingle(x => x.DirectoryPath == root);
    (await store.LoadAsync(default)).LastLibraryPath.Should().Be(root);
}
```

Add a second test that `OpenAsync` rejects a missing directory with `DirectoryNotFoundException`.

Create reusable test helpers. `InMemoryAppSettingsStore` implements `IAppSettingsStore` with in-memory properties initialized to:

```csharp
public AppSettings Settings { get; private set; } = new(null, "en-US", "Light", "Detailed", true);
public List<LibraryDescriptor> Libraries { get; private set; } = [];
```

`TemporaryDirectory` creates a unique directory below `Path.GetTempPath()` and removes it in `Dispose()`.

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~LibraryServiceTests
```

Expected: FAIL because `LibraryService` is missing.

- [ ] **Step 3: Implement library creation, opening, and JSON settings**

Implement `LibraryService` with:

```csharp
public async Task<LibraryDescriptor> CreateAsync(string name, string directoryPath, CancellationToken ct)
{
    Directory.CreateDirectory(directoryPath);
    Directory.CreateDirectory(Path.Combine(directoryPath, "books"));
    return await RememberAsync(new(name, Path.GetFullPath(directoryPath), DateTimeOffset.UtcNow), ct);
}

public async Task<LibraryDescriptor> OpenAsync(string directoryPath, CancellationToken ct)
{
    if (!Directory.Exists(directoryPath)) throw new DirectoryNotFoundException(directoryPath);
    Directory.CreateDirectory(Path.Combine(directoryPath, "books"));
    var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directoryPath));
    return await RememberAsync(new(name, Path.GetFullPath(directoryPath), DateTimeOffset.UtcNow), ct);
}
```

Implement `JsonAppSettingsStore` in the platform-local appdata directory using `System.Text.Json`. Use atomic writes through a `.tmp` file followed by `File.Move(temp, target, true)`. Store `settings.json` and `libraries.json` separately.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~LibraryServiceTests
git add src/EbookManager.Application/Libraries src/EbookManager.Infrastructure/Settings tests/EbookManager.Tests/Libraries
git commit -m "Add portable library management and app settings"
```

Expected: PASS.

## Task 4: Persist Libraries with EF Core SQLite and Migrations

**Files:**
- Create: `src/EbookManager.Infrastructure/Persistence/LibraryDbContext.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/LibraryDbContextFactory.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/DesignTimeLibraryDbContextFactory.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Entities/BookEntity.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Entities/AuthorEntity.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Entities/BookAuthorEntity.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Entities/TagEntity.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Entities/BookTagEntity.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Entities/BookFileEntity.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Entities/ImportRunEntity.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Entities/ImportItemEntity.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/Migrations/<generated migration files>`
- Create: `src/EbookManager.Infrastructure/Persistence/Repositories/EfBookRepository.cs`
- Test: `tests/EbookManager.Tests/Infrastructure/LibraryDbContextTests.cs`

- [ ] **Step 1: Write failing persistence tests**

Create tests that construct `LibraryDbContextFactory` for a temporary directory, call `MigrateAsync`, add a book and file through `EfBookRepository`, and assert:

```csharp
File.Exists(Path.Combine(libraryPath, "library.db")).Should().BeTrue();
(await repository.ListAsync(default)).Should().ContainSingle(x => x.Metadata.Title == "Test Book");
(await repository.HasHashAsync("ABC123", default)).Should().BeTrue();
```

Add a test that multiple authors retain their input order.

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~LibraryDbContextTests
```

Expected: FAIL because persistence types are absent.

- [ ] **Step 3: Implement EF entities and mappings**

Implement entity classes for `BookEntity`, `AuthorEntity`, `BookAuthorEntity`, `TagEntity`, `BookTagEntity`, `BookFileEntity`, `ImportRunEntity`, and `ImportItemEntity`.

Configure:

```csharp
modelBuilder.Entity<BookFileEntity>().HasIndex(x => x.Sha256).IsUnique();
modelBuilder.Entity<BookAuthorEntity>().HasKey(x => new { x.BookId, x.AuthorId });
modelBuilder.Entity<BookAuthorEntity>().HasIndex(x => new { x.BookId, x.Order }).IsUnique();
modelBuilder.Entity<BookTagEntity>().HasKey(x => new { x.BookId, x.TagId });
```

Use `ValueConverter<DateOnly?, string?>` for nullable publication dates and store enums as strings.

- [ ] **Step 4: Implement context factory and repository**

`LibraryDbContextFactory.Create(directoryPath)` must use:

```csharp
var options = new DbContextOptionsBuilder<LibraryDbContext>()
    .UseSqlite($"Data Source={Path.Combine(directoryPath, "library.db")}")
    .Options;
return new LibraryDbContext(options);
```

Implement `IDesignTimeDbContextFactory<LibraryDbContext>` in `DesignTimeLibraryDbContextFactory`. It creates a design-time SQLite file below `Path.GetTempPath()` so `dotnet ef migrations add` can instantiate the context without the MAUI app.

Map repository reads and writes between EF entities and domain records. Normalize possible-duplicate matching with trimmed lowercase title and author strings.

- [ ] **Step 5: Create the initial migration and run tests**

```powershell
dotnet tool run dotnet-ef migrations add InitialLibrarySchema `
  --project src/EbookManager.Infrastructure `
  --startup-project src/EbookManager.Infrastructure `
  --output-dir Persistence/Migrations
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~LibraryDbContextTests
```

Expected: PASS and a migration under `Persistence/Migrations`.

- [ ] **Step 6: Commit**

```powershell
git add src/EbookManager.Infrastructure/Persistence tests/EbookManager.Tests/Infrastructure
git commit -m "Persist ebook libraries with SQLite"
```

## Task 5: Implement Hashing, Managed Files, Scanning, and Metadata Adapters

**Files:**
- Create: `src/EbookManager.Infrastructure/Files/Sha256FileHasher.cs`
- Create: `src/EbookManager.Infrastructure/Files/ManagedLibraryFileStore.cs`
- Create: `src/EbookManager.Application/Importing/DirectoryScanner.cs`
- Create: `src/EbookManager.Infrastructure/Metadata/FallbackMetadataAdapter.cs`
- Create: `src/EbookManager.Infrastructure/Metadata/EpubMetadataAdapter.cs`
- Create: `src/EbookManager.Infrastructure/Metadata/CbzMetadataAdapter.cs`
- Create: `src/EbookManager.Infrastructure/Metadata/MetadataAdapterResolver.cs`
- Test: `tests/EbookManager.Tests/Importing/ImportPrimitivesTests.cs`

- [ ] **Step 1: Write failing primitive tests**

Add `Scanner_respects_recursive_flag`: create `root.epub` and `nested/nested.epub`, call `Scan(root, false)` and assert only `root.epub`, then call `Scan(root, true)` and assert both files.

Add `Hasher_returns_stable_uppercase_sha256`: write UTF-8 bytes for `ebook-manager`, calculate the expected value with `Convert.ToHexString(SHA256.HashData(bytes))`, and assert `Sha256FileHasher.ComputeSha256Async` returns that value.

Add a theory `Fallback_adapter_extracts_filename_metadata` with `The Hobbit - J.R.R. Tolkien.epub` expecting title `The Hobbit` and author `J.R.R. Tolkien`, plus `Unknown Title.pdf` expecting title `Unknown Title` and author `Unknown`.

Add an EPUB fixture created in the test with `ZipArchive`: `META-INF/container.xml`, `OEBPS/content.opf`, and `OEBPS/cover.jpg`. Assert title, author, language, publisher, ISBN, description, and cover bytes.

Add a CBZ fixture with two images and assert that the alphabetically first supported image is selected as cover.

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~ImportPrimitivesTests
```

Expected: FAIL.

- [ ] **Step 3: Implement primitives**

`Sha256FileHasher` opens an async sequential-read stream and returns `Convert.ToHexString(await SHA256.HashDataAsync(stream, ct))`.

`DirectoryScanner.Scan(directory, recursive)` uses:

```csharp
var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
return Directory.EnumerateFiles(directory, "*", option)
    .Where(path => EbookFormatExtensions.TryFromFilename(path, out _))
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToArray();
```

`ManagedLibraryFileStore` copies to `books/<book-id>/<original-filename>` and writes `cover.jpg` when cover bytes exist. Return paths relative to the library root.

- [ ] **Step 4: Implement conservative adapters**

`FallbackMetadataAdapter` handles all recognized formats, parses `Title - Author` filenames, uses `Unknown` when author is absent, and returns `Unsupported` from `WriteAsync`.

`EpubMetadataAdapter` handles EPUB and KEPUB. It reads the OPF path from `META-INF/container.xml`, then uses namespace-agnostic XML local-name matching to extract Dublin Core fields and cover references. If the archive is malformed, return the fallback result plus a warning.

`CbzMetadataAdapter` handles CBZ, extracts the first `.jpg`, `.jpeg`, `.png`, or `.webp` entry as cover, and delegates text metadata to fallback. CBR remains recognized through fallback because RAR parsing is deferred.

`MetadataAdapterResolver` returns the first specific adapter that can handle a format, then fallback.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~ImportPrimitivesTests
git add src/EbookManager.Application/Importing src/EbookManager.Infrastructure/Files src/EbookManager.Infrastructure/Metadata tests/EbookManager.Tests/Importing
git commit -m "Add ebook import primitives and metadata adapters"
```

Expected: PASS.

## Task 6: Build the Per-Book Transactional Import Pipeline

**Files:**
- Create: `src/EbookManager.Application/Importing/ImportService.cs`
- Modify: `src/EbookManager.Infrastructure/Persistence/Repositories/EfBookRepository.cs`
- Test: `tests/EbookManager.Tests/Importing/ImportServiceTests.cs`

- [ ] **Step 1: Write failing import service tests**

Cover these behaviors with temporary files and a temporary SQLite library:

- `Import_copies_source_and_preserves_original`: import one EPUB, assert `Added`, assert the source still exists, and assert exactly one copied managed file exists below `books/<book-id>/`.
- `Import_skips_exact_hash_duplicate`: import the same EPUB twice, assert the second outcome is `ExactDuplicate`, and assert only one book exists.
- `Import_reports_title_author_duplicate_without_copying_second_file`: create two different EPUB byte streams with the same title and author, import both, assert the second outcome is `PossibleDuplicate`, and assert only one managed book directory exists.
- `Import_continues_after_one_file_fails`: import a missing EPUB path followed by a valid EPUB, assert outcomes `Failed` and `Added`.

Verify copied files live below `books/<book-id>/`, source files still exist, and each result has `Added`, `ExactDuplicate`, `PossibleDuplicate`, or `Failed`.

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~ImportServiceTests
```

Expected: FAIL.

- [ ] **Step 3: Implement `ImportService`**

Use one method:

```csharp
public async Task<ImportBatchResult> ImportAsync(
    IReadOnlyList<string> sourcePaths,
    CancellationToken cancellationToken)
```

For each file: validate format, hash, reject exact duplicate, read metadata, reject normalized title-author duplicate, copy managed file, add domain book and file, and append a result. Catch exceptions per file, remove any copied book directory, and append `Failed`. Do not catch `OperationCanceledException`.

Record `ImportRunEntity` and `ImportItemEntity` through a repository method so completed UI summaries survive app restarts.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~ImportServiceTests
git add src/EbookManager.Application/Importing src/EbookManager.Infrastructure/Persistence tests/EbookManager.Tests/Importing
git commit -m "Implement transactional ebook imports"
```

Expected: PASS.

## Task 7: Add Search, Metadata Save, Undo Support, and Delete

**Files:**
- Create: `src/EbookManager.Application/Books/BookSearchService.cs`
- Create: `src/EbookManager.Application/Books/BookService.cs`
- Test: `tests/EbookManager.Tests/Books/BookServiceTests.cs`
- Test: `tests/EbookManager.Tests/Books/BookSearchServiceTests.cs`

- [ ] **Step 1: Write failing search tests**

Create books with matches in title, author, description, language, publisher, tag, series, ISBN, and reading status. Assert:

```csharp
service.Filter(books, "tolkien").Should().ContainSingle(x => x.Metadata.Title == "The Hobbit");
service.Filter(books, "").Should().HaveCount(books.Count);
```

- [ ] **Step 2: Write failing book service tests**

Test that save updates SQLite even when adapter write-back returns `Unsupported`, and delete removes both the database record and `books/<book-id>/`.

- [ ] **Step 3: Run tests to verify failure**

```powershell
dotnet test tests/EbookManager.Tests --filter "FullyQualifiedName~BookSearchServiceTests|FullyQualifiedName~BookServiceTests"
```

Expected: FAIL.

- [ ] **Step 4: Implement services**

`BookSearchService.Filter` performs case-insensitive `Contains` checks across displayed metadata, authors, tags, ISBN, and reading status.

Extend `IBookRepository` and `EfBookRepository` with `ListFilesAsync(Guid bookId, CancellationToken cancellationToken)` and `UpdateFileWriteBackAsync(Guid fileId, MetadataWriteResult result, CancellationToken cancellationToken)`.

`BookService.SaveAsync` updates SQLite first and then attempts write-back for each managed file. Persist each adapter outcome to `BookFiles`. `BookService.DeleteAsync` deletes managed files first, then removes the database record. If filesystem deletion fails, return a cleanup warning and leave the record intact so the problem remains discoverable.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/EbookManager.Tests --filter "FullyQualifiedName~BookSearchServiceTests|FullyQualifiedName~BookServiceTests"
git add src/EbookManager.Application/Books src/EbookManager.Infrastructure/Persistence tests/EbookManager.Tests/Books
git commit -m "Add book search editing and deletion services"
```

Expected: PASS.

## Task 8: Compose Services and Initialize the Current Library

**Files:**
- Modify: `src/EbookManager.App/MauiProgram.cs`
- Create: `src/EbookManager.Application/Libraries/CurrentLibrary.cs`
- Create: `src/EbookManager.Application/Libraries/AppStartupService.cs`
- Create: `src/EbookManager.Domain/Abstractions/ILibraryDatabaseInitializer.cs`
- Create: `src/EbookManager.Infrastructure/Persistence/LibraryDatabaseInitializer.cs`
- Create: `src/EbookManager.App/Services/FilePickerService.cs`
- Create: `src/EbookManager.App/Services/IFolderPicker.cs`
- Create: `src/EbookManager.App/Platforms/Windows/WindowsFolderPicker.cs`
- Test: `tests/EbookManager.Tests/App/AppStartupServiceTests.cs`

- [ ] **Step 1: Write failing startup tests**

Use a fake settings store, fake `ILibraryDatabaseInitializer`, and temporary library. Assert startup reopens `LastLibraryPath`, migrates the database, and leaves current library unset when the saved path no longer exists.

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~AppStartupServiceTests
```

Expected: FAIL.

- [ ] **Step 3: Implement composition services**

`CurrentLibrary` holds the active `LibraryDescriptor` and raises `Changed`. Add an `ILibraryDatabaseInitializer` domain contract and implement it in Infrastructure by calling `Database.MigrateAsync`.

`AppStartupService.InitializeAsync` loads settings, opens a valid last library through `LibraryService`, creates a context, and calls `Database.MigrateAsync`.

`FilePickerService` wraps MAUI `FilePicker.Default.PickMultipleAsync` with the nine supported extensions.

`IFolderPicker` exposes:

```csharp
Task<string?> PickAsync(CancellationToken cancellationToken);
```

Implement Windows `FolderPicker` using `Windows.Storage.Pickers.FolderPicker`, initialize it with the MAUI window handle, and return `folder?.Path`.

- [ ] **Step 4: Register dependencies**

Register stores, factories, services, adapters, current library, pages, and viewmodels in `MauiProgram.CreateMauiApp`. Add:

```csharp
using Syncfusion.Maui.Core.Hosting;
builder.ConfigureSyncfusionCore();
```

Register the Syncfusion license from `SYNCFUSION_LICENSE_KEY` when present. Do not commit a license key.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~AppStartupServiceTests
git add src/EbookManager.Application src/EbookManager.Domain src/EbookManager.Infrastructure src/EbookManager.App tests/EbookManager.Tests/App
git commit -m "Compose app services and library startup"
```

Expected: PASS.

## Task 9: Implement Library and Details ViewModels

**Files:**
- Create: `src/EbookManager.Presentation/ViewModels/BookDetailsViewModel.cs`
- Create: `src/EbookManager.Presentation/ViewModels/LibraryViewModel.cs`
- Create: `src/EbookManager.Presentation/ViewModels/ImportResultViewModel.cs`
- Create: `src/EbookManager.Presentation/ViewModels/SettingsViewModel.cs`
- Create: `src/EbookManager.Presentation/Abstractions/IUserInteractionService.cs`
- Test: `tests/EbookManager.Tests/App/ViewModels/BookDetailsViewModelTests.cs`
- Test: `tests/EbookManager.Tests/App/ViewModels/LibraryViewModelTests.cs`

- [ ] **Step 1: Write failing dirty-state and undo tests**

Assert:

```csharp
viewModel.Load(book);
viewModel.Title = "Changed";
viewModel.HasUnsavedChanges.Should().BeTrue();
viewModel.Undo();
viewModel.Title.Should().Be(book.Metadata.Title);
viewModel.HasUnsavedChanges.Should().BeFalse();
```

- [ ] **Step 2: Write failing library filter and view switch tests**

Assert typing into `SearchText` updates `VisibleBooks`, selecting a book loads details, and `SelectedView` switches between `Bookshelf`, `Detailed`, and `List`.

- [ ] **Step 3: Run tests to verify failure**

```powershell
dotnet test tests/EbookManager.Tests --filter "FullyQualifiedName~BookDetailsViewModelTests|FullyQualifiedName~LibraryViewModelTests"
```

Expected: FAIL.

- [ ] **Step 4: Implement viewmodels**

Use `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm. Keep an immutable loaded snapshot in `BookDetailsViewModel`; recompute `HasUnsavedChanges` whenever an editable property changes.

Keep MAUI APIs out of the Presentation project. Define `IUserInteractionService` for file selection, folder selection, confirmation, and navigation to import results. Implement that interface in the MAUI app during Task 11.

`LibraryViewModel` owns:

```csharp
ObservableCollection<BookRowViewModel> VisibleBooks
string SearchText
LibraryView SelectedView
BookRowViewModel? SelectedBook
IAsyncRelayCommand AddBooksCommand
IAsyncRelayCommand ScanFolderCommand
IAsyncRelayCommand CreateLibraryCommand
IAsyncRelayCommand OpenLibraryCommand
```

Refresh from `IBookRepository.ListAsync` after imports, saves, deletes, and library switches.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/EbookManager.Tests --filter "FullyQualifiedName~BookDetailsViewModelTests|FullyQualifiedName~LibraryViewModelTests"
git add src/EbookManager.Presentation tests/EbookManager.Tests/App/ViewModels
git commit -m "Add library and metadata editor viewmodels"
```

Expected: PASS.

## Task 10: Add Localization, Themes, and Confirmation Settings

**Files:**
- Create: `src/EbookManager.App/Resources/Strings/AppResources.resx`
- Create: `src/EbookManager.App/Resources/Strings/AppResources.nl.resx`
- Create: `src/EbookManager.App/Resources/Strings/AppResources.de.resx`
- Create: `src/EbookManager.App/Resources/Strings/AppResources.fr.resx`
- Create: `src/EbookManager.App/Resources/Strings/AppResources.es.resx`
- Create: `src/EbookManager.App/Resources/Strings/AppResources.it.resx`
- Create: `src/EbookManager.App/Resources/Styles/Themes/LightTheme.xaml`
- Create: `src/EbookManager.App/Resources/Styles/Themes/DarkTheme.xaml`
- Create: `src/EbookManager.App/Services/LocalizationService.cs`
- Create: `src/EbookManager.App/Services/ThemeService.cs`
- Create: `src/EbookManager.App/Services/DeleteConfirmationService.cs`
- Create: `src/EbookManager.App/Services/UserInteractionService.cs`
- Test: `tests/EbookManager.Tests/Infrastructure/JsonAppSettingsStoreTests.cs`

- [ ] **Step 1: Write failing settings tests**

Assert `JsonAppSettingsStore` round-trips culture, theme, default view, and delete confirmation behavior. UI application of language and theme is checked manually in Task 12 because those services intentionally depend on MAUI resources.

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~JsonAppSettingsStoreTests
```

Expected: FAIL.

- [ ] **Step 3: Add resource keys and complete Dutch and English values**

Include at least: toolbar actions, three view names, search placeholder, grid headers, reading statuses, e-reader unavailable, details fields, unsaved warning, save, undo, delete, scan recursion option, import outcomes, settings fields, delete confirmation text, remember answer, app version, and book count.

Create German, French, Spanish, and Italian resource files containing the same keys with English values and a top-level XML comment that translations are intentionally prepared but not shipped as selectable languages in Milestone 1.

- [ ] **Step 4: Implement services and themes**

Expose selectable cultures `en-US` and `nl-NL` only. Implement light and dark resource dictionaries and merge exactly one at runtime. Persist culture, theme, default view, and delete confirmation behavior through `IAppSettingsStore`.

The delete confirmation dialog must return both `Confirmed` and `RememberAnswer`; persist `ConfirmDelete = false` only when the user confirmed deletion and checked remember-answer.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~JsonAppSettingsStoreTests
git add src/EbookManager.App/Resources src/EbookManager.App/Services tests/EbookManager.Tests/App
git commit -m "Add localization themes and confirmation settings"
```

Expected: PASS.

## Task 11: Build the Approved MAUI Workspace

**Files:**
- Modify: `src/EbookManager.App/App.xaml`
- Modify: `src/EbookManager.App/App.xaml.cs`
- Replace: `src/EbookManager.App/MainPage.xaml`
- Modify: `src/EbookManager.App/MainPage.xaml.cs`
- Create: `src/EbookManager.App/Views/BookshelfView.xaml`
- Create: `src/EbookManager.App/Views/DetailedGridView.xaml`
- Create: `src/EbookManager.App/Views/ListView.xaml`
- Create: `src/EbookManager.App/Views/BookDetailsView.xaml`
- Create: `src/EbookManager.App/Views/SettingsPage.xaml`
- Create: `src/EbookManager.App/Views/ImportResultPage.xaml`
- Create: `src/EbookManager.App/Resources/Images/add_book.svg`
- Create: `src/EbookManager.App/Resources/Images/scan_folder.svg`
- Create: `src/EbookManager.App/Resources/Images/new_library.svg`
- Create: `src/EbookManager.App/Resources/Images/open_library.svg`
- Create: `src/EbookManager.App/Resources/Images/settings.svg`
- Create: `src/EbookManager.App/Resources/Images/save.svg`
- Create: `src/EbookManager.App/Resources/Images/undo.svg`
- Create: `src/EbookManager.App/Resources/Images/delete.svg`

- [ ] **Step 1: Replace the template page with the approved four-region shell**

Build `MainPage.xaml` with:

```text
Grid rows: toolbar, secondary view/search bar, workspace, status bar
Workspace columns: 220px filter placeholder, star-sized library, 320px details
```

Bind toolbar commands for add, scan, new library, open library, and settings. Add SVG toolbar icons with localized tooltips. The left panel contains the visible Milestone 1 filter headings as disabled expansion rows with a localized future-version hint.

Implement `UserInteractionService` as the MAUI adapter for `IUserInteractionService`: delegate file and folder selection to picker services, show the delete confirmation through `DeleteConfirmationService`, and navigate to `ImportResultPage`.

- [ ] **Step 2: Implement the three view controls**

`BookshelfView`: `CollectionView` with cover, two-line title, and two-line authors.

`DetailedGridView`: Syncfusion `SfDataGrid` with cover template column, title, authors, reading status, and localized e-reader unavailable value.

`ListView`: `CollectionView` rows with title, authors, reading status, and e-reader unavailable value without cover.

All views bind to the same `VisibleBooks` collection and `SelectedBook`.

- [ ] **Step 3: Implement the editable details panel**

Bind cover, title, authors, description, language, publisher, publication date, tags, series, series number, ISBN, and reading status. Show the unsaved banner only when `HasUnsavedChanges` is true. Wire save, undo, and delete commands.

- [ ] **Step 4: Add settings and import result pages**

Settings exposes language, theme, default startup view, and reset-delete-confirmation behavior.

Import results list each file with localized outcome and message and show counts for added, exact duplicate, possible duplicate, and failed.

- [ ] **Step 5: Build and commit**

```powershell
dotnet build src/EbookManager.App/EbookManager.App.csproj -f net10.0-windows10.0.19041.0
git add src/EbookManager.App
git commit -m "Build the native MAUI library workspace"
```

Expected: build succeeds.

## Task 12: Wire End-to-End Flows and Run Verification

**Files:**
- Modify: `src/EbookManager.App/App.xaml.cs`
- Modify: `src/EbookManager.Presentation/ViewModels/LibraryViewModel.cs`
- Modify: `src/EbookManager.Presentation/ViewModels/BookDetailsViewModel.cs`
- Create: `tests/EbookManager.Tests/Integration/VerticalSliceTests.cs`
- Create: `docs/manual-tests/milestone-1-checklist.md`

- [ ] **Step 1: Write the end-to-end integration test**

The test must:

```text
1. Create a temporary ELibrary.
2. Generate a minimal EPUB fixture with metadata and cover.
3. Import it through ImportService.
4. Reopen the SQLite library through a new context.
5. Search for the author.
6. Edit title and reading status through BookService.
7. Assert SQLite is authoritative when write-back is Unsupported.
8. Delete the book.
9. Assert both database record and managed directory are gone.
```

- [ ] **Step 2: Run the integration test to verify any remaining failure**

```powershell
dotnet test tests/EbookManager.Tests --filter FullyQualifiedName~VerticalSliceTests
```

Expected before final wiring: FAIL if a service composition gap remains.

- [ ] **Step 3: Complete startup and command wiring**

On startup: load settings, apply language and theme, attempt to reopen the last valid library, migrate SQLite, and refresh the displayed book collection.

For toolbar actions: open MAUI picker services, call library/import services, refresh visible books, and navigate to import results.

For save, undo, and delete: call application services, respect confirmation behavior, refresh visible books, and preserve cleanup warnings for display.

- [ ] **Step 4: Create the manual checklist**

Create `docs/manual-tests/milestone-1-checklist.md` with:

```markdown
# Milestone 1 Manual Verification

- [ ] Launch the Windows app without a library and create `ELibrary`.
- [ ] Restart and confirm the last library reopens.
- [ ] Import multiple supported files from the file picker.
- [ ] Scan a directory once without and once with subdirectories.
- [ ] Confirm exact duplicates and possible duplicates appear in results.
- [ ] Switch between bookshelf, detailed, and list views.
- [ ] Search by title and author.
- [ ] Edit metadata, confirm the unsaved banner, undo, edit again, and save.
- [ ] Switch between Dutch and English.
- [ ] Switch between light and dark.
- [ ] Delete a book, remember the confirmation answer, then restore prompts in Settings.
- [ ] Confirm the status bar shows assembly version and displayed book count.
```

- [ ] **Step 5: Run automated verification**

```powershell
dotnet test EbookManager.sln
dotnet build src/EbookManager.App/EbookManager.App.csproj -f net10.0-windows10.0.19041.0
```

Expected: all tests pass and build succeeds.

- [ ] **Step 6: Launch the Windows app and execute the manual checklist**

```powershell
dotnet run --project src/EbookManager.App/EbookManager.App.csproj -f net10.0-windows10.0.19041.0
```

Expected: native Windows MAUI window opens without a web server.

- [ ] **Step 7: Commit**

```powershell
git add src tests docs/manual-tests
git commit -m "Complete the Ebook Manager vertical slice"
```

## Task 13: Final Review Before Milestone 2

**Files:**
- Create: `README.md`

- [ ] **Step 1: Run a clean verification pass**

```powershell
dotnet test EbookManager.sln
dotnet build src/EbookManager.App/EbookManager.App.csproj -f net10.0-windows10.0.19041.0
git status --short
```

Expected: tests pass, build succeeds, and the worktree contains only intentional changes.

- [ ] **Step 2: Document the tested vertical slice**

Create or update `README.md` with prerequisites, `SYNCFUSION_LICENSE_KEY`, restore/build/run commands, supported Milestone 1 formats, and the explicit Milestone 2 exclusions.

- [ ] **Step 3: Commit documentation**

```powershell
git add README.md
git commit -m "Document the Milestone 1 desktop app"
```

- [ ] **Step 4: Stop and review**

Do not begin Milestone 2 automatically. Review the working Windows application and decide whether drag-and-drop, faceted filters, search highlighting, or adapter depth should be prioritized first.
