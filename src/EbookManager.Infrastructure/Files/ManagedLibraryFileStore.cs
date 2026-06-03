using EbookManager.Domain.Abstractions;

namespace EbookManager.Infrastructure.Files;

public sealed class ManagedLibraryFileStore(string libraryRootPath) : ILibraryFileStore
{
    private readonly string libraryRoot = Canonicalize(libraryRootPath);

    public string GetAbsolutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("The relative path must not be blank.", nameof(relativePath));
        }

        return EnsureContained(Path.Combine(libraryRoot, relativePath));
    }

    public async Task<(string RelativeBookPath, string? RelativeCoverPath)> CopyIntoLibraryAsync(
        Guid bookId,
        string sourcePath,
        byte[]? coverBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var booksDirectory = EnsureContained(Path.Combine(libraryRoot, "books"));
        Directory.CreateDirectory(booksDirectory);

        var bookDirectory = GetBookDirectory(bookId);
        var stagingDirectory = EnsureContained(Path.Combine(
            booksDirectory,
            $".{bookId:N}.{Guid.NewGuid():N}.staging"));
        var backupDirectory = EnsureContained(Path.Combine(
            booksDirectory,
            $".{bookId:N}.{Guid.NewGuid():N}.backup"));
        var managedSourceName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(managedSourceName))
        {
            throw new ArgumentException("The source path must include a file name.", nameof(sourcePath));
        }

        await using var source = new FileStream(
            Path.GetFullPath(sourcePath),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        Directory.CreateDirectory(stagingDirectory);
        var absoluteBookPath = EnsureContained(Path.Combine(bookDirectory, managedSourceName));
        var stagedBookPath = EnsureContained(Path.Combine(stagingDirectory, managedSourceName));

        try
        {
            await using (var destination = new FileStream(
                stagedBookPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.Create,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                }))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            string? stagedCoverPath = null;
            if (coverBytes is { Length: > 0 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                stagedCoverPath = EnsureContained(Path.Combine(stagingDirectory, "cover.jpg"));
                await File.WriteAllBytesAsync(stagedCoverPath, coverBytes, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var targetExisted = Directory.Exists(bookDirectory);
            var backupCreated = false;
            if (targetExisted)
            {
                Directory.Move(bookDirectory, backupDirectory);
                backupCreated = true;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stagingDirectory, bookDirectory);
            }
            catch
            {
                if (backupCreated && !Directory.Exists(bookDirectory) && Directory.Exists(backupDirectory))
                {
                    Directory.Move(backupDirectory, bookDirectory);
                    backupCreated = false;
                }

                throw;
            }

            if (backupCreated && Directory.Exists(backupDirectory))
            {
                TryDeleteDirectory(backupDirectory);
            }

            return (
                ToRelativePath(absoluteBookPath),
                stagedCoverPath is null ? null : ToRelativePath(Path.Combine(bookDirectory, "cover.jpg")));
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
    }

    public Task DeleteBookDirectoryAsync(Guid bookId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bookDirectory = EnsureContained(GetBookDirectory(bookId));
        if (Directory.Exists(bookDirectory))
        {
            Directory.Delete(bookDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private string GetBookDirectory(Guid bookId) => Path.Combine(libraryRoot, "books", bookId.ToString("N"));

    private string EnsureContained(string path)
    {
        var fullPath = Canonicalize(path);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootWithSeparator = libraryRoot.EndsWith(Path.DirectorySeparatorChar)
            ? libraryRoot
            : $"{libraryRoot}{Path.DirectorySeparatorChar}";

        if (!fullPath.Equals(libraryRoot, pathComparison) &&
            !fullPath.StartsWith(rootWithSeparator, pathComparison))
        {
            throw new InvalidOperationException($"Path '{path}' escapes the managed library root.");
        }

        return fullPath;
    }

    private static string Canonicalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private string ToRelativePath(string absolutePath) =>
        Path.GetRelativePath(libraryRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
