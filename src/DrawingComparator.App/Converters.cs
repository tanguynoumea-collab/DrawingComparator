using System.Globalization;
using System.Windows;
using System.Windows.Data;

using DrawingComparator.Core;

namespace DrawingComparator.App;

/// <summary>Visible si la valeur est non nulle (et non chaîne vide), sinon Collapsed.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Collapsed si la valeur booléenne est vraie (pour masquer l'état vide une fois un plan chargé).</summary>
public sealed class BoolToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Teinte d'un calque → pinceau de son liseré (couleurs métier des tokens).</summary>
public sealed class TintToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is LayerTint.Red ? "BrushTintBase" : "BrushTintRevision";
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Négation booléenne bidirectionnelle (segmented Rigide/Affine : le bouton « Rigide » = !IsAffineMode).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>
/// Égalité entre un double lié et le paramètre du XAML (segmented du pas d'ajustement) :
/// coché ⇔ valeur = paramètre ; cocher pousse le paramètre dans la propriété.
/// </summary>
public sealed class DoubleEqualsConverter : IValueConverter
{
    private static double Param(object? parameter)
        => double.Parse((string)parameter!, CultureInfo.InvariantCulture);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && Math.Abs(d - Param(parameter)) < 0.0001;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Param(parameter) : Binding.DoNothing;
}