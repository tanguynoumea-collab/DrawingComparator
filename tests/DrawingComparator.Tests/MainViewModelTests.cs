using System.IO;

using DrawingComparator.App.Services;
using DrawingComparator.App.ViewModels;
using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

/// <summary>
/// Chemins d'erreur PDF via le fake service (TST-03/04), export côté ViewModel (TST-07/08),
/// aller-retour projet complet, et pipeline de tuile nette (point 1) au niveau du VM.
/// </summary>
public class MainViewModelTests
{
    private static (MainViewModel Vm, FakePdfDocumentService Pdf, StubDialogs Dialogs, StubRecents Recents) MakeVm()
    {
        var pdf = new FakePdfDocumentService();
        var compositor = new ComparisonCompositor();
        var dialogs = new StubDialogs();
        var recents = new StubRecents();
        var vm = new MainViewModel(pdf, compositor, new ExportService(compositor, pdf), dialogs, recents,
            new DrawingComparator.App.Services.ProjectStore());
        return (vm, pdf, dialogs, recents);
    }

    // ── TST-03/04 : chemins d'erreur ──────────────────────────────────────────

    [Fact]
    public async Task OpenFailure_ShowsDialog_LayerKeepsNothing()
    {
        var (vm, pdf, dialogs, _) = MakeVm();
        pdf.FailOpenWith = "« plan.pdf » est protégé par mot de passe.";

        await vm.LoadIntoLayerAsync(vm.BaseLayer, "plan.pdf");

        Assert.Single(dialogs.Errors);
        Assert.Contains("mot de passe", dialogs.Errors[0]);
        Assert.False(vm.BaseLayer.HasFile);
        Assert.False(vm.HasAnyDocument);
    }

    [Fact]
    public async Task RenderFailure_AfterSuccessfulOpen_ShowsDialog_NoRaster()
    {
        var (vm, pdf, dialogs, _) = MakeVm();
        pdf.FailRenderWith = "PDFium a refusé la page.";

        await vm.LoadIntoLayerAsync(vm.BaseLayer, "plan.pdf");

        Assert.Single(dialogs.Errors);
        Assert.Null(vm.BaseLayer.Raster);
        // Le document reste référencé : l'utilisateur peut changer de page et retenter.
        Assert.True(vm.BaseLayer.HasFile);
    }

    [Fact]
    public async Task DetailRenderFailure_KeepsOverview_SetsStatus_NoDialog()
    {
        // SEN-04 : l'échec de la tuile nette ne détruit pas l'état — l'overview reste,
        // le statut informe, aucun dialogue par pan/zoom.
        var (vm, pdf, dialogs, _) = MakeVm();
        await vm.LoadIntoLayerAsync(vm.BaseLayer, "plan.pdf");
        vm.SetViewportSize(new SKSizeI(400, 300));
        await (vm.CurrentRecompose ?? Task.CompletedTask);
        Assert.NotNull(vm.BaseLayer.Raster);

        pdf.FailRenderWith = "poignée PDFium perdue";
        vm.ZoomAt(new SKPoint(200, 150), 30f); // bien au-delà du DPI de l'overview
        await (vm.CurrentDetailRender ?? Task.CompletedTask);

        Assert.NotNull(vm.BaseLayer.Raster);
        Assert.Null(vm.BaseLayer.DetailRaster);
        Assert.StartsWith("Netteté indisponible", vm.StatusMessage);
        Assert.Empty(dialogs.Errors);
    }

    // ── Point 1 : la tuile nette au niveau VM ─────────────────────────────────

    [Fact]
    public async Task ZoomBeyondOverviewDpi_RendersDetailTile_CoveringViewport()
    {
        var (vm, pdf, _, _) = MakeVm();
        await vm.LoadIntoLayerAsync(vm.BaseLayer, "plan.pdf");
        vm.SetViewportSize(new SKSizeI(400, 300));
        await (vm.CurrentRecompose ?? Task.CompletedTask);

        vm.ZoomAt(new SKPoint(200, 150), 20f);
        await (vm.CurrentDetailRender ?? Task.CompletedTask);
        await (vm.CurrentRecompose ?? Task.CompletedTask);

        Assert.NotNull(vm.BaseLayer.DetailRaster);
        Assert.True(vm.BaseLayer.DetailDpi > vm.BaseLayer.OverviewDpi);

        // La tuile couvre la région visible : le compositeur la choisira (jamais les deux).
        Assert.True(vm.ViewMatrix.TryInvert(out var inv));
        var visible = inv.MapRect(new SKRect(0, 0, 400, 300));
        visible.Inflate(-1, -1);
        Assert.True(vm.BaseLayer.DetailRegionDoc.Contains(visible));
    }

