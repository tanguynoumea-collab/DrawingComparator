using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using DrawingComparator.App.Services;
using DrawingComparator.App.ViewModels;

using SkiaSharp;

namespace DrawingComparator.App.Views;

/// <summary>
/// Le canvas de superposition. Seul code-behind toléré par le DESIGN_PLAN :
/// les maths d'input souris (zoom, pan, clics de calage, loupe). Toute décision
/// métier est déléguée au MainViewModel.
/// Convention de coordonnées : le ViewModel travaille en PIXELS PHYSIQUES
/// (SEN-10) ; ce contrôle convertit les positions souris (DIPs) via _dpiScale.
/// </summary>
public partial class ComparatorView : UserControl
{
    private const int LoupeSizeDip = 140;
    private const float LoupeMagnification = 4f;

    private MainViewModel? _vm;
    private Point _lastPanPoint;
    private bool _isPanning;
    private double _dpiScale = 1.0;
    private DateTime _lastLoupeUpdate = DateTime.MinValue;

    public ComparatorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        MouseWheel += OnMouseWheel;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseLeave += (_, _) => LoupeBorder.Visibility = Visibility.Collapsed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Ajustement fin au clavier (item 3) : les flèches translatent le plan RÉVISÉ du pas
    /// courant en mm papier ; Maj force le pas fin (0,1), Ctrl le pas large (2).
    /// Preview : sans lui, les flèches partent dans la navigation de focus WPF.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null)
            return;
        int dx = 0, dy = 0;
        switch (e.Key)
        {
            case Key.Left: dx = -1; break;
            case Key.Right: dx = 1; break;
            case Key.Up: dy = -1; break; // repère document Y vers le bas
            case Key.Down: dy = 1; break;
            default: return;
        }

        double? overrideStep =
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.01 :
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1.0 : null;
        _vm.NudgeRevision(dx, dy, overrideStep);
        e.Handled = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _vm?.SetDisplayScale(_dpiScale);
        PushViewportSize();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpiScale = newDpi.DpiScaleX;
        _vm?.SetDisplayScale(_dpiScale);
        PushViewportSize();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.CompositeUpdated -= OnCompositeUpdated;
        _vm = e.NewValue as MainViewModel;
        if (_vm is not null)
        {
            _vm.CompositeUpdated += OnCompositeUpdated;
            _vm.SetDisplayScale(_dpiScale);
        }
    }

    private void OnCompositeUpdated()
    {
        // La composition rattrape la vue courante : plus besoin du transform intermédiaire.
        UpdateInterimTransform();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => PushViewportSize();

    private void PushViewportSize()
    {
        // La vue peut être repliée par l'onglet Accueil (Visibility) : un viewport 1×1
        // fausserait le fit initial (zoom négatif) — on attend une taille réelle.
        if (ActualWidth < 1 || ActualHeight < 1)
            return;
        _vm?.SetViewportSize(new SKSizeI(
            (int)Math.Round(ActualWidth * _dpiScale),
            (int)Math.Round(ActualHeight * _dpiScale)));
    }

    /// <summary>Position souris (DIPs) → pixels physiques du viewport.</summary>
    private SKPoint DevicePoint(Point p)
        => new((float)(p.X * _dpiScale), (float)(p.Y * _dpiScale));

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm is null)
            return;
        var pos = e.GetPosition(this);
        float factor = e.Delta > 0 ? 1.2f : 1f / 1.2f;
        _vm.ZoomAt(DevicePoint(pos), factor);
        UpdateInterimTransform();
        UpdateLoupe(pos, force: true);
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null)
            return;
        Focus();

        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2 && !_vm.IsAligning)
            {
                _vm.FitToWindow();
                UpdateInterimTransform();
                return;
            }
            _vm.HandleAlignmentClick(DevicePoint(e.GetPosition(this)));
        }
        else if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is null)
            return;
        var pos = e.GetPosition(this);

        if (_isPanning)
        {
            var delta = pos - _lastPanPoint;
            _lastPanPoint = pos;
            _vm.Pan((float)(delta.X * _dpiScale), (float)(delta.Y * _dpiScale));
            UpdateInterimTransform();
        }

        _vm.UpdateCursorPosition(DevicePoint(pos));
        UpdateLoupe(pos);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// Entre deux compositions, la différence entre la vue demandée et la vue composée
    /// est portée par un RenderTransform GPU : le pan/zoom colle à la main, la netteté
    /// revient dès que la composition rattrape. Le delta est calculé en pixels physiques ;
    /// le RenderTransform s'applique en DIPs → les translations sont divisées par _dpiScale.
    /// </summary>
    private void UpdateInterimTransform()
    {
        if (_vm is null)
            return;
        if (!_vm.ComposedViewMatrix.TryInvert(out var composedInverse))
            return;
        var delta = _vm.ViewMatrix.PreConcat(composedInverse);
        CompositeImage.RenderTransform = new MatrixTransform(
            delta.ScaleX, delta.SkewY, delta.SkewX, delta.ScaleY,
            delta.TransX / _dpiScale, delta.TransY / _dpiScale);
    }

    private void UpdateLoupe(Point pos, bool force = false)
    {
        // La loupe ne sert qu'à VISER : elle disparaît dès qu'aucun point n'est attendu.
        if (_vm is null || !_vm.IsPosingPoint)
        {
            LoupeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        // ~30 images/s suffisent pour viser ; au-delà on gaspille le CPU.
        var now = DateTime.UtcNow;
        if (!force && (now - _lastLoupeUpdate).TotalMilliseconds < 33)
            return;
        _lastLoupeUpdate = now;

        int sizePx = Math.Max(1, (int)Math.Round(LoupeSizeDip * _dpiScale));
        using var bitmap = _vm.ComposeLoupe(DevicePoint(pos), sizePx, LoupeMagnification);
        LoupeImage.Source = SkiaInterop.ToWriteableBitmap(
            bitmap, LoupeImage.Source as System.Windows.Media.Imaging.WriteableBitmap, _dpiScale);

        // La loupe se place en haut à droite du curseur, ou de l'autre côté près des bords.
        double x = pos.X + 24;
        double y = pos.Y - LoupeSizeDip - 28;
        if (x + LoupeSizeDip + 8 > ActualWidth)
            x = pos.X - LoupeSizeDip - 24;
        if (y < 8)
            y = pos.Y + 24;
        System.Windows.Controls.Canvas.SetLeft(LoupeBorder, x);
        System.Windows.Controls.Canvas.SetTop(LoupeBorder, y);
        LoupeBorder.Visibility = Visibility.Visible;
    }
}