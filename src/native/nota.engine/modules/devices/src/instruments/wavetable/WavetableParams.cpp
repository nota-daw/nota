// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Aurora implementation. Behavioural deltas are the auto-sleep flag, the hoisted inverse drive gain,
// and FastMath on downstream values only (Tube drive via tanhPrecise, warp Fold shaper
// and unison pan gains via fast sin/cos). The LFO sine, chorus tap LFO and drive Fold
// shaper were reverted to std:: after the null test measured them over budget
// (−44.8/−51/−51 dB); pitch-defining math stays std::-exact throughout.

#include "WavetableSynth.h"
#include "WavetableInternals.h"

#include "common/DeviceMath.h"
#include <vector>
#include <utility>
#include <unordered_map>
#include <string>
#include <memory>
#include <cstring>
#include <cstdint>
#include <cstddef>
#include <cmath>
#include <atomic>
#include <algorithm>
#include "nota/dsp/Instrument.h"

namespace nota {

WavetableSynth::~WavetableSynth() = default;

WavetableSynth::WavetableSynth() {
    set(Table, 0.0f);
    set(Position, 0.0f);
    set(Warp, 0.5f);
    set(Unison, 0.30f);
    set(Attack, 0.01f);
    set(Decay, 0.30f);
    set(Sustain, 0.75f);
    set(Release, 0.25f);
    set(Cutoff, 0.70f);
    set(Resonance, 0.10f);
    set(Gain, 0.80f);
    set(Osc1WarpMode, 0.0f);
    set(Osc1Level, 0.85f);
    set(Osc1Oct, 0.5f);
    set(Osc1Semi, 0.5f);
    set(Osc1Detune, 0.5f);
    set(Osc1On, 1.0f);
    set(Osc2Table, 0.33f);
    set(Osc2Position, 0.0f);
    set(Osc2Warp, 0.5f);
    set(Osc2WarpMode, 0.0f);
    set(Osc2Level, 0.0f);
    set(Osc2Oct, 0.5f);
    set(Osc2Semi, 0.5f);
    set(Osc2Detune, 0.55f);
    set(Osc2On, 0.0f);
    set(SubLevel, 0.0f);
    set(SubOct, 0.5f);
    set(SubWave, 0.0f);
    set(UniDetune, 0.3f);
    set(UniSpread, 0.6f);
    set(Fil1Type, 0.0f);
    set(Fil1Slope, 0.0f);
    set(Fil1Env, 0.5f);
    set(Fil1Lfo, 0.5f);
    set(Fil1Key, 0.3f);
    set(Fil2Type, 0.0f);
    set(Fil2Freq, 0.7f);
    set(Fil2Reso, 0.1f);
    set(Fil2Slope, 0.0f);
    set(Fil2Env, 0.5f);
    set(Fil2Lfo, 0.5f);
    set(Fil2Key, 0.3f);
    set(RouteOsc1, 0.0f);
    set(RouteOsc2, 0.0f);
    set(RouteSub, 0.0f);
    set(FilSeries, 0.0f);
    set(Env2Attack, 0.02f);
    set(Env2Decay, 0.4f);
    set(Env2Sustain, 0.3f);
    set(Env2Release, 0.3f);
    set(Lfo1Rate, 0.4f);
    set(Lfo1Depth, 0.5f);
    set(Lfo1Shape, 0.0f);
    set(Lfo1Sync, 0.0f);
    set(Lfo2Rate, 0.3f);
    set(Lfo2Depth, 0.5f);
    set(Lfo2Shape, 0.0f);
    set(FxDrive, 0.0f);
    set(FxChorus, 0.0f);
    set(FxChorusRate, 0.3f);
    set(FxReverb, 0.0f);
    set(ModWheel, 0.0f);
    set(MonoMode, 0.0f);
    for (int s = 0; s < kSources; ++s)
        for (int d = 0; d < kDests; ++d) set(static_cast<Param>(matrixIdx(s, d)), 0.5f);
    for (int m = 0; m < kMacros; ++m) {
        set(static_cast<Param>(macroIdx(m, 0)), 0.0f);
        set(static_cast<Param>(macroIdx(m, 1)), 0.0f);
        set(static_cast<Param>(macroIdx(m, 2)), 0.5f);
    }
    set(Bend, 0.5f);
    set(BendRange, 1.0f / 11.0f); // centred, ±2 semitones
    set(OutPan, 0.5f);
    set(Lfo2Sync, 0.0f);
    // The three FX blocks start switched ON with their amount at zero, so a patch saved
    // before they had switches sounds exactly as it did.
    set(FxDriveOn, 1.0f);
    set(FxDriveMode, 0.0f);
    set(FxTone, 0.5f);
    set(FxChorusOn, 1.0f);
    set(FxChorusVoices, 0.5f);
    set(FxReverbOn, 1.0f);
    set(FxReverbMode, 0.0f);
    set(FxReverbSize, 0.5f);
    set(DCurve, 0.5f);
    set(RCurve, 0.5f);
    set(EnvExp, 0.0f);
    set(E2DCurve, 0.5f);
    set(E2RCurve, 0.5f);
    set(E2EnvExp, 0.0f); // straight, Lin: old projects keep their envelopes
    tables_ = sharedTables();
    chorusBuf_.assign(4096, 0.0f);
    for (auto& c : revC_) c.buf.assign(1, 0.0f);
    batchWork_ = std::make_unique<BatchWork>();
}

// The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
void WavetableSynth::controller(int32_t ctl, float value) {
    if (ctl == kCtlModWheel) pn_[ModWheel].store(std::clamp(value, 0.0f, 1.0f), std::memory_order_relaxed);
    Instrument::controller(ctl, value);
    wake();
}

void WavetableSynth::setSampleRate(double sr) {
    sampleRate_ = sr > 0 ? sr : 44100.0;
    chorusBuf_.assign(static_cast<size_t>(sampleRate_ * 0.05) + 2, 0.0f);
    chorusW_ = 0;
    static const int ct[kRevCombs] = {1116, 1188, 1277, 1356};
    for (int c = 0; c < kRevCombs; ++c) {
        revC_[c].buf.assign(static_cast<size_t>(ct[c] * sampleRate_ / 44100.0) + 1, 0.0f);
        revC_[c].idx = 0;
        revC_[c].store = 0;
    }
    static const int at[kRevAll] = {556, 441};
    for (int a = 0; a < kRevAll; ++a) {
        revA_[a].buf.assign(static_cast<size_t>(at[a] * sampleRate_ / 44100.0) + 1, 0.0f);
        revA_[a].idx = 0;
    }
    toneL_ = toneR_ = 0.0f;
    exprCoef_ = exprSmoothCoef(sampleRate_);
    wake();
}

void WavetableSynth::noteExpression(int32_t pitch, int32_t dim, float value) {
    wake();
    if (pitch < 0) {
        gexpr_.set(dim, value);
        return;
    }
    float add[kExprDims];
    gexpr_.offsets(wheelRange(), add);
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.stage != Stage::Release && v.stage != Stage::Off)
            v.ex.set(dim, value, add);
}

