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

    private void LibraryDropZoneDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void LibraryDropZoneDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            await viewModel.ImportFilesAsync(paths);
        }
    }
}
