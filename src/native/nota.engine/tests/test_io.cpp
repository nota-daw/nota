// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "fixtures/FakeAsioDriver.h"
#include "nota/core/ConfigFile.h"
#include "nota/dsp/rt/ExpressionTypes.h"
#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/io/audio/AudioConfigStore.h"
#include "nota/io/audio/AudioDriverRegistry.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/NullAudioDriver.h"
#include "nota/io/audio/asio/AsioDriverApi.h"
#include "nota/io/audio/OutputRouter.h"
#include "nota/io/audio/asio/AsioDriverList.h"
#include "nota/io/audio/asio/AsioSampleFormat.h"
#include "nota/io/audio/asio/AsioSession.h"
#include "nota/io/gamepad/GamepadInput.h"
#include "nota/io/gamepad/GamepadTypes.h"
#include "nota/io/gamepad/IGamepadDriver.h"
#include "nota/io/midi/MidiDriverRegistry.h"
#include "nota/io/midi/MidiParser.h"
#include "nota/io/midi/MidiTypes.h"
#include "nota/io/midi/MpeInput.h"
#include "nota/io/midi/NullMidiDriver.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <memory>
#include <string>
#include <system_error>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

#define NOTA_CHECK(cond)                                                                                               \
    do {                                                                                                               \
        if (!(cond)) {                                                                                                 \
            std::fprintf(stderr, "test_io FAILED at %s:%d: %s\n", __FILE__, __LINE__, #cond);                          \
            std::abort();                                                                                              \
        }                                                                                                              \
    } while (false)

namespace {

using namespace nota::io::audio;
using namespace nota::io::audio::asio;
using namespace nota::io::midi;
using namespace nota::io::gamepad;
using nota::test::FakeAsioDriver;

double sampleLsb(SampleType t) {
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

struct Rig {
    AsioSession session;
    FakeAsioDriver* fake = nullptr;
    bool open(FakeAsioDriver::Config cfg, AsioSession::Options opt = {}, void (*tweak)(FakeAsioDriver&) = nullptr) {
        auto d = std::make_unique<FakeAsioDriver>(std::move(cfg));
        fake = d.get();
        if (tweak) tweak(*d);
        return session.open(std::move(d), opt);
    }
};

void testAudioRegistryAndNullDriver() {
    registerBuiltinAudioDrivers();
    auto& reg = AudioDriverRegistry::instance();
    NOTA_CHECK(reg.find("null") != nullptr);
    NOTA_CHECK(reg.defaultDriver() != nullptr);

    NullAudioDriver nullDrv;
    auto devs = nullDrv.enumerateDevices(false);
    NOTA_CHECK(devs.size() == 1 && devs[0].uid == "null:out");
    NOTA_CHECK(nullDrv.effectiveDeviceUid("missing", true) == "null:in");

    auto out = nullDrv.createOutputStream();
    auto in = nullDrv.createInputStream();
    NOTA_CHECK(out && in);
    OutputStreamConfig cfg;
    cfg.sampleRate = 48000.0;
    cfg.bufferFrames = 512;
    NOTA_CHECK(out->start([](float*, float*, int32_t) {}, cfg));
    NOTA_CHECK(out->isRunning() && out->silentFallback());
    NOTA_CHECK(out->sampleRate() == 48000.0 && out->bufferFrames() == 512);
    NOTA_CHECK(out->createCoupledInputStream() == nullptr);
    out->stop();
    NOTA_CHECK(!out->isRunning());

    NOTA_CHECK(in->start([](const float*, int32_t, int32_t) {}, 0, 44100.0));
    NOTA_CHECK(in->isRunning() && in->channelCount() == 2 && in->channelName(0) == "Null In 1");
    in->stop();
    NOTA_CHECK(!in->isRunning() && in->channelCount() == 0);

    (void)enumerateAsioDrivers();
}

void testAsioFormats() {
    const SampleType types[] = {SampleType::Int16LSB, SampleType::Int24LSB, SampleType::Int32LSB,
        SampleType::Float32LSB, SampleType::Float64LSB, SampleType::Int32LSB16, SampleType::Int32LSB18,
        SampleType::Int32LSB20, SampleType::Int32LSB24};
    std::vector<float> src{-1.0f, -0.5f, 0.0f, 0.5f, 1.0f};
    for (int i = 0; i < 64; ++i) src.push_back(0.8f * std::sin(static_cast<float>(i) * 0.37f));
    const auto n = static_cast<int32_t>(src.size());
    for (SampleType t : types) {
        NOTA_CHECK(isSupported(t));
        std::vector<uint8_t> raw(static_cast<std::size_t>(n) * 8u);
        std::vector<float> back(static_cast<std::size_t>(n) * 2u, 7.0f);
        fromFloat(src.data(), 1, t, raw.data(), n);
        toFloat(raw.data(), t, back.data(), n, 2);
        for (int32_t i = 0; i < n; ++i) {
            NOTA_CHECK(std::fabs(back[static_cast<std::size_t>(i) * 2u] - src[static_cast<std::size_t>(i)]) <=
                       sampleLsb(t) * 1.01 + 1e-7);
        }
        NOTA_CHECK(back[1] == 7.0f);
    }
    for (SampleType t : {SampleType::Int16MSB, SampleType::Int24MSB, SampleType::Int32MSB, SampleType::Float32MSB,
             SampleType::Float64MSB, SampleType::Int32MSB16, SampleType::Int32MSB24, SampleType::DSDInt8LSB1,
             SampleType::DSDInt8MSB1, SampleType::DSDInt8NER8}) {
        NOTA_CHECK(!isSupported(t));
    }
}

void testAsioRoutingAndDuplex() {
    for (SampleType t : {SampleType::Int32LSB, SampleType::Float32LSB}) {
        Rig rig;
        FakeAsioDriver::Config cfg;
        cfg.inputs = 0;
        cfg.outputs = 8;
        cfg.outputType = t;
        AsioSession::Options opt;
        opt.outputPair = 2;
        NOTA_CHECK(rig.open(cfg, opt));
        rig.session.setRenderCallback([](float* out, float*, int32_t n) {
            for (int32_t i = 0; i < n; ++i) {
                out[i * 2] = 0.25f;
                out[i * 2 + 1] = -0.5f;
            }
        });
        NOTA_CHECK(rig.session.start());
        rig.fake->pump(100);
        const auto& l = rig.fake->written(2);
        const auto& r = rig.fake->written(3);
        NOTA_CHECK(l.size() == 100u * 64u && r.size() == l.size());
        for (std::size_t i = 0; i < l.size(); ++i) {
            NOTA_CHECK(std::fabs(l[i] - 0.25f) <= sampleLsb(t) && std::fabs(r[i] + 0.5f) <= sampleLsb(t));
        }
        for (int32_t ch : {0, 1, 4, 5, 6, 7}) NOTA_CHECK(!rig.fake->nonZeroBytes(ch));
        NOTA_CHECK(rig.fake->outputReadyCalls() == 100);
        rig.session.close();
    }

    Rig mono;
    FakeAsioDriver::Config cfg;
    cfg.inputs = 0;
    cfg.outputs = 1;
    NOTA_CHECK(mono.open(cfg));
    mono.session.setRenderCallback([](float* out, float*, int32_t n) {
        for (int32_t i = 0; i < n; ++i) {
            out[i * 2] = 0.2f;
            out[i * 2 + 1] = 0.6f;
        }
    });
    NOTA_CHECK(mono.session.start());
    mono.fake->pump(4);
    const auto& m = mono.fake->written(0);
    NOTA_CHECK(!m.empty() && std::fabs(m.front() - 0.4f) < 1e-6f && std::fabs(m.back() - 0.4f) < 1e-6f);

    Rig cueRig;
    FakeAsioDriver::Config c6;
    c6.inputs = 0;
    c6.outputs = 6;
    NOTA_CHECK(cueRig.open(c6));
    cueRig.session.setRenderCallback([](float* main, float* cue, int32_t n) {
        for (int32_t i = 0; i < n; ++i) {
            main[i * 2] = 0.25f;
            main[i * 2 + 1] = 0.25f;
            cue[i * 2] = -0.5f;
            cue[i * 2 + 1] = -0.5f;
        }
    });
    cueRig.session.setOutputRouting(OutputRouting{0, 2, 4, 2});
    NOTA_CHECK(cueRig.session.start());
    cueRig.fake->pump(2);
    NOTA_CHECK(std::fabs(cueRig.fake->written(0).back() - 0.25f) < 1e-6f);
    NOTA_CHECK(std::fabs(cueRig.fake->written(4).back() + 0.5f) < 1e-6f);
    NOTA_CHECK(!cueRig.fake->nonZeroBytes(2) && !cueRig.fake->nonZeroBytes(3));
    cueRig.session.setOutputRouting(OutputRouting{0, 2, 6, 2});
    cueRig.fake->pump(1);
    NOTA_CHECK(std::fabs(cueRig.fake->written(0).back() - 0.25f) < 1e-6f);
    cueRig.session.close();

    Rig duplex;
    FakeAsioDriver::Config dcfg;
    dcfg.inputs = 4;
    dcfg.outputs = 2;
    dcfg.inputType = SampleType::Int24LSB;
    dcfg.inputSignal = [](int32_t ch, int64_t) { return 0.1f * static_cast<float>(ch + 1); };
    AsioSession::Options dopt;
    dopt.inputPair = 2;
    NOTA_CHECK(duplex.open(dcfg, dopt));
    std::vector<char> order;
    float got[4]{};
    int32_t gotChannels = 0;
    const InputCallback inCb = [&](const float* s, int32_t, int32_t channels) {
        order.push_back('i');
        gotChannels = channels;
        for (int32_t c = 0; c < 4; ++c) got[c] = c < channels ? s[c] : -1.0f;
    };
    duplex.session.setRenderCallback([&](float*, float*, int32_t) { order.push_back('r'); });
    NOTA_CHECK(duplex.session.setInputCallback(&inCb));
    NOTA_CHECK(duplex.session.start());
    duplex.fake->pump(3);
    NOTA_CHECK((order == std::vector<char>{'i', 'r', 'i', 'r', 'i', 'r'}));
    NOTA_CHECK(gotChannels == 4 && std::fabs(got[0] - 0.1f) < 1e-6f && std::fabs(got[3] - 0.4f) < 1e-6f);
    NOTA_CHECK(duplex.session.setInputCallback(nullptr));
    duplex.fake->pump(1);
    NOTA_CHECK(order.size() == 7);
    duplex.session.close();
}

void testAsioResetsAndFailures() {
    Rig rig;
    FakeAsioDriver::Config cfg;
    AsioSession::Options opt;
    opt.sampleRate = 96000.0;
    NOTA_CHECK(rig.open(cfg, opt));
    NOTA_CHECK(rig.session.sampleRateRefused() && rig.session.sampleRate() == 48000.0);
    NOTA_CHECK(rig.session.start());
    for (int32_t sel : {kResetRequest, kBufferSizeChange, kLatenciesChanged}) {
        NOTA_CHECK(rig.fake->sendMessage(sel) == 1);
        NOTA_CHECK(rig.session.pollEvent() == AsioSession::kEventResetRequested);
        NOTA_CHECK(rig.session.pollEvent() == AsioSession::kEventNone);
    }
    rig.fake->changeSampleRate(44100.0);
    NOTA_CHECK(rig.session.pollEvent() == AsioSession::kEventResetRequested);
    NOTA_CHECK(rig.session.driverChangedRate() == 44100.0);
    rig.fake->pump(3);
    NOTA_CHECK(rig.session.pollEvent() == AsioSession::kEventNone);
    rig.fake->setPositionStep(64 + 37);
    rig.fake->pump(2);
    NOTA_CHECK(rig.session.pollEvent() == AsioSession::kEventResetRequested);

    rig.fake->setPositionStep(0);
    rig.session.setWatchdogTimeout(std::chrono::milliseconds(30));
    rig.fake->setPanelOpen(true);
    rig.fake->sendMessage(kResetRequest);
    std::this_thread::sleep_for(std::chrono::milliseconds(60));
    NOTA_CHECK(rig.session.pollEvent() == AsioSession::kEventNone);
    rig.fake->setPanelOpen(false);
    NOTA_CHECK(rig.session.pollEvent() == AsioSession::kEventResetRequested);

    Rig busy;
    cfg.initFails = true;
    NOTA_CHECK(!busy.open(cfg) && busy.session.failure() == AsioSession::Failure::InitFailed);

    Rig msb;
    NOTA_CHECK(!msb.open(FakeAsioDriver::Config{}, {}, [](FakeAsioDriver& d) {
        d.setOutputType(1, SampleType::Int32MSB);
    }) && msb.session.failure() == AsioSession::Failure::UnsupportedFormat);

    Rig none;
    FakeAsioDriver::Config noOut;
    noOut.outputs = 0;
    NOTA_CHECK(!none.open(noOut) && none.session.failure() == AsioSession::Failure::NoOutputs);

    Rig silent;
    NOTA_CHECK(silent.open(FakeAsioDriver::Config{}));
    silent.session.setWatchdogTimeout(std::chrono::milliseconds(40));
    NOTA_CHECK(silent.session.start());
    silent.fake->setStopsCallingBack(true);
    std::this_thread::sleep_for(std::chrono::milliseconds(80));
    NOTA_CHECK(silent.session.pollEvent() == AsioSession::kEventDriverFailed);
    silent.session.close();

    AsioSession stuck;
    auto drv = std::make_unique<FakeAsioDriver>(FakeAsioDriver::Config{});
    FakeAsioDriver* fake = drv.get();
    NOTA_CHECK(stuck.open(std::move(drv), {}));
    std::atomic<bool> inRender{false};
    std::atomic<bool> release{false};
    stuck.setRenderCallback([&](float* out, float*, int32_t n) {
        inRender.store(true);
        while (!release.load()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
        std::fill(out, out + static_cast<std::size_t>(n) * 2u, 0.5f);
    });
    NOTA_CHECK(stuck.start());
    std::thread audio([fake] { fake->pump(1); });
    while (!inRender.load()) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    stuck.close();
    NOTA_CHECK(stuck.wedged());
    release.store(true);
    audio.join();
    NOTA_CHECK(!fake->written(0).empty() && fake->written(0).front() == 0.5f);
    NOTA_CHECK(!stuck.open(std::make_unique<FakeAsioDriver>(FakeAsioDriver::Config{}), {}) &&
               stuck.failure() == AsioSession::Failure::Timeout);
}

void testConfigStores() {
    namespace fs = std::filesystem;
    const std::string saved = nota::core::cfgfile::dataDirOverride();
    std::error_code ec;
    const fs::path dir =
        fs::temp_directory_path(ec) /
        ("nota-test-io-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    nota::core::cfgfile::setDataDirOverride(dir.string());

    AudioConfig ac;
    ac.outputDeviceUid = "dev-out";
    ac.inputDeviceUid = "dev-in";
    ac.sampleRate = 96000.0;
    ac.bufferFrames = 128;
    ac.asioDriverClsid = "{11111111-2222-3333-4444-555555555555}";
    ac.asioDriverName = "Fake ASIO";
    ac.asioOutputPair = 4;
    ac.asioInputPair = 2;
    saveAudioConfig(ac);
    const AudioConfig ab = loadAudioConfig();
    NOTA_CHECK(ab.outputDeviceUid == "dev-out" && ab.inputDeviceUid == "dev-in");
    NOTA_CHECK(ab.sampleRate == 96000.0 && ab.bufferFrames == 128);
    NOTA_CHECK(ab.asioOutputPair == 4 && ab.asioInputPair == 2);

    MidiConfig mc;
    mc.disabledInputUids = {"1234", "abcd"};
    mc.mpe = false;
    mc.mpeBendRange = 24;
    saveMidiConfig(mc);
    const MidiConfig mb = loadMidiConfig();
    NOTA_CHECK(mb.disabledInputUids == mc.disabledInputUids && !mb.mpe && mb.mpeBendRange == 24);

    nota::core::cfgfile::setDataDirOverride(saved);
    fs::remove_all(dir, ec);
}

void testMidiAndMpe() {
    registerBuiltinMidiDrivers();
    NOTA_CHECK(MidiDriverRegistry::instance().find("null") != nullptr);
    NullMidiDriver nullMidi;
    auto in = nullMidi.createInput();
    auto out = nullMidi.createOutput();
    NOTA_CHECK(in && out);
    NOTA_CHECK(in->open([](int32_t, int32_t, int32_t, int32_t, int32_t) {}, {}));
    in->close();
    NOTA_CHECK(!out->open("") && out->open("loop") && !out->send(nullptr, 0));

    MidiParser parser;
    int notes = 0;
    int sysexLen = 0;
    const uint8_t stream[] = {0x90, 60, 100, 64, 90, 0xF0, 0x7E, 0xF8, 0x06, 0x01, 0xF7};
    parser.feed(
        stream, sizeof(stream),
        [&](int status, int, int, int) {
            if (status == 0x90) ++notes;
        },
        [&](const uint8_t*, int32_t len) { sysexLen = len; });
    NOTA_CHECK(notes == 2 && sysexLen == 5);

    struct Rec final : MpeInput::Sink {
        int notesOn = 0;
        int notesOff = 0;
        float lastBend = 0.0f;
        float lastPressure = 0.0f;
        float lastSustain = 0.0f;
        void mpeNoteOn(int32_t, float) override { ++notesOn; }
        void mpeNoteOff(int32_t) override { ++notesOff; }
        void mpeExpression(int32_t, int32_t dim, float v) override {
            if (dim == nota::dsp::ExprBend) lastBend = v;
            else if (dim == nota::dsp::ExprPressure)
                lastPressure = v;
        }
        void mpeController(int32_t ctl, float v) override {
            if (ctl == nota::dsp::kCtlSustain) lastSustain = v;
        }
    };

    MpeInput mpe;
    mpe.configure(true, 48);
    Rec rec;
    mpe.message(0xE0, 1, 0x7F, 0x7F, rec);
    mpe.message(0xD0, 1, 64, 0, rec);
    mpe.message(0x90, 1, 60, 127, rec);
    mpe.message(0xB0, 1, 64, 127, rec);
    NOTA_CHECK(rec.notesOn == 1 && rec.lastBend > 47.0f && rec.lastPressure > 0.49f && rec.lastSustain == 1.0f);
    mpe.message(0x90, 1, 60, 0, rec);
    NOTA_CHECK(rec.notesOff == 1);
}

class MockGamepadDriver final : public IGamepadDriver {
  public:
    std::string_view id() const noexcept override { return "mock"; }
    std::string_view displayName() const noexcept override { return "Mock Pad"; }
    bool start(GamepadSink& sink) override {
        sink_ = &sink;
        sink_->onPadsChanged({GamepadDeviceInfo{"mock-1", "Mock Controller"}});
        sink_->onAxisValue(0, GamepadAxis::LeftX, 127);
        sink_->onAxisValue(0, GamepadAxis::RightTrigger, 80);
        sink_->onButtonEdge(0, 1, true);
        sink_->onButtonEdge(0, GamepadButton::DpadUp, false);
        return true;
    }
    void stop() override {
        if (sink_) sink_->onPadsChanged({});
        sink_ = nullptr;
    }

  private:
    GamepadSink* sink_ = nullptr;
};

void testGamepad() {
    auto drv = std::make_unique<MockGamepadDriver>();
    GamepadInput gp(std::move(drv));
    gp.start();
    NOTA_CHECK(gp.padCount() == 1);
    const char* uid = nullptr;
    const char* name = nullptr;
    gp.padInfo(0, &uid, &name);
    NOTA_CHECK(std::string(uid) == "mock-1" && std::string(name) == "Mock Controller");

    GamepadButtonEvent ev[4]{};
    const int32_t n = gp.pollEvents(ev, 4);
    NOTA_CHECK(n == 2);
    NOTA_CHECK(ev[0].pressed == 1 && ev[0].buttonId == 1 && ev[0].pad == 0);
    NOTA_CHECK(ev[1].pressed == 0 && ev[1].buttonId == GamepadButton::DpadUp);

    int32_t axes[GamepadAxis::Count]{};
    NOTA_CHECK(gp.axisValues(0, axes, GamepadAxis::Count) == GamepadAxis::Count);
    NOTA_CHECK(axes[GamepadAxis::LeftX] == 127);
    NOTA_CHECK(axes[GamepadAxis::LeftY] == 64);
    NOTA_CHECK(axes[GamepadAxis::RightTrigger] == 80);

    gp.stop();
    NOTA_CHECK(gp.padCount() == 0);
}

} // namespace

int main() {
    testAudioRegistryAndNullDriver();
    testAsioFormats();
    testAsioRoutingAndDuplex();
    testAsioResetsAndFailures();
    testConfigStores();
    testMidiAndMpe();
    testGamepad();
    return 0;
}
