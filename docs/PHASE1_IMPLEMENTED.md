# Phase 1 implementation

Phase 1 establishes the contracts that every later planner, game adapter, and Windows executor will use. It does not send mouse input or inspect a live game window yet.

## Delivered

- Immutable color and image primitives: `RgbColor`, `RgbaPixel`, `ImageFrame`.
- Normalized canvas geometry: `PixelSize`, `NormalizedPoint`, `NormalizedRect`, and `ScreenPoint`.
- Drawing strategies: automatic, pixel-dot, horizontal/vertical scanline, contour, fill, and hybrid.
- Ordered `DrawingPlan` groups with stroke statistics and normalized travel estimates.
- Execution state/progress/result contracts, cancellation through `CancellationToken`, pause/resume contract, and target verification result types.
- Input abstraction for mouse movement, button state, keyboard shortcuts, and text entry. The concrete Windows implementation remains a later phase.
- Versioned `GameProfile` draft model with window matching, canvas calibration, brush, timing, color adapter, and supported-mode metadata.
- Capability-based game adapter and color adapter contracts.
- Windows automation boundaries for window discovery, capture, input, and hotkeys.
- WinUI shell with image picker, drag-and-drop preview, profile/mode controls, state badge, and compact responsive layout.

## Validation

The solution restores, builds, and runs the contract tests. Core tests cover color parsing, immutable image updates, plan statistics, mode availability, and stroke validation. Integration tests cover profile validation and calibration requirements.

## Explicitly deferred

No live `SendInput`, target-window discovery, canvas recognition, color-picker automation, image decoding pipeline, or real drawing execution is included in this phase. Those concerns are intentionally isolated behind the contracts above and are scheduled in phases 2–5.
