using EbookManager.Presentation.Abstractions;
using EbookManager.Presentation.ViewModels;
using EbookManager.App.Views;
using Microsoft.Win32;

namespace EbookManager.App.Services;

public sealed class UserInteractionService(DeleteConfirmationService deleteConfirmationService) : IUserInteractionService
{
    private readonly DeleteConfirmationService deleteConfirmationService = deleteConfirmationService;

    public Task<IReadOnlyList<string>> PickBookFilesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "E-books|*.epub;*.kepub.epub;*.pdf;*.cbr;*.cbz;*.mobi;*.azw;*.azw3;*.kfx|All files|*.*"
        };

        var result = dialog.ShowDialog() == true
            ? dialog.FileNames.ToArray()
            : [];
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Task<string?> PickScanFolderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder to scan"
        };

        var result = dialog.ShowDialog() == true ? dialog.FolderName : null;
        return Task.FromResult(result);
    }

    public Task<string?> PickLibraryDirectoryAsync(string title, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        var result = dialog.ShowDialog() == true ? dialog.FolderName : null;
        return Task.FromResult(result);
    }

    public Task<bool> ConfirmDeleteAsync(string title, CancellationToken cancellationToken) =>
        deleteConfirmationService.ConfirmAsync(title, cancellationToken);

    public Task<string?> PromptTextAsync(
        string title,
        string message,
        string initialValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ShowTextPrompt(title, message, initialValue);
        return Task.FromResult(result);
    }

    public Task<bool> ConfirmMetadataValueRemovalAsync(
        string value,
        int affectedBookCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var message = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            EbookManager.App.Localization.LocalizedStrings.Current["FilterRemoveConfirmationMessage"],
            value,
            affectedBookCount);
        var result = System.Windows.MessageBox.Show(
            message,
            EbookManager.App.Localization.LocalizedStrings.Current["FilterRemoveConfirmationTitle"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        return Task.FromResult(result == System.Windows.MessageBoxResult.Yes);
    }

    public Task ShowImportResultAsync(ImportResultViewModel result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new ImportResultWindow(result);
        if (System.Windows.Application.Current?.MainWindow is { } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return Task.CompletedTask;
    }

    public Task<Guid?> PickImportRunAsync(ImportHistoryViewModel history, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new ImportHistoryWindow(history);
        if (System.Windows.Application.Current?.MainWindow is { } owner)
        {
            window.Owner = owner;
        }

        var result = window.ShowDialog() == true ? window.SelectedRunId : null;
        return Task.FromResult(result);
    }

    private static string? ShowTextPrompt(string title, string message, string initialValue)
    {
        var window = new System.Windows.Window
        {
            Title = EbookManager.App.Localization.LocalizedStrings.Current["FilterRenameTitle"],
            Width = 420,
            Height = 190,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
        };

        var panel = new System.Windows.Controls.StackPanel
        {
            Margin = new System.Windows.Thickness(20)
        };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                EbookManager.App.Localization.LocalizedStrings.Current["FilterRenameMessage"],
                initialValue),
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new System.Windows.Thickness(0, 0, 0, 8)
        });

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = initialValue,
            Margin = new System.Windows.Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(textBox);

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var saveButton = new System.Windows.Controls.Button
        {
            Content = EbookManager.App.Localization.LocalizedStrings.Current["Save"],
            MinWidth = 90,
            Margin = new System.Windows.Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var cancelButton = new System.Windows.Controls.Button
        {
            Content = EbookManager.App.Localization.LocalizedStrings.Current["Cancel"],
            MinWidth = 90,
            IsCancel = true
        };
        saveButton.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);
        window.Content = panel;
        window.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        if (System.Windows.Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true ? textBox.Text : null;
    }
}
