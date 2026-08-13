using System.Runtime.Versioning;

using PDFtoImage;

using SkiaSharp;

namespace DrawingComparator.Core;

/// <summary>
/// Accès PDFium via PDFtoImage. PDFium n'est pas thread-safe : tous les appels
/// de rendu passent par un verrou global unique (<see cref="PdfiumLock"/>).
/// Les octets d'un document ouvert sont conservés en mémoire (jamais relus par
/// chemin : l'identité du document est épinglée à l'ouverture) et libérés au
/// dernier <see cref="Release"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PdfDocumentService : IPdfDocumentService
{
    /// <summary>Plafond du grand côté d'un raster (limite de texture GPU courante).</summary>
    public const float MaxRenderEdge = 8192f;

    /// <summary>Plafond de surface d'un raster (~70 Mpx ≈ 280 Mo BGRA) — inviolable quel que soit le DPI demandé.</summary>
    public const double MaxRenderPixels = 70_000_000;

    private static readonly object PdfiumLock = new();

    private sealed record OpenDocument(byte[] Bytes, PdfDocumentInfo Info)
    {
        public int RefCount { get; set; } = 1;
    }

    private readonly Dictionary<string, OpenDocument> _documents = [];
    private readonly object _cacheLock = new();

    public Task<PdfDocumentInfo> OpenAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() =>
        {
            lock (_cacheLock)
            {
                if (_documents.TryGetValue(filePath, out var existing))
                {
                    existing.RefCount++;
                    return existing.Info;
                }
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new PdfLoadException($"Impossible de lire le fichier « {Path.GetFileName(filePath)} » : {ex.Message}", ex);
            }

            PdfDocumentInfo info;
            try
            {
                lock (PdfiumLock)
                {
                    int pageCount = Conversion.GetPageCount(bytes);
                    var sizes = Conversion.GetPageSizes(bytes);
                    info = new PdfDocumentInfo(filePath, pageCount,
                        sizes.Select(s => new SKSize(s.Width, s.Height)).ToList());
                }
            }
            catch (Exception ex)
            {
                throw new PdfLoadException(TranslatePdfiumError(filePath, ex), ex);
            }

            lock (_cacheLock)
            {
                if (_documents.TryGetValue(filePath, out var raced))
                {
                    raced.RefCount++;
                    return raced.Info;
                }
                _documents[filePath] = new OpenDocument(bytes, info);
            }
            return info;
        }, ct);

    public void Release(string filePath)
    {
        lock (_cacheLock)
        {
            if (_documents.TryGetValue(filePath, out var doc) && --doc.RefCount <= 0)
                _documents.Remove(filePath);
        }
    }

    public Task<SKBitmap> RenderPageAsync(string filePath, int pageIndex, float dpi, CancellationToken ct = default)
        => Task.Run(() =>
        {
            OpenDocument? doc;
            lock (_cacheLock)
            {
                _documents.TryGetValue(filePath, out doc);
            }
            if (doc is null)
                throw new PdfLoadException($"« {Path.GetFileName(filePath)} » n'est plus ouvert — rechargez-le.");
            if (pageIndex < 0 || pageIndex >= doc.Info.PageCount)
                throw new PdfLoadException($"La page {pageIndex + 1} n'existe pas dans « {Path.GetFileName(filePath)} » ({doc.Info.PageCount} pages).");

            float cappedDpi = CapDpi(doc.Info.PageSizesPoints[pageIndex], dpi);

            try
            {
                lock (PdfiumLock)
                {
                    ct.ThrowIfCancellationRequested();
                    var options = new RenderOptions(Dpi: (int)Math.Round(cappedDpi), WithAnnotations: true, AntiAliasing: PdfAntiAliasing.All);
                    return Conversion.ToImage(doc.Bytes, page: pageIndex, options: options);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PdfLoadException(TranslatePdfiumError(filePath, ex), ex);
            }
        }, ct);

    /// <summary>
    /// Budget de rendu, appliqué sur les tailles mesurées par le service : le DPI effectif
    /// garantit grand côté ≤ <see cref="MaxRenderEdge"/> ET surface ≤ <see cref="MaxRenderPixels"/>,
    /// sans plancher — une MediaBox géante réduit le DPI au lieu de faire exploser l'allocation. Pur, testable.
    /// </summary>
    public static float CapDpi(SKSize pageSizePoints, float requestedDpi)
    {
        float dpi = Math.Max(1f, requestedDpi);
        float scale = dpi / 72f;

        float longSidePt = Math.Max(pageSizePoints.Width, pageSizePoints.Height);
        if (longSidePt * scale > MaxRenderEdge)
            scale = MaxRenderEdge / longSidePt;

        double pixels = (double)pageSizePoints.Width * scale * pageSizePoints.Height * scale;
        if (pixels > MaxRenderPixels)
            scale *= (float)Math.Sqrt(MaxRenderPixels / pixels);

        return scale * 72f;
    }

    private static string TranslatePdfiumError(string filePath, Exception ex)
    {
        string name = Path.GetFileName(filePath);
        string msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("password"))
            return $"« {name} » est protégé par mot de passe. Déverrouillez-le dans votre lecteur PDF puis rechargez-le.";
        if (msg.Contains("format") || msg.Contains("corrupt") || msg.Contains("invalid"))
            return $"« {name} » n'est pas un PDF valide ou est corrompu.";
        return $"Impossible d'ouvrir « {name} » : {ex.Message}";
    }
}