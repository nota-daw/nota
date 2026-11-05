# Validation: Controls inside MIDI CC lanes

**Date**: 2026-11-04
**Branch**: `feat/multiple-midi-cc-lanes`, existing nota-daw/nota PR #425.
**Original head**: `957d52865886483dd63f81f1065c70915c3f3667`.

## Implementation

Primary and extra controller lanes share one compact controller-name/CC-number
dropdown and adjacent close button. Controls have their own shallow row, leaving
every graph point reachable. Unused header width retains the graph background.
The former full-width CC lanes toolbar is replaced by a sidebar **+ lane** action.
The gutter scale reads graph coordinates directly: 127/64/0 for ordinary MIDI
controllers, signed normalized values for the pitch wheel. Resize hit zones retain
their six-pixel extent and paint only a thin seam. Settings keys and MIDI routing
are unchanged. Clip strips with many overlapping clips now scroll within the layout
budget so they cannot consume the entire note viewport.

## Checks

| Check | Result |
| --- | --- |
| .NET Release solution build | Passed; no errors, existing compiler warnings |
| Windows native engine and scanner build | Passed |
| `scripts/build-win.ps1` full gate | Passed on final revision, including smoke, render-pool and scanner-scale suites |
| Real Windows Avalonia CCProbe | **85 assertions passed** |
| Separate-process lane-height restoration | Passed |
| Controller picker, primary/extra removal and MIDI preservation | Passed |
| Menus invalidated by clip reload or lane removal | Passed |
| Close control in a 180 px wide roll | Passed |
| Minimum/expanded value scale and inverse pointer mapping | Passed |
| Folded Drum Rack, unfold/refold and re-added lane origins | Passed |
| Zoom/scroll and independent duplicate-controller heights | Passed |
| Floating and real docked editor, Mixer, restored heights, panel-edge resizing | Passed |
| Twelve stacked clips, scrolling and active-clip changes in both hosts | Passed |
| Preferences Shortcuts filtered to the controller rows | Passed; rendered and visually inspected |
| Studio Dark/Light screenshots | Rendered and visually inspected |
| `git diff --check` | Passed |
| External manual `npm run build` / `npm run check` | Passed; 106 pages, zero errors, 72 existing warnings |
| Manual screenshots | Clip editor, multi-clip editor and Shortcuts retaken in Dark/Light |
| Manual publication patch | Zero-context patch reverse-checked against the locally updated EN/RU pages |

Commands are in [quickstart.md](quickstart.md). Logs and generated harness output
remain in ignored `build/`. The existing CCProbe is extended instead of adding a
duplicate editor test harness.

## Before and after

Same 1100 × 850 window, Studio Dark, 100% scale, identical notes/controller curves;
before uses the original PR head. Evidence is stored on the existing `pr-assets`
branch of the PR author's fork, without adding images to the code branch.

| Before | After |
| --- | --- |
| ![Before](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422-inline-controls/cc-controls-before.png) | ![After](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422-inline-controls/cc-controls-after.png) |

Additional [Studio Light](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422-inline-controls/studio-light-cc.png)
and [stacked-clip](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-422-inline-controls/stacked-inline-cc.png) captures.

## Limits and observed failures

- Windows x64 was tested locally; macOS and Linux were not run.
- The first smoke run found an obsolete French `CC lanes` key; it and the old
  controller-tab instruction were removed. Localization guards then passed.
- A concurrent rerun exceeded the render-pool timing limit (55/4125 late blocks)
  while documentation screenshots were rendering. The previous full run passed;
  the final quiet run passed with **0/4125** late blocks and completed the full gate.
- `dotnet format whitespace --verify-no-changes` is not clean on the untouched PR
  baseline either: 260 diagnostics in the same six files. This change follows the
  surrounding compact formatting; no repository-wide reformat was made.
- The separate manual repository contains pre-existing local work. Only relevant
  pages/screenshots were updated; the shortcut generator used the existing workspace
  source plus the new controller rows to preserve its other pending feature entries.
  No manual commit or push is included in this app-only PR. Apply [manual.patch](manual.patch)
  with `git apply --unidiff-zero` in nota-docs and regenerate shortcuts when publishing
  its separate update.

## Submission

Implementation converged: nine functional requirements, four buildable success criteria,
nine story acceptance scenarios and all eight constitution principles reviewed; zero
remaining code findings. SC-005 and task T016 also passed: commit `54a69e30259e1ebb2de9bc87c6382a366ba213c3`
was pushed with an ordinary fast forward and the GitHub API confirmed it as the head
of the still-open PR #425 on the same fork/branch. The original PR head remains an
ancestor. No new branch or PR was created. All 16 tasks are complete.

Final quiet gate: `build/build-win-quiet.log` ends with `SMOKE TEST PASSED` and
`SCAN SCALE PASSED` (exit 0). The concurrent timing failure was not reproduced.

## Merge with develop — 2026-11-04

Integrated develop at `d57d86dc839433c654ae17f67267a96b328fa35b` into the existing PR
branch, preserving the prior PR head `273051768eabd99201810044a37ca0d5e610c623`.

Three conflict paths were resolved:

- `PianoRollView.Controllers.cs`: retain the PR's cache/gesture generation checks
  while using develop's stable localization key for the clear menu.
- `PreferencesWindow.cs`: retain develop's Bend-range instruction and key-based
  strings, plus the PR's in-lane dropdown, sidebar action and divider instructions.
- `Languages/fr.json`: accept develop's removal of bundled translations. Migrate
  the lane UI's English texts to stable keys in `Nota.Application/Localization/en.json`;
  translations follow develop's downloaded-language and English-fallback model.

Validation after integration:

- **86 real Windows UI checks passed**, including the retained Bend-range instruction.
- Independent lane heights restored in a fresh process.
- Final `scripts/build-win.ps1` **passed**, exit 0: native/managed build, scanner
  self-test, full smoke suite and scan-scale suite. Logs are in ignored
  `build/merge-develop-build-win-final.log`.
- Localization guards passed: every key resolves, no reference entries are unused,
  and translations are no longer bundled. The redesign has 11 new stable text keys;
  the superseded primary-controller instruction key was removed.
- The first full run missed one render timing guard (five late blocks in 50 ms,
  limit four). A standalone recheck passed, and the final full run also passed
  (maximum burst three). No guard thresholds or native code were changed relative
  to develop.
- No unresolved Git entries remain; the PR diff against develop passes whitespace
  checks. The original redesign spec and integration plan converge with no remaining
  implementation gaps. macOS and Linux were not run locally.
