using DrawingComparator.App.Services;
using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

/// <summary>Dialogues neutralisés : les erreurs sont enregistrées, l'export configurable.</summary>
internal sealed class StubDialogs : IUserDialogs
{
    public List<string> Errors { get; } = [];

    /// <summary>Requête retournée par ShowExportDialog (null = utilisateur annule).</summary>
    public ExportRequest? NextExportRequest { get; set; }

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    /// <summary>Chemin retourné par PickProjectSavePath (null = utilisateur annule).</summary>
    public string? NextProjectSavePath { get; set; }

    /// <summary>Réponse à la confirmation de chemin réseau (SEC2-01). Défaut : refus.</summary>
    public bool ConfirmNetworkPaths { get; set; }

    public int NetworkConfirmCalls { get; private set; }
    public int RelocateCalls { get; private set; }

    public string? PickPdfFile(string title) => null;
    public string? PickProjectOrPdf() => null;
    public string? PickProjectSavePath(string suggestedFileName) => NextProjectSavePath;

    public string? RelocateMissingFile(string missingPath)
    {
        RelocateCalls++;
        return null;
    }

    public Task<bool> ConfirmOpenNetworkPathAsync(string path)
    {
        NetworkConfirmCalls++;
        return Task.FromResult(ConfirmNetworkPaths);
    }

    public ExportRequest? ShowExportDialog() => NextExportRequest;

    /// <summary>Exécute l'export directement, sans fenêtre — la progression est collectée.</summary>
    public List<double> ProgressReports { get; } = [];

    /// <summary>Simule un clic sur Annuler avant le premier tick.</summary>
    public bool CancelExport { get; set; }

    public async Task<bool> RunExportWithProgressAsync(Func<IProgress<double>, CancellationToken, Task> exportWork)
    {
        var progress = new SyncProgress(ProgressReports);
        using var cts = new CancellationTokenSource();
        if (CancelExport)
            cts.Cancel();
        try
        {
            await exportWork(progress, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private sealed class SyncProgress(List<double> sink) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (sink)
            {
                sink.Add(value);
            }
        }
    }
}

/// <summary>Liste des récents en mémoire (pas de %APPDATA% dans les tests).</summary>
internal sealed class StubRecents : IRecentProjectsService
{
    private readonly List<RecentProject> _items = [];
    public IReadOnlyList<RecentProject> Items => _items;

    public void Touch(RecentProject project)
    {
        _items.RemoveAll(p => p.ProjectPath == project.ProjectPath);
        _items.Insert(0, project);
    }

    public void Remove(string projectPath) => _items.RemoveAll(p => p.ProjectPath == projectPath);
}

/// <summary>
/// Fake du service PDF (TST-03/04) : documents synthétiques, erreurs injectables,
/// rendus = bitmaps blancs avec un pixel noir en (0,0) — assez pour la géométrie.
/// </summary>
internal sealed class FakePdfDocumentService : IPdfDocumentService
{
    public int PageCount { get; set; } = 3;
    public SKSize PageSize { get; set; } = new(595, 842);

    /// <summary>Si non-null : OpenAsync échoue avec ce message.</summary>
    public string? FailOpenWith { get; set; }

    /// <summary>Si non-null : les rendus échouent avec ce message (après une ouverture réussie — SEN-04).</summary>
    public string? FailRenderWith { get; set; }

    public int OpenCalls { get; private set; }
    public int ReleaseCalls { get; private set; }
    public int RenderPageCalls { get; private set; }
    public int RenderRegionCalls { get; private set; }
    public readonly List<(SKRect Region, float Dpi)> RegionRequests = [];

    public Task<PdfDocumentInfo> OpenAsync(string filePath, CancellationToken ct = default)
    {
        OpenCalls++;
        if (FailOpenWith is { } msg)
            throw new PdfLoadException(msg);
        var sizes = Enumerable.Repeat(PageSize, PageCount).ToList();
        return Task.FromResult(new PdfDocumentInfo(filePath, PageCount, sizes));
    }

    public void Release(string filePath) => ReleaseCalls++;

    public Task<SKBitmap> RenderPageAsync(string filePath, int pageIndex, float dpi, CancellationToken ct = default)
    {
        RenderPageCalls++;
        if (FailRenderWith is { } msg)
            throw new PdfLoadException(msg);
        if (pageIndex < 0 || pageIndex >= PageCount)
            throw new PdfLoadException($"La page {pageIndex + 1} n'existe pas.");
        float scale = PdfDocumentService.CapDpi(PageSize, dpi) / 72f;
        return Task.FromResult(MakeBitmap(
            Math.Max(1, (int)Math.Round(PageSize.Width * scale)),
            Math.Max(1, (int)Math.Round(PageSize.Height * scale))));
    }

    public Task<SKBitmap> RenderRegionAsync(string filePath, int pageIndex, SKRect regionPoints, float dpi, CancellationToken ct = default)
    {
        RenderRegionCalls++;
        if (FailRenderWith is { } msg)
            throw new PdfLoadException(msg);
        regionPoints.Intersect(new SKRect(0, 0, PageSize.Width, PageSize.Height));
        if (regionPoints.Width <= 0 || regionPoints.Height <= 0)
            throw new ArgumentException("La région demandée est hors de la page.", nameof(regionPoints));
        lock (RegionRequests)
        {
            RegionRequests.Add((regionPoints, dpi));
        }
        float scale = PdfDocumentService.CapDpi(new SKSize(regionPoints.Width, regionPoints.Height), dpi) / 72f;
        return Task.FromResult(MakeBitmap(
            Math.Max(1, (int)Math.Round(regionPoints.Width * scale)),
            Math.Max(1, (int)Math.Round(regionPoints.Height * scale))));
    }

    private static SKBitmap MakeBitmap(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.White);
        bmp.SetPixel(0, 0, SKColors.Black);
        return bmp;
    }
}