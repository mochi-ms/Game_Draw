using GameDraw.Core.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameDraw_App.Services;

public sealed class PreviewRenderer : IDisposable
{
    private readonly string _previewDirectory = Path.Combine(Path.GetTempPath(), "GameDraw", "previews");
    private readonly List<string> _generatedFiles = new();

    public PreviewRenderer()
    {
        Directory.CreateDirectory(_previewDirectory);
    }

    public BitmapImage FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new BitmapImage(new Uri(Path.GetFullPath(path), UriKind.Absolute));
    }

    public async Task<BitmapImage> RenderAsync(ImageBuffer image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var path = Path.Combine(_previewDirectory, $"processed-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, image.ToPngBytes(), cancellationToken).ConfigureAwait(false);
        lock (_generatedFiles)
        {
            _generatedFiles.Add(path);
        }

        return FromFile(path);
    }

    public void Dispose()
    {
        lock (_generatedFiles)
        {
            foreach (var file in _generatedFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // The OS may still hold a preview bitmap. Temporary files are safe to leave.
                }
            }

            _generatedFiles.Clear();
        }
    }
}
