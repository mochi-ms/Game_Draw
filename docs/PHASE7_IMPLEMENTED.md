# Phase 7 implementation

Phase 7 adds a deterministic visual-safety layer between window capture and
native drawing input. Recognition can now confirm that the Podiums canvas and
tool anchors are still where the profile expects them before input continues.

## Capture and frame conversion

- `WindowsWindowCapture` captures the current client area with `BitBlt` and
  materializes a BGRA `CapturedWindowFrame`.
- `CapturedWindowFrameExtensions.ToImageFrame` converts the native buffer into
  the shared Core `ImageFrame` model. Zero alpha from common 32-bit DIBs is
  treated as opaque because it represents an unspecified screen alpha.
- Native capture is guarded by `OperatingSystem.IsWindows`; all recognition
  tests use synthetic frames and never call user32/gdi32.

## Template and anchor matching

- `TemplateMatcher` performs normalized RGB error matching with transparent
  template pixels ignored, configurable search step, search region, sample
  cap, and minimum confidence.
- `AnchorMatcher` applies per-anchor thresholds and reports missing anchors
  instead of returning a guessed coordinate.
- `PodiumsVisualDetector` detects the largest bright, neutral, rectangular
  connected component as the whiteboard canvas and matches required/optional
  tool anchors supplied by calibration or a template pack.

## Safety and drift handling

- `VisualVerificationProfile` stores confidence and drift thresholds in the
  generic profile, including an enable switch and consecutive-failure pause
  count.
- `VisualDriftMonitor` compares normalized canvas bounds and anchor centers,
  compensating for frame size changes while measuring movement in pixels.
- `PodiumsVisualSafetyCoordinator` converts missing/low-confidence detections
  into repeated visual failures. Once the threshold is reached it calls
  `IVisualPauseController`, causing `WindowsDrawingExecutor` to pause before
  sending more input and marking the observation for recalibration.
- A recovery path is explicit: reset the coordinator after a fresh manual or
  visual calibration, then clear the visual pause before resuming.

## Verification

Core tests cover exact template hits, confidence failures, consecutive drift,
and pause/reset behavior. Integration tests cover synthetic Podiums canvas and
anchor detection, canvas movement, BGRA conversion, and executor visual pause.
The complete solution builds with zero warnings and all tests pass.
