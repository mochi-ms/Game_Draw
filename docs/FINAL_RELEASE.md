# GameDraw final release handoff

The current handoff is `1.0.0-rc.12`. It replaces raster scan-row execution for preserved ink with artist-ordered centreline paths: large silhouette and outer strokes first, facial features second, and local hair/clothing details last. It also samples mouse-up three times across two game frames, preventing a missed release from turning the next positioning move into a long filled connector. Brush thickness and the active drawing tool remain under manual in-game control.

## Build one distributable archive

Run from PowerShell at the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

The script restores dependencies, runs the complete Release test suite, publishes an unpackaged self-contained application containing both .NET and Windows App SDK runtime files, checks that `GameDraw.App.exe` exists, and creates:

- `artifacts/GameDraw-1.0.0-rc.12-win-x64.zip`
- `artifacts/GameDraw-1.0.0-rc.12-win-x64.zip.sha256`

Use a new version when creating another build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version 1.0.0-rc.12
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
9. Confirm the execution-path preview matches the actual stroke geometry and the largest silhouette/outer strokes run before facial and local-detail strokes.

Do not publish as a signed stable release until this live pass succeeds on the target account and the target game's automation rules have been reviewed.

## Signing boundary

The repository can create a complete unsigned self-contained archive. A public signed MSIX/installer requires a publisher identity and a private code-signing certificate. Those credentials are deliberately not generated or stored in this repository.
