# Ebook Manager Milestone 3 Calibre Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Calibre `metadata.opf` sidecar import and conservative metadata cleanup so real imported libraries immediately populate title, authors, tags, series, language, publisher, description, ISBN, and numeric series numbers.

**Architecture:** Add focused metadata infrastructure that reads Calibre OPF sidecars and normalizes merged metadata. Keep `ImportService` as the orchestration point, but move sidecar precedence and cleanup into a small resolver so import remains readable and testable. SQLite stays authoritative after import, and Ebook Manager's `metadata.json` sidecar remains the highest-precedence portable correction format.

**Tech Stack:** .NET 10, C#, WPF app shell, application/infrastructure/domain layering, EF Core SQLite, xUnit, FluentAssertions, XML parsing with `System.Xml.Linq`.

---

## File Structure

- Create `src/EbookManager.Domain/Abstractions/IMetadataSourceResolver.cs`
  - Field-level metadata resolver used by `ImportService`.
- Create `src/EbookManager.Infrastructure/Metadata/CalibreOpfMetadataSidecarStore.cs`
  - Reads sibling `metadata.opf` files and returns `MetadataReadResult?`.
- Create `src/EbookManager.Infrastructure/Metadata/BookMetadataCleaner.cs`
  - Applies bracketed title parsing and simple author normalization.
- Create `src/EbookManager.Infrastructure/Metadata/MetadataSourceResolver.cs`
  - Merges embedded adapter metadata, Ebook Manager `metadata.json`, Calibre `metadata.opf`, and cleanup.
- Modify `src/EbookManager.Application/Importing/ImportService.cs`
  - Replace direct sidecar handling with `IMetadataSourceResolver`.
- Modify `src/EbookManager.App/App.xaml.cs`
  - Register new metadata services in DI.
- Modify `tests/EbookManager.Tests/TestSupport/ImportServiceFixture.cs`
  - Register the new resolver for integration-style import tests.
- Modify `tests/EbookManager.Tests/Importing/ImportPrimitivesTests.cs`
  - Add tests for Calibre OPF parsing and metadata cleanup.
- Modify `tests/EbookManager.Tests/Importing/ImportServiceTests.cs`
  - Add precedence and scan/import integration tests.
- Create `docs/manual-tests/milestone-3-checklist.md`
  - Manual test list for Calibre-style imports.
- Modify `README.md`
  - Document Milestone 3 capabilities and metadata precedence.

---

### Task 1: Add Calibre OPF Sidecar Reader

**Files:**
- Create: `src/EbookManager.Infrastructure/Metadata/CalibreOpfMetadataSidecarStore.cs`
- Test: `tests/EbookManager.Tests/Importing/ImportPrimitivesTests.cs`

- [ ] **Step 1: Write failing OPF mapping test**

Add this test to `ImportPrimitivesTests`:

```csharp
[Fact]
public async Task Calibre_opf_sidecar_reads_core_metadata_fields()
{
    var bookPath = WriteBytesFile("Calibre/Triptiek/Triptiek.epub", [1, 2, 3]);
    File.WriteAllText(
        Path.Combine(Path.GetDirectoryName(bookPath)!, "metadata.opf"),
        """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <metadata>
            <dc:title>Triptiek</dc:title>
            <dc:creator>Karin Slaughter</dc:creator>
            <dc:description>Een Atlanta-thriller.</dc:description>
            <dc:language>nl</dc:language>
            <dc:publisher>Cargo</dc:publisher>
            <dc:date>2006-01-02</dc:date>
            <dc:identifier opf:scheme="ISBN" xmlns:opf="http://www.idpf.org/2007/opf">9789023423456</dc:identifier>
            <dc:subject>Thriller</dc:subject>
            <dc:subject>Crime</dc:subject>
            <meta name="calibre:series" content="Atlanta" />
            <meta name="calibre:series_index" content="1" />
          </metadata>
        </package>
        """);
    var store = new CalibreOpfMetadataSidecarStore();

    var result = await store.TryReadAsync(bookPath, default);

    result.Should().NotBeNull();
    result!.Metadata.Title.Should().Be("Triptiek");
    result.Metadata.Authors.Should().Equal("Karin Slaughter");
    result.Metadata.Description.Should().Be("Een Atlanta-thriller.");
    result.Metadata.Language.Should().Be("nl");
    result.Metadata.Publisher.Should().Be("Cargo");
    result.Metadata.PublicationDate.Should().Be(new DateOnly(2006, 1, 2));
    result.Metadata.Isbn.Should().Be("9789023423456");
    result.Metadata.Tags.Should().Equal("Thriller", "Crime");
    result.Metadata.Series.Should().Be("Atlanta");
    result.Metadata.SeriesNumber.Should().Be(1);
    result.Warning.Should().BeNull();
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test tests/EbookManager.Tests --no-restore --filter FullyQualifiedName~Calibre_opf_sidecar_reads_core_metadata_fields
```

