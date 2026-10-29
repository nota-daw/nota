// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — device settings: audio output/input device / sample-rate / buffer
// (M7-1) and MIDI input enable/disable (M7-2). Device enumeration is a hardware
// query, so the snapshots below are file-static (not engine-bound).

#include "nota_engine_internal.h"

#include "audio_io/AudioBackendFactory.h"
#include "audio_io/AudioConfig.h"
#include "audio_io/MidiConfig.h"
#include "audio_io/MpeInput.h"
#include "audio_io/NotaConfigFile.h"
#include "audio_io/asio/AsioSession.h"
#include "audio_io/asio/FakeAsioDriver.h"
#ifdef NOTA_HAS_ASIO
#include "audio_io/windows/AsioBackend.h"
#include "audio_io/windows/AsioDriverList.h"
#endif

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <memory>
#include <string>
#include <thread>
#include <vector>

// ---- Audio device settings (M7-1) -----------------------------------------

namespace {
// Snapshot of connected devices, refreshed by nota_audio_refresh_devices().
// Global (not engine-bound): enumeration is a hardware query.
std::vector<nota::AudioDeviceInfo> g_outputDevices;
std::vector<nota::AudioDeviceInfo> g_inputDevices;

const char* deviceField(const std::vector<nota::AudioDeviceInfo>& list, int32_t i, bool uid) {
    if (i < 0 || i >= static_cast<int32_t>(list.size())) return nullptr;
    return uid ? list[i].uid.c_str() : list[i].name.c_str();
}
#ifdef NOTA_HAS_ASIO
// Installed ASIO drivers, refreshed with the device lists (specs/049).
std::vector<nota::asio::AsioDriverInfo> g_asioDrivers;
#endif
} // namespace

extern "C" {

void nota_audio_refresh_devices(void) {
    g_outputDevices = nota::enumerateAudioDevices(/*inputScope=*/false);
    g_inputDevices = nota::enumerateAudioDevices(/*inputScope=*/true);
#ifdef NOTA_HAS_ASIO
    g_asioDrivers = nota::asio::enumerateAsioDrivers();
#endif
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

NotaResult nota_audio_set_output_device(NotaEngine* e, const char* uid) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioOutputDevice(uid ? std::string(uid) : std::string{});
    return NOTA_OK;
}
NotaResult nota_audio_set_input_device(NotaEngine* e, const char* uid) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioInputDevice(uid ? std::string(uid) : std::string{});
    return NOTA_OK;
}
NotaResult nota_audio_set_sample_rate(NotaEngine* e, double sr) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioSampleRate(sr);
    return NOTA_OK;
}
NotaResult nota_audio_set_buffer_frames(NotaEngine* e, int32_t frames) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioBufferFrames(frames);
    return NOTA_OK;
}
NotaResult nota_audio_set_wasapi_exclusive(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioWasapiExclusive(enabled != 0);
    return NOTA_OK;
}
int32_t nota_audio_wasapi_exclusive(const NotaEngine* e) {
    return (e && CENG(e)->audioConfig().wasapiExclusive) ? 1 : 0;
}

const char* nota_audio_output_device(const NotaEngine* e) {
    static std::string s; // owned by the engine, valid until the next call
    s = e ? CENG(e)->audioConfig().outputDeviceUid : std::string{};
    return s.c_str();
}
const char* nota_audio_input_device(const NotaEngine* e) {
    static std::string s; // owned by the engine, valid until the next call
    s = e ? CENG(e)->audioConfig().inputDeviceUid : std::string{};
    return s.c_str();
}
double nota_audio_sample_rate(const NotaEngine* e) {
    return e ? CENG(e)->audioConfig().sampleRate : 0.0;
}
int32_t nota_audio_buffer_frames(const NotaEngine* e) {
    return e ? CENG(e)->audioConfig().bufferFrames : 0;
}

NotaResult nota_audio_apply(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->applyAudioConfig() ? NOTA_OK : NOTA_ERR_AUDIO_DEVICE;
}
double nota_audio_negotiated_sample_rate(const NotaEngine* e) {
    return e ? CENG(e)->sampleRate() : 0.0;
}
int32_t nota_audio_negotiated_buffer_frames(const NotaEngine* e) {
    return e ? CENG(e)->bufferFrames() : 0;
}
int32_t nota_audio_exclusive_fallback(const NotaEngine* e) {
    return (e && CENG(e)->audioExclusiveFallback()) ? 1 : 0;
}
int32_t nota_audio_silent_fallback(const NotaEngine* e) {
    return (e && CENG(e)->audioSilentFallback()) ? 1 : 0;
}

// ---- ASIO (Windows; specs/049) ---------------------------------------------

