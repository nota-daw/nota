// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackCore implementation — method bodies
// . Concrete instrument headers live only here, so
// consumers of RackCore.h no longer parse 15 synth headers.

#include "nota/devices/racks/RackCore.h"
#include "common/DeviceMath.h"
#include <memory>
#include <cstdint>
#include <cstddef>
#include <atomic>
#include <algorithm>
#include <nota/dsp/DeviceRegistry.h>
#include <nota/dsp/capabilities/ISampleInstrument.h>

#include "nota/dsp/rt/DeviceTelemetry.h"
#include <nota/dsp/DeviceRegistry.h>

#include <vector>
#include <utility>
#include <string>

namespace nota::devices {

RackCore::PluginFactory& RackCore::pluginFactory() {
    static PluginFactory f;
    return f;
}

uint64_t RackCore::nextChainUid() noexcept {
    static std::atomic<uint64_t> next{1};
    return next.fetch_add(1, std::memory_order_relaxed);
}

RackCore::RackCore() {
    for (auto& m : macros_) m.store(0.0f, std::memory_order_relaxed);
    commit(std::make_shared<RackState>());
}

RackCore::~RackCore() = default;

void RackCore::forwardTransportSlow(RackState& st, const TransportInfo& ti) {
    for (auto& c : st.chains) {
        if (c.instrument) c.instrument->setTransportInfo(ti);
        for (auto& d : c.devices)
            if (d) d->setTransportInfo(ti);
    }
}

bool RackCore::chainsHostPlugin() const {
    const RackState* st = live();
    if (!st) return false;
    for (const auto& c : st->chains) {
        if (c.instrument && c.instrument->hostsPlugin()) return true;
        for (const auto& d : c.devices)
            if (d && d->hostsPlugin()) return true;
    }
    return false;
}

void RackCore::forwardTempoSlow(RackState& st, double beatStart, double spb, bool playing) {
    for (auto& c : st.chains) {
        if (c.instrument) c.instrument->setTransport(beatStart, spb, playing);
        for (auto& d : c.devices)
            if (d) d->setTransport(beatStart, spb, playing);
    }
}

void RackCore::prepareParallelScratch(const RackState* st, int32_t minFrames) {
    if (!st) return;
    const int32_t n = static_cast<int32_t>(st->chains.size());
    if (n >= 2) parallelArena_.prepare(n, std::max(minFrames, maxBlock_));
}

bool RackCore::chainSupportsParallel(const RackChain& c) {
    if (c.instrument && !c.instrument->supportsParallelRender()) return false;
    for (const auto& d : c.devices)
        if (d && !d->bypassed() && !d->supportsParallelRender()) return false;
    return true;
}

void RackCore::setRates(double sr, int32_t maxBlock) {
    sampleRate_ = sr > 0 ? sr : 44100.0;
    maxBlock_ = maxBlock > 0 ? maxBlock : kMaxBlock;
    auto st = authoring_;
    if (!st) return;
    prepareParallelScratch(st.get(), maxBlock_);
    for (auto& c : st->chains) {
        if (c.instrument) c.instrument->setSampleRate(sampleRate_);
        for (auto& d : c.devices)
            if (d) d->setSampleRate(sampleRate_, maxBlock_);
        for (auto& m : c.midiDevices)
            if (m) m->setSampleRate(sampleRate_, maxBlock_);
    }
}

void RackCore::ensureChainBlockCapacity(int32_t frames) {
    if (frames <= 0) return;
    auto st = authoring_;
    if (!st) return;
    prepareParallelScratch(st.get(), frames);
    for (auto& c : st->chains) {
        if (c.instrument) c.instrument->ensureBlockCapacity(frames);
        for (auto& d : c.devices)
            if (d) d->ensureBlockCapacity(frames);
    }
}

void RackCore::forkFrom(const RackCore& o) {
    auto ns = o.copyState();
    for (auto& c : ns->chains) {
        auto ctl = std::make_shared<ChainControls>();
        if (c.ctl) {
            const auto& s = *c.ctl;
            constexpr auto r = std::memory_order_relaxed;
            ctl->gain.store(s.gain.load(r), r);
            ctl->pan.store(s.pan.load(r), r);
            ctl->mute.store(s.mute.load(r), r);
            ctl->solo.store(s.solo.load(r), r);
            ctl->triggerNote.store(s.triggerNote.load(r), r);
            ctl->keyLo.store(s.keyLo.load(r), r);
            ctl->keyHi.store(s.keyHi.load(r), r);
            ctl->velLo.store(s.velLo.load(r), r);
            ctl->velHi.store(s.velHi.load(r), r);
            ctl->chokeGroup.store(s.chokeGroup.load(r), r);
            ctl->tune.store(s.tune.load(r), r);
            ctl->decay.store(s.decay.load(r), r);
            ctl->ampEnv.store(s.ampEnv.load(r), r); // a ringing pad keeps its level
            ctl->selLo.store(s.selLo.load(r), r);
            ctl->selHi.store(s.selHi.load(r), r);
            ctl->transpose.store(s.transpose.load(r), r);
            ctl->velScale.store(s.velScale.load(r), r);
        }
        c.ctl = std::move(ctl);
    }
    commit(ns);
    constexpr auto r = std::memory_order_relaxed;
    for (int32_t i = 0; i < kNumMacros; ++i) {
        macros_[i].store(o.macros_[i].load(r), r);
        macroNames_[i] = o.macroNames_[i];
    }
    rackVolume_.store(o.rackVolume_.load(r), r);
    glide_.store(o.glide_.load(r), r);
    swing_.store(o.swing_.load(r), r);
    humanize_.store(o.humanize_.load(r), r);
    mode_.store(o.mode_.load(r), r);
    dryWet_.store(o.dryWet_.load(r), r);
    pdc_.store(o.pdc_.load(r), r);
    chainSelect_.store(o.chainSelect_.load(r), r);
    selFollow_.store(o.selFollow_.load(r), r);
    liveSel_.store(o.liveSel_.load(r), r);
    mono_.store(o.mono_.load(r), r);
    merge_.store(o.merge_.load(r), r);
    thru_.store(o.thru_.load(r), r);
    sampleRate_ = o.sampleRate_;
    maxBlock_ = o.maxBlock_;
}

void RackCore::propagateTelemetry(RackState& st) const {
    for (auto& c : st.chains) {
        if (c.instrument) c.instrument->setTelemetryMode(telemetryMode_);
        for (auto& d : c.devices)
            if (d) d->setTelemetryMode(telemetryMode_);
    }
}

void RackCore::setTelemetryMode(dsp::TelemetryMode mode) {
    telemetryMode_ = mode;
    if (authoring_) propagateTelemetry(*authoring_);
}

bool RackCore::setChainName(int32_t c, const std::string& name) {
    auto ns = copyState();
    if (c < 0 || c >= static_cast<int32_t>(ns->chains.size())) return false;
    ns->chains[static_cast<size_t>(c)].name = name;
    commit(ns);
    return true;
}

int32_t RackCore::addChain(int32_t instKind) {
    auto inst = makeInstrument(instKind);
    if (inst) inst->setSampleRate(sampleRate_);
    auto ns = copyState();
    RackChain c;
    c.instrument = inst;
    ns->chains.push_back(std::move(c));
    const int32_t idx = static_cast<int32_t>(ns->chains.size()) - 1;
    commit(ns);
    return idx;
}

int32_t RackCore::addChainInstrument(std::shared_ptr<Instrument> inst) {
    if (inst) inst->setSampleRate(sampleRate_);
    auto ns = copyState();
    RackChain c;
    c.instrument = std::move(inst);
    ns->chains.push_back(std::move(c));
    const int32_t idx = static_cast<int32_t>(ns->chains.size()) - 1;
    commit(ns);
    return idx;
}

bool RackCore::setChainInstrumentSample(int32_t chain, std::shared_ptr<SampleBuffer> buf, int32_t root, bool loop) {
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    auto* oldInst = ns->chains[static_cast<size_t>(chain)].instrument.get();
    auto* oldSm = dynamic_cast<dsp::capabilities::ISampleInstrument*>(oldInst);
    std::shared_ptr<dsp::Instrument> fresh = oldSm ? oldInst->clone() : dsp::DeviceRegistry::createInstrument(1);
    auto* sm = dynamic_cast<dsp::capabilities::ISampleInstrument*>(fresh.get());
    if (!sm) return false;
    fresh->setSampleRate(sampleRate_);
    sm->setSample(std::move(buf), root, loop);
    if (oldSm) fresh->presetTag = oldInst->presetTag; // still the preset it was loaded from (specs/048)
    ns->chains[static_cast<size_t>(chain)].instrument = std::move(fresh);
    commit(ns);
    return true;
}

bool RackCore::removeChain(int32_t chain) {
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    ns->chains.erase(ns->chains.begin() + chain);
    pruneMappings(ns, chain);
    commit(ns);
    return true;
}

// What the blob leaves out because it points outside the rack: the children's sidechain keys
// and the Arps' CC targets. A target in the source chain itself moves to the copy.
void RackCore::copyExternalLinks(const RackChain& src, RackChain& dst) {
    if (src.devices.size() == dst.devices.size())
        for (size_t i = 0; i < src.devices.size(); ++i) {
            const Device* s = src.devices[i].get();
            Device* d = dst.devices[i].get();
            if (!s || !d) continue;
            d->setSidechainSourceTrackId(s->sidechainSourceTrackId());
            d->setSidechainPart(s->sidechainPart());
            d->setSidechainTapPre(s->sidechainTapPre());
            d->setSidechainGainDb(s->sidechainGainDb());
            d->setSidechainMix(s->sidechainMix());
        }
    if (src.instrument && dst.instrument) {
        dst.instrument->setSidechainSourceTrackId(src.instrument->sidechainSourceTrackId());
        dst.instrument->setSidechainPart(src.instrument->sidechainPart());
    }
    if (src.midiDevices.size() == dst.midiDevices.size())
        for (size_t i = 0; i < src.midiDevices.size(); ++i) {
            const MidiDevice* s = src.midiDevices[i].get();
            MidiDevice* d = dst.midiDevices[i].get();
            if (!s || !d) continue;
            d->setCcDepth(s->ccDepth());
            if (s->ccDestDevice() < 0 && s->ccDestChainUid() == 0) continue;
            d->setCcDest(s->ccDestDevice(), s->ccDestParam());
            if (const uint64_t uid = s->ccDestChainUid())
                d->setCcDestNested(uid == src.uid ? dst.uid : uid, s->ccDestSlot());
        }
}

int32_t RackCore::duplicateChain(int32_t chain) {
    if (chain < 0 || chain >= chainCount()) return -1;
    // Rebuild the chain from this rack's own blob in a scratch rack: the blob already knows
    // how to recreate every child kind with all of its state.
    RackCore tmp;
    tmp.setRates(sampleRate_, maxBlock_);
    const auto blob = serialize();
    tmp.deserialize(blob.data(), static_cast<int32_t>(blob.size()));
    if (!tmp.authoring_ || chain >= static_cast<int32_t>(tmp.authoring_->chains.size())) return -1;
    auto ns = copyState();
    const int32_t at = chain + 1;
    RackChain& copy = tmp.authoring_->chains[static_cast<size_t>(chain)];
    copyExternalLinks(ns->chains[static_cast<size_t>(chain)], copy);
    ns->chains.insert(ns->chains.begin() + at, std::move(copy));
    std::vector<MacroMapping> copies;
    for (auto& m : ns->mappings) {
        if (m.chainIndex == chain) {
            MacroMapping c = m;
            c.chainIndex = at;
            copies.push_back(c);
        } else if (m.chainIndex >= at)
            ++m.chainIndex;
    }
    ns->mappings.insert(ns->mappings.end(), copies.begin(), copies.end());
    commit(ns);
    return at;
}

bool RackCore::setChainInstrument(int32_t chain, int32_t instKind) {
    auto inst = makeInstrument(instKind);
    if (!inst) return false;
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    inst->setSampleRate(sampleRate_);
    ns->chains[static_cast<size_t>(chain)].instrument = inst;
    ++ns->chains[static_cast<size_t>(chain)].instGen; // another instrument: windows on the old one let go
    commit(ns);
    return true;
}

bool RackCore::replaceChainInstrument(int32_t chain, std::shared_ptr<Instrument> inst, bool prepare) {
    if (!inst) return false;
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    if (prepare) inst->setSampleRate(sampleRate_);
    ns->chains[static_cast<size_t>(chain)].instrument = std::move(inst);
    commit(ns);
    return true;
}

void RackCore::reapplyMacros() {
    for (int32_t i = 0; i < kNumMacros; ++i) applyMacro(i, macros_[i].load(std::memory_order_relaxed));
    for (int32_t c = 0; c < chainCount(); ++c)
        if (auto* inner = dynamic_cast<RackCore*>(chainInstrument(c))) inner->reapplyMacros();
}

uint64_t RackCore::chainUid(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return 0;
    const auto& c = st->chains[static_cast<size_t>(chain)];
    return (c.uid << 16) ^ (c.instGen & 0xFFFF);
}

int32_t RackCore::chainInstrumentKind(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return -2;
    return st->chains[static_cast<size_t>(chain)].instrument ? st->chains[static_cast<size_t>(chain)].instrument->kind()
                                                             : -2;
}

Instrument* RackCore::chainInstrument(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
    return st->chains[static_cast<size_t>(chain)].instrument.get();
}

int32_t RackCore::chainDeviceCount(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return 0;
    return static_cast<int32_t>(st->chains[static_cast<size_t>(chain)].devices.size());
}

int32_t RackCore::addChainDevice(int32_t chain, int32_t deviceKind) {
    auto dev = makeDevice(deviceKind);
    if (!dev) return -1;
    return addChainDeviceInstance(chain, dev);
}

int32_t RackCore::addChainDeviceInstance(int32_t chain, std::shared_ptr<Device> dev, bool prepare) {
    if (!dev) return -1;
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return -1;
    if (prepare) dev->setSampleRate(sampleRate_, maxBlock_);
    ns->chains[static_cast<size_t>(chain)].devices.push_back(std::move(dev));
    const int32_t idx = static_cast<int32_t>(ns->chains[static_cast<size_t>(chain)].devices.size()) - 1;
    commit(ns);
    return idx;
}

bool RackCore::removeChainDevice(int32_t chain, int32_t dev) {
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    auto& d = ns->chains[static_cast<size_t>(chain)].devices;
    if (dev < 0 || dev >= static_cast<int32_t>(d.size())) return false;
    d.erase(d.begin() + dev);
    // Mappings follow their device: the removed one's go, the ones after it shift down.
    auto& mv = ns->mappings;
    for (auto it = mv.begin(); it != mv.end();) {
        if (it->chainIndex == chain && it->deviceIndex == dev) {
            it = mv.erase(it);
            continue;
        }
        if (it->chainIndex == chain && it->deviceIndex > dev) --it->deviceIndex;
        ++it;
    }
    commit(ns);
    return true;
}

bool RackCore::moveChainDevice(int32_t chain, int32_t from, int32_t to) {
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    auto& d = ns->chains[static_cast<size_t>(chain)].devices;
    const int32_t n = static_cast<int32_t>(d.size());
    if (from < 0 || from >= n) return false;
    if (to < 0) to = 0;
    if (to >= n) to = n - 1;
    if (to == from) return true;
    auto x = d[static_cast<size_t>(from)];
    d.erase(d.begin() + from);
    d.insert(d.begin() + to, x);
    for (auto& m : ns->mappings) {
        if (m.chainIndex != chain || m.deviceIndex < 0) continue;
        if (m.deviceIndex == from) m.deviceIndex = to;
        else if (from < to && m.deviceIndex > from && m.deviceIndex <= to)
            --m.deviceIndex;
        else if (to < from && m.deviceIndex >= to && m.deviceIndex < from)
            ++m.deviceIndex;
    }
    commit(ns);
    return true;
}

int32_t RackCore::insertChainDeviceInstance(int32_t chain, std::shared_ptr<Device> dev, int32_t at, bool prepare) {
    if (!dev) return -1;
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return -1;
    auto& d = ns->chains[static_cast<size_t>(chain)].devices;
    const int32_t n = static_cast<int32_t>(d.size());
    if (at < 0 || at > n) at = n;
    if (prepare) dev->setSampleRate(sampleRate_, maxBlock_);
    d.insert(d.begin() + at, std::move(dev));
    for (auto& m : ns->mappings)
        if (m.chainIndex == chain && m.deviceIndex >= at) ++m.deviceIndex;
    commit(ns);
    return at;
}

int32_t RackCore::moveChainDeviceToChain(int32_t chain, int32_t idx, int32_t toChain) {
    auto ns = copyState();
    const int32_t nc = static_cast<int32_t>(ns->chains.size());
    if (chain < 0 || chain >= nc || toChain < 0 || toChain >= nc || toChain == chain) return -1;
    auto& from = ns->chains[static_cast<size_t>(chain)].devices;
    if (idx < 0 || idx >= static_cast<int32_t>(from.size())) return -1;
    auto x = from[static_cast<size_t>(idx)];
    from.erase(from.begin() + idx);
    auto& to = ns->chains[static_cast<size_t>(toChain)].devices;
    const int32_t at = static_cast<int32_t>(to.size());
    to.push_back(std::move(x));
    for (auto& m : ns->mappings) {
        if (m.chainIndex != chain || m.deviceIndex < 0) continue;
        if (m.deviceIndex == idx) {
            m.chainIndex = toChain;
            m.deviceIndex = at;
        } else if (m.deviceIndex > idx) {
            --m.deviceIndex;
        }
    }
    commit(ns);
    return at;
}

Device* RackCore::chainDeviceAt(int32_t chain, int32_t dev) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
    auto& d = st->chains[static_cast<size_t>(chain)].devices;
    if (dev < 0 || dev >= static_cast<int32_t>(d.size())) return nullptr;
    return d[static_cast<size_t>(dev)].get();
}