Expected: compile failure because `CalibreOpfMetadataSidecarStore` does not exist.

- [ ] **Step 3: Implement OPF sidecar store**

Create `CalibreOpfMetadataSidecarStore.cs`:

```csharp
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;

namespace EbookManager.Infrastructure.Metadata;

public sealed class CalibreOpfMetadataSidecarStore
{
    public const string FileName = "metadata.opf";

    public async Task<MetadataReadResult?> TryReadAsync(
        string bookFilePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(Path.GetFullPath(bookFilePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var opfPath = Path.Combine(directory, FileName);
        if (!File.Exists(opfPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                opfPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            var metadataElement = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "metadata");

            if (metadataElement is null)
            {
                return new MetadataReadResult(
                    new BookMetadata(Path.GetFileNameWithoutExtension(bookFilePath), ["Unknown"]),
                    "Calibre OPF metadata section is missing.");
            }

            var title = FirstElementValue(metadataElement, "title")
                ?? Path.GetFileNameWithoutExtension(bookFilePath);
            var authors = ElementValues(metadataElement, "creator").ToArray();
            if (authors.Length == 0)
            {
                authors = ["Unknown"];
            }

            var tags = ElementValues(metadataElement, "subject")
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new MetadataReadResult(
                new BookMetadata(
                    title,
                    authors,
                    FirstElementValue(metadataElement, "description"),
                    FirstElementValue(metadataElement, "language"),
                    FirstElementValue(metadataElement, "publisher"),
                    ParseDate(FirstElementValue(metadataElement, "date")),
                    tags.Length > 0 ? tags : null,
                    MetaContent(metadataElement, "calibre:series"),
                    ParseSeriesNumber(MetaContent(metadataElement, "calibre:series_index")),
                    FirstIsbn(metadataElement)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            return new MetadataReadResult(
                new BookMetadata(Path.GetFileNameWithoutExtension(bookFilePath), ["Unknown"]),
                $"Calibre OPF ignored: {exception.GetType().Name}");
        }
    }

    private static string? FirstElementValue(XElement element, string localName) =>
        ElementValues(element, localName).FirstOrDefault();

    private static IEnumerable<string> ElementValues(XElement element, string localName) =>
        element
            .Elements()
            .Where(child => child.Name.LocalName == localName)
            .Select(child => child.Value.Trim())
            .Where(value => value.Length > 0);

    private static string? MetaContent(XElement metadataElement, string name) =>
        metadataElement
            .Elements()
            .Where(child => child.Name.LocalName == "meta")
            .FirstOrDefault(child => string.Equals(child.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("content")
            ?.Value
            .Trim() is { Length: > 0 } value
                ? value
                : null;

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static decimal? ParseSeriesNumber(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string? FirstIsbn(XElement metadataElement)
    {
        foreach (var identifier in metadataElement.Elements().Where(child => child.Name.LocalName == "identifier"))
        {
            var value = identifier.Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            var scheme = identifier
                .Attributes()
                .FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("scheme", StringComparison.OrdinalIgnoreCase) &&
                    attribute.Value.Equals("ISBN", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(scheme) || value.Contains("isbn", StringComparison.OrdinalIgnoreCase))
            {
                return value.Replace("ISBN:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            }
        }

        return metadataElement
            .Elements()
            .FirstOrDefault(child => child.Name.LocalName == "identifier" && child.Value.Trim().Length > 0)
            ?.Value
            .Trim();
    }
}
```

