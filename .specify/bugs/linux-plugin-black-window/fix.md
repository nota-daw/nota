# Bug Fix: Linux — hosted plugins show a black window and make no sound

- **Slug**: linux-plugin-black-window
- **Fixed**: 2026-10-14
- **Assessment**: ./assessment.md
- **Status**: applied (not yet verified on Linux)

## Summary

Added a C ABI entry `nota_pluginhost_pump()` that services JUCE's Linux event loop. The app ticks it from a 10 ms
Avalonia `DispatcherTimer` on Linux only. With the loop serviced, plugin editors get their X11 events (paint, close
button), and hosted plugins run their timers and async work again, which they hand to our `IRunLoop` when they are
created.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/pluginhost/PluginHost.cpp` | modified | `nota_pluginhost_pump()`. On Linux/BSD it loops `juce::detail::dispatchNextMessageOnSystemQueue(true)` (non-blocking `poll(0)`), at most 64 batches per call. It is a no-op before JUCE GUI init and off the message thread. It returns 0 on macOS/Windows. |
| `src/native/nota.engine/include/nota/nota_engine.h` | modified | Declares and documents the export. `nota_exports.txt` / `.map` already export `nota_*`. |
| `src/managed/Nota.Infrastructure/Interop/NativeMethods.PluginHost.cs` | modified | `PluginHostPump` P/Invoke. |
| `src/managed/Nota.Infrastructure/Interop/NotaEngine.PluginHost.cs` | modified | `NotaEngine.PumpPluginHost()`. |
| `src/managed/Nota.App/App.axaml.cs` | modified | On `OperatingSystem.IsLinux()`, starts a 10 ms `DispatcherTimer` (Normal priority) that calls the pump. |
| `tests/Nota.SmokeTest/Program.cs` | added test | "plugin host pump" check. |
| `CHANGELOG.md` | modified | `[Unreleased] → Fixed`: plug-ins work on Linux. |

## Diff Highlights

```cpp
extern "C" NOTA_API int32_t nota_pluginhost_pump(void) {
#if JUCE_LINUX || JUCE_BSD
    if (g_juceGui == nullptr) return 0;
    auto* mm = juce::MessageManager::getInstanceWithoutCreating();
    if (mm == nullptr || !mm->isThisTheMessageThread()) return 0;
    int n = 0;
    while (n < kMaxPerPump && juce::detail::dispatchNextMessageOnSystemQueue(true)) ++n;
    return n;
#else
    return 0;
#endif
}
```

## Tests Added or Updated

- `tests/Nota.SmokeTest/Program.cs` "plugin host pump" check: the export resolves on every platform, and with no
  plugin loaded it returns 0 without blocking. This also runs on headless Linux CI, so a missing symbol or an
  accidental block is caught.
- Not added: an end-to-end "instantiate a VST3 on Linux, pump, render, assert non-silent" test. The smoke test has
  no hosted-plugin fixture yet. `NotaPentad` is an opt-in build. See Follow-ups.

## Local Verification

- Commands run: `bash scripts/build.sh` (macOS arm64) → build OK, `SMOKE TEST PASSED` (3291 PASS, 0 FAIL), including
  `[PASS] plugin host pump: exported, non-blocking no-op with no plugin loaded`.
- Not verified: the `JUCE_LINUX` branch was **not compiled** here (macOS host; Docker daemon not running). The
  `juce::detail::dispatchNextMessageOnSystemQueue` declaration was matched against the vendored JUCE
  (`juce_events/messages/juce_MessageManager.cpp:96-99`, `juce_events/native/juce_Messaging_linux.cpp:341`). Linux
  CI (`scripts/build-linux.sh`) will compile it.
- Not verified: behaviour on a real Linux desktop (Wayland + XWayland). That is `/speckit-bug-test`.

## Deviations from Assessment

- **Root cause of the silence, refined (strengthens the assessment).** The assessment said hosted plugins get
  `IRunLoop` through the editor window. They actually get it earlier. JUCE's VST3 host context exposes
  `Linux::IRunLoop` (`juce_VST3PluginFormatImpl.h:682`), and a JUCE-built plugin's component and controller pick it
  up when they are created (`juce_audio_plugin_client_VST3.cpp:776-779` → `registerHandlerForRunLoop` →
  `updateCurrentMessageThread`). The plugin then **stops its own message thread** and adopts our UI thread. So
  everything async in the plugin depended on the never-pumped loop from the moment of instantiation. That explains
  the reporter's "silent even without opening the editor". The same pump fixes it, and no code change follows from
  the refinement.
- The pump timer starts at app startup on Linux, not lazily at first instantiation. It is cheap: `poll(0)` plus an
  early return before JUCE init. This avoids threading the "first plugin" event into the App layer.

## Follow-ups

- Run `/speckit-bug-test` on a Linux machine (Wayland session): a registry plugin (e.g. Surge XT) should sound with
  the editor closed, the editor should paint and take mouse input, the close button should close it, and reopening
  should work.
- Linux editor polish, outside this fix: `PluginEditorWindow` has a macOS-only key monitor. Check that the
  computer-keyboard note map reaches the plugin window on Linux. Also check that `setAlwaysOnTop` behaves under
  XWayland.
- Consider a Linux CI smoke test that builds `NotaPentad` and instantiates it in-process with a pump + render
  assert.
