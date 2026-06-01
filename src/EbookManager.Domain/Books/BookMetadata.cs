namespace EbookManager.Domain.Books;

public sealed record BookMetadata
{
    public BookMetadata(
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
        byte[]? CoverBytes = null)
    {
        this.Title = Title;
        this.Authors = Array.AsReadOnly(Authors.ToArray());
        this.Description = Description;
        this.Language = Language;
        this.Publisher = Publisher;
        this.PublicationDate = PublicationDate;
        this.Tags = Tags is null ? null : Array.AsReadOnly(Tags.ToArray());
        this.Series = Series;
        this.SeriesNumber = SeriesNumber;
        this.Isbn = Isbn;
        this.CoverBytes = CoverBytes is null ? null : Array.AsReadOnly(CoverBytes.ToArray());
    }

    public string Title { get; }
    public IReadOnlyList<string> Authors { get; }
    public string? Description { get; }
    public string? Language { get; }
    public string? Publisher { get; }
    public DateOnly? PublicationDate { get; }
    public IReadOnlyList<string>? Tags { get; }
    public string? Series { get; }
    public decimal? SeriesNumber { get; }
    public string? Isbn { get; }
    public IReadOnlyList<byte>? CoverBytes { get; }

    public bool Equals(BookMetadata? other) =>
        other is not null &&
        Title == other.Title &&
        Authors.SequenceEqual(other.Authors) &&
        Description == other.Description &&
        Language == other.Language &&
        Publisher == other.Publisher &&
        PublicationDate == other.PublicationDate &&
        NullableSequenceEqual(Tags, other.Tags) &&
        Series == other.Series &&
        SeriesNumber == other.SeriesNumber &&
        Isbn == other.Isbn &&
        NullableSequenceEqual(CoverBytes, other.CoverBytes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title);
        AddSequence(ref hash, Authors);
        hash.Add(Description);
        hash.Add(Language);
        hash.Add(Publisher);
        hash.Add(PublicationDate);
        AddNullableSequence(ref hash, Tags);
        hash.Add(Series);
        hash.Add(SeriesNumber);
        hash.Add(Isbn);
        AddNullableSequence(ref hash, CoverBytes);
        return hash.ToHashCode();
    }

    private static bool NullableSequenceEqual<T>(
        IReadOnlyList<T>? first,
        IReadOnlyList<T>? second) =>
        first is null ? second is null : second is not null && first.SequenceEqual(second);

    private static void AddNullableSequence<T>(ref HashCode hash, IReadOnlyList<T>? values)
    {
        hash.Add(values is not null);
        if (values is not null)
        {
            AddSequence(ref hash, values);
        }
    }

    private static void AddSequence<T>(ref HashCode hash, IReadOnlyList<T> values)
    {
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}