void WavetableSynth::setTransport(double beatStart, double samplesPerBeat, bool playing) {
    if (samplesPerBeat > 0.0) spb_ = samplesPerBeat;
    beat_ = std::isfinite(beatStart) ? beatStart : 0.0;
    playing_ = playing;
    sinceBlock_ = 0;
}

void WavetableSynth::wake() {
    gate_.wake();
}

std::string WavetableSynth::pluginParamId(int32_t i) const {
    if (i < 0 || i >= kNumParams) return {};
    if (i < MatrixBase) {
        static const char* ids[] = {"table", "position", "warp", "unison", "attack", "decay", "sustain", "release",
            "cutoff", "resonance", "gain", "osc1warpmode", "osc1level", "osc1oct", "osc1semi", "osc1detune", "osc1on",
            "osc2table", "osc2position", "osc2warp", "osc2warpmode", "osc2level", "osc2oct", "osc2semi", "osc2detune",
            "osc2on", "sublevel", "suboct", "subwave", "unidetune", "unispread", "fil1type", "fil1slope", "fil1env",
            "fil1lfo", "fil1key", "fil2type", "fil2freq", "fil2reso", "fil2slope", "fil2env", "fil2lfo", "fil2key",
            "routeosc1", "routeosc2", "routesub", "filseries", "env2attack", "env2decay", "env2sustain", "env2release",
            "lfo1rate", "lfo1depth", "lfo1shape", "lfo1sync", "lfo2rate", "lfo2depth", "lfo2shape", "fxdrive",
            "fxchorus", "fxchorusrate", "fxreverb", "modwheel", "mono"};
        return std::string(ids[i]);
    }
    if (i < MacroBase) {
        int j = i - MatrixBase;
        return "mtx" + std::to_string(j / kDests) + "_" + std::to_string(j % kDests);
    }
    if (i >= ExtraBase) {
        static const char* ex[] = {"bend", "bendrange", "outpan", "lfo2sync", "fxdriveon", "fxdrivemode", "fxtone",
            "fxchoruson", "fxchorusvoices", "fxreverbon", "fxreverbmode", "fxreverbsize", "dcurve", "rcurve", "envexp",
            "env2dcurve", "env2rcurve", "env2exp"};
        return std::string(ex[i - ExtraBase]);
    }
    int j = i - MacroBase, m = j / 3, f = j % 3;
    return "mac" + std::to_string(m) + (f == 0 ? "val" : f == 1 ? "dest" : "amt");
}

