<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: Piano-roll playhead still stutters

- **Slug**: piano-roll-playhead-stutter (from assessment/fix context)
- **Tested**: 2026-11-01
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The actual App's arrangement MIDI piano roll now advances on every observed animation
callback, matching the arrangement. Cursor-only animation retains the notes and ruler
composition draw lists. The rebuilt Windows Release app passes the full build/smoke/
scanner checks and the real-window lifecycle diagnostic on WASAPI and Yamaha ASIO.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Before-fix reproduction | PianoProbe before diagnostics/before.json | pass (bug reproduced) | 150 cursor updates / 1120 frame callbacks; 150 grid and 150 ruler draw-list replacements. |
| WASAPI final reproduction | PianoProbe after diagnostics/after-final.json | pass | 1176 cursor and arrangement updates / 1176 callbacks; 235.20 changes/s; zero static draw-list replacements and zero reported dropouts. |
| Yamaha Steinberg USB ASIO | PianoProbe after diagnostics/after-yamaha-isolated.json "Yamaha Steinberg USB ASIO" | pass | Active backend Asio, negotiated 64 frames; 1175 cursor and arrangement updates / 1175 callbacks, 235.03 changes/s; zero static replacements and zero dropouts. |
| Seeks and playback | Automated actual clip editor checks | pass | Nonzero clip origin, paused/stopped seeks, hiding outside clip, playing=false, resume and repeated song loops. |
| Overlay geometry and transforms | Reflection and real-window capture | pass | Existing ruler/grid bounds match their overlays; clipped and non-interactive; horizontal zoom and scrolling work. |
| Floating editor and lifecycle | Actual detail popout, minimize, suspend, resume, close | pass | Floating window takes frame ownership when main is minimized; suspend/pause/close detach callbacks; stopped seek still updates. |
| Full Windows regression | scripts/build-win.ps1 from VS x64 developer environment | pass | Exit 0; SMOKE TEST PASSED; SCAN SCALE PASSED; scanner self-test passed. |
| Whitespace | git diff --check | pass | No errors. |

## Diagnostic Refinement

Earlier unisolated ASIO runs passed the cursor-cadence assertion but failed the strict
zero-static-redraw assertion (one repeat counted 41 replacements; another counted 11).
The instrumented repeat captured pixels-per-beat changing from 15.625 through 18.75,
22.5 and 27 during the nominal cursor-only interval, before the probe's own zoom step.
Interactive zoom legitimately redraws notes/ruler, invalidating a cursor-only premise.
The first failed run did not record enough input state to determine its precise cause.

The final probe disables input in its temporary main window only during the initial
cursor-only interval, then restores it before lifecycle/geometry checks. That isolated
ASIO run passed with zero replacements. No production changes were made in response
to those diagnostic assertions. The prior successful unisolated ASIO run is retained
as after-yamaha.json (235.60 changes/s, 0 replacements, 0 dropouts).

## Output Excerpts

```text
Before: cursorUpdates=150, frameCount=1120, gridDraws=150, rulerDraws=150
After WASAPI: cursorUpdates=1176, frameCount=1176, gridDraws=0, rulerDraws=0
After isolated Yamaha ASIO: cursorUpdates=1175, frameCount=1175, gridDraws=0, rulerDraws=0
SMOKE TEST PASSED
SCAN SCALE PASSED
```

## Reproduce

From the repository root, after building Release:

```powershell
dotnet build .specify/bugs/piano-roll-playhead-stutter/diagnostics/PianoProbe.csproj
dotnet .specify/bugs/piano-roll-playhead-stutter/diagnostics/bin/Debug/net10.0/PianoProbe.dll after .specify/bugs/piano-roll-playhead-stutter/diagnostics/recheck.json
dotnet .specify/bugs/piano-roll-playhead-stutter/diagnostics/bin/Debug/net10.0/PianoProbe.dll after .specify/bugs/piano-roll-playhead-stutter/diagnostics/recheck-yamaha.json "Yamaha Steinberg USB ASIO"
```

The probe opens the real app with scratch settings and generated MIDI notes, never a
user project. It also captures a local PNG beside each successful final JSON output.
Inspected after-final.png: cursor aligned through ruler and grid, correctly clipped
after zoom/scroll. Logs, images and scratch data are ignored; source and small JSON
measurements are retained. Local full build log: diagnostics/build-win.log.

## Residual Risks

- Counts measure actual cursor-property changes on animation callbacks, not physical
  monitor/compositor presentation or a guarantee for every machine.
- Musical/status colours, editing data, native audio/ABI and Session-slot clock semantics
  were not changed. This fixes arrangement MIDI editors; Session-slot interpolation
  remains outside scope.
- The diagnostic verifies pointer-routing properties and transforms; full interactive
  ruler scrubbing/note editing across all display scales remains a manual check.
- The diagnostic screenshot call has an existing-API deprecation warning; the app build
  succeeds with its existing unrelated warnings.

## Recommendation

Close this arrangement MIDI editor follow-up as verified. Restart the Release executable
at src/managed/Nota.App/bin/Release/net10.0/Nota.App.exe to test the updated cursor.
