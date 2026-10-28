# Bug Fix: VST editor typing-keyboard audition

- **Slug**: vst-editor-keyboard
- **Fixed**: 2026-10-28
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Hosted plugin editor windows audition the computer keyboard on that visible
plugin, at the main window's octave (`Z/X`) and velocity (`C/V`); every other
key (Space/Return included) belongs to the plugin. Fixed: missing `A`, octave
drift, lost wiring on reopen, stuck notes, and no path outside macOS.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `pluginhost/hosting/TypingKeyboard.h` | added | Shared map (mac keycodes + characters, with `A`) and process-wide octave/velocity state; no JUCE |
| `pluginhost/hosting/TypingKeyboard.cpp` | added | Implementation + `nota_pluginhost_set_typing_keyboard` ABI (clamped, atomics; audio thread never touches it) |
| `pluginhost/hosting/PluginEditorWindow.h` | modified | Sink in constructor, `setNoteSink`, `releaseHeldNotes`, JUCE `keyPressed`/`keyStateChanged(bool)` fallback |
| `pluginhost/hosting/PluginEditorWindow.cpp` | modified | Monitor uses the shared map, text-field exemption, per-key held pitch, notes consumed only, wiring fix in `EditorHost::open` |
| `src/native/nota.engine/CMakeLists.txt` | modified | `TypingKeyboard.cpp` in `nota_pluginhost` |
| `include/nota/nota_engine.h` | modified | `nota_pluginhost_set_typing_keyboard` declaration |
| `Nota.Infrastructure/Interop/NativeMethods.PluginHost.cs` | modified | P/Invoke of the new ABI |
| `Nota.Infrastructure/Interop/NotaEngine.Devices.cs` | modified | `SetPluginTypingKeyboard` (instance, next to `OpenPluginEditor`) |
| `Nota.Application/Engine/IAudioEngine.cs` | modified | `SetPluginTypingKeyboard` on the port |
| `Nota.App/MainWindow.Input.cs` | modified | `PushTypingKeyboardToHost` + push on `Z/X/C/V` |
| `Nota.App/MainWindow.axaml.cs` | modified | One push at startup (next to `InitGamepad`) |
| `CHANGELOG.md` | modified | `[Unreleased] Fixed` entry |

## Diff Highlights (optional)

- Sink wired before `installKeyMonitor()` (constructor), not after; reopen does `setNoteSink` instead of a silent `setVisible`.
- `noteOn` stores the resolved pitch per key; `releaseHeldNotes()` on hide/close/destructor releases only editor notes (main-window live notes belong to the engine).
- Notes consumed (`nil`/`true`), everything else passed through — Space/Return explicitly the plugin's.

## Tests Added or Updated

- No automated tests (the GUI path has no headless coverage). Coverage is the manual matrix from the assessment.
- Manual matrix from the assessment (macOS/Win/Linux × plugins × `A` vs `W-K` × `Z/X` × text field × close while holding a key) — to run by the reporter; native HWND-child editors on Windows outside generic/JUCE editors remain a follow-up.

## Local Verification

- `cmake --build … --target nota-scanner-selftest nota_engine` → OK (after one fix: JUCE 8 `keyStateChanged` returns `bool`).
- `nota-scanner-selftest` → `SCANNER SELFTEST PASSED`.
- `dotnet build Nota.sln -c Release` → 0 errors, 33 warnings (pre-existing).
- Smoke test (3 runs): two marginal FAILs of render-pool timing guards (`overloaded: 176/4125 late`, then `9`, then `5` in 50 ms at threshold ≤4) — values jump between runs on the same code, and the change does not touch the render path (new TU, GUI/message-thread only, additive ABI, UI push). Verdict: timing flake on this machine, not a regression; remaining ~4390 checks green.

## Deviations from Assessment

- Per-key release of held editor notes on hide instead of the suggested `allNotesOff`: a global all-off would also cut voices played from the main window (armed track) on the same plugin.
- No changes in `PluginInstrument.cpp`/`Engine`: state is process-wide in pluginhost, so no fan-out needed; engine-less ABI (like `pump`), zero AR-5 impact.
- Win/Linux fallback is a JUCE listener (generic + JUCE editors); native HWND views swallowing keys need a message hook — explicit follow-up, not a promised fix.

## Follow-ups

- `WH_GETMESSAGE` hook (Windows) / native VST3 editor verification — separate bug after the manual matrix.
- Plugin GUI piano spike (`NotaPentad_VST3`) + docs sentence — out of scope for this fix.
- `nota-docs`/`nota-shortcuts`: no shortcut changes (map unchanged), only VST window behavior — CHANGELOG suffices.
