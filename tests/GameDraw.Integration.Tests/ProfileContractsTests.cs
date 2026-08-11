using GameDraw.Core.Geometry;
using GameDraw.Profiles;

namespace GameDraw.Integration.Tests;

public sealed class ProfileContractsTests
{
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
