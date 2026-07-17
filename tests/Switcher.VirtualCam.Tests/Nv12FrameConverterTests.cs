using Switcher.Contracts;
using Switcher.VirtualCam.FrameConversion;

namespace Switcher.VirtualCam.Tests;

public sealed class Nv12FrameConverterTests
{
    private static byte[] Bgra(byte b, byte g, byte r, byte a = 255) => [b, g, r, a];

    private static FrameData SolidFrame(int width, int height, byte b, byte g, byte r)
    {
        var pixel = Bgra(b, g, r);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            pixel.CopyTo(pixels, i * 4);
        }

        return new FrameData(width, height, pixels);
    }

    [Theory]
    [InlineData(2, 2, 6)]
    [InlineData(4, 2, 12)]
    [InlineData(4, 4, 24)]
    public void GetRequiredBufferSize_ReturnsLumaPlusHalfResolutionInterleavedChroma(int width, int height, int expected)
    {
        Assert.Equal(expected, Nv12FrameConverter.GetRequiredBufferSize(width, height));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    [InlineData(0, 2)]
    [InlineData(2, -2)]
    public void GetRequiredBufferSize_RejectsInvalidDimensions(int width, int height)
    {
        Assert.ThrowsAny<ArgumentException>(() => Nv12FrameConverter.GetRequiredBufferSize(width, height));
    }

    [Fact]
    public void ConvertBgraToNv12_White_ProducesNeutralChromaAndBrightLuma()
    {
        var frame = SolidFrame(2, 2, b: 255, g: 255, r: 255);
        var destination = new byte[Nv12FrameConverter.GetRequiredBufferSize(2, 2)];

        Nv12FrameConverter.ConvertBgraToNv12(frame, destination);

        Assert.Equal([235, 235, 235, 235], destination[..4]);
        Assert.Equal([128, 128], destination[4..]);
    }

    [Fact]
    public void ConvertBgraToNv12_Black_ProducesNeutralChromaAndDarkLuma()
    {
        var frame = SolidFrame(2, 2, b: 0, g: 0, r: 0);
        var destination = new byte[Nv12FrameConverter.GetRequiredBufferSize(2, 2)];

        Nv12FrameConverter.ConvertBgraToNv12(frame, destination);

        Assert.Equal([16, 16, 16, 16], destination[..4]);
        Assert.Equal([128, 128], destination[4..]);
    }

    [Fact]
    public void ConvertBgraToNv12_ComputesPerPixelLumaButAveragesChromaAcrossTheBlock()
    {
        // A 2x2 chroma block containing two pure-red and two pure-blue pixels: luma differs
        // per pixel, but the single U/V sample for the block is derived from their average color.
        var pixels = new byte[2 * 2 * 4];
        Bgra(b: 0, g: 0, r: 255).CopyTo(pixels, 0 * 4); // top-left: red
        Bgra(b: 0, g: 0, r: 255).CopyTo(pixels, 1 * 4); // top-right: red
        Bgra(b: 255, g: 0, r: 0).CopyTo(pixels, 2 * 4); // bottom-left: blue
        Bgra(b: 255, g: 0, r: 0).CopyTo(pixels, 3 * 4); // bottom-right: blue
        var frame = new FrameData(2, 2, pixels);
        var destination = new byte[Nv12FrameConverter.GetRequiredBufferSize(2, 2)];

        Nv12FrameConverter.ConvertBgraToNv12(frame, destination);

        Assert.Equal([82, 82, 41, 41], destination[..4]);
        Assert.Equal([165, 175], destination[4..]);
    }

    [Fact]
    public void ConvertBgraToNv12_DestinationTooSmall_Throws()
    {
        var frame = SolidFrame(2, 2, 0, 0, 0);
        var destination = new byte[Nv12FrameConverter.GetRequiredBufferSize(2, 2) - 1];

        Assert.Throws<ArgumentException>(() => Nv12FrameConverter.ConvertBgraToNv12(frame, destination));
    }

    [Fact]
    public void ConvertBgraToNv12_SourceShorterThanDeclaredResolution_Throws()
    {
        var frame = new FrameData(4, 4, new byte[4]);
        var destination = new byte[Nv12FrameConverter.GetRequiredBufferSize(4, 4)];

        Assert.Throws<ArgumentException>(() => Nv12FrameConverter.ConvertBgraToNv12(frame, destination));
    }
}
