using SkiaSharp;

namespace DrawingComparator.Core;

/// <summary>
/// Un calque décrit pour l'export tuilé : le service rend lui-même les régions PDF nécessaires,
/// bande par bande — les rasters d'écran ne sont jamais réutilisés pour la feuille entière.
/// </summary>
public sealed record ExportLayer(
    string FilePath,
    int PageIndex,
    SKSize PageSizePoints,
    SKMatrix DocToBase,
    LayerTint Tint,
    float Strength,
    bool Binarize = false);

public interface IExportService
{
    /// <summary>
    /// Exporte le comparatif en PNG sur l'emprise du plan de base, au DPI demandé, en rendant
    /// chaque bande par région PDFium au DPI cible : le DPI annoncé est le DPI réel (SEN-11),
    /// quelle que soit la taille de la feuille — seul le plafond <see cref="ExportService.MaxSheetPixels"/>
    /// peut le réduire, et le DPI effectif est alors retourné. Une progression déterminée est
    /// émise par bande (0..1) ; l'annulation abandonne le fichier.
    /// </summary>
    Task<float> ExportSheetPngAsync(string outputPath, SKSize baseSizePoints, float dpi,
        IReadOnlyList<ExportLayer> layers, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Exporte une vue arbitraire (viewport écran, WYSIWYG) depuis les rasters fournis.</summary>
    Task ExportViewPngAsync(string outputPath, SKSizeI sizePixels, SKMatrix baseToView,
        IReadOnlyList<LayerRenderInfo> layers, CancellationToken ct = default);
}

public sealed class ExportService(IComparisonCompositor compositor, IPdfDocumentService pdfService) : IExportService
{
    /// <summary>Garde-fou mémoire de l'export vue (bitmap unique) : ~120 Mpx ≈ 480 Mo BGRA.</summary>
    public const double MaxPixels = 120_000_000;

    /// <summary>
    /// Garde-fou de la feuille entière (rendu par bandes : le pic = bitmap final + une bande) :
    /// ~200 Mpx couvre un A0 à 300 DPI (~140 Mpx) sans mentir sur le DPI.
    /// </summary>
    public const double MaxSheetPixels = 200_000_000;

    /// <summary>Budget de pixels d'une bande de rendu (rasters de région + composite de bande).</summary>
    private const double BandPixels = 16_000_000;

    /// <summary>
    /// Miroir de <c>PdfDocumentService.MaxRenderEdge</c> (classe annotée windows-only, CA1416
    /// interdit sa lecture depuis ce type neutre) ; l'égalité des deux est verrouillée par test.
    /// </summary>
    public const float MaxRegionEdgePx = 8192f;

    /// <summary>Marge ajoutée aux régions rendues (points PDF) pour absorber l'anti-aliasing aux joints de bandes.</summary>
    private const float BandSeamMarginPoints = 2f;

    /// <summary>
    /// Échelle points→pixels retenue pour une feuille donnée : le DPI demandé,
    /// réduit si nécessaire pour tenir dans le budget. Pur, testable.
    /// </summary>
    public static (float Scale, float EffectiveDpi) ComputeExportScale(SKSize sizePoints, float dpi,
        double maxPixels = MaxSheetPixels)
    {
        float scale = dpi / 72f;
        double pixels = (double)sizePoints.Width * scale * sizePoints.Height * scale;
        if (pixels > maxPixels)
        {
            scale *= (float)Math.Sqrt(maxPixels / pixels);
        }
        return (scale, scale * 72f);
    }

    /// <summary>
    /// Facteur d'échelle du calage d'un calque (√|det|) : le rendu de sa région doit se faire
    /// à DPI × facteur DANS SON PROPRE ESPACE pour arriver au DPI cible une fois transformé
    /// vers la base (dev-senior SEN2-04 — plans à échelles différentes).
    /// </summary>
    public static float LayerDpiFactor(SKMatrix docToBase)
    {
        double det = Math.Abs(docToBase.ScaleX * (double)docToBase.ScaleY - docToBase.SkewX * (double)docToBase.SkewY);
        return det > 0 ? (float)Math.Sqrt(det) : 1f;
    }

