using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

/// <summary>
/// Removes only near-white pixels connected to the image border. Enclosed
/// whites such as teeth, eye whites, highlights, and clothing remain drawable.
/// </summary>
public static class NearWhiteBackgroundProcessor
{
    public static ImageFrame RemoveBorderConnected(
        ImageFrame source,
        byte minimumChannel = 232,
        byte maximumChroma = 18)
    {
        ArgumentNullException.ThrowIfNull(source);
        var remove = new bool[source.PixelCount];
        var queued = new bool[source.PixelCount];
        var queue = new Queue<int>();

        for (var x = 0; x < source.Width; x++)
        {
            Enqueue(x, 0);
            Enqueue(x, source.Height - 1);
        }

        for (var y = 1; y < source.Height - 1; y++)
        {
            Enqueue(0, y);
            Enqueue(source.Width - 1, y);
        }

        while (queue.TryDequeue(out var index))
        {
            var pixel = source.Pixels[index];
            var minimum = Math.Min(pixel.Color.R, Math.Min(pixel.Color.G, pixel.Color.B));
            var maximum = Math.Max(pixel.Color.R, Math.Max(pixel.Color.G, pixel.Color.B));
            if (pixel.Alpha > 0 && (minimum < minimumChannel || maximum - minimum > maximumChroma))
            {
                continue;
            }

            remove[index] = true;
            var x = index % source.Width;
            var y = index / source.Width;
            if (x > 0) Enqueue(x - 1, y);
            if (x + 1 < source.Width) Enqueue(x + 1, y);
            if (y > 0) Enqueue(x, y - 1);
            if (y + 1 < source.Height) Enqueue(x, y + 1);
        }

        var pixels = source.Pixels
            .Select((pixel, index) => remove[index]
                ? new RgbaPixel(pixel.Color, 0)
                : pixel)
            .ToArray();
        return new ImageFrame(source.Width, source.Height, pixels);

        void Enqueue(int x, int y)
        {
            var index = (y * source.Width) + x;
            if (!queued[index])
            {
                queued[index] = true;
                queue.Enqueue(index);
            }
        }
    }
}