std::shared_ptr<MidiDevice> RackCore::makeChainMidiDevice(int32_t kind) {
    // Built-in MIDI effects only; kind 6 is the MIDI Effect Rack itself, which never nests.
    if (kind < 0 || kind > 5) return nullptr;
    return dsp::DeviceRegistry::createMidiEffect(kind);
}

int32_t RackCore::chainMidiDeviceCount(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return 0;
    return static_cast<int32_t>(st->chains[static_cast<size_t>(chain)].midiDevices.size());
}

int32_t RackCore::addChainMidiDevice(int32_t chain, int32_t midiKind) {
    return addChainMidiDeviceInstance(chain, makeChainMidiDevice(midiKind));
}

int32_t RackCore::addChainMidiDeviceInstance(int32_t chain, std::shared_ptr<MidiDevice> dev) {
    if (!dev || dev->midiKind() < 0 || dev->midiKind() > 5) return -1;
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return -1;
    dev->setSampleRate(sampleRate_, maxBlock_);
    ns->chains[static_cast<size_t>(chain)].midiDevices.push_back(std::move(dev));
    const int32_t idx = static_cast<int32_t>(ns->chains[static_cast<size_t>(chain)].midiDevices.size()) - 1;
    commit(ns);
    return idx;
}

bool RackCore::removeChainMidiDevice(int32_t chain, int32_t idx) {
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    auto& d = ns->chains[static_cast<size_t>(chain)].midiDevices;
    if (idx < 0 || idx >= static_cast<int32_t>(d.size())) return false;
    d.erase(d.begin() + idx);
    // Mappings follow their MIDI effect: the removed one's go, the ones after it shift down.
    auto& mv = ns->mappings;
    for (auto it = mv.begin(); it != mv.end();) {
        if (it->chainIndex == chain && isMidiTarget(it->deviceIndex)) {
            const int32_t j = midiTargetIndex(it->deviceIndex);
            if (j == idx) {
                it = mv.erase(it);
                continue;
            }
            if (j > idx) it->deviceIndex = midiTarget(j - 1);
        }
        ++it;
    }
    commit(ns);
    return true;
}

