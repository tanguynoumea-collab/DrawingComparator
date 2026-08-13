namespace DrawingComparator.App.Services;

/// <summary>Zone exportée : la feuille entière du plan de base, ou la vue courante à l'écran.</summary>
public sealed record ExportRequest(string OutputPath, float Dpi, bool CurrentViewOnly);

public interface IUserDialogs
{
    Task ShowErrorAsync(string title, string message);

    /// <summary>Sélecteur de fichier PDF. Null si annulé.</summary>
    string? PickPdfFile(string title);

    /// <summary>Dialogue d'export (résolution + zone) puis choix du fichier de sortie. Null si annulé.</summary>
    ExportRequest? ShowExportDialog();
}