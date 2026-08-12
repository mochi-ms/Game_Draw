using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Targeting;
using GameDraw.Profiles;
using GameDraw.GameAdapters.Podiums.Vision;

namespace GameDraw.GameAdapters.Podiums;

public sealed class PodiumsColorAdapter : IColorAdapter
{
    private static readonly TimeSpan DoubleClickInterval = TimeSpan.FromMilliseconds(28);
    private static readonly TimeSpan FocusSettleDelay = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan KeySettleDelay = TimeSpan.FromMilliseconds(14);
    private static readonly TimeSpan SelectionSettleDelay = TimeSpan.FromMilliseconds(32);
    private static readonly TimeSpan CommitSettleDelay = TimeSpan.FromMilliseconds(100);

    public ColorAdapterKind Kind => ColorAdapterKind.HexInput;

    public string DisplayName => "Podiums HEX color input";

    public async ValueTask<AdapterActionResult> SelectColorAsync(
        RgbColor color,
        GameProfile profile,
        IGameAdapterExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);

        var layout = PodiumsProfileSettings.ReadControlLayout(profile);
        if (!layout.IsConfigured || !layout.HasColorControls)
        {
            return new(false, "Podiums HEX controls are not calibrated.");
        }

        var layoutErrors = layout.Validate();
        if (layoutErrors.Count > 0)
        {
            return new(false, string.Join(" ", layoutErrors));
        }

