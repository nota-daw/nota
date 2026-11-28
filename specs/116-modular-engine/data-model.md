<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Data Model: Modular Native Engine (`specs/116-modular-engine`)

## 1. Math Entities (`nota::math`)
- **`Vec<T, Lanes>`** (`Vec4f`, `Vec4i`, `Vec4u`): Fixed-width SIMD register abstraction over NEON / SSE2 / AVX2 / scalar fallback with uniform load/store/FMA/clamp/squashNonFinite and stereo interleave/deinterleave helpers.
- **`CpuFeatures`**: Detected runtime & compile-time SIMD/FMA capabilities (`neon`, `sse2`, `avx2`, `fma`).
- **`RealFft`**: Pimpl real-to-complex / complex-to-real FFT wrapper encapsulating `Accelerate` (macOS) or `pffft` (Linux/Windows).

## 2. Media Entities (`nota::media`)
- **`AudioStreamInfo`**: `{ channels, sampleRate, totalFrames, bitsPerSample, isSeekable, formatId }`.
- **`AudioEncodeSpec`**: `{ formatId, sampleRate, channels, bitsPerSample, bitrateKbps, dither, compressionLevel }`.
- **`AudioFormatCapabilities`**: `{ formatId, displayName, extensions, canDecode, canEncode, supportsSeeking, supportsStreaming }`.
- **`IAudioDecoder` / `IAudioEncoder` / `AudioCodecRegistry`**: Factory/probe registry mapping paths and format IDs cleanly to codec implementations (`Wav`, `Flac`, `Mp3`).

## 3. DSP & Plug-in Contracts (`nota::dsp`)
- **`DeviceCategory` & `DeviceDescriptor`**: `{ kind, slug, displayName, category, capabilities (Rack|Sidechain|MidiKey|Mpe|ChainAllowed|CreatesTrack), supportsParallelRender }`.
- **`DeviceRegistry`**: Thread-safe singleton registry storing descriptors, factories, defaults, cloning helpers, and the injected `std::shared_ptr<IPluginHost>`.
- **`PluginFormatKind` & `PluginFormatDescriptor`**: `{ formatId, displayName, kind (Vst3|AudioUnit|Clap|Lv2|Vst2|Custom), priority, usesFilesystemPaths, supportsOutOfProcessScan, bundleExtensions }`.
- **`PluginDescriptor`**: `{ uniqueId, legacyIdentifier, formatId, name, descriptiveName, manufacturer, version, category, fileOrIdentifier, moduleTimestampMs, isInstrument, numInputChannels, numOutputChannels, hasSharedAudioMidi, backendPayload }`.
- **`IPluginCatalog`, `IPluginScanner`, `IPluginHost`**: Pure C++20 interfaces free of JUCE types.

## 4. Hardware I/O Entities (`nota::io`)
- **`nota::io::audio`**:
  - **`AudioDriverCapabilities`**: `{ driverId, displayName, roles, supportsExclusiveMode, supportsControlPanel, supportsSharedDuplexClock, hasPerChannelNames }`.
  - **`AudioDeviceDescriptor`**: `{ uid, name, displayName, driverId, maxOutputChannels, maxInputChannels, isDefaultOutput, isDefaultInput, isBluetooth, isDuplex, supportedSampleRates, outputChannels, inputChannels }`.
  - **`AudioStreamConfig` / `AudioStreamState`**: Selected driver/devices/rate/buffer/routing and active fallback status + hardware latencies + `ioWorkgroup`.
  - **`IAudioDriver`, `IAudioOutputStream`, `IAudioInputStream`, `AudioDriverRegistry`**.
- **`nota::io::midi`**: `MidiPortInfo`, `MidiConfig`, `IMidiDriver`, `IMidiInput`, `IMidiOutput`, `MidiDriverRegistry`, `MidiParser`, `MpeInput`.
- **`nota::io::gamepad`**: `GamepadButtonEvent`, `GamepadDeviceInfo`, `IGamepadDriver`, `GamepadInput`.

## 5. OOP Engine Handles (`nota::engine`)
- **`Engine` (root Pimpl)** -> creates/returns lightweight copyable views/handles holding `std::weak_ptr<Engine::Impl>` + entity ID (`trackId`, `clipId`, `deviceIndex`, `paramIndex`, `rackChainId`, `sceneIndex`):
  - `Engine::Transport`, `Engine::Mixer`, `Engine::TrackCollection`, `Engine::Track`, `Engine::Clip`, `Engine::DeviceChain`, `Engine::Device`, `Engine::Parameter`, `Engine::Rack`, `Engine::RackChain`, `Engine::Macro`, `Engine::Session`, `Engine::Scene`, `Engine::Slot`, `Engine::Recorder`, `Engine::AutomationLane`, `Engine::Modulator`, `Engine::CvLink`, `Engine::Preview`, `Engine::AudioIo`, `Engine::MidiIo`, `Engine::GamepadIo`, `Engine::History`, `Engine::Diagnostics`.
- **Validity state transition**:
  - `Created / Alive`: Handle points to live graph/model object -> methods execute commands or read state.
  - `Invalidated / Removed`: Target track/clip/device removed (or `Engine` destroyed) -> `isValid() == false`, mutators no-op returning `false`/empty handle.
