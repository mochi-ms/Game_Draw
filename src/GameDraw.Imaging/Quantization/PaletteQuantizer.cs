using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Color;
using GameDraw.Imaging.Palettes;

namespace GameDraw.Imaging.Quantization;

public enum DitherMode
{
    None = 0,
    FloydSteinberg = 1,
    Atkinson = 2,
    OrderedBayer4 = 3
}

public sealed record QuantizationOptions
{
    public DitherMode DitherMode { get; init; } = DitherMode.None;

    public ColorDistanceMetric DistanceMetric { get; init; } = ColorDistanceMetric.Cie76;

    public double ErrorDiffusionStrength { get; init; } = 1d;

    public byte TransparentThreshold { get; init; }

    public bool PreserveAlpha { get; init; } = true;

    public int TransparentPaletteIndex { get; init; }

    public void Validate(int paletteSize)
    {
        if (!Enum.IsDefined(DitherMode))
        {
            throw new ArgumentException("지원하지 않는 디더링 모드입니다.");
        }

        if (!Enum.IsDefined(DistanceMetric))
        {
            throw new ArgumentException("지원하지 않는 색상 거리 메트릭입니다.");
        }

        if (!double.IsFinite(ErrorDiffusionStrength) || ErrorDiffusionStrength < 0d || ErrorDiffusionStrength > 1d)
        {
            throw new ArgumentException("오류 확산 강도는 0~1 사이여야 합니다.");
        }

        if (TransparentPaletteIndex < 0 || TransparentPaletteIndex >= paletteSize)
        {
            throw new ArgumentException("투명 팔레트 인덱스가 팔레트 범위를 벗어났습니다.");
        }
    }
}

public sealed class QuantizedImage
{
    private readonly byte[] _indices;

    internal QuantizedImage(
        ImageFrame source,
        ImageFrame rendered,
        ColorPalette palette,
        IReadOnlyList<byte> indices)
    {
        Source = source;
        Rendered = rendered;
        Palette = palette;
        _indices = indices.ToArray();
    }

    public ImageFrame Source { get; }

    public ImageFrame Rendered { get; }

    public ColorPalette Palette { get; }

    public int Width => Source.Width;

    public int Height => Source.Height;

    public IReadOnlyList<byte> Indices => _indices;

    public byte this[int x, int y] => _indices[(y * Width) + x];
}

