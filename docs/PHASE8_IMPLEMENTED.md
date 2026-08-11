# Phase 8 implemented

## Desktop workflow

- Removed the page-level vertical `ScrollViewer`.
- Added a single-viewport three-column layout: workflow, preview, and controls.
- Uses display work-area sizing plus width/height-aware compact rules.
- Keeps image selection, analysis, Podiums calibration, and execution visible together.
- Added an in-app four-step help dialog and clearer numbered actions.
- Moved infrequent profile, logical-size, transfer, and accessibility options into a compact flyout.

## Analysis hang fix

- Confirmed Windows `Application Hang` events rather than an image-decoder crash.
- Runs image processing and planning away from the UI thread.
- Added cancellation ownership to the preparation flow.
- Replaced repeated full edge scans in contour chaining with indexed outgoing edges.
- Replaced quadratic nearest-stroke ordering for large plans with row-serpentine ordering.
- Automatic candidates retain only the selected full plan to avoid multiplying memory use.
- The dense 128×128 automatic regression test dropped from about 22 seconds to below one second on the development machine.

## Adapter SDK

- Added portable `.gamedrawprofile` import/export with atomic writes.
- Added current-schema migration and future-schema rejection.
- Extended adapter capabilities for portable profiles and custom window targets.
- Added a reusable exact HEX input color adapter.
- Added `Generic HEX Whiteboard` as a second/reference adapter without changing the core image planner or Windows executor.

## Safety

- The existing global `F7` pause/resume and `F8` immediate-stop behavior remains unchanged.
- Profile imports receive a new identity and cannot silently overwrite an existing local profile.
- The main start action is unavailable until both image analysis and calibration are ready.
