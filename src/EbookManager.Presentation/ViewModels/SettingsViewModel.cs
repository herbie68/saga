using CommunityToolkit.Mvvm.ComponentModel;
using EbookManager.Domain.Abstractions;

namespace EbookManager.Presentation.ViewModels;

public sealed partial class SettingsViewModel(IAppSettingsStore settingsStore) : ObservableObject
{
    private readonly IAppSettingsStore settingsStore = settingsStore;

    [ObservableProperty]
    private string culture = "en-US";

    [ObservableProperty]
    private string theme = "Light";

    [ObservableProperty]
    private string defaultView = "Detailed";

    [ObservableProperty]
    private bool confirmDelete = true;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        Culture = settings.Culture;
        Theme = settings.Theme;
        DefaultView = settings.DefaultView;
        ConfirmDelete = settings.ConfirmDelete;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var current = await settingsStore.LoadAsync(cancellationToken);
        await settingsStore.SaveAsync(
            current with
            {
                Culture = Culture,
                Theme = Theme,
                DefaultView = DefaultView,
                ConfirmDelete = ConfirmDelete
            },
            cancellationToken);
    }
}
