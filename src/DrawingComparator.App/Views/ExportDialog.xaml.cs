using System.Windows;

namespace DrawingComparator.App.Views;

public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();
    }

    public bool CurrentViewOnly => CurrentViewRadio.IsChecked == true;
    public float Dpi => Dpi300Radio.IsChecked == true ? 300f : 150f;

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