- [ ] **Step 4: Run OPF primitive tests**

Run:

```powershell
dotnet test tests/EbookManager.Tests --no-restore --filter FullyQualifiedName~Calibre_opf_sidecar
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/EbookManager.Infrastructure/Metadata/CalibreOpfMetadataSidecarStore.cs tests/EbookManager.Tests/Importing/ImportPrimitivesTests.cs
git commit -m "Add Calibre OPF metadata reader"
```

---

### Task 2: Add Conservative Metadata Cleanup

**Files:**
- Create: `src/EbookManager.Infrastructure/Metadata/BookMetadataCleaner.cs`
- Test: `tests/EbookManager.Tests/Importing/ImportPrimitivesTests.cs`

- [ ] **Step 1: Write failing cleanup tests**

Add these tests to `ImportPrimitivesTests`:

```csharp
[Fact]
public void Metadata_cleaner_extracts_bracketed_series_title_and_number()
{
    var metadata = new BookMetadata("[Atlanta 01] - Triptiek", ["Slaughter, Karin"]);

    var cleaned = BookMetadataCleaner.Clean(metadata);

    cleaned.Title.Should().Be("Triptiek");
    cleaned.Series.Should().Be("Atlanta");
    cleaned.SeriesNumber.Should().Be(1);
    cleaned.Authors.Should().Equal("Karin Slaughter");
}

[Fact]
public void Metadata_cleaner_does_not_overwrite_explicit_series_values()
{
    var metadata = new BookMetadata(
        "[Other 99] - Triptiek",
        ["Karin Slaughter"],
        Series: "Atlanta",
        SeriesNumber: 1);

    var cleaned = BookMetadataCleaner.Clean(metadata);

    cleaned.Title.Should().Be("Triptiek");
    cleaned.Series.Should().Be("Atlanta");
    cleaned.SeriesNumber.Should().Be(1);
}

[Theory]
[InlineData("[Atlanta XX] - Triptiek")]
[InlineData("[Atlanta] - Triptiek")]
[InlineData("[Atlanta 01]")]
public void Metadata_cleaner_ignores_ambiguous_bracketed_titles(string title)
{
    var metadata = new BookMetadata(title, ["Author"]);

    var cleaned = BookMetadataCleaner.Clean(metadata);

    cleaned.Title.Should().Be(title);
    cleaned.Series.Should().BeNull();
    cleaned.SeriesNumber.Should().BeNull();
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests/EbookManager.Tests --no-restore --filter FullyQualifiedName~Metadata_cleaner
```

Expected: compile failure because `BookMetadataCleaner` does not exist.

- [ ] **Step 3: Implement metadata cleaner**

Create `BookMetadataCleaner.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using EbookManager.Domain.Books;

namespace EbookManager.Infrastructure.Metadata;

public static partial class BookMetadataCleaner
{
    public static BookMetadata Clean(BookMetadata metadata)
    {
        var title = metadata.Title.Trim();
        var series = NormalizeBlank(metadata.Series);
        var seriesNumber = metadata.SeriesNumber;

        if (TryParseBracketedSeries(title, out var parsedTitle, out var parsedSeries, out var parsedNumber))
        {
            title = parsedTitle;
            series ??= parsedSeries;
            seriesNumber ??= parsedNumber;
        }

        var authors = metadata.Authors
            .Select(NormalizeAuthor)
            .Where(author => author.Length > 0)
            .ToArray();

        if (authors.Length == 0)
        {
            authors = ["Unknown"];
        }

        return new BookMetadata(
            title,
            authors,
            NormalizeBlank(metadata.Description),
            NormalizeBlank(metadata.Language),
            NormalizeBlank(metadata.Publisher),
            metadata.PublicationDate,
            metadata.Tags?.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.Ordinal).ToArray(),
            series,
            seriesNumber,
            NormalizeBlank(metadata.Isbn),
            metadata.CoverBytes);
    }

    private static bool TryParseBracketedSeries(
        string title,
        out string parsedTitle,
        out string parsedSeries,
        out decimal parsedNumber)
    {
        parsedTitle = title;
        parsedSeries = string.Empty;
        parsedNumber = 0;

        var match = BracketedTitleRegex().Match(title);
        if (!match.Success)
        {
            return false;
        }

        var seriesPart = match.Groups["series"].Value.Trim();
        var numberPart = match.Groups["number"].Value.Trim();
        var titlePart = match.Groups["title"].Value.Trim();

        if (titlePart.Length == 0 ||
            seriesPart.Length == 0 ||
            !decimal.TryParse(numberPart, NumberStyles.Number, CultureInfo.InvariantCulture, out parsedNumber))
        {
            return false;
        }

        parsedTitle = titlePart;
        parsedSeries = seriesPart;
        return true;
    }

    private static string NormalizeAuthor(string author)
    {
        var trimmed = author.Trim();
        var commaIndex = trimmed.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex < 0 || commaIndex != trimmed.LastIndexOf(',', StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lastName = trimmed[..commaIndex].Trim();
        var firstName = trimmed[(commaIndex + 1)..].Trim();
        return lastName.Length > 0 && firstName.Length > 0
            ? $"{firstName} {lastName}"
            : trimmed;
    }

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^\[(?<series>.+?)\s+(?<number>\d+(?:\.\d+)?)\]\s*[-:\s]\s*(?<title>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedTitleRegex();
}
```