std::string WavetableSynth::pluginParamName(int32_t i) const {
    if (i < 0 || i >= kNumParams) return {};
    if (i < MatrixBase) {
        static const char* nm[] = {"Osc1 Table", "Osc1 Position", "Osc1 Warp", "Unison", "Attack", "Decay", "Sustain",
            "Release", "Cutoff", "Resonance", "Gain", "Osc1 Warp Mode", "Osc1 Level", "Osc1 Octave", "Osc1 Semi",
            "Osc1 Detune", "Osc1 On", "Osc2 Table", "Osc2 Position", "Osc2 Warp", "Osc2 Warp Mode", "Osc2 Level",
            "Osc2 Octave", "Osc2 Semi", "Osc2 Detune", "Osc2 On", "Sub Level", "Sub Octave", "Sub Wave",
            "Unison Detune", "Unison Spread", "Filter 1 Type", "Filter 1 Slope", "Filter 1 Env", "Filter 1 LFO",
            "Filter 1 Key", "Filter 2 Type", "Filter 2 Freq", "Filter 2 Reso", "Filter 2 Slope", "Filter 2 Env",
            "Filter 2 LFO", "Filter 2 Key", "Route Osc1", "Route Osc2", "Route Sub", "Filter Series", "Env2 Attack",
            "Env2 Decay", "Env2 Sustain", "Env2 Release", "LFO1 Rate", "LFO1 Depth", "LFO1 Shape", "LFO1 Sync",
            "LFO2 Rate", "LFO2 Depth", "LFO2 Shape", "Drive", "Chorus", "Chorus Rate", "Reverb", "Mod Wheel",
            "Mono Mode"};
        return std::string(nm[i]);
    }
    static const char* sn[] = {"Env 1", "Env 2", "LFO 1", "LFO 2", "Velocity", "Key", "Mod Whl", "Random"};
    static const char* dn[] = {"Pitch", "Osc2 Pitch", "Position", "Cutoff", "Reso", "Level", "Pan"};
    if (i < MacroBase) {
        int j = i - MatrixBase;
        return std::string(sn[j / kDests]) + " → " + dn[j % kDests];
    }
    if (i >= ExtraBase) {
        static const char* exNm[] = {"Pitch Bend", "Bend Range", "Out Pan", "LFO2 Sync", "Drive On", "Drive Mode",
            "Drive Tone", "Chorus On", "Chorus Voices", "Reverb On", "Reverb Mode", "Reverb Size", "Decay Curve",
            "Release Curve", "Env Exp", "Env2 Decay Curve", "Env2 Release Curve", "Env2 Exp"};
        return std::string(exNm[i - ExtraBase]);
    }
    int j = i - MacroBase, m = j / 3, f = j % 3;
    return "Macro " + std::to_string(m + 1) + (f == 0 ? "" : f == 1 ? " Dest" : " Amount");
}

void WavetableSynth::pluginParamSet(int32_t i, float v) {
    if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    wake();
}

int32_t WavetableSynth::pluginParamIndexOfId(const std::string& id) const {
    static const std::unordered_map<std::string, int32_t> map = [this] {
        std::unordered_map<std::string, int32_t> m;
        m.reserve(kNumParams * 2);
        for (int32_t i = 0; i < kNumParams; ++i) m.emplace(pluginParamId(i), i);
        return m;
    }();
    auto it = map.find(id);
    return it == map.end() ? -1 : it->second;
}

