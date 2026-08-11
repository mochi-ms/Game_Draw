# Podiums core drawing fix

This revision replaces the fragile corner-point calibration and manual 15-second window switch.

## Root causes fixed

- The visual preflight rejected execution when its white-canvas detection differed from saved calibration.
- A failed attempt left the workspace in a non-retryable state.
- The floating GameDraw window could cover Podiums tool and HEX controls, intercepting automation clicks.
- A minimized Roblox window produced off-screen calibration geometry.
- The execution status was duplicated by a large in-app overlay and a DPI-clipped popup.

## New workflow

1. Pick an aspect-ratio preset and drag the exact canvas rectangle directly over Roblox.
2. Reuse saved tool coordinates, or capture only the six tool controls on first setup.
3. Start drawing; GameDraw hides, restores Roblox if minimized, and activates it automatically.
4. Use the visually detected canvas only when it agrees with the dragged rectangle. Otherwise keep the user-selected rectangle.
5. Retry immediately after an execution error. `F7` pauses/releases input and `F8` stops/releases input.

The external status card is read-only so the user does not need to click it while Roblox owns the foreground.
