using GameDraw.Core.Geometry;
using GameDraw.Profiles;

namespace GameDraw.GameAdapters.Podiums.Calibration;

public enum PodiumsCalibrationStep
{
    CaptureCanvasTopLeft = 0,
    CaptureCanvasBottomRight = 1,
    CapturePencilTool = 2,
    CaptureEraserTool = 3,
    CaptureFillTool = 4,
    CaptureBrushSizeMinimum = 5,
    CaptureBrushSizeMaximum = 6,
    CaptureHexInput = 7,
    Completed = 8,
    Cancelled = 9
}

public sealed record PodiumsCalibrationOptions
{
    /// <summary>
    /// Logical image dimensions are supplied independently from the physical
    /// window size. They are not restricted to 32x32 or 48x48.
    /// </summary>
    public int LogicalWidth { get; init; } = 512;

    public int LogicalHeight { get; init; } = 512;

    public bool RequireControls { get; init; } = true;

    public bool IncludeFillTool { get; init; } = true;

    public bool IncludeBrushSize { get; init; } = true;

    public bool IncludeColorControls { get; init; } = true;

    public void Validate()
    {
        if (LogicalWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LogicalWidth));
        }

        if (LogicalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LogicalHeight));
        }
    }
}

public sealed record PodiumsCalibrationState(
    PodiumsCalibrationStep Step,
    NormalizedPoint? CanvasTopLeft,
    NormalizedPoint? CanvasBottomRight,
    PodiumsControlLayout Controls,
    string Message);

public sealed record PodiumsCalibrationResult(
    bool Succeeded,
    CanvasProfile Canvas,
    PodiumsControlLayout Controls,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage = null);

/// <summary>
/// Pure calibration state machine. The UI only needs to display State.Message,
/// capture a normalized client point, and persist the result when Complete()
/// succeeds. No mouse or window API is called here.
/// </summary>
public sealed class PodiumsCalibrationSession
{
    private readonly PodiumsCalibrationOptions _options;
    private PodiumsControlLayout _controls = PodiumsControlLayout.Unconfigured;

    public PodiumsCalibrationSession(PodiumsCalibrationOptions? options = null)
    {
        _options = options ?? new PodiumsCalibrationOptions();
        _options.Validate();
        State = new(
            PodiumsCalibrationStep.CaptureCanvasTopLeft,
            null,
            null,
            _controls,
            "Capture the top-left corner of the Podiums canvas.");
    }

    public PodiumsCalibrationState State { get; private set; }

    public PodiumsCalibrationState Capture(NormalizedPoint point)
    {
        if (State.Step is PodiumsCalibrationStep.Completed or PodiumsCalibrationStep.Cancelled)
        {
            return State;
        }

        if (!point.IsWithinUnitSquare)
        {
            return State with { Message = "The calibration point must be inside the target client." };
        }

        switch (State.Step)
        {
            case PodiumsCalibrationStep.CaptureCanvasTopLeft:
                State = State with
                {
                    CanvasTopLeft = point,
                    Step = PodiumsCalibrationStep.CaptureCanvasBottomRight,
                    Message = "Capture the bottom-right corner of the Podiums canvas."
                };
                break;

            case PodiumsCalibrationStep.CaptureCanvasBottomRight:
                if (State.CanvasTopLeft is not { } topLeft ||
                    point.X <= topLeft.X ||
                    point.Y <= topLeft.Y)
                {
                    return State with
                    {
                        Message = "The bottom-right corner must be below and to the right of the top-left corner."
                    };
                }

                State = State with { CanvasBottomRight = point };
                AdvanceAfterCanvas();
                break;

            case PodiumsCalibrationStep.CapturePencilTool:
                _controls = _controls with { PencilTool = point };
                Advance(PodiumsCalibrationStep.CaptureEraserTool, "Capture the Podiums eraser tool.");
                break;

            case PodiumsCalibrationStep.CaptureEraserTool:
                _controls = _controls with { EraserTool = point };
                AdvanceAfterEraser();
                break;

            case PodiumsCalibrationStep.CaptureFillTool:
                _controls = _controls with { FillTool = point, HasFillTool = true };
                AdvanceAfterFill();
                break;

            case PodiumsCalibrationStep.CaptureBrushSizeMinimum:
                _controls = _controls with
                {
                    BrushSizeMinimum = point
                };
                Advance(PodiumsCalibrationStep.CaptureBrushSizeMaximum, "Capture the maximum end of the Podiums brush-size slider.");
                break;

            case PodiumsCalibrationStep.CaptureBrushSizeMaximum:
                _controls = _controls with
                {
                    BrushSizeMaximum = point,
                    HasBrushSizeControl = true
                };
                AdvanceAfterBrushSize();
                break;

            case PodiumsCalibrationStep.CaptureHexInput:
                _controls = _controls with { HexInput = point, HasColorControls = true };
                CompleteState();
                break;
        }

        return State;
    }

