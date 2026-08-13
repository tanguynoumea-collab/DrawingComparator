using SkiaSharp;

namespace DrawingComparator.Core;

public enum LayerTint { Red, Blue }

/// <summary>
/// Un calque prêt à composer : son raster (rendu noir sur blanc), l'échelle de rasterisation
/// (pixels par point PDF), sa transformation vers le repère du plan de base (identité pour
/// le plan de base lui-même, matrice de calage pour le plan révisé), sa teinte et son intensité.
/// </summary>
/// <param name="Strength">
/// Intensité effective 0..1 = opacité utilisateur. Sous blending multiplicatif,
/// elle s'applique en interpolation vers le blanc, jamais en alpha.
/// </param>
/// <param name="RegionOriginDoc">
/// Origine du raster en points PDF du calque (SEN-14) : (0,0) pour un raster pleine page,
/// le coin haut-gauche de la région pour une tuile de rendu par région.
/// </param>
/// <param name="RasterScaleY">
/// Échelle verticale si elle diffère de <paramref name="RasterScale"/> (l'arrondi entier du DPI
/// sur une petite région rend les axes légèrement anisotropes) ; 0 = identique à l'horizontale.
/// </param>
/// <param name="Binarize">
/// Nettoyage des PDF scannés : seuillage de luminance appliqué AVANT la teinte
/// (fond gris → blanc, traits → pleine intensité). Sans effet notable sur un PDF vectoriel net.
/// </param>
public sealed record LayerRenderInfo(
    SKImage Image,
    float RasterScale,
    SKMatrix DocToBase,
    LayerTint Tint,
    float Strength,
    SKPoint RegionOriginDoc = default,
    float RasterScaleY = 0f,
    bool Binarize = false);

public interface IComparisonCompositor
{
    /// <summary>Compose dans un bitmap neuf de la taille demandée (viewport écran, loupe, export).</summary>
    /// <remarks>
    /// Sûr d'être appelé depuis un thread de fond : les SKImage sources sont immuables et
    /// leur lecture concurrente est supportée par Skia ; leur durée de vie est garantie par
    /// Begin/EndBackgroundCompose côté appelant (SEN-07, FIA-01).
    /// </remarks>
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

    // Privée depuis le cycle 2 (PERT-05) : tout consommateur externe passe par ComposeToBitmap.
    private static void Compose(SKCanvas canvas, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers)
    {
        canvas.Clear(SKColors.White);

        foreach (var layer in layers)
        {
            if (layer.Strength <= 0f)
                continue;

            // pixels du raster → points PDF du calque (échelles par axe + origine de région, SEN-14)
            // → points du plan de base → pixels écran
            float scaleY = layer.RasterScaleY > 0f ? layer.RasterScaleY : layer.RasterScale;
            var rasterToDoc = SKMatrix.CreateTranslation(layer.RegionOriginDoc.X, layer.RegionOriginDoc.Y)
                .PreConcat(SKMatrix.CreateScale(1f / layer.RasterScale, 1f / scaleY));
            var total = baseToView;
            total = total.PreConcat(layer.DocToBase);
            total = total.PreConcat(rasterToDoc);

            using var paint = new SKPaint();
            var tint = CreateTintFilter(layer.Tint, layer.Strength);
            paint.ColorFilter = layer.Binarize
                ? SKColorFilter.CreateCompose(tint, CreateBinarizeFilter())
                : tint;
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
    // Luminance BT.709 : un trait source COLORÉ (nuage de révision rouge, surligneur
    // jaune) doit rester visible dans le comparatif — prendre un seul canal comme
    // luminance ferait disparaître silencieusement ces traits (dev-senior SEN-09).
    private const float LumR = 0.2126f;
    private const float LumG = 0.7152f;
    private const float LumB = 0.0722f;

    internal static SKColorFilter CreateTintFilter(LayerTint tint, float strength)
    {
        float s = Math.Clamp(strength, 0f, 1f);
        // Colonne de translation en espace normalisé 0..1 (convention Skia moderne).
        float offset = 1f - s;
        float lr = s * LumR, lg = s * LumG, lb = s * LumB;

        float[] matrix = tint == LayerTint.Red
            ? [
                0, 0, 0, 0, 1,          // R' = 1 (canal teinté saturé)
                lr, lg, lb, 0, offset,  // G' = s·luminance + (1−s)
                lr, lg, lb, 0, offset,  // B' = idem
                0, 0, 0, 0, 1,          // A' = opaque
              ]
            : [
                lr, lg, lb, 0, offset,
                lr, lg, lb, 0, offset,
                0, 0, 0, 0, 1,
                0, 0, 0, 0, 1,
              ];

        return SKColorFilter.CreateColorMatrix(matrix);
    }

    /// <summary>Seuil de binarisation sur la luminance BT.709 : au-dessus = fond (blanc), en dessous = trait (noir).</summary>
    private const int BinarizeThreshold = 209; // ≈ 0,82 — le gris de fond des scans passe au blanc, les traits restent.

    /// <summary>
    /// Filtre de binarisation (roadmap item 8) : luminance BT.709 dans les trois canaux puis
    /// seuillage par table. Composé AVANT la teinte — zéro passe raster supplémentaire, réversible,
    /// et le raster source reste intact (la logique de région n'est pas affectée).
    /// </summary>
    internal static SKColorFilter CreateBinarizeFilter()
    {
        float[] toLuminance =
        [
            LumR, LumG, LumB, 0, 0,
            LumR, LumG, LumB, 0, 0,
            LumR, LumG, LumB, 0, 0,
            0, 0, 0, 0, 1,
        ];

        var table = new byte[256];
        var identity = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            table[i] = i >= BinarizeThreshold ? (byte)255 : (byte)0;
            identity[i] = (byte)i;
        }

        return SKColorFilter.CreateCompose(
            SKColorFilter.CreateTable(identity, table, table, table),
            SKColorFilter.CreateColorMatrix(toLuminance));
    }
}