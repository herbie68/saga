using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using EbookManager.Application.Importing;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Books;
using EbookManager.Domain.Metadata;
using EbookManager.Infrastructure.Files;
using EbookManager.Infrastructure.Metadata;
using EbookManager.Tests.TestSupport;
using FluentAssertions;

namespace EbookManager.Tests.Importing;

public sealed class ImportPrimitivesTests : IDisposable
{
    private readonly TemporaryDirectory temporaryDirectory = new();

    [Fact]
    public void Scanner_respects_recursive_flag_and_sorts_matches()
    {
        var root = temporaryDirectory.DirectoryPath;
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);

        var first = WriteTextFile(Path.Combine(root, "alpha.epub"));
        var second = WriteTextFile(Path.Combine(nested, "zeta.epub"));
        WriteTextFile(Path.Combine(root, "ignore.txt"));

        var scanner = new DirectoryScanner();

        scanner.Scan(root, recursive: false).Should().Equal(first);
        scanner.Scan(root, recursive: true).Should().Equal(first, second);
    }

    [Fact]
    public void Scanner_observes_cancellation_before_work()
    {
        var scanner = new DirectoryScanner();
        var cancellationToken = new CancellationToken(canceled: true);

        var act = () => scanner.Scan(temporaryDirectory.DirectoryPath, recursive: true, cancellationToken);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Scanner_skips_inaccessible_directories_without_aborting_the_batch()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = temporaryDirectory.DirectoryPath;
        var accessible = WriteTextFile(Path.Combine(root, "accessible.epub"));
        var blockedDirectory = Directory.CreateDirectory(Path.Combine(root, "blocked"));
        WriteTextFile(Path.Combine(blockedDirectory.FullName, "hidden.epub"));
        var denyRule = SetDirectoryInaccessible(blockedDirectory);
        try
        {
            var scanner = new DirectoryScanner();
            var results = scanner.Scan(root, recursive: true);

            results.Should().Equal(accessible);
        }
        finally
        {
            RestoreDirectoryAccess(blockedDirectory, denyRule);
            Directory.Delete(blockedDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public void Scanner_does_not_follow_reparse_points_when_recursive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = temporaryDirectory.DirectoryPath;
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
        var nestedFile = WriteTextFile(Path.Combine(nested.FullName, "nested.epub"));
        var linkPath = Path.Combine(root, "nested-link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, nested.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var scanner = new DirectoryScanner();

        var results = scanner.Scan(root, recursive: true);

        results.Should().Equal(nestedFile);
    }

    [Fact]
    public void Scanner_returns_empty_when_root_itself_is_a_reparse_point()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var target = Directory.CreateDirectory(Path.Combine(temporaryDirectory.DirectoryPath, "target"));
        var targetFile = Path.Combine(target.FullName, "root-linked.epub");
        File.WriteAllText(targetFile, "placeholder");
        var linkPath = Path.Combine(temporaryDirectory.DirectoryPath, "root-link");

        try
        {
            Directory.CreateSymbolicLink(linkPath, target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var scanner = new DirectoryScanner();

        scanner.Scan(linkPath, recursive: true).Should().BeEmpty();
        File.Exists(targetFile).Should().BeTrue();
    }

    [Fact]
    public async Task Hasher_returns_stable_uppercase_sha256()
    {
        var path = WriteBytesFile("ebook-manager.txt", Encoding.UTF8.GetBytes("ebook-manager"));
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ebook-manager")));
        var hasher = new Sha256FileHasher();

        var actual = await hasher.ComputeSha256Async(path, default);

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task Hasher_observes_cancellation_before_work()
    {
        var path = WriteBytesFile("cancelled.txt", Encoding.UTF8.GetBytes("cancelled"));
        var hasher = new Sha256FileHasher();

        var act = () => hasher.ComputeSha256Async(path, new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Managed_store_copies_files_under_book_directory_and_deletes_only_book_directory()
    {
        var libraryRoot = Path.Combine(temporaryDirectory.DirectoryPath, "Library");
        Directory.CreateDirectory(libraryRoot);
        var sourcePath = WriteBytesFile("source.epub", [1, 2, 3, 4]);
        var store = new ManagedLibraryFileStore(libraryRoot);
        var bookId = Guid.NewGuid();

        var (relativeBookPath, relativeCoverPath) = await store.CopyIntoLibraryAsync(
            bookId,
            sourcePath,
            [5, 6, 7],
            default);

        relativeBookPath.Should().Be($"books/{bookId:N}/source.epub");
        relativeCoverPath.Should().Be($"books/{bookId:N}/cover.jpg");
        File.Exists(Path.Combine(libraryRoot, relativeBookPath)).Should().BeTrue();
        File.Exists(Path.Combine(libraryRoot, relativeCoverPath!)).Should().BeTrue();

        await store.DeleteBookDirectoryAsync(bookId, default);

        Directory.Exists(Path.Combine(libraryRoot, "books", bookId.ToString("N"))).Should().BeFalse();
        Directory.Exists(Path.Combine(libraryRoot, "books")).Should().BeTrue();
        Directory.Exists(libraryRoot).Should().BeTrue();
    }

    [Fact]
    public void Managed_store_resolves_absolute_paths_safely_within_the_library_root()
    {
        var libraryRoot = Path.Combine(temporaryDirectory.DirectoryPath, "Library");
        Directory.CreateDirectory(libraryRoot);
        var store = new ManagedLibraryFileStore(libraryRoot);

        var resolved = store.GetAbsolutePath("books/123/book.epub");

        resolved.Should().Be(Path.GetFullPath(Path.Combine(libraryRoot, "books", "123", "book.epub")));
    }

    [Fact]
    public async Task Managed_store_observes_cancellation_before_copying()
    {
        var libraryRoot = Path.Combine(temporaryDirectory.DirectoryPath, "Library");
        Directory.CreateDirectory(libraryRoot);
        var sourcePath = WriteBytesFile("cancel-source.epub", [1, 2, 3]);
        var store = new ManagedLibraryFileStore(libraryRoot);

        var act = () => store.CopyIntoLibraryAsync(
            Guid.NewGuid(),
            sourcePath,
            [9, 8, 7],
            new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(libraryRoot, "books")).Should().BeFalse();
    }

    [Fact]
    public async Task Managed_store_replaces_stale_contents_when_source_filename_changes()
    {
        var libraryRoot = Path.Combine(temporaryDirectory.DirectoryPath, "Library");
        Directory.CreateDirectory(libraryRoot);
        var store = new ManagedLibraryFileStore(libraryRoot);
        var bookId = Guid.NewGuid();

        var firstSource = WriteBytesFile("first/The Hobbit.epub", [1, 2, 3]);
        await store.CopyIntoLibraryAsync(bookId, firstSource, [9, 9, 9], default);

        var secondSource = WriteBytesFile("second/The Hobbit - Revised.epub", [4, 5, 6, 7]);
        var result = await store.CopyIntoLibraryAsync(bookId, secondSource, null, default);

        var bookDirectory = Path.Combine(libraryRoot, "books", bookId.ToString("N"));
        result.RelativeBookPath.Should().Be($"books/{bookId:N}/The Hobbit - Revised.epub");
        result.RelativeCoverPath.Should().BeNull();
        Directory.EnumerateFiles(bookDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Should()
            .Equal("The Hobbit - Revised.epub");
        File.Exists(Path.Combine(bookDirectory, "The Hobbit.epub")).Should().BeFalse();
        File.Exists(Path.Combine(bookDirectory, "cover.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task Managed_store_leaves_prior_book_directory_intact_when_a_repeat_copy_is_cancelled()
    {
        var libraryRoot = Path.Combine(temporaryDirectory.DirectoryPath, "Library");
        Directory.CreateDirectory(libraryRoot);
        var store = new ManagedLibraryFileStore(libraryRoot);
        var bookId = Guid.NewGuid();

        var firstSource = WriteBytesFile("first/Original.epub", [1, 2, 3]);
        await store.CopyIntoLibraryAsync(bookId, firstSource, [4, 5, 6], default);

        var secondSource = WriteBytesFile("second/Replacement.epub", [7, 8, 9]);
        var before = Directory.EnumerateFiles(
                Path.Combine(libraryRoot, "books", bookId.ToString("N")),
                "*",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var act = () => store.CopyIntoLibraryAsync(
            bookId,
            secondSource,
            [10, 11, 12],
            new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();

        var after = Directory.EnumerateFiles(
                Path.Combine(libraryRoot, "books", bookId.ToString("N")),
                "*",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        after.Should().Equal(before);
    }

    [Fact]
    public async Task Json_sidecar_store_roundtrips_portable_book_metadata_next_to_book_file()
    {
        var bookFilePath = WriteBytesFile("Library/books/book-id/book.epub", [1, 2, 3]);
        var metadata = new BookMetadata(
            "Corrected Title",
            ["Corrected Author"],
            Description: "Corrected description",
            Language: "nl",
            Publisher: "Publisher",
            PublicationDate: new DateOnly(2026, 6, 3),
            Tags: ["Thriller", "Crime"],
            Series: "Atlanta",
            SeriesNumber: 1,
            Isbn: "9780000000000",
            CoverBytes: [9, 8, 7]);
        var store = new JsonMetadataSidecarStore();

        await store.WriteAsync(bookFilePath, metadata, default);
        var roundtripped = await store.TryReadAsync(bookFilePath, default);

        File.Exists(Path.Combine(Path.GetDirectoryName(bookFilePath)!, JsonMetadataSidecarStore.FileName))
            .Should()
            .BeTrue();
        roundtripped.Should().NotBeNull();
        roundtripped!.Title.Should().Be("Corrected Title");
        roundtripped.Authors.Should().Equal("Corrected Author");
        roundtripped.Tags.Should().Equal("Thriller", "Crime");
        roundtripped.Series.Should().Be("Atlanta");
        roundtripped.SeriesNumber.Should().Be(1);
        roundtripped.CoverBytes.Should().BeNull();
    }

    [Fact]
    public async Task Json_sidecar_store_ignores_malformed_sidecar_files()
    {
        var bookFilePath = WriteBytesFile("Library/books/malformed/book.epub", [1, 2, 3]);
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(bookFilePath)!, JsonMetadataSidecarStore.FileName),
            "{ this is not valid json");
        var store = new JsonMetadataSidecarStore();

        var result = await store.TryReadAsync(bookFilePath, default);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("The Hobbit - J.R.R. Tolkien.epub", "The Hobbit", "J.R.R. Tolkien")]
    [InlineData("Unknown Title.pdf", "Unknown Title", "Unknown")]
    public async Task Fallback_adapter_extracts_filename_metadata(
        string filename,
        string expectedTitle,
        string expectedAuthor)
    {
        var path = WriteBytesFile(filename, [1, 2, 3]);
        var adapter = new FallbackMetadataAdapter();

        var result = await adapter.ReadAsync(path, EbookFormat.Pdf, default);

        result.Metadata.Title.Should().Be(expectedTitle);
        result.Metadata.Authors.Should().Equal(expectedAuthor);
        result.Warning.Should().BeNull();
    }

    [Fact]
    public async Task Fallback_adapter_reports_unsupported_write()
    {
        var adapter = new FallbackMetadataAdapter();

        var result = await adapter.WriteAsync(
            WriteBytesFile("book.epub", [1, 2, 3]),
            EbookFormat.Epub,
            new BookMetadata("Title", ["Author"]),
            default);

        result.Status.Should().Be(MetadataWriteBackStatus.Unsupported);
    }

    [Fact]
    public async Task Epub_adapter_reads_metadata_and_embedded_cover()
    {
        byte[] coverBytes = [0x01, 0x02, 0x03, 0x04];
        var path = CreateArchive("sample.epub", archive =>
        {
            AddTextEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            AddTextEntry(archive, "OEBPS/content.opf", """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>The Hobbit</dc:title>
                    <dc:creator>J.R.R. Tolkien</dc:creator>
                    <dc:language>en</dc:language>
                    <dc:publisher>Allen &amp; Unwin</dc:publisher>
                    <dc:description>One ring to rule them all.</dc:description>
                    <dc:identifier id="bookid">9780000000000</dc:identifier>
                    <meta name="cover" content="cover-image" />
                  </metadata>
                  <manifest>
                    <item id="cover-image" href="cover.jpg" media-type="image/jpeg" properties="cover-image" />
                  </manifest>
                </package>
                """);

            AddBinaryEntry(archive, "OEBPS/cover.jpg", coverBytes);
        });
        var adapter = new EpubMetadataAdapter();

        var result = await adapter.ReadAsync(path, EbookFormat.Epub, default);

        result.Metadata.Title.Should().Be("The Hobbit");
        result.Metadata.Authors.Should().Equal("J.R.R. Tolkien");
        result.Metadata.Language.Should().Be("en");
        result.Metadata.Publisher.Should().Be("Allen & Unwin");
        result.Metadata.Description.Should().Be("One ring to rule them all.");
        result.Metadata.Isbn.Should().Be("9780000000000");
        result.Metadata.CoverBytes.Should().Equal(coverBytes);
        result.Warning.Should().BeNull();
    }

    [Fact]
    public async Task Epub_adapter_reads_subject_tags_and_calibre_series_metadata()
    {
        var path = CreateArchive("series.epub", archive =>
        {
            AddTextEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            AddTextEntry(archive, "OEBPS/content.opf", """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Triptiek</dc:title>
                    <dc:creator>Karin Slaughter</dc:creator>
                    <dc:subject>Thriller</dc:subject>
                    <dc:subject>Crime</dc:subject>
                    <dc:subject>Thriller</dc:subject>
                    <meta name="calibre:series" content="Atlanta" />
                    <meta name="calibre:series_index" content="1" />
                  </metadata>
                </package>
                """);
        });
        var adapter = new EpubMetadataAdapter();

        var result = await adapter.ReadAsync(path, EbookFormat.Epub, default);

        result.Metadata.Tags.Should().Equal("Thriller", "Crime");
        result.Metadata.Series.Should().Be("Atlanta");
        result.Metadata.SeriesNumber.Should().Be(1);
        result.Warning.Should().BeNull();
    }

    [Fact]
    public async Task Epub_adapter_returns_filename_fallback_with_warning_for_malformed_archive()
    {
        var path = CreateArchive("Malformed.epub", archive =>
        {
            AddTextEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
                  <rootfiles />
                </container>
                """);
        });
        var adapter = new EpubMetadataAdapter();

        var result = await adapter.ReadAsync(path, EbookFormat.Epub, default);

        result.Metadata.Title.Should().Be("Malformed");
        result.Metadata.Authors.Should().Equal("Unknown");
        result.Warning.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Epub_adapter_rejects_absolute_or_traversal_cover_hrefs()
    {
        var path = CreateArchive("Malicious.epub", archive =>
        {
            AddTextEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            AddTextEntry(archive, "OEBPS/content.opf", """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Malicious</dc:title>
                    <dc:creator>Author</dc:creator>
                    <meta name="cover" content="cover-image" />
                  </metadata>
                  <manifest>
                    <item id="cover-image" href="../cover.jpg" media-type="image/jpeg" />
                  </manifest>
                </package>
                """);
        });
        var adapter = new EpubMetadataAdapter();

        var result = await adapter.ReadAsync(path, EbookFormat.Epub, default);

        result.Metadata.Title.Should().Be("Malicious");
        result.Metadata.CoverBytes.Should().BeNull();
        result.Warning.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Epub_adapter_rejects_ambiguous_duplicate_canonical_entries()
    {
        var path = CreateArchive("Duplicate.epub", archive =>
        {
            AddTextEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            AddTextEntry(archive, "OEBPS/content.opf", """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Duplicate</dc:title>
                    <dc:creator>Author</dc:creator>
                    <meta name="cover" content="cover-image" />
                  </metadata>
                  <manifest>
                    <item id="cover-image" href="images/cover.jpg" media-type="image/jpeg" />
                  </manifest>
                </package>
                """);

            AddBinaryEntry(archive, "OEBPS/images/cover.jpg", [1, 2, 3]);
            AddBinaryEntry(archive, "OEBPS/images/./cover.jpg", [4, 5, 6]);
        });
        var adapter = new EpubMetadataAdapter();

        var result = await adapter.ReadAsync(path, EbookFormat.Epub, default);

        result.Metadata.Title.Should().Be("Duplicate");
        result.Metadata.CoverBytes.Should().BeNull();
        result.Warning.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Epub_adapter_rejects_duplicate_rootfile_entries()
    {
        var path = CreateArchive("DuplicateRootfile.epub", archive =>
        {
            AddTextEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
                    <rootfile full-path="OEBPS/other.opf" media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            AddTextEntry(archive, "OEBPS/content.opf", """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Duplicate Rootfile</dc:title>
                    <dc:creator>Author</dc:creator>
                  </metadata>
                </package>
                """);
            AddTextEntry(archive, "OEBPS/other.opf", """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Other</dc:title>
                    <dc:creator>Author</dc:creator>
                  </metadata>
                </package>
                """);
        });
        var adapter = new EpubMetadataAdapter();

        var result = await adapter.ReadAsync(path, EbookFormat.Epub, default);

        result.Metadata.Title.Should().Be("DuplicateRootfile");
        result.Warning.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Cbz_adapter_chooses_the_alphabetically_first_supported_image()
    {
        byte[] alphaBytes = [0x11, 0x22];
        byte[] betaBytes = [0x33, 0x44];
        var path = CreateArchive("The Hobbit - J.R.R. Tolkien.cbz", archive =>
        {
            AddBinaryEntry(archive, "images/Zeta.png", betaBytes);
            AddBinaryEntry(archive, "images/Alpha.jpeg", alphaBytes);
            AddTextEntry(archive, "notes.txt", "ignored");
        });
        var adapter = new CbzMetadataAdapter();

        var result = await adapter.ReadAsync(path, EbookFormat.Cbz, default);

        result.Metadata.Title.Should().Be("The Hobbit");
        result.Metadata.Authors.Should().Equal("J.R.R. Tolkien");
        result.Metadata.CoverBytes.Should().Equal(alphaBytes);
    }

    [Fact]
    public async Task Cbz_adapter_rejects_archives_with_too_many_entries()
    {
        var path = CreateArchive("TooMany.cbz", archive =>
        {
            for (var index = 0; index < 2001; index++)
            {
                AddTextEntry(archive, $"pages/page-{index:D4}.txt", "ignored");
            }

            AddBinaryEntry(archive, "images/cover.jpg", [1, 2, 3]);
        });
        var adapter = new CbzMetadataAdapter();

        var act = () => adapter.ReadAsync(path, EbookFormat.Cbz, default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Cbz_adapter_rejects_selected_images_that_exceed_the_size_limit()
    {
        var largeCover = new byte[11 * 1024 * 1024];
        Random.Shared.NextBytes(largeCover);
        var path = CreateArchive("LargeCover.cbz", archive =>
        {
            AddBinaryEntry(archive, "images/cover.png", largeCover);
        });
        var adapter = new CbzMetadataAdapter();

        var act = () => adapter.ReadAsync(path, EbookFormat.Cbz, default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Cbz_adapter_honors_cancellation_during_cover_read()
    {
        var largeCover = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(largeCover);
        var path = CreateArchive("Cancelable.cbz", archive =>
        {
            AddBinaryEntry(archive, "images/cover.webp", largeCover);
        });
        var adapter = new CbzMetadataAdapter();
        using var cancellationTokenSource = new CancellationTokenSource();

        var readTask = adapter.ReadAsync(path, EbookFormat.Cbz, cancellationTokenSource.Token);
        cancellationTokenSource.CancelAfter(1);

        await FluentActions.Awaiting(() => readTask).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Metadata_resolver_prefers_specific_adapters_before_fallback()
    {
        var fallback = new FallbackMetadataAdapter();
        var epub = new EpubMetadataAdapter();
        var cbz = new CbzMetadataAdapter();
        var resolver = new MetadataAdapterResolver([fallback, epub, cbz]);

        resolver.Resolve(EbookFormat.Epub).Should().BeSameAs(epub);
        resolver.Resolve(EbookFormat.Cbz).Should().BeSameAs(cbz);
        resolver.Resolve(EbookFormat.Pdf).Should().BeSameAs(fallback);
    }

    [Fact]
    public void Metadata_resolver_rejects_duplicate_specific_adapters_for_same_format()
    {
        var fallback = new FallbackMetadataAdapter();
        var first = new FakeAdapter(EbookFormat.Epub);
        var second = new FakeAdapter(EbookFormat.Epub);

        var act = () => new MetadataAdapterResolver([fallback, first, second]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Epub*");
    }

    [Fact]
    public void Metadata_resolver_requires_exactly_one_fallback_adapter()
    {
        var specific = new FakeAdapter(EbookFormat.Epub);
        var fallback = new FallbackMetadataAdapter();

        var missingAct = () => new MetadataAdapterResolver([specific]);
        var duplicateAct = () => new MetadataAdapterResolver([fallback, new FallbackMetadataAdapter(), specific]);

        missingAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*fallback*");
        duplicateAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*fallback*");
    }

    public void Dispose() => temporaryDirectory.Dispose();

    private string WriteTextFile(string relativePath)
    {
        var path = Path.Combine(temporaryDirectory.DirectoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
        return path;
    }

    private string WriteBytesFile(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(temporaryDirectory.DirectoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string CreateArchive(string relativePath, Action<ZipArchive> writeEntries)
    {
        var path = Path.Combine(temporaryDirectory.DirectoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        writeEntries(archive);
        return path;
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = new StreamWriter(entry.Open(), Encoding.UTF8);
        stream.Write(content);
    }

    private static void AddBinaryEntry(ZipArchive archive, string entryName, byte[] content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static FileSystemAccessRule? SetDirectoryInaccessible(DirectoryInfo directoryInfo)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var identity = WindowsIdentity.GetCurrent();
        if (identity.User is null)
        {
            return null;
        }

        var security = directoryInfo.GetAccessControl();
        var denyRule = new FileSystemAccessRule(
            identity.User,
            FileSystemRights.ListDirectory | FileSystemRights.ReadAndExecute | FileSystemRights.Traverse,
            AccessControlType.Deny);
        security.AddAccessRule(denyRule);
        directoryInfo.SetAccessControl(security);
        return denyRule;
    }

    private static void RestoreDirectoryAccess(DirectoryInfo directoryInfo, FileSystemAccessRule? denyRule)
    {
        if (!OperatingSystem.IsWindows() || denyRule is null)
        {
            return;
        }

        var security = directoryInfo.GetAccessControl();
        security.RemoveAccessRuleAll(denyRule);
        directoryInfo.SetAccessControl(security);
    }

    private sealed class FakeAdapter(EbookFormat format) : IMetadataAdapter
    {
        public bool CanHandle(EbookFormat candidateFormat) => candidateFormat == format;

        public Task<MetadataReadResult> ReadAsync(string path, EbookFormat candidateFormat, CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataReadResult(new BookMetadata("Fake", ["Fake"])));

        public Task<MetadataWriteResult> WriteAsync(
            string path,
            EbookFormat candidateFormat,
            BookMetadata metadata,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataWriteResult(MetadataWriteBackStatus.Unsupported));
    }
}
