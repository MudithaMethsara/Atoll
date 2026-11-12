using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atoll.Services;
using Xunit;

namespace Atoll.Tests;

public sealed class DominantColorTests
{
    [Fact]
    public void ExtractDominantColor_ReturnsVibrantAccentFromSolidBitmap()
    {
        int width = 32, height = 32, stride = width * 4;
        byte[] bgra = new byte[height * stride];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 40;      // B
            bgra[i + 1] = 200; // G
            bgra[i + 2] = 60;  // R
            bgra[i + 3] = 255; // A
        }

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
        bmp.Freeze();

        Color dominant = ImageCacheService.ExtractDominantColor(bmp);
        Assert.True(dominant.G > dominant.R);
        Assert.True(dominant.G > dominant.B);
    }

    [Fact]
    public void ImageCache_CachesAndDownscalesLargeImage()
    {
        int width = 256, height = 256, stride = width * 4;
        byte[] bgra = new byte[height * stride];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 220;
            bgra[i + 1] = 90;
            bgra[i + 2] = 40;
            bgra[i + 3] = 255;
        }

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        byte[] pngBytes = ms.ToArray();

        var cache = new ImageCacheService();
        var first = cache.GetOrDecode(pngBytes, "test.art");
        var second = cache.GetOrDecode(pngBytes, "test.art");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first!.Value.Image.PixelWidth <= 128);
        Assert.Same(first.Value.Image, second!.Value.Image);
    }
}

