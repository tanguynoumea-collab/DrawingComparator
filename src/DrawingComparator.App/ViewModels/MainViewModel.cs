using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DrawingComparator.App.Services;
using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.App.ViewModels;

/// <summary>Étapes de l'outil de calage 2 points (DESIGN_PLAN §3 « mode calage »).</summary>
public enum AlignmentStep
{
    Inactive,
    Point1OnRevision,
    Point1OnBase,
    Point2OnRevision,
    Point2OnBase,
}

public sealed partial class MainViewModel : ObservableObject
{
    private const float FitMarginPx = 24f;

    /// <summary>Zoom 100 % = 1 point PDF (1/72") affiché sur 96/72 pixel WPF (device-independent, 96 DPI).</summary>
    private const float ScreenPxPerPdfPoint = 96f / 72f;
    private const float MinZoomScale = 0.02f;
    private const float MaxZoomScale = 60f;

    private readonly IComparisonCompositor _compositor;
    private readonly IExportService _exportService;
    private readonly IUserDialogs _dialogs;

    private readonly List<SKImage> _retiredBitmaps = [];
    private int _composersInFlight;
    private bool _composeRunning;
    private bool _composeDirty;

    // Points de calage capturés, dans le repère PDF de leur plan respectif.
    private SKPoint _p1, _q1, _p2;
    private SKMatrix _alignBeforeSession = SKMatrix.Identity;

    public MainViewModel(IPdfDocumentService pdfService, IComparisonCompositor compositor,
        IExportService exportService, IUserDialogs dialogs)
    {
        _compositor = compositor;
        _exportService = exportService;
        _dialogs = dialogs;

        BaseLayer = new LayerViewModel(isBase: true, pdfService, OnLayerErrorAsync, OnLayerChanged, RetireBitmap)
        {
            Tint = LayerTint.Red,
        };
        RevisionLayer = new LayerViewModel(isBase: false, pdfService, OnLayerErrorAsync, OnLayerChanged, RetireBitmap)
        {
            Tint = LayerTint.Blue,
        };
    }

    partial void OnAlignmentStepChanged(AlignmentStep value)
        => StartAlignmentCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task OpenBaseAsync()
    {
        if (_dialogs.PickPdfFile("Choisir le plan de BASE") is { } path)
            await LoadIntoLayerAsync(BaseLayer, path);
    }

    [RelayCommand]
    private async Task OpenRevisionAsync()
    {
        if (_dialogs.PickPdfFile("Choisir le plan RÉVISÉ") is { } path)
            await LoadIntoLayerAsync(RevisionLayer, path);
    }

    public LayerViewModel BaseLayer { get; }
    public LayerViewModel RevisionLayer { get; }

    /// <summary>Matrice de calage : points PDF du plan révisé → points PDF du plan de base. 3×2 générale.</summary>
    public SKMatrix AlignMatrix { get; private set; } = SKMatrix.Identity;

    /// <summary>Matrice de vue : points PDF du plan de base → pixels du viewport.</summary>
    public SKMatrix ViewMatrix { get; private set; } = SKMatrix.CreateScale(ScreenPxPerPdfPoint, ScreenPxPerPdfPoint);

    public SKSizeI ViewportSize { get; private set; }

    [ObservableProperty]
    private WriteableBitmap? _compositeBitmap;

    [ObservableProperty]
    private bool _hasAnyDocument;

