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
}