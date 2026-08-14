using System.IO;
using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DrawingComparator.App.Services;
using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const float FitMarginPx = 24f;

    /// <summary>Zoom 100 % = 1 point PDF (1/72") affiché sur 96/72 pixel WPF (device-independent, 96 DPI).</summary>
    private const float ScreenPxPerPdfPoint = 96f / 72f;
    private const float MinZoomScale = 0.02f;
    private const float MaxZoomScale = 60f;

    /// <summary>Délai de stabilisation de la vue avant de rendre la tuile nette (pipeline v2).</summary>
    private const int DetailDebounceMs = 150;

    /// <summary>Marge de la tuile autour du viewport (absorbe le pan avant re-rendu).</summary>
    private const float DetailRegionInflate = 0.25f;

    private readonly IComparisonCompositor _compositor;
    private readonly IExportService _exportService;
    private readonly IUserDialogs _dialogs;
    private readonly IRecentProjectsService _recents;
    private readonly IProjectStore _projectStore;

    private readonly List<SKImage> _retiredBitmaps = [];
    private int _composersInFlight;
    private bool _composeRunning;
    private bool _composeDirty;

    private CancellationTokenSource? _detailCts;
    private CancellationTokenSource? _snackbarCts;

    public MainViewModel(IPdfDocumentService pdfService, IComparisonCompositor compositor,
        IExportService exportService, IUserDialogs dialogs, IRecentProjectsService recents,
        IProjectStore projectStore)
    {
        _compositor = compositor;
        _exportService = exportService;
        _dialogs = dialogs;
        _recents = recents;
        _projectStore = projectStore;

        Alignment = new AlignmentSession(RequestRecompose);
        // La session porte ses propres notifications ; la façade les relaie au XAML tels quels
        // (mêmes noms de propriétés) — le shell ne peut plus muter ses invariants (ARC2-01).
        Alignment.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(AlignmentSession.AlignmentStep))
                StartAlignmentCommand.NotifyCanExecuteChanged();
        };

        BaseLayer = new LayerViewModel(isBase: true, pdfService, OnLayerErrorAsync, OnLayerChanged, RetireBitmap)
        {
            Tint = LayerTint.Red,
        };
        RevisionLayer = new LayerViewModel(isBase: false, pdfService, OnLayerErrorAsync, OnLayerChanged, RetireBitmap)
        {
            Tint = LayerTint.Blue,
        };
    }

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

    // ── Calage : la machine vit dans AlignmentSession, la façade re-expose ────

    public AlignmentSession Alignment { get; }

    /// <summary>Matrice de calage : points PDF du plan révisé → points PDF du plan de base. 3×2 générale.</summary>
    public SKMatrix AlignMatrix => Alignment.Matrix;

    public AlignmentStep AlignmentStep => Alignment.AlignmentStep;
    public bool IsAligning => Alignment.IsAligning;
    public bool IsPosingPoint => Alignment.IsPosingPoint;
    public bool IsAlignmentCommitted => Alignment.IsAlignmentCommitted;
    public string Step1Glyph => Alignment.Step1Glyph;
    public string Step2Glyph => Alignment.Step2Glyph;
    public string Step3Glyph => Alignment.Step3Glyph;
    public string Step4Glyph => Alignment.Step4Glyph;
    public string StepInstruction => Alignment.StepInstruction;
    public string? AlignmentInlineError => Alignment.AlignmentInlineError;
    public string? AlignmentSummary => Alignment.AlignmentSummary;
    public bool HasAlignment => Alignment.HasAlignment;
    public double? ResidualMm => Alignment.ResidualMm;
    public string? ResidualText => Alignment.ResidualText;
    public bool HasControlPointForDisplay => Alignment.HasControlPointForDisplay;
    public bool CanUseAffine => Alignment.CanUseAffine;
    public string? AnisotropyWarning => Alignment.AnisotropyWarning;
    public bool CanNudge => Alignment.CanNudge;

    /// <summary>Liaison TwoWay du segmented Rigide/Affine.</summary>
    public bool IsAffineMode
    {
        get => Alignment.IsAffineMode;
        set => Alignment.IsAffineMode = value;
    }

    /// <summary>Liaison TwoWay du pas d'ajustement (0,1 / 0,5 / 2 mm).</summary>
    public double NudgeStepMm
    {
        get => Alignment.NudgeStepMm;
        set => Alignment.NudgeStepMm = value;
    }

    public void NudgeRevision(int dxSteps, int dySteps, double? overrideStepMm = null)
        => Alignment.Nudge(dxSteps, dySteps, overrideStepMm);

    private bool CanStartAlignment() => BaseLayer.Raster is not null && RevisionLayer.Raster is not null && !IsAligning;

    [RelayCommand(CanExecute = nameof(CanStartAlignment))]
    private void StartAlignment() => Alignment.Start();

    [RelayCommand]
    private void CancelAlignment() => Alignment.Cancel();

    [RelayCommand]
    private void FinishAlignment() => Alignment.Finish();

    [RelayCommand]
    private void AddControlPoint() => Alignment.AddControlPoint();

    [RelayCommand]
    private void UndoAlignmentPoint() => Alignment.Undo();

    [RelayCommand]
    private void ResetAlignment() => Alignment.Reset();

    /// <summary>Traite un clic gauche en mode calage (pixels écran). Retourne false hors mode calage.</summary>
    public bool HandleAlignmentClick(SKPoint screenPoint)
    {
        if (!Alignment.IsAligning)
            return false;
        if (!ViewMatrix.TryInvert(out var viewInverse))
            return true;
        return Alignment.HandleClick(viewInverse.MapPoint(screenPoint));
    }

    // ── Vue ───────────────────────────────────────────────────────────────────

    /// <summary>Matrice de vue : points PDF du plan de base → pixels du viewport.</summary>
    public SKMatrix ViewMatrix { get; private set; } = SKMatrix.CreateScale(ScreenPxPerPdfPoint, ScreenPxPerPdfPoint);

    public SKSizeI ViewportSize { get; private set; }

    /// <summary>
    /// Échelle DPI de l'écran (1,0 = 96 DPI). Le viewport et la ViewMatrix travaillent
    /// en PIXELS PHYSIQUES ; le WriteableBitmap composite porte ce DPI pour que WPF
    /// l'affiche pixel-à-pixel, net à 125/150 % (dev-senior SEN-10).
    /// </summary>
    public double DisplayScale { get; private set; } = 1.0;

    public void SetDisplayScale(double scale)
    {
        if (Math.Abs(scale - DisplayScale) < 0.001)
            return;
        DisplayScale = scale;
        UpdateZoomText();
        RequestRecompose();
    }

    [ObservableProperty]
    private WriteableBitmap? _compositeBitmap;

    [ObservableProperty]
    private bool _hasAnyDocument;

    /// <summary>
    /// Onglet actif (UAT cycle 2) : Accueil (dépôt + Reprendre, accessible à tout moment)
    /// ou Projet (le comparateur). Bascule automatique vers Projet au premier chargement,
    /// retour Accueil quand plus aucun document.
    /// </summary>
    [ObservableProperty]
    private bool _isHomeView = true;

    /// <summary>
    /// Mode d'affichage (UAT cycle 2, « calque des différences ») : superposition classique,
    /// écarts seuls, ou écarts posés sur l'un des deux plans en gris de contexte.
    /// </summary>
    [ObservableProperty]
    private CompareViewMode _compareMode = CompareViewMode.Overlay;

    partial void OnCompareModeChanged(CompareViewMode value) => RequestRecompose();

    // ── Snackbar (item 5) ─────────────────────────────────────────────────────

    [ObservableProperty]
    private string? _snackbarMessage;

    [ObservableProperty]
    private bool _snackbarIsDanger;

    [ObservableProperty]
    private string? _snackbarRevealPath;

    private void ShowSnackbar(string message, string? revealPath = null, bool danger = false)
    {
        SnackbarMessage = message;
        SnackbarRevealPath = revealPath;
        SnackbarIsDanger = danger;
        _snackbarCts?.Cancel();
        _snackbarCts?.Dispose();
        _snackbarCts = new CancellationTokenSource();
        _ = AutoHideSnackbarAsync(_snackbarCts.Token);
    }

    private async Task AutoHideSnackbarAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), ct);
            if (!ct.IsCancellationRequested)
                SnackbarMessage = null;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private void DismissSnackbar() => SnackbarMessage = null;

    [RelayCommand]
    private void RevealInExplorer()
    {
        if (SnackbarRevealPath is { } path && File.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    // ── Barre de statut ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _cursorPositionText = "x       —  y       — mm";

    [ObservableProperty]
    private string _zoomText = "zoom 100 %";

    [ObservableProperty]
    private string _pagesText = "";

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>La tuile nette est en cours de rendu (FIA-07) — « ⟳ netteté… » en barre de statut.</summary>
    [ObservableProperty]
    private bool _isSharpening;

    // ── Bandeau « page semble vide » (item 9) ─────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyPageMessage))]
    private bool _emptyPageBannerDismissed;

    public string? EmptyPageMessage
    {
        get
        {
            if (EmptyPageBannerDismissed || BaseLayer is null || RevisionLayer is null)
                return null;
            if (BaseLayer.PageLooksEmpty)
                return "La page du plan de BASE semble vide — vérifiez le n° de page.";
            if (RevisionLayer.PageLooksEmpty)
                return "La page du plan RÉVISÉ semble vide — vérifiez le n° de page.";
            return null;
        }
    }

    [RelayCommand]
    private void DismissEmptyPageBanner() => EmptyPageBannerDismissed = true;

    // ── Chargement ────────────────────────────────────────────────────────────

    public async Task LoadIntoLayerAsync(LayerViewModel layer, string path)
    {
        bool firstDocument = !HasAnyDocument;
        await layer.LoadFileAsync(path);
        HasAnyDocument = BaseLayer.HasFile || RevisionLayer.HasFile;
        if (HasAnyDocument)
            IsHomeView = false; // un plan arrive → onglet Projet
        UpdatePagesText();
        StartAlignmentCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
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
        // Les setters des initialiseurs de calques notifient avant la fin du constructeur.
        if (BaseLayer is null || RevisionLayer is null)
            return;
        // ClearFile passe aussi par ici : l'état « aucun document » doit se recalculer (design-review n°2).
        HasAnyDocument = BaseLayer.HasFile || RevisionLayer.HasFile;
        if (!HasAnyDocument)
            IsHomeView = true; // plus rien à comparer → retour Accueil
        StartAlignmentCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        EmptyPageBannerDismissed = false;
        OnPropertyChanged(nameof(EmptyPageMessage));
        UpdatePagesText();
        RequestRecompose();
    }

    private void UpdatePagesText()
    {
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
        if (scale <= 0)
            return; // viewport dégénéré (vue repliée) : le fit reviendra avec une vraie taille
        float tx = (ViewportSize.Width - size.Width * scale) / 2f;
        float ty = (ViewportSize.Height - size.Height * scale) / 2f;
        ViewMatrix = new SKMatrix(scale, 0, tx, 0, scale, ty, 0, 0, 1);
        UpdateZoomText();
        RequestRecompose();
    }

    private void UpdateZoomText()
        => ZoomText = $"zoom {ViewMatrix.ScaleX / (ScreenPxPerPdfPoint * DisplayScale) * 100:0} %";

    public void UpdateCursorPosition(SKPoint screenPoint)
    {
        if (!ViewMatrix.TryInvert(out var inverse))
            return;
        var doc = inverse.MapPoint(screenPoint);
        // points PDF → millimètres papier (1 pt = 25,4/72 mm)
        CursorPositionText = $"x {doc.X * 25.4f / 72f,7:0.0}  y {doc.Y * 25.4f / 72f,7:0.0} mm";
    }

    [RelayCommand]
    private void SwapTints()
    {
        (BaseLayer.Tint, RevisionLayer.Tint) = (RevisionLayer.Tint, BaseLayer.Tint);
    }

    // ── Export (items 5 + 9 + SEN-11) ─────────────────────────────────────────

    private bool CanExport() => HasAnyDocument;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var request = _dialogs.ShowExportDialog();
        if (request is null)
            return;

        try
        {
            if (request.CurrentViewOnly)
            {
                // Vue courante : WYSIWYG depuis les rasters à l'écran, rendu à 2× pour l'archivage.
                // Les SKImage capturés sont lus par le thread d'export : composition « en vol » (FIA-01).
                var layers = BuildRenderLayers();
                BeginBackgroundCompose();
                try
                {
                    var size = new SKSizeI(ViewportSize.Width * 2, ViewportSize.Height * 2);
                    var view = SKMatrix.CreateScale(2f, 2f).PreConcat(ViewMatrix);
                    await _exportService.ExportViewPngAsync(request.OutputPath, size, view, layers, CompareMode);
                }
                finally
                {
                    EndBackgroundCompose();
                }
                ShowSnackbar($"Exporté (vue courante) → {Path.GetFileName(request.OutputPath)}", request.OutputPath);
            }
            else
            {
                // Feuille entière : rendu tuilé au DPI demandé — les rasters d'écran ne sont
                // pas utilisés, le service rend ses propres régions PDF (SEN-11).
                var sheetSize = BaseLayer.HasFile ? BaseLayer.PageSizePoints : RevisionLayer.PageSizePoints;
                var exportLayers = BuildExportLayers();
                bool pdf = request.Format == ExportFormat.Pdf;
                float effectiveDpi = 0f;
                bool completed = await _dialogs.RunExportWithProgressAsync(async (progress, ct) =>
                {
                    effectiveDpi = pdf
                        ? await _exportService.ExportSheetPdfAsync(
                            request.OutputPath, sheetSize, request.Dpi, exportLayers, CompareMode, progress, ct)
                        : await _exportService.ExportSheetPngAsync(
                            request.OutputPath, sheetSize, request.Dpi, exportLayers, CompareMode, progress, ct);
                });
                if (completed)
                    ShowSnackbar(
                        $"Exporté ({(pdf ? "PDF, " : "")}{effectiveDpi:0} DPI) → {Path.GetFileName(request.OutputPath)}",
                        request.OutputPath);
            }
        }
        catch (Exception ex)
        {
            ShowSnackbar($"L'écriture du PNG a échoué : {ex.Message} Réessayez vers un autre dossier.", danger: true);
        }
    }

    private List<ExportLayer> BuildExportLayers()
    {
        var layers = new List<ExportLayer>(2);
        if (BaseLayer is { HasFile: true, FilePath: { } basePath })
            layers.Add(new ExportLayer(basePath, BaseLayer.SelectedPage - 1, BaseLayer.PageSizePoints,
                SKMatrix.Identity, BaseLayer.Tint, (float)(BaseLayer.OpacityPercent / 100.0), BaseLayer.Binarize));
        if (RevisionLayer is { HasFile: true, FilePath: { } revPath })
            layers.Add(new ExportLayer(revPath, RevisionLayer.SelectedPage - 1, RevisionLayer.PageSizePoints,
                AlignMatrix, RevisionLayer.Tint, (float)(RevisionLayer.OpacityPercent / 100.0), RevisionLayer.Binarize));
        return layers;
    }

    // ── Projets (item 2) ──────────────────────────────────────────────────────

    [ObservableProperty]
    private string? _currentProjectPath;

    public IReadOnlyList<RecentProject> RecentProjects => _recents.Items;

    public bool HasRecentProjects => _recents.Items.Count > 0;

    private bool CanSaveProject() => BaseLayer.HasFile && RevisionLayer.HasFile;

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private async Task SaveProjectAsync()
    {
        if (CurrentProjectPath is null)
        {
            await SaveProjectAsAsync();
            return;
        }
        SaveProjectTo(CurrentProjectPath);
    }

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private Task SaveProjectAsAsync()
    {
        string suggested =
            $"{Path.GetFileNameWithoutExtension(BaseLayer.FileName)}-vs-{Path.GetFileNameWithoutExtension(RevisionLayer.FileName)}{ProjectSerializer.FileExtension}";
        if (_dialogs.PickProjectSavePath(suggested) is { } path)
            SaveProjectTo(path);
        return Task.CompletedTask;
    }

    private void SaveProjectTo(string path)
    {
        try
        {
            var project = new ComparisonProject
            {
                Base = new ProjectLayer(BaseLayer.FilePath!, BaseLayer.SelectedPage, BaseLayer.OpacityPercent, BaseLayer.Binarize),
                Revision = new ProjectLayer(RevisionLayer.FilePath!, RevisionLayer.SelectedPage, RevisionLayer.OpacityPercent, RevisionLayer.Binarize),
                Align = ProjectMatrix.FromMatrix(AlignMatrix),
                TintsSwapped = BaseLayer.Tint == LayerTint.Blue,
            };
            _projectStore.Save(path, project);
            CurrentProjectPath = path;
            TouchRecents(path);
            ShowSnackbar($"Projet enregistré → {Path.GetFileName(path)}", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // ArgumentException : état float pathologique refusé par la sérialisation — AVANT
            // d'avoir touché le fichier (DON2-02, écriture atomique DON2-01).
            ShowSnackbar($"Enregistrement impossible : {ex.Message} Réessayez vers un autre dossier.", danger: true);
        }
    }

    /// <summary>Ctrl+O unique : un .dcproj ouvre le projet, un .pdf charge le plan de BASE.</summary>
    [RelayCommand]
    private async Task OpenAnyAsync()
    {
        if (_dialogs.PickProjectOrPdf() is not { } path)
            return;
        if (path.EndsWith(ProjectSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
            await OpenProjectAsync(path);
        else
            await LoadIntoLayerAsync(BaseLayer, path);
    }

    [RelayCommand]
    private Task OpenRecentAsync(RecentProject recent) => OpenProjectAsync(recent.ProjectPath);

    public async Task OpenProjectAsync(string path)
    {
        if (!_projectStore.FileExists(path))
        {
            _recents.Remove(path);
            OnPropertyChanged(nameof(RecentProjects));
            OnPropertyChanged(nameof(HasRecentProjects));
            await _dialogs.ShowErrorAsync("Ouverture du projet impossible",
                $"« {Path.GetFileName(path)} » n'existe plus — il a été retiré de la liste Reprendre.");
            return;
        }

        ComparisonProject project;
        try
        {
            project = _projectStore.Load(path);
        }
        catch (ProjectLoadException ex)
        {
            await _dialogs.ShowErrorAsync("Ouverture du projet impossible", ex.Message);
            return;
        }

        // Chemins re-validés : repli local « à côté du .dcproj » d'abord, confirmation
        // explicite avant toute résolution réseau (SEC2-01), relocalisation en dernier.
        if (await ResolveProjectFileAsync(project.Base.FilePath, path) is not { } basePath)
            return;
        if (await ResolveProjectFileAsync(project.Revision.FilePath, path) is not { } revPath)
            return;

        BaseLayer.Tint = project.TintsSwapped ? LayerTint.Blue : LayerTint.Red;
        RevisionLayer.Tint = project.TintsSwapped ? LayerTint.Red : LayerTint.Blue;

        await LoadIntoLayerAsync(BaseLayer, basePath);
        await LoadIntoLayerAsync(RevisionLayer, revPath);
        if (BaseLayer.DocumentInfo is null || RevisionLayer.DocumentInfo is null)
            return; // l'erreur a déjà été montrée par le chargement

        BaseLayer.SelectedPage = Math.Clamp(project.Base.Page, 1, BaseLayer.DocumentInfo.PageCount);
        RevisionLayer.SelectedPage = Math.Clamp(project.Revision.Page, 1, RevisionLayer.DocumentInfo.PageCount);
        BaseLayer.OpacityPercent = project.Base.OpacityPercent;
        RevisionLayer.OpacityPercent = project.Revision.OpacityPercent;
        BaseLayer.Binarize = project.Base.Binarize;
        RevisionLayer.Binarize = project.Revision.Binarize;

        Alignment.ApplyLoadedMatrix(project.Align.ToMatrix());
        FitToWindow();
        RequestRecompose();

        CurrentProjectPath = path;
        TouchRecents(path);
    }

    private async Task<string?> ResolveProjectFileAsync(string storedPath, string projectPath)
    {
        string sibling = Path.Combine(Path.GetDirectoryName(projectPath) ?? "", Path.GetFileName(storedPath));
        bool nonLocal = _projectStore.IsNonLocalPath(storedPath);

        if (!nonLocal && _projectStore.FileExists(storedPath))
            return storedPath;

        // Repli local AVANT toute sonde réseau : un .dcproj hostile ne déclenche
        // jamais de résolution SMB silencieuse (SEC2-01).
        if (_projectStore.FileExists(sibling))
            return sibling;

        if (nonLocal)
        {
            if (!await _dialogs.ConfirmOpenNetworkPathAsync(storedPath))
                return _dialogs.RelocateMissingFile(storedPath);
            if (_projectStore.FileExists(storedPath))
                return storedPath;
        }

        return _dialogs.RelocateMissingFile(storedPath);
    }

    private void TouchRecents(string projectPath)
    {
        _recents.Touch(new RecentProject(projectPath,
            BaseLayer.FileName ?? "?", RevisionLayer.FileName ?? "?", DateTime.Now));
        OnPropertyChanged(nameof(RecentProjects));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    // ── Composition ───────────────────────────────────────────────────────────

    /// <summary>Régions visibles (gonflées d'une marge de pan) en points PDF de chaque calque + DPI de la vue.</summary>
    private (SKRect BaseRegion, SKRect RevRegion, float ViewDpi)? ComputeVisibleRegions()
    {
        if (ViewportSize.Width <= 0 || !ViewMatrix.TryInvert(out var viewInverse))
            return null;
        var baseRect = viewInverse.MapRect(new SKRect(0, 0, ViewportSize.Width, ViewportSize.Height));
        baseRect.Inflate(baseRect.Width * DetailRegionInflate, baseRect.Height * DetailRegionInflate);

        var revRect = AlignMatrix.TryInvert(out var alignInverse)
            ? alignInverse.MapRect(baseRect) // AABB dans le repère du révisé (la rotation gonfle, correct)
            : baseRect;

        // La ViewMatrix est une similitude sans rotation : ScaleX suffit (hypothèse documentée).
        return (baseRect, revRect, ViewMatrix.ScaleX * 72f);
    }

    private List<LayerRenderInfo> BuildRenderLayers()
    {
        var regions = ComputeVisibleRegions();
        var layers = new List<LayerRenderInfo>(2);
        if (BaseLayer.ToRenderInfo(SKMatrix.Identity, regions?.BaseRegion, regions?.ViewDpi ?? 0f) is { } b)
            layers.Add(b);
        if (RevisionLayer.ToRenderInfo(AlignMatrix, regions?.RevRegion, regions?.ViewDpi ?? 0f) is { } r)
            layers.Add(r);
        return layers;
    }

    /// <summary>
    /// Recomposition coalescée : jamais plus d'une composition de viewport en vol,
    /// la dernière demande gagne. Déclenche aussi (avec debounce) le rendu de la
    /// tuile nette de la région visible — pipeline v2.
    /// </summary>
    public void RequestRecompose()
    {
        if (ViewportSize.Width <= 0 || ViewportSize.Height <= 0)
            return;
        // Sans document, pas de feuille : le canvas garde son fond sombre (état vide du
        // DESIGN_PLAN §3 — un composite blanc rendrait la liste Reprendre illisible).
        if (!HasAnyDocument)
        {
            CompositeBitmap = null;
            return;
        }
        ScheduleDetailRender();
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
                var layers = BuildRenderLayers();
                var view = ViewMatrix;
                var size = ViewportSize;
                var mode = CompareMode;

                BeginBackgroundCompose();
                SKBitmap bitmap;
                try
                {
                    bitmap = await Task.Run(() => _compositor.ComposeToBitmap(size, view, layers, mode));
                }
                finally
                {
                    EndBackgroundCompose();
                }
                try
                {
                    // Une itération partie AVANT que l'état vide reprenne la main ne doit pas
                    // faire réapparaître un composite fantôme (dev-council n°2, FIA2-04).
                    if (HasAnyDocument)
                        CompositeBitmap = SkiaInterop.ToWriteableBitmap(bitmap, CompositeBitmap, DisplayScale);
                }
                finally
                {
                    bitmap.Dispose();
                }
                if (!HasAnyDocument)
                    break;
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

    // ── Tuile nette (point 1 : rendu par région, debounce, annulation) ────────

    private void ScheduleDetailRender()
    {
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _detailCts = new CancellationTokenSource();
        CurrentDetailRender = RenderDetailsAfterDelayAsync(_detailCts.Token);
    }

    /// <summary>Tâche du rendu de tuile en cours — observable par les tests.</summary>
    public Task? CurrentDetailRender { get; private set; }

    internal async Task RenderDetailsAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DetailDebounceMs, ct);
            if (ComputeVisibleRegions() is not { } regions)
                return;

            // Le révisé s'affiche à travers AlignMatrix : sa tuile doit être rendue à
            // DPI × √|det| dans SON espace pour être nette une fois transformée
            // (dev-senior SEN2-04 — plans à échelles différentes, ex. 1:50 vs 1:100).
            float revisionDpi = regions.ViewDpi * ExportService.LayerDpiFactor(AlignMatrix);

            bool changed = false;
            foreach (var (layer, region, dpi) in new[]
            {
                (BaseLayer, regions.BaseRegion, regions.ViewDpi),
                (RevisionLayer, regions.RevRegion, revisionDpi),
            })
            {
                if (layer.Raster is null)
                    continue;
                // En dessous du DPI de l'overview, le chemin mipmap actuel est déjà optimal.
                if (dpi <= layer.OverviewDpi * 1.02f)
                    continue;
                IsSharpening = true;
                changed |= await layer.RenderDetailAsync(region, dpi, ct);
            }

            if (changed && !ct.IsCancellationRequested)
                RequestRecompose();
        }
        catch (OperationCanceledException)
        {
        }
        catch (PdfLoadException ex)
        {
            // SEN-04 : l'overview reste affiché, l'échec de netteté s'annonce sans dialogue.
            StatusMessage = $"Netteté indisponible : {ex.Message}";
        }
        catch (Exception ex)
        {
            // Filet terminal (FIA2-02) : la tâche est fire-and-forget — aucune exception
            // (OOM de tuile, SKException…) ne doit devenir une exception non observée muette.
            StatusMessage = $"Netteté indisponible : {ex.Message}";
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsSharpening = false;
        }
    }

    /// <summary>Matrice de vue au moment de la dernière composition (pour le transform GPU intermédiaire).</summary>
    public SKMatrix ComposedViewMatrix { get; private set; } = SKMatrix.Identity;

    public event Action? CompositeUpdated;

    /// <summary>Compose la loupe du viseur : région centrée sur le curseur, grossie ×<paramref name="magnification"/>.
    /// La loupe compose SANS scrim : c'est elle qui « perce » le voile du mode calage à pleine lumière.</summary>
    public SKBitmap ComposeLoupe(SKPoint screenPoint, int sizePx, float magnification)
    {
        var m = SKMatrix.CreateTranslation(sizePx / 2f, sizePx / 2f)
            .PreConcat(SKMatrix.CreateScale(magnification, magnification))
            .PreConcat(SKMatrix.CreateTranslation(-screenPoint.X, -screenPoint.Y))
            .PreConcat(ViewMatrix);
        return _compositor.ComposeToBitmap(new SKSizeI(sizePx, sizePx), m, BuildRenderLayers(), CompareMode);
    }

    // ── Cycle de vie des rasters (FIA-01) ─────────────────────────────────────
    // Toute composition qui lit les SKImage sur un thread de fond (viewport, export)
    // s'encadre de Begin/EndBackgroundCompose — appelés sur le thread UI, comme les
    // remplacements de raster : le compteur suffit, pas besoin de verrou.
    // L'invariant de thread est vérifié en Debug (dev-senior SEN-06).

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    [System.Diagnostics.Conditional("DEBUG")]
    private void AssertOwnerThread()
        => System.Diagnostics.Debug.Assert(Environment.CurrentManagedThreadId == _ownerThreadId,
            "Begin/EndBackgroundCompose et RetireBitmap doivent être appelés sur le thread UI.");

    /// <summary>Déclare une composition de fond lisant les rasters courants.</summary>
    internal void BeginBackgroundCompose()
    {
        AssertOwnerThread();
        _composersInFlight++;
    }

    /// <summary>Termine une composition de fond ; libère les rasters retirés quand plus rien ne les lit.</summary>
    internal void EndBackgroundCompose()
    {
        AssertOwnerThread();
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
        AssertOwnerThread();
        if (_composersInFlight > 0)
            _retiredBitmaps.Add(bitmap);
        else
            bitmap.Dispose();
    }

    /// <summary>Appelé par le conteneur DI à la fermeture (singleton IDisposable).</summary>
    public void Dispose()
    {
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _detailCts = null;
        _snackbarCts?.Cancel();
        _snackbarCts?.Dispose();
        _snackbarCts = null;
        BaseLayer.Dispose();
        RevisionLayer.Dispose();
        // SEN-05 : les rasters retirés par les Dispose des calques ci-dessus sont purgés
        // immédiatement s'il ne reste aucune composition en vol.
        if (_composersInFlight == 0)
        {
            foreach (var b in _retiredBitmaps)
                b.Dispose();
            _retiredBitmaps.Clear();
        }
    }
}