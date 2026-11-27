// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — device settings: audio output/input device / sample-rate / buffer
// (M7-1), MIDI input/MPE/surface (M7-2 / specs/051), and gamepad input.

#include "nota/nota_engine.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Engine.h"
#include "nota_engine_internal.h"

#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/io/audio/AudioDriverRegistry.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/asio/AsioDriverList.h"
#include "nota/io/audio/asio/AsioSampleFormat.h"
#include "nota/io/gamepad/GamepadTypes.h"
#include "nota/io/midi/MidiDriverRegistry.h"
#include "nota/io/midi/MidiTypes.h"
#include "nota/io/midi/MpeInput.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace {

std::vector<nota::io::audio::AudioDeviceInfo> g_outputDevices;
std::vector<nota::io::audio::AudioDeviceInfo> g_inputDevices;
std::vector<nota::io::audio::asio::AsioDriverInfo> g_asioDrivers;
std::vector<nota::io::midi::MidiDeviceInfo> g_midiInputs;

constexpr size_t kStackEventLimit = 64;

const char* deviceField(const std::vector<nota::io::audio::AudioDeviceInfo>& list, int32_t i, bool uid) {
    if (i < 0 || i >= static_cast<int32_t>(list.size())) return nullptr;
    const auto& d = list[static_cast<size_t>(i)];
    return uid ? d.uid.c_str() : d.name.c_str();
}

const char* midiField(int32_t i, bool uid) {
    if (i < 0 || i >= static_cast<int32_t>(g_midiInputs.size())) return nullptr;
    const auto& m = g_midiInputs[static_cast<size_t>(i)];
    return uid ? m.uid.c_str() : m.name.c_str();
}

const nota::engine::MidiPortRow* midiPortRow(const NotaEngine* e, int32_t i) {
    if (!e || i < 0) return nullptr;
    if (e->midiPortsCache.empty()) e->midiPortsCache = nota::c_abi::ceng(e).midi().portRows();
    return static_cast<size_t>(i) < e->midiPortsCache.size() ? &e->midiPortsCache[static_cast<size_t>(i)] : nullptr;
}

} // namespace

extern "C" {

// ---- Audio device settings (M7-1) ------------------------------------------

void nota_audio_refresh_devices(void) {
    g_outputDevices = nota::io::audio::enumerateAudioDevices(/*inputScope=*/false);
    g_inputDevices = nota::io::audio::enumerateAudioDevices(/*inputScope=*/true);
    g_asioDrivers = nota::io::audio::asio::enumerateAsioDrivers();
}

int32_t nota_audio_output_device_count(void) {
    return static_cast<int32_t>(g_outputDevices.size());
}

int32_t nota_audio_input_device_count(void) {
    return static_cast<int32_t>(g_inputDevices.size());
}

const char* nota_audio_output_device_uid(int32_t i) {
    return deviceField(g_outputDevices, i, true);
}

const char* nota_audio_output_device_name(int32_t i) {
    return deviceField(g_outputDevices, i, false);
}

const char* nota_audio_input_device_uid(int32_t i) {
    return deviceField(g_inputDevices, i, true);
}

const char* nota_audio_input_device_name(int32_t i) {
    return deviceField(g_inputDevices, i, false);
}

int32_t nota_audio_effective_output_uid(const NotaEngine* e, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::io::audio::effectiveAudioDeviceUid(nota::c_abi::ceng(e).audio().config().outputDeviceUid, false), out,
        cap);
}

int32_t nota_audio_effective_input_uid(const NotaEngine* e, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::io::audio::effectiveAudioDeviceUid(nota::c_abi::ceng(e).audio().config().inputDeviceUid, true), out, cap);
}

NotaResult nota_audio_set_output_device(NotaEngine* e, const char* uid) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).audio().setOutputDevice(uid ? std::string(uid) : std::string{});
    return NOTA_OK;
}

NotaResult nota_audio_set_input_device(NotaEngine* e, const char* uid) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).audio().setInputDevice(uid ? std::string(uid) : std::string{});
    return NOTA_OK;
}

NotaResult nota_audio_set_sample_rate(NotaEngine* e, double sr) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).audio().setSampleRate(sr);
    return NOTA_OK;
}

