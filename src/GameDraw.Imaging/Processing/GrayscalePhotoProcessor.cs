using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

public static class GrayscalePhotoProcessor
{
    public static ImageFrame Process(ImageFrame source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var luminance = source.Pixels.Select(pixel =>
        {
            if (pixel.Alpha == 0)
            {
                return 255d;
            }

            return (0.2126d * pixel.Color.R) +
                   (0.7152d * pixel.Color.G) +
                   (0.0722d * pixel.Color.B);
        }).ToArray();
        var pixels = new RgbaPixel[source.PixelCount];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var index = (y * source.Width) + x;
                var sourcePixel = source.Pixels[index];
                if (sourcePixel.Alpha == 0)
                {
                    pixels[index] = sourcePixel;
                    continue;
                }

                // A small edge-preserving unsharp pass keeps eyes, hair and
                // fabric detail visible after palette quantization. Constant
                // neutral areas remain unchanged, so it adds detail without
                // inventing global contrast or banding.
                var weighted = 0d;
                var totalWeight = 0d;
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        var sampleX = Math.Clamp(x + offsetX, 0, source.Width - 1);
                        var sampleY = Math.Clamp(y + offsetY, 0, source.Height - 1);
                        var sampleIndex = (sampleY * source.Width) + sampleX;
                        var weight = offsetX == 0 && offsetY == 0 ? 4d :
                            offsetX == 0 || offsetY == 0 ? 2d : 1d;
                        var sample = source.Pixels[sampleIndex].Alpha == 0
                            ? luminance[index]
                            : luminance[sampleIndex];
                        weighted += sample * weight;
                        totalWeight += weight;
                    }
                }

                var blurred = weighted / totalWeight;
                var enhanced = luminance[index] + ((luminance[index] - blurred) * 0.72d);
                var value = (byte)Math.Clamp((int)Math.Round(enhanced), 0, 255);
                pixels[index] = new RgbaPixel(new RgbColor(value, value, value), sourcePixel.Alpha);
            }
        }

        return new ImageFrame(source.Width, source.Height, pixels);
    }
}