    [Fact]
    public async Task ZoomedOut_NoDetailTileRequested()
    {
        var (vm, pdf, _, _) = MakeVm();
        await vm.LoadIntoLayerAsync(vm.BaseLayer, "plan.pdf");
        vm.SetViewportSize(new SKSizeI(400, 300));
        await (vm.CurrentDetailRender ?? Task.CompletedTask);

        // Vue par défaut (≈ fit) : l'overview suffit, aucune région PDFium demandée.
        Assert.Equal(0, pdf.RenderRegionCalls);
        Assert.Null(vm.BaseLayer.DetailRaster);
    }

    // ── TST-07/08 : export côté ViewModel ─────────────────────────────────────

    [Fact]
    public async Task ExportSheet_FromVm_WritesFile_ReportsProgress_ShowsSnackbar()
    {
        var (vm, pdf, dialogs, _) = MakeVm();
        await vm.LoadIntoLayerAsync(vm.BaseLayer, "base.pdf");
        await vm.LoadIntoLayerAsync(vm.RevisionLayer, "rev.pdf");
        string path = Path.Combine(Path.GetTempPath(), $"dc-test-{Guid.NewGuid():N}.png");
        dialogs.NextExportRequest = new ExportRequest(path, 144, CurrentViewOnly: false);

        try
        {
            await vm.ExportCommand.ExecuteAsync(null);

            Assert.True(File.Exists(path));
            Assert.NotEmpty(dialogs.ProgressReports);
            Assert.NotNull(vm.SnackbarMessage);
            Assert.False(vm.SnackbarIsDanger);
            Assert.Equal(path, vm.SnackbarRevealPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportFailure_ShowsDangerSnackbar_NoCrash()
    {
        var (vm, _, dialogs, _) = MakeVm();
        await vm.LoadIntoLayerAsync(vm.BaseLayer, "base.pdf");
        // Dossier inexistant → File.Create échoue dans le service.
        string path = Path.Combine(Path.GetTempPath(), $"dc-absent-{Guid.NewGuid():N}", "out.png");
        dialogs.NextExportRequest = new ExportRequest(path, 144, CurrentViewOnly: false);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.NotNull(vm.SnackbarMessage);
        Assert.True(vm.SnackbarIsDanger);
        Assert.Contains("échoué", vm.SnackbarMessage);
    }

    [Fact]
    public async Task ExportCancelled_NoSnackbar_NoFile()
    {
        var (vm, _, dialogs, _) = MakeVm();
        await vm.LoadIntoLayerAsync(vm.BaseLayer, "base.pdf");
        string path = Path.Combine(Path.GetTempPath(), $"dc-test-{Guid.NewGuid():N}.png");
        dialogs.NextExportRequest = new ExportRequest(path, 144, CurrentViewOnly: false);
        dialogs.CancelExport = true;

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Null(vm.SnackbarMessage);
        Assert.False(File.Exists(path));
    }

    // ── Projets : aller-retour complet au niveau VM ───────────────────────────

    [Fact]
    public async Task SaveFromVm_ThenOpenInFreshVm_RestoresFullState()
    {
        // TST2-01 : le round-trip passe par le VRAI chemin d'écriture du VM
        // (SaveProjectCommand → mapping état → IProjectStore), pas par un DTO construit à la main.
        string dir = Directory.CreateTempSubdirectory("dc-proj").FullName;
        string basePdf = Path.Combine(dir, "base.pdf");
        string revPdf = Path.Combine(dir, "rev.pdf");
        File.WriteAllText(basePdf, "fake");
        File.WriteAllText(revPdf, "fake");
        string projectPath = Path.Combine(dir, "chantier.dcproj");

        try
        {
            var (vm, _, dialogs, recents) = MakeVm();
            await vm.LoadIntoLayerAsync(vm.BaseLayer, basePdf);
            await vm.LoadIntoLayerAsync(vm.RevisionLayer, revPdf);
            vm.BaseLayer.SelectedPage = 2;
            vm.BaseLayer.OpacityPercent = 70;
            vm.RevisionLayer.Binarize = true;
            vm.SwapTintsCommand.Execute(null);

            dialogs.NextProjectSavePath = projectPath;
            await vm.SaveProjectAsCommand.ExecuteAsync(null);

            Assert.True(File.Exists(projectPath));
            Assert.Equal(projectPath, vm.CurrentProjectPath);
            Assert.NotNull(vm.SnackbarMessage);
            Assert.False(vm.SnackbarIsDanger);
            Assert.Single(recents.Items);

            var (vm2, _, dialogs2, recents2) = MakeVm();
            await vm2.OpenProjectAsync(projectPath);

            Assert.Empty(dialogs2.Errors);
            Assert.Equal(basePdf, vm2.BaseLayer.FilePath);
            Assert.Equal(revPdf, vm2.RevisionLayer.FilePath);
            Assert.Equal(2, vm2.BaseLayer.SelectedPage);
            Assert.Equal(70, vm2.BaseLayer.OpacityPercent);
            Assert.True(vm2.RevisionLayer.Binarize);
            // TintsSwapped : la permutation survit au round-trip complet.
            Assert.Equal(LayerTint.Blue, vm2.BaseLayer.Tint);
            Assert.Equal(LayerTint.Red, vm2.RevisionLayer.Tint);
            Assert.Equal(projectPath, vm2.CurrentProjectPath);
            Assert.Single(recents2.Items);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveProject_IoFailure_ShowsDangerSnackbar_KeepsRunning()
    {
        var (vm, _, dialogs, _) = MakeVm();
        string dir = Directory.CreateTempSubdirectory("dc-proj").FullName;
        string basePdf = Path.Combine(dir, "base.pdf");
        File.WriteAllText(basePdf, "fake");
        try
        {
            await vm.LoadIntoLayerAsync(vm.BaseLayer, basePdf);
            await vm.LoadIntoLayerAsync(vm.RevisionLayer, basePdf);
            // Dossier inexistant → IOException dans le store.
            dialogs.NextProjectSavePath = Path.Combine(dir, "absent", "x.dcproj");
            await vm.SaveProjectAsCommand.ExecuteAsync(null);

            Assert.NotNull(vm.SnackbarMessage);
            Assert.True(vm.SnackbarIsDanger);
            Assert.Null(vm.CurrentProjectPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OpenProject_UncPathWithoutSibling_AsksConfirmation_DeniedAborts()
    {
        // SEC2-01 : sans repli local, un chemin réseau exige une confirmation explicite ;
        // refus → relocalisation proposée → annulée → ouverture abandonnée, AUCUNE sonde UNC.
        var (vm, _, dialogs, _) = MakeVm();
        string dir = Directory.CreateTempSubdirectory("dc-proj").FullName;
        string projectPath = Path.Combine(dir, "chantier.dcproj");
        try
        {
            ProjectSerializer.Save(projectPath, new ComparisonProject
            {
                Base = new ProjectLayer(@"\\serveur-inconnu\plans\base.pdf", 1, 100),
                Revision = new ProjectLayer(@"\\serveur-inconnu\plans\rev.pdf", 1, 100),
            });

            await vm.OpenProjectAsync(projectPath);

            Assert.Equal(1, dialogs.NetworkConfirmCalls);
            Assert.Equal(1, dialogs.RelocateCalls);
            Assert.False(vm.BaseLayer.HasFile);
            Assert.Null(vm.CurrentProjectPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OpenProject_MissingPdf_FallsBackToProjectSiblingFile()
    {
        var (vm, _, dialogs, _) = MakeVm();
        string dir = Directory.CreateTempSubdirectory("dc-proj").FullName;
        string siblingBase = Path.Combine(dir, "base.pdf");
        string siblingRev = Path.Combine(dir, "rev.pdf");
        File.WriteAllText(siblingBase, "fake");
        File.WriteAllText(siblingRev, "fake");
        string projectPath = Path.Combine(dir, "chantier.dcproj");

        try
        {
            // Le projet référence des chemins réseau morts ; les fichiers homonymes
            // vivent à côté du .dcproj → repli automatique, sans dialogue.
            ProjectSerializer.Save(projectPath, new ComparisonProject
            {
                Base = new ProjectLayer(@"\\serveur-mort\plans\base.pdf", 1, 100),
                Revision = new ProjectLayer(@"\\serveur-mort\plans\rev.pdf", 1, 100),
            });

            await vm.OpenProjectAsync(projectPath);

            Assert.Empty(dialogs.Errors);
            Assert.Equal(siblingBase, vm.BaseLayer.FilePath);
            Assert.Equal(siblingRev, vm.RevisionLayer.FilePath);
            // Le repli local a précédé toute résolution réseau : aucune confirmation demandée (SEC2-01).
            Assert.Equal(0, dialogs.NetworkConfirmCalls);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OpenProject_GoneProjectFile_RemovedFromRecents()
    {
        var (vm, _, dialogs, recents) = MakeVm();
        string path = Path.Combine(Path.GetTempPath(), $"dc-gone-{Guid.NewGuid():N}.dcproj");
        recents.Touch(new RecentProject(path, "a.pdf", "b.pdf", DateTime.Now));

        await vm.OpenProjectAsync(path);

        Assert.Single(dialogs.Errors);
        Assert.Empty(recents.Items);
    }

    // ── Onglets Accueil / Projet (UAT cycle 2) ────────────────────────────────

    [Fact]
    public async Task Tabs_SwitchToProjectOnLoad_BackToHomeWhenEmpty()
    {
        var (vm, _, _, _) = MakeVm();
        Assert.True(vm.IsHomeView);

        await vm.LoadIntoLayerAsync(vm.BaseLayer, "plan.pdf");
        Assert.False(vm.IsHomeView); // un plan chargé → onglet Projet

        // L'utilisateur peut revenir à l'Accueil sans fermer le projet (liste Reprendre).
        vm.IsHomeView = true;
        Assert.True(vm.HasAnyDocument);

        // Retirer le dernier plan → retour Accueil forcé.
        vm.IsHomeView = false;
        vm.BaseLayer.ClearFileCommand.Execute(null);
        Assert.True(vm.IsHomeView);
        Assert.False(vm.HasAnyDocument);
    }

    [Fact]
    public async Task ExportPdf_FromVm_WritesFile_ShowsPdfSnackbar()
    {
        var (vm, _, dialogs, _) = MakeVm();
        await vm.LoadIntoLayerAsync(vm.BaseLayer, "base.pdf");
        string path = Path.Combine(Path.GetTempPath(), $"dc-test-{Guid.NewGuid():N}.pdf");
        dialogs.NextExportRequest = new ExportRequest(path, 600, CurrentViewOnly: false, ExportFormat.Pdf);

        try
        {
            await vm.ExportCommand.ExecuteAsync(null);

            Assert.True(File.Exists(path));
            Assert.NotNull(vm.SnackbarMessage);
            Assert.Contains("PDF", vm.SnackbarMessage);
            Assert.Contains("600", vm.SnackbarMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Heuristique « page semble vide » (item 9) ─────────────────────────────

    [Fact]
    public void LooksEmpty_TrueOnWhitePage_FalseWithAnyStroke()
    {
        using var white = new SKBitmap(new SKImageInfo(512, 512, SKColorType.Bgra8888, SKAlphaType.Premul));
        white.Erase(SKColors.White);
        Assert.True(LayerViewModel.LooksEmpty(white));

        // Un seul trait fin quelque part suffit à invalider le bandeau.
        using (var canvas = new SKCanvas(white))
        using (var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 3 })
        {
            canvas.DrawLine(50, 50, 400, 380, paint);
        }
        Assert.False(LayerViewModel.LooksEmpty(white));
    }
}