NotaResult nota_audio_set_buffer_frames(NotaEngine* e, int32_t frames) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).audio().setBufferFrames(frames);
    return NOTA_OK;
}

NotaResult nota_audio_set_wasapi_exclusive(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).audio().setWasapiExclusive(enabled != 0);
    return NOTA_OK;
}

int32_t nota_audio_wasapi_exclusive(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).audio().config().wasapiExclusive) ? 1 : 0;
}

const char* nota_audio_output_device(const NotaEngine* e) {
    return nota::c_abi::cacheThreadString(e ? nota::c_abi::ceng(e).audio().config().outputDeviceUid : std::string{});
}

const char* nota_audio_input_device(const NotaEngine* e) {
    return nota::c_abi::cacheThreadString(e ? nota::c_abi::ceng(e).audio().config().inputDeviceUid : std::string{});
}

double nota_audio_sample_rate(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().config().sampleRate : 0.0;
}

int32_t nota_audio_buffer_frames(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().config().bufferFrames : 0;
}

NotaResult nota_audio_apply(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).audio().applyConfig() ? NOTA_OK : NOTA_ERR_AUDIO_DEVICE;
}

double nota_audio_negotiated_sample_rate(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().sampleRate() : 0.0;
}

int32_t nota_audio_negotiated_buffer_frames(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().bufferFrames() : 0;
}

int32_t nota_audio_exclusive_fallback(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).audio().exclusiveFallback()) ? 1 : 0;
}

int32_t nota_audio_silent_fallback(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).audio().silentFallback()) ? 1 : 0;
}

// ---- ASIO (Windows; specs/049) ---------------------------------------------

NotaResult nota_audio_set_system(NotaEngine* e, int32_t system) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).audio().setSystem(
        system == 1 ? nota::io::audio::AudioSystem::Asio : nota::io::audio::AudioSystem::Wasapi);
    return NOTA_OK;
}

int32_t nota_audio_system(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).audio().config().audioSystem == nota::io::audio::AudioSystem::Asio) ? 1 : 0;
}

int32_t nota_audio_active_system(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).audio().activeSystem() == nota::io::audio::AudioSystem::Asio) ? 1 : 0;
}

int32_t nota_audio_asio_available(void) {
    return nota::io::audio::asioSupported() ? 1 : 0;
}

int32_t nota_audio_asio_driver_count(void) {
    return static_cast<int32_t>(g_asioDrivers.size());
}

const char* nota_audio_asio_driver_id(int32_t i) {
    if (i >= 0 && static_cast<size_t>(i) < g_asioDrivers.size())
        return g_asioDrivers[static_cast<size_t>(i)].clsid.c_str();
    return nullptr;
}

const char* nota_audio_asio_driver_name(int32_t i) {
    if (i >= 0 && static_cast<size_t>(i) < g_asioDrivers.size())
        return g_asioDrivers[static_cast<size_t>(i)].displayName.c_str();
    return nullptr;
}

NotaResult nota_audio_set_asio_driver(NotaEngine* e, const char* id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    if (nota::io::audio::asioSupported()) {
        const std::string clsid = id ? id : "";
        if (g_asioDrivers.empty()) g_asioDrivers = nota::io::audio::asio::enumerateAsioDrivers();
        const auto* d = clsid.empty() ? nullptr : nota::io::audio::asio::findAsioDriver(g_asioDrivers, clsid, {});
        nota::c_abi::eng(e).audio().setAsioDriver(clsid, d ? d->name : std::string{});
    }
    return NOTA_OK;
}

const char* nota_audio_asio_driver(const NotaEngine* e) {
    return nota::c_abi::cacheThreadString(e ? nota::c_abi::ceng(e).audio().config().asioDriverClsid : std::string{});
}

int32_t nota_audio_asio_channel_count(const NotaEngine* e, int32_t is_input) {
    if (e && nota::c_abi::ceng(e).audio().activeSystem() == nota::io::audio::AudioSystem::Asio)
        return nota::c_abi::ceng(e).audio().driverChannelCount(is_input != 0);
    return 0;
}

