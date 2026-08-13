using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.App.ViewModels;

/// <summary>
/// Un calque : un fichier PDF, une page, un raster, une opacité, un rôle (base ou révision).
/// Le raster est rendu à un DPI adaptatif plafonné (grand côté ≤ MaxRasterEdge px).
/// </summary>
public sealed partial class LayerViewModel : ObservableObject, IDisposable
{
    private const float MaxRasterEdge = 8192f;
    private const float MaxDpi = 300f;

    private readonly IPdfDocumentService _pdfService;
    private readonly Func<LayerViewModel, Exception, Task> _onError;
    private readonly Action _onRasterChanged;
    private readonly Action<SKImage> _retireBitmap;
    private CancellationTokenSource? _renderCts;

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

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Raster courant (noir sur blanc), immuable, prêt pour l'échantillonnage mipmap.</summary>
    public SKImage? Raster { get; private set; }

    /// <summary>Pixels du raster par point PDF.</summary>
    public float RasterScale { get; private set; }

    public SKSize PageSizePoints =>
        DocumentInfo is { } info && SelectedPage >= 1 && SelectedPage <= info.PageCount
            ? info.PageSizesPoints[SelectedPage - 1]
            : SKSize.Empty;

    public async Task LoadFileAsync(string path)
    {
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
            // en cas d'échec d'ouverture, le calque garde son contenu).
            if (previousPath is not null && previousPath != path)
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
        }
    }

    partial void OnSelectedPageChanged(int value)
    {
        if (DocumentInfo is not null)
            _ = RenderCurrentPageAsync();
    }

    partial void OnOpacityPercentChanged(double value) => _onRasterChanged();

    partial void OnTintChanged(LayerTint value) => _onRasterChanged();

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
        float dpi = Math.Min(MaxRasterEdge / Math.Max(size.Width, size.Height) * 72f, MaxDpi);

        IsLoading = true;
        try
        {
            var bitmap = await _pdfService.RenderPageAsync(FilePath, SelectedPage - 1, dpi, ct);
            if (ct.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }
            bitmap.SetImmutable();
            var image = SKImage.FromBitmap(bitmap); // zéro copie sur bitmap immuable
            bitmap.Dispose();
            var old = Raster;
            Raster = image;
            // L'échelle réelle découle des pixels rendus, pas du DPI demandé (PDFtoImage arrondit).
            RasterScale = image!.Width / size.Width;
            if (old is not null)
                _retireBitmap(old);
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

    [RelayCommand]
    private void ClearFile()
    {
        _renderCts?.Cancel();
        if (FilePath is not null)
            _pdfService.Release(FilePath);
        FilePath = null;
        DocumentInfo = null;
        PageNumbers = [];
        var old = Raster;
        Raster = null;
        RasterScale = 0;
        if (old is not null)
            _retireBitmap(old);
        _onRasterChanged();
    }

    public void Dispose()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
    }

    /// <summary>Info de composition, ou null si le calque n'a pas de raster.</summary>
    public LayerRenderInfo? ToRenderInfo(SKMatrix docToBase, float strengthMultiplier = 1f)
        => Raster is null
            ? null
            : new LayerRenderInfo(Raster, RasterScale, docToBase, Tint,
                (float)(OpacityPercent / 100.0) * strengthMultiplier);
}