using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameDraw.Profiles;

/// <summary>
/// Portable, versioned import/export format used by the adapter SDK.
/// Imported profiles receive a fresh identity so they never overwrite an
/// existing local profile by accident.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "Instance service supports dependency injection and future transfer format providers.")]
public sealed class ProfileTransferService
{
    public const int CurrentTransferVersion = 1;
    public const string FormatName = "gamedraw-profile";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task ExportAsync(
        GameProfile profile,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidatePath(path);
        var validation = profile.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", validation.Errors));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("프로필 저장 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var document = new ProfileTransferDocument(CurrentTransferVersion, FormatName, profile);
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<GameProfile> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ValidatePath(path);
        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<ProfileTransferDocument>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("프로필 파일이 비어 있습니다.");
        if (!string.Equals(document.Format, FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("GameDraw 프로필 파일이 아닙니다.");
        }

        if (document.SchemaVersion is < 1 or > CurrentTransferVersion)
        {
            throw new InvalidDataException($"지원하지 않는 프로필 파일 버전입니다: {document.SchemaVersion}.");
        }

        var migrated = GameProfileMigration.ToCurrent(document.Profile) with { Id = Guid.NewGuid() };
        var validation = migrated.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", validation.Errors));
        }

        return migrated;
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("프로필 파일 경로는 비워 둘 수 없습니다.", nameof(path));
        }
    }

    private sealed record ProfileTransferDocument(
        int SchemaVersion,
        string Format,
        GameProfile Profile);
}

public static class GameProfileMigration
{
    public const int CurrentSchemaVersion = 1;

    public static GameProfile ToCurrent(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"더 새로운 프로필 스키마입니다: {profile.SchemaVersion}.");
        }

        return profile.SchemaVersion switch
        {
            0 => profile with { SchemaVersion = CurrentSchemaVersion },
            CurrentSchemaVersion => profile,
            _ => throw new InvalidDataException($"지원하지 않는 프로필 스키마입니다: {profile.SchemaVersion}.")
        };
    }
}
