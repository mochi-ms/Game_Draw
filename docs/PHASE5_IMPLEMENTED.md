# Phase 5 implementation

Phase 5 adds the first real game adapter without putting Podiums-specific
coordinates into the planner or Windows input layer.

## Podiums profile

- `PodiumsGameAdapter` exposes the `podiums.roblox` adapter id, all seven
  drawing modes, canvas calibration, exact color, brush, and fill capabilities.
- `PodiumsProfileSettings` stores a typed `PodiumsControlLayout` in the
  generic profile's `AdapterSettings` dictionary. Values are invariant-culture
  normalized coordinates, so a profile can be serialized or migrated without
  changing the core schema.
- The default profile matches the Roblox process (`RobloxPlayerBeta`) and a
  Roblox window title, selects the HEX color adapter, and leaves all physical
  controls uncalibrated until the user captures them.

## Calibration and fallback

- `PodiumsCalibrationSession` is a UI-agnostic state machine for canvas
  corners, pencil, brush, optional fill, brush-size, and HEX controls.
- Canvas calibration accepts any positive logical image size. It is not limited
  to 32x32 or 48x48, so the source image can keep its original dimensions.
- `PodiumsCalibrationSession.CreateManual` supports a known canvas rectangle
  without mouse capture and returns an explicit warning when controls still
  need calibration.
- All points are normalized to the target client. The runtime mapper supplies
  current physical screen coordinates after window movement, resize, or DPI
  changes.

## Input adapters and verification

- `PodiumsColorAdapter` focuses the HEX field, selects the existing text,
  types an exact `#RRGGBB` value, and clicks the apply control. Key cleanup is
  best-effort and uses `IInputSafetyController` when available.
- `PodiumsToolAdapter` selects pencil, brush, fill, and brush size using the
  same normalized layout.
- `PodiumsGameAdapter.VerifyAsync` checks the profile, window matcher, handle,
  client size, canvas calibration, color adapter, and control layout before any
  real input is allowed.
- `PodiumsAdapterCatalog` is the first catalog entry and is ready for app
  composition in the next UX phase.

## Verification

The integration suite records adapter input instead of calling native
`SendInput`. It covers full calibration progression, invalid points, manual
fallback warnings, exact HEX entry, tool selection, target mismatch, safe
verification, and catalog discovery. The complete solution builds with zero
warnings and all tests pass.
