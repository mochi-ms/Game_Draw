using GameDraw.Core.Geometry;
using GameDraw.Profiles;

namespace GameDraw.Integration.Tests;

public sealed class ProfileContractsTests
{
    [Fact]
    public async Task JsonStoreRoundTripsUpdatesAndDeletesProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gamedraw-profile-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "profiles.json");
        try
        {
            using var store = new JsonGameProfileStore(path);
            var profile = GameProfile.CreateDefault("테스트", "Podiums");

            await store.SaveAsync(profile);
            var loaded = Assert.Single(await store.LoadAsync());
            Assert.Equal(profile.Id, loaded.Id);
            Assert.Equal("테스트", loaded.Name);

            await store.SaveAsync(profile with { Name = "수정됨" });
            Assert.Equal("수정됨", Assert.Single(await store.LoadAsync()).Name);

            await store.DeleteAsync(profile.Id);
            Assert.Empty(await store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void DefaultProfileIsAValidDraftWithCalibrationWarning()
    {
        var result = GameProfile.CreateDefault("테스트 프로필", "테스트 게임").Validate();

        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void CalibratedProfileRequiresValidBoundsAndLogicalSize()
    {
        var profile = GameProfile.CreateDefault() with
        {
            Canvas = new CanvasProfile
            {
                IsCalibrated = true,
                Bounds = new NormalizedRect(0.8, 0.8, 0.4, 0.4),
                LogicalWidth = 0,
                LogicalHeight = 0
            }
        };

        var result = profile.Validate();

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2);
    }

    [Fact]
    public void VisualVerificationProfileRejectsUnsafeThresholds()
    {
        var profile = GameProfile.CreateDefault() with
        {
            VisualVerification = new VisualVerificationProfile
            {
                MinimumConfidence = 1.5d,
                ConsecutiveFailuresBeforePause = 0
            }
        };

        var result = profile.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("confidence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("consecutive", StringComparison.OrdinalIgnoreCase));
    }
}
