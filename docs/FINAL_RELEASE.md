# GameDraw final release handoff

The current handoff is `1.0.0-rc.10`. It adds natural dark-mark centreline extraction, precision outline and ink-preserving alternatives, exact-color and pixel modes, four quality presets, game-sampled mouse interpolation, one-key F5 analyze/start, confirmed pen-up transitions, automatic Podiums HEX entry, and a clipped rounded execution panel. Brush thickness is always left under manual in-game control.

## Build one distributable archive

Run from PowerShell at the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

The script restores dependencies, runs the complete Release test suite, publishes an unpackaged self-contained application containing both .NET and Windows App SDK runtime files, checks that `GameDraw.App.exe` exists, and creates:

- `artifacts/GameDraw-1.0.0-rc.10-win-x64.zip`
- `artifacts/GameDraw-1.0.0-rc.10-win-x64.zip.sha256`

Use a new version when creating another build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version 1.0.0-rc.10
```

## Acceptance pass on Podiums

1. Close older GameDraw processes and launch the new build.
2. Load a portrait or illustration. Compare **Natural pen**, **Ink preserving**, **Precision outline**, and **Original color** previews at multiple quality presets.
3. Choose a canvas ratio, click **Drag area**, and drag the exact white drawing area in Roblox. For color, capture pencil, brush slider, and HEX coordinates with the tool setup wizard.
4. Press `F5`. GameDraw must hide itself and restore/activate Roblox automatically.
5. Confirm long simplified strokes remain continuous, separate contours never receive a pen-down positioning move, `F7` releases the mouse while paused, and `F8` releases it immediately while stopping.
6. In color mode verify each color change performs HEX focus, `Ctrl+A`, `Delete`, value entry, and `Enter`.
7. Move or resize Roblox slightly and confirm that a visual-detector mismatch falls back to the dragged canvas instead of blocking execution.
8. Test **Float over game** and confirm that disabling it restores the previous window bounds.
9. Confirm the execution-path preview matches the actual stroke geometry and facial strokes begin before the outer form when a portrait is detected.

Do not publish as a signed stable release until this live pass succeeds on the target account and the target game's automation rules have been reviewed.

## Signing boundary

The repository can create a complete unsigned self-contained archive. A public signed MSIX/installer requires a publisher identity and a private code-signing certificate. Those credentials are deliberately not generated or stored in this repository.
