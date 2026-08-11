# Drawing Modes

Every mode outputs the same intermediate representation:

```text
Image → ProcessedImage → DrawingPlan → VerifiedExecution
```

## Pixel

Places one point per logical cell. The planner must account for brush diameter and the target canvas pitch so adjacent points do not unintentionally merge.

## Scanline

Groups contiguous pixels of the same mapped color into horizontal or vertical strokes. Direction can be selected automatically by estimated travel cost.

## Contour

Extracts edges, simplifies paths, removes tiny components, and orders paths for minimal travel.

## Fill

Uses a game fill tool when the profile exposes one. Otherwise it generates a bounded hatch or scanline fill that respects the calibrated brush.

## Hybrid

Draws structural contours first and fills regions second. It is the default candidate for illustrations.
