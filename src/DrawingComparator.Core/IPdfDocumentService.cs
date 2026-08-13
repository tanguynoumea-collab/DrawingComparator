using SkiaSharp;

namespace DrawingComparator.Core;

/// <summary>Un document PDF ouvert : ses octets restent en mémoire, ses pages sont mesurées en points PDF.</summary>
public sealed record PdfDocumentInfo(string FilePath, int PageCount, IReadOnlyList<SKSize> PageSizesPoints);

public interface IPdfDocumentService
{
    /// <summary>Ouvre le PDF et mesure ses pages. Lève <see cref="PdfLoadException"/> en cas de fichier corrompu ou protégé.</summary>
    Task<PdfDocumentInfo> OpenAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Rasterise une page entière en niveaux de gris implicites (rendu noir sur blanc), au DPI demandé.
    /// Le bitmap retourné appartient à l'appelant (Dispose).
    /// </summary>
    Task<SKBitmap> RenderPageAsync(string filePath, int pageIndex, float dpi, CancellationToken ct = default);
}

/// <summary>Échec d'ouverture ou de rendu d'un PDF, avec un message destiné à l'utilisateur.</summary>
public sealed class PdfLoadException(string userMessage, Exception? inner = null) : Exception(userMessage, inner);
