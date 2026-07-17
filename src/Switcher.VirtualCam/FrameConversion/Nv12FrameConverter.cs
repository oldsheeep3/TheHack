using Switcher.Contracts;

namespace Switcher.VirtualCam.FrameConversion;

/// <summary>
/// Converts a composited <see cref="FrameData"/> (BGRA32, matching
/// <c>Switcher.Media.Compositing.DirectX11Compositor</c>'s render-target format) into NV12 — the
/// pixel format expected by the DirectShow virtual camera device (see README.md, §"Virtual camera
/// device"). NV12 is one Y plane (full resolution) followed by one interleaved U/V plane
/// (half resolution in each dimension), which is what DirectShow/Media Foundation capture consumers
/// expect from a virtual webcam and is roughly half the size of the source BGRA32 buffer.
/// </summary>
internal static class Nv12FrameConverter
{
    private const int BgraBytesPerPixel = 4;
    private const int ChromaSubsampling = 2;

    // ITU-R BT.601 (studio range) RGB -> YUV coefficients.
    private const float YRCoefficient = 0.257f;
    private const float YGCoefficient = 0.504f;
    private const float YBCoefficient = 0.098f;
    private const int YOffset = 16;

    private const float URCoefficient = -0.148f;
    private const float UGCoefficient = -0.291f;
    private const float UBCoefficient = 0.439f;

    private const float VRCoefficient = 0.439f;
    private const float VGCoefficient = -0.368f;
    private const float VBCoefficient = -0.071f;

    private const int ChromaOffset = 128;

    /// <summary>Number of bytes an NV12 buffer needs for the given resolution. Both dimensions must
    /// be even (required by 4:2:0 chroma subsampling).</summary>
    public static int GetRequiredBufferSize(int width, int height)
    {
        ValidateDimensions(width, height);
        var lumaSize = width * height;
        var chromaSize = (width / ChromaSubsampling) * (height / ChromaSubsampling) * ChromaSubsampling;
        return lumaSize + chromaSize;
    }

    /// <summary>Writes <paramref name="frame"/>'s pixels into <paramref name="destination"/> as NV12.
    /// <paramref name="destination"/> must be at least <see cref="GetRequiredBufferSize"/> bytes.</summary>
    public static void ConvertBgraToNv12(FrameData frame, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateDimensions(frame.Width, frame.Height);

        var requiredSize = GetRequiredBufferSize(frame.Width, frame.Height);
        if (destination.Length < requiredSize)
        {
            throw new ArgumentException(
                $"Destination buffer ({destination.Length} bytes) is smaller than the required NV12 size ({requiredSize} bytes).",
                nameof(destination));
        }

        var source = frame.Pixels.Span;
        var expectedSourceSize = frame.Width * frame.Height * BgraBytesPerPixel;
        if (source.Length < expectedSourceSize)
        {
            throw new ArgumentException(
                $"Source frame has fewer pixel bytes ({source.Length}) than its declared {frame.Width}x{frame.Height} BGRA32 size ({expectedSourceSize}).",
                nameof(frame));
        }

        var lumaPlaneSize = frame.Width * frame.Height;
        var lumaPlane = destination[..lumaPlaneSize];
        var chromaPlane = destination.Slice(lumaPlaneSize, GetRequiredBufferSize(frame.Width, frame.Height) - lumaPlaneSize);

        WriteLumaPlane(frame, source, lumaPlane);
        WriteInterleavedChromaPlane(frame, source, chromaPlane);
    }

    private static void WriteLumaPlane(FrameData frame, ReadOnlySpan<byte> source, Span<byte> lumaPlane)
    {
        var stride = frame.Width * BgraBytesPerPixel;
        for (var y = 0; y < frame.Height; y++)
        {
            var row = source.Slice(y * stride, stride);
            var lumaRow = lumaPlane.Slice(y * frame.Width, frame.Width);
            for (var x = 0; x < frame.Width; x++)
            {
                var pixel = row.Slice(x * BgraBytesPerPixel, BgraBytesPerPixel);
                lumaRow[x] = ComputeLuma(pixel[2], pixel[1], pixel[0]);
            }
        }
    }

    private static void WriteInterleavedChromaPlane(FrameData frame, ReadOnlySpan<byte> source, Span<byte> chromaPlane)
    {
        var stride = frame.Width * BgraBytesPerPixel;
        var chromaWidth = frame.Width / ChromaSubsampling;

        for (var chromaY = 0; chromaY < frame.Height / ChromaSubsampling; chromaY++)
        {
            var chromaRow = chromaPlane.Slice(chromaY * chromaWidth * ChromaSubsampling, chromaWidth * ChromaSubsampling);
            for (var chromaX = 0; chromaX < chromaWidth; chromaX++)
            {
                var (r, g, b) = AverageBlock(source, stride, chromaX * ChromaSubsampling, chromaY * ChromaSubsampling);
                chromaRow[chromaX * ChromaSubsampling] = ComputeChromaU(r, g, b);
                chromaRow[chromaX * ChromaSubsampling + 1] = ComputeChromaV(r, g, b);
            }
        }
    }

    private static (float R, float G, float B) AverageBlock(ReadOnlySpan<byte> source, int stride, int originX, int originY)
    {
        float r = 0, g = 0, b = 0;
        for (var dy = 0; dy < ChromaSubsampling; dy++)
        {
            var row = source.Slice((originY + dy) * stride, stride);
            for (var dx = 0; dx < ChromaSubsampling; dx++)
            {
                var pixel = row.Slice((originX + dx) * BgraBytesPerPixel, BgraBytesPerPixel);
                b += pixel[0];
                g += pixel[1];
                r += pixel[2];
            }
        }

        const int SampleCount = ChromaSubsampling * ChromaSubsampling;
        return (r / SampleCount, g / SampleCount, b / SampleCount);
    }

    private static byte ComputeLuma(byte r, byte g, byte b) =>
        ClampToByte(YRCoefficient * r + YGCoefficient * g + YBCoefficient * b + YOffset);

    private static byte ComputeChromaU(float r, float g, float b) =>
        ClampToByte(URCoefficient * r + UGCoefficient * g + UBCoefficient * b + ChromaOffset);

    private static byte ComputeChromaV(float r, float g, float b) =>
        ClampToByte(VRCoefficient * r + VGCoefficient * g + VBCoefficient * b + ChromaOffset);

    private static byte ClampToByte(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);

    private static void ValidateDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width % ChromaSubsampling != 0 || height % ChromaSubsampling != 0)
        {
            throw new ArgumentException($"Width ({width}) and height ({height}) must both be even for 4:2:0 NV12 output.");
        }
    }
}
