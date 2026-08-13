using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace DrawingComparator.App.Services;

public static class SkiaInterop
{
    /// <summary>
    /// Copie un SKBitmap Bgra8888 prémultiplié vers un WriteableBitmap Pbgra32,
    /// en réutilisant le bitmap existant si les dimensions n'ont pas changé.
    /// </summary>
    public static WriteableBitmap ToWriteableBitmap(SKBitmap source, WriteableBitmap? existing)
    {
        var target = existing is { } e && e.PixelWidth == source.Width && e.PixelHeight == source.Height
            ? e
            : new WriteableBitmap(source.Width, source.Height, 96, 96, PixelFormats.Pbgra32, null);

        target.WritePixels(
            new Int32Rect(0, 0, source.Width, source.Height),
            source.GetPixels(),
            source.ByteCount,
            source.RowBytes);
        return target;
    }
}
