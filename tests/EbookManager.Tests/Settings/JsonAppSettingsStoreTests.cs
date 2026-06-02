using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Libraries;
using EbookManager.Infrastructure.Settings;
using EbookManager.Tests.TestSupport;
using FluentAssertions;

namespace EbookManager.Tests.Settings;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
    private readonly TemporaryDirectory temporaryDirectory = new();

    [Fact]
    public async Task Settings_round_trip_through_injected_base_directory()
    {
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);
        var settings = new AppSettings("C:\\Books", "nl-NL", "Dark", "List", false);

        await store.SaveAsync(settings, CancellationToken.None);

        var loaded = await new JsonAppSettingsStore(temporaryDirectory.DirectoryPath).LoadAsync(default);
        loaded.Should().Be(settings);
        File.Exists(Path.Combine(temporaryDirectory.DirectoryPath, "settings.json")).Should().BeTrue();
        File.Exists(Path.Combine(temporaryDirectory.DirectoryPath, "libraries.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Libraries_round_trip_separately_from_settings()
    {
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);
        LibraryDescriptor[] libraries =
        [
            new("First", "C:\\First", DateTimeOffset.Parse("2026-06-01T08:00:00Z")),
            new("Second", "C:\\Second", DateTimeOffset.Parse("2026-06-01T09:00:00Z"))
        ];

        await store.SaveLibrariesAsync(libraries, CancellationToken.None);

        var loaded = await new JsonAppSettingsStore(temporaryDirectory.DirectoryPath).ListLibrariesAsync(default);
        loaded.Should().Equal(libraries);
        File.Exists(Path.Combine(temporaryDirectory.DirectoryPath, "libraries.json")).Should().BeTrue();
        File.Exists(Path.Combine(temporaryDirectory.DirectoryPath, "settings.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_save_overwrites_target_and_leaves_no_temporary_file()
    {
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);
        await store.SaveAsync(new(null, "en-US", "Light", "Detailed", true), default);

        var updated = new AppSettings("C:\\Updated", "nl-NL", "Dark", "List", false);
        await store.SaveAsync(updated, default);

        (await store.LoadAsync(default)).Should().Be(updated);
        File.Exists(Path.Combine(temporaryDirectory.DirectoryPath, "settings.json.tmp")).Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_saves_use_independent_temporary_files()
    {
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);
        var saves = Enumerable.Range(0, 100)
            .Select(index => store.SaveAsync(
                new($"C:\\Library{index}", "en-US", "Light", "Detailed", true),
                default));

        var act = () => Task.WhenAll(saves);

        await act.Should().NotThrowAsync();
        Directory.GetFiles(temporaryDirectory.DirectoryPath, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Load_quarantines_malformed_settings_and_returns_defaults()
    {
        var path = Path.Combine(temporaryDirectory.DirectoryPath, "settings.json");
        await File.WriteAllTextAsync(path, "{");
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);

        var settings = await store.LoadAsync(default);

        settings.Should().Be(new AppSettings(null, "en-US", "Light", "Detailed", true));
        File.Exists(path).Should().BeFalse();
        Directory.GetFiles(temporaryDirectory.DirectoryPath, "settings.json.*.corrupt").Should().ContainSingle();
    }

    [Fact]
    public async Task ListLibraries_quarantines_malformed_json_and_returns_empty_list()
    {
        var path = Path.Combine(temporaryDirectory.DirectoryPath, "libraries.json");
        await File.WriteAllTextAsync(path, "{");
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);

        var libraries = await store.ListLibrariesAsync(default);

        libraries.Should().BeEmpty();
        File.Exists(path).Should().BeFalse();
        Directory.GetFiles(temporaryDirectory.DirectoryPath, "libraries.json.*.corrupt").Should().ContainSingle();
    }

    [Fact]
    public async Task Corrupt_load_racing_with_valid_save_does_not_move_saved_target_or_throw()
    {
        var path = Path.Combine(temporaryDirectory.DirectoryPath, "settings.json");
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);

        for (var index = 0; index < 100; index++)
        {
            await File.WriteAllTextAsync(path, "{");
            var savedSettings = new AppSettings($"C:\\Library{index}", "nl-NL", "Dark", "List", false);
            var operations = Enumerable.Range(0, 20)
                .Select(_ => store.LoadAsync(default))
                .Cast<Task>()
                .Append(store.SaveAsync(savedSettings, default));

            var act = () => Task.WhenAll(operations);

            await act.Should().NotThrowAsync();
            File.Exists(path).Should().BeTrue();
            (await store.LoadAsync(default)).Should().Be(savedSettings);
        }
    }

    [Fact]
    public async Task Concurrent_corrupt_loads_do_not_throw()
    {
        var path = Path.Combine(temporaryDirectory.DirectoryPath, "settings.json");
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);

        for (var index = 0; index < 100; index++)
        {
            await File.WriteAllTextAsync(path, "{");
            var loads = Enumerable.Range(0, 50)
                .Select(_ => store.LoadAsync(default));

            var act = () => Task.WhenAll(loads);

            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task Pre_canceled_load_does_not_return_default_settings()
    {
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);

        var act = () => store.LoadAsync(new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Pre_canceled_list_libraries_does_not_return_default_list()
    {
        var store = new JsonAppSettingsStore(temporaryDirectory.DirectoryPath);

        var act = () => store.ListLibrariesAsync(new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Pre_canceled_save_does_not_create_base_directory()
    {
        var baseDirectory = Path.Combine(temporaryDirectory.DirectoryPath, "NotCreated");
        var store = new JsonAppSettingsStore(baseDirectory);

        var act = () => store.SaveAsync(
            new AppSettings(null, "en-US", "Light", "Detailed", true),
            new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(baseDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task Pre_canceled_save_libraries_does_not_create_base_directory()
    {
        var baseDirectory = Path.Combine(temporaryDirectory.DirectoryPath, "NotCreated");
        var store = new JsonAppSettingsStore(baseDirectory);

        var act = () => store.SaveLibrariesAsync([], new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(baseDirectory).Should().BeFalse();
    }

    public void Dispose() => temporaryDirectory.Dispose();
}