NotaResult nota_audio_set_system(NotaEngine* e, int32_t system) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioSystem(system == 1 ? nota::AudioSystem::Asio : nota::AudioSystem::Wasapi);
    return NOTA_OK;
}
int32_t nota_audio_system(const NotaEngine* e) {
    return (e && CENG(e)->audioConfig().audioSystem == nota::AudioSystem::Asio) ? 1 : 0;
}
int32_t nota_audio_active_system(const NotaEngine* e) {
    return (e && CENG(e)->activeAudioSystem() == nota::AudioSystem::Asio) ? 1 : 0;
}

int32_t nota_audio_asio_available(void) {
    return nota::asioSupported() ? 1 : 0;
}
int32_t nota_audio_asio_driver_count(void) {
#ifdef NOTA_HAS_ASIO
    return static_cast<int32_t>(g_asioDrivers.size());
#else
    return 0;
#endif
}
const char* nota_audio_asio_driver_id(int32_t i) {
#ifdef NOTA_HAS_ASIO
    if (i >= 0 && i < static_cast<int32_t>(g_asioDrivers.size())) return g_asioDrivers[i].clsid.c_str();
#else
    (void)i;
#endif
    return nullptr;
}
const char* nota_audio_asio_driver_name(int32_t i) {
#ifdef NOTA_HAS_ASIO
    if (i >= 0 && i < static_cast<int32_t>(g_asioDrivers.size())) return g_asioDrivers[i].displayName.c_str();
#else
    (void)i;
#endif
    return nullptr;
}
NotaResult nota_audio_set_asio_driver(NotaEngine* e, const char* id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
#ifdef NOTA_HAS_ASIO
    const std::string clsid = id ? id : "";
    if (g_asioDrivers.empty()) g_asioDrivers = nota::asio::enumerateAsioDrivers();
    // The registry name travels with the CLSID: it finds the driver again if a reinstall
    // gives it a new CLSID (research R10).
    const auto* d = clsid.empty() ? nullptr : nota::asio::findAsioDriver(g_asioDrivers, clsid, {});
    ENG(e)->setAsioDriver(clsid, d ? d->name : std::string{});
#else
    (void)id;
#endif
    return NOTA_OK;
}
const char* nota_audio_asio_driver(const NotaEngine* e) {
    static std::string s; // owned by the engine, valid until the next call
    s = e ? CENG(e)->audioConfig().asioDriverClsid : std::string{};
    return s.c_str();
}

int32_t nota_audio_asio_channel_count(const NotaEngine* e, int32_t is_input) {
#ifdef NOTA_HAS_ASIO
    if (e && CENG(e)->activeAudioSystem() == nota::AudioSystem::Asio)
        return static_cast<int32_t>(nota::asio::sharedAsioSession().channels(is_input != 0).size());
#else
    (void)e;
    (void)is_input;
#endif
    return 0;
}
const char* nota_audio_asio_channel_name(const NotaEngine* e, int32_t is_input, int32_t index) {
#ifdef NOTA_HAS_ASIO
    static std::string s; // valid until the next call
    if (e && CENG(e)->activeAudioSystem() == nota::AudioSystem::Asio) {
        const auto& ch = nota::asio::sharedAsioSession().channels(is_input != 0);
        if (index >= 0 && index < static_cast<int32_t>(ch.size())) {
            s = ch[static_cast<size_t>(index)].name;
            return s.c_str();
        }
    }
#else
    (void)e;
    (void)is_input;
    (void)index;
#endif
    return nullptr;
}
NotaResult nota_audio_set_asio_pair(NotaEngine* e, int32_t is_input, int32_t first_channel) {
    if (!e || first_channel < 0) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAsioPair(is_input != 0, first_channel);
    return NOTA_OK;
}
int32_t nota_audio_asio_pair(const NotaEngine* e, int32_t is_input) {
    if (!e) return 0;
    return is_input ? CENG(e)->audioConfig().asioInputPair : CENG(e)->audioConfig().asioOutputPair;
}

NotaResult nota_audio_asio_open_control_panel(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
#ifdef NOTA_HAS_ASIO
    if (CENG(e)->activeAudioSystem() == nota::AudioSystem::Asio && nota::asio::sharedAsioSession().openControlPanel())
        return NOTA_OK;
#endif
    return NOTA_ERR_AUDIO_DEVICE;
}

int32_t nota_audio_asio_panel_open(const NotaEngine* e) {
#ifdef NOTA_HAS_ASIO
    if (e && CENG(e)->activeAudioSystem() == nota::AudioSystem::Asio && nota::asio::sharedAsioSession().panelOpen())
        return 1;
#else
    (void)e;
#endif
    return 0;
}