- [ ] **Step 4: Run cleanup tests**

Run:

```powershell
dotnet test tests/EbookManager.Tests --no-restore --filter FullyQualifiedName~Metadata_cleaner
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/EbookManager.Infrastructure/Metadata/BookMetadataCleaner.cs tests/EbookManager.Tests/Importing/ImportPrimitivesTests.cs
git commit -m "Add conservative metadata cleanup"
```

---

### Task 3: Integrate Metadata Source Resolution Into Import

**Files:**
- Create: `src/EbookManager.Domain/Abstractions/IMetadataSourceResolver.cs`
- Create: `src/EbookManager.Infrastructure/Metadata/MetadataSourceResolver.cs`
- Modify: `src/EbookManager.Application/Importing/ImportService.cs`
- Modify: `src/EbookManager.App/App.xaml.cs`
- Modify: `tests/EbookManager.Tests/TestSupport/ImportServiceFixture.cs`
- Test: `tests/EbookManager.Tests/Importing/ImportServiceTests.cs`

- [ ] **Step 1: Write failing precedence tests**

Add these tests to `ImportServiceTests`:

```csharp
[Fact]
public async Task Import_async_prefers_calibre_opf_over_embedded_metadata_for_text_fields()
{
    await using var fixture = await ImportServiceFixture.CreateAsync();
    var service = fixture.CreateService();
    var source = fixture.WriteBytesFile(
        @"incoming\Wrong Title - Wrong Author.pdf",
        Encoding.UTF8.GetBytes("opf-import"));
    File.WriteAllText(
        Path.Combine(Path.GetDirectoryName(source)!, "metadata.opf"),
        """
        <package xmlns:dc="http://purl.org/dc/elements/1.1/">
          <metadata>
            <dc:title>Correct Title</dc:title>
            <dc:creator>Correct Author</dc:creator>
            <dc:subject>Imported tag</dc:subject>
            <meta name="calibre:series" content="Series Name" />
            <meta name="calibre:series_index" content="2" />
          </metadata>
        </package>
        """);

    var result = await service.ImportAsync([source], default);

    var book = await fixture.BookRepository.GetAsync(result.Items.Single().BookId!.Value, default);
    book!.Metadata.Title.Should().Be("Correct Title");
    book.Metadata.Authors.Should().Equal("Correct Author");
    book.Metadata.Tags.Should().Equal("Imported tag");
    book.Metadata.Series.Should().Be("Series Name");
    book.Metadata.SeriesNumber.Should().Be(2);
    result.Items.Single().Message.Should().Contain("calibre opf");
}

[Fact]
public async Task Import_async_prefers_json_sidecar_over_calibre_opf()
{
    await using var fixture = await ImportServiceFixture.CreateAsync();
    var sidecars = new ReturningMetadataSidecarStore(
        new BookMetadata("Json Title", ["Json Author"], Tags: ["Json tag"]));
    var service = fixture.CreateService(metadataSidecarStore: sidecars);
    var source = fixture.WriteBytesFile(@"incoming\Book.pdf", Encoding.UTF8.GetBytes("json-over-opf"));
    File.WriteAllText(
        Path.Combine(Path.GetDirectoryName(source)!, "metadata.opf"),
        """
        <package xmlns:dc="http://purl.org/dc/elements/1.1/">
          <metadata>
            <dc:title>Opf Title</dc:title>
            <dc:creator>Opf Author</dc:creator>
          </metadata>
        </package>
        """);

    var result = await service.ImportAsync([source], default);

    var book = await fixture.BookRepository.GetAsync(result.Items.Single().BookId!.Value, default);
    book!.Metadata.Title.Should().Be("Json Title");
    book.Metadata.Authors.Should().Equal("Json Author");
    book.Metadata.Tags.Should().Equal("Json tag");
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests/EbookManager.Tests --no-restore --filter "FullyQualifiedName~Import_async_prefers_calibre_opf|FullyQualifiedName~Import_async_prefers_json_sidecar"
```

