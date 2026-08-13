using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.App.ViewModels;

/// <summary>
/// Un calque : un fichier PDF, une page, une opacité, un rôle (base ou révision), et DEUX rasters
/// (cycle 2, pipeline v2) : l'« overview » pleine page — le repli permanent, jamais de flash blanc —
/// et la « tuile de vue », la région visible rendue au DPI de l'écran pour la netteté vectorielle.
/// Le compositeur n'en reçoit qu'UN à la fois : empiler les deux en multiply assombrirait
/// les traits communs (LLM-Council cycle 2).
/// </summary>
public sealed partial class LayerViewModel : ObservableObject, IDisposable
{
    // Le repli pleine page n'a plus besoin d'être énorme : la tuile de vue porte la netteté.
    private const float MaxOverviewEdge = 4096f;
    private const float MaxOverviewDpi = 300f;

    private readonly IPdfDocumentService _pdfService;
    private readonly Func<LayerViewModel, Exception, Task> _onError;
    private readonly Action _onRasterChanged;
    private readonly Action<SKImage> _retireBitmap;
    private CancellationTokenSource? _renderCts;

    // SEN-08 : les chargements sont sérialisés (drops rapides, ouverture de projet =
    // deux LoadFileAsync d'affilée) — le comptage de références du service reste cohérent.
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public LayerViewModel(bool isBase, IPdfDocumentService pdfService,
        Func<LayerViewModel, Exception, Task> onError, Action onRasterChanged,
        Action<SKImage> retireBitmap)
    {
        IsBase = isBase;
        _pdfService = pdfService;
        _onError = onError;
        _onRasterChanged = onRasterChanged;
        _retireBitmap = retireBitmap;
    }

    public bool IsBase { get; }