int32_t nota_audio_poll_device_event(NotaEngine* e) {
    return e ? ENG(e)->pollAudioDeviceEvent() : 0;
}
const char* nota_audio_fallback_reason(const NotaEngine* e) {
    static std::string s; // owned by the engine, valid until the next call
    s = e ? CENG(e)->audioFallbackReason() : std::string{};
    return s.c_str();
}

int32_t nota_audio_input_latency_frames(const NotaEngine* e) {
    return e ? CENG(e)->audioInputLatencyFrames() : 0;
}
int32_t nota_audio_output_latency_frames(const NotaEngine* e) {
    return e ? CENG(e)->audioOutputLatencyFrames() : 0;
}

} // extern "C"

// ---- ASIO self-test (specs/049, FR-016) -------------------------------------
// Drives AsioSession through FakeAsioDriver on the calling thread. Uses the ASIO callback
// routing of a session of its own, so it must not run while a real ASIO driver is open
// (the smoke test never starts the audio backend).

namespace {

using nota::asio::AsioSession;
using nota::asio::FakeAsioDriver;
using nota::asio::SampleType;

struct AsioCheck {
    bool ok = true;
    void operator()(bool cond, const char* what) {
        if (cond) return;
        ok = false;
        std::fprintf(stderr, "nota_asio_selftest: FAILED %s\n", what);
    }
};

double lsb(SampleType t) {
    switch (t) {
    case SampleType::Int16LSB:
    case SampleType::Int32LSB16: return 1.0 / 32768.0;
    case SampleType::Int32LSB18: return 1.0 / 131072.0;
    case SampleType::Int32LSB20: return 1.0 / 524288.0;
    case SampleType::Int24LSB:
    case SampleType::Int32LSB24: return 1.0 / 8388608.0;
    case SampleType::Int32LSB: return 1.0 / 2147483648.0;
    default: return 0.0;
    }
}

// A session opened on a fresh fake driver; `fake` stays usable while the session owns it.
struct Rig {
    AsioSession session;
    FakeAsioDriver* fake = nullptr;
    bool open(FakeAsioDriver::Config cfg, AsioSession::Options opt = {},
              void (*tweak)(FakeAsioDriver&) = nullptr) {
        auto d = std::make_unique<FakeAsioDriver>(std::move(cfg));
        fake = d.get();
        if (tweak) tweak(*d);
        return session.open(std::move(d), opt);
    }
};

void checkFormats(AsioCheck& check) {
    const SampleType types[] = {SampleType::Int16LSB,   SampleType::Int24LSB,   SampleType::Int32LSB,
                                SampleType::Float32LSB, SampleType::Float64LSB, SampleType::Int32LSB16,
                                SampleType::Int32LSB18, SampleType::Int32LSB20, SampleType::Int32LSB24};
    std::vector<float> src{-1.0f, -0.5f, 0.0f, 0.5f, 1.0f};
    for (int i = 0; i < 64; ++i) src.push_back(0.8f * static_cast<float>(std::sin(i * 0.37)));
    const auto n = static_cast<int32_t>(src.size());
    for (SampleType t : types) {
        check(nota::asio::isSupported(t), "every LSB PCM / float type is supported");
        std::vector<uint8_t> raw(static_cast<size_t>(n) * 8);
        std::vector<float> back(static_cast<size_t>(n) * 2, 7.0f);
        nota::asio::fromFloat(src.data(), 1, t, raw.data(), n);
        nota::asio::toFloat(raw.data(), t, back.data(), n, 2); // stride 2: every other float
        bool within = true;
        for (int32_t i = 0; i < n; ++i)
            if (std::fabs(back[static_cast<size_t>(i) * 2] - src[static_cast<size_t>(i)]) > lsb(t) * 1.01 + 1e-7)
                within = false;
        check(within, nota::asio::sampleTypeName(t)); // round trip within 1 LSB
        check(back[1] == 7.0f, "toFloat honours the stride");
    }
    for (SampleType t : {SampleType::Int16MSB, SampleType::Int24MSB, SampleType::Int32MSB, SampleType::Float32MSB,
                         SampleType::Float64MSB, SampleType::Int32MSB16, SampleType::Int32MSB24,
                         SampleType::DSDInt8LSB1, SampleType::DSDInt8MSB1, SampleType::DSDInt8NER8})
        check(!nota::asio::isSupported(t), "MSB and DSD types are refused");
}

void checkRouting(AsioCheck& check) {
    // Output pair 2 of an 8-out driver gets the render; every other output stays exactly zero.
    for (SampleType t : {SampleType::Int32LSB, SampleType::Float32LSB}) {
        Rig rig;
        FakeAsioDriver::Config cfg;
        cfg.inputs = 0;
        cfg.outputs = 8;
        cfg.outputType = t;
        AsioSession::Options opt;
        opt.outputPair = 2;
        check(rig.open(cfg, opt), "8-out fake opens");
        rig.session.setRenderCallback([](float* out, int32_t n) {
            for (int32_t i = 0; i < n; ++i) {
                out[i * 2] = 0.25f;
                out[i * 2 + 1] = -0.5f;
            }
        });
        check(rig.session.start(), "8-out fake starts");
        rig.fake->pump(100);
        const auto& l = rig.fake->written(2);
        const auto& r = rig.fake->written(3);
        bool pair = l.size() == 100u * 64u && r.size() == l.size();
        for (size_t i = 0; pair && i < l.size(); ++i)
            pair = std::fabs(l[i] - 0.25f) <= lsb(t) && std::fabs(r[i] + 0.5f) <= lsb(t);
        check(pair, t == SampleType::Int32LSB ? "Int32LSB render on outputs 3/4" : "Float32LSB render on outputs 3/4");
        bool silent = true;
        for (int32_t ch : {0, 1, 4, 5, 6, 7}) silent = silent && !rig.fake->nonZeroBytes(ch);
        check(silent, "every other output is exact zeros each switch");
        check(rig.fake->outputReadyCalls() == 100, "outputReady after every switch");
        rig.session.close();
    }
    // A one-output driver plays the mono down-sum.
    Rig mono;
    FakeAsioDriver::Config cfg;
    cfg.inputs = 0;
    cfg.outputs = 1;
    check(mono.open(cfg), "1-out fake opens");
    mono.session.setRenderCallback([](float* out, int32_t n) {
        for (int32_t i = 0; i < n; ++i) {
            out[i * 2] = 0.2f;
            out[i * 2 + 1] = 0.6f;
        }
    });
    mono.session.start();
    mono.fake->pump(4);
    const auto& m = mono.fake->written(0);
    check(!m.empty() && std::fabs(m.front() - 0.4f) < 1e-6f && std::fabs(m.back() - 0.4f) < 1e-6f,
          "mono output is the down-sum");
    // An out-of-range pair falls back to the first.
    Rig clamp;
    cfg.outputs = 2;
    AsioSession::Options far;
    far.outputPair = 6;
    check(clamp.open(cfg, far) && clamp.session.outputPair() == 0, "out-of-range pair clamps to the first");
}

void checkDuplex(AsioCheck& check) {
    // Input pair 2 of a 4-in driver reaches the input callback, before render, every switch.
    Rig rig;
    FakeAsioDriver::Config cfg;
    cfg.inputs = 4;
    cfg.outputs = 2;
    cfg.inputType = SampleType::Int24LSB;
    cfg.inputSignal = [](int32_t ch, int64_t) { return 0.1f * static_cast<float>(ch + 1); };
    AsioSession::Options opt;
    opt.inputPair = 2;
    check(rig.open(cfg, opt), "duplex fake opens");
    std::vector<char> order;
    float gotL = 0.0f, gotR = 0.0f;
    const nota::InputCallback in = [&](const float* s, int32_t) {
        order.push_back('i');
        gotL = s[0];
        gotR = s[1];
    };
    rig.session.setRenderCallback([&](float*, int32_t) { order.push_back('r'); });
    rig.session.setInputCallback(&in);
    rig.session.start();
    rig.fake->pump(3);
    check(order == std::vector<char>{'i', 'r', 'i', 'r', 'i', 'r'}, "input of switch n before render of switch n");
    check(std::fabs(gotL - 0.3f) < 1e-6f && std::fabs(gotR - 0.4f) < 1e-6f, "input pair 3/4 reaches the callback");
    rig.session.setInputCallback(nullptr);
    rig.fake->pump(1);
    check(order.size() == 7, "an unregistered input is no longer called");
    rig.session.close();

    // A trailing odd input is a mono pair: duplicated to both sides.
    Rig mono;
    cfg.inputs = 3;
    check(mono.open(cfg, opt), "3-in fake opens");
    mono.session.setInputCallback(&in);
    mono.session.start();
    mono.fake->pump(1);
    check(std::fabs(gotL - 0.3f) < 1e-6f && std::fabs(gotR - 0.3f) < 1e-6f, "mono input is duplicated");
    mono.session.setInputCallback(nullptr);
    mono.session.close();

    // Latency compensation: in 100 + out 150 frames moves a take 250 frames earlier.
    Rig lat;
    cfg.inputLatency = 100;
    cfg.outputLatency = 150;
    check(lat.open(cfg), "latency fake opens");
    const int32_t roundTrip = lat.session.inputLatency() + lat.session.outputLatency();
    check(roundTrip == 250, "driver latencies are reported");
    const double spb = 24000.0; // 120 BPM at 48 kHz
    double trim = -1.0;
    const double start = nota::Engine::latencyCompensatedStart(8.0, roundTrip, spb, &trim);
    check(std::fabs((8.0 - start) * spb - 250.0) < 1e-6 && trim == 0.0, "take starts 250 frames earlier");
    const double early = nota::Engine::latencyCompensatedStart(100.0 / spb, roundTrip, spb, &trim);
    check(early == 0.0 && std::fabs(trim - 150.0) < 1e-6, "a take at the very start trims instead");
    check(nota::Engine::latencyCompensatedStart(8.0, 0, spb, &trim) == 8.0, "no latency, no shift");
}

void checkResets(AsioCheck& check) {
    Rig rig;
    FakeAsioDriver::Config cfg;
    AsioSession::Options opt;
    opt.sampleRate = 96000.0; // the fake supports 44.1 / 48 kHz only
    check(rig.open(cfg, opt), "fake opens with an unsupported rate request");
    check(rig.session.sampleRateRefused() && rig.session.sampleRate() == 48000.0,
          "an unsupported rate is refused and the driver's rate kept");
    rig.session.start();
    for (int32_t sel : {nota::asio::kResetRequest, nota::asio::kBufferSizeChange, nota::asio::kLatenciesChanged}) {
        check(rig.fake->sendMessage(sel) == 1, "reset-type message accepted");
        check(rig.session.pollEvent() == AsioSession::kEventResetRequested, "reset message -> one RESET_REQUESTED");
        check(rig.session.pollEvent() == AsioSession::kEventNone, "the event is cleared on read");
    }
    rig.fake->changeSampleRate(44100.0);
    check(rig.session.pollEvent() == AsioSession::kEventResetRequested, "sampleRateDidChange -> RESET_REQUESTED");
    check(rig.session.driverChangedRate() == 44100.0, "a rate set in the driver's panel is reported with the reset");
    check(rig.session.pollEvent() == AsioSession::kEventNone, "rate event cleared on read");
    rig.fake->pump(3);
    check(rig.session.pollEvent() == AsioSession::kEventNone, "steady positions raise nothing");
    rig.fake->setPositionStep(64 + 37);
    rig.fake->pump(2);
    check(rig.session.pollEvent() == AsioSession::kEventResetRequested, "silent buffer-size change -> RESET_REQUESTED");
    check(rig.session.pollEvent() == AsioSession::kEventNone, "buffer-size event cleared on read");

    // A reset sent while the driver's panel is open waits for the panel to close — and so
    // does the watchdog: restarting now would queue behind the modal panel.
    // The driver may also stop its buffers while its panel reconfigures it: once the panel
    // closes, the pending reset wins over the watchdog (reopen ASIO, don't fall back).
    rig.fake->setPositionStep(0);
    rig.session.setWatchdogTimeout(std::chrono::milliseconds(30));
    rig.fake->setPanelOpen(true);
    rig.fake->sendMessage(nota::asio::kResetRequest);
    std::this_thread::sleep_for(std::chrono::milliseconds(60));
    check(rig.session.pollEvent() == AsioSession::kEventNone, "no restart while the driver's panel is open");
    std::this_thread::sleep_for(std::chrono::milliseconds(60)); // silent past the watchdog, panel still open
    rig.fake->setPanelOpen(false);
    check(rig.session.pollEvent() == AsioSession::kEventResetRequested,
          "after the panel closes the deferred reset comes first, not DRIVER_FAILED");

    int xruns = 0;
    Rig ov;
    check(ov.open(FakeAsioDriver::Config{}), "overload fake opens");
    ov.session.setXrunCallback([&] { ++xruns; });
    ov.session.start();
    ov.fake->sendMessage(nota::asio::kOverload);
    check(xruns == 1, "kAsioOverload counts a dropout");
    check(ov.fake->sendMessage(nota::asio::kSelectorSupported, nota::asio::kResetRequest) == 1 &&
              ov.fake->sendMessage(nota::asio::kEngineVersion) == 2 &&
              ov.fake->sendMessage(nota::asio::kSupportsTimeInfo) == 1,
          "host answers the selector queries");
}

void checkFailures(AsioCheck& check) {
    Rig busy;
    FakeAsioDriver::Config cfg;
    cfg.initFails = true;
    check(!busy.open(cfg) && busy.session.failure() == AsioSession::Failure::InitFailed &&
              !busy.session.failureText().empty() && busy.session.state() == AsioSession::State::Failed,
          "init fails -> Failed(InitFailed) with a reason");

    Rig msb;
    check(!msb.open(FakeAsioDriver::Config{}, {}, [](FakeAsioDriver& d) { d.setOutputType(1, SampleType::Int32MSB); }) &&
              msb.session.failure() == AsioSession::Failure::UnsupportedFormat,
          "MSB output -> Failed(UnsupportedFormat)");

    Rig none;
    FakeAsioDriver::Config noOut;
    noOut.outputs = 0;
    check(!none.open(noOut) && none.session.failure() == AsioSession::Failure::NoOutputs, "no outputs -> Failed(NoOutputs)");

    Rig silent;
    check(silent.open(FakeAsioDriver::Config{}), "watchdog fake opens");
    silent.session.setWatchdogTimeout(std::chrono::milliseconds(60));
    silent.session.start();
    silent.fake->pump(2);
    check(silent.session.pollEvent() == AsioSession::kEventNone, "a driver calling back is healthy");
    silent.fake->setStopsCallingBack(true);
    std::this_thread::sleep_for(std::chrono::milliseconds(150));
    check(silent.session.pollEvent() == AsioSession::kEventDriverFailed &&
              silent.session.failure() == AsioSession::Failure::Stopped,
          "a driver that stops calling back -> DRIVER_FAILED");
    silent.session.close();

    Rig late;
    int renders = 0;
    check(late.open(FakeAsioDriver::Config{}), "late-callback fake opens");
    late.session.setRenderCallback([&](float*, int32_t) { ++renders; });
    late.session.start();
    late.fake->pump(2);
    late.session.stop();
    late.fake->pump(1, /*evenIfStopped=*/true);
    check(renders == 2, "a bufferSwitch after stop() is ignored");

    // A switch that never returns (a stalled plug-in): teardown must not free what it reads,
    // and no driver may open next to it. Here the "stall" ends after teardown gave up.
    AsioSession stuck;
    auto drv = std::make_unique<FakeAsioDriver>(FakeAsioDriver::Config{});
    FakeAsioDriver* fake = drv.get(); // leaked with the session's state when it wedges
    check(stuck.open(std::move(drv), {}), "stall fake opens");
    std::atomic<bool> inRender{false}, release{false};
    stuck.setRenderCallback([&](float* out, int32_t n) {
        inRender = true;
        while (!release.load()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
        std::fill(out, out + static_cast<size_t>(n) * 2, 0.5f);
    });
    stuck.start();
    std::thread audio([fake] { fake->pump(1); });
    while (!inRender.load()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    stuck.close(); // gives up after its bounded wait and leaks the switch's state
    check(stuck.wedged(), "a switch that never returns wedges the session");
    release = true;
    audio.join(); // the switch finishes on memory that is still alive
    check(!fake->written(0).empty() && fake->written(0).front() == 0.5f, "the stalled switch completes safely");
    check(!stuck.open(std::make_unique<FakeAsioDriver>(FakeAsioDriver::Config{}), {}) &&
              stuck.failure() == AsioSession::Failure::Timeout,
          "no driver opens next to a wedged switch");
}

void checkConfigKeys(AsioCheck& check) {
    namespace fs = std::filesystem;
    const std::string saved = nota::cfgfile::dataDirOverride();
    std::error_code ec;
    const fs::path dir = fs::temp_directory_path(ec) /
                         ("nota-asio-selftest-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    nota::cfgfile::setDataDirOverride(dir.string());
    nota::AudioConfig cfg;
    check(nota::loadAudioConfig().audioSystem == nota::AudioSystem::Wasapi, "missing audio.json keys mean WASAPI");
    cfg.audioSystem = nota::AudioSystem::Asio;
    cfg.asioDriverClsid = "{11111111-2222-3333-4444-555555555555}";
    cfg.asioDriverName = "Fake ASIO";
    cfg.asioOutputPair = 4;
    cfg.asioInputPair = 2;
    nota::saveAudioConfig(cfg);
    const nota::AudioConfig back = nota::loadAudioConfig();
    check(back.asioDriverClsid == cfg.asioDriverClsid && back.asioDriverName == cfg.asioDriverName &&
              back.asioOutputPair == 4 && back.asioInputPair == 2,
          "ASIO keys round-trip through audio.json");
#if defined(_WIN32)
    check(back.audioSystem == nota::AudioSystem::Asio, "audioSystem round-trips on Windows");
#else
    check(back.audioSystem == nota::AudioSystem::Wasapi, "audioSystem reads as the native system off Windows");
#endif
    nota::cfgfile::setDataDirOverride(saved);
    fs::remove_all(dir, ec);
}

} // namespace

extern "C" int32_t nota_asio_selftest(void) {
    AsioCheck check;
    {
        // On the heap: an Engine is far bigger than a 1 MB Windows main-thread stack.
        auto engine = std::make_unique<nota::Engine>();
        check(engine->monitorSharedClockSelfTest(), "an ASIO block split by a loop reaches monitoring whole");
    }
    checkFormats(check);
    checkRouting(check);
    checkDuplex(check);
    checkResets(check);
    checkFailures(check);
    checkConfigKeys(check);
    return check.ok ? 1 : 0;
}

// ---- MIDI device settings (M7-2) ------------------------------------------

namespace {
// Snapshot of connected MIDI inputs, refreshed by nota_midi_refresh_devices().
std::vector<nota::MidiDeviceInfo> g_midiInputs;

const char* midiField(int32_t i, bool uid) {
    if (i < 0 || i >= static_cast<int32_t>(g_midiInputs.size())) return nullptr;
    return uid ? g_midiInputs[i].uid.c_str() : g_midiInputs[i].name.c_str();
}
} // namespace

extern "C" {

void nota_midi_refresh_devices(void) {
    g_midiInputs = nota::enumerateMidiInputs();
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
    ENG(e)->setMidiInputEnabled(std::string(uid), enabled != 0);
    return NOTA_OK;
}
int32_t nota_midi_input_enabled(const NotaEngine* e, const char* uid) {
    return (e && uid && CENG(e)->midiInputEnabled(std::string(uid))) ? 1 : 0;
}
NotaResult nota_midi_set_mpe(NotaEngine* e, int32_t enabled, int32_t bend_range) {
    if (!e || bend_range < 1 || bend_range > 96) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setMpe(enabled != 0, bend_range);
    return NOTA_OK;
}
int32_t nota_midi_mpe_enabled(const NotaEngine* e) {
    return (e && CENG(e)->midiConfig().mpe) ? 1 : 0;
}
int32_t nota_midi_mpe_bend_range(const NotaEngine* e) {
    return e ? CENG(e)->midiConfig().mpeBendRange : 48;
}
int32_t nota_mpe_selftest(void) {
    struct Ev {
        int kind;
        int32_t pitch;
        int32_t dim;
        float v;
    }; // kind 0 on, 1 off, 2 expr
    struct Rec final : nota::MpeInput::Sink {
        std::vector<Ev> ev;
        void mpeNoteOn(int32_t p, float v) override { ev.push_back({0, p, -1, v}); }
        void mpeNoteOff(int32_t p) override { ev.push_back({1, p, -1, 0.0f}); }
        void mpeExpression(int32_t p, int32_t d, float v) override { ev.push_back({2, p, d, v}); }
        // Controllers read as instrument-wide expression where they have a dim, else dim
        // kCtlDimBase + ctl.
        void mpeController(int32_t c, float v) override {
            const int32_t d = c == nota::kCtlPitchWheel        ? 0
                              : c == nota::kCtlChannelPressure ? 1
                              : c == 74                        ? 2
                              : c == nota::kCtlSustain         ? 3
                                                               : nota::kCtlDimBase + c;
            ev.push_back({2, -1, d, v});
        }
        bool has(int kind, int32_t p, int32_t d, float v) const {
            for (const auto& e : ev)
                if (e.kind == kind && e.pitch == p && e.dim == d && std::fabs(e.v - v) < 0.02f) return true;
            return false;
        }
    } r;
    nota::MpeInput m;
    m.configure(true, 48);
    auto bend = [&](int ch, float x) {
        const int raw = std::clamp((int)std::lround(8192 + x * 8191), 0, 16383);
        m.message(0xE0, ch, raw & 0x7F, raw >> 7, r);
    };

    // 1: a member channel's state before its note-on lands on the note right after it.
    bend(1, 0.25f);
    m.message(0xD0, 1, 64, 0, r);
    m.message(0xB0, 1, 74, 100, r);
    r.ev.clear();
    m.message(0x90, 1, 60, 100, r);
    if (r.ev.empty() || r.ev[0].kind != 0 || !r.has(2, 60, 0, 12.0f) || !r.has(2, 60, 1, 64 / 127.0f) ||
        !r.has(2, 60, 2, 100 / 127.0f))
        return 1;
    // 2: member bend is per-note, in semitones (±48 by default).
    r.ev.clear();
    bend(1, -0.5f);
    if (r.ev.size() != 1 || !r.has(2, 60, 0, -24.0f)) return 2;
    // 3: another member channel's note is untouched by channel 2's bend.
    m.message(0x90, 2, 64, 100, r);
    r.ev.clear();
    bend(1, 0.0f);
    for (const auto& e : r.ev)
        if (e.pitch == 64) return 3;
    // 4: the master channel addresses the whole instrument, wheel normalized.
    r.ev.clear();
    bend(0, 1.0f);
    m.message(0xD0, 0, 127, 0, r);
    if (!r.has(2, -1, 0, 1.0f) || !r.has(2, -1, 1, 1.0f)) return 4;
    // 5: RPN 0 on a member channel re-ranges every member of the zone.
    m.message(0xB0, 3, 101, 0, r);
    m.message(0xB0, 3, 100, 0, r);
    m.message(0xB0, 3, 6, 24, r);
    if (m.bendRange(1) != 24 || m.bendRange(15) != 24 || m.bendRange(0) != 48) return 5;
    r.ev.clear();
    bend(1, 0.5f);
    if (!r.has(2, 60, 0, 12.0f)) return 5;
    // 6: after note-off the channel no longer bends that pitch.
    m.message(0x80, 1, 60, 0, r);
    r.ev.clear();
    bend(1, 0.1f);
    for (const auto& e : r.ev)
        if (e.pitch == 60) return 6;
    // 7: MCM on channel 1 shrinks the lower zone; channel 5 becomes an ordinary channel.
    m.message(0xB0, 0, 101, 0, r);
    m.message(0xB0, 0, 100, 6, r);
    m.message(0xB0, 0, 6, 3, r);
    if (m.lowerMembers() != 3) return 7;
    m.message(0x90, 5, 70, 100, r);
    r.ev.clear();
    bend(5, 1.0f);
    if (!r.has(2, -1, 0, 1.0f)) return 7;
    // 8: MPE off: every channel is an ordinary keyboard; poly pressure stays per-note.
    nota::MpeInput off;
    off.configure(false, 48);
    r.ev.clear();
    off.message(0x90, 1, 60, 100, r);
    {
        const int raw = 16383;
        off.message(0xE0, 1, raw & 0x7F, raw >> 7, r);
    }
    off.message(0xA0, 1, 60, 127, r);
    if (r.ev.size() != 3 || !r.has(2, -1, 0, 1.0f) || !r.has(2, 60, 1, 1.0f)) return 8;
    // 9: every other CC of an ordinary channel is a controller (the mod wheel, expression), the
    // pedal keeps its value, and data entry / channel mode messages are not controllers.
    r.ev.clear();
    off.message(0xB0, 1, 1, 127, r);
    off.message(0xB0, 1, 11, 64, r);
    off.message(0xB0, 1, 64, 40, r);
    off.message(0xB0, 1, 123, 0, r);
    off.message(0xB0, 1, 38, 5, r);
    if (r.ev.size() != 3 || !r.has(2, -1, nota::kCtlDimBase + 1, 1.0f) ||
        !r.has(2, -1, nota::kCtlDimBase + 11, 64 / 127.0f) || !r.has(2, -1, 3, 40 / 127.0f))
        return 9;
    // 10: a member channel's CC stays out of the instrument-wide controllers (MPE on).
    r.ev.clear();
    m.message(0xB0, 1, 1, 100, r);
    if (!r.ev.empty()) return 10;
    return 0;
}

NotaResult nota_midi_apply(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->applyMidiConfig() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

// MIDI-learn: drain incoming CC/note-on events into a caller buffer of int32 quads
// {kind, channel, number, value}. Returns the event count written (<= max).
int32_t nota_midi_poll_control_events(NotaEngine* e, int32_t* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    return ENG(e)->pollControlEvents(reinterpret_cast<nota::ControlEvent*>(out), max);
}

} // extern "C"

// ---- Gamepad input ---------------------------------------------------------

extern "C" {

void nota_gamepad_start(NotaEngine* e) {
    if (e) ENG(e)->startGamepadInput();
}
void nota_gamepad_stop(NotaEngine* e) {
    if (e) ENG(e)->stopGamepadInput();
}
int32_t nota_gamepad_count(NotaEngine* e) {
    return e ? ENG(e)->gamepadCount() : 0;
}

const char* nota_gamepad_uid(NotaEngine* e, int32_t i) {
    const char* uid = nullptr;
    const char* name = nullptr;
    if (e) ENG(e)->gamepadInfo(i, &uid, &name);
    return uid ? uid : "";
}
const char* nota_gamepad_name(NotaEngine* e, int32_t i) {
    const char* uid = nullptr;
    const char* name = nullptr;
    if (e) ENG(e)->gamepadInfo(i, &uid, &name);
    return name ? name : "";
}

int32_t nota_gamepad_poll_events(NotaEngine* e, NotaGamepadButtonEvent* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    return ENG(e)->pollGamepadEvents(reinterpret_cast<nota::GamepadInput::ButtonEvent*>(out), max);
}

int32_t nota_gamepad_axis_values(NotaEngine* e, int32_t pad, int32_t* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    return ENG(e)->gamepadAxisValues(pad, out, max);
}

} // extern "C"