Expected: at least the Calibre OPF test fails because import does not read OPF sidecars.

- [ ] **Step 3: Add metadata source resolver interface**

Create `IMetadataSourceResolver.cs`:

```csharp
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;

namespace EbookManager.Domain.Abstractions;

public interface IMetadataSourceResolver
{
    Task<MetadataReadResult> ReadAsync(
        string sourcePath,
        EbookFormat format,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement metadata source resolver**

Create `MetadataSourceResolver.cs`:

```csharp
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;

namespace EbookManager.Infrastructure.Metadata;

public sealed class MetadataSourceResolver(
    IMetadataAdapterResolver metadataAdapterResolver,
    IMetadataSidecarStore? jsonSidecarStore,
    CalibreOpfMetadataSidecarStore calibreOpfSidecarStore) : IMetadataSourceResolver
{
    public async Task<MetadataReadResult> ReadAsync(
        string sourcePath,
        EbookFormat format,
        CancellationToken cancellationToken)
    {
        var embedded = await metadataAdapterResolver.Resolve(format).ReadAsync(sourcePath, format, cancellationToken);
        var opf = await calibreOpfSidecarStore.TryReadAsync(sourcePath, cancellationToken);
        var json = jsonSidecarStore is null
            ? null
            : await jsonSidecarStore.TryReadAsync(sourcePath, cancellationToken);

        var merged = embedded.Metadata;
        var sourceMessage = embedded.Warning;

        if (opf is not null)
        {
            merged = Merge(embedded.Metadata, opf.Metadata);
            sourceMessage = AppendWarning(sourceMessage, opf.Warning ?? "metadata source: calibre opf");
        }

        if (json is not null)
        {
            merged = Merge(merged, json);
            sourceMessage = AppendWarning(sourceMessage, "metadata source: metadata json");
        }

        return new MetadataReadResult(BookMetadataCleaner.Clean(merged), sourceMessage);
    }

    private static BookMetadata Merge(BookMetadata lowerPriority, BookMetadata higherPriority) =>
        new(
            Prefer(higherPriority.Title, lowerPriority.Title),
            higherPriority.Authors.Count > 0 ? higherPriority.Authors : lowerPriority.Authors,
            Prefer(higherPriority.Description, lowerPriority.Description),
            Prefer(higherPriority.Language, lowerPriority.Language),
            Prefer(higherPriority.Publisher, lowerPriority.Publisher),
            higherPriority.PublicationDate ?? lowerPriority.PublicationDate,
            higherPriority.Tags is { Count: > 0 } ? higherPriority.Tags : lowerPriority.Tags,
            Prefer(higherPriority.Series, lowerPriority.Series),
            higherPriority.SeriesNumber ?? lowerPriority.SeriesNumber,
            Prefer(higherPriority.Isbn, lowerPriority.Isbn),
            higherPriority.CoverBytes ?? lowerPriority.CoverBytes);

    private static string Prefer(string? higherPriority, string? lowerPriority) =>
        string.IsNullOrWhiteSpace(higherPriority)
            ? lowerPriority ?? string.Empty
            : higherPriority.Trim();

    private static string? AppendWarning(string? current, string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return current;
        }

        return string.IsNullOrWhiteSpace(current)
            ? next
            : $"{current}; {next}";
    }
}
```

- [ ] **Step 5: Modify ImportService constructor and metadata read**

In `ImportService.cs`, replace the constructor dependency pair:

```csharp
IMetadataAdapterResolver metadataAdapterResolver,
IImportExceptionClassifier exceptionClassifier,
IMetadataSidecarStore? metadataSidecarStore = null)
```

with:

```csharp
IMetadataSourceResolver metadataSourceResolver,
IImportExceptionClassifier exceptionClassifier)
```

Replace the private fields:

```csharp
private readonly IMetadataAdapterResolver metadataAdapterResolver = metadataAdapterResolver;
private readonly IMetadataSidecarStore? metadataSidecarStore = metadataSidecarStore;
```

with:

```csharp
private readonly IMetadataSourceResolver metadataSourceResolver = metadataSourceResolver;
```

Replace the metadata read block:

```csharp
metadata = await metadataAdapterResolver.Resolve(format).ReadAsync(sourcePath!, format, cancellationToken);
var sidecarMetadata = metadataSidecarStore is null
    ? null
    : await metadataSidecarStore.TryReadAsync(sourcePath!, cancellationToken);
