using System.Text.Json;
using GameDraw.Core.Profiles;

namespace GameDraw_App.Services;

public sealed class JsonProfileStore
{
    private readonly string _profileDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameDraw",
        "profiles");

    public JsonProfileStore()
    {
        Directory.CreateDirectory(_profileDirectory);
    }

    public string DirectoryPath => _profileDirectory;

    public async Task<IReadOnlyList<GameProfile>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<GameProfile>();
        foreach (var path in Directory.EnumerateFiles(_profileDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                profiles.Add(ProfileSerializer.Deserialize(json));
            }
            catch (JsonException)
            {
                // Invalid user profiles are ignored during startup and can be repaired through import/export.
            }
            catch (InvalidOperationException)
            {
                // Same behavior for schema validation failures.
            }
        }

        return profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var updated = profile with { UpdatedAt = DateTimeOffset.UtcNow };
        var json = ProfileSerializer.Serialize(updated);
        var path = GetPath(updated.Id);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, true);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task ExportAsync(GameProfile profile, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await File.WriteAllTextAsync(path, ProfileSerializer.Serialize(profile), cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameProfile> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var profile = ProfileSerializer.Deserialize(json);
        await SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        return profile;
    }

    private string GetPath(Guid id) => Path.Combine(_profileDirectory, $"{id:N}.json");
}
