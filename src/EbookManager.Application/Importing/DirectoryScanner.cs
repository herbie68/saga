using EbookManager.Domain.Books;

namespace EbookManager.Application.Importing;

public sealed class DirectoryScanner
{
    public string[] Scan(
        string directoryPath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootDirectory = new DirectoryInfo(Path.GetFullPath(directoryPath));
        FileAttributes rootAttributes;
        try
        {
            rootAttributes = rootDirectory.Attributes;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
        {
            return [];
        }

        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        var matches = new List<string>();
        var directories = new Stack<string>();
        directories.Push(rootDirectory.FullName);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDirectory = directories.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(currentDirectory);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
            {
                continue;
            }

            foreach (var path in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(path);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or FileNotFoundException or PathTooLongException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (recursive)
                    {
                        directories.Push(path);
                    }

                    continue;
                }

                if (EbookFormatExtensions.TryFromFilename(path, out _))
                {
                    matches.Add(path);
                }
            }
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches.ToArray();
    }
}
