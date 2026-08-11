using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Targeting;
using GameDraw.GameAdapters;
using GameDraw.GameAdapters.Podiums;
using GameDraw.GameAdapters.Podiums.Calibration;
using GameDraw.Profiles;

namespace GameDraw.Integration.Tests;

public sealed class PodiumsAdapterTests
{
    [Fact]
    public void DefaultProfileUsesHexAdapterAndAllDrawingModes()
    {
        var adapter = new PodiumsGameAdapter();
        var profile = adapter.CreateDefaultProfile();

        Assert.Equal("podiums.roblox", adapter.Id);
        Assert.Equal(ColorAdapterKind.HexInput, profile.ColorAdapter.Kind);
        Assert.True(profile.ColorAdapter.SupportsExactColor);
        Assert.Equal(7, profile.SupportedModes.Count);
        Assert.False(PodiumsProfileSettings.ReadControlLayout(profile).IsConfigured);
        Assert.NotEmpty(profile.Validate().Warnings);
    }

    [Fact]
    public void CalibrationWizardCapturesCanvasAndAllPodiumsControls()
    {
        var session = new PodiumsCalibrationSession(new PodiumsCalibrationOptions
        {
            LogicalWidth = 127,
            LogicalHeight = 83
        });

        Assert.Equal(PodiumsCalibrationStep.CaptureCanvasTopLeft, session.State.Step);
        session.Capture(new NormalizedPoint(0.1, 0.2));
        session.Capture(new NormalizedPoint(0.9, 0.8));
        session.Capture(new NormalizedPoint(0.7, 0.1));
        session.Capture(new NormalizedPoint(0.8, 0.1));
        session.Capture(new NormalizedPoint(0.9, 0.1));
        session.Capture(new NormalizedPoint(0.8, 0.2));
        session.Capture(new NormalizedPoint(0.7, 0.2));
        session.Capture(new NormalizedPoint(0.8, 0.2));

        var result = session.Complete();

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(0.1, result.Canvas.Bounds.X, precision: 6);
        Assert.Equal(0.2, result.Canvas.Bounds.Y, precision: 6);
        Assert.Equal(0.8, result.Canvas.Bounds.Width, precision: 6);
        Assert.Equal(0.6, result.Canvas.Bounds.Height, precision: 6);
        Assert.Equal(127, result.Canvas.LogicalWidth);
        Assert.Equal(83, result.Canvas.LogicalHeight);
        Assert.True(result.Controls.IsConfigured);
        Assert.True(result.Controls.HasColorControls);
        Assert.True(result.Controls.HasFillTool);
        Assert.True(result.Controls.HasBrushSizeControl);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CalibrationWizardRejectsInvalidCanvasOrderAndOutOfBoundsPoint()
    {
        var session = new PodiumsCalibrationSession();

        session.Capture(new NormalizedPoint(-0.1, 0.2));
        Assert.Equal(PodiumsCalibrationStep.CaptureCanvasTopLeft, session.State.Step);

        session.Capture(new NormalizedPoint(0.5, 0.5));
        var state = session.Capture(new NormalizedPoint(0.4, 0.6));

        Assert.Equal(PodiumsCalibrationStep.CaptureCanvasBottomRight, state.Step);
        Assert.Contains("bottom-right", state.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.Complete().Succeeded);
    }

    [Fact]
    public void ManualCalibrationAllowsCanvasOnlyWithExplicitWarning()
    {
        var result = PodiumsCalibrationSession.CreateManual(new CanvasProfile
        {
            IsCalibrated = true,
            Bounds = new NormalizedRect(0.1, 0.1, 0.8, 0.8),
            LogicalWidth = 256,
            LogicalHeight = 128
        });

        Assert.True(result.Succeeded);
        Assert.False(result.Controls.IsConfigured);
        Assert.Contains(result.Warnings, warning => warning.Contains("controls", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HexAdapterSelectsExactColorThroughRecordedInput()
    {
        var profile = CreateConfiguredProfile();
        var input = new RecordingInputController();
        var context = CreateContext(input);
        var adapter = new PodiumsColorAdapter();

        var result = await adapter.SelectColorAsync(new RgbColor(0x12, 0x34, 0x56), profile, context);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("#123456", Assert.Single(input.TypedText));
        Assert.Equal(new ScreenPoint(120, 140), input.Clicks[0]);
        Assert.Equal(new ScreenPoint(130, 150), input.Clicks[1]);
        Assert.Contains("key-down:Control", input.Events);
        Assert.Contains("key-up:A", input.Events);
        Assert.True(input.ReleaseAllKeysCalls > 0);
    }

    [Fact]
    public async Task ToolAdapterSelectsPencilFillAndBrushSize()
    {
        var profile = CreateConfiguredProfile();
        var input = new RecordingInputController();
        var context = CreateContext(input);
        var adapter = new PodiumsToolAdapter();

        var pencil = await adapter.SelectToolAsync(PodiumsToolKind.Pencil, profile, context);
        var fill = await adapter.SelectToolAsync(PodiumsToolKind.Fill, profile, context);
        var size = await adapter.SelectBrushSizeAsync(7, profile, context);

        Assert.True(pencil.Succeeded, pencil.Message);
        Assert.True(fill.Succeeded, fill.Message);
        Assert.True(size.Succeeded, size.Message);
        Assert.Contains(new ScreenPoint(170, 110), input.Clicks);
        Assert.Contains(new ScreenPoint(190, 110), input.Clicks);
        Assert.Contains("7", input.TypedText);
        Assert.Contains("key-down:Enter", input.Events);
    }

    [Fact]
    public async Task PodiumsVerifierRequiresCalibrationAndRejectsMismatchedWindow()
    {
        var adapter = new PodiumsGameAdapter();
        var profile = adapter.CreateDefaultProfile();
        var uncalibrated = await adapter.VerifyAsync(CreateTarget(), profile);
        Assert.False(uncalibrated.IsSafeToRun);
        Assert.Contains(uncalibrated.Issues, issue => issue.Code == "PODIUMS_CANVAS_NOT_CALIBRATED");

        var configured = CreateConfiguredProfile();
        var mismatch = await adapter.VerifyAsync(
            CreateTarget() with { ProcessName = "not-roblox" },
            configured);
        Assert.False(mismatch.IsSafeToRun);
        Assert.Contains(mismatch.Issues, issue => issue.Code == "TARGET_WINDOW_MISMATCH");
    }

    [Fact]
    public async Task PodiumsVerifierAcceptsConfiguredForegroundTargetAndCatalogListsAdapter()
    {
        var adapter = new PodiumsGameAdapter();
        var result = await adapter.VerifyAsync(CreateTarget(), CreateConfiguredProfile());
        var catalog = new PodiumsAdapterCatalog();

        Assert.True(result.IsSafeToRun, string.Join(" | ", result.Issues.Select(issue => issue.Message)));
        Assert.Single(catalog.Adapters);
        Assert.Equal("podiums.roblox", catalog.Adapters[0].Id);
    }

    private static GameProfile CreateConfiguredProfile()
    {
        var adapter = new PodiumsGameAdapter();
        var controls = new PodiumsControlLayout
        {
            IsConfigured = true,
            HasColorControls = true,
            HasFillTool = true,
            HasBrushSizeControl = true,
            PencilTool = new NormalizedPoint(0.7, 0.1),
            BrushTool = new NormalizedPoint(0.8, 0.1),
            FillTool = new NormalizedPoint(0.9, 0.1),
            BrushSizeControl = new NormalizedPoint(0.8, 0.2),
            HexInput = new NormalizedPoint(0.2, 0.4),
            HexApply = new NormalizedPoint(0.3, 0.5),
            DefaultBrushSizePixels = 1
        };
        var profile = PodiumsProfileSettings.ApplyControlLayout(adapter.CreateDefaultProfile(), controls);
        return profile with
        {
            Canvas = new CanvasProfile
            {
                IsCalibrated = true,
                Bounds = new NormalizedRect(0.1, 0.1, 0.8, 0.8),
                LogicalWidth = 64,
                LogicalHeight = 64
            }
        };
    }

    private static TargetWindowSnapshot CreateTarget()
        => new(123, "RobloxPlayerBeta", "Roblox - Podiums", 1000, 800, 96, true);

    private static GameAdapterExecutionContext CreateContext(RecordingInputController input)
        => new(
            input,
            CreateTarget(),
            point => new ScreenPoint(
                100 + (int)Math.Round(point.X * 100, MidpointRounding.AwayFromZero),
                100 + (int)Math.Round(point.Y * 100, MidpointRounding.AwayFromZero)));

    private sealed class RecordingInputController : IInputSafetyController
    {
        public List<string> Events { get; } = new();

        public List<ScreenPoint> Clicks { get; } = new();

        public List<string> TypedText { get; } = new();

        public int ReleaseAllKeysCalls { get; private set; }

        public ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default)
        {
            Events.Add($"move:{point.X},{point.Y}");
            return ValueTask.CompletedTask;
        }

        public ValueTask MouseDownAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
        {
            Events.Add($"mouse-down:{button}");
            return ValueTask.CompletedTask;
        }

        public ValueTask MouseUpAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
        {
            Events.Add($"mouse-up:{button}");
            return ValueTask.CompletedTask;
        }

        public ValueTask ClickAsync(ScreenPoint point, InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
        {
            Clicks.Add(point);
            Events.Add($"click:{point.X},{point.Y}");
            return ValueTask.CompletedTask;
        }

        public ValueTask KeyDownAsync(InputKey key, CancellationToken cancellationToken = default)
        {
            Events.Add($"key-down:{key}");
            return ValueTask.CompletedTask;
        }

        public ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default)
        {
            Events.Add($"key-up:{key}");
            return ValueTask.CompletedTask;
        }

        public ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default)
        {
            TypedText.Add(text);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAllButtonsAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask ReleaseAllKeysAsync(CancellationToken cancellationToken = default)
        {
            ReleaseAllKeysCalls++;
            Events.Add("release-all-keys");
            return ValueTask.CompletedTask;
        }
    }
}
