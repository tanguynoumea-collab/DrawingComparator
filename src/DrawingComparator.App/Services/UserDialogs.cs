using System.Windows;

using DrawingComparator.App.Views;

using Microsoft.Win32;

namespace DrawingComparator.App.Services;

public sealed class UserDialogs : IUserDialogs
{
    public async Task ShowErrorAsync(string title, string message)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "Fermer",
        };
        await box.ShowDialogAsync();
    }

    public string? PickPdfFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Plans PDF (*.pdf)|*.pdf",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public ExportRequest? ShowExportDialog()
    {
        var dialog = new ExportDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return null;

        var save = new SaveFileDialog
        {
            Title = "Exporter le comparatif",
            Filter = "Image PNG (*.png)|*.png",
            FileName = $"comparatif-{DateTime.Now:yyyyMMdd-HHmm}.png",
        };
        if (save.ShowDialog() != true)
            return null;

        return new ExportRequest(save.FileName, dialog.Dpi, dialog.CurrentViewOnly);
    }
}