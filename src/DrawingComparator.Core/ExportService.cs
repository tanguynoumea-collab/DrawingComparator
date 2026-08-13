using SkiaSharp;

namespace DrawingComparator.Core;

public interface IExportService
{
    /// <summary>
    /// Exporte le comparatif en PNG sur l'emprise du plan de base, au DPI demandé.
    /// Si la surface dépasse <see cref="MaxPixels"/>, le DPI est réduit d'autant et
    /// le DPI effectif est retourné (jamais d'échec silencieux, jamais d'explosion mémoire).
    /// </summary>
    Task<float> ExportPngAsync(string outputPath, SKSize baseSizePoints, float dpi,
        IReadOnlyList<LayerRenderInfo> layers, CancellationToken ct = default);
}

public sealed class ExportService(IComparisonCompositor compositor) : IExportService
{
    /// <summary>Garde-fou mémoire : ~120 Mpx ≈ 480 Mo BGRA le temps de l'encodage.</summary>
    public const double MaxPixels = 120_000_000;

    public Task<float> ExportPngAsync(string outputPath, SKSize baseSizePoints, float dpi,
        IReadOnlyList<LayerRenderInfo> layers, CancellationToken ct = default)
        => Task.Run(() =>
        {
            float scale = dpi / 72f;
            double pixels = (double)baseSizePoints.Width * scale * baseSizePoints.Height * scale;
            if (pixels > MaxPixels)
            {
                scale *= (float)Math.Sqrt(MaxPixels / pixels);
            }
            float effectiveDpi = scale * 72f;

            var size = new SKSizeI(
                Math.Max(1, (int)Math.Round(baseSizePoints.Width * scale)),
                Math.Max(1, (int)Math.Round(baseSizePoints.Height * scale)));

            ct.ThrowIfCancellationRequested();
            using var bitmap = compositor.ComposeToBitmap(size, SKMatrix.CreateScale(scale, scale), layers);

            ct.ThrowIfCancellationRequested();
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("L'encodage PNG a échoué.");
            using var stream = File.Create(outputPath);
            data.SaveTo(stream);

            return effectiveDpi;
        }, ct);
}
