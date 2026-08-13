using System.Windows;

using DrawingComparator.App.ViewModels;

namespace DrawingComparator.App;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private static string? GetDroppedPdf(DragEventArgs e)
        => e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
           && files[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? files[0]
            : null;

    private void OnDragOverFile(object sender, DragEventArgs e)
    {
        e.Effects = GetDroppedPdf(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDropBase(object sender, DragEventArgs e)
    {
        if (GetDroppedPdf(e) is { } path)
            await _viewModel.LoadIntoLayerAsync(_viewModel.BaseLayer, path);
    }

    private async void OnDropRevision(object sender, DragEventArgs e)
    {
        if (GetDroppedPdf(e) is { } path)
            await _viewModel.LoadIntoLayerAsync(_viewModel.RevisionLayer, path);
    }
}