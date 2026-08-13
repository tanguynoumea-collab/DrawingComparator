using DrawingComparator.App.Services;
using DrawingComparator.App.ViewModels;
using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

/// <summary>
/// Tests du cycle recomposition/rétirement (dev-council n°1 : FIA-01, TST-02) :
/// coalescing déterministe, et libération des rasters différée tant qu'une
/// composition de fond — viewport OU export — peut encore les lire.
/// </summary>
public class RecomposeLifecycleTests
{
    /// <summary>Compositeur contrôlable : compte les appels et peut bloquer jusqu'à signal.</summary>
    private sealed class GatedCompositor : IComparisonCompositor
    {
        public int ComposeCalls;
        public SemaphoreSlim? Gate;

        public SKBitmap ComposeToBitmap(SKSizeI size, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers)
        {
            Interlocked.Increment(ref ComposeCalls);
            Gate?.Wait();
            var bmp = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            bmp.Erase(SKColors.White);
            return bmp;
        }
    }

    private static SKImage MakeImage()
    {
        using var bmp = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.White);
        bmp.SetImmutable();
        return SKImage.FromBitmap(bmp);
    }

    private static MainViewModel MakeVm(GatedCompositor compositor)
    {
        var pdfService = new PdfDocumentService();
        var vm = new MainViewModel(pdfService, compositor, new ExportService(compositor, pdfService),
            new StubDialogs(), new StubRecents(), new DrawingComparator.App.Services.ProjectStore());
        // Sans document, RequestRecompose ne compose plus (état vide sombre, design-review n°2) :
        // ces tests exercent la MACHINERIE de composition, on simule un document présent.
        vm.HasAnyDocument = true;
        return vm;
    }

    [Fact]
    public async Task RapidRequests_AreCoalesced_LastOneWins()
    {
        var compositor = new GatedCompositor();
        var vm = MakeVm(compositor);
        vm.SetViewportSize(new SKSizeI(8, 8));
        vm.RequestRecompose(); // déclenche la compose n°1

        // Trois demandes pendant que la boucle tourne : une seule compose supplémentaire.
        vm.RequestRecompose();
        vm.RequestRecompose();
        vm.RequestRecompose();
        await (vm.CurrentRecompose ?? Task.CompletedTask);

        Assert.InRange(compositor.ComposeCalls, 2, 2);
    }

    [Fact]
    public async Task RetiredImage_IsNotDisposed_WhileViewportComposeInFlight()
    {
        var compositor = new GatedCompositor { Gate = new SemaphoreSlim(0) };
        var vm = MakeVm(compositor);
        vm.SetViewportSize(new SKSizeI(8, 8));
        vm.RequestRecompose(); // compose bloquée sur le Gate

        var image = MakeImage();
        vm.RetireBitmap(image);
        Assert.NotEqual(IntPtr.Zero, image.Handle); // toujours vivant : une compose lit peut-être

        compositor.Gate.Release(10);
        await (vm.CurrentRecompose ?? Task.CompletedTask);
        Assert.Equal(IntPtr.Zero, image.Handle); // libéré une fois la compose finie
    }

    [Fact]
    public void RetiredImage_IsNotDisposed_WhileExportInFlight_EvenAfterViewportCompose()
    {
        // Le scénario FIA-01 : un export (BeginBackgroundCompose) est en vol,
        // aucune compose viewport — le retrait ne doit PAS disposer.
        var compositor = new GatedCompositor();
        var vm = MakeVm(compositor);

        vm.BeginBackgroundCompose(); // simule ExportAsync en cours
        var image = MakeImage();
        vm.RetireBitmap(image);
        Assert.NotEqual(IntPtr.Zero, image.Handle);

        vm.EndBackgroundCompose(); // export terminé → purge
        Assert.Equal(IntPtr.Zero, image.Handle);
    }

    [Fact]
    public void RetiredImage_WithNoComposeInFlight_IsDisposedImmediately()
    {
        var compositor = new GatedCompositor();
        var vm = MakeVm(compositor);

        var image = MakeImage();
        vm.RetireBitmap(image);
        Assert.Equal(IntPtr.Zero, image.Handle);
    }

    [Fact]
    public async Task ComposeFailure_StopsLoop_SetsStatus_NoThrow()
    {
        var compositor = new ThrowingCompositor();
        var pdfService = new PdfDocumentService();
        var vm = new MainViewModel(pdfService, compositor,
            new ExportService(compositor, pdfService), new StubDialogs(), new StubRecents(),
            new DrawingComparator.App.Services.ProjectStore())
        {
            HasAnyDocument = true,
        };

        vm.SetViewportSize(new SKSizeI(8, 8));
        vm.RequestRecompose();
        await (vm.CurrentRecompose ?? Task.CompletedTask);

        Assert.StartsWith("Rendu impossible", vm.StatusMessage);
        // La boucle est ressortie proprement : une nouvelle demande repart.
        vm.RequestRecompose();
        await (vm.CurrentRecompose ?? Task.CompletedTask);
        Assert.Equal(2, compositor.Calls);
    }

    private sealed class ThrowingCompositor : IComparisonCompositor
    {
        public int Calls;
        public SKBitmap ComposeToBitmap(SKSizeI size, SKMatrix baseToView, IReadOnlyList<LayerRenderInfo> layers)
        {
            Calls++;
            throw new InvalidOperationException("boum");
        }
    }
}