bool RackCore::moveChainMidiDevice(int32_t chain, int32_t from, int32_t to) {
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    auto& d = ns->chains[static_cast<size_t>(chain)].midiDevices;
    const int32_t n = static_cast<int32_t>(d.size());
    if (from < 0 || from >= n) return false;
    to = std::clamp(to, 0, n - 1);
    if (to == from) return true;
    auto x = d[static_cast<size_t>(from)];
    d.erase(d.begin() + from);
    d.insert(d.begin() + to, x);
    for (auto& m : ns->mappings) {
        if (m.chainIndex != chain || !isMidiTarget(m.deviceIndex)) continue;
        const int32_t j = midiTargetIndex(m.deviceIndex);
        if (j == from) m.deviceIndex = midiTarget(to);
        else if (from < to && j > from && j <= to)
            m.deviceIndex = midiTarget(j - 1);
        else if (to < from && j >= to && j < from)
            m.deviceIndex = midiTarget(j + 1);
    }
    commit(ns);
    return true;
}

int32_t RackCore::insertChainMidiDeviceInstance(int32_t chain, std::shared_ptr<MidiDevice> dev, int32_t at) {
    if (!dev || dev->midiKind() < 0 || dev->midiKind() > 5) return -1;
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return -1;
    auto& d = ns->chains[static_cast<size_t>(chain)].midiDevices;
    const int32_t n = static_cast<int32_t>(d.size());
    if (at < 0 || at > n) at = n;
    dev->setSampleRate(sampleRate_, maxBlock_);
    d.insert(d.begin() + at, std::move(dev));
    for (auto& m : ns->mappings)
        if (m.chainIndex == chain && isMidiTarget(m.deviceIndex) && midiTargetIndex(m.deviceIndex) >= at)
            m.deviceIndex = midiTarget(midiTargetIndex(m.deviceIndex) + 1);
    commit(ns);
    return at;
}

