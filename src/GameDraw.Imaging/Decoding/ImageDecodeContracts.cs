using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Decoding;

public enum AlphaPolicy
{
    Preserve = 0,
    CompositeOnBackground = 1,
    Reject = 2
}

public sealed record ImageDecodeOptions
{
    public bool ApplyExifOrientation { get; init; } = true;

    // Preserve source alpha by default; a target profile can opt into
    // compositing against its known canvas color without losing source data.
    public AlphaPolicy AlphaPolicy { get; init; } = AlphaPolicy.Preserve;

    public RgbColor BackgroundColor { get; init; } = RgbColor.White;

    public byte TransparentThreshold { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(AlphaPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(AlphaPolicy));
        }
    }
}

public sealed record DecodedImage(
    ImageFrame Frame,
    string? SourceName,
    string FormatName,
    int BitsPerChannel);

public interface IImageDecoder
{
    Task<DecodedImage> DecodeAsync(
        Stream source,
        string? sourceName = null,
        ImageDecodeOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<DecodedImage> DecodeFileAsync(
        string path,
        ImageDecodeOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class ImageDecodeException(string message, Exception? innerException = null)
    : Exception(message, innerException);