// ---- state blob (contracts/state-blob.md) --------------------------------
namespace {
constexpr char kTrailerTag[4] = {'A', 'W', 'T', '1'};
constexpr uint32_t kTrailerVersion = 1;

void putU8(std::vector<uint8_t>& b, uint8_t v) {
    b.push_back(v);
}
void putU16(std::vector<uint8_t>& b, uint16_t v) {
    b.push_back(static_cast<uint8_t>(v & 0xFF));
    b.push_back(static_cast<uint8_t>(v >> 8));
}
void putU32(std::vector<uint8_t>& b, uint32_t v) {
    for (int i = 0; i < 4; ++i) b.push_back(static_cast<uint8_t>(v >> (8 * i)));
}
void putStr(std::vector<uint8_t>& b, const std::string& s) {
    const size_t n = std::min<size_t>(s.size(), 0xFFFF);
    putU16(b, static_cast<uint16_t>(n));
    b.insert(b.end(), s.begin(), s.begin() + static_cast<ptrdiff_t>(n));
}

struct Reader {
    const uint8_t* p;
    size_t n, at = 0;
    bool ok = true;
    bool need(size_t k) {
        if (!ok || at + k > n) ok = false;
        return ok;
    }
    uint8_t u8() { return need(1) ? p[at++] : 0; }
    uint16_t u16() {
        if (!need(2)) return 0;
        const uint16_t v = static_cast<uint16_t>(p[at] | (p[at + 1] << 8));
        at += 2;
        return v;
    }
    uint32_t u32() {
        if (!need(4)) return 0;
        uint32_t v = 0;
        for (int i = 0; i < 4; ++i) v |= static_cast<uint32_t>(p[at + static_cast<size_t>(i)]) << (8 * i);
        at += 4;
        return v;
    }
    std::string str() {
        const uint16_t k = u16();
        if (!need(k)) return {};
        std::string s(reinterpret_cast<const char*>(p + at), k);
        at += k;
        return s;
    }
};
} // namespace

std::vector<uint8_t> WavetableSynth::stateWith(bool includeTables) const {
    std::vector<uint8_t> b(kNumParams * sizeof(float));
    for (int i = 0; i < kNumParams; ++i) {
        float v = pn_[i].load(std::memory_order_relaxed);
        std::memcpy(b.data() + static_cast<size_t>(i) * sizeof(float), &v, sizeof(float));
    }
    const auto set = tableSet_.current();
    const uint8_t flag[2] = {
        useCustom_[0].load(std::memory_order_relaxed), useCustom_[1].load(std::memory_order_relaxed)};
    bool any = flag[0] || flag[1];
    if (set)
        for (int o = 0; o < 2; ++o) any = any || set->slot[o] || !set->missing[o].empty();
    if (!any) return b; // byte-identical to the blob before custom tables existed

    const size_t start = b.size();
    b.insert(b.end(), kTrailerTag, kTrailerTag + 4);
    putU32(b, kTrailerVersion);
    putU8(b, flag[0]);
    putU8(b, flag[1]);
    for (int o = 0; o < 2; ++o) {
        const AuroraTable* t = (includeTables && set) ? set->slot[o].get() : nullptr;
        putU8(b, t ? 1 : 0);
        putStr(b, set ? set->missing[o] : std::string());
        if (!t) continue;
        const AuroraTable* t0 = set->slot[0].get();
        if (o == 1) {
            const bool same = t0 && (t0 == t || std::memcmp(t0->hash, t->hash, 16) == 0);
            putU8(b, same ? 1 : 0);
            if (same) continue;
        }
        putStr(b, t->name);
        putU8(b, static_cast<uint8_t>(t->origin));
        putU8(b, t->truncatedLength ? 1 : 0);
        putU8(b, t->truncatedFrames ? 1 : 0);
        putU32(b, static_cast<uint32_t>(t->frames));
        const auto* f = reinterpret_cast<const uint8_t*>(t->base());
        b.insert(b.end(), f, f + static_cast<size_t>(t->frames) * AuroraTable::kSize * sizeof(float));
    }
    const uint32_t S = static_cast<uint32_t>(b.size() - start);
    putU32(b, S);
    b.insert(b.end(), kTrailerTag, kTrailerTag + 4);
    return b;
}