int32_t RackCore::moveChainMidiDeviceToChain(int32_t chain, int32_t idx, int32_t toChain) {
    auto ns = copyState();
    const int32_t nc = static_cast<int32_t>(ns->chains.size());
    if (chain < 0 || chain >= nc || toChain < 0 || toChain >= nc || toChain == chain) return -1;
    auto& from = ns->chains[static_cast<size_t>(chain)].midiDevices;
    if (idx < 0 || idx >= static_cast<int32_t>(from.size())) return -1;
    auto x = from[static_cast<size_t>(idx)];
    from.erase(from.begin() + idx);
    auto& to = ns->chains[static_cast<size_t>(toChain)].midiDevices;
    const int32_t at = static_cast<int32_t>(to.size());
    to.push_back(std::move(x));
    for (auto& m : ns->mappings) {
        if (m.chainIndex != chain || !isMidiTarget(m.deviceIndex)) continue;
        const int32_t j = midiTargetIndex(m.deviceIndex);
        if (j == idx) {
            m.chainIndex = toChain;
            m.deviceIndex = midiTarget(at);
        } else if (j > idx) {
            m.deviceIndex = midiTarget(j - 1);
        }
    }
    commit(ns);
    return at;
}

MidiDevice* RackCore::chainMidiDeviceAt(int32_t chain, int32_t idx) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
    auto& d = st->chains[static_cast<size_t>(chain)].midiDevices;
    if (idx < 0 || idx >= static_cast<int32_t>(d.size())) return nullptr;
    return d[static_cast<size_t>(idx)].get();
}

