using System.Collections.Frozen;
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

    [Fact]
    public void Supported_formats_are_exposed_as_a_frozen_set()
    {
        EbookFormatExtensions.Supported.Should().BeAssignableTo<FrozenSet<EbookFormat>>();
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

    [Theory]
    [InlineData("book.1")]
    [InlineData("book.unknown")]
    public void Unknown_or_numeric_extensions_are_rejected(string filename)
    {
        EbookFormatExtensions.TryFromFilename(filename, out _).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(SupportedFormats))]
    public void Supported_formats_round_trip_through_extension_mapping(EbookFormat format)
    {
        var extension = format.ToExtension();

        EbookFormatExtensions.TryFromFilename($"book{extension}", out var actual).Should().BeTrue();
        actual.Should().Be(format);
    }

    [Fact]
    public void Undefined_format_cannot_be_converted_to_an_extension()
    {
        var act = () => ((EbookFormat)999).ToExtension();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Metadata_copies_caller_owned_collections_and_cover_bytes()
    {
        var authors = new List<string> { "Original author" };
        var tags = new List<string> { "Original tag" };
        var coverBytes = new byte[] { 1, 2, 3 };
        var metadata = new BookMetadata("Title", authors, Tags: tags, CoverBytes: coverBytes);

        authors[0] = "Changed author";
        tags[0] = "Changed tag";
        coverBytes[0] = 9;

        metadata.Authors.Should().Equal("Original author");
        metadata.Tags.Should().Equal("Original tag");
        metadata.CoverBytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Metadata_returns_a_cover_byte_copy_from_the_public_array_contract()
    {
        var metadata = new BookMetadata("Title", ["Author"], CoverBytes: [1, 2, 3]);

        byte[]? exposedCoverBytes = metadata.CoverBytes;
        exposedCoverBytes![0] = 9;

        metadata.CoverBytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Metadata_with_equivalent_content_compares_equal()
    {
        var first = new BookMetadata(
            "Title",
            ["Author"],
            Description: "Description",
            Tags: ["Tag"],
            CoverBytes: [1, 2, 3]);
        var second = new BookMetadata(
            "Title",
            ["Author"],
            Description: "Description",
            Tags: ["Tag"],
            CoverBytes: [1, 2, 3]);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    public static TheoryData<EbookFormat> SupportedFormats =>
        new(Enum.GetValues<EbookFormat>());
}
