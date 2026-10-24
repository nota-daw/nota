// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackCore implementation — method bodies
// . Concrete instrument headers live only here, so
// consumers of RackCore.h no longer parse 15 synth headers.

#include "RackCore.h"

#include "devices/instruments/Synth.h"
#include "devices/instruments/PhysicalSynth.h"
#include "devices/instruments/WavetableSynth.h"
#include "devices/instruments/VoltSynth.h"
#include "devices/instruments/BassSynth.h"
#include "devices/instruments/PendulumSynth.h"
#include "devices/instruments/OperatorSynth.h"
#include "devices/instruments/FluxSynth.h"
#include "devices/instruments/RhythmMachine.h"
#include "devices/instruments/Monolith.h"
#include "devices/instruments/Pentad.h"
#include "devices/instruments/Consort.h"
#include "devices/instruments/Keys.h"
#include "devices/instruments/GrainSynth.h"
#include "devices/instruments/Sampler.h"

#include <cmath>

namespace nota {

void RackCore::forwardTransport(const TransportInfo& ti) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains) {
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

void RackCore::forwardTempo(double beatStart, double spb, bool playing) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains) {
        if (c.instrument) c.instrument->setTransport(beatStart, spb, playing);
        for (auto& d : c.devices)
            if (d) d->setTransport(beatStart, spb, playing);
    }
}