if (sidecarMetadata is not null)
{
    metadata = metadata with { Metadata = sidecarMetadata };
}
```

with:

```csharp
metadata = await metadataSourceResolver.ReadAsync(sourcePath!, format, cancellationToken);
```

- [ ] **Step 6: Update DI registration**

In `App.xaml.cs`, keep existing adapter and sidecar registrations, then add:

```csharp
services.AddSingleton<CalibreOpfMetadataSidecarStore>();
services.AddSingleton<IMetadataSourceResolver, MetadataSourceResolver>();
```

Ensure `ImportService` construction uses the registered `IMetadataSourceResolver` automatically.

- [ ] **Step 7: Update test fixture service creation**

In `ImportServiceFixture.cs`, update `CreateService` so it can accept an optional `IMetadataSidecarStore`:

```csharp
public ImportService CreateService(
    IBookRepository? bookRepository = null,
    IMetadataSidecarStore? metadataSidecarStore = null)
{
    var metadataSourceResolver = new MetadataSourceResolver(
        MetadataAdapterResolver,
        metadataSidecarStore ?? new JsonMetadataSidecarStore(),
        new CalibreOpfMetadataSidecarStore());

    return new ImportService(
        bookRepository ?? BookRepository,
        ImportRepository,
        FileStore,
        FileHasher,
        metadataSourceResolver,
        ExceptionClassifier);
}
```

For tests that manually construct `ImportService`, replace resolver arguments with:

```csharp
new MetadataSourceResolver(
    fixture.MetadataAdapterResolver,
    null,
    new CalibreOpfMetadataSidecarStore())
```

When a test uses a custom `IMetadataAdapterResolver`, wrap that custom resolver in `MetadataSourceResolver`.

- [ ] **Step 8: Run import precedence tests**

Run:

```powershell
dotnet test tests/EbookManager.Tests --no-restore --filter "FullyQualifiedName~Import_async_prefers_calibre_opf|FullyQualifiedName~Import_async_prefers_json_sidecar"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```powershell
git add src/EbookManager.Domain/Abstractions/IMetadataSourceResolver.cs src/EbookManager.Infrastructure/Metadata/MetadataSourceResolver.cs src/EbookManager.Application/Importing/ImportService.cs src/EbookManager.App/App.xaml.cs tests/EbookManager.Tests/TestSupport/ImportServiceFixture.cs tests/EbookManager.Tests/Importing/ImportServiceTests.cs
git commit -m "Integrate Calibre metadata source resolution"
```

---

### Task 4: Harden OPF Edge Cases And Directory Scan Behavior

**Files:**
- Modify: `src/EbookManager.Infrastructure/Metadata/CalibreOpfMetadataSidecarStore.cs`
- Modify: `src/EbookManager.Infrastructure/Metadata/MetadataSourceResolver.cs`
- Test: `tests/EbookManager.Tests/Importing/ImportPrimitivesTests.cs`
- Test: `tests/EbookManager.Tests/Importing/ImportServiceTests.cs`

