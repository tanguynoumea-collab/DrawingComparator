using System.IO;

using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

public class ExportServiceTests
{
    private static SKImage MakeRaster()
    {
        using var bmp = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.White);
        bmp.SetPixel(0, 0, SKColors.Black);
        bmp.SetImmutable();
        return SKImage.FromBitmap(bmp);
    }

    private static string TempPng()
        => Path.Combine(Path.GetTempPath(), $"dc-test-{Guid.NewGuid():N}.png");

    private static ExportService MakeService(FakePdfDocumentService? pdf = null)
        => new(new ComparisonCompositor(), pdf ?? new FakePdfDocumentService());

    [Fact]
    public void ComputeExportScale_UnderBudget_KeepsRequestedDpi()
    {
        // A4 à 300 DPI ≈ 8,7 Mpx : largement sous le plafond.
        var (scale, effectiveDpi) = ExportService.ComputeExportScale(new SKSize(595, 842), 300);
        Assert.Equal(300f, effectiveDpi, 0.01f);
        Assert.Equal(300f / 72f, scale, 0.001f);
    }

    [Fact]
    public void ComputeExportScale_A0At300_IsNowHonoured()
    {
        // SEN-11 : la promesse du cycle — un A0 à 300 DPI (~140 Mpx) tient dans le budget
        // feuille (rendu par bandes) : le DPI annoncé est le DPI réel.
        var (_, effectiveDpi) = ExportService.ComputeExportScale(new SKSize(2384, 3370), 300);
        Assert.Equal(300f, effectiveDpi, 0.01f);
    }

    [Fact]
    public void ComputeExportScale_OverBudget_ReducesToMaxSheetPixels()
    {
        // A0 à 600 DPI ≈ 558 Mpx : doit redescendre exactement au plafond feuille.
        var sheet = new SKSize(2384, 3370);
        var (scale, effectiveDpi) = ExportService.ComputeExportScale(sheet, 600);

        double pixels = (double)sheet.Width * scale * sheet.Height * scale;
        Assert.InRange(pixels, ExportService.MaxSheetPixels * 0.99, ExportService.MaxSheetPixels * 1.01);
        Assert.True(effectiveDpi < 600);
        Assert.Equal(scale * 72f, effectiveDpi, 0.01f);
    }

    [Fact]
    public async Task ExportSheet_RendersBands_ReportsProgress_WritesExpectedSize()
    {
        var fake = new FakePdfDocumentService { PageSize = new SKSize(400, 2000) };
        var service = MakeService(fake);
        var layers = new List<ExportLayer>
        {
            new("base.pdf", 0, fake.PageSize, SKMatrix.Identity, LayerTint.Red, 1f),
            new("rev.pdf", 0, fake.PageSize, SKMatrix.CreateTranslation(10, 5), LayerTint.Blue, 1f),
        };
        string path = TempPng();
        var reports = new List<double>();
        try
        {
            // 400×2000 pt à 144 DPI → 800×4000 px ; bande = 16 Mpx / 800 ≥ 4000 → au moins 1 bande.
            float dpi = await service.ExportSheetPngAsync(path, new SKSize(400, 2000), 144, layers,
                new SynchronousProgress(reports));

            Assert.Equal(144f, dpi, 0.5f);
            using var decoded = SKBitmap.Decode(path);
            Assert.NotNull(decoded);
            Assert.Equal(800, decoded.Width);
            Assert.Equal(4000, decoded.Height);

            Assert.NotEmpty(reports);
            Assert.Equal(1.0, reports[^1], 3);
            Assert.True(fake.RenderRegionCalls >= 2, "chaque calque de chaque bande passe par le rendu de région");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class SynchronousProgress(List<double> sink) : IProgress<double>
    {
        public void Report(double value) => sink.Add(value);
    }

    [Fact]
    public async Task ExportSheet_HonoursRequestedDpi_PerBandRegion()
    {
        // Le service demande chaque bande au DPI effectif : c'est ce qui tue SEN-11.
        var fake = new FakePdfDocumentService { PageSize = new SKSize(500, 500) };
        var service = MakeService(fake);
        var layers = new List<ExportLayer> { new("base.pdf", 0, fake.PageSize, SKMatrix.Identity, LayerTint.Red, 1f) };
        string path = TempPng();
        try
        {
            await service.ExportSheetPngAsync(path, fake.PageSize, 300, layers);
            lock (fake.RegionRequests)
            {
                Assert.All(fake.RegionRequests, r => Assert.Equal(300f, r.Dpi, 0.5f));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MaxRegionEdge_MirrorsPdfServiceBudget()
    {
        // Le miroir CA1416 ne doit jamais dériver du vrai plafond du service.
        Assert.Equal(PdfDocumentService.MaxRenderEdge, ExportService.MaxRegionEdgePx);
    }

    [Fact]
    public async Task ExportSheet_WideSheet_TilesInX_SoNoRegionHitsTheEdgeCap()
    {
        // SEN2-01 : sur un A0 (2384 pt de large), une bande pleine largeur à 300 DPI ferait
        // 9 933 px > MaxRenderEdge → PDFium dégraderait le DPI en silence. Les tuiles X×Y
        // doivent garder CHAQUE région sous le plafond d'arête au DPI demandé.
        var fake = new FakePdfDocumentService { PageSize = new SKSize(2384, 3370) }; // A0 portrait
        var service = MakeService(fake);
        var layers = new List<ExportLayer> { new("base.pdf", 0, fake.PageSize, SKMatrix.Identity, LayerTint.Red, 1f) };
        string path = TempPng();
        try
        {
            float dpi = await service.ExportSheetPngAsync(path, fake.PageSize, 300, layers);
            Assert.Equal(300f, dpi, 0.5f);

            lock (fake.RegionRequests)
            {
                Assert.True(fake.RegionRequests.Count >= 2, "l'A0 doit être découpé en plusieurs tuiles");
                Assert.All(fake.RegionRequests, r =>
                {
                    float edgePx = Math.Max(r.Region.Width, r.Region.Height) * r.Dpi / 72f;
                    Assert.True(edgePx <= PdfDocumentService.MaxRenderEdge + 1,
                        $"région {r.Region.Width:0}×{r.Region.Height:0} pt à {r.Dpi} DPI = {edgePx:0} px > plafond");
                });
            }

            using var decoded = SKBitmap.Decode(path);
            Assert.Equal((int)Math.Round(2384 * 300f / 72f), decoded.Width);
            Assert.Equal((int)Math.Round(3370 * 300f / 72f), decoded.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportSheet_ScaledRevision_IsRenderedAtCompensatedDpi()
    {
        // SEN2-04 : un révisé calé à ×2 (1:50 → 1:100) doit être rendu à DPI×2 dans son
        // propre espace, sinon il arrive flou une fois agrandi vers la base.
        var fake = new FakePdfDocumentService { PageSize = new SKSize(400, 400) };
        var service = MakeService(fake);
        var layers = new List<ExportLayer>
        {
            new("rev.pdf", 0, fake.PageSize, SKMatrix.CreateScale(2f, 2f), LayerTint.Blue, 1f),
        };
        string path = TempPng();
        try
        {
            await service.ExportSheetPngAsync(path, new SKSize(800, 800), 144, layers);
            lock (fake.RegionRequests)
            {
                Assert.All(fake.RegionRequests, r => Assert.Equal(288f, r.Dpi, 0.5f));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportSheet_Cancelled_CreatesNoFile()
    {
        var service = MakeService();
        var layers = new List<ExportLayer>
        {
            new("base.pdf", 0, new SKSize(100, 50), SKMatrix.Identity, LayerTint.Red, 1f),
        };
        string path = TempPng();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ExportSheetPngAsync(path, new SKSize(100, 50), 144, layers, ct: cts.Token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ExportViewPng_UsesGivenViewportAndMatrix()
    {
        using var raster = MakeRaster();
        var service = MakeService();
        var layers = new List<LayerRenderInfo> { new(raster, 1f, SKMatrix.Identity, LayerTint.Red, 1f) };
        string path = TempPng();
        try
        {
            await service.ExportViewPngAsync(path, new SKSizeI(64, 32), SKMatrix.CreateScale(4, 4), layers);
            using var decoded = SKBitmap.Decode(path);
            Assert.Equal(64, decoded.Width);
            Assert.Equal(32, decoded.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class PdfRenderBudgetTests
{
    [Fact]
    public void CapDpi_NormalPage_KeepsRequestedDpi()
    {
        // A3 paysage à 300 DPI : grand côté ≈ 4961 px, sous les deux plafonds.
        float dpi = PdfDocumentService.CapDpi(new SKSize(1190.55f, 841.89f), 300);
        Assert.Equal(300f, dpi, 0.01f);
    }

    [Fact]
    public void CapDpi_GiantMediaBox_IsCappedInsteadOfExploding()
    {
        // SEC-01 : MediaBox de 14 400 pt (max spec) — l'ancien clamp MinDpi=72 aurait
        // produit un raster 14 400 px (~830 Mo). Le budget doit tenir édge ET surface.
        var page = new SKSize(14400, 14400);
        float dpi = PdfDocumentService.CapDpi(page, 300);
        float scale = dpi / 72f;

        Assert.True(page.Width * scale <= PdfDocumentService.MaxRenderEdge + 1);
        Assert.True((double)page.Width * scale * page.Height * scale <= PdfDocumentService.MaxRenderPixels * 1.01);
    }

    [Fact]
    public void CapDpi_A0At300_IsCappedByEdgeThenUnderPixelBudget()
    {
        // A0 (2384×3370 pt) à 300 DPI : grand côté 14 042 px > 8192 → c'est le plafond
        // d'arête qui s'applique en premier ; la surface résultante reste sous le budget.
        var page = new SKSize(2384, 3370);
        float dpi = PdfDocumentService.CapDpi(page, 300);
        float scale = dpi / 72f;
        double pixels = (double)page.Width * scale * page.Height * scale;

        Assert.True(dpi < 300);
        Assert.InRange(page.Height * scale, PdfDocumentService.MaxRenderEdge * 0.99, PdfDocumentService.MaxRenderEdge * 1.01);
        Assert.True(pixels <= PdfDocumentService.MaxRenderPixels);
    }

    [Fact]
    public void CapDpi_SmallRegion_KeepsHighDpi()
    {
        // Le cœur du point 1 : le budget porte sur la RÉGION — une tuile de viewport
        // (quelques centaines de points) garde son DPI de vue même très élevé.
        float dpi = PdfDocumentService.CapDpi(new SKSize(300, 200), 1200);
        Assert.Equal(1200f, dpi, 0.01f);
    }
}