        var input = context.Input;
        var controlHeld = false;
        var aHeld = false;
        try
        {
            if (input is IInputSafetyController safety)
            {
                // Roblox can retain a stale pen-down state even after the
                // physical button was released. Clear it before moving from
                // the canvas to the HEX field so that move cannot draw a
                // diagonal connector across the artwork.
                await safety.ReleaseAllKeysAsync(cancellationToken).ConfigureAwait(false);
                // Spread the releases over several rendered frames. Sending
                // the same events in one native batch is not sufficient when
                // Roblox retained its previous-frame pen latch.
                for (var confirmation = 0; confirmation < 4; confirmation++)
                {
                    await safety.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
                    if (confirmation < 3)
                    {
                        await Task.Delay(24, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await Task.Delay(45, cancellationToken).ConfigureAwait(false);

            var hex = color.ToHex();
            var calibratedPoint = context.Map(layout.HexInput);
            var verticalStep = Math.Clamp(
                (int)Math.Round(32d * Math.Max(1d, context.Target.Dpi / 96d)),
                28,
                52);
            var candidates = new[]
            {
                calibratedPoint,
                new ScreenPoint(calibratedPoint.X, calibratedPoint.Y - verticalStep),
                new ScreenPoint(calibratedPoint.X, calibratedPoint.Y - (verticalStep * 2)),
                new ScreenPoint(calibratedPoint.X, calibratedPoint.Y + verticalStep)
            }.Distinct().ToArray();
            foreach (var hexPoint in candidates)
            {
                // Podiums' TextBox needs the same real double-click gesture a
                // user performs. Keep the two clicks close enough that Roblox
                // recognizes them as a double click rather than two slow
                // independent clicks.
                await input.ClickAsync(hexPoint, cancellationToken: cancellationToken).ConfigureAwait(false);
                await Task.Delay(DoubleClickInterval, cancellationToken).ConfigureAwait(false);
                await input.ClickAsync(hexPoint, cancellationToken: cancellationToken).ConfigureAwait(false);
                await Task.Delay(FocusSettleDelay, cancellationToken).ConfigureAwait(false);

                // Make deletion explicit: select the entire current value,
                // clear it with both common editing keys, select once more to
                // defeat partial word selection, then type the complete HEX
                // value as real foreground keyboard input.
                await SelectAllAsync().ConfigureAwait(false);
                await PressAsync(InputKey.Backspace).ConfigureAwait(false);
                await PressAsync(InputKey.Delete).ConfigureAwait(false);
                await Task.Delay(SelectionSettleDelay, cancellationToken).ConfigureAwait(false);
                await SelectAllAsync().ConfigureAwait(false);
                await PressAsync(InputKey.Backspace).ConfigureAwait(false);
                await input.TypeTextAsync(hex, cancellationToken).ConfigureAwait(false);
                await Task.Delay(SelectionSettleDelay, cancellationToken).ConfigureAwait(false);
                if (input is IClipboardInputController clipboard)
                {
                    if (!await VerifyFieldTextAsync(clipboard, hex).ConfigureAwait(false))
                    {
                        // Some Roblox TextBox versions ignore Unicode packets
                        // but accept clipboard paste. Clear the field again and
                        // use paste as a verified fallback at the same point.
                        await SelectAllAsync().ConfigureAwait(false);
                        await PressAsync(InputKey.Backspace).ConfigureAwait(false);
                        await clipboard.SetClipboardTextAsync(hex, cancellationToken).ConfigureAwait(false);
                        await ChordAsync(InputKey.V).ConfigureAwait(false);
                        await Task.Delay(SelectionSettleDelay, cancellationToken).ConfigureAwait(false);
                        if (!await VerifyFieldTextAsync(clipboard, hex).ConfigureAwait(false))
                        {
                            // Never use Escape: Roblox handles it globally and
                            // opens the system menu over the canvas.
                            await SafetyReleaseBeforeRetryAsync().ConfigureAwait(false);
                            continue;
                        }
                    }
                }

                await PressAsync(InputKey.Enter).ConfigureAwait(false);
                await Task.Delay(CommitSettleDelay, cancellationToken).ConfigureAwait(false);
                return new(true, $"Selected and verified {hex} in Podiums.");
            }

            return new(false, $"Podiums HEX input did not contain {hex} at the calibrated field or nearby fallback positions.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, $"Podiums HEX color selection failed: {exception.Message}");
        }
        finally
        {
            if (aHeld)
            {
                await TryReleaseKeyAsync(input, InputKey.A).ConfigureAwait(false);
            }

            if (controlHeld)
            {
                await TryReleaseKeyAsync(input, InputKey.Control).ConfigureAwait(false);
            }

            if (input is IInputSafetyController safety)
            {
                await TryReleaseAllButtonsAsync(safety).ConfigureAwait(false);
                await TryReleaseAllKeysAsync(safety).ConfigureAwait(false);
            }
        }

        async ValueTask SelectAllAsync()
        {
            await input.KeyDownAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            controlHeld = true;
            await Task.Delay(KeySettleDelay, cancellationToken).ConfigureAwait(false);
            await input.KeyDownAsync(InputKey.A, cancellationToken).ConfigureAwait(false);
            aHeld = true;
            await Task.Delay(KeySettleDelay, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.A, cancellationToken).ConfigureAwait(false);
            aHeld = false;
            await Task.Delay(KeySettleDelay, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            controlHeld = false;
            await Task.Delay(SelectionSettleDelay, cancellationToken).ConfigureAwait(false);
        }

        async ValueTask SafetyReleaseBeforeRetryAsync()
        {
            if (input is IInputSafetyController safety)
            {
                await safety.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
                await safety.ReleaseAllKeysAsync(cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(55, cancellationToken).ConfigureAwait(false);
        }

        async ValueTask PressAsync(InputKey key)
        {
            await input.KeyDownAsync(key, cancellationToken).ConfigureAwait(false);
            await Task.Delay(KeySettleDelay, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(key, cancellationToken).ConfigureAwait(false);
        }

        async ValueTask ChordAsync(InputKey key)
        {
            await input.KeyDownAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            controlHeld = true;
            await Task.Delay(KeySettleDelay, cancellationToken).ConfigureAwait(false);
            await input.KeyDownAsync(key, cancellationToken).ConfigureAwait(false);
            await Task.Delay(KeySettleDelay, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(key, cancellationToken).ConfigureAwait(false);
            await Task.Delay(KeySettleDelay, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            controlHeld = false;
        }

        async ValueTask<bool> VerifyFieldTextAsync(
            IClipboardInputController clipboard,
            string expected)
        {
            await SelectAllAsync().ConfigureAwait(false);
            var sentinel = $"GameDraw-verify-{Guid.NewGuid():N}";
            await clipboard.SetClipboardTextAsync(sentinel, cancellationToken).ConfigureAwait(false);
            await ChordAsync(InputKey.C).ConfigureAwait(false);
            await Task.Delay(SelectionSettleDelay, cancellationToken).ConfigureAwait(false);
            var copied = await clipboard.GetClipboardTextAsync(cancellationToken).ConfigureAwait(false);
            return NormalizeHex(copied) == NormalizeHex(expected);
        }
    }

    private static string? NormalizeHex(string? value)
    {
        var normalized = value?.Trim();
        if (normalized?.StartsWith('#') == true)
        {
            normalized = normalized[1..];
        }

        return normalized?.ToUpperInvariant();
    }

    private static async ValueTask TryReleaseKeyAsync(IInputController input, InputKey key)
    {
        try
        {
            await input.KeyUpAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A safety release below is best effort and must not mask the
            // original adapter failure.
        }
    }

    private static async ValueTask TryReleaseAllKeysAsync(IInputSafetyController input)
    {
        try
        {
            await input.ReleaseAllKeysAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Do not mask the result of the color operation.
        }
    }

    private static async ValueTask TryReleaseAllButtonsAsync(IInputSafetyController input)
    {
        try
        {
            await input.ReleaseAllButtonsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Do not mask the result of the color operation.
        }
    }
}

public sealed class PodiumsToolAdapter
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance helper keeps the adapter API ready for future game-specific state.")]
    public async ValueTask<AdapterActionResult> SelectToolAsync(
        PodiumsToolKind tool,
        GameProfile profile,
        IGameAdapterExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);

        var layout = PodiumsProfileSettings.ReadControlLayout(profile);
        if (!layout.HasTool(tool))
        {
            return new(false, $"Podiums {tool} tool is not calibrated.");
        }

        var point = tool switch
        {
            PodiumsToolKind.Pencil => layout.PencilTool,
            PodiumsToolKind.Eraser => layout.EraserTool,
            PodiumsToolKind.Fill => layout.FillTool,
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };

        try
        {
            var screenPoint = context.Map(point);
            if (context.Input is IPointerCaptureResetController captureReset)
            {
                // Move to the tool while Roblox is unfocused. If its Lua-side
                // pencil latch survived the previous stroke, it never observes
                // this cross-canvas travel and therefore cannot draw a line.
                await captureReset.RepositionWithCaptureResetAsync(
                    context.Target.Handle,
                    screenPoint,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (context.Input is IInputSafetyController safety)
            {
                await safety.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
                await safety.ReleaseAllKeysAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }

            await context.Input.ClickAsync(screenPoint, cancellationToken: cancellationToken).ConfigureAwait(false);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            return new(true, $"Selected Podiums {tool} tool.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, $"Podiums tool selection failed: {exception.Message}");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance helper keeps the adapter API ready for future game-specific state.")]
    public async ValueTask<AdapterActionResult> SelectBrushSizeAsync(
        int sizePixels,
        GameProfile profile,
        IGameAdapterExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        var layout = PodiumsProfileSettings.ReadControlLayout(profile);
        if (!layout.IsConfigured || !layout.HasBrushSizeControl)
        {
            return new(false, "Podiums brush-size control is not calibrated.");
        }

        if (sizePixels < layout.MinimumBrushSizePixels || sizePixels > layout.MaximumBrushSizePixels)
        {
            return new(false, $"Podiums brush size must be between {layout.MinimumBrushSizePixels} and {layout.MaximumBrushSizePixels}.");
        }

        try
        {
            var span = Math.Max(1, layout.MaximumBrushSizePixels - layout.MinimumBrushSizePixels);
            var amount = (sizePixels - layout.MinimumBrushSizePixels) / (double)span;
            // Podiums uses a vertical size slider whose smallest brush is at
            // the top. Older calibration instructions allowed the two ends to
            // be stored in reverse, which made a request for size 1 click the
            // bottom and select the maximum (currently displayed as 60).
            // Resolve the visual low/high endpoints from their geometry so
            // both old and newly calibrated profiles remain safe.
            var first = layout.BrushSizeMinimum;
            var second = layout.BrushSizeMaximum;
            var vertical = Math.Abs(second.Y - first.Y) >= Math.Abs(second.X - first.X);
            var minimumPoint = vertical
                ? (first.Y <= second.Y ? first : second)
                : (first.X <= second.X ? first : second);
            var maximumPoint = minimumPoint == first ? second : first;
            var sliderPoint = new GameDraw.Core.Geometry.NormalizedPoint(
                minimumPoint.X + ((maximumPoint.X - minimumPoint.X) * amount),
                minimumPoint.Y + ((maximumPoint.Y - minimumPoint.Y) * amount));
            await context.Input.ClickAsync(context.Map(sliderPoint), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new(true, $"Selected Podiums brush size {sizePixels}px.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, $"Podiums brush-size selection failed: {exception.Message}");
        }
        finally
        {
            if (context.Input is IInputSafetyController safety)
            {
                try
                {
                    await safety.ReleaseAllKeysAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort safety cleanup.
                }
            }
        }
    }
}

public sealed class PodiumsGameAdapter : IGameAdapter
{
    private static readonly IReadOnlyList<DrawingMode> SupportedDrawingModes = new[]
    {
        DrawingMode.Auto,
        DrawingMode.Pixel,
        DrawingMode.HorizontalScanline,
        DrawingMode.VerticalScanline,
        DrawingMode.Contour,
        DrawingMode.Fill,
        DrawingMode.Hybrid,
        DrawingMode.CleanStroke,
        DrawingMode.ArtistStroke,
        DrawingMode.SafeStamp,
        DrawingMode.HalftoneStamp,
        DrawingMode.SmartFill
    };

    public string Id => "podiums.roblox";

    public string DisplayName => "Podiums (Roblox)";

    public GameAdapterCapabilities Capabilities =>
        GameAdapterCapabilities.CanvasCalibration |
        GameAdapterCapabilities.ColorSelection |
        GameAdapterCapabilities.BrushSelection |
        GameAdapterCapabilities.FillTool |
        GameAdapterCapabilities.VisualVerification |
        GameAdapterCapabilities.PortableProfiles;

    public IReadOnlyList<DrawingMode> SupportedModes => SupportedDrawingModes;

    public PodiumsVisualDetector VisualDetector { get; } = new();

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Factory remains on the adapter for discoverability in app composition.")]
    public PodiumsVisualSafetyCoordinator CreateVisualSafetyCoordinator(
        GameProfile profile,
        IVisualPauseController? pauseController = null)
        => PodiumsVisualSafetyCoordinator.ForProfile(profile, pauseController);

    public GameProfile CreateDefaultProfile()
        => PodiumsProfileSettings.CreateDefaultProfile();

    public ValueTask<TargetVerificationResult> VerifyAsync(
        TargetWindowSnapshot target,
        GameProfile profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        var issues = new List<TargetVerificationIssue>();
        var validation = profile.Validate();
        foreach (var error in validation.Errors)
        {
            issues.Add(new(TargetVerificationSeverity.Error, "PROFILE_INVALID", error));
        }

        foreach (var warning in validation.Warnings)
        {
            issues.Add(new(TargetVerificationSeverity.Warning, "PROFILE_WARNING", warning));
        }

        if (target.Handle == 0)
        {
            issues.Add(new(TargetVerificationSeverity.Error, "TARGET_HANDLE_INVALID", "Target window handle is invalid."));
        }

        if (target.ClientWidth <= 0 || target.ClientHeight <= 0)
        {
            issues.Add(new(TargetVerificationSeverity.Error, "TARGET_CLIENT_EMPTY", "Target client area is empty."));
        }

        var matcher = profile.Window;
        if (matcher is null || !matcher.IsConfigured)
        {
            issues.Add(new(
                TargetVerificationSeverity.Warning,
                "WINDOW_MATCHER_UNCONFIGURED",
                "Window matching is not configured; verify the target manually."));
        }
        else if (!Matches(matcher, target))
        {
            issues.Add(new(
                TargetVerificationSeverity.Error,
                "TARGET_WINDOW_MISMATCH",
                "The selected window does not match the Podiums profile."));
        }

        if (!profile.Canvas.IsCalibrated)
        {
            issues.Add(new(
                TargetVerificationSeverity.Error,
                "PODIUMS_CANVAS_NOT_CALIBRATED",
                "Calibrate the Podiums canvas before running."));
        }

        if (profile.ColorAdapter.Kind != ColorAdapterKind.HexInput)
        {
            issues.Add(new(
                TargetVerificationSeverity.Error,
                "PODIUMS_COLOR_ADAPTER_MISMATCH",
                "Podiums requires the HEX color adapter."));
        }

        var layout = PodiumsProfileSettings.ReadControlLayout(profile);
        foreach (var error in layout.Validate())
        {
            issues.Add(new(TargetVerificationSeverity.Error, "PODIUMS_CONTROLS_INVALID", error));
        }

        if (!layout.IsConfigured)
        {
            issues.Add(new(
                TargetVerificationSeverity.Warning,
                "PODIUMS_CONTROLS_NOT_CALIBRATED",
                "Podiums tool controls are not calibrated."));
        }
        else
        {
            if (!layout.HasColorControls)
            {
                issues.Add(new(
                    TargetVerificationSeverity.Warning,
                    "PODIUMS_COLOR_CONTROLS_NOT_CONFIGURED",
                    "HEX color controls are not configured."));
            }

            if (!layout.HasFillTool)
            {
                issues.Add(new(
                    TargetVerificationSeverity.Warning,
                    "PODIUMS_FILL_TOOL_NOT_CONFIGURED",
                    "Fill tool was not captured; fill mode will be unavailable."));
            }
        }

        if (!target.IsForeground)
        {
            issues.Add(new(
                TargetVerificationSeverity.Warning,
                "TARGET_NOT_FOREGROUND",
                "Target window is not currently foreground."));
        }

        var safe = issues.All(issue => issue.Severity != TargetVerificationSeverity.Error);
        return ValueTask.FromResult(new TargetVerificationResult(safe, issues));
    }

    private static bool Matches(WindowMatcher matcher, TargetWindowSnapshot target)
    {
        if (!string.IsNullOrWhiteSpace(matcher.ProcessName) &&
            !string.Equals(
                NormalizeProcessName(target.ProcessName),
                NormalizeProcessName(matcher.ProcessName),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(matcher.TitleContains) ||
            target.Title.Contains(matcher.TitleContains, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessName(string value)
        => Path.GetFileNameWithoutExtension(value.Trim());
}

/// <summary>
/// Connects the generic executor lifecycle to the controls exposed by the
/// calibrated Podiums profile.
/// </summary>
public sealed class PodiumsExecutionHooks : IDrawingExecutionHooks
{
    private readonly GameProfile _profile;
    private readonly IGameAdapterExecutionContext _context;
    private readonly PodiumsColorAdapter _colors = new();
    private readonly bool _selectColors;
    private PodiumsToolKind? _activeTool;
    private bool _resetPenLatchBetweenColors;
    private bool _manageDrawingTools;

    public PodiumsExecutionHooks(
        GameProfile profile,
        IGameAdapterExecutionContext context,
        bool selectColors = true)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _selectColors = selectColors;
    }

    public async ValueTask BeforePlanAsync(
        GameDraw.Core.Drawing.DrawingPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        // Every automated colour change leaves the canvas, regardless of the
        // drawing mode. Treat all of them as a strong pointer-capture boundary.
        _resetPenLatchBetweenColors = _selectColors;
        _manageDrawingTools = plan.Mode == DrawingMode.SmartFill;
        if (plan.Mode != DrawingMode.SmartFill)
        {
            // Existing modes keep the user's selected drawing tool and brush.
            return;
        }

        var layout = PodiumsProfileSettings.ReadControlLayout(_profile);
        if (plan.EnumerateStrokes().Any(item =>
                item.Stroke.ToolAction == GameDraw.Core.Drawing.DrawingToolAction.Fill) &&
            !layout.HasFillTool)
        {
            throw new InvalidOperationException("스마트 외곽선·채우기를 사용하려면 페인트 통 위치를 먼저 설정하세요.");
        }

        await SelectToolAsync(PodiumsToolKind.Pencil, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask BeforeColorGroupAsync(
        GameDraw.Core.Colors.RgbColor color,
        int colorGroupIndex,
        CancellationToken cancellationToken = default)
    {
        if (!_selectColors)
        {
            return;
        }

        if (_resetPenLatchBetweenColors &&
            _context.Input is IPointerCaptureResetController captureReset)
        {
            if (colorGroupIndex > 0 && _activeTool != PodiumsToolKind.Fill)
            {
                // Losing foreground focus does not necessarily release a
                // Roblox Lua-side pointer latch: a window holding capture can
                // still observe cursor travel. Complete one fresh stationary
                // down/up gesture at the finished endpoint first. It can only
                // repaint that endpoint, never create a connector.
                await ResetStationaryPenLatchAsync(cancellationToken).ConfigureAwait(false);
            }

            // Critical ordering: travel to HEX while GameDraw owns focus, then
            // restore Roblox at the stationary field coordinate. Resetting and
            // restoring first allowed the stale Lua pencil latch to observe the
            // later canvas-to-HEX movement and draw a line clipped at the canvas
            // edge.
            var hexPoint = PodiumsProfileSettings.ReadControlLayout(_profile).HexInput;
            await captureReset.RepositionWithCaptureResetAsync(
                _context.Target.Handle,
                _context.Map(hexPoint),
                cancellationToken).ConfigureAwait(false);
        }
        else if (colorGroupIndex > 0 && _resetPenLatchBetweenColors)
        {
            await ResetStationaryPenLatchAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = await _colors.SelectColorAsync(color, _profile, _context, cancellationToken)
            .ConfigureAwait(false);
        EnsureSucceeded(result);
    }

    public async ValueTask BeforeStrokeAsync(
        GameDraw.Core.Drawing.DrawingStroke stroke,
        int strokeIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (!_manageDrawingTools)
        {
            // Preserve the tool selected by the user for all normal modes.
            // Clicking Pencil here adds an unnecessary HEX -> tool -> canvas
            // trip and was another source of long accidental connectors.
            return;
        }

        var desired = stroke.ToolAction == GameDraw.Core.Drawing.DrawingToolAction.Fill
            ? PodiumsToolKind.Fill
            : PodiumsToolKind.Pencil;
        if (_activeTool == desired)
        {
            return;
        }

        await SelectToolAsync(desired, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SelectToolAsync(
        PodiumsToolKind tool,
        CancellationToken cancellationToken)
    {
        var result = await new PodiumsToolAdapter()
            .SelectToolAsync(tool, _profile, _context, cancellationToken)
            .ConfigureAwait(false);
        EnsureSucceeded(result);
        _activeTool = tool;
    }

    private async ValueTask ResetStationaryPenLatchAsync(CancellationToken cancellationToken)
    {
        // A complete stationary click at the already-finished point resets
        // Podiums' internal drag latch without creating a connector. It is
        // followed by a full released frame before any movement toward HEX.
        if (_context.Input is IInputSafetyController safety)
        {
            await safety.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Span a complete 30 Hz frame on both sides so the game must observe
        // this as a new stationary gesture, not a continuation of the stroke.
        await Task.Delay(34, cancellationToken).ConfigureAwait(false);
        await _context.Input.MouseDownAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
        await Task.Delay(34, cancellationToken).ConfigureAwait(false);
        await _context.Input.MouseUpAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
        await Task.Delay(34, cancellationToken).ConfigureAwait(false);
        if (_context.Input is IInputSafetyController finalSafety)
        {
            await finalSafety.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(18, cancellationToken).ConfigureAwait(false);
            await finalSafety.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void EnsureSucceeded(AdapterActionResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Message ?? "Podiums control action failed.");
        }
    }
}

public sealed class PodiumsAdapterCatalog : IGameAdapterCatalog
{
    public PodiumsAdapterCatalog()
    {
        Adapters = new IGameAdapter[] { new PodiumsGameAdapter() };
    }

    public IReadOnlyList<IGameAdapter> Adapters { get; }
}
