using System.Runtime.Versioning;
using PDFtoImage;
using SkiaSharp;

namespace DrawingComparator.Core;

/// <summary>
/// Accès PDFium via PDFtoImage. PDFium n'est pas thread-safe : tous les appels
/// de rendu passent par un verrou global unique (<see cref="PdfiumLock"/>).
/// Les octets du document sont conservés en mémoire pour éviter les relectures disque.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PdfDocumentService : IPdfDocumentService
{
    private static readonly object PdfiumLock = new();

    private readonly Dictionary<string, byte[]> _bytesCache = [];
    private readonly object _cacheLock = new();

    public Task<PdfDocumentInfo> OpenAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() =>
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new PdfLoadException($"Impossible de lire le fichier « {Path.GetFileName(filePath)} » : {ex.Message}", ex);
            }

            lock (_cacheLock)
            {
                _bytesCache[filePath] = bytes;
            }

            try
            {
                lock (PdfiumLock)
                {
                    int pageCount = Conversion.GetPageCount(bytes);
                    var sizes = Conversion.GetPageSizes(bytes);
                    var sizesPoints = sizes.Select(s => new SKSize(s.Width, s.Height)).ToList();
                    return new PdfDocumentInfo(filePath, pageCount, sizesPoints);
                }
            }
            catch (Exception ex)
            {
                throw new PdfLoadException(TranslatePdfiumError(filePath, ex), ex);
            }
        }, ct);

    public Task<SKBitmap> RenderPageAsync(string filePath, int pageIndex, float dpi, CancellationToken ct = default)
        => Task.Run(() =>
        {
            byte[]? bytes;
            lock (_cacheLock)
            {
                _bytesCache.TryGetValue(filePath, out bytes);
            }
            bytes ??= File.ReadAllBytes(filePath);

            try
            {
                lock (PdfiumLock)
                {
                    ct.ThrowIfCancellationRequested();
                    var options = new RenderOptions(Dpi: (int)Math.Round(dpi), WithAnnotations: true, AntiAliasing: PdfAntiAliasing.All);
                    return Conversion.ToImage(bytes, page: pageIndex, options: options);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new PdfLoadException($"La page {pageIndex + 1} n'existe pas dans « {Path.GetFileName(filePath)} ».", ex);
            }
            catch (Exception ex)
            {
                throw new PdfLoadException(TranslatePdfiumError(filePath, ex), ex);
            }
        }, ct);

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
