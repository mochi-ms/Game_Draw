# Phase 7.5 implemented

Phase 7.5 turns the phase 1–7 libraries into one runnable Podiums workflow.

## Integrated workflow

1. Select an image by picker or drag and drop.
2. Open Roblox Podiums and start profile calibration.
3. Hover each requested canvas/control position and press the global `F6` hotkey.
4. Analyze the image with a selectable drawing mode, logical resolution, and 2–256 color budget.
5. Start drawing, switch to Roblox within 15 seconds, and monitor progress in the always-on-top status window.
6. Use global `F7` to pause/resume and `F8` to stop and release input immediately.

## Correctness and safety changes

- Profiles are atomically persisted as versioned JSON under Local AppData.
- Podiums uses pencil, eraser, fill, vertical brush-size slider endpoints, and HEX input committed with Enter.
- The generic executor invokes target-specific hooks before the plan and each color group.
- Pause and visual pause release held mouse/keyboard state immediately and no longer overwrite one another.
- Resume safely restores the mouse-down state only when the target remains foreground.
- Target geometry and foreground state are refreshed between stroke points.
- A visual preflight compares the detected canvas with saved calibration before any input is sent.
- Live capture failures or repeated canvas drift request a safety pause.
- x86, x64, and ARM64 runtime assets are declared consistently for CLI and IDE builds.

## Verification

- Debug solution build: zero warnings and zero errors.
- 63 automated tests cover core, imaging, planning, adapters, persistence, execution hooks, foreground loss, emergency stop, and mid-stroke visual pause.
- The x64 packaged WinUI app was launched and visually checked at expanded and compact widths.

Actual drawing input is intentionally not triggered by the automated test suite. A calibrated live Podiums smoke test remains the final manual acceptance check before Phase 8.
