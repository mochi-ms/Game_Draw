using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

using CoreColor = GameDraw.Core.Colors.RgbColor;

namespace GameDraw.Core.Imaging;

public sealed class ImageBuffer
{
    private readonly CoreColor?[] _pixels;

    public ImageBuffer(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Width = width;
        Height = height;
        _pixels = new CoreColor?[width * height];
    }

    public ImageBuffer(int width, int height, IEnumerable<CoreColor?> pixels)
        : this(width, height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var values = pixels.ToArray();
        if (values.Length != _pixels.Length)
        {
            throw new ArgumentException("The pixel count does not match the image dimensions.", nameof(pixels));
        }

        values.CopyTo(_pixels, 0);
    }

    public int Width { get; }

    public int Height { get; }

    public CoreColor? this[int x, int y]
    {
        get
        {
            ValidateCoordinates(x, y);
            return _pixels[(y * Width) + x];
        }
        set
        {
            ValidateCoordinates(x, y);
            _pixels[(y * Width) + x] = value;
        }
    }

    public IEnumerable<CoreColor?> Pixels => _pixels;

    public ImageBuffer Clone() => new(Width, Height, _pixels);

    public byte[] ToPngBytes()
    {
        using var image = new Image<Rgba32>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = this[x, y];
                image[x, y] = pixel is { } color
                    ? new Rgba32(color.R, color.G, color.B, 255)
                    : new Rgba32(0, 0, 0, 0);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    private void ValidateCoordinates(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }
}
