using System.IO;

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

    internal static string SampleBase => Path.Combine(SamplesDir, "plan-base.pdf");
    internal static string SampleRevision => Path.Combine(SamplesDir, "plan-revision.pdf");

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
        var vm = new MainViewModel(pdfService, compositor,
            new ExportService(compositor, pdfService), dialogs, new StubRecents(),
            new DrawingComparator.App.Services.ProjectStore());

        await vm.LoadIntoLayerAsync(vm.BaseLayer, SampleBase);
        await vm.LoadIntoLayerAsync(vm.RevisionLayer, SampleRevision);
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

    private static void FourAlignmentClicks(MainViewModel vm, SKPoint corner1, SKPoint corner2)
    {
        Assert.True(vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, corner1)));
        Assert.True(vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, corner1)));
        // Après l'ancrage, le plan révisé s'est déplacé : le clic 3 vise sa nouvelle position.
        Assert.True(vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, corner2)));
        Assert.True(vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, corner2)));
    }

    [Fact]
    public async Task FourClicks_AlignRevisionOntoBase_ThenFinishClosesBanner()
    {
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);

        // Deux angles du bâtiment, éloignés l'un de l'autre.
        var corner1 = new SKPoint(150, 120);
        var corner2 = new SKPoint(950, 680);

        vm.StartAlignmentCommand.Execute(null);
        Assert.True(vm.IsAligning);
        Assert.True(vm.IsPosingPoint);

        FourAlignmentClicks(vm, corner1, corner2);

        // Cycle 2 : le calage est appliqué mais le bandeau reste ouvert (point de contrôle facultatif).
        Assert.Equal(AlignmentStep.Aligned, vm.AlignmentStep);
        Assert.False(vm.IsPosingPoint);
        Assert.True(vm.HasAlignment);

        vm.FinishAlignmentCommand.Execute(null);
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
    public async Task Alignment_UnderZoomedAndPannedView_GivesSameMatrix()
    {
        // TST-05 : les clics traversent une ViewMatrix zoomée/pannée — la matrice de calage
        // (en points PDF) doit être identique à celle obtenue en vue par défaut.
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);
        vm.SetViewportSize(new SKSizeI(1600, 1000));
        vm.ZoomAt(new SKPoint(400, 300), 2.5f);
        vm.Pan(-180f, 95f);

        var corner1 = new SKPoint(150, 120);
        var corner2 = new SKPoint(950, 680);

        vm.StartAlignmentCommand.Execute(null);
        FourAlignmentClicks(vm, corner1, corner2);
        Assert.Equal(AlignmentStep.Aligned, vm.AlignmentStep);
        Assert.Null(vm.AlignmentInlineError);

        var baked = RevisionBakedTransform();
        foreach (var corner in new[] { corner1, corner2, new SKPoint(300, 550) })
        {
            var mapped = vm.AlignMatrix.MapPoint(baked.MapPoint(corner));
            Assert.Equal(corner.X, mapped.X, 0.5f);
            Assert.Equal(corner.Y, mapped.Y, 0.5f);
        }
    }

    [Fact]
    public async Task ControlPoint_ReportsResidual_WithoutChangingMatrix()
    {
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);
        var corner1 = new SKPoint(150, 120);
        var corner2 = new SKPoint(950, 680);

        vm.StartAlignmentCommand.Execute(null);
        FourAlignmentClicks(vm, corner1, corner2);
        var matrixAfterSimilarity = vm.AlignMatrix;

        // Étape 5 facultative : le couple de contrôle mesure, il ne transforme pas.
        vm.AddControlPointCommand.Execute(null);
        Assert.Equal(AlignmentStep.ControlOnRevision, vm.AlignmentStep);
        var control = new SKPoint(500, 200);
        vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, control));
        vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, control));

        Assert.Equal(AlignmentStep.Aligned, vm.AlignmentStep);
        Assert.Equal(matrixAfterSimilarity, vm.AlignMatrix);
        Assert.NotNull(vm.ResidualMm);
        // La transformation bakée est une similitude exacte : le résiduel doit être minuscule.
        Assert.True(vm.ResidualMm < 0.5, $"résiduel {vm.ResidualMm} mm");
        Assert.True(vm.CanUseAffine);
    }

    [Fact]
    public async Task AffineMode_IsExplicit_AndMapsAllThreePairs()
    {
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);
        var corner1 = new SKPoint(150, 120);
        var corner2 = new SKPoint(950, 680);
        var control = new SKPoint(500, 200);

        vm.StartAlignmentCommand.Execute(null);
        FourAlignmentClicks(vm, corner1, corner2);
        vm.AddControlPointCommand.Execute(null);
        vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, control));
        vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, control));

        // SEN-01 : jamais de bascule silencieuse — c'est un choix explicite.
        Assert.False(vm.IsAffineMode);
        vm.IsAffineMode = true;

        var baked = RevisionBakedTransform();
        foreach (var p in new[] { corner1, corner2, control })
        {
            var mapped = vm.AlignMatrix.MapPoint(baked.MapPoint(p));
            Assert.Equal(p.X, mapped.X, 0.5f);
            Assert.Equal(p.Y, mapped.Y, 0.5f);
        }

        // Retour au rigide : la similitude 2 points est restaurée.
        vm.IsAffineMode = false;
        var (scale, _) = AlignmentMath.Decompose(vm.AlignMatrix);
        Assert.Equal(1.0 / 0.96, scale, 2);
    }

    [Fact]
    public async Task UndoAlignmentPoint_WalksBackwardsThroughSteps()
    {
        // TST-06 : Retour arrière re-pose le point précédent, y compris depuis l'état Aligned.
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);
        var corner1 = new SKPoint(150, 120);
        var corner2 = new SKPoint(950, 680);

        vm.StartAlignmentCommand.Execute(null);
        vm.HandleAlignmentClick(RevisionFeatureOnScreen(vm, corner1));
        Assert.Equal(AlignmentStep.Point1OnBase, vm.AlignmentStep);

        vm.UndoAlignmentPointCommand.Execute(null);
        Assert.Equal(AlignmentStep.Point1OnRevision, vm.AlignmentStep);

        // Rejouer jusqu'au calage appliqué, puis revenir en arrière depuis Aligned.
        FourAlignmentClicks(vm, corner1, corner2);
        Assert.Equal(AlignmentStep.Aligned, vm.AlignmentStep);

        vm.UndoAlignmentPointCommand.Execute(null);
        Assert.Equal(AlignmentStep.Point2OnBase, vm.AlignmentStep);
        // La similitude a été retirée : on est revenu à l'état « ancré » (translation seule).
        var (scale, rotation) = AlignmentMath.Decompose(vm.AlignMatrix);
        Assert.Equal(1.0, scale, 3);
        Assert.Equal(0.0, rotation, 3);

        // Le 4e clic recommitte la similitude.
        vm.HandleAlignmentClick(BaseFeatureOnScreen(vm, corner2));
        Assert.Equal(AlignmentStep.Aligned, vm.AlignmentStep);
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

    [Fact]
    public async Task Escape_AfterCommit_KeepsAlignment()
    {
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);
        vm.StartAlignmentCommand.Execute(null);
        FourAlignmentClicks(vm, new SKPoint(150, 120), new SKPoint(950, 680));

        // Le calage est appliqué : Échap referme le bandeau sans le défaire.
        vm.CancelAlignmentCommand.Execute(null);
        Assert.False(vm.IsAligning);
        Assert.True(vm.HasAlignment);
        Assert.NotEqual(SKMatrix.Identity, vm.AlignMatrix);
    }

    [Fact]
    public async Task Escape_DuringControlPointPosing_KeepsCommittedAlignment()
    {
        // SEN2-03 : ouvrir « + point de contrôle » puis Échap ne doit PAS détruire la
        // similitude committée — retour à l'état Aligned, matrice intacte.
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);
        vm.StartAlignmentCommand.Execute(null);
        FourAlignmentClicks(vm, new SKPoint(150, 120), new SKPoint(950, 680));
        var committed = vm.AlignMatrix;

        vm.AddControlPointCommand.Execute(null);
        Assert.Equal(AlignmentStep.ControlOnRevision, vm.AlignmentStep);
        vm.CancelAlignmentCommand.Execute(null);

        Assert.Equal(AlignmentStep.Aligned, vm.AlignmentStep);
        Assert.Equal(committed, vm.AlignMatrix);
        Assert.True(vm.HasAlignment);

        // Un second Échap (depuis Aligned) referme le bandeau en conservant toujours le calage.
        vm.CancelAlignmentCommand.Execute(null);
        Assert.False(vm.IsAligning);
        Assert.Equal(committed, vm.AlignMatrix);
    }

    [Fact]
    public async Task NudgeRevision_TranslatesInBaseMillimetres_PreservingScaleAndRotation()
    {
        // Item 3 : les flèches translatent le plan RÉVISÉ en mm papier de la BASE,
        // sans toucher l'échelle ni la rotation du calage.
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);
        vm.StartAlignmentCommand.Execute(null);
        FourAlignmentClicks(vm, new SKPoint(150, 120), new SKPoint(950, 680));
        vm.FinishAlignmentCommand.Execute(null);

        var before = vm.AlignMatrix;
        var probe = new SKPoint(400, 400);
        var mappedBefore = before.MapPoint(probe);

        vm.NudgeRevision(dxSteps: 2, dySteps: -1); // pas par défaut 0,5 mm

        var mappedAfter = vm.AlignMatrix.MapPoint(probe);
        float ptPerStep = 0.5f * 72f / 25.4f;
        Assert.Equal(mappedBefore.X + 2 * ptPerStep, mappedAfter.X, 0.001f);
        Assert.Equal(mappedBefore.Y - 1 * ptPerStep, mappedAfter.Y, 0.001f);

        var (scaleBefore, rotBefore) = AlignmentMath.Decompose(before);
        var (scaleAfter, rotAfter) = AlignmentMath.Decompose(vm.AlignMatrix);
        Assert.Equal(scaleBefore, scaleAfter, 6);
        Assert.Equal(rotBefore, rotAfter, 6);

        // Le pas forcé (Maj = 0,1 mm) est honoré ponctuellement.
        var mapped = vm.AlignMatrix.MapPoint(probe);
        vm.NudgeRevision(1, 0, overrideStepMm: 0.1);
        Assert.Equal(mapped.X + 0.1f * 72f / 25.4f, vm.AlignMatrix.MapPoint(probe).X, 0.001f);
    }

    [Fact]
    public async Task Nudge_WithoutAlignment_DoesNothing()
    {
        var dialogs = new StubDialogs();
        var vm = await LoadBothPlansAsync(dialogs);
        vm.NudgeRevision(1, 1);
        Assert.Equal(SKMatrix.Identity, vm.AlignMatrix);
    }
}