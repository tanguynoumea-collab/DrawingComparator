using SkiaSharp;

namespace DrawingComparator.Core;

/// <summary>Un document PDF ouvert : ses octets restent en mémoire, ses pages sont mesurées en points PDF.</summary>
public sealed record PdfDocumentInfo(string FilePath, int PageCount, IReadOnlyList<SKSize> PageSizesPoints);

/// <summary>
/// Contrat avec cycle de vie et budget de ressources (dev-council n°1, groupe ARC-03/SEC-01/CROSS-02) :
/// un document est ouvert puis LIBÉRÉ ; le rendu est budgété EN PIXELS côté service, sur les tailles
/// que le service a lui-même mesurées — un DPI demandé excessif ou une MediaBox géante ne peuvent
/// pas provoquer d'allocation démesurée.
/// </summary>
public interface IPdfDocumentService
{
    /// <summary>Ouvre le PDF (comptage de références par chemin) et mesure ses pages. Lève <see cref="PdfLoadException"/>.</summary>
    Task<PdfDocumentInfo> OpenAsync(string filePath, CancellationToken ct = default);

    /// <summary>Libère une ouverture. Quand plus personne ne tient le document, ses octets sont purgés.</summary>
    void Release(string filePath);

    /// <summary>
    /// Rasterise une page entière (rendu noir sur blanc) au DPI demandé, plafonné par le service
    /// (grand côté ≤ <see cref="PdfDocumentService.MaxRenderEdge"/> px, surface ≤ <see cref="PdfDocumentService.MaxRenderPixels"/>).
    /// Le document doit être ouvert. Le bitmap retourné appartient à l'appelant.
    /// </summary>
    Task<SKBitmap> RenderPageAsync(string filePath, int pageIndex, float dpi, CancellationToken ct = default);
}

/// <summary>Échec d'ouverture ou de rendu d'un PDF, avec un message destiné à l'utilisateur.</summary>
public sealed class PdfLoadException(string userMessage, Exception? inner = null) : Exception(userMessage, inner);