using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Decoding;
using GameDraw.Imaging.Palettes;
using GameDraw.Imaging.Quantization;
using GameDraw.Imaging.Resampling;

namespace GameDraw.Imaging.Processing;

public sealed record ImageProcessingOptions
{
    public ImageDecodeOptions Decode { get; init; } = new();

    public PixelSize? TargetSize { get; init; }

    public ResamplingOptions Resampling { get; init; } = new();

    public PaletteBuildOptions Palette { get; init; } = new();

    public ColorPalette? FixedPalette { get; init; }

    public QuantizationOptions Quantization { get; init; } = new();

    public void Validate()
    {
        Decode.Validate();
        Resampling.Validate();
        Palette.Validate();
        if (FixedPalette is not null && FixedPalette.Count == 0)
        {
            throw new ArgumentException("고정 팔레트는 비어 있을 수 없습니다.", nameof(FixedPalette));
        }
    }
}

public sealed record ImageProcessingResult(
    DecodedImage Decoded,
    ImageFrame WorkingFrame,
    ColorPalette Palette,
    QuantizedImage Quantized);

/// <summary>
/// Orchestrates decode, optional high-quality resize, palette selection, and
/// quantization. Every intermediate frame is detached and deterministic.
/// </summary>
public sealed class ImageProcessingPipeline
{
    private readonly IImageDecoder _decoder;
    private readonly AdaptivePaletteBuilder _paletteBuilder;
    private readonly PaletteQuantizer _quantizer;

    public ImageProcessingPipeline(
        IImageDecoder? decoder = null,
        AdaptivePaletteBuilder? paletteBuilder = null,
        PaletteQuantizer? quantizer = null)
    {
        _decoder = decoder ?? new ImageDecoder();
        _paletteBuilder = paletteBuilder ?? new AdaptivePaletteBuilder();
        _quantizer = quantizer ?? new PaletteQuantizer();
    }

    public async Task<ImageProcessingResult> ProcessFileAsync(
        string path,
        ImageProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ImageProcessingOptions();
        options.Validate();
        var decoded = await _decoder.DecodeFileAsync(path, options.Decode, cancellationToken).ConfigureAwait(false);
        return ProcessDecoded(decoded, options, cancellationToken);
    }

    public async Task<ImageProcessingResult> ProcessAsync(
        Stream source,
        string? sourceName = null,
        ImageProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new ImageProcessingOptions();
        options.Validate();
        var decoded = await _decoder.DecodeAsync(source, sourceName, options.Decode, cancellationToken).ConfigureAwait(false);
        return ProcessDecoded(decoded, options, cancellationToken);
    }

    public ImageProcessingResult ProcessFrame(
        ImageFrame frame,
        ImageProcessingOptions? options = null,
        string? sourceName = null,
        string sourceFormat = "frame",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        options ??= new ImageProcessingOptions();
        options.Validate();
        return ProcessDecoded(new DecodedImage(frame.Clone(), sourceName, sourceFormat, 8), options, cancellationToken);
    }

    private ImageProcessingResult ProcessDecoded(
        DecodedImage decoded,
        ImageProcessingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var working = options.TargetSize is { } targetSize
            ? ImageResampler.Resize(decoded.Frame, targetSize, options.Resampling)
            : decoded.Frame.Clone();
        cancellationToken.ThrowIfCancellationRequested();

        var palette = options.FixedPalette ?? _paletteBuilder.Build(working, options.Palette);
        cancellationToken.ThrowIfCancellationRequested();
        var quantized = _quantizer.Quantize(working, palette, options.Quantization);
        cancellationToken.ThrowIfCancellationRequested();
        return new ImageProcessingResult(decoded, working, palette, quantized);
    }
}
