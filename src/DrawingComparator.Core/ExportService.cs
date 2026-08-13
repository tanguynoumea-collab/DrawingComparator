using SkiaSharp;

namespace DrawingComparator.Core;

public interface IExportService
{
    /// <summary>
    /// Exporte le comparatif en PNG sur l'emprise du plan de base, au DPI demandé.
    /// Si la surface dépasse <see cref="ExportService.MaxPixels"/>, le DPI est réduit d'autant et
    /// le DPI effectif est retourné (jamais d'échec silencieux, jamais d'explosion mémoire).
    /// </summary>
    Task<float> ExportPngAsync(string outputPath, SKSize baseSizePoints, float dpi,
        IReadOnlyList<LayerRenderInfo> layers, CancellationToken ct = default);

    /// <summary>Exporte une vue arbitraire (viewport écran) en PNG — même pipeline que la feuille entière.</summary>
    Task ExportViewPngAsync(string outputPath, SKSizeI sizePixels, SKMatrix baseToView,
        IReadOnlyList<LayerRenderInfo> layers, CancellationToken ct = default);
}

public sealed class ExportService(IComparisonCompositor compositor) : IExportService
{
    /// <summary>Garde-fou mémoire : ~120 Mpx ≈ 480 Mo BGRA le temps de l'encodage.</summary>
    public const double MaxPixels = 120_000_000;

    /// <summary>
    /// Échelle points→pixels retenue pour une feuille donnée : le DPI demandé,
    /// réduit si nécessaire pour tenir dans <see cref="MaxPixels"/>. Pur, testable.
    /// </summary>
    public static (float Scale, float EffectiveDpi) ComputeExportScale(SKSize sizePoints, float dpi)
    {
        float scale = dpi / 72f;
        double pixels = (double)sizePoints.Width * scale * sizePoints.Height * scale;
        if (pixels > MaxPixels)
        {
            scale *= (float)Math.Sqrt(MaxPixels / pixels);
        }
        return (scale, scale * 72f);
    }

    public Task<float> ExportPngAsync(string outputPath, SKSize baseSizePoints, float dpi,
        IReadOnlyList<LayerRenderInfo> layers, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var (scale, effectiveDpi) = ComputeExportScale(baseSizePoints, dpi);
            var size = new SKSizeI(
                Math.Max(1, (int)Math.Round(baseSizePoints.Width * scale)),
                Math.Max(1, (int)Math.Round(baseSizePoints.Height * scale)));

            ComposeAndSave(outputPath, size, SKMatrix.CreateScale(scale, scale), layers, ct);
            return effectiveDpi;
        }, ct);

    public Task ExportViewPngAsync(string outputPath, SKSizeI sizePixels, SKMatrix baseToView,
        IReadOnlyList<LayerRenderInfo> layers, CancellationToken ct = default)
        => Task.Run(() =>
        {
            // Même garde-fou mémoire que la feuille entière (dev-senior SEN-02).
            double pixels = (double)sizePixels.Width * sizePixels.Height;
            if (pixels > MaxPixels)
            {
                float f = (float)Math.Sqrt(MaxPixels / pixels);
                sizePixels = new SKSizeI(
                    Math.Max(1, (int)(sizePixels.Width * f)),
                    Math.Max(1, (int)(sizePixels.Height * f)));
                baseToView = SKMatrix.CreateScale(f, f).PreConcat(baseToView);
            }
            ComposeAndSave(outputPath, sizePixels, baseToView, layers, ct);
        }, ct);

    private void ComposeAndSave(string outputPath, SKSizeI size, SKMatrix baseToView,
        IReadOnlyList<LayerRenderInfo> layers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var bitmap = compositor.ComposeToBitmap(size, baseToView, layers);

        ct.ThrowIfCancellationRequested();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("L'encodage PNG a échoué.");
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
    }
}