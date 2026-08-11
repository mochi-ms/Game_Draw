# Phase 4 implementation

Phase 4 adds the safety boundary between a normalized `DrawingPlan` and real Windows input. The implementation is deliberately split so planning and tests never need to call `SendInput`.

## Target and coordinates

- `WindowsWindowLocator` enumerates visible windows and captures process, title, client size, DPI, and foreground state.
- `WindowsWindowGeometryProvider` refreshes the client rectangle and converts its top-left to physical screen coordinates with `ClientToScreen`.
- `TargetWindowBinding` refreshes geometry before every stroke, so moving/resizing a window or moving it to another monitor does not reuse stale screen coordinates.
- `ClientCoordinateMapper` maps normalized planner coordinates through a profile canvas rectangle and clamps to the current client bounds. Negative multi-monitor origins are retained.
- `WindowsTargetVerifier` rejects invalid handles, empty client areas, and unresolved automatic modes; foreground loss is surfaced as a warning and enforced by the executor when required.

## Input safety

- `WindowsInputController` uses `SendInput` for absolute virtual-desktop mouse movement, mouse buttons, virtual keys, and Unicode text.
- A configurable rate limiter defaults to 600 input events per second.
- Held mouse buttons and keys are tracked and can be released independently of the caller's sequence.
- `WindowsDrawingExecutor` releases buttons after every stroke, in its outer cleanup path, on emergency stop, and on disposal.

## Execution state

- `Pause()`/`Resume()` use an asynchronous gate between input operations.
- `RequestStop()` cancels the active execution, resumes a paused gate, and performs an immediate best-effort button/key release.
- Target geometry and foreground status are rechecked before every stroke.
- Execution reports preparing/running/paused-compatible progress, completion, stopping, and failure results through the existing Core contracts.

## Verification

Integration tests use a recording input stub and fake geometry provider. They cover DPI/multi-monitor coordinate remapping, successful execution, foreground blocking, emergency stop cleanup, pause/resume, and invalid target handles. No test invokes native input.
