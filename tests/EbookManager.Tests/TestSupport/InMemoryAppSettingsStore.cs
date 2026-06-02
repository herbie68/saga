using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Libraries;

namespace EbookManager.Tests.TestSupport;

public sealed class InMemoryAppSettingsStore : IAppSettingsStore
{
    public AppSettings Settings { get; private set; } = new(null, "en-US", "Light", "Detailed", true);

    public List<LibraryDescriptor> Libraries { get; private set; } = [];

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Settings);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Settings = settings;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LibraryDescriptor>> ListLibrariesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<LibraryDescriptor>>(Libraries);
    }

    public Task SaveLibrariesAsync(
        IReadOnlyList<LibraryDescriptor> libraries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Libraries = [.. libraries];
        return Task.CompletedTask;
    }
}
