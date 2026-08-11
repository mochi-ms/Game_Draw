# Clean stroke and compact floating UI

## Clean pen stroke

`DrawingMode.CleanStroke` is intended for monochrome line art. It performs three steps:

1. Zhang-Suen thinning reduces thick raster edges to a one-pixel center line.
2. The skeleton graph is split only at endpoints and junctions, so each branch becomes one continuous mouse-down stroke.
3. Ramer-Douglas-Peucker simplification removes small pixel stair-steps while retaining meaningful corners.

This avoids the thousands of short horizontal marks produced when scanline mode is applied to detailed line art. Automatic mode maps black line art to CleanStroke; color images keep the existing automatic mode comparison.

## Floating workspace

Floating mode uses a dedicated compact layout instead of shrinking the desktop workspace. It provides three tabs:

- Image: file selection, render style, speed, drawing mode, color count, and analysis.
- Connection: canvas aspect preset, drag selection, and tool-coordinate reset.
- Run: plan summary, progress, start, and stop/reset.

The large preview remains in desktop mode. The compact header provides help, reset, and return-to-large-view actions.

## Reset semantics

Workspace reset requests an immediate executor stop, cancels analysis or calibration, releases input, clears the current image and progress, and returns to image selection. The saved Podiums canvas and tool calibration remain intact.
