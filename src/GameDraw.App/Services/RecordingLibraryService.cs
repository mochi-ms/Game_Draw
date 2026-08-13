using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GameDraw.Core.Geometry;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameDraw_App.Services;

public sealed record DrawingRecording(
    string Id,
    string Name,
    string VideoFileName,
    string ThumbnailFileName,
    DateTimeOffset CreatedAt,
    TimeSpan Duration,
    int Width,
    int Height,
    int FrameCount,
    bool Completed,
    string Mode,
    string SourceImageName);

public sealed class RecordingLibraryService : IDisposable
{
    private const int FramesPerSecond = 6;
    private readonly SemaphoreSlim _libraryGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private bool _disposed;

    public RecordingLibraryService(string? rootPath = null)
    {
        RootPath = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameDraw",
            "Recordings");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    private string LibraryPath => Path.Combine(RootPath, "library.json");

    public async Task<CanvasRecordingSession> StartRecordingAsync(
        ScreenRect canvasBounds,
        string sourceImageName,
        string mode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!canvasBounds.IsValid || canvasBounds.Width < 2 || canvasBounds.Height < 2)
        {
            throw new ArgumentException("녹화할 캔버스 영역이 올바르지 않습니다.", nameof(canvasBounds));
        }

        var id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var videoFileName = $"{id}.avi";
        var thumbnailFileName = $"{id}.jpg";
        var session = new CanvasRecordingSession(
            this,
            canvasBounds,
            id,
            Path.GetFileNameWithoutExtension(sourceImageName),
            mode,
            Path.Combine(RootPath, videoFileName),
            videoFileName,
            Path.Combine(RootPath, thumbnailFileName),
            thumbnailFileName,
            FramesPerSecond);
        await session.StartAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task<IReadOnlyList<DrawingRecording>> GetRecordingsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _libraryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadLibraryUnsafeAsync(cancellationToken).ConfigureAwait(false);
            // Persist the repaired index so a malformed/older library file can
            // never hide recordings whose AVI and thumbnail still exist.
            await WriteLibraryUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
            return items
                .Where(HasAnyMedia)
                .OrderByDescending(item => item.CreatedAt)
                .ToArray();
        }
        finally
        {
            _libraryGate.Release();
        }
    }

    public string GetVideoPath(DrawingRecording recording)
        => Path.Combine(RootPath, recording.VideoFileName);

    public string GetThumbnailPath(DrawingRecording recording)
        => Path.Combine(RootPath, recording.ThumbnailFileName);

    public async Task RenameAsync(
        string id,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalized = NormalizeName(newName);
        await _libraryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadLibraryUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var index = items.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (index < 0)
            {
                return;
            }

            items[index] = items[index] with { Name = normalized };
            await WriteLibraryUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _libraryGate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _libraryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadLibraryUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var item = items.FirstOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal));
            if (item is null)
            {
                return;
            }

            items.Remove(item);
            DeleteIfPresent(GetVideoPath(item));
            DeleteIfPresent(GetThumbnailPath(item));
            await WriteLibraryUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _libraryGate.Release();
        }
    }

    internal async Task AddAsync(DrawingRecording recording, CancellationToken cancellationToken)
    {
        await _libraryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadLibraryUnsafeAsync(cancellationToken).ConfigureAwait(false);
            items.RemoveAll(item => string.Equals(item.Id, recording.Id, StringComparison.Ordinal));
            items.Add(recording);
            await WriteLibraryUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _libraryGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libraryGate.Dispose();
    }

    private async Task<List<DrawingRecording>> ReadLibraryUnsafeAsync(CancellationToken cancellationToken)
    {
        var recordings = new List<DrawingRecording>();
        if (File.Exists(LibraryPath))
        {
            try
            {
                await using var stream = new FileStream(
                    LibraryPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var stored = await JsonSerializer.DeserializeAsync<List<DrawingRecording?>>(
                    stream,
                    _jsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (stored is not null)
                {
                    recordings.AddRange(stored.Where(IsValidMetadata).Select(item => item!));
                }
            }
            catch (JsonException)
            {
                // Keep the media files intact. They are rediscovered below.
            }
            catch (IOException)
            {
                // A temporary index read failure must not make the page crash.
            }
        }

        RecoverUnindexedMedia(recordings);
        return recordings;
    }

    private void RecoverUnindexedMedia(List<DrawingRecording> recordings)
    {
        var indexedVideoFiles = recordings
            .Select(item => item.VideoFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var videoPath in Directory.EnumerateFiles(RootPath, "*.avi", SearchOption.TopDirectoryOnly))
        {
            var videoFileName = Path.GetFileName(videoPath);
            if (indexedVideoFiles.Contains(videoFileName))
            {
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(videoPath);
            var thumbnailFileName = id + ".jpg";
            if (!File.Exists(Path.Combine(RootPath, thumbnailFileName)))
            {
                continue;
            }

            var createdAt = new DateTimeOffset(File.GetCreationTime(videoPath));
            recordings.Add(new DrawingRecording(
                id,
                $"복구된 그림 기록 · {createdAt:MM-dd HH-mm}",
                videoFileName,
                thumbnailFileName,
                createdAt,
                TimeSpan.Zero,
                0,
                0,
                0,
                false,
                "복구된 기록",
                string.Empty));
            indexedVideoFiles.Add(videoFileName);
        }

        var indexedThumbnailFiles = recordings
            .Select(item => item.ThumbnailFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var thumbnailPath in Directory.EnumerateFiles(RootPath, "*.jpg", SearchOption.TopDirectoryOnly))
        {
            var thumbnailFileName = Path.GetFileName(thumbnailPath);
            if (indexedThumbnailFiles.Contains(thumbnailFileName))
            {
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(thumbnailPath);
            var createdAt = new DateTimeOffset(File.GetCreationTime(thumbnailPath));
            recordings.Add(new DrawingRecording(
                id,
                $"복구된 완성 썸네일 · {createdAt:MM-dd HH-mm}",
                id + ".avi",
                thumbnailFileName,
                createdAt,
                TimeSpan.Zero,
                0,
                0,
                0,
                false,
                "썸네일 기록",
                string.Empty));
            indexedThumbnailFiles.Add(thumbnailFileName);
        }
    }

    private async Task WriteLibraryUnsafeAsync(
        IReadOnlyList<DrawingRecording> recordings,
        CancellationToken cancellationToken)
    {
        var temporaryPath = LibraryPath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, recordings, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, LibraryPath, true);
    }

    private bool HasAnyMedia(DrawingRecording recording)
        => File.Exists(GetVideoPath(recording)) || File.Exists(GetThumbnailPath(recording));

    private static bool IsValidMetadata(DrawingRecording? recording)
        => recording is not null
            && !string.IsNullOrWhiteSpace(recording.Id)
            && !string.IsNullOrWhiteSpace(recording.VideoFileName)
            && !string.IsNullOrWhiteSpace(recording.ThumbnailFileName)
            && string.Equals(Path.GetFileName(recording.VideoFileName), recording.VideoFileName, StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(recording.ThumbnailFileName), recording.ThumbnailFileName, StringComparison.Ordinal);

    private static string NormalizeName(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("영상 이름을 입력하세요.", nameof(value));
        }

        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}

public sealed class CanvasRecordingSession : IAsyncDisposable
{
    private readonly RecordingLibraryService _library;
    private readonly ScreenRect _bounds;
    private readonly string _id;
    private readonly string _sourceImageName;
    private readonly string _mode;
    private readonly string _videoPath;
    private readonly string _videoFileName;
    private readonly string _thumbnailPath;
    private readonly string _thumbnailFileName;
    private readonly int _framesPerSecond;
    private readonly CancellationTokenSource _captureCancellation = new();
    private readonly Stopwatch _stopwatch = new();
    private MjpegAviWriter? _writer;
    private Task? _captureTask;
    private byte[]? _lastFrame;
    private bool _stopped;
    private bool _saved;

    internal CanvasRecordingSession(
        RecordingLibraryService library,
        ScreenRect bounds,
        string id,
        string sourceImageName,
        string mode,
        string videoPath,
        string videoFileName,
        string thumbnailPath,
        string thumbnailFileName,
        int framesPerSecond)
    {
        _library = library;
        _bounds = bounds;
        _id = id;
        _sourceImageName = sourceImageName;
        _mode = mode;
        _videoPath = videoPath;
        _videoFileName = videoFileName;
        _thumbnailPath = thumbnailPath;
        _thumbnailFileName = thumbnailFileName;
        _framesPerSecond = framesPerSecond;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _writer = new MjpegAviWriter(_videoPath, _bounds.Width, _bounds.Height, _framesPerSecond);
            _stopwatch.Start();
            var firstFrame = await DesktopRegionCapture.CaptureJpegAsync(_bounds, cancellationToken).ConfigureAwait(false);
            _lastFrame = firstFrame;
            _writer.AddFrame(firstFrame);
            _captureTask = CaptureLoopAsync(_captureCancellation.Token);
        }
        catch
        {
            _writer?.Dispose();
            _writer = null;
            DeletePartialFiles();
            throw;
        }
    }

    public async Task<DrawingRecording> StopAsync(
        bool completed,
        CancellationToken cancellationToken = default)
    {
        if (_stopped)
        {
            throw new InvalidOperationException("이미 종료된 녹화입니다.");
        }

        _stopped = true;
        _captureCancellation.Cancel();
        if (_captureTask is not null)
        {
            try
            {
                await _captureTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // The final frame is captured only after the executor reports 100%,
        // making the library thumbnail the finished calibrated canvas.
        try
        {
            var finalFrame = await DesktopRegionCapture.CaptureJpegAsync(_bounds, cancellationToken)
                .ConfigureAwait(false);
            _lastFrame = finalFrame;
            _writer?.AddFrame(finalFrame);
        }
        catch when (_lastFrame is not null)
        {
            // Preserve the last valid frame if the game window disappeared
            // during an emergency stop.
        }

        _stopwatch.Stop();
        if (_lastFrame is null || _writer is null)
        {
            throw new InvalidOperationException("녹화 프레임을 만들지 못했습니다.");
        }

        await File.WriteAllBytesAsync(_thumbnailPath, _lastFrame, cancellationToken).ConfigureAwait(false);
        _writer.Complete();
        var recording = new DrawingRecording(
            _id,
            BuildDefaultName(_sourceImageName),
            _videoFileName,
            _thumbnailFileName,
            DateTimeOffset.Now,
            _stopwatch.Elapsed,
            _bounds.Width,
            _bounds.Height,
            _writer.FrameCount,
            completed,
            _mode,
            _sourceImageName);
        _writer.Dispose();
        _writer = null;
        await _library.AddAsync(recording, cancellationToken).ConfigureAwait(false);
        _saved = true;
        return recording;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_stopped)
            {
                try
                {
                    await StopAsync(false, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
        finally
        {
            _writer?.Dispose();
            _writer = null;
            if (!_saved)
            {
                DeletePartialFiles();
            }

            _captureCancellation.Dispose();
        }
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / _framesPerSecond));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var frame = await DesktopRegionCapture.CaptureJpegAsync(_bounds, cancellationToken).ConfigureAwait(false);
                _lastFrame = frame;
                _writer?.AddFrame(frame);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A transient desktop-frame failure should create a slightly
                // longer video frame, never slow or cancel the drawing.
            }
        }
    }

    private static string BuildDefaultName(string sourceImageName)
        => string.IsNullOrWhiteSpace(sourceImageName)
            ? $"그림 기록 {DateTime.Now:yyyy-MM-dd HH-mm-ss}"
            : $"{sourceImageName} · {DateTime.Now:MM-dd HH-mm}";

    private void DeletePartialFiles()
    {
        if (File.Exists(_videoPath))
        {
            File.Delete(_videoPath);
        }

        if (File.Exists(_thumbnailPath))
        {
            File.Delete(_thumbnailPath);
        }
    }
}

internal static class DesktopRegionCapture
{
    public static async Task<byte[]> CaptureJpegAsync(
        ScreenRect bounds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pixels = CaptureBgra(bounds);
        using var stream = new InMemoryRandomAccessStream();
        var properties = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(0.82f, PropertyType.Single)
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, properties);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)bounds.Width,
            (uint)bounds.Height,
            96d,
            96d,
            pixels);
        await encoder.FlushAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (stream.Size > int.MaxValue)
        {
            throw new InvalidOperationException("캡처 프레임이 너무 큽니다.");
        }

        var result = new byte[(int)stream.Size];
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(result);
        return result;
    }

    private static byte[] CaptureBgra(ScreenRect bounds)
    {
        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new InvalidOperationException("화면 캡처 DC를 열지 못했습니다.");
        }

        var memoryDc = CreateCompatibleDC(screenDc);
        nint bitmap = nint.Zero;
        nint previous = nint.Zero;
        try
        {
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = bounds.Width,
                    Height = -bounds.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };
            bitmap = CreateDIBSection(screenDc, ref info, 0, out var bits, nint.Zero, 0);
            if (bitmap == nint.Zero || bits == nint.Zero)
            {
                throw new InvalidOperationException("화면 캡처 버퍼를 만들지 못했습니다.");
            }

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(
                memoryDc,
                0,
                0,
                bounds.Width,
                bounds.Height,
                screenDc,
                bounds.X,
                bounds.Y,
                0x00CC0020 | 0x40000000))
            {
                throw new InvalidOperationException("지정한 캔버스 영역을 캡처하지 못했습니다.");
            }

            var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (previous != nint.Zero)
            {
                _ = SelectObject(memoryDc, previous);
            }

            if (bitmap != nint.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint bits,
        nint section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        nint destination,
        int x,
        int y,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        uint operation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }
}