void WavetableSynth::setState(const uint8_t* data, int32_t size) {
    if (!data || size < 0) return;
    // The trailer, when there is one, is found from the end of the blob.
    size_t paramBytes = static_cast<size_t>(size);
    const uint8_t* sec = nullptr;
    size_t secLen = 0;
    if (size >= 8 && std::memcmp(data + size - 4, kTrailerTag, 4) == 0) {
        uint32_t S = 0;
        for (int i = 0; i < 4; ++i) S |= static_cast<uint32_t>(data[size - 8 + i]) << (8 * i);
        if (static_cast<uint64_t>(S) + 8 <= static_cast<uint64_t>(size) && S >= 4 &&
            std::memcmp(data + size - 8 - S, kTrailerTag, 4) == 0) {
            paramBytes = static_cast<size_t>(size) - S - 8;
            sec = data + paramBytes;
            secLen = S;
        }
    }
    const int n = std::min<int>(kNumParams, static_cast<int>(paramBytes / sizeof(float)));
    for (int i = 0; i < n; ++i) {
        float v;
        std::memcpy(&v, data + static_cast<size_t>(i) * sizeof(float), sizeof(float));
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

    if (!sec) {
        // No trailer: a blob from before custom tables, or one without any. Both
        // oscillators go back to their built-in tables.
        const auto cur = tableSet_.current();
        if (cur && (cur->slot[0] || cur->slot[1] || !cur->missing[0].empty() || !cur->missing[1].empty()))
            publishSet(std::make_shared<AuroraTableSet>());
        useCustom_[0].store(0, std::memory_order_relaxed);
        useCustom_[1].store(0, std::memory_order_relaxed);
        wake();
        return;
    }
    Reader r{sec, secLen};
    r.at = 4;
    if (r.u32() != kTrailerVersion || !r.ok) return; // a newer writer: keep the tables as they are
    uint8_t flag[2];
    flag[0] = r.u8();
    flag[1] = r.u8();
    auto next = std::make_shared<AuroraTableSet>();
    for (int o = 0; o < 2 && r.ok; ++o) {
        const uint8_t present = r.u8();
        next->missing[o] = r.str();
        if (!present) continue;
        if (o == 1 && r.u8()) {
            next->slot[1] = next->slot[0];
            continue;
        }
        std::string name = r.str();
        const int32_t origin = r.u8();
        const bool tl = r.u8() != 0, tf = r.u8() != 0;
        const uint32_t frames = r.u32();
        if (!r.ok || frames < 1 || frames > static_cast<uint32_t>(AuroraTable::kMaxFrames)) {
            r.ok = false;
            break;
        }
        const size_t bytes = static_cast<size_t>(frames) * AuroraTable::kSize * sizeof(float);
        if (!r.need(bytes)) break;
        std::vector<float> base(static_cast<size_t>(frames) * AuroraTable::kSize);
        std::memcpy(base.data(), sec + r.at, bytes);
        r.at += bytes;
        std::shared_ptr<const AuroraTable> t;
        if (AuroraTable::buildFromFrames(base.data(), static_cast<int32_t>(frames), std::move(name), origin, t) ==
            AuroraTable::Ok) {
            auto m = std::const_pointer_cast<AuroraTable>(t);
            m->truncatedLength = tl;
            m->truncatedFrames = tf;
            next->slot[o] = t;
        }
    }
    if (!r.ok) return; // a damaged section: the parameters stand, the tables stay as they were
    publishSet(std::move(next));
    useCustom_[0].store(flag[0] ? 1 : 0, std::memory_order_relaxed);
    useCustom_[1].store(flag[1] ? 1 : 0, std::memory_order_relaxed);
    wake();
}

std::shared_ptr<Instrument> WavetableSynth::clone() const {
    auto s = std::make_shared<WavetableSynth>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    if (auto cur = tableSet_.current()) s->tableSet_.publish(cur); // immutable: shared, not copied
    for (int o = 0; o < 2; ++o) s->useCustom_[o].store(useCustom_[o].load(std::memory_order_relaxed));
    s->setSampleRate(sampleRate_);
    return s;
}

// ---- custom tables: message-thread setters ----------------------------------
void WavetableSynth::setTable(int osc, std::shared_ptr<const AuroraTable> t) {
    if (osc < 0 || osc > 1) return;
    auto next = copySet();
    next->slot[osc] = std::move(t);
    next->missing[osc].clear();
    const bool has = next->slot[osc] != nullptr;
    publishSet(std::move(next));
    useCustom_[osc].store(has ? 1 : 0, std::memory_order_relaxed);
    wake();
}

void WavetableSynth::clearTable(int osc) {
    if (osc < 0 || osc > 1) return;
    useCustom_[osc].store(0, std::memory_order_relaxed);
    auto next = copySet();
    next->slot[osc].reset();
    next->missing[osc].clear();
    publishSet(std::move(next));
    wake();
}

void WavetableSynth::setMissing(int osc, const std::string& name) {
    if (osc < 0 || osc > 1) return;
    auto next = copySet();
    next->missing[osc] = name;
    publishSet(std::move(next));
}

std::shared_ptr<const AuroraTable> WavetableSynth::table(int osc) const {
    if (osc < 0 || osc > 1) return nullptr;
    auto cur = tableSet_.current();
    return cur ? cur->slot[osc] : nullptr;
}

std::string WavetableSynth::missingName(int osc) const {
    if (osc < 0 || osc > 1) return {};
    auto cur = tableSet_.current();
    return cur ? cur->missing[osc] : std::string();
}

} // namespace nota
