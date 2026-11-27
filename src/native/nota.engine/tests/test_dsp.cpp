// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/Device.h"
#include "nota/dsp/DeviceRegistry.h"
#include "nota/dsp/EditorPreview.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/MidiDevice.h"
#include "nota/dsp/MidiTypes.h"
#include "nota/dsp/capabilities/ISampleInstrument.h"
#include "nota/dsp/capabilities/IWavetableInstrument.h"
#include "nota/dsp/poly/PolyVoiceManager.h"
#include "nota/dsp/poly/SynthVoice.h"
#include "nota/dsp/primitives/BiquadCascade.h"
#include "nota/dsp/primitives/Convolution.h"
#include "nota/dsp/primitives/Oversampler.h"
#include "nota/dsp/primitives/SpectrumAnalyzer.h"
#include "nota/dsp/rt/OutputRouting.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/SnapshotHistory.h"
#include "nota/dsp/rt/SpscRingBuffer.h"
#include "nota/media/SampleBuffer.h"

#include <array>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <memory>
#include <utility>
#include <string>
#include <vector>

namespace {

int gFail = 0;

#define CHECK(cond)                                                                                                    \
    do {                                                                                                               \
        if (!(cond)) {                                                                                                 \
            std::fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond);                                       \
            ++gFail;                                                                                                   \
        }                                                                                                              \
    } while (false)

class DummyDevice final : public nota::dsp::Device {
  public:
    void setSampleRate(double /*sr*/, int32_t /*maxBlock*/) override {}
    void process(float* /*buf*/, int32_t /*frames*/) override {}
    int32_t builtinKind() const override { return 99; }
};

class DummyInstrument final : public nota::dsp::Instrument, public nota::dsp::capabilities::ISampleInstrument {
  public:
    void setSampleRate(double /*sr*/) override {}
    void noteOn(int32_t /*pitch*/, float /*vel*/) override {}
    void noteOff(int32_t /*pitch*/) override {}
    void allNotesOff() override {}
    void render(float* /*out*/, int32_t /*frames*/) override {}
    int32_t kind() const override { return 77; }

    void setSample(std::shared_ptr<nota::media::SampleBuffer> s, int32_t r, bool l) override {
        smp_ = std::move(s);
        root_ = r;
        loop_ = l;
    }
    void setRoot(int32_t r) override { root_ = r; }
    std::shared_ptr<nota::media::SampleBuffer> sample() const override { return smp_; }
    int32_t rootNote() const override { return root_; }
    bool loopEnabled() const override { return loop_; }

  private:
    std::shared_ptr<nota::media::SampleBuffer> smp_;
    int32_t root_ = 60;
    bool loop_ = false;
};

class DummyMidi final : public nota::dsp::MidiDevice {
  public:
    void process(const nota::dsp::MidiEv* /*in*/, int /*nIn*/, nota::dsp::MidiEv* /*out*/, int& nOut, int /*maxOut*/,
        int32_t /*frames*/, double /*beatStart*/, double /*spb*/, bool /*playing*/) override {
        nOut = 0;
    }
    int32_t midiKind() const override { return 55; }
};

