using EbookManager.Presentation.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace EbookManager.App.Views;

public partial class DuplicateCandidatesWindow : System.Windows.Window
{
    public DuplicateCandidatesWindow(DuplicateCandidatesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private void DuplicateRowDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: DuplicateCandidateRowViewModel row })
        {
            e.Handled = true;
            ShowDetails(row);
        }
    }

    private void ShowDetailsClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DuplicateCandidateRowViewModel row })
        {
            e.Handled = true;
            ShowDetails(row);
        }
    }

    private void ShowDetails(DuplicateCandidateRowViewModel row)
    {
        var window = new DuplicateCandidateDetailsWindow(row)
        {
            Owner = this
        };
        window.ShowDialog();
    }
}
