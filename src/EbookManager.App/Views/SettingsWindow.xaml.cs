using EbookManager.App.Services;
using EbookManager.Presentation.ViewModels;

namespace EbookManager.App.Views;

public partial class SettingsWindow : System.Windows.Window
{
    private readonly SettingsViewModel viewModel;
    private readonly LocalizationService localizationService;
    private readonly ThemeService themeService;

    public SettingsWindow(
        SettingsViewModel viewModel,
        LocalizationService localizationService,
        ThemeService themeService)
    {
        this.viewModel = viewModel;
        this.localizationService = localizationService;
        this.themeService = themeService;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await viewModel.LoadAsync();
    }

    private async void SaveClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        await viewModel.SaveAsync();
        localizationService.ApplyCulture(viewModel.Culture);
        themeService.ApplyTheme(viewModel.Theme);
        DialogResult = true;
    }

    private void CancelClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
