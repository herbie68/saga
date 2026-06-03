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
    private bool isHandlingDrop;

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
        RegisterLibraryDragDropHandlers();
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
        if (isHandlingDrop)
        {
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            e.Handled = true;
            isHandlingDrop = true;
            try
            {
                await viewModel.ImportFilesAsync(paths);
            }
            finally
            {
                isHandlingDrop = false;
            }
        }
    }

    private void RegisterLibraryDragDropHandlers()
    {
        var dragHandler = new System.Windows.DragEventHandler(LibraryDropZoneDragOver);
        AddHandler(System.Windows.DragDrop.PreviewDragEnterEvent, dragHandler, handledEventsToo: true);
        AddHandler(System.Windows.DragDrop.PreviewDragOverEvent, dragHandler, handledEventsToo: true);
        AddHandler(System.Windows.DragDrop.DragEnterEvent, dragHandler, handledEventsToo: true);
        AddHandler(System.Windows.DragDrop.DragOverEvent, dragHandler, handledEventsToo: true);

        var dropHandler = new System.Windows.DragEventHandler(LibraryDropZoneDrop);
        AddHandler(System.Windows.DragDrop.PreviewDropEvent, dropHandler, handledEventsToo: true);
        AddHandler(System.Windows.DragDrop.DropEvent, dropHandler, handledEventsToo: true);
    }
}
