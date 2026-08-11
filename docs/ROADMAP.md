# Greenfield Roadmap

## Phase 0 — repository foundation

- Preserve the previous prototype as `legacy/old-prototype`.
- Create a clean orphan `main` branch.
- Generate the new solution, project boundaries, test projects, toolchain pinning, and documentation.
- Verify restore/build/test.
- Push the initial `main` baseline.

## Phase 1 — domain contracts and application shell

Define immutable image, color, canvas, profile, drawing mode, plan, progress, cancellation, and verification contracts. Create a minimal WinUI shell with image selection and a visible state model.

## Phase 2 — high-fidelity imaging

Implement full-resolution decoding, alpha/background policy, high-quality resampling, color management, adaptive palettes, Lab/Delta-E mapping, dithering, and golden-image tests.

## Phase 3 — multi-mode planning

Implement pixel, horizontal/vertical scanline, contour/vector, fill/hatch, hybrid, and automatic mode selection. Add stroke ordering and time estimation.

## Phase 4 — safe Windows execution

Implement target-window binding, client-relative coordinate mapping, DPI/multi-monitor handling, `SendInput`, foreground checks, pause/resume, emergency stop, input-rate limits, and guaranteed mouse release.

## Phase 5 — Podiums adapter

Implement the first profile and calibration wizard for the Podiums canvas, pencil, brush size, and HEX input. Support manual calibration fallback and state verification.

## Phase 6 — responsive One UI-inspired UX

Implement expanded/medium/compact layouts, dark mode, loading and progress states, accessible typography, keyboard navigation, reduced motion, and an always-on-top execution panel.

## Phase 7 — visual recognition

Add window capture, template/anchor matching, canvas detection, tool detection, confidence thresholds, and automatic pause/recalibration on drift.

## Phase 7.5 — integrated safety and Podiums reality pass

Connect image processing, planning, profile persistence, Podiums controls, Windows execution, global safety hotkeys, live visual checks, and responsive UI into one runnable workflow. Correct the real Podiums eraser, vertical brush-size slider, and HEX-enter interaction. Require immediate input release on pause/stop and block input as soon as Roblox loses foreground focus.

## Phase 8 — adapter SDK and second game

Add profile import/export, capability-based adapters, migration, and a second drawing game without changing the core planner or executor.

Implemented with a versioned portable profile service, schema migration, reusable HEX input adapter, capability catalog, and Generic HEX Whiteboard reference adapter. The same pass also replaced the vertically stacked shell with a height-aware desktop workspace and removed large-image analysis hangs.

## Phase 9 — production hardening

Run image-quality, DPI, multi-monitor, long-run, cancellation, packaging, and regression tests. Publish a signed or documented self-contained build.
