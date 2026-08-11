# Phase 3 implementation

Phase 3 converts a `QuantizedImage` into the same immutable `DrawingPlan` contract regardless of the selected drawing strategy.

## Supported modes

- **Pixel** — one point stroke per drawable logical pixel.
- **Horizontal scanline** — contiguous same-color runs become horizontal strokes, with optional serpentine direction.
- **Vertical scanline** — the same run merging along columns.
- **Contour** — cell boundary edges are chained into simplified closed loops.
- **Fill** — row-based bounded fill strokes with configurable row spacing.
- **Hybrid** — contour strokes followed by fill strokes for structural edges plus surface coverage.
- **Auto** — evaluates all six concrete modes and returns the lowest estimated movement-cost candidate.

## Ordering and estimation

Within each color group, strokes use deterministic nearest-neighbor ordering and reverse open strokes when that reduces pen-up travel. Optional color-group ordering is available for target profiles that prefer movement over palette order.

`PlanEstimate` reports stroke/point/color counts, pen-down and pen-up travel in logical pixels, total travel, and estimated duration using movement speed and inter-stroke/color-change delays.

Transparent source pixels are skipped by default, while profiles can explicitly include them. All generated coordinates are normalized to the logical image canvas, so the plan can later be mapped to any calibrated game window.

## Verification

Planning tests cover one-point-per-pixel output, horizontal/vertical run merging, rectangle contour simplification, fill/hybrid composition, automatic candidate ranking, transparent pixel filtering, and movement/delay estimation.

## Deferred

The planner does not yet know a target game's brush diameter, fill tool, or canvas calibration. Those constraints are applied by the adapter and Windows execution phases.
