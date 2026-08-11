using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameDraw.Core.Profiles;

public static class ProfileSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var validation = profile.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Profile validation failed: {string.Join("; ", validation.Errors)}");
        }

        return JsonSerializer.Serialize(profile, Options);
    }

    public static GameProfile Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var profile = JsonSerializer.Deserialize<GameProfile>(json, Options)
            ?? throw new JsonException("Profile JSON is empty.");
        var validation = profile.Validate();
        if (!validation.IsValid)
        {
            throw new JsonException($"Profile validation failed: {string.Join("; ", validation.Errors)}");
        }

        return profile;
    }

    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
