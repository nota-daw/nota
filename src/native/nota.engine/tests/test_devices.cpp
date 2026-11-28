// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Tests for nota::devices module: registration, slugs, capabilities, racks & DSP self-tests.

#include "nota/devices/DeviceIds.h"
#include "nota/devices/Register.h"
#include "nota/devices/racks/DrumRack.h"
#include "nota/devices/racks/MidiRack.h"
#include "nota/devices/racks/RackDevice.h"
#include "nota/devices/racks/RackInstrument.h"
#include "nota/dsp/DeviceRegistry.h"
#include "nota/dsp/MidiTypes.h"
#include "nota/dsp/capabilities/IMosaicInstrument.h"
#include "nota/dsp/capabilities/IRhythmInstrument.h"
#include "nota/dsp/capabilities/ISampleInstrument.h"
#include "nota/dsp/capabilities/ISlotAnnotationDevice.h"
#include "nota/dsp/capabilities/IWavetableInstrument.h"
#include "nota/media/SampleBuffer.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <memory>
#include <vector>

namespace {

int gFailures = 0;

void expect(bool cond, const char* msg) {
    if (!cond) {
        std::fprintf(stderr, "FAIL: %s\n", msg);
        ++gFailures;
    }
}

using namespace nota::devices;
using nota::dsp::DeviceRegistry;
namespace caps = nota::dsp::capabilities;

void testRegistrationAndSlugs() {
    registerBuiltinDevices();
    expect(builtinDevicesRegistered(), "devices registered");
    registerBuiltinDevices(); // idempotent
    expect(builtinDevicesRegistered(), "devices still registered after second call");

    const auto fxKinds = DeviceRegistry::audioEffectKinds();
    const auto chainFxKinds = DeviceRegistry::chainEffectKinds();
    const auto instKinds = DeviceRegistry::instrumentKinds();
    const auto midiKinds = DeviceRegistry::midiEffectKinds();

    expect(fxKinds.size() == 28u, "28 audio effect kinds registered");
    expect(chainFxKinds.size() == 27u, "27 chain audio effect kinds registered (EffectRack excluded)");
    expect(instKinds.size() == 19u, "19 instrument kinds registered");
    expect(midiKinds.size() == 7u, "7 MIDI effect kinds registered");

    expect(DeviceRegistry::createChainEffect(static_cast<int32_t>(AudioEffectKind::EffectRack)) == nullptr,
        "EffectRack excluded from chain effect factory");

    for (const int32_t k : fxKinds) {
        const auto slug = audioEffectSlug(k);
        expect(!slug.empty(), "audio effect slug non-empty");
        expect(audioEffectKindFromSlug(slug) == k, "audio effect slug round-trip");
        auto dev = DeviceRegistry::createAudioEffect(k);
        expect(dev != nullptr, "createAudioEffect returns instance");
        dev->setSampleRate(48000.0, 256);
        std::vector<float> buf(256u * 2u, 0.0f);
        dev->process(buf.data(), 256);
        for (float v : buf) expect(std::isfinite(v), "effect output finite");
    }

    for (const int32_t k : instKinds) {
        const auto slug = instrumentSlug(k);
        expect(!slug.empty(), "instrument slug non-empty");
        expect(instrumentKindFromSlug(slug) == k, "instrument slug round-trip");
        auto inst = DeviceRegistry::createInstrument(k);
        expect(inst != nullptr, "createInstrument returns instance");
        inst->setSampleRate(48000.0);
        inst->noteOn(60, 0.8f);
        std::vector<float> buf(256u * 2u, 0.0f);
        inst->render(buf.data(), 256);
        inst->noteOff(60);
        inst->allNotesOff();
        for (float v : buf) expect(std::isfinite(v), "instrument render finite");
    }

    for (const int32_t k : midiKinds) {
        const auto slug = midiEffectSlug(k);
        expect(!slug.empty(), "midi effect slug non-empty");
        expect(midiEffectKindFromSlug(slug) == k, "midi effect slug round-trip");
        auto md = DeviceRegistry::createMidiEffect(k);
        expect(md != nullptr, "createMidiEffect returns instance");
        md->setSampleRate(48000.0, 256);
        nota::dsp::MidiEv in[2] = {{0, true, 60, 0.9f, 0.0f}, {128, false, 60, 0.0f, 0.0f}};
        nota::dsp::MidiEv out[32] = {};
        int nOut = 0;
        md->process(in, 2, out, nOut, 32, 256, 0.0, 24000.0, false);
        expect(nOut >= 0 && nOut <= 32, "midi process output count bounded");
        md->reset();
    }
}

void testCapabilities() {
    // ISlotAnnotationDevice (Eq)
    {
        auto eq = DeviceRegistry::createAudioEffect(static_cast<int32_t>(AudioEffectKind::Eq));
        auto* ann = dynamic_cast<caps::ISlotAnnotationDevice*>(eq.get());
        expect(ann != nullptr && ann->slotAnnotationCount() == 8, "Eq exposes 8 slot annotations");
        ann->setSlotAnnotationText(0, "Body", "Warm low shelf");
        expect(ann->slotAnnotationLabel(0) == "Body", "Eq slot label preserved");
        expect(ann->slotAnnotationNote(0) == "Warm low shelf", "Eq slot note preserved");
        const auto state = eq->getState();
        auto eq2 = DeviceRegistry::createAudioEffect(static_cast<int32_t>(AudioEffectKind::Eq));
        eq2->setState(state.data(), static_cast<int32_t>(state.size()));
        auto* ann2 = dynamic_cast<caps::ISlotAnnotationDevice*>(eq2.get());
        expect(ann2 && ann2->slotAnnotationLabel(0) == "Body", "Eq annotation survives state round-trip");
    }

    // ISampleInstrument (Sampler & GrainSynth)
    {
        auto buf = std::make_shared<nota::media::SampleBuffer>();
        buf->channels = 1;
        buf->frames = 512;
        buf->sourceSampleRate = 48000.0;
        buf->samples.assign(512u, 0.25f);
        buf->buildPeakTable();

        auto smp = DeviceRegistry::createInstrument(static_cast<int32_t>(InstrumentKind::Sampler));
        auto* cap = dynamic_cast<caps::ISampleInstrument*>(smp.get());
        expect(cap != nullptr, "Sampler exposes ISampleInstrument");
        cap->setSample(buf, 62, true);
        expect(cap->sample() == buf && cap->rootNote() == 62 && cap->loopEnabled(), "Sampler capability round-trip");

        auto gr = DeviceRegistry::createInstrument(static_cast<int32_t>(InstrumentKind::GrainSynth));
        auto* gcap = dynamic_cast<caps::ISampleInstrument*>(gr.get());
        expect(gcap != nullptr, "GrainSynth exposes ISampleInstrument");
        gcap->setSample(buf, 64, false);
        expect(gcap->sample() == buf && gcap->rootNote() == 64 && !gcap->loopEnabled(), "Grain capability round-trip");
    }

    // IMosaicInstrument
    {
        setMosaicRoot("samples", "/tmp/nonexistent-samples");
        auto mos = DeviceRegistry::createInstrument(static_cast<int32_t>(InstrumentKind::Mosaic));
        auto* mcap = dynamic_cast<caps::IMosaicInstrument*>(mos.get());
        expect(mcap != nullptr, "Mosaic exposes IMosaicInstrument");
        mcap->setProgram("mosaic 1\n");
        expect(!mcap->programText().empty(), "Mosaic stores program text");
    }

    // IWavetableInstrument
    {
        auto wt = DeviceRegistry::createInstrument(static_cast<int32_t>(InstrumentKind::WavetableSynth));
        auto* wcap = dynamic_cast<caps::IWavetableInstrument*>(wt.get());
        expect(wcap != nullptr, "WavetableSynth exposes IWavetableInstrument");
        std::vector<float> sine(static_cast<size_t>(nota::dsp::WavetableAsset::kSize), 0.0f);
        for (size_t i = 0; i < sine.size(); ++i)
            sine[i] = std::sin(2.0f * 3.14159265f * static_cast<float>(i) / static_cast<float>(sine.size()));
        std::shared_ptr<const nota::dsp::WavetableAsset> asset;
        const auto rc = nota::dsp::WavetableAsset::buildFromFrames(
            sine.data(), 1, "TestSine", nota::dsp::WavetableAsset::SingleCycle, asset);
        expect(rc == nota::dsp::WavetableAsset::Ok && asset != nullptr, "build WavetableAsset");
        wcap->setWavetable(0, asset);
        expect(wcap->wavetable(0) == asset && wcap->usesCustomWavetable(0), "Wavetable set and selected");
        const auto state = wcap->serializeState(true);
        expect(!state.empty(), "Wavetable state serialized");
    }

    // IRhythmInstrument
    {
        auto rh = DeviceRegistry::createInstrument(static_cast<int32_t>(InstrumentKind::RhythmMachine));
        auto* rcap = dynamic_cast<caps::IRhythmInstrument*>(rh.get());
        expect(rcap != nullptr && rcap->rhythmVoiceCount() == 8, "Rhythm exposes IRhythmInstrument");
        rcap->setKitName("Factory 909");
        expect(rcap->kitName() == "Factory 909", "Rhythm kit name set");
        const int32_t devIdx = rcap->addVoiceDevice(0, static_cast<int32_t>(AudioEffectKind::Eq));
        expect(devIdx == 0 && rcap->voiceDeviceCount(0) == 1 && rcap->voiceDevice(0, 0) != nullptr,
            "Rhythm voice FX created via DeviceRegistry");
        const int32_t mapIdx = rcap->addMacroMapping(0, 0, 0, 2, 100.0f, 5000.0f);
        caps::RhythmMacroMap m{};
        expect(mapIdx == 0 && rcap->macroMapping(0, m) && m.device == 0 && m.param == 2,
            "Rhythm macro mapping round-trip");
        expect(rcap->removeVoiceDevice(0, 0) && rcap->voiceDeviceCount(0) == 0, "Rhythm voice FX removed");
    }
}

void testRacksAndHelpers() {
    auto rackDev = std::make_shared<RackDevice>();
    expect(rackDev->asRackCapability() != nullptr, "RackDevice implements IRack");
    const int32_t c0 = rackDev->addChain(-1);
    expect(c0 == 0 && rackDev->chainCount() == 1, "RackDevice adds chain");
    const int32_t d0 = rackDev->addChainDevice(c0, static_cast<int32_t>(AudioEffectKind::Delay));
    expect(d0 == 0 && rackDev->chainDeviceCount(c0) == 1, "RackDevice adds chain Delay");
    const auto blob = rackDev->getState();

    auto rackDev2 = std::make_shared<RackDevice>();
    rackDev2->setState(blob.data(), static_cast<int32_t>(blob.size()));
    expect(rackDev2->chainCount() == 1 && rackDev2->chainDeviceCount(0) == 1, "RackDevice state round-trip");

    auto instRack = std::make_shared<RackInstrument>();
    const int32_t ic0 = instRack->addChain(static_cast<int32_t>(InstrumentKind::Synth));
    expect(ic0 == 0 && instRack->chainInstrumentKind(0) == 0, "RackInstrument chain Synth");
    expect(instRack->addChain(static_cast<int32_t>(InstrumentKind::InstrumentRack)) == -1 ||
               instRack->chainInstrument(1) == nullptr,
        "InstrumentRack never nests inside another rack");

    auto drumRack = std::make_shared<DrumRack>();
    const int32_t pad0 = drumRack->addChain(static_cast<int32_t>(InstrumentKind::Sampler));
    drumRack->setChainTriggerNote(pad0, 36);
    drumRack->setChainChoke(pad0, 1);
    drumRack->setSampleRate(48000.0);
    drumRack->noteOn(36, 0.9f);
    std::vector<float> out(256u * 2u, 0.0f);
    drumRack->render(out.data(), 256);
    drumRack->allNotesOff();

    auto midiRack = std::make_shared<MidiRack>();
    expect(midiRack->chainCount() == 1, "MidiRack starts with one chain");
    expect(midiRack->addChainMidiDevice(0, static_cast<int32_t>(MidiEffectKind::MidiScale)) == 0,
        "MidiRack adds chain MIDI effect");

    expect(twinParamFromV1(0) == 0, "twinParamFromV1 global index preserved");
    expect(twinParamFromV1(600) > 600, "twinParamFromV1 shifts post-v1 indices");
    expect(reverbCombWrapSelfTest(), "Reverb comb wrap self-test passes");
    expect(runBuiltinFxSelfTest(), "Built-in FX + SIMD self-test passes");
}

} // namespace

int main() {
    testRegistrationAndSlugs();
    testCapabilities();
    testRacksAndHelpers();
    return gFailures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
