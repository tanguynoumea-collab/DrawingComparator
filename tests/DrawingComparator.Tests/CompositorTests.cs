using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

public class CompositorTests
{
    /// <summary>Raster 2×2 noir sur blanc : (0,0) noir, le reste blanc.</summary>
    private static SKImage MakeRaster()
    {
        using var bmp = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.White);
        bmp.SetPixel(0, 0, SKColors.Black);
        bmp.SetImmutable();
        return SKImage.FromBitmap(bmp);
    }

    [Fact]
    public void CommonStroke_IsDark_UniqueStrokes_KeepPureTints()
    {
        using var raster1 = MakeRaster();
        using var raster2 = MakeRaster();
        // Le second calque est décalé d'un pixel en X : son trait noir tombe en (1,0).
        var compositor = new ComparisonCompositor();

        var layers = new List<LayerRenderInfo>
        {
            new(raster1, RasterScale: 1f, SKMatrix.Identity, LayerTint.Red, Strength: 1f),
            new(raster2, RasterScale: 1f, SKMatrix.CreateTranslation(1, 0), LayerTint.Blue, Strength: 1f),
        };

        using var result = compositor.ComposeToBitmap(new SKSizeI(3, 2), SKMatrix.Identity, layers);

        // (0,0) : trait rouge seul → rouge pur.
        var redOnly = result.GetPixel(0, 0);
        Assert.True(redOnly.Red > 240 && redOnly.Green < 15 && redOnly.Blue < 15,
            $"Attendu rouge pur, obtenu {redOnly}");

        // (1,0) : trait bleu seul (raster2 décalé) × blanc du raster1 → bleu pur.
        var blueOnly = result.GetPixel(1, 0);
        Assert.True(blueOnly.Blue > 240 && blueOnly.Red < 15 && blueOnly.Green < 15,
            $"Attendu bleu pur, obtenu {blueOnly}");

        // (1,1) : blanc des deux → blanc.
        var white = result.GetPixel(1, 1);
        Assert.True(white.Red > 240 && white.Green > 240 && white.Blue > 240,
            $"Attendu blanc, obtenu {white}");
    }

    [Fact]
    public void OverlappingStrokes_MultiplyToNearBlack()
    {
        using var raster1 = MakeRaster();
        using var raster2 = MakeRaster();
        var compositor = new ComparisonCompositor();

        var layers = new List<LayerRenderInfo>
        {
            new(raster1, 1f, SKMatrix.Identity, LayerTint.Red, 1f),
            new(raster2, 1f, SKMatrix.Identity, LayerTint.Blue, 1f),
        };

        using var result = compositor.ComposeToBitmap(new SKSizeI(2, 2), SKMatrix.Identity, layers);

        // (0,0) : rouge pur × bleu pur = noir (couleur combinée sombre).
        var common = result.GetPixel(0, 0);
        Assert.True(common.Red < 15 && common.Green < 15 && common.Blue < 15,
            $"Attendu quasi noir, obtenu {common}");
    }

    [Fact]
    public void ZeroStrength_MakesLayerVanish()
    {
        using var raster1 = MakeRaster();
        var compositor = new ComparisonCompositor();

        var layers = new List<LayerRenderInfo>
        {
            new(raster1, 1f, SKMatrix.Identity, LayerTint.Red, Strength: 0f),
        };

        using var result = compositor.ComposeToBitmap(new SKSizeI(2, 2), SKMatrix.Identity, layers);

        // Intensité 0 = interpolation complète vers le blanc : le calque disparaît.
        var pixel = result.GetPixel(0, 0);
        Assert.True(pixel.Red > 240 && pixel.Green > 240 && pixel.Blue > 240,
            $"Attendu blanc, obtenu {pixel}");
    }

    [Fact]
    public void ColoredSourceStroke_StaysVisibleInBothTints()
    {
        // SEN-09 : un nuage de révision ROUGE (source colorée, pas noire) doit rester
        // visible — la luminance BT.709 d'un rouge pur est ~0,21, pas 1,0.
        using var bmp = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.White);
        bmp.SetPixel(0, 0, SKColors.Red);
        bmp.SetImmutable();
        using var raster = SKImage.FromBitmap(bmp);
        var compositor = new ComparisonCompositor();

        foreach (var tint in new[] { LayerTint.Red, LayerTint.Blue })
        {
            var layers = new List<LayerRenderInfo> { new(raster, 1f, SKMatrix.Identity, tint, 1f) };
            using var result = compositor.ComposeToBitmap(new SKSizeI(2, 2), SKMatrix.Identity, layers);
            var pixel = result.GetPixel(0, 0);

            // Le trait ne doit PAS avoir disparu (blanc) : ses canaux non saturés
            // portent la luminance ~0,21 → nettement sombres.
            int minChannel = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
            Assert.True(minChannel < 100,
                $"Trait rouge source invisible en teinte {tint} : {pixel}");
        }
    }

    [Fact]
    public void HalfStrength_IsPaleTint_NotAlphaBlend()
    {
        using var raster1 = MakeRaster();
        var compositor = new ComparisonCompositor();

        var layers = new List<LayerRenderInfo>
        {
            new(raster1, 1f, SKMatrix.Identity, LayerTint.Red, Strength: 0.5f),
        };

        using var result = compositor.ComposeToBitmap(new SKSizeI(2, 2), SKMatrix.Identity, layers);

        // Lerp vers le blanc : le canal rouge reste saturé, les autres remontent à ~127.
        var pixel = result.GetPixel(0, 0);
        Assert.True(pixel.Red > 240, $"Canal rouge attendu saturé, obtenu {pixel}");
        Assert.InRange(pixel.Green, 110, 145);
        Assert.InRange(pixel.Blue, 110, 145);
    }

    [Fact]
    public void RegionOrigin_PositionsTileInDocSpace()
    {
        // SEN-14 : une tuile dont l'origine est (5, 3) points doc doit dessiner son
        // pixel (0,0) à l'écran en (5, 3) — la translation d'origine précède l'échelle.
        using var raster = MakeRaster();
        var compositor = new ComparisonCompositor();

        var layers = new List<LayerRenderInfo>
        {
            new(raster, RasterScale: 1f, SKMatrix.Identity, LayerTint.Red, 1f,
                RegionOriginDoc: new SKPoint(5, 3)),
        };

        using var result = compositor.ComposeToBitmap(new SKSizeI(8, 6), SKMatrix.Identity, layers);

        var moved = result.GetPixel(5, 3);
        Assert.True(moved.Red > 240 && moved.Green < 15, $"Trait attendu en (5,3), obtenu {moved}");
        var origin = result.GetPixel(0, 0);
        Assert.True(origin.Red > 240 && origin.Green > 240, $"(0,0) doit être blanc, obtenu {origin}");
    }

    [Fact]
    public void AnisotropicRasterScale_UsesBothAxes()
    {
        // L'arrondi entier du DPI d'une région peut produire des échelles X/Y différentes :
        // un raster 2×4 couvrant 2×2 points doc (scaleY = 2) doit se dessiner sur 2×2 px.
        using var bmp = new SKBitmap(new SKImageInfo(2, 4, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Black);
        bmp.SetImmutable();
        using var raster = SKImage.FromBitmap(bmp);
        var compositor = new ComparisonCompositor();

        var layers = new List<LayerRenderInfo>
        {
            new(raster, RasterScale: 1f, SKMatrix.Identity, LayerTint.Red, 1f, RasterScaleY: 2f),
        };

        using var result = compositor.ComposeToBitmap(new SKSizeI(4, 6), SKMatrix.Identity, layers);

        Assert.True(result.GetPixel(1, 1).Red > 240 && result.GetPixel(1, 1).Green < 15,
            "l'intérieur du trait doit être rouge");
        var below = result.GetPixel(1, 3); // sous les 2 points doc couverts → blanc
        Assert.True(below.Green > 240, $"attendu blanc sous la tuile, obtenu {below}");
    }

    // ── Mode Différences (UAT cycle 2) ───────────────────────────────────────

    [Fact]
    public void DifferencesOnly_ErasesCommonStrokes_KeepsUniqueOnes()
    {
        using var raster1 = MakeRaster(); // trait noir en (0,0)
        using var raster2 = MakeRaster();
        var compositor = new ComparisonCompositor();

        // Le second calque est décalé d'un pixel : (0,0) = commun ? Non — raster2 translaté
        // met SON trait en (1,0) : (0,0) = rouge seul, (1,0) = bleu seul. Superposons-les
        // exactement pour avoir un commun : troisième config sans décalage.
        var overlapping = new List<LayerRenderInfo>
        {
            new(raster1, 1f, SKMatrix.Identity, LayerTint.Red, 1f),
            new(raster2, 1f, SKMatrix.Identity, LayerTint.Blue, 1f),
        };
        using var diffCommon = compositor.ComposeToBitmap(new SKSizeI(2, 2), SKMatrix.Identity,
            overlapping, CompareViewMode.DifferencesOnly);
        var common = diffCommon.GetPixel(0, 0);
        Assert.True(common.Red > 240 && common.Green > 240 && common.Blue > 240,
            $"trait commun attendu EFFACÉ (blanc), obtenu {common}");

        var offset = new List<LayerRenderInfo>
        {
            new(raster1, 1f, SKMatrix.Identity, LayerTint.Red, 1f),
            new(raster2, 1f, SKMatrix.CreateTranslation(1, 0), LayerTint.Blue, 1f),
        };
        using var diffUnique = compositor.ComposeToBitmap(new SKSizeI(3, 2), SKMatrix.Identity,
            offset, CompareViewMode.DifferencesOnly);
        var red = diffUnique.GetPixel(0, 0);
        Assert.True(red.Red > 240 && red.Green < 15, $"écart rouge attendu conservé, obtenu {red}");
        var blue = diffUnique.GetPixel(1, 0);
        Assert.True(blue.Blue > 240 && blue.Red < 15, $"écart bleu attendu conservé, obtenu {blue}");
    }

    [Fact]
    public void DifferencesOverBase_ShowsBaseAsGrayContext_DifferencesInColour()
    {
        using var raster1 = MakeRaster(); // base : trait en (0,0)
        using var raster2 = MakeRaster(); // révisé décalé : trait en (1,0)
        var compositor = new ComparisonCompositor();

        var layers = new List<LayerRenderInfo>
        {
            new(raster1, 1f, SKMatrix.Identity, LayerTint.Red, 1f),
            new(raster2, 1f, SKMatrix.CreateTranslation(1, 0), LayerTint.Blue, 1f),
        };
        using var result = compositor.ComposeToBitmap(new SKSizeI(3, 2), SKMatrix.Identity,
            layers, CompareViewMode.DifferencesOverBase);

        // (0,0) : trait de la BASE — écart rouge posé sur son propre gris de contexte :
        // le canal rouge domine nettement (le pixel n'est ni blanc ni gris neutre).
        var baseStroke = result.GetPixel(0, 0);
        Assert.True(baseStroke.Red > baseStroke.Green + 60,
            $"écart rouge attendu dominant sur le contexte, obtenu {baseStroke}");
        // (1,0) : trait du RÉVISÉ seul → bleu conservé (multiply sur fond blanc du contexte).
        var revStroke = result.GetPixel(1, 0);
        Assert.True(revStroke.Blue > 200 && revStroke.Red < 100,
            $"écart bleu attendu, obtenu {revStroke}");
        // (2,1) : rien nulle part → blanc.
        var empty = result.GetPixel(2, 1);
        Assert.True(empty.Red > 240 && empty.Green > 240, $"fond attendu blanc, obtenu {empty}");
    }

    [Fact]
    public void Binarize_CleansGreyScanBackground_KeepsStrokes()
    {
        // Item 8 : fond gris de scan (~0,88) → blanc ; trait sombre → teinte pleine.
        using var bmp = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(new SKColor(225, 225, 225)); // gris clair de scanner
        bmp.SetPixel(0, 0, new SKColor(70, 70, 70)); // trait encre
        bmp.SetImmutable();
        using var raster = SKImage.FromBitmap(bmp);
        var compositor = new ComparisonCompositor();

        var on = new List<LayerRenderInfo> { new(raster, 1f, SKMatrix.Identity, LayerTint.Red, 1f, Binarize: true) };
        using var cleaned = compositor.ComposeToBitmap(new SKSizeI(2, 2), SKMatrix.Identity, on);

        var background = cleaned.GetPixel(1, 1);
        Assert.True(background.Red > 240 && background.Green > 240 && background.Blue > 240,
            $"fond gris attendu blanc après binarisation, obtenu {background}");
        var stroke = cleaned.GetPixel(0, 0);
        Assert.True(stroke.Red > 240 && stroke.Green < 15 && stroke.Blue < 15,
            $"trait attendu rouge plein, obtenu {stroke}");

        // Sans binarisation, le fond gris assombrit tout le comparatif (le défaut des scans).
        var off = new List<LayerRenderInfo> { new(raster, 1f, SKMatrix.Identity, LayerTint.Red, 1f) };
        using var raw = compositor.ComposeToBitmap(new SKSizeI(2, 2), SKMatrix.Identity, off);
        Assert.True(raw.GetPixel(1, 1).Green < 240, "sans binarisation le fond gris doit rester teinté");
    }
}