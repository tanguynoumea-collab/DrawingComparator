using System.ComponentModel;
using System.Windows;

namespace DrawingComparator.App.Views;

/// <summary>
/// Progression déterminée de l'export feuille entière + annulation. La fenêtre ne se ferme
/// que lorsque l'export a réellement terminé (succès, erreur ou annulation prise en compte).
/// IDisposable pour le CancellationTokenSource — l'appelant (UserDialogs) dispose après usage.
/// </summary>
public partial class ExportProgressDialog : Window, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private bool _workDone;

    public ExportProgressDialog()
    {
        InitializeComponent();
    }

    public CancellationToken CancellationToken => _cts.Token;

    public void SetProgress(double fraction)
    {
        Bar.Value = fraction * 100;
        PercentText.Text = $"{fraction * 100:0} %";
    }

    /// <summary>À appeler (thread UI) quand la tâche d'export est terminée : ferme le dialogue.</summary>
    public void CompleteAndClose()
    {
        _workDone = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        PercentText.Text = "Annulation…";
        _cts.Cancel();
    }

    public void Dispose()
    {
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // La croix de la fenêtre vaut Annuler : on n'abandonne jamais un export en le laissant
        // tourner en arrière-plan sans feedback.
        if (!_workDone)
        {
            _cts.Cancel();
            e.Cancel = true;
            CancelButton.IsEnabled = false;
            PercentText.Text = "Annulation…";
        }
    }
}