public sealed class PaletteQuantizer
{
    private static readonly double[,] Bayer4 =
    {
        { 0d, 8d, 2d, 10d },
        { 12d, 4d, 14d, 6d },
        { 3d, 11d, 1d, 9d },
        { 15d, 7d, 13d, 5d }
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Kept as an instance service so the pipeline can replace it with a target-specific quantizer.")]
    public QuantizedImage Quantize(
        ImageFrame source,
        ColorPalette palette,
        QuantizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(palette);
        options ??= new QuantizationOptions();
        options.Validate(palette.Count);

        var paletteColors = palette.Colors.ToArray();
        var paletteLabs = paletteColors.Select(ColorMath.ToLab).ToArray();
        var paletteLinear = paletteColors.Select(ColorMath.ToLinear).ToArray();
        var indices = new byte[source.PixelCount];
        var rendered = new RgbaPixel[source.PixelCount];

        switch (options.DitherMode)
        {
            case DitherMode.None:
                MapWithoutDither(source, palette, paletteLabs, indices, rendered, options);
                break;
            case DitherMode.OrderedBayer4:
                MapWithOrderedDither(source, palette, paletteLabs, indices, rendered, options);
                break;
            case DitherMode.FloydSteinberg:
                MapWithErrorDiffusion(source, palette, paletteLabs, paletteLinear, indices, rendered, options, FloydSteinbergWeights);
                break;
            case DitherMode.Atkinson:
                MapWithErrorDiffusion(source, palette, paletteLabs, paletteLinear, indices, rendered, options, AtkinsonWeights);
                break;
            default:
                throw new InvalidOperationException("지원하지 않는 디더링 모드입니다.");
        }

        return new QuantizedImage(source, new ImageFrame(source.Width, source.Height, rendered), palette, indices);
    }

    private static void MapWithoutDither(
        ImageFrame source,
        ColorPalette palette,
        IReadOnlyList<LabColor> paletteLabs,
        Span<byte> indices,
        Span<RgbaPixel> rendered,
        QuantizationOptions options)
    {
        for (var index = 0; index < source.PixelCount; index++)
        {
            var sourcePixel = source.Pixels[index];
            if (sourcePixel.Alpha <= options.TransparentThreshold)
            {
                indices[index] = (byte)options.TransparentPaletteIndex;
                rendered[index] = Render(palette[options.TransparentPaletteIndex], sourcePixel.Alpha, options.PreserveAlpha);
                continue;
            }

            var mappedIndex = FindNearest(sourcePixel.Color, paletteLabs, options.DistanceMetric);
            indices[index] = (byte)mappedIndex;
            rendered[index] = Render(palette[mappedIndex], sourcePixel.Alpha, options.PreserveAlpha);
        }
    }

    private static void MapWithOrderedDither(
        ImageFrame source,
        ColorPalette palette,
        IReadOnlyList<LabColor> paletteLabs,
        Span<byte> indices,
        Span<RgbaPixel> rendered,
        QuantizationOptions options)
    {
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var index = (y * source.Width) + x;
                var sourcePixel = source.Pixels[index];
                if (sourcePixel.Alpha <= options.TransparentThreshold)
                {
                    indices[index] = (byte)options.TransparentPaletteIndex;
                    rendered[index] = Render(palette[options.TransparentPaletteIndex], sourcePixel.Alpha, options.PreserveAlpha);
                    continue;
                }

                var rgb = ColorMath.ToLinear(sourcePixel.Color);
                var offset = ((Bayer4[y % 4, x % 4] + 0.5d) / 16d) - 0.5d;
                var adjusted = new LinearRgb(
                    rgb.R + (offset * options.ErrorDiffusionStrength / 8d),
                    rgb.G + (offset * options.ErrorDiffusionStrength / 8d),
                    rgb.B + (offset * options.ErrorDiffusionStrength / 8d));
                var mappedColor = ColorMath.FromLinear(adjusted);
                var mappedIndex = FindNearest(mappedColor, paletteLabs, options.DistanceMetric);
                indices[index] = (byte)mappedIndex;
                rendered[index] = Render(palette[mappedIndex], sourcePixel.Alpha, options.PreserveAlpha);
            }
        }
    }

    private static void MapWithErrorDiffusion(
        ImageFrame source,
        ColorPalette palette,
        IReadOnlyList<LabColor> paletteLabs,
        LinearRgb[] paletteLinear,
        Span<byte> indices,
        Span<RgbaPixel> rendered,
        QuantizationOptions options,
        IReadOnlyList<ErrorWeight> weights)
    {
        var errors = new double[checked(source.PixelCount * 3)];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var index = (y * source.Width) + x;
                var sourcePixel = source.Pixels[index];
                if (sourcePixel.Alpha <= options.TransparentThreshold)
                {
                    indices[index] = (byte)options.TransparentPaletteIndex;
                    rendered[index] = Render(palette[options.TransparentPaletteIndex], sourcePixel.Alpha, options.PreserveAlpha);
                    continue;
                }

                var sourceLinear = ColorMath.ToLinear(sourcePixel.Color);
                var adjusted = new LinearRgb(
                    sourceLinear.R + errors[(index * 3) + 0],
                    sourceLinear.G + errors[(index * 3) + 1],
                    sourceLinear.B + errors[(index * 3) + 2]);
                var mappedColor = ColorMath.FromLinear(adjusted);
                var mappedIndex = FindNearest(mappedColor, paletteLabs, options.DistanceMetric);
                var mappedLinear = paletteLinear[mappedIndex];
                indices[index] = (byte)mappedIndex;
                rendered[index] = Render(palette[mappedIndex], sourcePixel.Alpha, options.PreserveAlpha);

                var errorR = (adjusted.R - mappedLinear.R) * options.ErrorDiffusionStrength;
                var errorG = (adjusted.G - mappedLinear.G) * options.ErrorDiffusionStrength;
                var errorB = (adjusted.B - mappedLinear.B) * options.ErrorDiffusionStrength;
                foreach (var weight in weights)
                {
                    var targetX = x + weight.OffsetX;
                    var targetY = y + weight.OffsetY;
                    if ((uint)targetX >= (uint)source.Width || (uint)targetY >= (uint)source.Height)
                    {
                        continue;
                    }

                    var targetIndex = (targetY * source.Width) + targetX;
                    errors[(targetIndex * 3) + 0] += errorR * weight.Weight;
                    errors[(targetIndex * 3) + 1] += errorG * weight.Weight;
                    errors[(targetIndex * 3) + 2] += errorB * weight.Weight;
                }
            }
        }
    }

    private static int FindNearest(
        RgbColor color,
        IReadOnlyList<LabColor> paletteLabs,
        ColorDistanceMetric metric)
    {
        var lab = ColorMath.ToLab(color);
        var bestIndex = 0;
        var bestDistance = double.PositiveInfinity;
        for (var index = 0; index < paletteLabs.Count; index++)
        {
            var distance = ColorMath.DeltaE(lab, paletteLabs[index], metric);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static RgbaPixel Render(RgbColor color, byte alpha, bool preserveAlpha)
        => new(color, preserveAlpha ? alpha : byte.MaxValue);

    private static readonly ErrorWeight[] FloydSteinbergWeights =
    {
        new(1, 0, 7d / 16d),
        new(-1, 1, 3d / 16d),
        new(0, 1, 5d / 16d),
        new(1, 1, 1d / 16d)
    };

    private static readonly ErrorWeight[] AtkinsonWeights =
    {
        new(1, 0, 1d / 8d),
        new(2, 0, 1d / 8d),
        new(-1, 1, 1d / 8d),
        new(0, 1, 1d / 8d),
        new(1, 1, 1d / 8d),
        new(0, 2, 1d / 8d)
    };

    private readonly record struct ErrorWeight(int OffsetX, int OffsetY, double Weight);
}
