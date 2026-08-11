# Phase 6 implementation

Phase 6 turns the shell into a responsive One UI-inspired workspace. The
layout state is intentionally separate from drawing execution so a future
executor can update progress without taking over presentation code.

## Responsive layout

- `ResponsiveLayoutPolicy` defines deterministic `Compact` (0–719 px),
  `Medium` (720–1119 px), and `Expanded` (1120 px and above) buckets.
- The WinUI page moves the control panel below the preview in compact mode,
  keeps a two-column workspace in medium mode, and gives the preview more
  space in expanded mode.
- Padding and execution overlay margins scale with the same state, preventing
  clipped controls on narrow windows.

## Theme and accessibility

- `ThemeResources.xaml` now has Light, Dark, and Default theme dictionaries;
  the page uses `ThemeResource` lookups so colors update immediately.
- The ViewModel cycles System → Light → Dark from the header theme button or
  Alt+T. F7 pauses/resumes and F8/Escape stop safely.
- Primary controls expose AutomationProperties names/help text, and the
  progress and loading surfaces are announced as distinct controls.
- A reduced-motion preference is available in the accessibility card. The
  current shell uses no decorative transitions, so enabling it is safe before
  execution animations are introduced.

## Loading and execution panel

- Image loading drives a modal progress overlay with a `ProgressRing` and
  status text, and always clears the busy flag in a `finally` block.
- Progress is clamped to 0–100% and exposed as both a bar and a text label.
- The execution panel is a high-z-order overlay inside the app window. It
  remains visible while running or paused, exposes pause/resume and stop
  commands, and supports a pinned/unpinned visual preference.
- The UI commands only change presentation state at this stage; native input
  continues to be owned by the Phase 4 executor and is not invoked by the
  shell preview.

## Verification

- Responsive breakpoints and two-column behavior are covered by Core tests.
- The full solution builds with zero warnings and all existing imaging,
  planning, Windows execution, and Podiums adapter tests remain green.
