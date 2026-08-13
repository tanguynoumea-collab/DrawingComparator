using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

/// <summary>
/// Spike C05 devenu suite de tests : le rendu par région de PDFtoImage (Bounds +
/// DpiRelativeToBounds) sur de VRAIS PDF — géométrie, netteté au-delà du plafond
/// pleine page, gestion des régions hors page. C'est le fait pivot du cycle 2.
/// </summary>
public class RegionRenderingTests
{
    [Fact]
    public async Task RenderRegion_SizeMatchesRegionTimesDpi()
    {
        var service = new PdfDocumentService();
        await service.OpenAsync(AlignmentWorkflowTests.SampleBase);
        try
        {
            var region = new SKRect(100, 150, 400, 350); // 300×200 pt
            using var bmp = await service.RenderRegionAsync(AlignmentWorkflowTests.SampleBase, 0, region, 144);

            // 144 DPI = ×2 : 600×400 px (l'arrondi entier du DPI peut décaler d'un pixel).
            Assert.InRange(bmp.Width, 598, 602);
            Assert.InRange(bmp.Height, 398, 402);
        }
        finally
        {
            service.Release(AlignmentWorkflowTests.SampleBase);
        }
    }

    [Fact]
    public async Task RenderRegion_MatchesFullPageCrop_AtSameDpi()
    {
        // La tuile doit montrer le MÊME dessin que le crop équivalent du rendu pleine page :
        // c'est la garantie qu'aucun décalage géométrique n'est introduit par Bounds.
        var service = new PdfDocumentService();
        var info = await service.OpenAsync(AlignmentWorkflowTests.SampleBase);
        try
        {
            const float dpi = 96f; // ×4/3 exact
            using var full = await service.RenderPageAsync(AlignmentWorkflowTests.SampleBase, 0, dpi);
            var region = new SKRect(120, 90, 420, 390); // 300×300 pt
            using var tile = await service.RenderRegionAsync(AlignmentWorkflowTests.SampleBase, 0, region, dpi);

            float scale = full.Width / info.PageSizesPoints[0].Width;
            int x0 = (int)Math.Round(region.Left * scale);
            int y0 = (int)Math.Round(region.Top * scale);

            // Échantillonnage clairsemé avec tolérance de voisinage ±1 px : l'AA de PDFium
            // peut décaler un bord de trait d'un demi-pixel entre les deux chemins de rendu,
            // mais un vrai décalage de géométrie (trait déplacé de > 1 px) doit échouer.
            int mismatches = 0, samples = 0;
            for (int y = 2; y < tile.Height - 2; y += 7)
            {
                for (int x = 2; x < tile.Width - 2; x += 7)
                {
                    samples++;
                    var a = tile.GetPixel(x, y);
                    bool matched = false;
                    for (int dy = -1; dy <= 1 && !matched; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !matched; dx++)
                        {
                            var b = full.GetPixel(x0 + x + dx, y0 + y + dy);
                            if (Math.Abs(a.Red - b.Red) <= 64 && Math.Abs(a.Green - b.Green) <= 64 && Math.Abs(a.Blue - b.Blue) <= 64)
                                matched = true;
                        }
                    }
                    if (!matched)
                        mismatches++;
                }
            }
            Assert.True(mismatches < samples * 0.02,
                $"{mismatches}/{samples} pixels échantillonnés divergent entre tuile et crop pleine page");
        }
        finally
        {
            service.Release(AlignmentWorkflowTests.SampleBase);
        }
    }

    [Fact]
    public async Task RenderRegion_BeyondFullPageCap_DeliversMorePixelsPerPoint()
    {
        // La promesse UAT n°1 : au zoom fort, la tuile porte plus de pixels par point PDF
        // que le raster pleine page plafonné — la netteté vectorielle à tout zoom.
        var service = new PdfDocumentService();
        var info = await service.OpenAsync(AlignmentWorkflowTests.SampleBase);
        try
        {
            var smallRegion = new SKRect(200, 200, 350, 320); // 150×120 pt visibles (zoom fort)
            using var tile = await service.RenderRegionAsync(AlignmentWorkflowTests.SampleBase, 0, smallRegion, 1200);
            float tileScale = tile.Width / smallRegion.Width;

            using var full = await service.RenderPageAsync(AlignmentWorkflowTests.SampleBase, 0, 1200);
            float fullScale = full.Width / info.PageSizesPoints[0].Width;

            Assert.True(tileScale > fullScale * 1.5f,
                $"tuile {tileScale:0.00} px/pt vs pleine page {fullScale:0.00} px/pt");
            Assert.Equal(1200f / 72f, tileScale, 0.1f);
        }
        finally
        {
            service.Release(AlignmentWorkflowTests.SampleBase);
        }
    }

    [Fact]
    public async Task RenderRegion_OutsidePage_IsAnArgumentError()
    {
        var service = new PdfDocumentService();
        await service.OpenAsync(AlignmentWorkflowTests.SampleBase);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.RenderRegionAsync(AlignmentWorkflowTests.SampleBase, 0,
                    new SKRect(-500, -500, -100, -100), 144));
        }
        finally
        {
            service.Release(AlignmentWorkflowTests.SampleBase);
        }
    }

    [Fact]
    public async Task RenderRegion_PartlyOutside_IsClampedToPage()
    {
        var service = new PdfDocumentService();
        await service.OpenAsync(AlignmentWorkflowTests.SampleBase);
        try
        {
            // Déborde à gauche et en haut : seule l'intersection (0..200, 0..100) est rendue.
            var region = new SKRect(-100, -50, 200, 100);
            using var bmp = await service.RenderRegionAsync(AlignmentWorkflowTests.SampleBase, 0, region, 72);
            Assert.InRange(bmp.Width, 199, 201);
            Assert.InRange(bmp.Height, 99, 101);
        }
        finally
        {
            service.Release(AlignmentWorkflowTests.SampleBase);
        }
    }
}