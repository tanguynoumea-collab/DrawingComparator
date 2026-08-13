using System.IO;
using System.Windows;

using DrawingComparator.App.Views;
using DrawingComparator.Core;

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

    public string? PickProjectOrPdf()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Ouvrir un projet ou un plan",
            Filter = "Projets et plans|*" + ProjectSerializer.FileExtension + ";*.pdf" +
                     "|Projets DrawingComparator (*" + ProjectSerializer.FileExtension + ")|*" + ProjectSerializer.FileExtension +
                     "|Plans PDF (*.pdf)|*.pdf",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickProjectSavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Enregistrer le projet",
            Filter = "Projet DrawingComparator (*" + ProjectSerializer.FileExtension + ")|*" + ProjectSerializer.FileExtension,
            FileName = suggestedFileName,
            DefaultExt = ProjectSerializer.FileExtension,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public async Task<bool> ConfirmOpenNetworkPathAsync(string path)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Chemin réseau dans le projet",
            Content = $"Ce projet référence un emplacement réseau :\n{path}\n\n" +
                      "S'y connecter enverra vos identifiants Windows à ce serveur. " +
                      "Ne continuez que si vous connaissez ce partage.",
            PrimaryButtonText = "Se connecter",
            CloseButtonText = "Ne pas s'y connecter",
        };
        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    public string? RelocateMissingFile(string missingPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Retrouver « {Path.GetFileName(missingPath)} » (réseau déconnecté ? fichier déplacé ?)",
            Filter = "Plans PDF (*.pdf)|*.pdf",
            FileName = Path.GetFileName(missingPath),
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

    public async Task<bool> RunExportWithProgressAsync(Func<IProgress<double>, CancellationToken, Task> exportWork)
    {
        using var dialog = new ExportProgressDialog { Owner = Application.Current.MainWindow };
        var progress = new Progress<double>(dialog.SetProgress);
        var task = exportWork(progress, dialog.CancellationToken);

        // Le dialogue se ferme quand l'export a réellement fini (succès, erreur, annulation actée).
        _ = task.ContinueWith(
            _ => dialog.Dispatcher.Invoke(dialog.CompleteAndClose),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        dialog.ShowDialog();

        try
        {
            await task;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}