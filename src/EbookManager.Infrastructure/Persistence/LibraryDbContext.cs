using System.Globalization;
using EbookManager.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EbookManager.Infrastructure.Persistence;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<BookEntity> Books => Set<BookEntity>();
    public DbSet<AuthorEntity> Authors => Set<AuthorEntity>();
    public DbSet<BookAuthorEntity> BookAuthors => Set<BookAuthorEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<BookTagEntity> BookTags => Set<BookTagEntity>();
    public DbSet<BookFileEntity> BookFiles => Set<BookFileEntity>();
    public DbSet<ImportRunEntity> ImportRuns => Set<ImportRunEntity>();
    public DbSet<ImportItemEntity> ImportItems => Set<ImportItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var nullableDateOnlyConverter = new ValueConverter<DateOnly?, string?>(
            date => date.HasValue ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            text => string.IsNullOrEmpty(text)
                ? null
                : DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture));

        modelBuilder.Entity<BookEntity>(book =>
        {
            book.ToTable("Books");
            book.HasKey(x => x.Id);
            book.Property(x => x.Title).IsRequired();
            book.Property(x => x.NormalizedTitle).IsRequired();
            book.Property(x => x.DuplicateKey).IsRequired();
            book.HasIndex(x => x.NormalizedTitle);
            book.HasIndex(x => new { x.NormalizedTitle, x.Id })
                .HasDatabaseName("IX_Books_NormalizedTitle_Id");
            book.HasIndex(x => x.DuplicateKey).IsUnique();
            book.Property(x => x.PublicationDate).HasConversion(nullableDateOnlyConverter);
            book.Property(x => x.ReadingStatus).HasConversion<string>();
        });

        modelBuilder.Entity<AuthorEntity>(author =>
        {
            author.ToTable("Authors");
            author.HasKey(x => x.Id);
            author.Property(x => x.Name).IsRequired();
            author.Property(x => x.NormalizedName).IsRequired();
            author.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<BookAuthorEntity>(bookAuthor =>
        {
            bookAuthor.ToTable("BookAuthors");
            bookAuthor.HasKey(x => new { x.BookId, x.AuthorId });
            bookAuthor.HasIndex(x => new { x.BookId, x.Order }).IsUnique();
            bookAuthor.HasOne(x => x.Book)
                .WithMany(x => x.BookAuthors)
                .HasForeignKey(x => x.BookId)
                .OnDelete(DeleteBehavior.Cascade);
            bookAuthor.HasOne(x => x.Author)
                .WithMany(x => x.BookAuthors)
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TagEntity>(tag =>
        {
            tag.ToTable("Tags");
            tag.HasKey(x => x.Id);
            tag.Property(x => x.Name).IsRequired();
            tag.Property(x => x.NormalizedName).IsRequired();
            tag.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<BookTagEntity>(bookTag =>
        {
            bookTag.ToTable("BookTags");
            bookTag.HasKey(x => new { x.BookId, x.TagId });
            bookTag.HasIndex(x => new { x.BookId, x.Order }).IsUnique();
            bookTag.HasOne(x => x.Book)
                .WithMany(x => x.BookTags)
                .HasForeignKey(x => x.BookId)
                .OnDelete(DeleteBehavior.Cascade);
            bookTag.HasOne(x => x.Tag)
                .WithMany(x => x.BookTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookFileEntity>(bookFile =>
        {
            bookFile.ToTable("BookFiles");
            bookFile.HasKey(x => x.Id);
            bookFile.Property(x => x.Format).HasConversion<string>();
            bookFile.Property(x => x.RelativePath).IsRequired();
            bookFile.Property(x => x.Sha256).IsRequired();
            bookFile.Property(x => x.WriteBackStatus).HasConversion<string>();
            bookFile.HasIndex(x => x.Sha256).IsUnique();
            bookFile.HasOne(x => x.Book)
                .WithMany(x => x.Files)
                .HasForeignKey(x => x.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImportRunEntity>(importRun =>
        {
            importRun.ToTable("ImportRuns");
            importRun.HasKey(x => x.Id);
            importRun.Property(x => x.Kind).IsRequired();
        });

        modelBuilder.Entity<ImportItemEntity>(importItem =>
        {
            importItem.ToTable("ImportItems");
            importItem.HasKey(x => x.Id);
            importItem.Property(x => x.Sequence).IsRequired();
            importItem.Property(x => x.SourcePath).IsRequired();
            importItem.Property(x => x.Outcome).HasConversion<string>();
            importItem.Property(x => x.Message).IsRequired();
            importItem.HasIndex(x => new { x.ImportRunId, x.Sequence }).IsUnique();
            importItem.HasOne(x => x.ImportRun)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.ImportRunId)
                .OnDelete(DeleteBehavior.Cascade);
            importItem.HasOne(x => x.Book)
                .WithMany()
                .HasForeignKey(x => x.BookId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
