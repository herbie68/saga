using EbookManager.Presentation.ViewModels;

namespace EbookManager.App.Views;

public partial class ImportResultWindow : System.Windows.Window
{
    public ImportResultWindow(ImportResultViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseClicked(object sender, System.Windows.RoutedEventArgs e) => Close();
}
