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
    /// bitmap if its size doesn't match the frame. An empty frame (0x0 - what
    /// <see cref="IVideoEngine.GetFrame"/> returns before the engine has rendered/tapped a target) leaves
    /// <paramref name="bitmap"/> untouched and is returned as-is: <see cref="WriteableBitmap"/> rejects a
    /// zero width/height with "Value does not fall within the expected range", so callers must never
    /// force-allocate one from an unrendered frame.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(bitmap))]
    public static WriteableBitmap? Write(WriteableBitmap? bitmap, FrameData frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length < frame.Width * frame.Height * 4)
        {
            return bitmap;
        }

        if (bitmap is null || bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height)
        {
            bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, palette: null);
        }

        var stride = frame.Width * 4;
        var rect = new Int32Rect(0, 0, frame.Width, frame.Height);

        // Hand WritePixels the buffer directly. The obvious `frame.Pixels.ToArray()` allocates and
        // copies a whole frame (8 MB at 1080p) on every write; at 30 fps across a dozen multiview cells
        // that alone buries the UI thread and the console visibly stops repainting - opening a second
        // window that also renders the cells was enough to freeze it outright.
        using var handle = frame.Pixels.Pin();
        unsafe
        {
            bitmap.WritePixels(rect, (IntPtr)handle.Pointer, frame.Pixels.Length, stride);
        }

        return bitmap;
    }
}
