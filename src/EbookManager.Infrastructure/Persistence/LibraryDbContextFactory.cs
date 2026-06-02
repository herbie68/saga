using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EbookManager.Infrastructure.Persistence;

public sealed class LibraryDbContextFactory
{
    public LibraryDbContext Create(string directoryPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directoryPath, "library.db")
        }.ToString();
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new LibraryDbContext(options);
    }
}
