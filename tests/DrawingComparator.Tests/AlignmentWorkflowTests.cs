using System.IO;

using DrawingComparator.App.Services;
using DrawingComparator.App.ViewModels;
using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

/// <summary>
/// Test d'intégration du flux de calage : vrais PDF d'exemple (docs/samples),
/// vraie machine à états, vrais clics écran — on vérifie que la matrice obtenue
/// renvoie bien les points du plan révisé sur leurs homologues du plan de base.
/// </summary>
public class AlignmentWorkflowTests
{
    private sealed class StubDialogs : IUserDialogs
    {
        public List<string> Errors { get; } = [];
        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add($"{title}: {message}");
            return Task.CompletedTask;
        }
        public string? PickPdfFile(string title) => null;
        public ExportRequest? ShowExportDialog() => null;
    }

    private static string SamplesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DrawingComparator.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "docs", "samples");
        }
    }

    /// <summary>Transformation baked dans plan-revision.pdf (voir le générateur d'exemples).</summary>
    private static SKMatrix RevisionBakedTransform()
    {
        var m = SKMatrix.CreateTranslation(80, 40);
        m = m.PreConcat(SKMatrix.CreateRotationDegrees(1.5f, 500, 400));
        m = m.PreConcat(SKMatrix.CreateScale(0.96f, 0.96f));
        return m;
    }

    private static async Task<MainViewModel> LoadBothPlansAsync(StubDialogs dialogs)
    {
        var pdfService = new PdfDocumentService();
        var compositor = new ComparisonCompositor();
        var vm = new MainViewModel(pdfService, compositor, new ExportService(compositor), dialogs);

        await vm.LoadIntoLayerAsync(vm.BaseLayer, Path.Combine(SamplesDir, "plan-base.pdf"));
        await vm.LoadIntoLayerAsync(vm.RevisionLayer, Path.Combine(SamplesDir, "plan-revision.pdf"));
        Assert.Empty(dialogs.Errors);
        Assert.NotNull(vm.BaseLayer.Raster);
        Assert.NotNull(vm.RevisionLayer.Raster);
        return vm;
    }

    /// <summary>Clic écran là où la feature du plan révisé est actuellement AFFICHÉE.</summary>
    private static SKPoint RevisionFeatureOnScreen(MainViewModel vm, SKPoint drawingPoint)
    {
        var revDoc = RevisionBakedTransform().MapPoint(drawingPoint);
        return vm.ViewMatrix.MapPoint(vm.AlignMatrix.MapPoint(revDoc));
    }

    private static SKPoint BaseFeatureOnScreen(MainViewModel vm, SKPoint drawingPoint)
        => vm.ViewMatrix.MapPoint(drawingPoint);

    [Fact]
    public async Task FourClicks_AlignRevisionOntoBase()
    {
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);

        // Deux angles du bâtiment, éloignés l'un de l'autre.
        var corner1 = new SKPoint(150, 120);
        var corner2 = new SKPoint(950, 680);

        vm.StartAlignmentCommand.Execute(null);
        Assert.True(vm.IsAligning);

        Assert.True(vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, corner1)));
        Assert.True(vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, corner1)));
        // Après l'ancrage, le plan révisé s'est déplacé : le clic 3 vise sa nouvelle position.
        Assert.True(vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, corner2)));
        Assert.True(vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, corner2)));

        Assert.False(vm.IsAligning);
        Assert.True(vm.HasAlignment);

        // La matrice doit renvoyer les coins du plan révisé sur les coins du plan de base…
        var baked = RevisionBakedTransform();
        foreach (var corner in new[] { corner1, corner2, new SKPoint(450, 400) })
        {
            var mapped = vm.AlignMatrix.MapPoint(baked.MapPoint(corner));
            Assert.Equal(corner.X, mapped.X, 0.5f);
            Assert.Equal(corner.Y, mapped.Y, 0.5f);
        }

        // …et sa décomposition doit inverser la transformation bakée (×0.96, +1.5°).
        var (scale, rotation) = AlignmentMath.Decompose(vm.AlignMatrix);
        Assert.Equal(1.0 / 0.96, scale, 2);
        Assert.Equal(-1.5, rotation, 1);
    }

    [Fact]
    public async Task DegeneratePoints_AreRefused_StateStaysOnLastStep()
    {
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);

        var corner = new SKPoint(150, 120);
        vm.StartAlignmentCommand.Execute(null);

        vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, corner));
        vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, corner));
        // Second couple : quasiment le même point → l'échelle est incalculable.
        vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, new SKPoint(150.1f, 120.1f)));
        vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, new SKPoint(500, 400)));

        Assert.True(vm.IsAligning);
        Assert.NotNull(vm.AlignmentInlineError);
    }

    [Fact]
    public async Task Escape_RestoresAlignmentBeforeSession()
    {
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);

        vm.StartAlignmentCommand.Execute(null);
        vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, new SKPoint(150, 120)));
        vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, new SKPoint(300, 300)));
        // L'ancrage a déjà modifié la matrice ; Échap doit tout restaurer.
        vm.CancelAlignmentCommand.Execute(null);

        Assert.False(vm.IsAligning);
        Assert.Equal(SKMatrix.Identity, vm.AlignMatrix);
        Assert.False(vm.HasAlignment);
    }
}