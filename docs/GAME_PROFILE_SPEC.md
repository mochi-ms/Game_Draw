# Game Profile Specification

Profiles are declarative data, not game-specific coordinates hard-coded in source.

```text
windowMatcher
clientCoordinateSystem
canvas
visualAnchors
toolCapabilities
colorAdapter
brush
drawingModes
timing
verificationRules
schemaVersion
```

Canvas and tool positions are stored relative to the selected window client area. Physical screen coordinates are derived only at execution time using the current window bounds and DPI. Adapter-specific values live in the profile's `adapterSettings` string dictionary; each adapter owns its versioned codec and validation.

The first profile will target the Podiums whiteboard with a calibrated canvas, pencil, brush size, and HEX color input. Visual recognition and manual calibration are both supported; low-confidence recognition must pause rather than guess.