RackCore::ChainControls* RackCore::chainControls(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
    return st->chains[static_cast<size_t>(chain)].ctl.get();
}

void RackCore::setChainZone(int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    if (auto* x = chainControls(c)) {
        // An edge never crosses the other: lo is held at or below hi.
        keyHi = std::clamp(keyHi, 0, 127);
        velHi = std::clamp(velHi, 0, 127);
        x->keyLo.store(std::clamp(keyLo, 0, keyHi), std::memory_order_relaxed);
        x->keyHi.store(keyHi, std::memory_order_relaxed);
        x->velLo.store(std::clamp(velLo, 0, velHi), std::memory_order_relaxed);
        x->velHi.store(velHi, std::memory_order_relaxed);
    }
}

void RackCore::setChainSelectRange(int32_t c, int32_t lo, int32_t hi) {
    if (auto* x = chainControls(c)) {
        hi = std::clamp(hi, 0, 127);
        x->selLo.store(std::clamp(lo, 0, hi), std::memory_order_relaxed);
        x->selHi.store(hi, std::memory_order_relaxed);
    }
}

void RackCore::chainSelectRange(int32_t c, int32_t& lo, int32_t& hi) const {
    auto* x = chainControls(c);
    lo = x ? x->selLo.load(std::memory_order_relaxed) : 0;
    hi = x ? x->selHi.load(std::memory_order_relaxed) : 127;
}

