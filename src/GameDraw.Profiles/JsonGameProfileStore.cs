using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameDraw.Profiles;

/// <summary>
/// Versioned, atomic JSON persistence for user-created game profiles.
/// </summary>
public sealed class JsonGameProfileStore : IGameProfileStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonGameProfileStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("프로필 저장 경로는 비워 둘 수 없습니다.", nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<GameProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(profile);
        var validation = profile.Validate();
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors), nameof(profile));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var index = profiles.FindIndex(item => item.Id == profile.Id);
            if (index >= 0)
            {
                profiles[index] = profile;
            }
            else
            {
                profiles.Add(profile);
            }

            await WriteCoreAsync(profiles, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .Where(profile => profile.Id != profileId)
                .ToList();
            await WriteCoreAsync(profiles, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private async Task<IReadOnlyList<GameProfile>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<GameProfile>();
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<ProfileStoreDocument>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        if (document is null || document.SchemaVersion != 1)
        {
            throw new InvalidDataException("지원하지 않는 GameDraw 프로필 저장소 형식입니다.");
        }

        return document.Profiles ?? Array.Empty<GameProfile>();
    }

    private async Task WriteCoreAsync(IReadOnlyList<GameProfile> profiles, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("프로필 저장 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new ProfileStoreDocument(1, profiles),
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record ProfileStoreDocument(
        int SchemaVersion,
        IReadOnlyList<GameProfile> Profiles);
}