    // ── Calage ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAligning), nameof(StepInstruction),
        nameof(Step1Glyph), nameof(Step2Glyph), nameof(Step3Glyph), nameof(Step4Glyph))]
    private AlignmentStep _alignmentStep = AlignmentStep.Inactive;

    public bool IsAligning => AlignmentStep != AlignmentStep.Inactive;

    public string Step1Glyph => StepGlyph(1, AlignmentStep.Point1OnRevision, "Point du RÉVISÉ");
    public string Step2Glyph => StepGlyph(2, AlignmentStep.Point1OnBase, "Même point sur la BASE");
    public string Step3Glyph => StepGlyph(3, AlignmentStep.Point2OnRevision, "Second point du RÉVISÉ");
    public string Step4Glyph => StepGlyph(4, AlignmentStep.Point2OnBase, "Même point sur la BASE");

    private string StepGlyph(int number, AlignmentStep stepOfChip, string label)
    {
        int currentIndex = (int)AlignmentStep;
        int chipIndex = (int)stepOfChip;
        string glyph = currentIndex > chipIndex ? "●" : currentIndex == chipIndex ? "◉" : "○";
        return $"{glyph} {number} {label}";
    }

    public string StepInstruction => AlignmentStep switch
    {
        AlignmentStep.Point1OnRevision => "Cliquez un point de référence sur le plan RÉVISÉ (ex. un angle de mur)",
        AlignmentStep.Point1OnBase => "Cliquez le même point sur le plan de BASE — il servira d'ancrage",
        AlignmentStep.Point2OnRevision => "Cliquez un second point sur le plan RÉVISÉ, loin du premier (ex. le bout du mur)",
        AlignmentStep.Point2OnBase => "Cliquez le même point sur le plan de BASE — il fixe l'échelle et la rotation",
        _ => string.Empty,
    };

    [ObservableProperty]
    private string? _alignmentInlineError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlignment))]
    private string? _alignmentSummary;

    public bool HasAlignment => AlignmentSummary is not null;

    // ── Barre de statut ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _cursorPositionText = "x       —  y       — mm";

    [ObservableProperty]
    private string _zoomText = "zoom 100 %";

    [ObservableProperty]
    private string _pagesText = "";

    [ObservableProperty]
    private string _statusMessage = "";

    // ── Chargement ────────────────────────────────────────────────────────────

    public async Task LoadIntoLayerAsync(LayerViewModel layer, string path)
    {
        bool firstDocument = !HasAnyDocument;
        await layer.LoadFileAsync(path);
        HasAnyDocument = BaseLayer.HasFile || RevisionLayer.HasFile;
        UpdatePagesText();
        StartAlignmentCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        if (firstDocument && HasAnyDocument)
            FitToWindow();
    }

    private async Task OnLayerErrorAsync(LayerViewModel layer, Exception ex)
    {
        string message = ex is PdfLoadException ? ex.Message : $"Erreur inattendue : {ex.Message}";
        await _dialogs.ShowErrorAsync("Chargement du PDF impossible", message);
    }

    private void OnLayerChanged()
    {
        UpdatePagesText();
        RequestRecompose();
    }

    private void UpdatePagesText()
    {
        // Les setters des initialiseurs de calques notifient avant la fin du constructeur.
        if (BaseLayer is null || RevisionLayer is null)
            return;
        static string Part(LayerViewModel l) =>
            l.DocumentInfo is { } d ? $"{l.SelectedPage}/{d.PageCount}" : "—";
        PagesText = $"page {Part(BaseLayer)} ↔ {Part(RevisionLayer)}";
    }

    // ── Vue : zoom / pan / fit (appelés par ComparatorView) ───────────────────

    public void SetViewportSize(SKSizeI size)
    {
        bool wasEmpty = ViewportSize.Width == 0;
        ViewportSize = size;
        if (wasEmpty && HasAnyDocument)
            FitToWindow();
        else
            RequestRecompose();
    }

    public void ZoomAt(SKPoint screenPoint, float factor)
    {
        float newScale = Math.Clamp(ViewMatrix.ScaleX * factor, MinZoomScale, MaxZoomScale);
        factor = newScale / ViewMatrix.ScaleX;
        var m = ViewMatrix;
        m = SKMatrix.CreateTranslation(screenPoint.X, screenPoint.Y)
            .PreConcat(SKMatrix.CreateScale(factor, factor))
            .PreConcat(SKMatrix.CreateTranslation(-screenPoint.X, -screenPoint.Y))
            .PreConcat(m);
        ViewMatrix = m;
        UpdateZoomText();
        RequestRecompose();
    }

    public void Pan(float dxPx, float dyPx)
    {
        ViewMatrix = SKMatrix.CreateTranslation(dxPx, dyPx).PreConcat(ViewMatrix);
        RequestRecompose();
    }

    public void FitToWindow()
    {
        var size = BaseLayer.HasFile ? BaseLayer.PageSizePoints : RevisionLayer.PageSizePoints;
        if (size.IsEmpty || ViewportSize.Width == 0)
            return;

        float scale = Math.Min(
            (ViewportSize.Width - 2 * FitMarginPx) / size.Width,
            (ViewportSize.Height - 2 * FitMarginPx) / size.Height);
        float tx = (ViewportSize.Width - size.Width * scale) / 2f;
        float ty = (ViewportSize.Height - size.Height * scale) / 2f;
        ViewMatrix = new SKMatrix(scale, 0, tx, 0, scale, ty, 0, 0, 1);
        UpdateZoomText();
        RequestRecompose();
    }

    private void UpdateZoomText()
        => ZoomText = $"zoom {ViewMatrix.ScaleX / ScreenPxPerPdfPoint * 100:0} %";

    public void UpdateCursorPosition(SKPoint screenPoint)
    {
        if (!ViewMatrix.TryInvert(out var inverse))
            return;
        var doc = inverse.MapPoint(screenPoint);
        // points PDF → millimètres papier (1 pt = 25,4/72 mm)
        CursorPositionText = $"x {doc.X * 25.4f / 72f,7:0.0}  y {doc.Y * 25.4f / 72f,7:0.0} mm";
    }

    // ── Commandes barre d'outils ──────────────────────────────────────────────

    private bool CanStartAlignment() => BaseLayer.Raster is not null && RevisionLayer.Raster is not null && !IsAligning;

    [RelayCommand(CanExecute = nameof(CanStartAlignment))]
    private void StartAlignment()
    {
        _alignBeforeSession = AlignMatrix;
        AlignmentInlineError = null;
        AlignmentStep = AlignmentStep.Point1OnRevision;
        RequestRecompose();
    }

    [RelayCommand]
    private void CancelAlignment()
    {
        if (!IsAligning)
            return;
        AlignMatrix = _alignBeforeSession;
        AlignmentStep = AlignmentStep.Inactive;
        AlignmentInlineError = null;
        UpdateAlignmentSummary();
        RequestRecompose();
    }

    /// <summary>Retour arrière : re-poser le point précédent.</summary>
    [RelayCommand]
    private void UndoAlignmentPoint()
    {
        AlignmentInlineError = null;
        switch (AlignmentStep)
        {
            case AlignmentStep.Point1OnBase:
                AlignmentStep = AlignmentStep.Point1OnRevision;
                break;
            case AlignmentStep.Point2OnRevision:
                // L'ancrage (translation) avait été appliqué : on le retire.
                AlignMatrix = _alignBeforeSession;
                AlignmentStep = AlignmentStep.Point1OnBase;
                RequestRecompose();
                break;
            case AlignmentStep.Point2OnBase:
                AlignmentStep = AlignmentStep.Point2OnRevision;
                break;
        }
    }

    [RelayCommand]
    private void SwapTints()
    {
        (BaseLayer.Tint, RevisionLayer.Tint) = (RevisionLayer.Tint, BaseLayer.Tint);
    }

    [RelayCommand]
    private void ResetAlignment()
    {
        AlignMatrix = SKMatrix.Identity;
        UpdateAlignmentSummary();
        RequestRecompose();
    }

    private bool CanExport() => HasAnyDocument;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var request = _dialogs.ShowExportDialog();
        if (request is null)
            return;

        var sheetSize = BaseLayer.HasFile ? BaseLayer.PageSizePoints : RevisionLayer.PageSizePoints;
        var layers = BuildRenderLayers(dimForAlignment: false);
        // Les SKImage capturés dans layers sont lus par le thread d'export : la
        // composition est déclarée « en vol » pour bloquer leur libération (FIA-01).
        BeginBackgroundCompose();
        try
        {
            StatusMessage = "Export en cours…";
            if (request.CurrentViewOnly)
            {
                // Vue courante : le viewport à l'écran, rendu à 2× pour l'archivage.
                var size = new SKSizeI(ViewportSize.Width * 2, ViewportSize.Height * 2);
                var view = SKMatrix.CreateScale(2f, 2f).PreConcat(ViewMatrix);
                await _exportService.ExportViewPngAsync(request.OutputPath, size, view, layers);
                StatusMessage = $"Exporté (vue courante) → {request.OutputPath}";
            }
            else
            {
                float effectiveDpi = await _exportService.ExportPngAsync(
                    request.OutputPath, sheetSize, request.Dpi, layers);
                StatusMessage = $"Exporté ({effectiveDpi:0} DPI) → {request.OutputPath}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "";
            await _dialogs.ShowErrorAsync("Export impossible",
                $"L'écriture du PNG a échoué : {ex.Message} Réessayez vers un autre dossier.");
        }
        finally
        {
            EndBackgroundCompose();
        }
    }

    // ── Clics de calage (appelés par ComparatorView) ──────────────────────────

    /// <summary>Traite un clic gauche en mode calage. Retourne false hors mode calage.</summary>
    public bool HandleAlignmentClick(SKPoint screenPoint)
    {
        if (!IsAligning)
            return false;
        if (!ViewMatrix.TryInvert(out var viewInverse))
            return true;

        var basePoint = viewInverse.MapPoint(screenPoint);
        AlignmentInlineError = null;

        switch (AlignmentStep)
        {
            case AlignmentStep.Point1OnRevision:
                _p1 = MapBaseToRevision(basePoint);
                AlignmentStep = AlignmentStep.Point1OnBase;
                break;

            case AlignmentStep.Point1OnBase:
                _q1 = basePoint;
                // Ancrage immédiat : le plan révisé vient se poser sur le point cible,
                // ce qui facilite le choix du second couple de points.
                AlignMatrix = AlignmentMath.ComputeAnchorTranslation(
                    AlignMatrix.MapPoint(_p1), _q1).PreConcat(AlignMatrix);
                AlignmentStep = AlignmentStep.Point2OnRevision;
                RequestRecompose();
                break;

            case AlignmentStep.Point2OnRevision:
                _p2 = MapBaseToRevision(basePoint);
                AlignmentStep = AlignmentStep.Point2OnBase;
                break;

            case AlignmentStep.Point2OnBase:
                try
                {
                    AlignMatrix = AlignmentMath.ComputeSimilarity(_p1, _q1, _p2, basePoint);
                    AlignmentStep = AlignmentStep.Inactive;
                    UpdateAlignmentSummary();
                    RequestRecompose();
                }
                catch (DegenerateAlignmentException ex)
                {
                    AlignmentInlineError = $"{ex.Message} Choisissez un point plus éloigné.";
                }
                break;
        }
        return true;
    }

    /// <summary>Point du repère base → repère propre du plan révisé (via l'inverse du calage courant).</summary>
    private SKPoint MapBaseToRevision(SKPoint basePoint)
        => AlignMatrix.TryInvert(out var inv) ? inv.MapPoint(basePoint) : basePoint;

    private void UpdateAlignmentSummary()
    {
        if (AlignMatrix == SKMatrix.Identity)
        {
            AlignmentSummary = null;
            return;
        }
        var (scale, rotation) = AlignmentMath.Decompose(AlignMatrix);
        AlignmentSummary = $"échelle ={scale,7:0.0000}\nrotation ={rotation,7:+0.00;-0.00}°";
    }

    // ── Composition ───────────────────────────────────────────────────────────

    private List<LayerRenderInfo> BuildRenderLayers(bool dimForAlignment)
    {
        float baseStrength = 1f, revisionStrength = 1f;
        if (dimForAlignment)
        {
            // Le calque attendu par l'étape courante reste à pleine intensité, l'autre s'efface.
            if (AlignmentStep is AlignmentStep.Point1OnRevision or AlignmentStep.Point2OnRevision)
                baseStrength = 0.25f;
            else if (AlignmentStep is AlignmentStep.Point1OnBase or AlignmentStep.Point2OnBase)
                revisionStrength = 0.25f;
        }

        var layers = new List<LayerRenderInfo>(2);
        if (BaseLayer.ToRenderInfo(SKMatrix.Identity, baseStrength) is { } b)
            layers.Add(b);
        if (RevisionLayer.ToRenderInfo(AlignMatrix, revisionStrength) is { } r)
            layers.Add(r);
        return layers;
    }

    /// <summary>
    /// Recomposition coalescée : jamais plus d'une composition de viewport en vol,
    /// la dernière demande gagne. Wrapper non-async : le cœur testable est
    /// <see cref="RecomposeLoopAsync"/>, dont les erreurs sont contenues (statut,
    /// pas de dialogue par événement souris).
    /// </summary>
    public void RequestRecompose()
    {
        if (ViewportSize.Width <= 0 || ViewportSize.Height <= 0)
            return;
        if (_composeRunning)
        {
            _composeDirty = true;
            return;
        }
        CurrentRecompose = RecomposeLoopAsync();
    }

    /// <summary>Tâche de la boucle de recomposition en cours — observable par les tests.</summary>
    public Task? CurrentRecompose { get; private set; }

    internal async Task RecomposeLoopAsync()
    {
        _composeRunning = true;
        try
        {
            do
            {
                _composeDirty = false;
                var layers = BuildRenderLayers(dimForAlignment: IsAligning);
                var view = ViewMatrix;
                var size = ViewportSize;

                BeginBackgroundCompose();
                SKBitmap bitmap;
                try
                {
                    bitmap = await Task.Run(() => _compositor.ComposeToBitmap(size, view, layers));
                }
                finally
                {
                    EndBackgroundCompose();
                }
                CompositeBitmap = SkiaInterop.ToWriteableBitmap(bitmap, CompositeBitmap);
                bitmap.Dispose();
                ComposedViewMatrix = view;
                CompositeUpdated?.Invoke();
            }
            while (_composeDirty);
        }
        catch (Exception ex)
        {
            // Pas de dialogue ici : un échec par MouseMove deviendrait une tempête
            // de fenêtres modales. Le statut informe, la boucle s'arrête proprement.
            StatusMessage = $"Rendu impossible : {ex.Message}";
        }
        finally
        {
            _composeRunning = false;
        }
    }

    /// <summary>Matrice de vue au moment de la dernière composition (pour le transform GPU intermédiaire).</summary>
    public SKMatrix ComposedViewMatrix { get; private set; } = SKMatrix.Identity;

    public event Action? CompositeUpdated;

    /// <summary>Compose la loupe du viseur : région centrée sur le curseur, grossie ×<paramref name="magnification"/>.</summary>
    public SKBitmap ComposeLoupe(SKPoint screenPoint, int sizePx, float magnification)
    {
        var m = SKMatrix.CreateTranslation(sizePx / 2f, sizePx / 2f)
            .PreConcat(SKMatrix.CreateScale(magnification, magnification))
            .PreConcat(SKMatrix.CreateTranslation(-screenPoint.X, -screenPoint.Y))
            .PreConcat(ViewMatrix);
        return _compositor.ComposeToBitmap(new SKSizeI(sizePx, sizePx), m, BuildRenderLayers(dimForAlignment: IsAligning));
    }

    // ── Cycle de vie des rasters (FIA-01) ─────────────────────────────────────
    // Toute composition qui lit les SKImage sur un thread de fond (viewport, export)
    // s'encadre de Begin/EndBackgroundCompose — appelés sur le thread UI, comme les
    // remplacements de raster : le compteur suffit, pas besoin de verrou.

    /// <summary>Déclare une composition de fond lisant les rasters courants.</summary>
    internal void BeginBackgroundCompose() => _composersInFlight++;

    /// <summary>Termine une composition de fond ; libère les rasters retirés quand plus rien ne les lit.</summary>
    internal void EndBackgroundCompose()
    {
        _composersInFlight--;
        if (_composersInFlight == 0)
        {
            foreach (var b in _retiredBitmaps)
                b.Dispose();
            _retiredBitmaps.Clear();
        }
    }

    /// <summary>Différer la libération d'un raster remplacé tant qu'une composition de fond peut le lire.</summary>
    public void RetireBitmap(SKImage bitmap)
    {
        if (_composersInFlight > 0)
            _retiredBitmaps.Add(bitmap);
        else
            bitmap.Dispose();
    }
}