const char* nota_audio_asio_channel_name(const NotaEngine* e, int32_t is_input, int32_t index) {
    if (e && nota::c_abi::ceng(e).audio().activeSystem() == nota::io::audio::AudioSystem::Asio) {
        std::string s = nota::c_abi::ceng(e).audio().driverChannelName(is_input != 0, index);
        if (!s.empty()) return nota::c_abi::cacheThreadString(std::move(s));
    }
    return nullptr;
}

NotaResult nota_audio_set_asio_pair(NotaEngine* e, int32_t is_input, int32_t first_channel) {
    if (!e || first_channel < 0) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).audio().setAsioPair(is_input != 0, first_channel);
    return NOTA_OK;
}

int32_t nota_audio_asio_pair(const NotaEngine* e, int32_t is_input) {
    if (!e) return 0;
    const auto cfg = nota::c_abi::ceng(e).audio().config();
    return is_input ? cfg.asioInputPair : cfg.asioOutputPair;
}

NotaResult nota_audio_asio_open_control_panel(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    if (nota::c_abi::ceng(e).audio().activeSystem() == nota::io::audio::AudioSystem::Asio &&
        nota::c_abi::eng(e).audio().openControlPanel())
        return NOTA_OK;
    return NOTA_ERR_AUDIO_DEVICE;
}

int32_t nota_audio_asio_panel_open(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).audio().activeSystem() == nota::io::audio::AudioSystem::Asio &&
               nota::c_abi::ceng(e).audio().isControlPanelOpen())
               ? 1
               : 0;
}

int32_t nota_audio_poll_device_event(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).audio().pollDeviceEvent() : 0;
}

const char* nota_audio_fallback_reason(const NotaEngine* e) {
    return nota::c_abi::cacheThreadString(e ? nota::c_abi::ceng(e).audio().fallbackReason() : std::string{});
}

int32_t nota_audio_input_latency_frames(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().inputLatencyFrames() : 0;
}

int32_t nota_audio_output_latency_frames(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).audio().outputLatencyFrames() : 0;
}

// ---- ASIO smoke self-check -------------------------------------------------

int32_t nota_asio_selftest(void) {
    using nota::io::audio::asio::SampleType;
    bool ok = true;
    {
        auto engine = std::make_unique<nota::Engine>();
        if (!engine->diagnostics().monitorSharedClockSelfTest()) {
            std::fprintf(stderr, "nota_asio_selftest: FAILED monitorSharedClockSelfTest\n");
            ok = false;
        }
    }
    if (!nota::io::audio::asio::isSupported(SampleType::Float32LSB) ||
        nota::io::audio::asio::isSupported(SampleType::Int32MSB)) {
        std::fprintf(stderr, "nota_asio_selftest: FAILED sample format support\n");
        ok = false;
    }
    const float src[4] = {-0.5f, 0.0f, 0.25f, 0.5f};
    uint8_t raw[16]{};
    float back[4]{};
    nota::io::audio::asio::fromFloat(src, 1, SampleType::Int24LSB, raw, 4);
    nota::io::audio::asio::toFloat(raw, SampleType::Int24LSB, back, 4, 1);
    for (int i = 0; i < 4; ++i) {
        if (std::fabs(back[i] - src[i]) > 1e-4f) {
            std::fprintf(stderr, "nota_asio_selftest: FAILED Int24LSB round-trip\n");
            ok = false;
        }
    }
    return ok ? 1 : 0;
}

// ---- MIDI device settings (M7-2) -------------------------------------------

void nota_midi_refresh_devices(void) {
    g_midiInputs = nota::io::midi::enumerateMidiInputs();
}

int32_t nota_midi_input_device_count(void) {
    return static_cast<int32_t>(g_midiInputs.size());
}

const char* nota_midi_input_device_uid(int32_t i) {
    return midiField(i, true);
}

const char* nota_midi_input_device_name(int32_t i) {
    return midiField(i, false);
}

NotaResult nota_midi_set_input_enabled(NotaEngine* e, const char* uid, int32_t enabled) {
    if (!e || !uid) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).midi().setInputEnabled(std::string(uid), enabled != 0);
    return NOTA_OK;
}

