using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using SkiaSharp;

namespace DrawingComparator.App.Services;

public static class SkiaInterop
{
    /// <summary>
    /// Copie un SKBitmap Bgra8888 prémultiplié vers un WriteableBitmap Pbgra32,
    /// en réutilisant le bitmap existant si dimensions et DPI n'ont pas changé.
    /// Le DPI porté (96 × displayScale) fait afficher le bitmap pixel-à-pixel par WPF.
    /// </summary>
    public static WriteableBitmap ToWriteableBitmap(SKBitmap source, WriteableBitmap? existing, double displayScale = 1.0)
    {
        double dpi = 96.0 * displayScale;
        var target = existing is { } e && e.PixelWidth == source.Width && e.PixelHeight == source.Height
                     && Math.Abs(e.DpiX - dpi) < 0.1
            ? e
            : new WriteableBitmap(source.Width, source.Height, dpi, dpi, PixelFormats.Pbgra32, null);

        target.WritePixels(
            new Int32Rect(0, 0, source.Width, source.Height),
            source.GetPixels(),
            source.ByteCount,
            source.RowBytes);
        return target;
    }
}