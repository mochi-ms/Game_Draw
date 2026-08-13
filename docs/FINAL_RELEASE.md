# GameDraw final release handoff

The current handoff is `1.0.0-rc.35`. Every drawing mode now uses a printer-style final ordering pass: strokes advance from top to bottom, alternate left-to-right and right-to-left by horizontal band, and keep pencil/fill phases stable. Nearby disconnected strokes use a fast released-frame positioning gate; jumps of 18 screen pixels or more and every canvas-to-HEX transition use the full pointer-capture reset and verified focus boundary.

Podiums HEX selection now performs a real double-click, Ctrl+A replacement, clipboard paste, Ctrl+A/Ctrl+C readback, normalized `RRGGBB` comparison, and Enter commit. A color is not reported as selected unless the editable field contains the requested value; two failed attempts abort execution before the next stroke. Normal drawing modes also preserve the user's selected tool instead of making an extra Pencil-button trip.

HEX entry now has a fixed safe cadence independent of the selected drawing speed: focus wait, `Ctrl+A`, `Delete`, repeated `Ctrl+A`, clipboard paste, `Enter`, and commit wait. The visible workflow list adds **AI grayscale photo**, which uses perceptual luminance, preserves transparency and portrait geometry, and quantizes to 4-32 neutral shades before safe-stamp planning.

The color workflow is exposed as **AI underdrawing and safe color**. It draws only the true connected subject silhouette first, then overlays the entire quantized photograph using short, local same-color pencil-stamp chunks. It no longer puts a black contour around every palette region and no longer uses paint-bucket clicks, so the final output stays close to the pixel-color preview while retaining the requested outline-first execution order.

AI silhouette planning rejects components touching the analysis-frame edges, preventing letterbox/background remnants from becoming a large rectangular border. Artist precision line art now uses a single direction of portrait shading strokes instead of the former wire-grid crosshatch. The execution panel reapplies both DWM rounded-corner preference and its native rounded window region every time it is shown or resized.

The expression selector contains five focused high-quality workflows: **AI underdrawing and safe color**, **AI grayscale photo**, **Original palette 256 colors**, **Artist precision line art**, and **High-quality halftone photo**. Older modes remain readable in saved profiles for compatibility but are no longer offered in the UI.

**Artist precision line art** remains available for monochrome work, combining thin feature contours with connected directional cross-hatching. Safe stamps may combine up to twelve adjacent same-color points only after verifying every short connector stays inside intended ink; the executor still breaks continuous pen-down travel at the configured safety distance.

The color-count control accepts 2–256. AI underdrawing uses a quality-dependent photo palette up to 64 colors and finishes every region with safe local stamps. **Original palette 256 colors** treats the control as a ceiling: at maximum execution speed it perceptually consolidates the palette to at most 128 representative colors, halving the risky HEX round trips while preserving the high-fidelity silhouette and facial detail. Lower speed settings retain up to the requested 256 colors. On the captain probe the maximum-speed plan changed from 256 colors / 18,355 strokes to 127 colors / 14,817 strokes, while the rendered preview remained visually equivalent.

## Build one distributable archive

Run from PowerShell at the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

The script restores dependencies, runs the complete Release test suite, publishes an unpackaged self-contained application containing both .NET and Windows App SDK runtime files, checks that `GameDraw.App.exe` exists, and creates:

- `artifacts/GameDraw-1.0.0-rc.35-win-x64.zip`
- `artifacts/GameDraw-1.0.0-rc.35-win-x64.zip.sha256`

Use a new version when creating another build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version 1.0.0-rc.35
```

## Acceptance pass on Podiums

1. Close older GameDraw processes and launch the new build.
2. Choose a canvas ratio, click **Drag area**, and drag the exact white drawing area in Roblox. Capture pencil and HEX coordinates with the tool setup wizard.
3. Confirm pencil and HEX coordinates are saved. Then load a portrait and compare the four visible high-quality workflows.
4. Press `F5`. GameDraw must hide itself and restore/activate Roblox automatically.
5. Confirm long simplified strokes remain continuous, separate contours never receive a pen-down positioning move, `F7` releases the mouse while paused, and `F8` releases it immediately while stopping.
6. First run the visible `HEX #FF3B82 live test`. Verify focus, `Ctrl+A`, `Delete`, repeat selection, clipboard `#FF3B82`, `Ctrl+V`, and `Enter`.
7. In **AI underdrawing and safe color**, confirm the subject silhouette runs first, no fill-tool click occurs, and the finished preview contains color-detail stamps rather than black borders around palette islands.
8. Move or resize Roblox slightly and confirm that a visual-detector mismatch falls back to the dragged canvas instead of blocking execution.
9. Test **Float over game** and confirm that disabling it restores the previous window bounds.
10. Confirm the execution-path preview matches the actual outline and bucket-fill result.

Do not publish as a signed stable release until this live pass succeeds on the target account and the target game's automation rules have been reviewed.

## Signing boundary

The repository can create a complete unsigned self-contained archive. A public signed MSIX/installer requires a publisher identity and a private code-signing certificate. Those credentials are deliberately not generated or stored in this repository.