int32_t nota_midi_input_enabled(const NotaEngine* e, const char* uid) {
    return (e && uid && nota::c_abi::ceng(e).midi().inputEnabled(std::string(uid))) ? 1 : 0;
}

NotaResult nota_midi_set_mpe(NotaEngine* e, int32_t enabled, int32_t bend_range) {
    if (!e || bend_range < 1 || bend_range > 96) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).midi().setMpe(enabled != 0, bend_range);
    return NOTA_OK;
}

int32_t nota_midi_mpe_enabled(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).midi().config().mpe) ? 1 : 0;
}

int32_t nota_midi_mpe_bend_range(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).midi().config().mpeBendRange : 48;
}

int32_t nota_mpe_selftest(void) {
    struct Ev {
        int kind;
        int32_t pitch;
        int32_t dim;
        float v;
    };
    struct Rec final : nota::io::midi::MpeInput::Sink {
        std::vector<Ev> ev;
        void mpeNoteOn(int32_t p, float v) override { ev.push_back({0, p, -1, v}); }
        void mpeNoteOff(int32_t p) override { ev.push_back({1, p, -1, 0.0f}); }
        void mpeExpression(int32_t p, int32_t d, float v) override { ev.push_back({2, p, d, v}); }
        void mpeController(int32_t c, float v) override {
            const int32_t d = c == nota::dsp::kCtlPitchWheel        ? 0
                              : c == nota::dsp::kCtlChannelPressure ? 1
                              : c == 74                             ? 2
                              : c == nota::dsp::kCtlSustain         ? 3
                                                                    : nota::dsp::kCtlDimBase + c;
            ev.push_back({2, -1, d, v});
        }
        bool has(int kind, int32_t p, int32_t d, float v) const {
            for (const auto& item : ev)
                if (item.kind == kind && item.pitch == p && item.dim == d && std::fabs(item.v - v) < 0.02f) return true;
            return false;
        }
    } r;
    nota::io::midi::MpeInput m;
    m.configure(true, 48);
    auto bend = [&](int ch, float x) {
        const int raw = std::clamp(static_cast<int>(std::lround(8192.0f + x * 8191.0f)), 0, 16383);
        m.message(0xE0, ch, raw & 0x7F, raw >> 7, r);
    };

    bend(1, 0.25f);
    m.message(0xD0, 1, 64, 0, r);
    m.message(0xB0, 1, 74, 100, r);
    r.ev.clear();
    m.message(0x90, 1, 60, 100, r);
    if (r.ev.empty() || r.ev[0].kind != 0 || !r.has(2, 60, 0, 12.0f) || !r.has(2, 60, 1, 64 / 127.0f) ||
        !r.has(2, 60, 2, 100 / 127.0f))
        return 1;

    r.ev.clear();
    bend(1, -0.5f);
    if (r.ev.size() != 1 || !r.has(2, 60, 0, -24.0f)) return 2;

    m.message(0x90, 2, 64, 100, r);
    r.ev.clear();
    bend(1, 0.0f);
    for (const auto& ev : r.ev)
        if (ev.pitch == 64) return 3;

    r.ev.clear();
    bend(0, 1.0f);
    m.message(0xD0, 0, 127, 0, r);
    if (!r.has(2, -1, 0, 1.0f) || !r.has(2, -1, 1, 1.0f)) return 4;

    m.message(0xB0, 3, 101, 0, r);
    m.message(0xB0, 3, 100, 0, r);
    m.message(0xB0, 3, 6, 24, r);
    if (m.bendRange(1) != 24 || m.bendRange(15) != 24 || m.bendRange(0) != 48) return 5;
    r.ev.clear();
    bend(1, 0.5f);
    if (!r.has(2, 60, 0, 12.0f)) return 5;

    m.message(0x80, 1, 60, 0, r);
    r.ev.clear();
    bend(1, 0.1f);
    for (const auto& ev : r.ev)
        if (ev.pitch == 60) return 6;

    m.message(0xB0, 0, 101, 0, r);
    m.message(0xB0, 0, 100, 6, r);
    m.message(0xB0, 0, 6, 3, r);
    if (m.lowerMembers() != 3) return 7;
    m.message(0x90, 5, 70, 100, r);
    r.ev.clear();
    bend(5, 1.0f);
    if (!r.has(2, -1, 0, 1.0f)) return 7;

    nota::io::midi::MpeInput off;
    off.configure(false, 48);
    r.ev.clear();
    off.message(0x90, 1, 60, 100, r);
    off.message(0xE0, 1, 16383 & 0x7F, 16383 >> 7, r);
    off.message(0xA0, 1, 60, 127, r);
    if (r.ev.size() != 3 || !r.has(2, -1, 0, 1.0f) || !r.has(2, 60, 1, 1.0f)) return 8;

    r.ev.clear();
    off.message(0xB0, 1, 1, 127, r);
    off.message(0xB0, 1, 11, 64, r);
    off.message(0xB0, 1, 64, 40, r);
    off.message(0xB0, 1, 123, 0, r);
    off.message(0xB0, 1, 38, 5, r);
    if (r.ev.size() != 3 || !r.has(2, -1, nota::dsp::kCtlDimBase + 1, 1.0f) ||
        !r.has(2, -1, nota::dsp::kCtlDimBase + 11, 64 / 127.0f) || !r.has(2, -1, 3, 40 / 127.0f))
        return 9;

    r.ev.clear();
    m.message(0xB0, 1, 1, 100, r);
    if (!r.ev.empty()) return 10;
    return 0;
}

