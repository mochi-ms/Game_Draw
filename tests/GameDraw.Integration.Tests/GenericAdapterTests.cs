using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Targeting;
using GameDraw.GameAdapters;
using GameDraw.GameAdapters.Generic;
using GameDraw.Profiles;

namespace GameDraw.Integration.Tests;

public sealed class GenericAdapterTests
{
    [Fact]
    public void CatalogContainsPodiumsAndGenericHexWhiteboard()
    {
        var catalog = new GameAdapterCatalog();

        Assert.Equal(2, catalog.Adapters.Count);
        Assert.NotNull(catalog.Find("podiums.roblox"));
        Assert.NotNull(catalog.Find("generic.hex-whiteboard"));
    }

    [Fact]
    public async Task GenericAdapterVerifiesPortableCalibratedProfile()
    {
        var adapter = new GenericHexWhiteboardAdapter();
        var profile = GenericHexProfileSettings.ApplyLayout(
            adapter.CreateDefaultProfile() with
            {
                Canvas = new CanvasProfile
                {
                    IsCalibrated = true,
                    Bounds = new NormalizedRect(0.1, 0.1, 0.8, 0.8),
                    LogicalWidth = 512,
                    LogicalHeight = 512
                }
            },
            new GenericHexControlLayout(
                new NormalizedPoint(0.8, 0.2),
                new NormalizedPoint(0.2, 0.8)));

        var result = await adapter.VerifyAsync(
            new TargetWindowSnapshot(42, "drawing-game", "Drawing Game", 1280, 720, 96, true),
            profile);

        Assert.True(result.IsSafeToRun, string.Join(" | ", result.Issues.Select(issue => issue.Message)));
        Assert.True(adapter.Capabilities.HasFlag(GameAdapterCapabilities.CustomWindowTarget));
        Assert.True(adapter.Capabilities.HasFlag(GameAdapterCapabilities.PortableProfiles));
    }

    [Fact]
    public async Task GenericHooksUseCalibratedPencilAndHexInput()
    {
        var profile = GenericHexProfileSettings.ApplyLayout(
            GenericHexProfileSettings.CreateDefaultProfile(),
            new GenericHexControlLayout(
                new NormalizedPoint(0.7, 0.1),
                new NormalizedPoint(0.2, 0.4)));
        var input = new RecordingInput();
        var context = new GameAdapterExecutionContext(
            input,
            new TargetWindowSnapshot(42, "drawing-game", "Drawing Game", 1000, 800, 96, true),
            point => new ScreenPoint((int)(point.X * 100), (int)(point.Y * 100)));
        var hooks = new GenericHexExecutionHooks(profile, context);
        var plan = GameDraw.Core.Drawing.DrawingPlan.Empty(
            GameDraw.Core.Models.DrawingMode.Pixel,
            new PixelSize(1, 1));

        await hooks.BeforePlanAsync(plan);
        await hooks.BeforeColorGroupAsync(new RgbColor(0x12, 0x34, 0x56), 0);

        Assert.Contains(new ScreenPoint(70, 10), input.Clicks);
        Assert.Contains(new ScreenPoint(20, 40), input.Clicks);
        Assert.Contains("#123456", input.Typed);
    }

    private sealed class RecordingInput : IInputSafetyController
    {
        public List<ScreenPoint> Clicks { get; } = new();
        public List<string> Typed { get; } = new();

        public ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask MouseDownAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask MouseUpAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ClickAsync(ScreenPoint point, InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
        {
            Clicks.Add(point);
            return ValueTask.CompletedTask;
        }
        public ValueTask KeyDownAsync(InputKey key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Typed.Add(text);
            return ValueTask.CompletedTask;
        }
        public ValueTask ReleaseAllButtonsAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ReleaseAllKeysAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
