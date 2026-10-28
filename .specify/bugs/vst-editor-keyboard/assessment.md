# Bug Assessment: VST editor keyboard injection

- **Slug**: vst-editor-keyboard
- **Created**: 2026-10-28
- **Source**: pasted text (plan-mode thread 2026-10-28: "in VST windows, playing via the physical keyboard inside the synth window does not work, although in theory it should" + decision "rather give it to the plugin" for Space/Return)
- **Verdict**: likely valid, needs reproduction
- **Severity**: medium

## Report (verbatim or summarized)

Reporter: in VST windows, playing with the computer's physical keyboard while the
synth window is focused does not work; in theory it should be possible. Follow-up
decision: the keyboard (notes as well as Space/Return) in the VST window belongs to
the plugin — audition plays that visible plugin, Space/Return do not drive Nota's
transport.

## Symptom

With focus on a native VST/AU editor window, typing-keyboard presses
(`A W S E D F T G Y H U J K`) play nothing (in whole or in part — `A` missing,
octave drift), even though injection code exists. Expected: the keys play the
visible instrument; every other key (Space/Return included) passes through to
the plugin.

## Reproduction

1. Add a VST3/AU instrument to a track, open its editor (`openEditor`).
2. Focus the editor window, press `A` — [expected: C4, observed per code: nothing].
3. Press `W S E D F T G Y H U J K` on macOS — [to confirm manually; the code predicts notes at velocity 0.85, fixed octave].
4. Repeat steps 2–3 on Windows/Linux — [expected per code: nothing, no injection].
5. Shift the octave with `Z/X` in the main window, return to the editor, compare pitch — [expected drift].
6. [NEEDS CLARIFICATION: exact OS, format (VST3/AU) and plugin the reporter tested; whether mouse-clicking the plugin GUI piano was meant too — that is a separate VST3 EditController/Processor mechanism and may be plugin-dependent].

## Suspected Code Paths

- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.cpp:21-37` — `keycodeToPitch` knows only `W..K`, no `A=60`; rigid map with no octave.
- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.cpp:72-93` — `installKeyMonitor` only `#if JUCE_MAC`; `return nil` consumes notes; no text-field exemption; `ev.window != handle.window` check.
- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.h:5-7` — comment states outright: on Windows "JUCE key events reach the editor normally" (unverified for native HWND-child editors).
- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.cpp:107-117` — `EditorHost::open` creates the window in the constructor (monitor already installed) and assigns `onNoteOn/Off` after the fact; on reopen it assigns nothing.
- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.cpp:87` — hardcoded velocity `0.85f`.
- `src/native/nota.engine/pluginhost/hosting/PluginInstrument.cpp:23-24,65-67` — `GuiNoteQueue` message→audio, drained in `render()`; the only path for editor notes.
- `src/native/nota.engine/pluginhost/hosting/PluginEffect.cpp:110-112` — effects wire no sink (correct: keys pass through to the plugin).
- `src/managed/Nota.App/MainWindow.Input.cs:30-35,379-385` — reference map `A=60..K=72` + `_typingOctave*12`, `_typingVelocity`; `src/managed/Nota.App/MainWindow.axaml.cs:35-36` — defaults `octave 0, vel 100`.
- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.cpp:119-121` — `close()` only hides, no `allNotesOff` (stuck-note risk).

## Root Cause Hypothesis

Confidence: medium. Injection exists only on macOS and is incomplete (no `A`, no
octave/velocity sync with managed), and does not exist at all on Windows/Linux;
on top of that, the fragile focus check + wiring mean some native editors
(separate NSView/NSWindow, popups) never receive notes. Mouse-clicking the
plugin GUI piano is a separate cause (VST3 Editor/Processor split, plugin-dependent)
— not part of this fix. Full confirmation needs the manual repro matrix.

## Proposed Remediation

**Preferred**: keep the audition semantics (editor keyboard plays that visible plugin;
Space/Return belong to the plugin) and repair the injection: (1) shared
`TypingKeyboard` table with `A`, (2) sink wired before window creation + re-assign on
reopen, (3) octave/velocity sync via a minimal new ABI (managed pushes on `Z/X/C/V`
and at startup; native computes `clamp(base+oct*12)`), (4) exemption for
`NSTextView/NSTextField`, (5) release of held notes on hide/close, (6) JUCE
`KeyListener` fallback on Win/Linux, (7) Space/Return explicitly pass-through by
requirement.

**Alternatives**:
- Routing via `Engine.NoteOn` (armed track) instead of direct injection — rejected: it would play the armed track instead of the visible plugin + double-note risk; conflicts with the reporter's decision.
- macOS-only + documenting the gap on Win/Linux — rejected: the report targets a cross-platform DAW.

**Files likely to change**:
- `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.h/.cpp`
- `src/native/nota.engine/pluginhost/hosting/TypingKeyboard.h/.cpp` (new shared map + state)
- `src/native/nota.engine/include/nota/nota_engine.h` + ABI TU (new typing-keyboard setter)
- `src/managed/Nota.Infrastructure/Interop/*` + `src/managed/Nota.App/MainWindow.Input.cs` (octave/vel push)

**Tests to add or update**:
- Manual matrix: macOS/Win/Linux × 2 plugins × `A` vs `W-K` × `Z/X` octave × text field × close while holding a key (the GUI path has no headless coverage).

## Risks & Considerations

- AR-6/AR-4: zero alloc/lock on the audio thread; octave/velocity as atomics only, pitch resolved before the queue; the queue is already SPSC.
- AR-7: the new ABI must be pure C; zero JUCE types outside `pluginhost/`.
- Reverse focus stealing: the text exemption must be precise, or preset-name typing starts playing notes.
- Perf gate CI: the change is message-thread only, but list tested platforms in the PR.
- Plugin GUI piano: promise no fix; only a spike on `NotaPentad_VST3` + a docs sentence.

## Open Questions

- [NEEDS CLARIFICATION: reporter's OS + format + exact plugin for repro]
- [NEEDS CLARIFICATION: whether Z/X had been shifted at test time (would explain "sometimes works")]
- Confirmation: audition = visible plugin (assumed from "give it to the plugin"; to close formally in fix).