NotaResult nota_midi_apply(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).midi().applyConfig() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

int32_t nota_midi_poll_control_events(NotaEngine* e, int32_t* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    std::array<nota::engine::ControlEventData, kStackEventLimit> stack{};
    std::vector<nota::engine::ControlEventData> heap;
    nota::engine::ControlEventData* dst = nullptr;
    if (static_cast<size_t>(max) <= stack.size()) {
        dst = stack.data();
    } else {
        heap.resize(static_cast<size_t>(max));
        dst = heap.data();
    }
    const int32_t n = nota::c_abi::eng(e).midi().pollControlEvents(dst, max);
    for (int32_t i = 0; i < n; ++i) {
        out[i * 4 + 0] = dst[i].kind;
        out[i * 4 + 1] = dst[i].channel;
        out[i * 4 + 2] = dst[i].number;
        out[i * 4 + 3] = dst[i].value;
    }
    return n;
}

// ---- MIDI control surfaces (specs/051) -------------------------------------

uint32_t nota_midi_ports_generation(void) {
    return nota::io::midi::midiPortsGeneration();
}

int32_t nota_midi_port_count(NotaEngine* e) {
    if (!e) return 0;
    e->midiPortsCache = nota::c_abi::eng(e).midi().ports();
    return static_cast<int32_t>(e->midiPortsCache.size());
}

int32_t nota_midi_port_source(const NotaEngine* e, int32_t i) {
    const auto* p = midiPortRow(e, i);
    return p ? p->source : -1;
}

const char* nota_midi_port_uid(const NotaEngine* e, int32_t i) {
    const auto* p = midiPortRow(e, i);
    return p ? p->uid.c_str() : nullptr;
}

const char* nota_midi_port_name(const NotaEngine* e, int32_t i) {
    const auto* p = midiPortRow(e, i);
    return p ? p->name.c_str() : nullptr;
}

const char* nota_midi_port_output_uid(const NotaEngine* e, int32_t i) {
    const auto* p = midiPortRow(e, i);
    return p ? p->outputUid.c_str() : nullptr;
}

NotaResult nota_midi_set_claims(NotaEngine* e, const NotaMidiClaim* claims, int32_t count) {
    if (!e || count < 0 || (count > 0 && !claims)) return NOTA_ERR_INVALID_ARG;
    std::vector<nota::engine::MidiClaimSpec> v;
    v.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i)
        v.push_back({claims[i].source, claims[i].whole_port, claims[i].status, claims[i].channel, claims[i].data1});
    return nota::c_abi::eng(e).midi().setClaims(v.empty() ? nullptr : v.data(), count) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

void nota_midi_set_learn_listening(NotaEngine* e, int32_t listening) {
    if (e) nota::c_abi::eng(e).midi().setLearnListening(listening != 0);
}