void RackCore::setRates(double sr, int32_t maxBlock) {
    sampleRate_ = sr > 0 ? sr : 44100.0;
    maxBlock_ = maxBlock > 0 ? maxBlock : kMaxBlock;
    auto st = authoring_;
    if (!st) return;
    for (auto& c : st->chains) {
        if (c.instrument) c.instrument->setSampleRate(sampleRate_);
        for (auto& d : c.devices)
            if (d) d->setSampleRate(sampleRate_, maxBlock_);
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
            ctl->ampEnv = s.ampEnv; // a ringing pad keeps its level
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
    sampleRate_ = o.sampleRate_;
    maxBlock_ = o.maxBlock_;
}

bool RackCore::setChainName(int32_t c, const std::string& name) {
    auto ns = copyState();
    if (c < 0 || c >= static_cast<int32_t>(ns->chains.size())) return false;
    ns->chains[c].name = name;
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
    auto* oldSm = dynamic_cast<Sampler*>(ns->chains[chain].instrument.get());
    std::shared_ptr<Instrument> fresh = oldSm ? oldSm->clone() : std::make_shared<Sampler>();
    auto* sm = dynamic_cast<Sampler*>(fresh.get());
    if (!sm) return false;
    sm->setSampleRate(sampleRate_);
    sm->setSample(std::move(buf), root, loop);
    ns->chains[chain].instrument = std::move(fresh);
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

bool RackCore::setChainInstrument(int32_t chain, int32_t instKind) {
    auto inst = makeInstrument(instKind);
    if (!inst) return false;
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    inst->setSampleRate(sampleRate_);
    ns->chains[chain].instrument = inst;
    commit(ns);
    return true;
}

int32_t RackCore::chainInstrumentKind(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return -2;
    return st->chains[chain].instrument ? st->chains[chain].instrument->kind() : -2;
}

Instrument* RackCore::chainInstrument(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
    return st->chains[chain].instrument.get();
}

int32_t RackCore::chainDeviceCount(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return 0;
    return static_cast<int32_t>(st->chains[chain].devices.size());
}

int32_t RackCore::addChainDevice(int32_t chain, int32_t deviceKind) {
    auto dev = makeDevice(deviceKind);
    if (!dev) return -1;
    return addChainDeviceInstance(chain, dev);
}

int32_t RackCore::addChainDeviceInstance(int32_t chain, std::shared_ptr<Device> dev) {
    if (!dev) return -1;
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return -1;
    dev->setSampleRate(sampleRate_, maxBlock_);
    ns->chains[chain].devices.push_back(std::move(dev));
    const int32_t idx = static_cast<int32_t>(ns->chains[chain].devices.size()) - 1;
    commit(ns);
    return idx;
}

bool RackCore::removeChainDevice(int32_t chain, int32_t dev) {
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return false;
    auto& d = ns->chains[chain].devices;
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
    auto& d = ns->chains[chain].devices;
    const int32_t n = static_cast<int32_t>(d.size());
    if (from < 0 || from >= n) return false;
    if (to < 0) to = 0;
    if (to >= n) to = n - 1;
    if (to == from) return true;
    auto x = d[from];
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

Device* RackCore::chainDeviceAt(int32_t chain, int32_t dev) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
    auto& d = st->chains[chain].devices;
    if (dev < 0 || dev >= static_cast<int32_t>(d.size())) return nullptr;
    return d[dev].get();
}

RackCore::ChainControls* RackCore::chainControls(int32_t chain) const {
    RackState* st = live();
    if (!st || chain < 0 || chain >= static_cast<int32_t>(st->chains.size())) return nullptr;
    return st->chains[chain].ctl.get();
}

void RackCore::setChainZone(int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    if (auto* x = chainControls(c)) {
        x->keyLo.store(std::clamp(keyLo, 0, 127), std::memory_order_relaxed);
        x->keyHi.store(std::clamp(keyHi, 0, 127), std::memory_order_relaxed);
        x->velLo.store(std::clamp(velLo, 0, 127), std::memory_order_relaxed);
        x->velHi.store(std::clamp(velHi, 0, 127), std::memory_order_relaxed);
    }
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
    auto ns = copyState();
    if (chain < 0 || chain >= static_cast<int32_t>(ns->chains.size())) return -1;
    ns->mappings.push_back({macro, chain, deviceIndex, paramIndex, rangeMin, rangeMax});
    const int32_t idx = static_cast<int32_t>(ns->mappings.size()) - 1;
    commit(ns);
    applyMacro(macro, macros_[macro].load(std::memory_order_relaxed));
    return idx;
}

bool RackCore::mappingInfo(int32_t i, MacroMapping& out) const {
    RackState* st = live();
    if (!st || i < 0 || i >= static_cast<int32_t>(st->mappings.size())) return false;
    out = st->mappings[i];
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
    ns->mappings[i].rangeMin = rangeMin;
    ns->mappings[i].rangeMax = rangeMax;
    const int32_t mo = ns->mappings[i].macro;
    commit(ns);
    applyMacro(mo, macros_[mo].load(std::memory_order_relaxed));
    return true;
}

bool RackCore::setMappingCurve(int32_t i, int32_t curve) {
    auto ns = copyState();
    if (i < 0 || i >= static_cast<int32_t>(ns->mappings.size())) return false;
    ns->mappings[i].curve = std::clamp(curve, 0, 3);
    const int32_t mo = ns->mappings[i].macro;
    commit(ns);
    applyMacro(mo, macros_[mo].load(std::memory_order_relaxed));
    return true;
}

std::vector<uint8_t> RackCore::serialize() const {
    RackState* st = live();
    std::vector<uint8_t> b;
    putU32(b, kMagic);
    putU32(b, kBlobVersion);
    for (int i = 0; i < kNumMacros; ++i) putF32(b, macros_[i].load(std::memory_order_relaxed));
    // v5: rack output + custom macro names.
    putF32(b, rackVolume_.load(std::memory_order_relaxed));
    putF32(b, glide_.load(std::memory_order_relaxed));
    for (int i = 0; i < kNumMacros; ++i) putString(b, macroNames_[i]);
    // v6: drum-rack kit-level timing.
    putF32(b, swing_.load(std::memory_order_relaxed));
    putF32(b, humanize_.load(std::memory_order_relaxed));
    // v7: audio-effect-rack routing + output stage.
    putI32(b, mode_.load(std::memory_order_relaxed));
    putF32(b, dryWet_.load(std::memory_order_relaxed));
    putU8(b, static_cast<uint8_t>(pdc_.load(std::memory_order_relaxed)));
    putF32(b, chainSelect_.load(std::memory_order_relaxed));
    putU8(b, static_cast<uint8_t>(selFollow_.load(std::memory_order_relaxed)));
    if (!st) {
        putU32(b, 0);
        putU32(b, 0);
        return b;
    }
    putU32(b, static_cast<uint32_t>(st->mappings.size()));
    for (auto& m : st->mappings) {
        putI32(b, m.macro);
        putI32(b, m.chainIndex);
        putI32(b, m.deviceIndex);
        putI32(b, m.paramIndex);
        putF32(b, m.rangeMin);
        putF32(b, m.rangeMax);
        putI32(b, m.curve); // v5
    }
    putU32(b, static_cast<uint32_t>(st->chains.size()));
    for (auto& c : st->chains) {
        putF32(b, c.ctl->gain.load(std::memory_order_relaxed));
        putF32(b, c.ctl->pan.load(std::memory_order_relaxed));
        putU8(b, c.ctl->mute.load(std::memory_order_relaxed) ? 1 : 0);
        putU8(b, c.ctl->solo.load(std::memory_order_relaxed) ? 1 : 0);
        putI32(b, c.ctl->triggerNote.load(std::memory_order_relaxed)); // blob v2
        // v5: key/velocity zone.
        putI32(b, c.ctl->keyLo.load(std::memory_order_relaxed));
        putI32(b, c.ctl->keyHi.load(std::memory_order_relaxed));
        putI32(b, c.ctl->velLo.load(std::memory_order_relaxed));
        putI32(b, c.ctl->velHi.load(std::memory_order_relaxed));
        // v6: drum-rack pad shaping.
        putI32(b, c.ctl->chokeGroup.load(std::memory_order_relaxed));
        putI32(b, c.ctl->tune.load(std::memory_order_relaxed));
        putF32(b, c.ctl->decay.load(std::memory_order_relaxed));
        putString(b, c.name); // v8: pad/chain display name
        const int32_t ik = c.instrument ? c.instrument->kind() : -1;
        putI32(b, ik);
        // v4: kind -1 = hosted plugin instrument OR no instrument; the id (empty
        // for none) lets the factory rebuild it. Built-ins (>=0) need no id.
        if (ik == -1) putString(b, c.instrument ? c.instrument->pluginIdentifier() : std::string{});
        std::vector<uint8_t> is = c.instrument ? c.instrument->getState() : std::vector<uint8_t>{};
        putU32(b, static_cast<uint32_t>(is.size()));
        b.insert(b.end(), is.begin(), is.end());
        if (ik == 1) { // Sampler payload (blob v3): self-contained sample
            auto* sm = dynamic_cast<Sampler*>(c.instrument.get());
            auto buf = sm ? sm->sample() : nullptr;
            putI32(b, sm ? sm->rootNote() : 60);
            putU8(b, (sm && sm->loopEnabled()) ? 1 : 0);
            putI32(b, buf ? buf->channels : 0);
            putI64(b, buf ? buf->frames : 0);
            putF64(b, buf ? buf->sourceSampleRate : 44100.0);
            const uint32_t n = buf ? static_cast<uint32_t>(buf->samples.size()) : 0;
            putU32(b, n);
            for (uint32_t s = 0; s < n; ++s) putF32(b, buf->samples[s]);
        }
        putU32(b, static_cast<uint32_t>(c.devices.size()));
        for (auto& d : c.devices) {
            const int32_t bk = d ? d->builtinKind() : -1;
            putI32(b, bk);
            putU8(b, (d && d->bypassed()) ? 1 : 0);
            if (bk == -1) { // v4: hosted plugin effect — id + opaque state
                putString(b, d ? d->pluginIdentifier() : std::string{});
                std::vector<uint8_t> ds = d ? d->getState() : std::vector<uint8_t>{};
                putU32(b, static_cast<uint32_t>(ds.size()));
                b.insert(b.end(), ds.begin(), ds.end());
            }
            const int32_t pc = d ? d->paramCount() : 0;
            putU32(b, static_cast<uint32_t>(pc));
            for (int32_t p = 0; p < pc; ++p) putF32(b, d->getParam(p));
        }
    }
    return b;
}

void RackCore::deserialize(const uint8_t* data, int32_t size) {
    if (!data || size < 8) return;
    Cursor cur{data, size, 0};
    if (getU32(cur) != kMagic) return;
    const uint32_t ver = getU32(cur); // v1 = no triggerNote; v2 adds it per chain
    for (int i = 0; i < kNumMacros; ++i) macros_[i].store(clamp01(getF32(cur)), std::memory_order_relaxed);
    if (ver >= 5) {
        rackVolume_.store(getF32(cur), std::memory_order_relaxed);
        glide_.store(getF32(cur), std::memory_order_relaxed);
        for (int i = 0; i < kNumMacros; ++i) macroNames_[i] = getString(cur);
    }
    if (ver >= 6) {
        swing_.store(getF32(cur), std::memory_order_relaxed);
        humanize_.store(getF32(cur), std::memory_order_relaxed);
    }
    if (ver >= 7) {
        mode_.store(getI32(cur), std::memory_order_relaxed);
        dryWet_.store(getF32(cur), std::memory_order_relaxed);
        pdc_.store(getU8(cur), std::memory_order_relaxed);
        chainSelect_.store(getF32(cur), std::memory_order_relaxed);
        selFollow_.store(getU8(cur), std::memory_order_relaxed);
    }
    auto ns = std::make_shared<RackState>();
    const uint32_t mapN = getU32(cur);
    for (uint32_t i = 0; i < mapN && cur.ok(); ++i) {
        MacroMapping m;
        m.macro = getI32(cur);
        m.chainIndex = getI32(cur);
        m.deviceIndex = getI32(cur);
        m.paramIndex = getI32(cur);
        m.rangeMin = getF32(cur);
        m.rangeMax = getF32(cur);
        if (ver >= 5) m.curve = getI32(cur);
        ns->mappings.push_back(m);
    }
    const uint32_t chainN = getU32(cur);
    for (uint32_t i = 0; i < chainN && cur.ok(); ++i) {
        RackChain c;
        c.ctl->gain.store(getF32(cur), std::memory_order_relaxed);
        c.ctl->pan.store(getF32(cur), std::memory_order_relaxed);
        c.ctl->mute.store(getU8(cur) != 0, std::memory_order_relaxed);
        c.ctl->solo.store(getU8(cur) != 0, std::memory_order_relaxed);
        c.ctl->triggerNote.store(ver >= 2 ? getI32(cur) : -1, std::memory_order_relaxed);
        if (ver >= 5) {
            c.ctl->keyLo.store(getI32(cur), std::memory_order_relaxed);
            c.ctl->keyHi.store(getI32(cur), std::memory_order_relaxed);
            c.ctl->velLo.store(getI32(cur), std::memory_order_relaxed);
            c.ctl->velHi.store(getI32(cur), std::memory_order_relaxed);
        }
        if (ver >= 6) {
            c.ctl->chokeGroup.store(getI32(cur), std::memory_order_relaxed);
            c.ctl->tune.store(getI32(cur), std::memory_order_relaxed);
            c.ctl->decay.store(getF32(cur), std::memory_order_relaxed);
        }
        if (ver >= 8) c.name = getString(cur);
        const int32_t ik = getI32(cur);
        std::string instPluginId;
        if (ik == -1 && ver >= 4) instPluginId = getString(cur); // v4 plugin/none id
        const uint32_t isLen = getU32(cur);
        const uint8_t* isPtr = cur.take(isLen);
        if (ik == -1) // hosted plugin (via factory) or no instrument (empty id)
            c.instrument = instPluginId.empty() ? nullptr : pluginFactory().instrumentById(instPluginId);
        else
            c.instrument = makeInstrument(ik);
        if (c.instrument) {
            c.instrument->setSampleRate(sampleRate_);
            if (isPtr && isLen > 0) c.instrument->setState(isPtr, static_cast<int32_t>(isLen));
        }
        if (ik == 1 && ver >= 3) { // Sampler payload (blob v3)
            const int32_t root = getI32(cur);
            const bool loop = getU8(cur) != 0;
            const int32_t ch = getI32(cur);
            const int64_t fr = getI64(cur);
            const double sr = getF64(cur);
            const uint32_t n = getU32(cur);
            auto buf = std::make_shared<SampleBuffer>();
            buf->channels = ch;
            buf->frames = fr;
            buf->sourceSampleRate = sr;
            buf->samples.resize(n);
            for (uint32_t s = 0; s < n && cur.ok(); ++s) buf->samples[s] = getF32(cur);
            if (auto* sm = dynamic_cast<Sampler*>(c.instrument.get())) sm->setSample(buf, root, loop);
        }
        const uint32_t devN = getU32(cur);
        for (uint32_t j = 0; j < devN && cur.ok(); ++j) {
            const int32_t dk = getI32(cur);
            const bool byp = getU8(cur) != 0;
            std::string devPluginId;
            const uint8_t* dsPtr = nullptr;
            uint32_t dsLen = 0;
            if (dk == -1 && ver >= 4) { // v4 hosted plugin effect
                devPluginId = getString(cur);
                dsLen = getU32(cur);
                dsPtr = cur.take(dsLen);
            }
            const uint32_t pc = getU32(cur);
            auto dev =
                (dk == -1) ? (devPluginId.empty() ? nullptr : pluginFactory().effectById(devPluginId)) : makeDevice(dk);
            if (dev) dev->setSampleRate(sampleRate_, maxBlock_);
            for (uint32_t p = 0; p < pc && cur.ok(); ++p) {
                const float pv = getF32(cur);
                if (dev) dev->setParam(static_cast<int32_t>(p), pv);
            }
            if (dev) {
                dev->paramsRestored(static_cast<int32_t>(pc));
                if (dsPtr && dsLen > 0) dev->setState(dsPtr, static_cast<int32_t>(dsLen));
                dev->setBypassed(byp);
                c.devices.push_back(dev);
            }
        }
        ns->chains.push_back(std::move(c));
    }
    commit(ns);
    for (int i = 0; i < kNumMacros; ++i) applyMacro(i, macros_[i].load(std::memory_order_relaxed));
}

int32_t RackCore::maxChainLatency() const {
    RackState* st = live();
    if (!st) return 0;
    int32_t maxLat = 0;
    for (auto& c : st->chains) {
        int32_t lat = c.instrument ? c.instrument->latencySamples() : 0;
        for (auto& d : c.devices)
            if (d) lat += d->latencySamples();
        if (lat > maxLat) maxLat = lat;
    }
    return maxLat;
}

std::shared_ptr<Instrument> RackCore::makeInstrument(int32_t kind) {
    switch (kind) {
    case 0: return std::make_shared<Synth>();
    case 1:
        return std::make_shared<
            Sampler>(); // bare; the sample rides the blob (deserialize) or engine (addChainInstrument)
    case 2: return std::make_shared<PhysicalSynth>();
    case 5: return std::make_shared<WavetableSynth>();
    case 6: return std::make_shared<VoltSynth>();
    case 7: return std::make_shared<BassSynth>();
    case 8: return std::make_shared<PendulumSynth>();
    case 9: return std::make_shared<OperatorSynth>();
    case 10: return std::make_shared<GrainSynth>();
    case 11: return std::make_shared<FluxSynth>();
    case 12: return std::make_shared<RhythmMachine>();
    case 13: return std::make_shared<Monolith>();
    case 14: return std::make_shared<Pentad>();
    case 15: return std::make_shared<Consort>();
    case 16: return std::make_shared<Keys>();
    default: return nullptr; // -1 = no instrument (effect rack); plugins are a follow-up
    }
}

float RackCore::applyCurve(float v, int32_t curve) {
    v = std::clamp(v, 0.0f, 1.0f);
    switch (curve) {
    case 1: return v * v;                     // Exp (slow start)
    case 2: return std::sqrt(v);              // Log (fast start)
    case 3: return v * v * (3.0f - 2.0f * v); // S-curve (smoothstep)
    default: return v;                        // Linear
    }
}

void RackCore::applyMacro(int32_t macro, float value) {
    RackState* st = live();
    if (!st) return;
    for (auto& m : st->mappings) {
        if (m.macro != macro) continue;
        const float target = m.rangeMin + applyCurve(value, m.curve) * (m.rangeMax - m.rangeMin);
        if (m.chainIndex < 0 || m.chainIndex >= static_cast<int32_t>(st->chains.size())) continue;
        auto& c = st->chains[m.chainIndex];
        if (m.deviceIndex == kPadControls) {
            auto& k = *c.ctl;
            switch (m.paramIndex) {
            case PadVolume: k.gain.store(std::clamp(target, 0.0f, 4.0f), std::memory_order_relaxed); break;
            case PadPan: k.pan.store(std::clamp(target, -1.0f, 1.0f), std::memory_order_relaxed); break;
            case PadTune:
                k.tune.store(std::clamp(static_cast<int32_t>(std::lround(target)), -48, 48), std::memory_order_relaxed);
                break;
            case PadDecay: k.decay.store(std::clamp(target, 0.0f, 1.0f), std::memory_order_relaxed); break;
            default: break;
            }
        } else if (m.deviceIndex < 0) {
            if (c.instrument) c.instrument->pluginParamSet(m.paramIndex, target);
        } else if (m.deviceIndex < static_cast<int32_t>(c.devices.size())) {
            if (c.devices[m.deviceIndex]) c.devices[m.deviceIndex]->setParam(m.paramIndex, target);
        }
    }
}

void RackCore::pruneMappings(std::shared_ptr<RackState>& ns, int32_t removedChain) {
    auto& v = ns->mappings;
    for (auto it = v.begin(); it != v.end();) {
        if (it->chainIndex == removedChain) it = v.erase(it);
        else {
            if (it->chainIndex > removedChain) --it->chainIndex;
            ++it;
        }
    }
}

std::string RackCore::getString(Cursor& c) {
    const uint32_t n = getU32(c);
    const uint8_t* p = c.take(n);
    return (p && n > 0) ? std::string(reinterpret_cast<const char*>(p), n) : std::string{};
}

} // namespace nota
