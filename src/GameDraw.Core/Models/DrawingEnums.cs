namespace GameDraw.Core.Models;

public enum DrawingMode
{
    Scanline,
    Pixel,
    LineArt
}

public enum BackgroundMode
{
    None,
    IgnoreWhite,
    IgnoreTransparent,
    IgnoreCustomColor
}

public enum FitMode
{
    Contain,
    Stretch
}

public enum ColorAdapterKind
{
    Manual,
    HexInput,
    FixedPalette,
    HsvPicker
}

public enum BrushStrategy
{
    Manual,
    Buttons,
    Slider
}

public enum DrawingExecutionState
{
    Idle,
    Preparing,
    Countdown,
    Drawing,
    Paused,
    Stopping,
    Completed,
    Error
}
