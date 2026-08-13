using SkiaSharp;

namespace DrawingComparator.Core;

public enum LayerTint { Red, Blue }

/// <summary>
/// Un calque prêt à composer : son raster (rendu noir sur blanc), l'échelle de rasterisation
/// (pixels par point PDF), sa transformation vers le repère du plan de base (identité pour
/// le plan de base lui-même, matrice de calage pour le plan révisé), sa teinte et son intensité.
/// </summary>
/// <param name="Strength">
/// Intensité effective 0..1 = opacité utilisateur × atténuation éventuelle (mode calage).
/// Sous blending multiplicatif, elle s'applique en interpolation vers le blanc, jamais en alpha.
/// </param>
public sealed record LayerRenderInfo(
    SKImage Image,
    float RasterScale,
    SKMatrix DocToBase,
    LayerTint Tint,
    float Strength);

public interface IComparisonCompositor
{
    /// <summary>Compose les calques teintés en multiply sur fond blanc, dans le repère écran donné.</summary>
    /// <param name="baseToView">points PDF du plan de base → pixels du viewport.</param>
    void Compose(SKCanvas canvas, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers);

    /// <summary>Compose dans un bitmap neuf de la taille demandée (viewport écran, loupe, export).</summary>
    SKBitmap ComposeToBitmap(SKSizeI size, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers);
}

/// <summary>
/// Compositeur Skia : fond blanc, chaque calque dessiné avec un filtre de teinte
/// (rouge : R'=255, G'=B'=lerp(255,L,strength) ; bleu symétrique) en SKBlendMode.Multiply.
/// Le blanc étant l'élément neutre du multiply, un trait présent sur un seul plan reste
/// rouge ou bleu pur, un trait commun ressort en violet très sombre, et strength=0 fait
/// disparaître le calque (il devient blanc).
/// </summary>
public sealed class ComparisonCompositor : IComparisonCompositor
{
    // CatmullRom : cubique interpolante (B=0) — restitue exactement le raster à l'échelle 1:1,
    // là où Mitchell (B=1/3) floute même sans transformation.
    private static readonly SKSamplingOptions Magnify = new(SKCubicResampler.CatmullRom);

    // En minification forte (vue d'ensemble d'un raster 300 DPI), un filtre cubique
    // sous-échantillonne et fait décrocher les traits fins : il faut des mipmaps.
    private static readonly SKSamplingOptions Minify = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public void Compose(SKCanvas canvas, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers)
    {
        canvas.Clear(SKColors.White);

        foreach (var layer in layers)
        {
            if (layer.Strength <= 0f)
                continue;

            // pixels du raster → points PDF du calque → points du plan de base → pixels écran
            var rasterToDoc = SKMatrix.CreateScale(1f / layer.RasterScale, 1f / layer.RasterScale);
            var total = baseToView;
            total = total.PreConcat(layer.DocToBase);
            total = total.PreConcat(rasterToDoc);

            using var paint = new SKPaint();
            paint.ColorFilter = CreateTintFilter(layer.Tint, layer.Strength);
            paint.BlendMode = SKBlendMode.Multiply;

            double scale = Math.Sqrt(Math.Abs(total.ScaleX * (double)total.ScaleY - total.SkewX * (double)total.SkewY));
            var sampling = scale < 0.95 ? Minify : Magnify;

            canvas.Save();
            canvas.Concat(in total);
            canvas.DrawImage(layer.Image, 0, 0, sampling, paint);
            canvas.Restore();
        }
    }

    public SKBitmap ComposeToBitmap(SKSizeI size, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            Compose(canvas, baseToView, layers);
        }
        return bitmap;
    }

    /// <summary>
    /// Matrice couleur de teinte. Le raster source est noir sur blanc (R=G=B=L).
    /// L'intensité est une interpolation vers le blanc : canal teinté saturé à 255,
    /// canaux opposés = strength·L + (1−strength)·255.
    /// </summary>
    internal static SKColorFilter CreateTintFilter(LayerTint tint, float strength)
    {
        float s = Math.Clamp(strength, 0f, 1f);
        // Colonne de translation en espace normalisé 0..1 (convention Skia moderne).
        float offset = 1f - s;

        float[] matrix = tint == LayerTint.Red
            ? [
                0, 0, 0, 0, 1,       // R' = 1 (canal teinté saturé)
                s, 0, 0, 0, offset,  // G' = s·L + (1−s)
                s, 0, 0, 0, offset,  // B' = idem
                0, 0, 0, 0, 1,       // A' = opaque
              ]
            : [
                s, 0, 0, 0, offset,
                s, 0, 0, 0, offset,
                0, 0, 0, 0, 1,
                0, 0, 0, 0, 1,
              ];

        return SKColorFilter.CreateColorMatrix(matrix);
    }
}
