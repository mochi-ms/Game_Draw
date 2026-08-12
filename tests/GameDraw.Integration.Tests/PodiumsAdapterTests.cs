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
        Assert.Equal(12, profile.SupportedModes.Count);
        Assert.Contains(GameDraw.Core.Models.DrawingMode.CleanStroke, profile.SupportedModes);
        Assert.Contains(GameDraw.Core.Models.DrawingMode.ArtistStroke, profile.SupportedModes);
        Assert.Contains(GameDraw.Core.Models.DrawingMode.SafeStamp, profile.SupportedModes);
        Assert.Contains(GameDraw.Core.Models.DrawingMode.HalftoneStamp, profile.SupportedModes);
        Assert.Contains(GameDraw.Core.Models.DrawingMode.SmartFill, profile.SupportedModes);
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
    public void DragSelectedCanvasSkipsCornerStepsAndKeepsExactBounds()
    {
        var bounds = new NormalizedRect(0.22, 0.18, 0.42, 0.61);
        var session = new PodiumsCalibrationSession(new PodiumsCalibrationOptions
        {
            LogicalWidth = 640,
            LogicalHeight = 480,
            InitialCanvasBounds = bounds
        });

        Assert.Equal(PodiumsCalibrationStep.CapturePencilTool, session.State.Step);
        session.Capture(new NormalizedPoint(0.7, 0.1));
        session.Capture(new NormalizedPoint(0.8, 0.1));
        session.Capture(new NormalizedPoint(0.9, 0.1));
        session.Capture(new NormalizedPoint(0.8, 0.2));
        session.Capture(new NormalizedPoint(0.7, 0.2));
        session.Capture(new NormalizedPoint(0.8, 0.2));

        var result = session.Complete();

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(bounds.X, result.Canvas.Bounds.X, precision: 10);
        Assert.Equal(bounds.Y, result.Canvas.Bounds.Y, precision: 10);
        Assert.Equal(bounds.Width, result.Canvas.Bounds.Width, precision: 10);
        Assert.Equal(bounds.Height, result.Canvas.Bounds.Height, precision: 10);
        Assert.Equal(640, result.Canvas.LogicalWidth);
        Assert.Equal(480, result.Canvas.LogicalHeight);
        Assert.True(result.Controls.IsConfigured);
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
        Assert.Equal("#123456", input.ClipboardText);
        Assert.All(input.Clicks, point => Assert.Equal(new ScreenPoint(120, 140), point));
        Assert.Equal(2, input.Clicks.Count);
        Assert.Contains("key-down:Control", input.Events);
        Assert.Contains("key-up:A", input.Events);
        Assert.Contains("key-down:Delete", input.Events);
        Assert.Contains("key-down:Backspace", input.Events);
        Assert.Contains("#123456", input.TypedText);
        Assert.Contains("key-down:Enter", input.Events);
        Assert.True(input.Events.IndexOf("key-up:A") < input.Events.IndexOf("key-down:Delete"));
        Assert.True(input.Events.IndexOf("key-down:Delete") < input.Events.IndexOf("key-down:Enter"));
        Assert.True(input.ReleaseAllButtonsCalls >= 5);
        Assert.True(input.ReleaseAllKeysCalls > 0);
    }

    [Fact]
    public async Task HexAdapterStopsAfterTwoAttemptsWhenFieldReadbackDoesNotMatch()
    {
        var profile = CreateConfiguredProfile();
        var input = new RecordingInputController { IgnorePaste = true, IgnoreTextInput = true };
        var adapter = new PodiumsColorAdapter();

        var result = await adapter.SelectColorAsync(
            new RgbColor(0x12, 0x34, 0x56),
            profile,
            CreateContext(input));

        Assert.False(result.Succeeded);
        Assert.Equal(8, input.Clicks.Count);
        Assert.Contains("key-down:C", input.Events);
        Assert.DoesNotContain("key-down:Escape", input.Events);
        Assert.True(input.ReleaseAllButtonsCalls > 0);
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
        Assert.Contains(new ScreenPoint(180, 127), input.Clicks);
        Assert.Empty(input.TypedText);
    }

    [Fact]
    public async Task ExecutionHooksKeepCurrentToolAndBrushThenChangeOnlyRequestedColor()
    {
        var profile = CreateConfiguredProfile();
        var input = new RecordingInputController();
        var hooks = new PodiumsExecutionHooks(profile, CreateContext(input));
        var plan = new GameDraw.Core.Drawing.DrawingPlan(
            GameDraw.Core.Models.DrawingMode.Pixel,
            new PixelSize(1, 1),
            new[]
            {
                new GameDraw.Core.Drawing.DrawingColorGroup(
                    new RgbColor(0xAA, 0xBB, 0xCC),
                    new[] { new GameDraw.Core.Drawing.DrawingStroke(new[] { new NormalizedPoint(0.5, 0.5) }) })
            });

        await hooks.BeforePlanAsync(plan);
        await hooks.BeforeColorGroupAsync(new RgbColor(0xAA, 0xBB, 0xCC), 0);

        Assert.DoesNotContain(new ScreenPoint(170, 110), input.Clicks);
        Assert.DoesNotContain(input.Clicks, point => point.X == 180 && point.Y is >= 120 and <= 180);
        Assert.Contains("reset-pointer-capture:123", input.Events);
        Assert.Contains("move:120,140", input.Events);
        Assert.True(input.Events.IndexOf("reset-pointer-capture:123") < input.Events.IndexOf("move:120,140"));
        Assert.True(input.Events.IndexOf("move:120,140") < input.Events.IndexOf("click:120,140"));
        Assert.Equal("#AABBCC", input.ClipboardText);
    }

    [Fact]
    public async Task SafeColorTransitionUsesNativeCaptureResetBeforeHexTravel()
    {
        var profile = CreateConfiguredProfile();
        var input = new RecordingInputController();
        var hooks = new PodiumsExecutionHooks(profile, CreateContext(input));
        var plan = new GameDraw.Core.Drawing.DrawingPlan(
            GameDraw.Core.Models.DrawingMode.SafeStamp,
            new PixelSize(2, 1),
            new[]
            {
                new GameDraw.Core.Drawing.DrawingColorGroup(
                    RgbColor.Black,
                    new[] { new GameDraw.Core.Drawing.DrawingStroke(new[] { new NormalizedPoint(0.25, 0.5) }) }),
                new GameDraw.Core.Drawing.DrawingColorGroup(
                    RgbColor.White,
                    new[] { new GameDraw.Core.Drawing.DrawingStroke(new[] { new NormalizedPoint(0.75, 0.5) }) })
            });

        await hooks.BeforePlanAsync(plan);
        await hooks.BeforeColorGroupAsync(RgbColor.Black, 0);
        var firstColorEnd = input.Events.Count;
        await hooks.BeforeColorGroupAsync(RgbColor.White, 1);

        var transition = input.Events.Skip(firstColorEnd).ToArray();
        Assert.Contains("reset-pointer-capture:123", transition);
        Assert.Contains("move:120,140", transition);
        Assert.Contains("mouse-down:Left", transition);
        Assert.Contains("mouse-up:Left", transition);
        Assert.True(Array.IndexOf(transition, "mouse-down:Left") <
                    Array.IndexOf(transition, "mouse-up:Left"));
        Assert.True(Array.IndexOf(transition, "mouse-up:Left") <
                    Array.IndexOf(transition, "reset-pointer-capture:123"));
        Assert.True(Array.IndexOf(transition, "reset-pointer-capture:123") <
                    Array.IndexOf(transition, "move:120,140"));
        Assert.True(Array.IndexOf(transition, "move:120,140") <
                    Array.FindIndex(transition, item => item.StartsWith("click:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SmartFillHooksSelectPencilThenSwitchToPaintBucketOnlyForFillActions()
    {
        var profile = CreateConfiguredProfile();
        var input = new RecordingInputController();
        var hooks = new PodiumsExecutionHooks(profile, CreateContext(input));
        var outline = new GameDraw.Core.Drawing.DrawingStroke(new[]
        {
            new NormalizedPoint(0.2, 0.2),
            new NormalizedPoint(0.8, 0.2),
            new NormalizedPoint(0.8, 0.8)
        }, isClosed: true);
        var fill = new GameDraw.Core.Drawing.DrawingStroke(
            new[] { new NormalizedPoint(0.5, 0.5) },
            toolAction: GameDraw.Core.Drawing.DrawingToolAction.Fill);
        var plan = new GameDraw.Core.Drawing.DrawingPlan(
            GameDraw.Core.Models.DrawingMode.SmartFill,
            new PixelSize(10, 10),
            new[] { new GameDraw.Core.Drawing.DrawingColorGroup(RgbColor.Black, new[] { outline, fill }) });

        await hooks.BeforePlanAsync(plan);
        await hooks.BeforeStrokeAsync(outline, 0);
        await hooks.BeforeStrokeAsync(fill, 1);

        Assert.Equal(1, input.Clicks.Count(point => point == new ScreenPoint(170, 110)));
        Assert.Equal(1, input.Clicks.Count(point => point == new ScreenPoint(190, 110)));
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
            EraserTool = new NormalizedPoint(0.8, 0.1),
            FillTool = new NormalizedPoint(0.9, 0.1),
            BrushSizeMinimum = new NormalizedPoint(0.8, 0.8),
            BrushSizeMaximum = new NormalizedPoint(0.8, 0.2),
            HexInput = new NormalizedPoint(0.2, 0.4),
            MinimumBrushSizePixels = 1,
            MaximumBrushSizePixels = 50,
            DefaultBrushSizePixels = 10
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

    private sealed class RecordingInputController : IPointerCaptureResetController, IClipboardInputController
    {
        private bool _controlDown;
        private string? _focusedFieldText;

        public List<string> Events { get; } = new();

        public List<ScreenPoint> Clicks { get; } = new();

        public List<string> TypedText { get; } = new();

        public string? ClipboardText { get; private set; }

        public bool IgnorePaste { get; init; }

        public bool IgnoreTextInput { get; init; }

        public int ReleaseAllButtonsCalls { get; private set; }

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
            if (key == InputKey.Control)
            {
                _controlDown = true;
            }
            else if (_controlDown && key == InputKey.V && !IgnorePaste)
            {
                _focusedFieldText = ClipboardText;
            }
            else if (_controlDown && key == InputKey.C)
            {
                ClipboardText = _focusedFieldText;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default)
        {
            Events.Add($"key-up:{key}");
            if (key == InputKey.Control)
            {
                _controlDown = false;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default)
        {
            TypedText.Add(text);
            if (!IgnoreTextInput)
            {
                _focusedFieldText = text;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask SetClipboardTextAsync(string text, CancellationToken cancellationToken = default)
        {
            ClipboardText = text;
            Events.Add($"clipboard:{text}");
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> GetClipboardTextAsync(CancellationToken cancellationToken = default)
        {
            Events.Add($"clipboard-read:{ClipboardText}");
            return ValueTask.FromResult(ClipboardText);
        }

        public ValueTask ReleaseAllButtonsAsync(CancellationToken cancellationToken = default)
        {
            ReleaseAllButtonsCalls++;
            Events.Add("release-all-buttons");
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAllKeysAsync(CancellationToken cancellationToken = default)
        {
            ReleaseAllKeysCalls++;
            Events.Add("release-all-keys");
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetPointerCaptureAsync(
            long targetWindowHandle,
            CancellationToken cancellationToken = default)
        {
            Events.Add($"reset-pointer-capture:{targetWindowHandle}");
            return ValueTask.CompletedTask;
        }
    }
}
