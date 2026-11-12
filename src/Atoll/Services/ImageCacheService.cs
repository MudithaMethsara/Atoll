using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Atoll.Services;

public readonly record struct CachedArt(BitmapSource Image, Color DominantColor);

public sealed class ImageCacheService
{
    private const int MaxEntries = 24;
    private const int DecodeTargetPx = 128;

    private readonly Dictionary<string, LinkedListNode<(string Key, CachedArt Art)>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, CachedArt Art)> _lru = new();
    private readonly object _sync = new();

    public CachedArt? GetOrDecode(byte[] bytes, string? hintKey = null)
    {
        if (bytes.Length == 0) return null;

        string key = hintKey ?? ComputeHashKey(bytes);
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Art;
            }
        }

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;

            BitmapSource frame = decoder.Frames[0];
            BitmapSource scaled = DownscaleIfNeeded(frame, DecodeTargetPx);
            scaled.Freeze();

            Color dominant = ExtractDominantColor(scaled);
            var entry = new CachedArt(scaled, dominant);

            lock (_sync)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    _lru.Remove(existing);
                    _map.Remove(key);
                }

                var newNode = new LinkedListNode<(string, CachedArt)>((key, entry));
                _lru.AddFirst(newNode);
                _map[key] = newNode;

                while (_lru.Count > MaxEntries && _lru.Last is not null)
                {
                    var last = _lru.Last;
                    _lru.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }

            return entry;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("ImageCache", $"Failed to decode art: {ex.Message}");
            return null;
        }
    }

    public static Color ExtractDominantColor(BitmapSource source)
    {
        try
        {
            BitmapSource bgra = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            if (width <= 0 || height <= 0) return Color.FromRgb(10, 132, 255);

            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            bgra.CopyPixels(pixels, stride, 0);

            int stepX = Math.Max(1, width / 16);
            int stepY = Math.Max(1, height / 16);

            double sumR = 0, sumG = 0, sumB = 0, weightSum = 0;

            for (int y = 0; y < height; y += stepY)
            {
                int row = y * stride;
                for (int x = 0; x < width; x += stepX)
                {
                    int idx = row + x * 4;
                    byte b = pixels[idx];
                    byte g = pixels[idx + 1];
                    byte r = pixels[idx + 2];
                    byte a = pixels[idx + 3];
                    if (a < 32) continue;

                    double max = Math.Max(r, Math.Max(g, b)) / 255.0;
                    double min = Math.Min(r, Math.Min(g, b)) / 255.0;
                    double sat = max <= 0.001 ? 0.0 : (max - min) / max;
                    double lum = (max + min) * 0.5;

                    // Prefer vibrant non-black/non-white pixels for iOS-style glow
                    double lumWeight = (lum > 0.12 && lum < 0.88) ? 1.0 : 0.25;
                    double weight = (0.3 + sat * 2.2) * lumWeight;

                    sumR += r * weight;
                    sumG += g * weight;
                    sumB += b * weight;
                    weightSum += weight;
                }
            }

            if (weightSum <= 0.001) return Color.FromRgb(10, 132, 255);

            byte avgR = (byte)Math.Clamp(sumR / weightSum, 35, 255);
            byte avgG = (byte)Math.Clamp(sumG / weightSum, 35, 255);
            byte avgB = (byte)Math.Clamp(sumB / weightSum, 35, 255);
            return Color.FromRgb(avgR, avgG, avgB);
        }
        catch
        {
            return Color.FromRgb(10, 132, 255);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    private static BitmapSource DownscaleIfNeeded(BitmapSource source, int maxSide)
    {
        if (source.PixelWidth <= maxSide && source.PixelHeight <= maxSide)
        {
            return source;
        }

        double scale = Math.Min((double)maxSide / source.PixelWidth, (double)maxSide / source.PixelHeight);
        var tb = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        return tb;
    }

    private static string ComputeHashKey(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 12);
    }
}

