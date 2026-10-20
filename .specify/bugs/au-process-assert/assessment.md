# Bug Assessment: Audio Unit plug-ins get 31 channels per block — JUCE assertion and dropouts

- **Slug**: au-process-assert
- **Created**: 2026-10-20
- **Source**: pasted text (console output from the user's debug run on macOS)
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

Debug build on macOS. Opening a project that hosts an Audio Unit plug-in prints:

```
JUCE v8.0.14
3985
JUCE Assertion failure in juce_AudioUnitPluginFormatImpl.h:1346
```

Pressing play then floods the console, once per audio block (~33 ms apart):

```
JUCE Assertion failure in juce_AudioUnitPluginFormatImpl.h:1346
[xrun] audio dropout #127 at 2026-10-20T10:01:26.2563380+03:00
2
JUCE Assertion failure in juce_AudioUnitPluginFormatImpl.h:1346
[xrun] audio dropout #128 at 2026-10-20T10:01:26.2902800+03:00
...
[xrun] audio dropout #219 at 2026-10-20T10:01:31.3891860+03:00
```

The bare numbers between lines (`3985`, `2`, `4`, `42`, `1806`) are an event count, according to the user. They are not printed by
Nota's code. On quit, JUCE's `LeakedObjectDetector` reports `VST3PluginFormatHeadless`, `AudioUnitPluginFormatHeadless`,
2 × `AudioPluginFormat`, `MessageManager`, and 84 × `PluginDescription`.

## Symptom

Every `processBlock` of a hosted AU fails JUCE's `buffer.getNumChannels() <= preparedChannels` check. In the debug build, each
failure goes with an audio dropout, so playback stutters on every block. Expected: AU plug-ins get a buffer that fits the layout
they were prepared for, with no assertion and no dropouts.

## Reproduction

1. macOS, debug build of the engine (assertions enabled).
2. Open a project with any Audio Unit instrument or effect on a track. The assertion is printed already on load.
3. Press play. The assertion is printed once per block, each time followed by `[xrun] audio dropout #N`.

[NEEDS CLARIFICATION: which AU the project uses, and whether the app was started under a debugger. With a debugger attached,
`jassertfalse` runs `JUCE_BREAK_IN_DEBUGGER` (`kill(0, SIGTRAP)`) on every block, which would explain a dropout per block on its own.]

## Suspected Code Paths

- `src/native/nota.engine/pluginhost/PluginHost.cpp:400` — `kNoAllocChans = 31`.
- `src/native/nota.engine/pluginhost/PluginHost.cpp:409` `pluginChannelPtrs()` — returns
  `max(kNoAllocChans, storage, plugin in, plugin out)`. Every plug-in, of any format, gets ≥ 31 channels.
- `src/native/nota.engine/pluginhost/PluginHost.cpp:742` `PluginInstrument::render` and `:~911` `PluginEffect::process` — wrap
  those pointers in a `juce::AudioBuffer` and call `plugin_->processBlock`.
- `vendor/JUCE/modules/juce_audio_processors_headless/format_types/juce_AudioUnitPluginFormatImpl.h:1302` —
  `preparedChannels = jmax(totalIn, totalOut)` in `prepareToPlay` (2 for a stereo plug-in).
- Same file, `:1346` — `jassert(buffer.getNumChannels() <= preparedChannels)`, followed by
  `inputBuffer.makeCopyOf(buffer, true)`. This resizes `inputBuffer` to 31 channels (an allocation on the audio thread on the
  first block) and copies 31 channels on every block after that.

## Root Cause Hypothesis

Commit 06057f89 (bug `plugin-add-crash-windows`, in releases since v0.52.1) made `pluginChannelPtrs()` always hand a plug-in at
least 31 channels. The headroom is there for VST3: JUCE's VST3 wrapper maps buses straight from `buffer.getWritePointer(ch)`, and
a VST3 plug-in that widens its layout at runtime would otherwise get null channels. The AU wrapper works differently. It copies
the incoming buffer into its own `inputBuffer`, which was sized at `prepareToPlay`, and asserts when the host buffer is wider.
The 31-channel buffer therefore fails the assertion on every block. The same path also does one audio-thread allocation and an
extra 31-channel copy per block in release builds, where the assertion itself is compiled out. A dropout on every block in the
debug run is consistent with the per-block assertion: logging plus `JUCE_BREAK_IN_DEBUGGER` when a debugger is attached.
Confidence: **high** for the assertion. **Medium** that the assertion alone causes the dropouts; the per-block copy and the
debug build also cost CPU.

## Proposed Remediation

**Preferred**: apply the channel headroom only to formats that need it. At prepare time (`PluginInstrument::prepare`,
`PluginEffect::setSampleRate`, both on the message thread), cache the minimum width for the instance: `kNoAllocChans` when
`getPluginDescription().pluginFormatName == "VST3"`, 0 otherwise. `pluginChannelPtrs()` takes that minimum and returns
`max(minChans, plugin in, plugin out)`, at least 1, capped at `kMaxChans`. For non-VST3 plug-ins it no longer adds
`storage.getNumChannels()`. `storage_` is padded to ≥ 2 channels, so a mono AU (prepared for 1) would still get 2 and fail the
assertion. The callers already read channel 1 only when `stereoOut_` / `stereoIn_`, so a 1-channel buffer is safe for them. An AU
that changes its layout is re-prepared by JUCE on its own path, and reading `getTotalNum*Channels()` each block keeps the width
in step with that.

**Alternatives**:
- Keep the 31-channel buffer for all formats and only cut the AU's width back to `preparedChannels`. This is not reachable from
  outside the wrapper, so in practice it is the same per-format branch.
- Remove the headroom entirely and resize on `audioProcessorChanged`. That needs a lock-free buffer swap; out of scope (already
  listed as a follow-up of `plugin-add-crash-windows`).

**Files likely to change**:
- `src/native/nota.engine/pluginhost/PluginHost.cpp`
- `tests/Nota.SmokeTest/Program.cs` (if the assertion check below is added)

**Tests to add or update**:
- The smoke test already hosts an AU instrument and an AU effect (`Program.cs:475`). In a debug build, check that they render
  without assertions. One way: run the smoke test and fail if stderr contains `juce_AudioUnitPluginFormatImpl.h`. Otherwise,
  verify by hand with Apple's built-in AUs (`DLSMusicDevice`, `AUDelay`), which are on every Mac.
- Regression guard for 06057f89: the Reaktor 6 VST3 layout-widening harness (`/tmp/widenharness`, see
  `plugin-add-crash-windows/fix.md`) should still render without SIGSEGV.

## Risks & Considerations

- VST3 behaviour must stay exactly as it is (the headroom fixes a real crash). Gate it by format name and keep the code path
  unchanged for VST3.
- An AU whose layout grows between `getTotalNum*Channels()` and `processBlock` gets a buffer narrower than its new layout. The AU
  wrapper copies by channel count (`makeCopyOf`) and maps buses through `inMapping`/`outMapping`, which were set up at prepare.
  It does not dereference null channels the way the VST3 wrapper did. Worth confirming during the fix.
- The leak reports on quit are a separate, debug-only issue: `formatManager()`, `MessageManager` and the plug-in list are not torn
  down before JUCE's static leak detectors run. They are not caused by this bug; track them separately if they matter.

## Open Questions

- [NEEDS CLARIFICATION: which AU plug-in(s) the project hosts, and whether the dropouts also happen in a release build.]
- [NEEDS CLARIFICATION: was the app running under a debugger (e.g. netcoredbg)?]
