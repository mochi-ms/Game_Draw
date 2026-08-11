# Phase 9 implemented

Phase 9 turns the end-to-end prototype into a usable Windows release candidate.

## Live drawing reliability

- The saved manual canvas rectangle is now treated as a registration hint instead of an exact pixel lock.
- Immediately before drawing, GameDraw detects the current bright Podiums canvas, checks overlap/shift/scale tolerances, and executes against the newly detected rectangle.
- Content-based canvas monitoring no longer runs after the first stroke. Painting the white surface therefore cannot be mistaken for the canvas disappearing.
- Foreground-window and geometry checks still run throughout execution, and pause/stop always release pressed input.
- A failed preflight now closes both execution surfaces instead of leaving an inactive overlay over the controls.

## Rendering choices

- **Automatic color** retains the adaptive-palette and exact HEX color workflow.
- **Black line art** uses a Sobel edge pass and transparent background so only detected contours are drawn.
- All existing planners remain available: pixel, horizontal/vertical scanline, contour, fill, hybrid, and automatic.

## Speed and floating UI

- Safe, fast, and very-fast presets apply 1×, 2×, and 4× timing multipliers to movement and control delays.
- Input throughput is capped at 1,000 events per second while all target-window safety checks remain enabled.
- The main window can be resized to the right side of the active display and kept always on top; disabling floating restores its previous bounds.

## Verification

- Canvas registration regression tests cover ordinary manual-coordinate variance and unrelated bright-region rejection.
- Line-art tests cover edge extraction, transparent background, and uniform-image behavior.
- Existing imaging, planning, adapter, coordinate, DPI, multi-monitor, pause, cancellation, and emergency-release tests remain in the release gate.

## Release build

From the repository root:

```powershell
dotnet restore GameDraw.sln
dotnet test GameDraw.sln -c Release
dotnet publish src/GameDraw.App/GameDraw.App.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false
```

`scripts/Build-Release.ps1` packages this output into a versioned ZIP, includes both the .NET and Windows App SDK runtimes, and writes a SHA-256 checksum. The unsigned release candidate is suitable for local and controlled testing. Public distribution still requires selecting a publisher identity and signing certificate.
