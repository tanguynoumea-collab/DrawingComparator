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

    [Fact]
    public void ComputeExportScale_UnderBudget_KeepsRequestedDpi()
    {
        // A4 à 300 DPI ≈ 8,7 Mpx : largement sous le plafond.
        var (scale, effectiveDpi) = ExportService.ComputeExportScale(new SKSize(595, 842), 300);
        Assert.Equal(300f, effectiveDpi, 0.01f);
        Assert.Equal(300f / 72f, scale, 0.001f);
    }

    [Fact]
    public void ComputeExportScale_OverBudget_ReducesToMaxPixels()
    {
        // A0 à 600 DPI ≈ 558 Mpx : doit redescendre exactement au plafond.
        var sheet = new SKSize(2384, 3370);
        var (scale, effectiveDpi) = ExportService.ComputeExportScale(sheet, 600);

        double pixels = (double)sheet.Width * scale * sheet.Height * scale;
        Assert.InRange(pixels, ExportService.MaxPixels * 0.99, ExportService.MaxPixels * 1.01);
        Assert.True(effectiveDpi < 600);
        Assert.Equal(scale * 72f, effectiveDpi, 0.01f);
    }

    [Fact]
    public async Task ExportPng_WritesDecodableFileWithExpectedSize()
    {
        using var raster = MakeRaster();
        var service = new ExportService(new ComparisonCompositor());
        var layers = new List<LayerRenderInfo> { new(raster, 1f, SKMatrix.Identity, LayerTint.Red, 1f) };
        string path = TempPng();
        try
        {
            // Feuille 100×50 pt à 144 DPI → 200×100 px.
            float dpi = await service.ExportPngAsync(path, new SKSize(100, 50), 144, layers);

            Assert.Equal(144f, dpi, 0.5f);
            using var decoded = SKBitmap.Decode(path);
            Assert.NotNull(decoded);
            Assert.Equal(200, decoded.Width);
            Assert.Equal(100, decoded.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportPng_Cancelled_CreatesNoFile()
    {
        using var raster = MakeRaster();
        var service = new ExportService(new ComparisonCompositor());
        var layers = new List<LayerRenderInfo> { new(raster, 1f, SKMatrix.Identity, LayerTint.Red, 1f) };
        string path = TempPng();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ExportPngAsync(path, new SKSize(100, 50), 144, layers, cts.Token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ExportViewPng_UsesGivenViewportAndMatrix()
    {
        using var raster = MakeRaster();
        var service = new ExportService(new ComparisonCompositor());
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
}