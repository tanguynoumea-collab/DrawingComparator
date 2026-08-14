namespace DrawingComparator.App.Services;

public enum ExportFormat
{
    Png,
    Pdf,
}

/// <summary>Zone exportée : la feuille entière du plan de base, ou la vue courante à l'écran.
/// Le PDF (UAT cycle 2) est toujours la feuille entière — rasters embarqués au DPI choisi.</summary>
public sealed record ExportRequest(string OutputPath, float Dpi, bool CurrentViewOnly,
    ExportFormat Format = ExportFormat.Png);

public interface IUserDialogs
{
    Task ShowErrorAsync(string title, string message);

    /// <summary>Sélecteur de fichier PDF. Null si annulé.</summary>
    string? PickPdfFile(string title);

    /// <summary>Sélecteur combiné projet (.dcproj) OU plan PDF — le Ctrl+O unique. Null si annulé.</summary>
    string? PickProjectOrPdf();

    /// <summary>Choix de l'emplacement d'un projet à enregistrer (.dcproj). Null si annulé.</summary>
    string? PickProjectSavePath(string suggestedFileName);

    /// <summary>
    /// Un PDF du projet n'est plus à l'emplacement enregistré : demande à l'utilisateur de le
    /// retrouver (Parcourir) ou d'abandonner l'ouverture. Retourne le nouveau chemin, ou null.
    /// </summary>
    string? RelocateMissingFile(string missingPath);

    /// <summary>
    /// Le .dcproj référence un chemin réseau (UNC) : confirmation explicite avant toute
    /// résolution SMB (SEC2-01 — un projet reçu de tiers ne sonde jamais le réseau en silence).
    /// </summary>
    Task<bool> ConfirmOpenNetworkPathAsync(string path);

    /// <summary>Dialogue d'export (résolution + zone) puis choix du fichier de sortie. Null si annulé.</summary>
    ExportRequest? ShowExportDialog();

    /// <summary>
    /// Exécute l'export feuille entière derrière une ProgressBar déterminée + bouton Annuler
    /// (item 9). Retourne false si l'utilisateur a annulé. Les autres exceptions remontent.
    /// </summary>
    Task<bool> RunExportWithProgressAsync(Func<IProgress<double>, CancellationToken, Task> exportWork);
}