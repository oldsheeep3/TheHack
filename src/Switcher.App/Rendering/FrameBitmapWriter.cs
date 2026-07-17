using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Switcher.Contracts;

namespace Switcher.App.Rendering;

/// <summary>
/// Copies a composited <see cref="FrameData"/> (BGRA32, per Switcher.Media/Switcher.VirtualCam) into a
/// reusable <see cref="WriteableBitmap"/> for display. Must be called on the owning bitmap's
/// dispatcher thread.
/// </summary>
public static class FrameBitmapWriter
{
    /// <summary>Writes <paramref name="frame"/> into <paramref name="bitmap"/>, reallocating the
    /// bitmap if its size doesn't match the frame.</summary>
    public static WriteableBitmap Write(WriteableBitmap? bitmap, FrameData frame)
    {
        if (bitmap is null || bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height)
        {
            bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, palette: null);
        }

        var stride = frame.Width * 4;
        var rect = new Int32Rect(0, 0, frame.Width, frame.Height);
        bitmap.WritePixels(rect, frame.Pixels.ToArray(), stride, 0);
        return bitmap;
    }
}
