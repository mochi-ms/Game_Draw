# GameDraw final release handoff

The 0–9 roadmap is implemented. The current handoff is `1.0.0-rc.1`: a release candidate intended for real Podiums validation before a signed public release.

## Build one distributable archive

Run from PowerShell at the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

The script restores dependencies, runs the complete Release test suite, publishes an unpackaged self-contained application containing both .NET and Windows App SDK runtime files, checks that `GameDraw.App.exe` exists, and creates:

- `artifacts/GameDraw-1.0.0-rc.1-win-x64.zip`
- `artifacts/GameDraw-1.0.0-rc.1-win-x64.zip.sha256`

Use a new version when creating another build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version 1.0.0-rc.2
```

## Acceptance pass on Podiums

1. Close older GameDraw processes and launch the new build.
2. Load one small, high-contrast image and select **Black line art / Fast / Horizontal scanline**.
3. Analyze, calibrate the eight controls if needed, then start drawing and switch to Roblox within 15 seconds.
4. Confirm that the first stroke lands inside the detected canvas, `F7` releases the mouse while paused, and `F8` releases it immediately while stopping.
5. Repeat with **Automatic color**, confirming that the HEX field changes and colored runs are drawn.
6. Move or resize Roblox slightly and confirm that execution uses the newly detected canvas instead of rejecting a harmless calibration difference.
7. Test **Float over game** and confirm that disabling it restores the previous window bounds.

Do not publish as a signed stable release until this live pass succeeds on the target account and the target game's automation rules have been reviewed.

## Signing boundary

The repository can create a complete unsigned self-contained archive. A public signed MSIX/installer requires a publisher identity and a private code-signing certificate. Those credentials are deliberately not generated or stored in this repository.