int32_t nota_midi_poll_surface_events(NotaEngine* e, int32_t* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    std::array<nota::engine::SurfaceEventData, kStackEventLimit> stack{};
    std::vector<nota::engine::SurfaceEventData> heap;
    nota::engine::SurfaceEventData* dst = nullptr;
    if (static_cast<size_t>(max) <= stack.size()) {
        dst = stack.data();
    } else {
        heap.resize(static_cast<size_t>(max));
        dst = heap.data();
    }
    const int32_t n = nota::c_abi::eng(e).midi().pollSurfaceEvents(dst, max);
    for (int32_t i = 0; i < n; ++i) {
        out[i * 5 + 0] = dst[i].source;
        out[i * 5 + 1] = dst[i].status;
        out[i * 5 + 2] = dst[i].channel;
        out[i * 5 + 3] = dst[i].d1;
        out[i * 5 + 4] = dst[i].d2;
    }
    return n;
}

uint32_t nota_midi_surface_dropped(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).midi().surfaceDropped() : 0;
}

int32_t nota_midi_poll_sysex(NotaEngine* e, int32_t* out_source, uint8_t* out, int32_t cap) {
    return e ? nota::c_abi::eng(e).midi().pollSysex(out_source, out, cap) : 0;
}

NotaResult nota_midi_send(NotaEngine* e, const char* output_uid, const uint8_t* bytes, int32_t len) {
    if (!e || !output_uid || !bytes || len <= 0 || !(bytes[0] & 0x80)) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).midi().send(output_uid, bytes, len) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

int32_t nota_midi_surface_selftest(void) {
    return nota::Engine::Diagnostics::midiSurfaceSelfTest();
}

int32_t nota_midi_test_port_add(NotaEngine* e, const char* name) {
    return (e && name) ? nota::c_abi::eng(e).midi().addTestPort(name) : -1;
}

NotaResult nota_midi_test_inject(NotaEngine* e, int32_t source, const uint8_t* bytes, int32_t len) {
    if (!e || !bytes || len <= 0) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).midi().injectTestMidi(source, bytes, len) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_midi_test_take_sent(NotaEngine* e, int32_t source, uint8_t* out, int32_t cap) {
    return e ? nota::c_abi::eng(e).midi().takeTestMidiSent(source, out, cap) : 0;
}

// ---- Gamepad input ---------------------------------------------------------

void nota_gamepad_start(NotaEngine* e) {
    if (e) nota::c_abi::eng(e).gamepad().start();
}

void nota_gamepad_stop(NotaEngine* e) {
    if (e) nota::c_abi::eng(e).gamepad().stop();
}

int32_t nota_gamepad_count(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).gamepad().count() : 0;
}

const char* nota_gamepad_uid(NotaEngine* e, int32_t i) {
    const char* uid = nullptr;
    const char* name = nullptr;
    if (e) nota::c_abi::eng(e).gamepad().info(i, &uid, &name);
    return uid ? uid : "";
}

const char* nota_gamepad_name(NotaEngine* e, int32_t i) {
    const char* uid = nullptr;
    const char* name = nullptr;
    if (e) nota::c_abi::eng(e).gamepad().info(i, &uid, &name);
    return name ? name : "";
}

int32_t nota_gamepad_poll_events(NotaEngine* e, NotaGamepadButtonEvent* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    std::array<nota::io::gamepad::GamepadButtonEvent, kStackEventLimit> stack{};
    std::vector<nota::io::gamepad::GamepadButtonEvent> heap;
    nota::io::gamepad::GamepadButtonEvent* dst = nullptr;
    if (static_cast<size_t>(max) <= stack.size()) {
        dst = stack.data();
    } else {
        heap.resize(static_cast<size_t>(max));
        dst = heap.data();
    }
    const int32_t n = nota::c_abi::eng(e).gamepad().pollEvents(dst, max);
    for (int32_t i = 0; i < n; ++i) out[i] = {dst[i].pressed, dst[i].buttonId, dst[i].pad};
    return n;
}

int32_t nota_gamepad_axis_values(NotaEngine* e, int32_t pad, int32_t* out, int32_t max) {
    return (e && out && max > 0) ? nota::c_abi::eng(e).gamepad().axisValues(pad, out, max) : 0;
}

} // extern "C"
