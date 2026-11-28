// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/devices/racks/RackCore.h"
#include "nota/devices/racks/DrumRack.h"
#include "common/DeviceMath.h"
#include <memory>
#include <cstdint>
#include <cstddef>
#include <cmath>
#include <atomic>
#include <algorithm>
#include <nota/dsp/DeviceRegistry.h>
#include <nota/dsp/capabilities/ISampleInstrument.h>
#include <vector>
#include <utility>
#include <string>

namespace nota::devices {
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
    // v9: Instrument Rack mono, MIDI Effect Rack output.
    putU8(b, static_cast<uint8_t>(mono_.load(std::memory_order_relaxed)));
    putU8(b, static_cast<uint8_t>(merge_.load(std::memory_order_relaxed)));
    putU8(b, static_cast<uint8_t>(thru_.load(std::memory_order_relaxed)));
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
            auto* sm = dynamic_cast<dsp::capabilities::ISampleInstrument*>(c.instrument.get());
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
        // v9: select range, transpose, velocity scale, then the chain's MIDI effects.
        putI32(b, c.ctl->selLo.load(std::memory_order_relaxed));
        putI32(b, c.ctl->selHi.load(std::memory_order_relaxed));
        putI32(b, c.ctl->transpose.load(std::memory_order_relaxed));
        putF32(b, c.ctl->velScale.load(std::memory_order_relaxed));
        putU32(b, static_cast<uint32_t>(c.midiDevices.size()));
        for (auto& m : c.midiDevices) {
            putI32(b, m ? m->midiKind() : -1);
            putU8(b, (m && m->bypassed()) ? 1 : 0);
            const int32_t pc = m ? m->paramCount() : 0;
            putU32(b, static_cast<uint32_t>(pc));
            for (int32_t p = 0; p < pc; ++p) putF32(b, m->getParam(p));
            std::vector<uint8_t> ms = m ? m->getState() : std::vector<uint8_t>{};
            putU32(b, static_cast<uint32_t>(ms.size()));
            b.insert(b.end(), ms.begin(), ms.end());
        }
        // v10: the pictures of hosted plug-ins' windows (specs/046) — the instrument's, then
        // each device's — so a plug-in grouped into a rack keeps its card's picture through
        // save, copy and ungroup. 0 = none.
        auto putPreview = [&b](const EditorPreview& p) {
            const uint32_t n = p.empty() ? 0 : static_cast<uint32_t>(p.jpeg->size());
            putU32(b, n);
            if (n > 0) b.insert(b.end(), p.jpeg->begin(), p.jpeg->end());
        };
        putPreview(c.instrument ? c.instrument->editorPreview : EditorPreview{});
        for (auto& d : c.devices) putPreview(d ? d->editorPreview() : EditorPreview{});
        // v11: each built-in device's state blob (an EQ-8's band labels, a looper's take, a
        // nested rack); a hosted plug-in's went in above. 0 = none.
        for (auto& d : c.devices) {
            std::vector<uint8_t> ds = d && d->builtinKind() >= 0 ? d->getState() : std::vector<uint8_t>{};
            putU32(b, static_cast<uint32_t>(ds.size()));
            b.insert(b.end(), ds.begin(), ds.end());
        }
    }
    return b;
}

// Depth of the rack blob being read on this thread: a chain of a blob read at depth > 0 is
// itself inside a nested rack, and racks nest one level (specs/041).
static thread_local int32_t tlReadDepth = 0;

