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

/// <summary>
/// Mode d'affichage du comparatif (UAT cycle 2, « calque des différences ») :
/// la superposition classique, ou le calque des ÉCARTS seuls — les traits communs
/// (sombres sous multiply) s'effacent, ne restent que le rouge et le bleu —
/// éventuellement posé sur l'un des deux plans en gris de contexte.
/// </summary>
public enum CompareViewMode
{
    Overlay,
    DifferencesOnly,
    DifferencesOverBase,
    DifferencesOverRevision,
}

public interface IComparisonCompositor
{
    /// <summary>Compose dans un bitmap neuf de la taille demandée (viewport écran, loupe, export).</summary>
    /// <remarks>
    /// Sûr d'être appelé depuis un thread de fond : les SKImage sources sont immuables et
    /// leur lecture concurrente est supportée par Skia ; leur durée de vie est garantie par
    /// Begin/EndBackgroundCompose côté appelant (SEN-07, FIA-01).
    /// Convention d'ordre des calques : la BASE d'abord, le RÉVISÉ ensuite — les modes
    /// « différences sur … » désignent leur fond par cet index.
    /// </remarks>
    SKBitmap ComposeToBitmap(SKSizeI size, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers,
        CompareViewMode mode = CompareViewMode.Overlay);
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
    private static void Compose(SKCanvas canvas, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers,
        CompareViewMode mode)
    {
        canvas.Clear(SKColors.White);

        if (mode == CompareViewMode.Overlay)
        {
            foreach (var layer in layers)
                DrawLayer(canvas, baseToView, layer, CreateLayerFilter(layer));
            return;
        }

        // Fond de contexte : le plan choisi en gris discret (neutre pour le multiply du calque diff).
        int backdropIndex = mode switch
        {
            CompareViewMode.DifferencesOverBase => 0,
            CompareViewMode.DifferencesOverRevision => 1,
            _ => -1,
        };
        if (backdropIndex >= 0 && backdropIndex < layers.Count)
            DrawLayer(canvas, baseToView, layers[backdropIndex], CreateBackdropFilter(layers[backdropIndex]));

        // Calque des différences : le composite multiply classique, dont les traits COMMUNS
        // (sombres : rouge×bleu → noir) sont effacés au restore par le filtre SKSL, puis le
        // résultat est multiplié sur le fond — le blanc reste neutre, les écarts se posent.
        using var diffPaint = new SKPaint
        {
            ColorFilter = DifferencesFilter,
            BlendMode = SKBlendMode.Multiply,
        };
        canvas.SaveLayer(diffPaint);
        canvas.Clear(SKColors.White);
        foreach (var layer in layers)
            DrawLayer(canvas, baseToView, layer, CreateLayerFilter(layer));
        canvas.Restore();
    }

    private static void DrawLayer(SKCanvas canvas, SKMatrix baseToView, LayerRenderInfo layer, SKColorFilter filter)
    {
        if (layer.Strength <= 0f)
            return;

        // pixels du raster → points PDF du calque (échelles par axe + origine de région, SEN-14)
        // → points du plan de base → pixels écran
        float scaleY = layer.RasterScaleY > 0f ? layer.RasterScaleY : layer.RasterScale;
        var rasterToDoc = SKMatrix.CreateTranslation(layer.RegionOriginDoc.X, layer.RegionOriginDoc.Y)
            .PreConcat(SKMatrix.CreateScale(1f / layer.RasterScale, 1f / scaleY));
        var total = baseToView;
        total = total.PreConcat(layer.DocToBase);
        total = total.PreConcat(rasterToDoc);

        using var paint = new SKPaint();
        paint.ColorFilter = filter;
        paint.BlendMode = SKBlendMode.Multiply;

        double scale = Math.Sqrt(Math.Abs(total.ScaleX * (double)total.ScaleY - total.SkewX * (double)total.SkewY));
        var sampling = scale < 0.95 ? Minify : Magnify;

        canvas.Save();
        canvas.Concat(in total);
        canvas.DrawImage(layer.Image, 0, 0, sampling, paint);
        canvas.Restore();
    }

    /// <summary>Filtre normal d'un calque : sa teinte, précédée du seuillage si binarisation.</summary>
    private static SKColorFilter CreateLayerFilter(LayerRenderInfo layer)
    {
        var tint = CreateTintFilter(layer.Tint, layer.Strength);
        return layer.Binarize
            ? SKColorFilter.CreateCompose(tint, CreateBinarizeFilter())
            : tint;
    }

    public SKBitmap ComposeToBitmap(SKSizeI size, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers,
        CompareViewMode mode = CompareViewMode.Overlay)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            Compose(canvas, baseToView, layers, mode);
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

    /// <summary>
    /// Filtre du mode Différences : un pixel dont NI le canal rouge NI le canal bleu n'est
    /// vif est un trait commun (multiply rouge×bleu → sombre) → effacé (blanc). Les écarts
    /// rouges (R saturé) et bleus (B saturé) traversent intacts. SKSL, évalué au restore
    /// du SaveLayer — aucune passe CPU sur le composite.
    /// </summary>
    private static readonly SKColorFilter DifferencesFilter = CreateDifferencesFilter();

    private static SKColorFilter CreateDifferencesFilter()
    {
        // Seuil 0,55 : à opacité ≥ 50 %, le produit des deux teintes passe sous le seuil
        // (effacé) tandis que le canal saturé d'un écart reste à 1 (conservé).
        const string Sksl = """
            half4 main(half4 c) {
                half strongest = max(c.r, c.b);
                return strongest < 0.55 ? half4(c.a, c.a, c.a, c.a) : c;
            }
            """;
        var effect = SKRuntimeEffect.CreateColorFilter(Sksl, out string? errors);
        if (effect is null)
            throw new InvalidOperationException($"Filtre Différences (SKSL) : {errors}");
        return effect.ToColorFilter();
    }

    /// <summary>
    /// Fond de contexte des modes « différences sur … » : le plan en GRIS discret
    /// (R'=G'=B' = s·L + (1−s), s réduit) — il situe les écarts sans leur voler leur couleur.
    /// </summary>
    private const float BackdropStrengthFactor = 0.30f;

    private static SKColorFilter CreateBackdropFilter(LayerRenderInfo layer)
    {
        float s = Math.Clamp(layer.Strength, 0f, 1f) * BackdropStrengthFactor;
        float offset = 1f - s;
        float lr = s * LumR, lg = s * LumG, lb = s * LumB;
        float[] matrix =
        [
            lr, lg, lb, 0, offset,
            lr, lg, lb, 0, offset,
            lr, lg, lb, 0, offset,
            0, 0, 0, 0, 1,
        ];
        var gray = SKColorFilter.CreateColorMatrix(matrix);
        return layer.Binarize
            ? SKColorFilter.CreateCompose(gray, CreateBinarizeFilter())
            : gray;
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