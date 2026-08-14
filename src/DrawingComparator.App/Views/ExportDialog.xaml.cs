using System.Windows;

using DrawingComparator.App.Services;

namespace DrawingComparator.App.Views;

public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();
    }

    public ExportFormat Format => PdfRadio.IsChecked == true ? ExportFormat.Pdf : ExportFormat.Png;

    /// <summary>Le PDF est toujours la feuille entière (la « vue courante » n'a de sens qu'en image).</summary>
    public bool CurrentViewOnly => Format == ExportFormat.Png && CurrentViewRadio.IsChecked == true;

    public float Dpi =>
        Dpi600Radio.IsChecked == true ? 600f :
        Dpi150Radio.IsChecked == true ? 150f : 300f;

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}