# Architecture

```text
GameDraw.App
  ├─ GameDraw.Core
  ├─ GameDraw.Imaging
  ├─ GameDraw.Planning
  ├─ GameDraw.Profiles
  ├─ GameDraw.GameAdapters
  └─ GameDraw.Automation.Windows
```

`GameDraw.Core` owns immutable domain models and execution contracts. `GameDraw.Imaging` owns decoding, color management, resampling, quantization, and dithering. `GameDraw.Planning` converts processed images into mode-specific `DrawingPlan` objects. `GameDraw.Profiles` owns versioned JSON profiles. `GameDraw.GameAdapters` provides game-specific capabilities and calibration workflows. `GameDraw.Automation.Windows` owns window discovery, capture, DPI mapping, and `SendInput`. `GameDraw.App` owns WinUI presentation and composition.

The same `DrawingPlan` is used for preview, statistics, dry run, and real execution.
