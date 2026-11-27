<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Contract: Central Registries & Hardware/Plugin Abstractions

## 1. Audio Codecs (`nota::media`)
- `IAudioDecoder`: `open(utf8Path, outInfo)`, `readFrames(interleavedOut, frames)`, `seekFrame(frame)`, `close()`, `info()`.
- `IAudioEncoder`: `open(utf8Path, spec)`, `writeFrames(interleavedIn, frames)`, `finish()`, `discard()`.
- `AudioCodecRegistry::instance()`: `registerDecoder(...)`, `registerEncoder(...)`, `openDecoder(...)`, `openEncoder(...)`, `probeHeader(...)`, `canDecodePath(...)`, `canEncodeFormat(...)`, `listFormats()`.

## 2. Built-in Devices (`nota::dsp` + `nota::devices`)
- `DeviceRegistry::instance()`: `registerEffect`, `registerInstrument`, `registerMidiEffect`, unified `createEffect`/`createChainEffect`/`createInstrument`/`createMidiEffect` by `int32_t kind` OR `std::string_view slug`, `clone*`, `default*Param`, `setPluginHost`/`pluginHost`, `list(category)`.
- Every built-in effect, instrument, MIDI FX, and rack registers from `nota_devices` via `registerBuiltinDevices()` and lives in its own subdirectory.

## 3. Format-Agnostic Plug-in Scanner (`nota::dsp` + `nota::pluginscanner`)
- `nota::scanner::IPluginFormatBackend`: `descriptor()`, `defaultSearchPaths()`, `discoverCandidates()`, `candidateExists()`, `candidateTimestampMs()`, `candidateDisplayName()`, `probeInWorker()`.
- `nota::scanner::PluginFormatRegistry`: `registerBackend()`, `find(formatId)`, `orderedBackends()`, `listFormats()`.
- `ScanWireCodec`: Format-neutral worker stdout framing (`<<<NOTA_PLUGINS_BEGIN>>>...<<<NOTA_PLUGINS_END>>>`) serializing `nota::dsp::PluginDescriptor` with opaque `backendPayload` so JUCE (`VST3`/`AudioUnit` today) and future `CLAP` / `LV2` / JUCE-independent `VST2` backends plug into one worker executable (`nota-scanworker`) and catalog (`PluginCatalogImpl` preserving lossless `plugins.xml` compatibility).

## 4. Hardware I/O (`nota::io`)
- **`nota::io::audio`**: `IAudioDriver`, `IAudioOutputStream` (incl. `createCoupledInputStream()` replacing global `AsioSession`), `IAudioInputStream`, `AudioDriverRegistry`, `OutputRouter`, `AudioConfigStore` (`coreaudio`, `miniaudio`, `asio`, `null`).
- **`nota::io::midi`**: `IMidiDriver`, `IMidiInput`, `IMidiOutput`, `MidiDriverRegistry`, `MidiParser`, `MpeInput` (`coremidi`, `rtmidi`, `null`).
- **`nota::io::gamepad`**: `IGamepadDriver`, `GamepadInput` (`GameController` macOS / stub).
