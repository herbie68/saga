using EbookManager.App.Services;
using EbookManager.App.Views;
using EbookManager.Presentation.ViewModels;

namespace EbookManager.App;

public partial class MainWindow : System.Windows.Window
{
    private readonly LibraryViewModel viewModel;
    private readonly SettingsViewModel settingsViewModel;
    private readonly LocalizationService localizationService;
    private readonly ThemeService themeService;

    public MainWindow(
        LibraryViewModel viewModel,
        SettingsViewModel settingsViewModel,
        LocalizationService localizationService,
        ThemeService themeService)
    {
        this.viewModel = viewModel;
        this.settingsViewModel = settingsViewModel;
        this.localizationService = localizationService;
        this.themeService = themeService;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await viewModel.RefreshAsync();
    }

    private void OpenSettingsClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        var window = new SettingsWindow(settingsViewModel, localizationService, themeService)
        {
            Owner = this
        };
        window.ShowDialog();
    }
}