void testRegistryAndCapabilities() {
    nota::dsp::DeviceRegistry::clearForTests();
    nota::dsp::DeviceRegistry::registerAudioEffect(99, [] { return std::make_shared<DummyDevice>(); }, true);
    nota::dsp::DeviceRegistry::registerInstrument(77, [] { return std::make_shared<DummyInstrument>(); });
    nota::dsp::DeviceRegistry::registerMidiEffect(55, [] { return std::make_shared<DummyMidi>(); });

    CHECK(nota::dsp::DeviceRegistry::hasAudioEffect(99));
    CHECK(nota::dsp::DeviceRegistry::hasChainEffect(99));
    CHECK(nota::dsp::DeviceRegistry::hasInstrument(77));
    CHECK(nota::dsp::DeviceRegistry::hasMidiEffect(55));

    auto d = nota::dsp::DeviceRegistry::createAudioEffect(99);
    auto dc = nota::dsp::DeviceRegistry::createChainEffect(99);
    auto i = nota::dsp::DeviceRegistry::createInstrument(77);
    auto m = nota::dsp::DeviceRegistry::createMidiEffect(55);
    CHECK(d != nullptr && d->builtinKind() == 99);
    CHECK(dc != nullptr && dc->builtinKind() == 99);
    CHECK(i != nullptr && i->kind() == 77);
    CHECK(m != nullptr && m->midiKind() == 55);

    auto* cap = dynamic_cast<nota::dsp::capabilities::ISampleInstrument*>(i.get());
    CHECK(cap != nullptr);
    cap->setRoot(48);
    CHECK(cap->rootNote() == 48);
    nota::dsp::DeviceRegistry::clearForTests();
}

void testSingleProcessCounters() {
    CHECK(!nota::dsp::anyOpenPluginEditor());
    CHECK(!nota::dsp::anyNeverSleepDevice());
    CHECK(!nota::dsp::heldMidiKeysRead());

    {
        nota::dsp::HeldMidiKeyReader reader;
        CHECK(nota::dsp::heldMidiKeysRead());
        nota::dsp::SilenceGate gate(128);
        gate.setHoldAwake(true);
        CHECK(nota::dsp::anyOpenPluginEditor());

        DummyDevice dev;
        dev.setSleepMode(nota::dsp::PluginSleepMode::Never);
        CHECK(nota::dsp::anyNeverSleepDevice());
    }

    CHECK(!nota::dsp::heldMidiKeysRead());
    CHECK(!nota::dsp::anyOpenPluginEditor());
    CHECK(!nota::dsp::anyNeverSleepDevice());

    const uint64_t l1 = nota::dsp::nextDeviceLineage();
    const uint64_t l2 = nota::dsp::nextDeviceLineage();
    CHECK(l2 > l1);

    const auto p1 = nota::dsp::EditorPreview::make({1, 2, 3});
    const auto p2 = nota::dsp::EditorPreview::make({4, 5});
    CHECK(!p1.empty() && p2.serial > p1.serial);
}

void testRtContainersAndRouting() {
    nota::dsp::CommandQueue q;
    CHECK(q.push({nota::dsp::CommandType::TransportPlay, 1, 2, 120.0, 0.0, 0.5f}));
    nota::dsp::Command cmd;
    CHECK(q.pop(cmd) && cmd.type == nota::dsp::CommandType::TransportPlay && cmd.i0 == 1);
    CHECK(!q.pop(cmd));

    nota::dsp::SnapshotHistory<int> hist;
    hist.publish(std::make_shared<int>(42));
    CHECK(hist.live() != nullptr && *hist.live() == 42);
    {
        nota::dsp::SnapshotHistory<int>::RenderScope scope(hist);
        CHECK(!hist.hasPendingRetire());
    }
    hist.publish(std::make_shared<int>(99));
    CHECK(*hist.live() == 99 && hist.hasPendingRetire());
    { nota::dsp::SnapshotHistory<int>::RenderScope s1(hist); }
    { nota::dsp::SnapshotHistory<int>::RenderScope s2(hist); }
    CHECK(!hist.hasPendingRetire());

    const nota::dsp::OutputRouting r{0, 2, 2, 2};
    CHECK(nota::dsp::validRouting(r));
    const auto resolved = nota::dsp::resolveRouting(r, 4);
    CHECK(resolved.mainWidth == 2 && resolved.cueFirst == 2 && resolved.cueWidth == 2 && resolved.flags == 0);
}

