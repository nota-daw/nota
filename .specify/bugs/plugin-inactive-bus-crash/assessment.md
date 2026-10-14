# Bug Assessment: Windows 0.52.1 — adding Vienna Synchron Pianos (VST3) crashes Nota on the first audio block

- **Slug**: plugin-inactive-bus-crash
- **Created**: 2026-10-14 (updated the same day after the reporter's answers)
- **Source**: pasted text (Visual Studio exception + call stack, no symbols) + reporter's follow-up
- **Verdict**: likely valid, needs reproduction
- **Severity**: high

## Report (verbatim or summarized)

> Вызвано исключение по адресу 0x00007FFC13B1E6B0 (vcruntime140.dll) в Nota.App.exe: 0xC0000005: нарушение прав
> доступа при записи по адресу 0x0000000000000020.

```
vcruntime140.dll!00007ffc13b1e6b0()
Vienna Synchron Pianos.dll!000000004309de6e()
Vienna Synchron Pianos.dll!000000004309cad4()
Vienna Synchron Pianos.dll!0000000042cb7695()
nota_engine.dll!00007ffb8a0080cb()   … 12 nota_engine frames, no symbols …
nota_engine.dll!00007ffb89f83d5a()
kernel32.dll!00007ffc2a0a7374()      (BaseThreadInitThunk)
ntdll.dll!00007ffc2bdbcc91()         (RtlUserThreadStart)
```

Follow-up from the reporter: **version 0.52.1**; the crash happens when they **just add the plugin to a track**.

The thread was created inside `nota_engine.dll`. On Windows that's the miniaudio WASAPI thread
(`MiniaudioBackend.cpp`); the warp-build worker (`Engine_Tracks.cpp:438`) never touches plugins. So it goes
audio callback → `Engine::render` → graph → `PluginInstrument::render` → JUCE VST3 `processBlock` → the plugin's
`process()`, which `memcpy`/`memset`s through a null-based pointer.

The vcruntime fault address and "write 0x20" are byte-identical to the second exception in
`.specify/bugs/plugin-add-crash-windows` (DecentSampler, 0.51.2). It's the same machine and the same fault, and it
survived that bug's fix (`06057f8`, in 0.52.1).

## Symptom

On Windows, Nota 0.52.1 crashes with an access violation as soon as Vienna Synchron Pianos (VST3) is added to a
track. The plugin gets its first `process()` call and writes through a null pointer. Expected: the plugin loads
and plays.

## Reproduction

1. Windows x64, Nota 0.52.1.
2. Install Vienna Synchron Pianos (VST3) and scan plug-ins.
3. Add it to a track (new instrument track or replace the instrument).
4. Crash on the first audio block.

[NEEDS CLARIFICATION: Synchron Pianos version / which piano; does it crash with the transport stopped too, without
playing a note?]

## Suspected Code Paths

- `src/native/nota.engine/vendor/JUCE/modules/juce_audio_processors_headless/format_types/juce_VST3Common.h:1057-1065`
  `HostBufferMapper::associateBufferTo` — for an **inactive** bus JUCE sets `numChannels = N`, every channel
  pointer = `nullptr`, `silenceFlags = all`. Our `pluginChannelPtrs()` headroom (0.52.1) doesn't help here: JUCE
  never asks our buffer for those channels.
- `src/native/nota.engine/pluginhost/PluginHost.cpp:766` `PluginInstrument::prepare()` —
  `setPlayConfigDetails(0, 2, …)` + `prepareToPlay`. It never activates the plugin's non-main buses, so a
  multi-output instrument runs with its aux output buses inactive and receives null channel pointers for them in
  every `process()` call. Same for `PluginEffect::prepare` (`PluginHost.cpp:~815`).
- `src/native/nota.engine/pluginhost/PluginHost.cpp:948` `instantiate()` — JUCE creates the VST3 with the
  plugin's default bus activation (`kDefaultActive`), so the aux outputs start inactive.
- `src/native/nota.engine/src/Engine.h:1520` `kMaxBlock = 16384` — every plugin is prepared with
  `maxSamplesPerBlock = 16384`. That's legal but unusual (DAWs typically pass ≤ 4096–8192). A plugin that sizes an
  internal pool from it and fails or caps the allocation could write through a null buffer. A secondary suspect.
- Ruled out for this report: the bus-widening null pointer (fixed in 0.52.1); the `Engine::start()` re-prepare race
  (see below; adding a plugin doesn't call `start()`). The plugin host code is essentially unchanged since
  0.50.1, and JUCE is unchanged.

## Root Cause Hypothesis

**Medium confidence.** Vienna's Synchron player is a multi-output instrument: its VST3 exposes extra stereo
output buses (mic positions / output routing) that aren't active by default. Nota never activates them, and JUCE
passes `nullptr` channel pointers for inactive buses with `silenceFlags` set. Many plugins check the flags or the
pointers (Reaktor 6: 1 active + 7 inactive stereo outs, works). Synchron Pianos apparently writes or clears every
output bus unconditionally, so its first `process()` call does a CRT `memset`/`memcpy` into a null channel and
faults. On MSVC's vectorized `memset`, the first faulting store can land at dst+0x20; an 8-sample offset into the
channel is also consistent with 0x20. It's deterministic on the first block, which matches "crashes as soon as
I add it".

Most real hosts (Cubase, Reaper, Live) give inactive buses valid scratch buffers, which is why the plugin gets
away with it elsewhere. That's the gap to close.

Unconfirmed: that Synchron Pianos actually exposes inactive aux buses. That has to be checked, either by logging
the bus layout at insert or by loading it in JUCE's AudioPluginHost.

### Also found (separate latent bug, not this crash)

`Engine::start()` (`src/Engine.cpp:54-66`) starts the audio callback *before* `propagateSampleRate()` re-prepares
every live plugin (`prepareToPlay` + `storage_.setSize`) from the UI thread. After export
(`AudioExporter.cs:343`), freeze/bounce (`TrackFreezer.cs:129`) or Preferences → Audio Apply, the audio thread can
be in `processBlock` while the plugin is being deactivated, reconfigured and reactivated. That's a real race with
the same crash signature, but it doesn't trigger on a plain insert. Track it separately.

## Proposed Remediation

**Preferred**: never let a hosted plugin run with null bus buffers. In `PluginInstrument::prepare()` and
`PluginEffect::prepare()`, call `plugin_->enableAllBuses()` before `prepareToPlay`. Replace
`setPlayConfigDetails(0, 2, …)` with `setRateAndBufferSizeDetails(sr, maxBlock_)`; forcing 0-in/2-out is what
fights the plugin's layout. `storage_` is already sized from the totals, and `render()` still mixes only channels
0/1, so the aux outputs render and are discarded. That's the same thing other hosts do with outputs nobody
routed. If the plugin rejects a bus (`enableAllBuses` → `setBusesLayout` fails), fall back to the current layout.

Ship it with a one-time diagnostic: log each bus (`name`, channels, active) on insert, behind the existing stderr
logging. That makes the next report self-explanatory.

**Alternatives**:
- Patch JUCE's `associateBufferTo` to point inactive-bus channels at a host scratch buffer instead of `nullptr`.
  It keeps aux buses inactive (no extra CPU) and protects every plugin, but it's a vendor patch: check the
  dependency policy in `CONTRIBUTING.md`, and every JUCE bump would have to carry it.
- Lower `kMaxBlock` handed to plugins (e.g. 4096) and split larger offline chunks. Do this if the bus log shows
  only main buses (the secondary suspect).
- Out-of-process plugin hosting: the long-term answer to any plugin crash.

**Files likely to change**:
- `src/native/nota.engine/pluginhost/PluginHost.cpp` (`PluginInstrument::prepare`, `PluginEffect::prepare`, bus log)
- `CHANGELOG.md` (`[Unreleased]` → Fixed)

**Tests to add or update**:
- Local (macOS): Reaktor 6 VST3, a multi-out instrument with inactive aux buses by default. After the fix its
  layout reports all buses active, it renders 200 blocks, and the main stereo output is unchanged.
- Smoke test: if a hosted test plugin is available, assert after insert that no bus is inactive
  (`getBusCount`/`getBus(i)->isEnabled()`).
- Windows retest by the reporter with Synchron Pianos on a build with the fix and the bus log.

## Risks & Considerations

- Enabling all buses costs CPU for multi-out plugins (they render outputs that are then discarded). Acceptable;
  a later "multi-out routing" feature would make them useful.
- Effects with a sidechain input bus would get it activated. `PluginEffect` already handles sidechain channel
  offsets (`scChanOffset_`), so check that enabling it doesn't conflict when no sidechain is routed (the input
  must be silent).
- Saved project state: bus activation isn't part of the plugin's state chunk in JUCE VST3 hosting, so it doesn't
  affect projects.
- If the bus log shows Synchron Pianos has only a main bus, this hypothesis is wrong. Fall back to the `kMaxBlock`
  suspect and ask for a full dump.

## Open Questions

- [NEEDS CLARIFICATION: Synchron Pianos version; does it crash with the transport stopped and no note played?]
- [NEEDS CLARIFICATION: does the same project/track work in another host (Reaper/Cubase) on that machine?]
- [NEEDS CLARIFICATION: a full dump (`procdump -e -ma Nota.App.exe`) would name the 12 `nota_engine` frames with a
  local RelWithDebInfo build.]