void RackCore::chainZone(int32_t c, int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const {
    auto* x = chainControls(c);
    keyLo = x ? x->keyLo.load(std::memory_order_relaxed) : 0;
    keyHi = x ? x->keyHi.load(std::memory_order_relaxed) : 127;
    velLo = x ? x->velLo.load(std::memory_order_relaxed) : 0;
    velHi = x ? x->velHi.load(std::memory_order_relaxed) : 127;
}

void RackCore::macroSet(int32_t i, float v) {
    if (i < 0 || i >= kNumMacros) return;
    v = std::clamp(v, 0.0f, 1.0f);
    macros_[i].store(v, std::memory_order_relaxed);
    applyMacro(i, v);
}

int32_t RackCore::addMacroMapping(
    int32_t macro, int32_t chain, int32_t deviceIndex, int32_t paramIndex, float rangeMin, float rangeMax) {
    if (macro < 0 || macro >= kNumMacros) return -1;
    if (const auto* device = chainDeviceAt(chain, deviceIndex);
        device && device->isLatencyConfigurationParam(paramIndex))
        return -1;
    auto ns = copyState();
    if (deviceIndex == kRackControl) {
        if (paramIndex < 0 || paramIndex >= kRackParams) return -1;
        chain = -1;
    } else if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size()))
        return -1;
    // One macro per target: an existing mapping of the same target moves to this macro.
    for (size_t i = 0; i < ns->mappings.size(); ++i) {
        auto& m = ns->mappings[i];
        if (m.chainIndex != chain || m.deviceIndex != deviceIndex || m.paramIndex != paramIndex) continue;
        m.macro = macro;
        commit(ns);
        applyMacro(macro, macros_[macro].load(std::memory_order_relaxed));
        return static_cast<int32_t>(i);
    }
    ns->mappings.push_back({macro, chain, deviceIndex, paramIndex, rangeMin, rangeMax});
    const int32_t idx = static_cast<int32_t>(ns->mappings.size()) - 1;
    commit(ns);
    applyMacro(macro, macros_[macro].load(std::memory_order_relaxed));
    return idx;
}