void RackCore::deserialize(const uint8_t* data, int32_t size) {
    if (!data || size < 8) return;
    struct DepthScope {
        DepthScope() { ++tlReadDepth; }
        ~DepthScope() { --tlReadDepth; }
    } depth;
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
    // v9 (older blobs: the defaults — mono off, merge and pass-through on).
    mono_.store(ver >= 9 ? getU8(cur) : 0, std::memory_order_relaxed);
    merge_.store(ver >= 9 ? getU8(cur) : 1, std::memory_order_relaxed);
    thru_.store(ver >= 9 ? getU8(cur) : 1, std::memory_order_relaxed);
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
        else if (!isRackKind(ik) || tlReadDepth == 1)
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
            if (auto* sm = dynamic_cast<dsp::capabilities::ISampleInstrument*>(c.instrument.get()))
                sm->setSample(buf, root, loop);
        }
        const uint32_t devN = getU32(cur);
        std::vector<Device*> devAt; // by blob position, null where a device wasn't rebuilt (v10 previews)
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
            devAt.push_back(dev.get());
        }
        if (ver >= 9) {
            const int32_t sl = getI32(cur), sh = getI32(cur);
            c.ctl->selHi.store(std::clamp(sh, 0, 127), std::memory_order_relaxed);
            c.ctl->selLo.store(std::clamp(sl, 0, std::clamp(sh, 0, 127)), std::memory_order_relaxed);
            c.ctl->transpose.store(std::clamp(getI32(cur), -48, 48), std::memory_order_relaxed);
            const float vs = getF32(cur);
            c.ctl->velScale.store(std::isfinite(vs) ? std::clamp(vs, 0.0f, 2.0f) : 1.0f, std::memory_order_relaxed);
            const uint32_t midiN = getU32(cur);
            for (uint32_t j = 0; j < midiN && cur.ok(); ++j) {
                const int32_t mk = getI32(cur);
                const bool byp = getU8(cur) != 0;
                const uint32_t pc = getU32(cur);
                auto md = makeChainMidiDevice(mk);
                if (md) md->setSampleRate(sampleRate_, maxBlock_);
                for (uint32_t p = 0; p < pc && cur.ok(); ++p) {
                    const float pv = getF32(cur);
                    if (md) md->setParam(static_cast<int32_t>(p), pv);
                }
                const uint32_t msLen = getU32(cur);
                const uint8_t* msPtr = cur.take(msLen);
                if (md) {
                    if (msPtr && msLen > 0) md->setState(msPtr, static_cast<int32_t>(msLen));
                    md->setBypassed(byp);
                    c.midiDevices.push_back(std::move(md));
                }
            }
        }
        if (ver >= 10) {
            auto getPreview = [&cur]() {
                const uint32_t n = getU32(cur);
                const uint8_t* p = cur.take(n);
                return (p && n > 0) ? EditorPreview::make(std::vector<uint8_t>(p, p + n)) : EditorPreview{};
            };
            auto ip = getPreview();
            if (c.instrument) c.instrument->editorPreview = std::move(ip);
            for (auto* d : devAt) {
                auto dp = getPreview();
                if (d) d->setEditorPreview(std::move(dp));
            }
        }
        if (ver >= 11)
            for (auto* d : devAt) {
                const uint32_t n = getU32(cur);
                const uint8_t* p = cur.take(n);
                if (d && p && n > 0 && d->builtinKind() >= 0) d->setState(p, static_cast<int32_t>(n));
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

std::shared_ptr<dsp::Instrument> RackCore::makeInstrument(int32_t kind) {
    if (kind == 3) return nullptr; // Instrument Rack never nests
    if (kind == 4) return std::make_shared<DrumRack>();
    return dsp::DeviceRegistry::createInstrument(kind);
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
        if (m.deviceIndex == kRackControl) { // rack-level control, no chain
            if (m.paramIndex == RackSelector)
                chainSelect_.store(std::clamp(target, 0.0f, 1.0f), std::memory_order_relaxed);
            continue;
        }
        if (m.chainIndex < 0 || m.chainIndex >= static_cast<int32_t>(st->chains.size())) continue;
        auto& c = st->chains[static_cast<size_t>(m.chainIndex)];
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
        } else if (isMidiTarget(m.deviceIndex)) {
            const int32_t j = midiTargetIndex(m.deviceIndex);
            if (j < static_cast<int32_t>(c.midiDevices.size()) && c.midiDevices[static_cast<size_t>(j)])
                c.midiDevices[static_cast<size_t>(j)]->setParam(m.paramIndex, target);
        } else if (m.deviceIndex < 0) {
            if (c.instrument) c.instrument->pluginParamSet(m.paramIndex, target);
        } else if (m.deviceIndex < static_cast<int32_t>(c.devices.size())) {
            if (c.devices[static_cast<size_t>(m.deviceIndex)])
                c.devices[static_cast<size_t>(m.deviceIndex)]->setLiveParam(m.paramIndex, target);
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

} // namespace nota::devices