- [ ] **Step 1: Write failing edge-case tests**

Add these tests:

```csharp
[Fact]
public async Task Calibre_opf_sidecar_returns_warning_for_malformed_opf_without_throwing()
{
    var bookPath = WriteBytesFile("Calibre/Broken/Broken.epub", [1, 2, 3]);
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(bookPath)!, "metadata.opf"), "<package>");
    var store = new CalibreOpfMetadataSidecarStore();

    var result = await store.TryReadAsync(bookPath, default);

    result.Should().NotBeNull();
    result!.Metadata.Title.Should().Be("Broken");
    result.Metadata.Authors.Should().Equal("Unknown");
    result.Warning.Should().Contain("Calibre OPF ignored");
}

[Fact]
public async Task Calibre_opf_sidecar_ignores_non_numeric_series_index()
{
    var bookPath = WriteBytesFile("Calibre/Series/Book.epub", [1, 2, 3]);
    File.WriteAllText(
        Path.Combine(Path.GetDirectoryName(bookPath)!, "metadata.opf"),
        """
        <package xmlns:dc="http://purl.org/dc/elements/1.1/">
          <metadata>
            <dc:title>Book</dc:title>
            <dc:creator>Author</dc:creator>
            <meta name="calibre:series" content="Series" />
            <meta name="calibre:series_index" content="one" />
          </metadata>
        </package>
        """);
    var store = new CalibreOpfMetadataSidecarStore();

    var result = await store.TryReadAsync(bookPath, default);

    result!.Metadata.Series.Should().Be("Series");
    result.Metadata.SeriesNumber.Should().BeNull();
}

[Fact]
public async Task Directory_scan_import_associates_sibling_calibre_opf()
{
    await using var fixture = await ImportServiceFixture.CreateAsync();
    var root = Path.Combine(fixture.WorkspacePath, "CalibreLibrary");
    var bookDirectory = Path.Combine(root, "Author", "Book");
    Directory.CreateDirectory(bookDirectory);
    var source = Path.Combine(bookDirectory, "Book.pdf");
    File.WriteAllBytes(source, Encoding.UTF8.GetBytes("scan-opf"));
    File.WriteAllText(
        Path.Combine(bookDirectory, "metadata.opf"),
        """
        <package xmlns:dc="http://purl.org/dc/elements/1.1/">
          <metadata>
            <dc:title>Scanned OPF Title</dc:title>
            <dc:creator>Scanned Author</dc:creator>
            <dc:subject>Scanned Tag</dc:subject>
          </metadata>
        </package>
        """);
    var scanner = new DirectoryScanner();
    var sources = scanner.Scan(root, recursive: true);

    var result = await fixture.CreateService().ImportAsync(sources, default);

    var book = await fixture.BookRepository.GetAsync(result.Items.Single().BookId!.Value, default);
    book!.Metadata.Title.Should().Be("Scanned OPF Title");
    book.Metadata.Authors.Should().Equal("Scanned Author");
    book.Metadata.Tags.Should().Equal("Scanned Tag");
}
```

- [ ] **Step 2: Run edge-case tests**

Run:

```powershell
dotnet test tests/EbookManager.Tests --no-restore --filter "FullyQualifiedName~Calibre_opf_sidecar|FullyQualifiedName~Directory_scan_import_associates"
```

Expected: PASS after Task 3; if a test fails, fix only the responsible parser or resolver behavior.

- [ ] **Step 3: Fix warning and fallback details if needed**

If malformed OPF title includes `.epub`, update fallback construction in `CalibreOpfMetadataSidecarStore` to use:

```csharp
private static BookMetadata FallbackMetadata(string bookFilePath) =>
    new(Path.GetFileNameWithoutExtension(bookFilePath), ["Unknown"]);
```

Then replace both inline malformed fallback constructions with:

```csharp
FallbackMetadata(bookFilePath)
```

- [ ] **Step 4: Run all import tests**

Run:

