using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Libraries;

namespace EbookManager.Libraries;

public sealed class LibraryService(IAppSettingsStore settingsStore)
{
    public async Task<LibraryDescriptor> CreateAsync(
        string name,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = LibraryPath.Canonicalize(directoryPath);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(fullPath);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.Combine(fullPath, "books"));
        return await RememberAsync(new(name, fullPath, DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task<LibraryDescriptor> OpenAsync(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = LibraryPath.Canonicalize(directoryPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.Combine(fullPath, "books"));
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        return await RememberAsync(new(name, fullPath, DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task<LibraryDescriptor> RememberAsync(
        LibraryDescriptor library,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var libraries = (await settingsStore.ListLibrariesAsync(cancellationToken))
            .Where(existing => !LibraryPath.Equals(existing.DirectoryPath, library.DirectoryPath))
            .Append(library)
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        await settingsStore.SaveLibrariesAsync(libraries, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var settings = await settingsStore.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await settingsStore.SaveAsync(settings with { LastLibraryPath = library.DirectoryPath }, cancellationToken);
        return library;
    }
}