void testDspPrimitivesAndWavetable() {
    // Biquad + SVF + Ladder + DelayLine + Oversampler + TruePeakMeter
    nota::dsp::BiquadCascade eq;
    eq.setStages(1);
    eq.setSection(0, nota::dsp::BiquadCascade::lowpass(48000.0, 1000.0, 0.7071));
    std::array<float, 64> stereo{};
    stereo[0] = 1.0f;
    stereo[1] = 1.0f;
    eq.processBlock(stereo.data(), 32, nota::dsp::BiquadChannel::Stereo);
    CHECK(std::isfinite(stereo[0]) && stereo[0] > 0.0f && stereo[0] < 1.0f);

    nota::dsp::Oversampler os;
    os.prepare(32);
    os.setActive(4);
    std::array<float, 64> osBuf{};
    osBuf[0] = 0.5f;
    osBuf[1] = -0.5f;
    os.process(osBuf.data(), 32, [](float& l, float& r, int32_t) {
        l *= 0.5f;
        r *= 0.5f;
    });
    CHECK(std::isfinite(osBuf[0]) && std::fabs(osBuf[0]) < 1.0f);

    // ConvKernel impulse response test (delta kernel => output matches input)
    std::vector<std::vector<float>> ir(1, std::vector<float>(256, 0.0f));
    ir[0][0] = 1.0f;
    auto kernel = nota::dsp::ConvKernel::build(ir, {{0, 0, 0}, {0, 1, 0}}, 1, false, true, 128, 48000.0);
    CHECK(kernel != nullptr && kernel->latency == 0);
    std::array<float, 128> inL{}, inR{}, outL{}, outR{};
    inL[0] = 0.75f;
    kernel->process(inL.data(), inR.data(), outL.data(), outR.data(), 128);
    CHECK(std::fabs(outL[0] - 0.75f) < 1e-4f);
    CHECK(std::fabs(outR[0] - 0.75f) < 1e-4f);

    // SpectrumAnalyzer 1 kHz sine peak
    nota::dsp::SpectrumAnalyzer spec;
    spec.prepare(48000.0);
    constexpr uint32_t kRing = 4096u;
    std::vector<float> ring(kRing, 0.0f);
    for (uint32_t i = 0; i < kRing; ++i) {
        ring[i] = std::sin(2.0f * 3.14159265f * 1000.0f * static_cast<float>(i) / 48000.0f);
    }
    spec.analyse(ring.data(), kRing - 1u, kRing, 0.0, false);
    CHECK(spec.valid());
    int32_t maxIdx = 0;
    for (int32_t i = 1; i < nota::dsp::SpectrumAnalyzer::kPoints; ++i) {
        if (spec.curve()[i] > spec.curve()[maxIdx]) maxIdx = i;
    }
    CHECK(maxIdx > 100 && maxIdx < 180);

    // PolyVoiceManager
    nota::dsp::PolyVoiceManager<nota::dsp::SynthVoice, 8> pvm;
    auto* v = pvm.noteOn(60, 0.8f);
    CHECK(v != nullptr && v->pitch == 60 && pvm.activeCount() == 1);
    pvm.noteOff(60);
    CHECK(pvm.voices()[0].released);

    // WavetableAsset round-trip
    std::vector<float> cycle(2048, 0.0f);
    for (std::size_t i = 0; i < cycle.size(); ++i) {
        cycle[i] = std::sin(2.0f * 3.14159265f * static_cast<float>(i) / 2048.0f);
    }
    std::shared_ptr<const nota::dsp::WavetableAsset> wt;
    const int32_t res = nota::dsp::WavetableAsset::buildFromMono(
        cycle.data(), static_cast<int64_t>(cycle.size()), 2048, "sine", false, wt);
    CHECK(res == nota::dsp::WavetableAsset::Ok && wt != nullptr && wt->frames == 1);
}

} // namespace

int main() {
    testRegistryAndCapabilities();
    testSingleProcessCounters();
    testRtContainersAndRouting();
    testDspPrimitivesAndWavetable();
    return gFail == 0 ? 0 : 1;
}