    /// <summary>Rôle affiché : « PLAN DE BASE » ou « PLAN RÉVISÉ ».</summary>
    public string RoleLabel => IsBase ? "PLAN DE BASE" : "PLAN RÉVISÉ";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(HasFile))]
    private string? _filePath;

    public string? FileName => FilePath is null ? null : Path.GetFileName(FilePath);
    public bool HasFile => FilePath is not null;

    [ObservableProperty]
    private PdfDocumentInfo? _documentInfo;

    [ObservableProperty]
    private IReadOnlyList<int> _pageNumbers = [];

    /// <summary>Page sélectionnée, 1-based (affichage utilisateur).</summary>
    [ObservableProperty]
    private int _selectedPage = 1;

    /// <summary>Opacité utilisateur 0..100 (slider). Convertie en intensité 0..1 pour le compositeur.</summary>
    [ObservableProperty]
    private double _opacityPercent = 100;

    [ObservableProperty]
    private LayerTint _tint;

    /// <summary>Nettoyage des PDF scannés (roadmap item 8) : seuillage appliqué par le compositeur, réversible.</summary>
    [ObservableProperty]
    private bool _binarize;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>La page rendue semble entièrement blanche (heuristique post-rendu, bandeau « page vide »).</summary>
    [ObservableProperty]
    private bool _pageLooksEmpty;

    /// <summary>Raster pleine page (noir sur blanc), immuable — le repli permanent.</summary>
    public SKImage? Raster { get; private set; }

    /// <summary>Pixels du raster pleine page par point PDF.</summary>
    public float RasterScale { get; private set; }

    /// <summary>DPI effectif du raster pleine page (au-delà, il faut la tuile de vue).</summary>
    public float OverviewDpi => RasterScale * 72f;

    // ── Tuile de vue (rendu par région, point 1 du cycle 2) ──────────────────

    /// <summary>Tuile de la région visible, rendue au DPI de la vue. Null tant que l'overview suffit.</summary>
    public SKImage? DetailRaster { get; private set; }

    /// <summary>Région couverte par la tuile, en points PDF de CE calque.</summary>
    public SKRect DetailRegionDoc { get; private set; }

    /// <summary>DPI demandé lors du rendu de la tuile (pour savoir si elle est encore à jour).</summary>
    public float DetailDpi { get; private set; }

    private float _detailScaleX;
    private float _detailScaleY;

    public SKSize PageSizePoints =>
        DocumentInfo is { } info && SelectedPage >= 1 && SelectedPage <= info.PageCount
            ? info.PageSizesPoints[SelectedPage - 1]
            : SKSize.Empty;

    public async Task LoadFileAsync(string path)
    {
        await _loadGate.WaitAsync();
        IsLoading = true;
        try
        {
            var info = await _pdfService.OpenAsync(path);
            string? previousPath = FilePath;
            FilePath = path;
            DocumentInfo = info;
            PageNumbers = Enumerable.Range(1, info.PageCount).ToList();
            SelectedPage = 1;
            // L'ancien document est libéré une fois le nouveau ouvert (jamais avant :
            // en cas d'échec d'ouverture, le calque garde son contenu). Y compris pour
            // le MÊME chemin : OpenAsync vient de prendre une référence de plus (FIAB-1).
            if (previousPath is not null)
                _pdfService.Release(previousPath);
            await RenderCurrentPageAsync();
        }
        catch (Exception ex)
        {
            await _onError(this, ex);
        }
        finally
        {
            IsLoading = false;
            _loadGate.Release();
        }
    }

    partial void OnSelectedPageChanged(int value)
    {
        if (DocumentInfo is not null)
            _ = RenderCurrentPageAsync();
    }

    partial void OnOpacityPercentChanged(double value) => _onRasterChanged();

    partial void OnTintChanged(LayerTint value) => _onRasterChanged();

    partial void OnBinarizeChanged(bool value) => _onRasterChanged();

    public async Task RenderCurrentPageAsync()
    {
        if (FilePath is null || DocumentInfo is null)
            return;

        var previousCts = _renderCts;
        previousCts?.Cancel();
        previousCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        var ct = _renderCts.Token;

        var size = PageSizePoints;
        if (size.IsEmpty)
            return;

        // Pas de plancher : sur une page géante, le DPI descend sous 72 plutôt que de
        // dépasser le budget — le service applique de toute façon son propre plafond (SEC-01).
        float dpi = Math.Min(MaxOverviewEdge / Math.Max(size.Width, size.Height) * 72f, MaxOverviewDpi);

        IsLoading = true;
        try
        {
            var bitmap = await _pdfService.RenderPageAsync(FilePath, SelectedPage - 1, dpi, ct);
            if (ct.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }
            bool looksEmpty = LooksEmpty(bitmap);
            bitmap.SetImmutable();
            var image = SKImage.FromBitmap(bitmap); // zéro copie sur bitmap immuable
            bitmap.Dispose();
            var old = Raster;
            Raster = image;
            // L'échelle réelle découle des pixels rendus, pas du DPI demandé (PDFtoImage arrondit).
            RasterScale = image!.Width / size.Width;
            if (old is not null)
                _retireBitmap(old);
            ClearDetail(); // la tuile appartenait à l'ancienne page/fichier
            PageLooksEmpty = looksEmpty;
            _onRasterChanged();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await _onError(this, ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Rend la tuile de la région visible au DPI demandé. En cas d'échec, l'overview reste
    /// affiché (SEN-04) — l'appelant décide du message. Retourne false si rien n'a changé.
    /// </summary>
    public async Task<bool> RenderDetailAsync(SKRect regionDoc, float dpi, CancellationToken ct)
    {
        if (FilePath is null || DocumentInfo is null)
            return false;

        regionDoc.Intersect(new SKRect(0, 0, PageSizePoints.Width, PageSizePoints.Height));
        if (regionDoc.Width <= 0 || regionDoc.Height <= 0)
            return false;

        // Tuile déjà à jour (même région à epsilon près, même DPI) : ne pas re-payer PDFium.
        if (DetailRaster is not null
            && Math.Abs(DetailDpi - dpi) < 0.5f
            && RectAlmostEqual(DetailRegionDoc, regionDoc))
            return false;

        var bitmap = await _pdfService.RenderRegionAsync(FilePath, SelectedPage - 1, regionDoc, dpi, ct);
        if (ct.IsCancellationRequested)
        {
            bitmap.Dispose();
            return false;
        }
        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);
        bitmap.Dispose();

        var old = DetailRaster;
        DetailRaster = image;
        DetailRegionDoc = regionDoc;
        DetailDpi = dpi;
        _detailScaleX = image!.Width / regionDoc.Width;
        _detailScaleY = image.Height / regionDoc.Height;
        if (old is not null)
            _retireBitmap(old);
        return true;
    }

    /// <summary>Retire la tuile de vue (changement de page/fichier, ou libération mémoire).</summary>
    public void ClearDetail()
    {
        var old = DetailRaster;
        DetailRaster = null;
        DetailRegionDoc = SKRect.Empty;
        DetailDpi = 0;
        if (old is not null)
            _retireBitmap(old);
    }

    private static bool RectAlmostEqual(SKRect a, SKRect b)
        => Math.Abs(a.Left - b.Left) < 0.5f && Math.Abs(a.Top - b.Top) < 0.5f
        && Math.Abs(a.Right - b.Right) < 0.5f && Math.Abs(a.Bottom - b.Bottom) < 0.5f;

    /// <summary>
    /// Heuristique « page vide » : échantillonnage clairsemé du raster — aucune zone
    /// nettement plus sombre que le papier ⇒ la page semble blanche (bandeau, item 9).
    /// </summary>
    internal static bool LooksEmpty(SKBitmap bitmap)
    {
        int stepX = Math.Max(1, bitmap.Width / 256);
        int stepY = Math.Max(1, bitmap.Height / 256);
        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Alpha > 32 && Math.Min(p.Red, Math.Min(p.Green, p.Blue)) < 232)
                    return false;
            }
        }
        return true;
    }

    [RelayCommand]
    private void ClearFile()
    {
        _renderCts?.Cancel();
        if (FilePath is not null)
            _pdfService.Release(FilePath);
        FilePath = null;
        DocumentInfo = null;
        PageNumbers = [];
        PageLooksEmpty = false;
        var old = Raster;
        Raster = null;
        RasterScale = 0;
        if (old is not null)
            _retireBitmap(old);
        ClearDetail();
        _onRasterChanged();
    }

    public void Dispose()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        // SEN-05 : purge des rasters à la fermeture — via le circuit de retrait différé,
        // au cas où une composition de fond serait encore en vol.
        var raster = Raster;
        Raster = null;
        if (raster is not null)
            _retireBitmap(raster);
        ClearDetail();
        _loadGate.Dispose();
    }

    /// <summary>
    /// Info de composition : la tuile de vue si elle couvre la région requise à un DPI
    /// meilleur que l'overview, SINON l'overview — jamais les deux (multiply).
    /// </summary>
    /// <param name="requiredRegionDoc">Région visible en points PDF de CE calque (null = pas d'exigence, overview).</param>
    /// <param name="requiredDpi">DPI de la vue (0 = pas d'exigence).</param>
    public LayerRenderInfo? ToRenderInfo(SKMatrix docToBase, SKRect? requiredRegionDoc = null, float requiredDpi = 0f)
    {
        if (Raster is null)
            return null;

        float strength = (float)(OpacityPercent / 100.0);

        if (DetailRaster is not null && requiredRegionDoc is { } required && DetailDpi > OverviewDpi)
        {
            // Tolérance sub-point : le calcul de la région visible et celui de la tuile
            // divergent d'un bruit d'arrondi.
            required.Inflate(-0.5f, -0.5f);
            required.Intersect(new SKRect(0, 0, PageSizePoints.Width, PageSizePoints.Height));
            if (DetailRegionDoc.Contains(required))
            {
                return new LayerRenderInfo(DetailRaster, _detailScaleX, docToBase, Tint, strength,
                    RegionOriginDoc: new SKPoint(DetailRegionDoc.Left, DetailRegionDoc.Top),
                    RasterScaleY: _detailScaleY,
                    Binarize: Binarize);
            }
        }

        _ = requiredDpi; // le choix repose sur la couverture ; le DPI requis pilote le rendu, pas la sélection
        return new LayerRenderInfo(Raster, RasterScale, docToBase, Tint, strength, Binarize: Binarize);
    }
}