internal sealed class MjpegAviWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _framesPerSecond;
    private readonly List<AviIndexEntry> _index = [];
    private readonly long _riffSizePosition;
    private readonly long _maxBytesPerSecondPosition;
    private readonly long _totalFramesPosition;
    private readonly long _mainSuggestedBufferPosition;
    private readonly long _streamLengthPosition;
    private readonly long _streamSuggestedBufferPosition;
    private readonly long _moviSizePosition;
    private readonly long _moviTypePosition;
    private int _maximumFrameSize;
    private bool _completed;

    public MjpegAviWriter(string path, int width, int height, int framesPerSecond)
    {
        _framesPerSecond = framesPerSecond;
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        _writer = new BinaryWriter(_stream, Encoding.ASCII, true);

        WriteFourCc("RIFF");
        _riffSizePosition = _stream.Position;
        _writer.Write(0u);
        WriteFourCc("AVI ");

        var headerList = BeginList("hdrl");
        WriteFourCc("avih");
        _writer.Write(56u);
        _writer.Write((uint)Math.Round(1_000_000d / framesPerSecond));
        _maxBytesPerSecondPosition = _stream.Position;
        _writer.Write(0u);
        _writer.Write(0u);
        _writer.Write(0x10u);
        _totalFramesPosition = _stream.Position;
        _writer.Write(0u);
        _writer.Write(0u);
        _writer.Write(1u);
        _mainSuggestedBufferPosition = _stream.Position;
        _writer.Write(0u);
        _writer.Write((uint)width);
        _writer.Write((uint)height);
        for (var index = 0; index < 4; index++)
        {
            _writer.Write(0u);
        }

        var streamList = BeginList("strl");
        WriteFourCc("strh");
        _writer.Write(56u);
        WriteFourCc("vids");
        WriteFourCc("MJPG");
        _writer.Write(0u);
        _writer.Write((ushort)0);
        _writer.Write((ushort)0);
        _writer.Write(0u);
        _writer.Write(1u);
        _writer.Write((uint)framesPerSecond);
        _writer.Write(0u);
        _streamLengthPosition = _stream.Position;
        _writer.Write(0u);
        _streamSuggestedBufferPosition = _stream.Position;
        _writer.Write(0u);
        _writer.Write(uint.MaxValue);
        _writer.Write(0u);
        _writer.Write((short)0);
        _writer.Write((short)0);
        _writer.Write((short)Math.Min(short.MaxValue, width));
        _writer.Write((short)Math.Min(short.MaxValue, height));

        WriteFourCc("strf");
        _writer.Write(40u);
        _writer.Write(40u);
        _writer.Write(width);
        _writer.Write(height);
        _writer.Write((ushort)1);
        _writer.Write((ushort)24);
        WriteFourCc("MJPG");
        _writer.Write(0u);
        _writer.Write(0);
        _writer.Write(0);
        _writer.Write(0u);
        _writer.Write(0u);
        EndList(streamList);
        EndList(headerList);

        _moviSizePosition = BeginList("movi");
        _moviTypePosition = _moviSizePosition + 4;
    }

    public int FrameCount => _index.Count;

    public void AddFrame(byte[] jpeg)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length == 0)
        {
            return;
        }

        var chunkPosition = _stream.Position;
        WriteFourCc("00dc");
        _writer.Write((uint)jpeg.Length);
        _writer.Write(jpeg);
        if ((jpeg.Length & 1) != 0)
        {
            _writer.Write((byte)0);
        }

        _maximumFrameSize = Math.Max(_maximumFrameSize, jpeg.Length);
        _index.Add(new AviIndexEntry((uint)(chunkPosition - _moviTypePosition), (uint)jpeg.Length));
    }

    public void Complete()
    {
        if (_completed)
        {
            return;
        }

        EndList(_moviSizePosition);
        WriteFourCc("idx1");
        _writer.Write((uint)(_index.Count * 16));
        foreach (var entry in _index)
        {
            WriteFourCc("00dc");
            _writer.Write(0x10u);
            _writer.Write(entry.Offset);
            _writer.Write(entry.Size);
        }

        var fileEnd = _stream.Position;
        WriteUInt32At(_riffSizePosition, checked((uint)(fileEnd - 8)));
        WriteUInt32At(_maxBytesPerSecondPosition, checked((uint)(_maximumFrameSize * _framesPerSecond)));
        WriteUInt32At(_totalFramesPosition, (uint)_index.Count);
        WriteUInt32At(_mainSuggestedBufferPosition, (uint)_maximumFrameSize);
        WriteUInt32At(_streamLengthPosition, (uint)_index.Count);
        WriteUInt32At(_streamSuggestedBufferPosition, (uint)_maximumFrameSize);
        _stream.Position = fileEnd;
        _writer.Flush();
        _stream.Flush(true);
        _completed = true;
    }

    public void Dispose()
    {
        if (!_completed)
        {
            Complete();
        }

        _writer.Dispose();
        _stream.Dispose();
    }

    private long BeginList(string type)
    {
        WriteFourCc("LIST");
        var sizePosition = _stream.Position;
        _writer.Write(0u);
        WriteFourCc(type);
        return sizePosition;
    }

    private void EndList(long sizePosition)
    {
        var end = _stream.Position;
        WriteUInt32At(sizePosition, checked((uint)(end - sizePosition - 4)));
        _stream.Position = end;
    }

    private void WriteUInt32At(long position, uint value)
    {
        var current = _stream.Position;
        _stream.Position = position;
        _writer.Write(value);
        _stream.Position = current;
    }

    private void WriteFourCc(string value)
    {
        if (value.Length != 4)
        {
            throw new ArgumentException("FourCC는 네 글자여야 합니다.", nameof(value));
        }

        _writer.Write(Encoding.ASCII.GetBytes(value));
    }

    private readonly record struct AviIndexEntry(uint Offset, uint Size);
}
