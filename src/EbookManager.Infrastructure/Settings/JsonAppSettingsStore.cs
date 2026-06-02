using System.Collections.Concurrent;
using System.Text.Json;
using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Libraries;

namespace EbookManager.Infrastructure.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly AppSettings DefaultSettings = new(null, "en-US", "Light", "Detailed", true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TargetLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string baseDirectory;

    public JsonAppSettingsStore(string? baseDirectory = null)
    {
        this.baseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EbookManager");
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
        ReadAsync("settings.json", DefaultSettings, cancellationToken);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
        WriteAsync("settings.json", settings, cancellationToken);

    public async Task<IReadOnlyList<LibraryDescriptor>> ListLibrariesAsync(
        CancellationToken cancellationToken) =>
        await ReadAsync("libraries.json", Array.Empty<LibraryDescriptor>(), cancellationToken);

    public Task SaveLibrariesAsync(
        IReadOnlyList<LibraryDescriptor> libraries,
        CancellationToken cancellationToken) =>
        WriteAsync("libraries.json", libraries, cancellationToken);

    private async Task<T> ReadAsync<T>(
        string filename,
        T defaultValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(baseDirectory, filename);
        var targetLock = GetTargetLock(path);

        await targetLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                return defaultValue;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken) ?? defaultValue;
            }
            catch (JsonException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Quarantine(path);
                return defaultValue;
            }
        }
        finally
        {
            targetLock.Release();
        }
    }

    private async Task WriteAsync<T>(
        string filename,
        T value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(baseDirectory);

        var path = Path.Combine(baseDirectory, filename);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            }

            var targetLock = GetTargetLock(path);
            await targetLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                targetLock.Release();
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static SemaphoreSlim GetTargetLock(string path) =>
        TargetLocks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    private static void Quarantine(string path)
    {
        if (File.Exists(path))
        {
            File.Move(path, $"{path}.{Guid.NewGuid():N}.corrupt");
        }
    }
}