bool RackCore::mappingInfo(int32_t i, MacroMapping& out) const {
    RackState* st = live();
    if (!st || i < 0 || i >= static_cast<int32_t>(st->mappings.size())) return false;
    out = st->mappings[static_cast<size_t>(i)];
    return true;
}

bool RackCore::removeMapping(int32_t i) {
    auto ns = copyState();
    if (i < 0 || i >= static_cast<int32_t>(ns->mappings.size())) return false;
    ns->mappings.erase(ns->mappings.begin() + i);
    commit(ns);
    return true;
}

bool RackCore::setMappingRange(int32_t i, float rangeMin, float rangeMax) {
    auto ns = copyState();
    if (i < 0 || i >= static_cast<int32_t>(ns->mappings.size())) return false;
    ns->mappings[static_cast<size_t>(i)].rangeMin = rangeMin;
    ns->mappings[static_cast<size_t>(i)].rangeMax = rangeMax;
    const int32_t mo = ns->mappings[static_cast<size_t>(i)].macro;
    commit(ns);
    applyMacro(mo, macros_[mo].load(std::memory_order_relaxed));
    return true;
}

bool RackCore::setMappingCurve(int32_t i, int32_t curve) {
    auto ns = copyState();
    if (i < 0 || i >= static_cast<int32_t>(ns->mappings.size())) return false;
    ns->mappings[static_cast<size_t>(i)].curve = std::clamp(curve, 0, 3);
    const int32_t mo = ns->mappings[static_cast<size_t>(i)].macro;
    commit(ns);
    applyMacro(mo, macros_[mo].load(std::memory_order_relaxed));
    return true;
}

} // namespace nota::devices
