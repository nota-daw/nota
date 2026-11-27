<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Contract: Object-Oriented C++20 `nota::Engine` API (`modules/engine/include/nota/engine/`)

## Design Principles
1. Every nested domain class (`Engine::Transport`, `Engine::Mixer`, `Engine::Track`, `Engine::TrackCollection`, `Engine::Clip`, `Engine::Device`, `Engine::DeviceChain`, `Engine::Parameter`, `Engine::Rack`, `Engine::RackChain`, `Engine::Macro`, `Engine::Session`, `Engine::Scene`, `Engine::Slot`, `Engine::Recorder`, `Engine::AutomationLane`, `Engine::Modulator`, `Engine::CvLink`, `Engine::Preview`, `Engine::AudioIo`, `Engine::MidiIo`, `Engine::GamepadIo`, `Engine::History`, `Engine::Diagnostics`) is forward-declared inside `Engine.h` and defined in its own <180-line self-contained header under `include/nota/engine/` (+ umbrella `<nota/engine/NotaEngine.h>`).
2. No third-party headers (`dr_*`, `FLAC`, `lame`, `juce`, `miniaudio`, `rtmidi`, `signalsmith`, `nlohmann`, OS frameworks) appear in any public header.
3. Entity handles (`Track`, `Clip`, `Device`, `Parameter`, `Rack`, `Scene`, `Slot`, `AutomationLane`, `Modulator`) hold `std::weak_ptr<Engine::Impl>` + stable IDs, provide `bool isValid() const noexcept` + `explicit operator bool() const noexcept`, and safely become no-ops when the referenced entity no longer exists.
4. Extensibility via registries (`DeviceRegistry`, `AudioDriverRegistry`, `MidiDriverRegistry`) means new built-in synths/effects/drivers work automatically via `createInstrument(slug)` / `addBuiltin(slug)` / `selectDriver(driverId)` without adding per-device methods to `Engine`.
