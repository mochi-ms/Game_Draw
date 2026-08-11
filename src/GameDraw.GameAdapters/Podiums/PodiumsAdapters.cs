using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Models;
using GameDraw.Core.Targeting;
using GameDraw.Profiles;
using GameDraw.GameAdapters.Podiums.Vision;

namespace GameDraw.GameAdapters.Podiums;

public sealed class PodiumsColorAdapter : IColorAdapter
{
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
            await input.ClickAsync(context.Map(layout.HexInput), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await input.KeyDownAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            controlHeld = true;
            await input.KeyDownAsync(InputKey.A, cancellationToken).ConfigureAwait(false);
            aHeld = true;
            await input.KeyUpAsync(InputKey.A, cancellationToken).ConfigureAwait(false);
            aHeld = false;
            await input.KeyUpAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            controlHeld = false;
            await input.TypeTextAsync(color.ToHex(), cancellationToken).ConfigureAwait(false);
            await input.KeyDownAsync(InputKey.Enter, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.Enter, cancellationToken).ConfigureAwait(false);

            return new(true, $"Selected {color.ToHex()} in Podiums.");
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
                await TryReleaseAllKeysAsync(safety).ConfigureAwait(false);
            }
        }
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
            await context.Input.ClickAsync(context.Map(point), cancellationToken: cancellationToken).ConfigureAwait(false);
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
            var sliderPoint = new GameDraw.Core.Geometry.NormalizedPoint(
                layout.BrushSizeMinimum.X + ((layout.BrushSizeMaximum.X - layout.BrushSizeMinimum.X) * amount),
                layout.BrushSizeMinimum.Y + ((layout.BrushSizeMaximum.Y - layout.BrushSizeMinimum.Y) * amount));
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
        DrawingMode.Hybrid
    };

    public string Id => "podiums.roblox";

    public string DisplayName => "Podiums (Roblox)";

    public GameAdapterCapabilities Capabilities =>
        GameAdapterCapabilities.CanvasCalibration |
        GameAdapterCapabilities.ColorSelection |
        GameAdapterCapabilities.BrushSelection |
        GameAdapterCapabilities.FillTool |
        GameAdapterCapabilities.VisualVerification;

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
    private readonly PodiumsToolAdapter _tools = new();

    public PodiumsExecutionHooks(GameProfile profile, IGameAdapterExecutionContext context)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async ValueTask BeforePlanAsync(
        GameDraw.Core.Drawing.DrawingPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var toolResult = await _tools.SelectToolAsync(
            PodiumsToolKind.Pencil,
            _profile,
            _context,
            cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(toolResult);

        var layout = PodiumsProfileSettings.ReadControlLayout(_profile);
        if (layout.HasBrushSizeControl)
        {
            var sizeResult = await _tools.SelectBrushSizeAsync(
                layout.DefaultBrushSizePixels,
                _profile,
                _context,
                cancellationToken).ConfigureAwait(false);
            EnsureSucceeded(sizeResult);
        }
    }

    public async ValueTask BeforeColorGroupAsync(
        GameDraw.Core.Colors.RgbColor color,
        int colorGroupIndex,
        CancellationToken cancellationToken = default)
    {
        var result = await _colors.SelectColorAsync(color, _profile, _context, cancellationToken)
            .ConfigureAwait(false);
        EnsureSucceeded(result);
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
