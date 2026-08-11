# Product Specification

## Product goal

GameDraw converts an input image into a verifiable `DrawingPlan` and executes that plan through user-calibrated, external Windows input.

## Quality goal

The application must preserve as much visual information as the target game can express. It must not impose an arbitrary 32×32 or eight-color ceiling. It must calculate the effective canvas resolution, color capability, stroke cost, and estimated duration before execution.

## Initial drawing modes

- Pixel: one point per logical cell.
- Scanline: contiguous horizontal or vertical runs.
- Contour: edges and simplified vector paths.
- Fill: native fill tool or generated hatch/scanline fill.
- Hybrid: outline followed by region fill.
- Auto: profile-aware mode recommendation.

## Non-goals

- Process injection, memory editing, packet manipulation, anti-cheat bypass.
- Claiming that automation is undetectable.
- Assuming every game exposes accessible UI controls.
