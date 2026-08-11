namespace GameDraw.Core.Imaging;

public sealed class ImageFrame
{
    private readonly RgbaPixel[] _pixels;

    public ImageFrame(int width, int height)
        : this(width, height, new RgbaPixel[checked(width * height)])
    {
    }

    public ImageFrame(int width, int height, IReadOnlyList<RgbaPixel> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        ArgumentNullException.ThrowIfNull(pixels);
        var expected = checked(width * height);
        if (pixels.Count != expected)
        {
            throw new ArgumentException($"Expected {expected} pixels but received {pixels.Count}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public int PixelCount => _pixels.Length;
    public IReadOnlyList<RgbaPixel> Pixels => _pixels;

    public RgbaPixel this[int x, int y] => _pixels[GetIndex(x, y)];

    public ImageFrame WithPixel(int x, int y, RgbaPixel pixel)
    {
        var copy = _pixels.ToArray();
        copy[GetIndex(x, y)] = pixel;
        return new ImageFrame(Width, Height, copy);
    }

    public ImageFrame Clone() => new(Width, Height, _pixels);

    private int GetIndex(int x, int y)
    {
        if ((uint)x >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return checked((y * Width) + x);
    }
}
