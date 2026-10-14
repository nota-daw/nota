# Bug Assessment: Linux — hosted plugins show a black window and make no sound

- **Slug**: linux-plugin-black-window
- **Created**: 2026-10-14
- **Source**: pasted text (maintainer report)
- **Verdict**: valid (symptoms match the code; fix still to be verified on Linux)
- **Severity**: high

## Report (verbatim or summarized)

> Нашел проблемы на линуксе. Качаю любой плагин из реестра, ни один не работает. Просто окно с логотипом X,
> черный фон, звука нет.

(Linux: every plugin installed from the registry fails. The editor window shows only an X logo on a black
background, and there's no sound.)

## Symptom

On Linux, any VST3 installed through Get Plug-ins loads: a track is created and an editor window opens. The window
is never painted, though. It stays black, with the X server's default "X" icon/decoration. The plugin also produces
no audio. Expected: the plugin's GUI draws and notes play, as on macOS and Windows.

## Reproduction

1. Linux x64 build of Nota (current `develop` / latest release). [NEEDS CLARIFICATION: distro, X11 vs Wayland
   (XWayland), Nota version]
2. Get Plug-ins → install any registry plugin (e.g. Surge XT) → it's scanned and listed.
3. Add it as an instrument and open its editor → a black window appears.
4. Play notes from the piano roll / keyboard → silence. [NEEDS CLARIFICATION: silent even with the editor never
   opened? Is a locally built/installed (non-registry) VST3 also affected? Expected yes.]

## Suspected Code Paths

- `src/native/nota.engine/pluginhost/PluginHost.cpp:9-13`: the design note states the integration
  premise: *"JUCE piggybacks on the already-running CFRunLoop"* (Avalonia's NSApp on macOS). On Windows the same
  works implicitly, because JUCE's hidden message HWND is pumped by Avalonia's Win32 loop. **On Linux JUCE has its
  own event loop** (an internal fd/poll loop over the X11 display fd and its message pipe). Something must call
  `MessageManager::runDispatchLoop` / `dispatchNextMessageOnSystemQueue`. Nothing in Nota does.
- `PluginHost.cpp:921-930` `instantiate()`: creates `ScopedJuceInitialiser_GUI` and the plugin on the Avalonia UI
  thread, then returns. No dispatching anywhere afterwards (grep: no `runDispatchLoop`, `dispatchNextMessage`,
  or a pump C ABI; no managed timer calling one).
- `PluginHost.cpp:416-449` `PluginEditorWindow`: a `DocumentWindow` with `juce::Colours::black` background. The X
  window is created and mapped, but the Expose/paint events and JUCE's repaint messages are never dispatched →
  exactly "black window with an X". The `JUCE_MAC` keyboard monitor has no Linux counterpart either (secondary).
- `src/native/nota.engine/CMakeLists.txt:192-197`: `JUCE_MODAL_LOOPS_PERMITTED=0`, so
  `runDispatchLoopUntil()` isn't available. Any pump has to use `dispatchNextMessageOnSystemQueue(true)`.

## Root Cause Hypothesis

The JUCE message loop never runs on Linux. **High confidence for the black window.** Nota relies on the host app's
native run loop to drive JUCE. That holds on macOS (shared CFRunLoop) and on Windows (shared Win32 message queue).
On Linux, Avalonia's X11 backend runs its own loop on its own `Display*` connection, so JUCE's X11 connection and
message pipe are never serviced. **Medium confidence for the silence.** VST3 plugins on Linux (JUCE-built ones in
particular: most of the registry) don't spin their own message thread. They register timers and fds with the host
through `Steinberg::Linux::IRunLoop`, which JUCE's host implements on the same never-pumped loop. Their
`AsyncUpdater`/`Timer`/`callAsync` work never fires: deferred init, patch loading, `prepareToPlay`-follow-ups. The
plugin therefore processes silence even though the audio thread calls `processBlock`. It matches "any plugin" and
"Linux only". This is not tied to the registry. A locally installed VST3 should behave the same.

## Proposed Remediation

**Preferred**: add a pump to the C ABI and drive it from the UI thread on Linux only.
- Native: `extern "C" NOTA_API void nota_pluginhost_pump(void)`. It is a no-op unless `JUCE_LINUX` and
  `g_juceGui != nullptr`; otherwise it loops `juce::dispatchNextMessageOnSystemQueue(true)` until it returns false
  (with a small per-call cap so a chatty plugin can't starve Avalonia). Declare it in `nota_engine.h`.
- Managed: P/Invoke in `NativeMethods.PluginHost.cs` and a wrapper in `NotaEngine.PluginHost.cs`. In the app, on
  `OperatingSystem.IsLinux()`, start an Avalonia `DispatcherTimer` (~10 ms, `DispatcherPriority.Background` or
  higher) once the first plugin is instantiated (or at startup). That keeps JUCE on the same thread that already
  owns the `MessageManager` (instantiate/open/close all happen on the UI thread), so no threading model changes.
- Also Linux: drop the `[NSApp …]`-only foreground handling assumptions. Check that
  `setAlwaysOnTop`/`toFront` behave, and that computer-keyboard MIDI reaches the editor (on Windows JUCE key events
  reach it natively; on Linux they should too once events are dispatched).

**Alternatives**:
- *Dedicated JUCE message thread on Linux* (what JUCE plugin wrappers do: `MessageThread` +
  `runDispatchLoop`). No 10 ms timer latency, but every `instantiate`/`openEditor`/`clone`/state call must be
  marshalled to that thread, and Xlib must be thread-safe (`XInitThreads`) with two X connections in one process.
  More invasive; consider it only if timer pumping proves janky.
- *Hook JUCE's fds into Avalonia's loop*. Avalonia's X11 platform has no public fd-watch API; brittle.

**Files likely to change**:
- `src/native/nota.engine/pluginhost/PluginHost.cpp`
- `src/native/nota.engine/include/nota/nota_engine.h` (new export)
- `src/managed/Nota.Infrastructure/Interop/NativeMethods.PluginHost.cs`
- `src/managed/Nota.Infrastructure/Interop/NotaEngine.PluginHost.cs`
- `src/managed/Nota.App/…` (where the engine/plugin host is wired up, e.g. `MainWindow.axaml.cs`)
- `CHANGELOG.md` ([Unreleased] → Fixed)

**Tests to add or update**:
- Smoke/native test (Linux CI): instantiate the in-tree `NotaPentad` test plugin, call `nota_pluginhost_pump`
  (must not crash and must return even with no display activity), render a note and assert non-silent output.
- Managed unit test that the pump timer is only started on Linux (or that the call is a safe no-op elsewhere).
- Manual Linux check: Surge XT (or another registry plugin) editor paints, responds to mouse, and notes sound.

## Risks & Considerations

- Re-entrancy: dispatching JUCE messages inside an Avalonia timer tick could re-enter Nota through plugin callbacks
  (`audioProcessorParameterChanged`, host-notify). Those already only touch atomics. Keep it that way.
- Headless/CI Linux with no `DISPLAY`: `ScopedJuceInitialiser_GUI` and the pump must stay safe. The pump must be a
  no-op before GUI init.
- Wayland sessions: JUCE is X11-only and needs XWayland. Worth a clear message if `DISPLAY` is unset.
- Two Xlib connections in one process (Avalonia's + JUCE's), on the same thread with the timer approach. That is
  fine; it gets riskier with the dedicated-thread alternative.
- Audio thread is untouched. No allocation/locking added there (CONTRIBUTING audio-thread rules).

## Clarifications (2026-10-14, reporter)

- **Silent even when the editor is never opened.** So the silence isn't a side effect of a frozen editor. It is
  consistent with plugin init/async work waiting on the never-pumped loop (the IRunLoop half of the hypothesis).
  `/speckit-bug-test` must still confirm audio once the pump lands. If it stays silent, look at the audio path next
  (bus layout / `pluginChannelPtrs`, `prepareToPlay`).
- **The editor window can't be closed**, only by quitting Nota. That fits the root cause: the close-button
  ClientMessage (`WM_DELETE_WINDOW`) is an X event JUCE never dispatches, so `closeButtonPressed()` never runs.
  The black window, the dead close button and the silence all have one cause.
- **Wayland session, any distro.** JUCE is X11-only, so it runs through XWayland while Avalonia may be on native
  Wayland or XWayland. The pump fix applies either way. Verify on a Wayland session (GNOME/KDE) specifically.
- Non-registry plugin: not checked. Expected to behave the same, because nothing in the hosting path depends on
  where the plugin came from.

## Open Questions

- [NEEDS CLARIFICATION: Nota version. Assumed current release / `develop`.]
