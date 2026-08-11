using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using CoreImageFrame = GameDraw.Core.Imaging.ImageFrame;

namespace GameDraw.Imaging.Decoding;

/// <summary>
/// Full-resolution decoder backed by ImageSharp/WIC-compatible format codecs.
/// The decoder materializes a detached <see cref="ImageFrame"/> so the source
/// stream and native image handle can be released immediately.
/// </summary>
public sealed class ImageDecoder : IImageDecoder
{
    public async Task<DecodedImage> DecodeAsync(
        Stream source,
        string? sourceName = null,
        ImageDecodeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new ImageDecodeOptions();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var image = Image.Load<Rgba32>(source);
            cancellationToken.ThrowIfCancellationRequested();

            if (options.ApplyExifOrientation)
            {
                image.Mutate(context => context.AutoOrient());
            }

            var rawPixels = new Rgba32[checked(image.Width * image.Height)];
            image.CopyPixelDataTo(rawPixels);
            var pixels = new RgbaPixel[rawPixels.Length];
            for (var y = 0; y < image.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < image.Width; x++)
                {
                    var sourcePixel = rawPixels[(y * image.Width) + x];
                    pixels[(y * image.Width) + x] = ConvertPixel(sourcePixel, options);
                }
            }

            return await Task.FromResult(new DecodedImage(
                new CoreImageFrame(image.Width, image.Height, pixels),
                sourceName,
                image.Metadata.DecodedImageFormat?.Name ?? "unknown",
                8));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ImageDecodeException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ImageDecodeException(
                $"이미지를 디코딩하지 못했습니다: {sourceName ?? "알 수 없는 입력"}",
                exception);
        }
    }

    public async Task<DecodedImage> DecodeFileAsync(
        string path,
        ImageDecodeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("이미지 경로는 비워 둘 수 없습니다.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("이미지 파일을 찾을 수 없습니다.", path);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await DecodeAsync(stream, path, options, cancellationToken).ConfigureAwait(false);
    }

    private static RgbaPixel ConvertPixel(Rgba32 sourcePixel, ImageDecodeOptions options)
    {
        var color = new RgbColor(sourcePixel.R, sourcePixel.G, sourcePixel.B);
        var alpha = sourcePixel.A <= options.TransparentThreshold ? (byte)0 : sourcePixel.A;

        return options.AlphaPolicy switch
        {
            AlphaPolicy.Preserve => new RgbaPixel(color, alpha),
            AlphaPolicy.CompositeOnBackground => RgbaPixel.Opaque(ColorMath.CompositeOver(color, alpha, options.BackgroundColor)),
            AlphaPolicy.Reject when alpha != byte.MaxValue
                => throw new ImageDecodeException("투명 픽셀이 포함되어 있어 현재 알파 정책에서 거부되었습니다."),
            AlphaPolicy.Reject => RgbaPixel.Opaque(color),
            _ => throw new InvalidOperationException("지원하지 않는 알파 정책입니다.")
        };
    }
}