```powershell
dotnet test tests/EbookManager.Tests --no-restore --filter FullyQualifiedName~EbookManager.Tests.Importing
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/EbookManager.Infrastructure/Metadata/CalibreOpfMetadataSidecarStore.cs src/EbookManager.Infrastructure/Metadata/MetadataSourceResolver.cs tests/EbookManager.Tests/Importing/ImportPrimitivesTests.cs tests/EbookManager.Tests/Importing/ImportServiceTests.cs
git commit -m "Harden Calibre OPF import behavior"
```

---

### Task 5: Documentation, Manual Checklist, And Full Verification

**Files:**
- Create: `docs/manual-tests/milestone-3-checklist.md`
- Modify: `README.md`

- [ ] **Step 1: Add Milestone 3 manual checklist**

Create `docs/manual-tests/milestone-3-checklist.md`:

```markdown
# Milestone 3 Manual Test Checklist

Use this checklist for Calibre metadata import testing.

## Calibre-Style Import

- Create or select a test library.
- Scan a folder where each book directory contains one supported ebook file and a sibling `metadata.opf`.
- Confirm imported title, authors, description, language, publisher, ISBN, tags, series, and series number match the OPF metadata.
- Confirm tags and series appear immediately in the left filters.
- Confirm search finds values imported from OPF metadata.
- Confirm bookshelf, detailed grid, list view, and details pane show the imported metadata.

## Precedence

- Import a book with only embedded metadata and confirm existing behavior still works.
- Import a book with embedded metadata plus `metadata.opf` and confirm OPF text metadata wins.
- Import a book with `metadata.json` plus `metadata.opf` and confirm `metadata.json` wins.
- Confirm cover extraction still works for EPUB when text metadata comes from `metadata.opf`.

## Cleanup Rules

- Import `[Atlanta 01] - Triptiek.epub` without stronger series metadata and confirm title `Triptiek`, series `Atlanta`, and series number `1`.
- Import a book with author `Slaughter, Karin` and confirm the author is stored as `Karin Slaughter`.
- Import a book with a non-numeric OPF series index and confirm the series number remains empty.

## Regression

- Drag and drop normal files without OPF sidecars.
- Add books through the toolbar.
- Scan with subdirectories disabled and enabled.
- Confirm duplicate and possible-duplicate results are still reported clearly.
```

- [ ] **Step 2: Update README current status and import section**

In `README.md`, update Current Status from Milestone 2/version `0.1` to active Milestone 3 work when implementation is complete. Add bullets:

```markdown
- Calibre `metadata.opf` sidecar import
- conservative title, author, and series cleanup
```

In Supported Import Formats metadata section, replace the precedence paragraph with:

```markdown
During import, metadata is resolved in this order:

1. Ebook Manager `metadata.json` sidecar next to the source file.
2. Calibre `metadata.opf` sidecar next to the source file.
3. Embedded format metadata, strongest for EPUB and KEPUB.
4. Filename fallback.
```

Also update the Manual Verification link to mention both Milestone 2 and Milestone 3 checklists.

- [ ] **Step 3: Run full automated tests**

Run:

```powershell
dotnet test EbookManager.sln --no-restore
```

Expected: all tests pass.

- [ ] **Step 4: Run release build**

Run:

```powershell
dotnet build EbookManager.sln -c Release --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 5: Commit docs and verification-ready state**

```powershell
git add README.md docs/manual-tests/milestone-3-checklist.md
git commit -m "Document Milestone 3 Calibre import testing"
```

- [ ] **Step 6: Final status check**

Run:

```powershell
git status --short
git log --oneline -5
```

Expected: only unrelated user/build-version changes may remain. Report test/build results and latest commit hashes to the user.

---

## Self-Review

- Spec coverage: OPF sidecar import, metadata precedence, field-level merge, bracketed title cleanup, author normalization, non-numeric series handling, scan association, import messages, tests, README, and manual checklist are all covered.
- Scope: Kobo/e-reader sync, native ebook write-back, custom views, full-text search, and GitHub issue reporting are excluded as specified.
- Type consistency: New `IMetadataSourceResolver` returns the existing `MetadataReadResult`; `ImportService` remains responsible for duplicate detection, copying, persistence, and result recording.
- Risk: The plan deliberately avoids automatic multi-format merging because the current UI has no merge/review workflow.
