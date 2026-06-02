using EbookManager.Domain.Abstractions;
using EbookManager.Domain.Libraries;
using EbookManager.Tests.TestSupport;
using FluentAssertions;

namespace EbookManager.Tests.Settings;

public sealed class InMemoryAppSettingsStoreTests
{
    [Fact]
    public async Task Pre_canceled_operations_throw_before_returning_or_mutating_state()
    {
        var store = new InMemoryAppSettingsStore();
        var cancellationToken = new CancellationToken(canceled: true);
        var changedSettings = new AppSettings("C:\\Changed", "nl-NL", "Dark", "List", false);
        LibraryDescriptor[] changedLibraries =
        [
            new("Changed", "C:\\Changed", DateTimeOffset.Parse("2026-06-02T08:00:00Z"))
        ];

        await FluentActions.Awaiting(() => store.LoadAsync(cancellationToken))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => store.ListLibrariesAsync(cancellationToken))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => store.SaveAsync(changedSettings, cancellationToken))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => store.SaveLibrariesAsync(changedLibraries, cancellationToken))
            .Should().ThrowAsync<OperationCanceledException>();

        store.Settings.Should().Be(new AppSettings(null, "en-US", "Light", "Detailed", true));
        store.Libraries.Should().BeEmpty();
    }
}