    public async Task<float> ExportSheetPngAsync(string outputPath, SKSize baseSizePoints, float dpi,
        IReadOnlyList<ExportLayer> layers, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var (scale, effectiveDpi) = ComputeExportScale(baseSizePoints, dpi);
        var size = new SKSizeI(
            Math.Max(1, (int)Math.Round(baseSizePoints.Width * scale)),
            Math.Max(1, (int)Math.Round(baseSizePoints.Height * scale)));

        // Vraies tuiles X×Y (dev-senior SEN2-01) : une bande pleine largeur d'un A0 à 300 DPI
        // dépasserait MaxRenderEdge et PDFium rendrait à un DPI dégradé silencieusement —
        // chaque tuile doit rester sous le plafond d'arête, marges de jointure comprises.
        int tileWidthPx = Math.Min(size.Width,
            (int)(MaxRegionEdgePx - 4 * BandSeamMarginPoints * scale - 8));
        int tileHeightPx = Math.Max(256, (int)(BandPixels / tileWidthPx));
        int cols = (size.Width + tileWidthPx - 1) / tileWidthPx;
        int rows = (size.Height + tileHeightPx - 1) / tileHeightPx;
        int tileCount = cols * rows;

        ct.ThrowIfCancellationRequested();
        using var sheet = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var sheetCanvas = new SKCanvas(sheet))
        {
            for (int tile = 0; tile < tileCount; tile++)
            {
                ct.ThrowIfCancellationRequested();
                int x0 = tile % cols * tileWidthPx;
                int y0 = tile / cols * tileHeightPx;
                int tileW = Math.Min(tileWidthPx, size.Width - x0);
                int tileH = Math.Min(tileHeightPx, size.Height - y0);

                // Emprise de la tuile en points du plan de base, avec marge anti-jointure.
                var tileRectPoints = new SKRect(x0 / scale, y0 / scale, (x0 + tileW) / scale, (y0 + tileH) / scale);
                tileRectPoints.Inflate(BandSeamMarginPoints, BandSeamMarginPoints);

                var infos = new List<LayerRenderInfo>(layers.Count);
                var tileImages = new List<SKImage>(layers.Count);
                try
                {
                    foreach (var layer in layers)
                    {
                        if (layer.Strength <= 0f || !layer.DocToBase.TryInvert(out var baseToDoc))
                            continue;

                        var region = baseToDoc.MapRect(tileRectPoints);
                        region.Inflate(BandSeamMarginPoints, BandSeamMarginPoints);
                        region.Intersect(new SKRect(0, 0, layer.PageSizePoints.Width, layer.PageSizePoints.Height));
                        if (region.Width <= 0 || region.Height <= 0)
                            continue; // le calque ne couvre pas cette tuile

                        var raster = await pdfService.RenderRegionAsync(
                            layer.FilePath, layer.PageIndex, region,
                            effectiveDpi * LayerDpiFactor(layer.DocToBase), ct).ConfigureAwait(false);
                        raster.SetImmutable();
                        var image = SKImage.FromBitmap(raster);
                        raster.Dispose();
                        tileImages.Add(image);

                        // L'échelle réelle se mesure sur les pixels rendus, par axe (arrondi entier du DPI).
                        infos.Add(new LayerRenderInfo(
                            image,
                            RasterScale: image.Width / region.Width,
                            layer.DocToBase,
                            layer.Tint,
                            layer.Strength,
                            RegionOriginDoc: new SKPoint(region.Left, region.Top),
                            RasterScaleY: image.Height / region.Height,
                            layer.Binarize));
                    }

                    ct.ThrowIfCancellationRequested();
                    var tileView = SKMatrix.CreateTranslation(-x0, -y0).PreConcat(SKMatrix.CreateScale(scale, scale));
                    using var tileBitmap = compositor.ComposeToBitmap(new SKSizeI(tileW, tileH), tileView, infos);
                    // Blit 1:1 : aucun ré-échantillonnage, la tuile tombe pile sur ses pixels.
                    sheetCanvas.DrawBitmap(tileBitmap, new SKPoint(x0, y0), new SKSamplingOptions(SKFilterMode.Nearest));
                }
                finally
                {
                    foreach (var image in tileImages)
                        image.Dispose();
                }

                progress?.Report((tile + 1) / (double)tileCount);
            }
        }

        ct.ThrowIfCancellationRequested();
        SavePng(outputPath, sheet);
        return effectiveDpi;
    }

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
            ct.ThrowIfCancellationRequested();
            using var bitmap = compositor.ComposeToBitmap(sizePixels, baseToView, layers);
            ct.ThrowIfCancellationRequested();
            SavePng(outputPath, bitmap);
        }, ct);

    private static void SavePng(string outputPath, SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("L'encodage PNG a échoué.");
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
    }
}