    public void Cancel()
    {
        if (State.Step is PodiumsCalibrationStep.Completed or PodiumsCalibrationStep.Cancelled)
        {
            return;
        }

        State = State with
        {
            Step = PodiumsCalibrationStep.Cancelled,
            Message = "Podiums calibration was cancelled."
        };
    }

    public PodiumsCalibrationResult Complete()
    {
        if (State.Step != PodiumsCalibrationStep.Completed)
        {
            return Failure($"Calibration is not complete. Current step: {State.Step}.");
        }

        if (State.CanvasTopLeft is not { } topLeft ||
            State.CanvasBottomRight is not { } bottomRight)
        {
            return Failure("Canvas corners were not captured.");
        }

        var bounds = new NormalizedRect(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
        var canvas = new CanvasProfile
        {
            IsCalibrated = true,
            Bounds = bounds,
            LogicalWidth = _options.LogicalWidth,
            LogicalHeight = _options.LogicalHeight
        };
        var warnings = new List<string>();
        if (!_options.RequireControls)
        {
            warnings.Add("Control calibration was skipped; configure Podiums tools before running.");
        }

        return new PodiumsCalibrationResult(true, canvas, _controls, warnings);
    }

    /// <summary>
    /// Creates a result for users who already know the canvas coordinates and
    /// want to skip the interactive wizard. Missing controls are a warning so
    /// a profile can still be saved and completed later.
    /// </summary>
    public static PodiumsCalibrationResult CreateManual(
        CanvasProfile canvas,
        PodiumsControlLayout? controls = null)
    {
        var warnings = new List<string>();
        if (!canvas.IsCalibrated ||
            !canvas.Bounds.IsWithinUnitSquare ||
            canvas.Bounds.Width <= 0 ||
            canvas.Bounds.Height <= 0 ||
            canvas.LogicalWidth <= 0 ||
            canvas.LogicalHeight <= 0)
        {
            return new PodiumsCalibrationResult(
                false,
                CanvasProfile.Uncalibrated,
                PodiumsControlLayout.Unconfigured,
                warnings,
                "Manual canvas calibration is invalid.");
        }

        controls ??= PodiumsControlLayout.Unconfigured;
        var controlErrors = controls.Validate();
        if (controlErrors.Count > 0)
        {
            return new PodiumsCalibrationResult(false, canvas, controls, warnings, string.Join(" ", controlErrors));
        }

        if (!controls.IsConfigured)
        {
            warnings.Add("Podiums controls are not configured; use the calibration wizard before running.");
        }

        return new PodiumsCalibrationResult(true, canvas, controls, warnings);
    }

    private void AdvanceAfterCanvas()
    {
        if (!_options.RequireControls)
        {
            CompleteState();
            return;
        }

        Advance(PodiumsCalibrationStep.CapturePencilTool, "Capture the Podiums pencil tool.");
    }

    private void AdvanceAfterEraser()
    {
        if (_options.IncludeFillTool)
        {
            Advance(PodiumsCalibrationStep.CaptureFillTool, "Capture the Podiums fill tool.");
            return;
        }

        AdvanceAfterFill();
    }

    private void AdvanceAfterFill()
    {
        if (_options.IncludeBrushSize)
        {
            Advance(PodiumsCalibrationStep.CaptureBrushSizeMinimum, "Capture the minimum end of the Podiums brush-size slider.");
            return;
        }

        AdvanceAfterBrushSize();
    }

    private void AdvanceAfterBrushSize()
    {
        if (_options.IncludeColorControls)
        {
            Advance(PodiumsCalibrationStep.CaptureHexInput, "Capture the Podiums HEX input field.");
            return;
        }

        CompleteState();
    }

    private void Advance(PodiumsCalibrationStep step, string message)
    {
        State = State with { Step = step, Controls = _controls, Message = message };
    }

    private void CompleteState()
    {
        _controls = _controls with { IsConfigured = _options.RequireControls };
        State = State with
        {
            Step = PodiumsCalibrationStep.Completed,
            Controls = _controls,
            Message = "Podiums calibration is complete."
        };
    }

    private PodiumsCalibrationResult Failure(string error)
        => new(
            false,
            CanvasProfile.Uncalibrated,
            _controls,
            Array.Empty<string>(),
            error);
}
