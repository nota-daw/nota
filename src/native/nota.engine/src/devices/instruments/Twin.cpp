// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — the param table, project state and the instrument shell (see Twin.h).
// The three descriptor tables below ARE the persisted layout: their order matches the enums
// in Twin.h and only ever grows at the end.

#include "devices/instruments/Twin.h"
#include "devices/instruments/TwinMod.h"
#include "devices/effects/Chorus.h"
#include "devices/effects/Delay.h"
#include "devices/effects/Flanger.h"
#include "devices/effects/Phaser.h"
#include "devices/effects/Reverb.h"
#include "dsp/core/ScopedNoDenormals.h"

#include <cstdio>
#include <cstring>
#include <unordered_map>

namespace nota {

namespace twin {
namespace {

using D = Domain;

struct Desc {
    const char* id;   // id suffix
    const char* name; // display name after the part / side prefix
    float def;
    uint16_t steps;
    Domain domain;
};

constexpr Desc kGlobalDesc[] = {
    {"mode", "Patch Mode", 0.0f, 3, D::Linear},
    {"splitkey", "Patch SplitKey", 60.0f / 127.0f, 128, D::Linear},
    {"share", "Patch VoiceShare", 0.5f, 5, D::Linear},
    {"voices", "Patch Voices", 1.0f, 9, D::Linear},
    {"padx", "Pad X", 0.5f, 0, D::Linear},
    {"pady", "Pad Y", 0.5f, 0, D::Linear},
    {"padz", "Pad Z", 0.0f, 0, D::Linear},
    {"bendrange", "Patch BendRange", 0.0f, 4, D::Linear},
    {"seq.on", "Sequencer On", 0.0f, 2, D::Linear},
    {"seq.kind", "Sequencer Kind", 0.0f, 2, D::Linear},
    {"seq.pattern", "Sequencer Pattern", 0.0f, 8, D::Linear},
    {"seq.rate", "Sequencer Rate", 0.6f, 6, D::Linear},
    {"seq.swing", "Sequencer Swing", 0.0f, 0, D::Linear},
    {"seq.latch", "Sequencer Latch", 0.0f, 2, D::Linear},
    {"seq.transpose", "Sequencer Transpose", 1.0f, 2, D::Linear},
    {"seq.target", "Sequencer Target", 0.0f, 3, D::Linear},
    {"arp.mode", "Arp Mode", 0.0f, 6, D::Linear},
    {"arp.oct", "Arp Octaves", 0.0f, 4, D::Linear},
    {"arp.gate", "Arp Gate", 0.5f, 0, D::Linear},
    {"arp.hold", "Arp Hold", 0.0f, 2, D::Linear},
    {"view", "View", 0.0f, 2, D::Linear}, // 0 = L, 1 = S (InstrumentView); opens as L
};
static_assert(sizeof(kGlobalDesc) / sizeof(kGlobalDesc[0]) == kGlobalN, "global table out of step with Twin.h");

constexpr Desc kPartDesc[] = {
    {"morph", "Morph", 0.0f, 0, D::Linear},
    {"voicemode", "Voice Mode", 0.0f, 4, D::Linear},
    {"glide", "Voice Glide", 0.0f, 0, D::LogTime},
    {"glidelegato", "Voice GlideLegato", 0.0f, 2, D::Linear},
    {"octave", "Voice Octave", 0.5f, 5, D::Linear},
    {"vol", "Out Volume", 0.7f, 0, D::Linear},
    {"pan", "Out Pan", 0.5f, 0, D::Linear},
    {"fxon", "Effects On", 1.0f, 2, D::Linear},
};
static_assert(sizeof(kPartDesc) / sizeof(kPartDesc[0]) == PMacro0, "part table out of step with Twin.h");

constexpr Desc kSideDesc[] = {
    {"o1wave", "Osc1 Wave", 0.5f, 0, D::Linear},
    {"o1pw", "Osc1 PW", 0.5f, 0, D::Linear},
    {"o1metal", "Osc1 Metal", 0.0f, 0, D::Linear},
    {"o1coarse", "Osc1 Coarse", 0.5f, 49, D::Linear},
    {"o1fine", "Osc1 Fine", 0.5f, 0, D::Linear},
    {"o1level", "Osc1 Level", 1.0f, 0, D::Linear},
    {"o1bal", "Osc1 Filter", 1.0f, 0, D::Linear},
    {"o2wave", "Osc2 Wave", 0.5f, 0, D::Linear},
    {"o2pw", "Osc2 PW", 0.5f, 0, D::Linear},
    {"o2metal", "Osc2 Metal", 0.0f, 0, D::Linear},
    {"o2coarse", "Osc2 Coarse", 0.5f, 49, D::Linear},
    {"o2fine", "Osc2 Fine", 0.5f, 0, D::Linear},
    {"o2level", "Osc2 Level", 0.0f, 0, D::Linear},
    {"o2bal", "Osc2 Filter", 1.0f, 0, D::Linear},
    {"sync", "Osc2 Sync", 0.0f, 2, D::Linear},
    {"fm21", "Osc2 FM", 0.0f, 0, D::Linear},
    {"fmfilt", "Osc2 FilterFM", 0.0f, 0, D::Linear},
    {"o2lo", "Osc2 LowFreq", 0.0f, 2, D::Linear},
    {"noiselevel", "Noise Level", 0.0f, 0, D::Linear},
    {"noisecolor", "Noise Colour", 0.5f, 0, D::Linear},
    {"noisebal", "Noise Filter", 1.0f, 0, D::Linear},
    {"mmcut", "Multimode Cutoff", 1.0f, 0, D::Pitch},
    {"mmres", "Multimode Resonance", 0.0f, 0, D::Linear},
    {"mmmode", "Multimode Mode", 0.0f, 0, D::Linear},
    {"mmkey", "Multimode KeyTrack", 0.0f, 0, D::Linear},
    {"mmenv", "Multimode Env", 0.5f, 0, D::Linear},
    {"ldcut", "Ladder Cutoff", 0.7f, 0, D::Pitch},
    {"ldres", "Ladder Resonance", 0.1f, 0, D::Linear},
    {"ldslope", "Ladder Slope", 1.0f, 2, D::Linear},
    {"ldkey", "Ladder KeyTrack", 0.5f, 0, D::Linear},
    {"ldenv", "Ladder Env", 0.6f, 0, D::Linear},
    {"route", "Filters Route", 1.0f, 0, D::Linear},
    {"brute", "Filters Brute", 0.0f, 0, D::Linear},
    {"drive", "Filters Drive", 0.0f, 0, D::Linear},
    {"fenva", "FilterEnv Attack", 0.0f, 0, D::LogTime},
    {"fenvd", "FilterEnv Decay", 0.4f, 0, D::LogTime},
    {"fenvs", "FilterEnv Sustain", 0.5f, 0, D::Linear},
    {"fenvr", "FilterEnv Release", 0.3f, 0, D::LogTime},
    {"fenvvel", "FilterEnv Velocity", 0.3f, 0, D::Linear},
    {"fenvcurve", "FilterEnv Curve", 0.5f, 0, D::Linear},
    {"aenva", "AmpEnv Attack", 0.0f, 0, D::LogTime},
    {"aenvd", "AmpEnv Decay", 0.4f, 0, D::LogTime},
    {"aenvs", "AmpEnv Sustain", 1.0f, 0, D::Linear},
    {"aenvr", "AmpEnv Release", 0.25f, 0, D::LogTime},
    {"aenvvel", "AmpEnv Velocity", 0.5f, 0, D::Linear},
    {"aenvcurve", "AmpEnv Curve", 0.5f, 0, D::Linear},
    {"menvdly", "ModEnv Delay", 0.0f, 0, D::LogTime},
    {"menva", "ModEnv Attack", 0.0f, 0, D::LogTime},
    {"menvd", "ModEnv Decay", 0.4f, 0, D::LogTime},
    {"menvs", "ModEnv Sustain", 0.0f, 0, D::Linear},
    {"menvr", "ModEnv Release", 0.3f, 0, D::LogTime},
    {"menvvel", "ModEnv Velocity", 0.0f, 0, D::Linear},
    {"menvloop", "ModEnv Loop", 0.0f, 2, D::Linear},
    {"lfo1shape", "LFO1 Shape", 0.0f, 6, D::Linear},
    {"lfo1rate", "LFO1 Rate", 0.45f, 0, D::LogTime},
    {"lfo1sync", "LFO1 Sync", 0.0f, 2, D::Linear},
    {"lfo1div", "LFO1 Division", 0.5f, 14, D::Linear},
    {"lfo1mode", "LFO1 Mode", 0.0f, 2, D::Linear},
    {"lfo1retrig", "LFO1 Retrigger", 0.0f, 2, D::Linear},
    {"lfo1fade", "LFO1 Fade", 0.0f, 0, D::LogTime},
    {"lfo1amt", "LFO1 Amount", 1.0f, 0, D::Linear},
    {"lfo2shape", "LFO2 Shape", 0.0f, 6, D::Linear},
    {"lfo2rate", "LFO2 Rate", 0.35f, 0, D::LogTime},
    {"lfo2sync", "LFO2 Sync", 0.0f, 2, D::Linear},
    {"lfo2div", "LFO2 Division", 0.5f, 14, D::Linear},
    {"lfo2mode", "LFO2 Mode", 0.0f, 2, D::Linear},
    {"lfo2retrig", "LFO2 Retrigger", 0.0f, 2, D::Linear},
    {"lfo2fade", "LFO2 Fade", 0.0f, 0, D::LogTime},
    {"lfo2amt", "LFO2 Amount", 1.0f, 0, D::Linear},
    {"lfo3shape", "LFO3 Shape", 0.0f, 6, D::Linear},
    {"lfo3rate", "LFO3 Rate", 0.25f, 0, D::LogTime},
    {"lfo3sync", "LFO3 Sync", 0.0f, 2, D::Linear},
    {"lfo3div", "LFO3 Division", 0.5f, 14, D::Linear},
    {"lfo3mode", "LFO3 Mode", 1.0f, 2, D::Linear},
    {"lfo3retrig", "LFO3 Retrigger", 0.0f, 2, D::Linear},
    {"lfo3fade", "LFO3 Fade", 0.0f, 0, D::LogTime},
    {"lfo3amt", "LFO3 Amount", 1.0f, 0, D::Linear},
    {"lfo3sym", "LFO3 Symmetry", 0.5f, 0, D::Linear},
    {"lfo3curve", "LFO3 Curve", 0.5f, 0, D::Linear},
    {"vibamt", "Perform Vibrato", 0.15f, 0, D::Linear},
    {"drift", "Voice Drift", 0.2f, 0, D::Linear},
    {"unidetune", "Voice UniDetune", 0.3f, 0, D::Linear},
    {"spread", "Voice Spread", 0.3f, 0, D::Linear},
    {"spreadmode", "Voice SpreadMode", 0.0f, 3, D::Linear},
    {"modfxtype", "ModFX Type", 0.0f, 4, D::Linear},
    {"modfxrate", "ModFX Rate", 0.3f, 0, D::LogTime},
    {"modfxdepth", "ModFX Depth", 0.5f, 0, D::Linear},
    {"modfxmix", "ModFX Mix", 0.3f, 0, D::Linear},
    {"dlytime", "Delay Time", 0.2f, 0, D::Linear}, // the Delay device: 2 s × v
    {"dlysync", "Delay Sync", 1.0f, 2, D::Linear},
    {"dlydiv", "Delay Division", 5.0f / 7.0f, 8, D::Linear}, // the Delay device's 1/16 … 1/2
    {"dlyfb", "Delay Feedback", 0.35f, 0, D::Linear},
    {"dlypingpong", "Delay PingPong", 1.0f, 2, D::Linear},
    {"dlymix", "Delay Mix", 0.25f, 0, D::Linear},
    {"revsize", "Reverb Size", 0.5f, 0, D::Linear},
    {"revdamp", "Reverb Damping", 0.5f, 0, D::Linear},
    {"revmix", "Reverb Mix", 0.2f, 0, D::Linear},
    {"modfxon", "ModFX On", 0.0f, 2, D::Linear},
    {"dlyon", "Delay On", 0.0f, 2, D::Linear},
    {"revon", "Reverb On", 0.0f, 2, D::Linear},
};
static_assert(sizeof(kSideDesc) / sizeof(kSideDesc[0]) == MatAmt0, "side table out of step with Twin.h");

ParamInfo make(const std::string& idPrefix, const std::string& namePrefix, const Desc& d) {
    return {idPrefix + d.id, namePrefix + d.name, d.def, d.steps, d.domain};
}

std::vector<ParamInfo> buildTable() {
    std::vector<ParamInfo> t;
    t.reserve(kNumParams);
    for (const Desc& d : kGlobalDesc) t.push_back(make("", "", d));
    static const char* const partId[kParts] = {"lo.", "up."};
    static const char* const partName[kParts] = {"Lower", "Upper"};
    static const char* const sideId[kSides] = {"a.", "b."};
    static const char* const sideName[kSides] = {" A", " B"};
    char buf[64];
    for (int p = 0; p < kParts; ++p) {
        const std::string pid = partId[p], pname = std::string(partName[p]) + " · ";
        for (const Desc& d : kPartDesc) t.push_back(make(pid, pname, d));
        for (int m = 0; m < kMacros; ++m) {
            std::snprintf(buf, sizeof buf, "macro%d", m + 1);
            std::string id = pid + buf;
            std::snprintf(buf, sizeof buf, "Macros Macro%d", m + 1);
            t.push_back({id, pname + buf, 0.0f, 0, Domain::Linear});
        }
        for (int r = 0; r < kRoutes; ++r) {
            std::snprintf(buf, sizeof buf, "m%d.src", r + 1);
            std::string sid = pid + buf;
            std::snprintf(buf, sizeof buf, "m%d.dst", r + 1);
            std::string did = pid + buf;
            std::snprintf(buf, sizeof buf, "Routing Source%d", r + 1);
            t.push_back({sid, pname + buf, 0.0f, kSrcScale + 1, Domain::Linear});
            std::snprintf(buf, sizeof buf, "Routing Destination%d", r + 1);
            t.push_back({did, pname + buf, 0.0f, kDstScale + 1, Domain::Linear});
        }
        for (int s = 0; s < kSides; ++s) {
            const std::string sid = pid + sideId[s], sname = std::string(partName[p]) + sideName[s] + " · ";
            for (const Desc& d : kSideDesc) t.push_back(make(sid, sname, d));
            for (int r = 0; r < kRoutes; ++r) {
                std::snprintf(buf, sizeof buf, "m%damt", r + 1);
                std::string id = sid + buf;
                std::snprintf(buf, sizeof buf, "Routing Depth%d", r + 1);
                t.push_back({id, sname + buf, 0.5f, 0, Domain::Linear});
            }
        }
    }
    // Init's routes (FR-033), removable like any other: pressure opens both cutoffs, slide and
    // pad X sweep the multimode, pad Y leans the morph, pad Z deepens the vibrato.
    struct Def {
        int src;
        int dst;
        float depth; // bipolar −1..1
    };
    const Def defs[] = {{SrcPressure, dstIndexOfSide(LdCut), 0.15f}, {SrcPressure, dstIndexOfSide(MmCut), 0.15f},
        {SrcSlide, dstIndexOfSide(MmCut), 0.2f}, {SrcPadX, dstIndexOfSide(MmCut), 0.2f}, {SrcPadY, kDstMorph, 0.5f},
        {SrcPadZ, kDstVibrato, 0.15f}};
    for (int p = 0; p < kParts; ++p)
        for (int r = 0; r < static_cast<int>(sizeof defs / sizeof defs[0]); ++r) {
            t[static_cast<size_t>(partIdx(p, PRoute0 + 2 * r))].def = static_cast<float>(defs[r].src) / kSrcScale;
            t[static_cast<size_t>(partIdx(p, PRoute0 + 2 * r + 1))].def = static_cast<float>(defs[r].dst) / kDstScale;
            for (int sd = 0; sd < kSides; ++sd)
                t[static_cast<size_t>(sideIdx(p, sd, MatAmt0 + r))].def = 0.5f + 0.5f * defs[r].depth;
        }
    return t;
}

} // namespace

const std::vector<ParamInfo>& paramTable() {
    static const std::vector<ParamInfo> t = buildTable();
    return t;
}

} // namespace twin

namespace {

void putU32(std::vector<uint8_t>& b, uint32_t v) {
    for (int i = 0; i < 4; ++i) b.push_back(static_cast<uint8_t>(v >> (i * 8)));
}

uint32_t getU32(const uint8_t* p) {
    return static_cast<uint32_t>(p[0]) | static_cast<uint32_t>(p[1]) << 8 | static_cast<uint32_t>(p[2]) << 16 |
           static_cast<uint32_t>(p[3]) << 24;
}

} // namespace

Twin::Twin() {
    const auto& t = twin::paramTable();
    for (int i = 0; i < kNumParams; ++i) pn_[i].store(t[static_cast<size_t>(i)].def, std::memory_order_relaxed);
    // The effects (US5): one set per part, built and sized here so render never allocates.
    for (int p = 0; p < twin::kParts; ++p) {
        fx_[p][FxChorus] = std::make_unique<Chorus>();
        fx_[p][FxFlanger] = std::make_unique<Flanger>();
        fx_[p][FxPhaser] = std::make_unique<Phaser>();
        fx_[p][FxDelay] = std::make_unique<Delay>();
        fx_[p][FxReverb] = std::make_unique<Reverb>();
        fx_[p][FxDelay]->setParam(Delay::LinkLR, 1.0f); // one time for both sides; Ping-pong spreads it
        for (auto& d : fx_[p]) d->setSampleRate(44100.0, twin::kCtl);
    }
    patAuth_ = std::make_shared<twin::PatternSet>();
    publishPatterns();
    tele_[ScRecStep].store(-1.0f, std::memory_order_relaxed);
    tele_[ScLastKey].store(-1.0f, std::memory_order_relaxed);
    resetAll();
}

void Twin::setSampleRate(double sr) {
    sr = sr > 0 ? sr : 44100.0;
    sampleRate_.store(sr, std::memory_order_relaxed);
    for (auto& part : fx_)
        for (auto& d : part) d->setSampleRate(sr, twin::kCtl);
}

void Twin::setTransportInfo(const TransportInfo& ti) {
    for (auto& part : fx_)
        for (auto& d : part) d->setTransportInfo(ti);
}

std::string Twin::pluginParamId(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? twin::paramTable()[static_cast<size_t>(i)].id : std::string();
}

std::string Twin::pluginParamName(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? twin::paramTable()[static_cast<size_t>(i)].name : std::string();
}

float Twin::pluginParamDefault(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? twin::paramTable()[static_cast<size_t>(i)].def : 0.0f;
}

std::string Twin::pluginParamText(int32_t i) const {
    if (i < 0 || i >= kNumParams) return {};
    const float v = pn_[i].load(std::memory_order_relaxed);
    // A matrix slot's source / destination reads as its name.
    for (int p = 0; p < twin::kParts; ++p) {
        const int r = i - twin::partIdx(p, twin::PRoute0);
        if (r >= 0 && r < 2 * twin::kRoutes)
            return r % 2 == 0 ? twin::srcName(twin::stepOf(v, twin::kSrcScale + 1))
                               : twin::dstName(twin::stepOf(v, twin::kDstScale + 1));
    }
    // Generic readouts until the stories give each param its musical units.
    const auto& d = twin::paramTable()[static_cast<size_t>(i)];
    char buf[32];
    if (d.steps > 1) std::snprintf(buf, sizeof buf, "%d", static_cast<int>(std::lround(v * (d.steps - 1))) + 1);
    else std::snprintf(buf, sizeof buf, "%.0f%%", v * 100.0f);
    return buf;
}

int32_t Twin::pluginParamIndexOfId(const std::string& id) const {
    static const std::unordered_map<std::string, int32_t> index = [] {
        std::unordered_map<std::string, int32_t> m;
        const auto& t = twin::paramTable();
        m.reserve(t.size());
        for (size_t i = 0; i < t.size(); ++i) m.emplace(t[i].id, static_cast<int32_t>(i));
        return m;
    }();
    const auto it = index.find(id);
    return it == index.end() ? -1 : it->second;
}

// ---- voices -------------------------------------------------------------------------------

void Twin::computeStatics(int v) {
    pentad::Rng r;
    r.seed(pentad::hash32(0x7717u * 2654435761U + pentad::hash32(static_cast<uint32_t>(v) + 17U)));
    twin::Voice& V = voices_[v];
    V.oP[0] = r.bi();
    V.oP[1] = r.bi();
    V.oCut = r.bi();
    V.oEnv = r.bi();
}

void Twin::resetAll() {
    for (int v = 0; v < kMaxVoices; ++v) {
        twin::Voice& V = voices_[v];
        V = twin::Voice{};
        V.rng.seed(pentad::hash32(0x7717u ^ pentad::hash32(static_cast<uint32_t>(v) * 0x9E3779B9U + 1U)));
        V.noiseRng = pentad::hash32(static_cast<uint32_t>(v) + 0x51ED27u) | 1u;
        for (auto& l : V.lfo) l.rng.seed(V.rng.next());
        V.osc[0].ph = V.rng.uni();
        V.osc[1].ph = V.rng.uni();
        V.walkLeft = 1 + static_cast<int>(V.rng.next() % 4096u);
        computeStatics(v);
    }
    for (int p = 0; p < twin::kParts; ++p)
        for (int l = 0; l < 3; ++l) {
            glfo_[p][l] = twin::Lfo{};
            glfo_[p][l].rng.seed(pentad::hash32(0xA5A5u + static_cast<uint32_t>(l + 3 * p)));
        }
    heldN_[0] = heldN_[1] = 0;
    heldBits_[0] = heldBits_[1] = 0;
    orderCtr_ = 0;
    ctlLeft_ = 0;
    gexpr_.reset();
    seqNoteN_ = 0;
    transpose_ = transKeys_ = 0;
    arpN_ = arpKeys_ = 0;
    pendPat_ = -1;
    playStep_ = -1;
    seqResync_ = true;
    for (auto& m : motion_) m = Motion{};
    for (auto& m : motionOvr_) m = twin::MotionOverride{};
    for (auto& part : dn_)
        for (auto& d : part) d.clear_buffers();
    // The effects start over too (a silent start or jump re-seeds everything, so renders repeat).
    for (auto& part : fx_) {
        part[FxChorus]->deviceAction(Chorus::A_Restart, 0, 0.0f);
        part[FxChorus]->deviceAction(Chorus::A_Clear, 0, 0.0f);
        part[FxFlanger]->deviceAction(Flanger::A_Restart, 0, 0.0f);
        part[FxFlanger]->deviceAction(Flanger::A_Clear, 0, 0.0f);
        part[FxPhaser]->deviceAction(Phaser::A_Restart, 0, 0.0f);
        part[FxPhaser]->deviceAction(Phaser::A_Clear, 0, 0.0f);
        part[FxDelay]->deviceAction(Delay::A_Reset, 0, 0.0f);
        part[FxReverb]->deviceAction(Reverb::A_Reset, 0, 0.0f);
    }
}

// Each part keeps its own stack of held keys (in the order played) for its mono fallback.
void Twin::pushHeld(int part, int note) {
    removeHeld(part, note);
    int* h = held_[part];
    int& n = heldN_[part];
    if (n == kMaxHeld) {
        std::memmove(h, h + 1, sizeof(int) * (kMaxHeld - 1));
        --n;
    }
    h[n++] = note;
}

void Twin::removeHeld(int part, int note) {
    int* h = held_[part];
    int& n = heldN_[part];
    for (int i = 0; i < n; ++i)
        if (h[i] == note) {
            std::memmove(h + i, h + i + 1, sizeof(int) * static_cast<size_t>(n - i - 1));
            --n;
            break;
        }
}

// ---- parts (US9) ---------------------------------------------------------------------------
// Single: the Lower part has every voice. Layer: both parts play every key. Split: keys below
// the split key go to Lower, the rest to Upper. In Layer and Split the voices are shared out —
// Lower takes `share` (2, 4 … 10), Upper the rest of the cap — and each part steals only from
// its own share.

int Twin::layoutMode() const { return twin::stepOf(get(twin::GMode), 3); }

Twin::Slice Twin::slice(int part) const {
    const int cap = voiceCap();
    if (layoutMode() == 0) return part == 0 ? Slice{0, cap} : Slice{0, 0};
    const int lo = std::clamp(2 + 2 * twin::stepOf(get(twin::GShare), 5), 2, cap - 2);
    return part == 0 ? Slice{0, lo} : Slice{lo, cap - lo};
}

int Twin::keyParts(int pitch, int* out) const {
    switch (layoutMode()) {
    case 1: out[0] = 0, out[1] = 1; return 2;
    case 2: out[0] = pitch < static_cast<int>(std::lround(get(twin::GSplitKey) * 127.0f)) ? 0 : 1; return 1;
    default: out[0] = 0; return 1;
    }
}

int Twin::seqParts(int* out) const {
    if (layoutMode() == 0) return out[0] = 0, 1;
    switch (twin::stepOf(get(twin::GSeqTarget), 3)) {
    case 1: out[0] = 1; return 1;
    case 2: out[0] = 0, out[1] = 1; return 2;
    default: out[0] = 0; return 1;
    }
}

// A free voice (least recently used), else the oldest releasing one, else the oldest of all
// with a steal fade (`fade` = true).
int Twin::allocate(int first, int count, bool& fade) {
    fade = false;
    int best = -1;
    uint64_t bo = ~0ull;
    for (int v = first; v < first + count; ++v)
        if (!voices_[v].active && voices_[v].pendNote < 0 && voices_[v].order < bo) bo = voices_[v].order, best = v;
    if (best >= 0) return best;
    for (int v = first; v < first + count; ++v)
        if (!voices_[v].gate && voices_[v].pendNote < 0 && voices_[v].order < bo) bo = voices_[v].order, best = v;
    if (best >= 0) return best;
    for (int v = first; v < first + count; ++v)
        if (voices_[v].pendNote < 0 && voices_[v].order < bo) bo = voices_[v].order, best = v;
    fade = true;
    return best >= 0 ? best : first;
}

void Twin::wakeVoice(twin::Voice& V) {
    V.active = true;
    V.fresh = true;
    for (auto& o : V.osc) {
        o.ph += V.rng.uni();
        o.ph -= std::floor(o.ph);
        o.hi = true;
        o.ring.clear();
    }
    V.o2Out = 0.0f;
    V.fx1[0] = V.fx1[1] = 0.0;
    V.fy1[0] = V.fy1[1] = 0.0f;
    V.noiseLp = 0.0f;
    V.mm.reset();
    V.ld.reset();
    V.ld.setInternalOversampling(1); // the voice already runs at ×2
    V.sat.reset();
    V.fb = 0.0f;
}

void Twin::startVoice(int v, int note, float vel, int s, int S, bool glide) {
    twin::Voice& V = voices_[v];
    const twin::PartCtl& c = ctl_[V.part];
    V.note = note;
    V.vel = vel;
    V.relVel = 0.0f;
    V.gate = true;
    V.seq = false;
    V.stackIdx = s;
    V.stackN = S;
    V.order = ++orderCtr_;
    const float u = S > 1 ? 2.0f * static_cast<float>(s) / static_cast<float>(S - 1) - 1.0f : 0.0f;
    V.det = u * c.uniDetune * 0.5f;
    V.pitchTgt = static_cast<float>(note);
    if (!V.hasPitch || !glide) V.pitch = V.pitchTgt;
    V.hasPitch = true;
    // Stereo position: the unison stack fans out; otherwise by voice or by key.
    float pos;
    if (S > 1) pos = u;
    else if (c.spreadMode == 1)
        pos = std::clamp((static_cast<float>(note) - 60.0f) / 30.0f, -1.0f, 1.0f);
    else
        pos = static_cast<float>((v * 7) % kMaxVoices) / static_cast<float>(kMaxVoices - 1) * 2.0f - 1.0f;
    const float pan = std::clamp(pos * c.spread + c.pan, -1.0f, 1.0f);
    V.panL = std::cos((pan + 1.0f) * 0.78539816f);
    V.panR = std::sin((pan + 1.0f) * 0.78539816f);
    for (int l = 0; l < 3; ++l) {
        if (c.lfoRetrig[l] && !c.lfoGlobal[l]) V.lfo[l].ph = 0.0;
        V.lfoFadeLvl[l] = c.lfoFade[l] > 0.0f ? 0.0f : 1.0f;
        V.lfoFadeInc[l] = c.lfoFade[l] > 0.0f ? static_cast<float>(1.0 / (c.lfoFade[l] * sr_)) : 0.0f;
    }
    if (!V.active) wakeVoice(V);
    V.fresh = true; // snap the ramps to this note's values
    V.ex.reset(c.exAdd);
    V.rnd = V.rng.bi();
    twin::voiceControl(V, c, sr_);
    V.fenv.gateOn(0);
    V.aenv.gateOn(0);
    V.menv.gateOn(static_cast<int32_t>(c.mDly * sr_));
}

void Twin::startPending(twin::Voice& V) {
    const int note = V.pendNote;
    V.pendNote = -1;
    V.fenv.lvl = V.aenv.lvl = V.menv.lvl = 0.0f;
    startVoice(static_cast<int>(&V - voices_), note, V.pendVel, V.pendStack, V.pendStackN, V.pendGlide);
    V.seq = V.pendSeq;
    for (int d = 0; d < kExprDims; ++d) V.ex.set(d, V.pendExpr[d], ctl_[V.part].exAdd);
}

void Twin::release(twin::Voice& V) {
    V.gate = false;
    V.fenv.gateOff();
    V.aenv.gateOff();
    V.menv.gateOff();
}

// ---- notes in: the keyboard, or the sequencer ----------------------------------------------

void Twin::noteOn(int32_t pitch, float velocity) {
    if (pitch < 0 || pitch > 127) return;
    wake();
    applyRate();
    if (!ctlInit_) updateControl();
    const float vel = std::clamp(velocity, 0.0f, 1.0f);
    tele_[ScLastKey].store(static_cast<float>(pitch), std::memory_order_relaxed);
    if (recArmed_.load(std::memory_order_relaxed)) { // step record: the card turns these into steps
        const uint32_t h = ringHead_.load(std::memory_order_relaxed);
        if (h - ringTail_.load(std::memory_order_acquire) < kRing) {
            ring_[h % kRing].store(static_cast<uint32_t>(pitch) << 8 | static_cast<uint32_t>(std::lround(vel * 127.0f)), std::memory_order_relaxed);
            ringHead_.store(h + 1, std::memory_order_release);
        }
    }
    if (seqActive()) {
        if (twin::stepOf(get(twin::GSeqKind), 2) == 1) { // the arpeggiator takes the keys
            if (get(twin::GArpHold) >= 0.5f && arpKeys_ == 0) arpN_ = 0; // a new chord replaces a held one
            if (arpN_ == 0) arpStartK_ = nextK_;
            if (arpN_ < 32) arpList_[arpN_++] = pitch;
            ++arpKeys_;
            arpVel_ = vel;
            return;
        }
        if (get(twin::GSeqTranspose) >= 0.5f) { // the key transposes the pattern from its first note
            int root = 60;
            if (const twin::PatternSet* ps = pats_.live()) {
                const twin::Pattern& P = ps->pat[curPat_];
                for (int i = 0; i < P.length; ++i)
                    if (P.step[i].n > 0) { root = P.step[i].note[0]; break; }
            }
            transpose_ = pitch - root;
            ++transKeys_;
            return;
        }
    }
    keyOn(pitch, vel);
}

void Twin::noteOff(int32_t pitch) {
    if (pitch < 0 || pitch > 127) return;
    wake();
    if (recArmed_.load(std::memory_order_relaxed)) {
        const uint32_t h = ringHead_.load(std::memory_order_relaxed);
        if (h - ringTail_.load(std::memory_order_acquire) < kRing) {
            ring_[h % kRing].store(1u << 16 | static_cast<uint32_t>(pitch) << 8, std::memory_order_relaxed);
            ringHead_.store(h + 1, std::memory_order_release);
        }
    }
    if (arpKeys_ > 0 || transKeys_ > 0) {
        bool wasArp = false;
        for (int i = 0; i < arpN_; ++i)
            if (arpList_[i] == pitch) wasArp = true;
        if (wasArp && arpKeys_ > 0) {
            --arpKeys_;
            if (get(twin::GArpHold) < 0.5f) { // without Hold the arpeggio follows the keys
                int w = 0;
                for (int i = 0; i < arpN_; ++i)
                    if (arpList_[i] != pitch) arpList_[w++] = arpList_[i];
                arpN_ = w;
            }
            return;
        }
        if (transKeys_ > 0) {
            if (--transKeys_ == 0 && get(twin::GSeqLatch) < 0.5f) transpose_ = 0;
            return;
        }
    }
    keyOff(pitch);
}

// The release velocity reaches the key's sounding voices before they release (FR-030).
void Twin::noteOffVelocity(int32_t pitch, float velocity) {
    if (pitch < 0 || pitch > 127) return;
    for (auto& V : voices_)
        if (V.active && V.gate && !V.seq && V.note == pitch) V.relVel = std::clamp(velocity, 0.0f, 1.0f);
    noteOff(pitch);
}

void Twin::keyOn(int pitch, float vel) {
    heldBits_[pitch >> 6] |= uint64_t{1} << (pitch & 63);
    int parts[2];
    const int n = keyParts(pitch, parts);
    for (int i = 0; i < n; ++i) keyOnPart(parts[i], pitch, vel);
}

void Twin::keyOff(int pitch) {
    heldBits_[pitch >> 6] &= ~(uint64_t{1} << (pitch & 63));
    for (int p = 0; p < twin::kParts; ++p) keyOffPart(p, pitch);
}

void Twin::keyOnPart(int part, int pitch, float vel) {
    const twin::PartCtl& c = ctl_[part];
    const Slice sl = slice(part);
    if (sl.count <= 0) return;
    const bool othersHeld = heldN_[part] > 0;
    pushHeld(part, pitch);
    const bool glideOn = c.glideSec > 0.0f && (!c.glideLegato || othersHeld);
    if (c.voiceMode == 0) { // Poly
        int v = -1;
        for (int k = sl.first; k < sl.first + sl.count; ++k) // the same key again → retrigger its voice
            if (voices_[k].gate && voices_[k].note == pitch && voices_[k].pendNote < 0 && !voices_[k].seq) {
                v = k;
                break;
            }
        bool fade = false;
        if (v < 0) v = allocate(sl.first, sl.count, fade);
        twin::Voice& V = voices_[v];
        V.part = part;
        if (fade) {
            V.pendNote = pitch;
            V.pendVel = vel;
            V.pendStack = 0;
            V.pendStackN = 1;
            V.pendGlide = glideOn;
            V.pendSeq = false;
            for (int d = 0; d < kExprDims; ++d) V.pendExpr[d] = kExprNeutral[d];
            V.fade = fadeLen_;
            V.gate = false;
        } else
            startVoice(v, pitch, vel, 0, 1, glideOn);
        return;
    }
    // Mono / Legato / Unison: the last key wins; Legato and Unison glide without retriggering
    // while another key is held, Mono always retriggers.
    const int S = c.voiceMode == 3 ? sl.count : 1;
    if (!othersHeld || c.voiceMode == 1) {
        for (int s = 0; s < S; ++s) {
            voices_[sl.first + s].part = part;
            startVoice(sl.first + s, pitch, vel, s, S, glideOn);
        }
        return;
    }
    for (int s = 0; s < S; ++s) {
        twin::Voice& V = voices_[sl.first + s];
        V.note = pitch;
        V.pitchTgt = static_cast<float>(pitch);
        if (!glideOn) V.pitch = V.pitchTgt;
    }
}

void Twin::keyOffPart(int part, int pitch) {
    bool wasHeld = false;
    for (int i = 0; i < heldN_[part]; ++i) wasHeld |= held_[part][i] == pitch;
    removeHeld(part, pitch);
    const twin::PartCtl& c = ctl_[part];
    const Slice sl = slice(part);
    if (c.voiceMode == 0 || sl.count <= 0) {
        for (auto& V : voices_) {
            if (V.part != part) continue;
            if (V.gate && V.note == pitch && !V.seq) release(V);
            if (V.pendNote == pitch && !V.pendSeq) V.pendNote = -1; // ended before its steal fade finished
        }
        return;
    }
    if (!wasHeld) return;
    const int S = c.voiceMode == 3 ? sl.count : 1;
    if (heldN_[part] == 0) {
        for (int s = 0; s < S; ++s)
            if (voices_[sl.first + s].gate && !voices_[sl.first + s].seq) release(voices_[sl.first + s]);
        return;
    }
    const int back = held_[part][heldN_[part] - 1]; // fall back to the last key still held
    for (int s = 0; s < S; ++s) {
        twin::Voice& V = voices_[sl.first + s];
        if (!V.active) continue;
        V.note = back;
        V.pitchTgt = static_cast<float>(back);
        if (c.glideSec <= 0.0f) V.pitch = V.pitchTgt;
    }
}

// A sequencer note: like a key, but outside the held-key stack, and released only by the
// sequencer. It plays on the parts seq.target names. In the mono modes the last note wins.
void Twin::seqOn(int pitch, float vel) {
    int parts[2];
    const int np = seqParts(parts);
    for (int i = 0; i < np; ++i) {
        const int part = parts[i];
        const twin::PartCtl& c = ctl_[part];
        const Slice sl = slice(part);
        if (sl.count <= 0) continue;
        if (c.voiceMode == 0) {
            bool fade = false;
            const int v = allocate(sl.first, sl.count, fade);
            twin::Voice& V = voices_[v];
            V.part = part;
            if (fade) {
                V.pendNote = pitch;
                V.pendVel = vel;
                V.pendStack = 0;
                V.pendStackN = 1;
                V.pendGlide = false;
                V.pendSeq = true;
                for (int d = 0; d < kExprDims; ++d) V.pendExpr[d] = kExprNeutral[d];
                V.fade = fadeLen_;
                V.gate = false;
            } else {
                startVoice(v, pitch, vel, 0, 1, false);
                V.seq = true;
            }
            continue;
        }
        const int S = c.voiceMode == 3 ? sl.count : 1;
        const bool legato = seqNoteN_ > 1 && c.voiceMode != 1;
        for (int s = 0; s < S; ++s) {
            twin::Voice& V = voices_[sl.first + s];
            V.part = part;
            if (legato && V.gate) {
                V.note = pitch;
                V.pitchTgt = static_cast<float>(pitch);
                if (c.glideSec <= 0.0f) V.pitch = V.pitchTgt;
            } else
                startVoice(sl.first + s, pitch, vel, s, S, c.glideSec > 0.0f);
            V.seq = true;
        }
    }
}

void Twin::seqOff(int pitch) {
    for (auto& V : voices_) {
        if (V.gate && V.seq && V.note == pitch) release(V);
        if (V.pendNote == pitch && V.pendSeq) V.pendNote = -1;
    }
}

void Twin::allNotesOff() {
    resetAll();
}

// ---- the sequencer (US8) -------------------------------------------------------------------
// Steps sit on a grid counted from the song's start (AR-9: the transport is the only clock), so a
// step's sample is the same however the host splits its blocks; a pattern's step is that index
// modulo its length. Odd steps lean late by the swing. While the transport is stopped the
// sequencer is silent and the keys play the instrument as usual.

bool Twin::seqActive() const {
    return get(twin::GSeqOn) >= 0.5f && playing_ && spb_ > 1.0;
}

double Twin::stepBeats() const {
    static constexpr double kRates[6] = {1.0, 0.5, 1.0 / 3.0, 0.25, 1.0 / 6.0, 0.125}; // 1/4 1/8 1/8T 1/16 1/16T 1/32
    return kRates[twin::stepOf(get(twin::GSeqRate), 6)];
}

double Twin::onsetSample(int64_t k) const {
    const double swing = (k & 1) ? get(twin::GSeqSwing) * 0.5 * seqStepBeats_ : 0.0;
    return std::ceil(((static_cast<double>(k) * seqStepBeats_) + swing) * spb_ - 1e-6);
}

void Twin::releaseSeqNotes() {
    for (int i = 0; i < seqNoteN_; ++i) seqOff(seqNotes_[i].pitch);
    seqNoteN_ = 0;
}

int Twin::seqService(int offset) {
    if (!seqActive()) {
        if (seqRunning_) {
            releaseSeqNotes();
            seqRunning_ = false;
            playStep_ = -1;
            for (auto& m : motion_) m.set = false;
        }
        return INT32_MAX;
    }
    const double now = tPos_ + tFrames_ + offset; // absolute sample
    const double sb = stepBeats();
    if (seqResync_ || sb != seqStepBeats_ || !seqRunning_) {
        if (seqResync_) releaseSeqNotes();
        seqStepBeats_ = sb;
        nextK_ = static_cast<int64_t>(std::ceil(now / spb_ / sb - 1e-9));
        while (onsetSample(nextK_) < now) ++nextK_;
        if (!seqRunning_) curPat_ = twin::stepOf(get(twin::GSeqPattern), twin::kPatterns), pendPat_ = -1;
        seqResync_ = false;
        seqRunning_ = true;
    }
    // Note-offs that are due, then the steps that are.
    for (int i = 0; i < seqNoteN_;)
        if (!seqNotes_[i].tie && seqNotes_[i].off <= now) {
            seqOff(seqNotes_[i].pitch);
            seqNotes_[i] = seqNotes_[--seqNoteN_];
        } else
            ++i;
    while (onsetSample(nextK_) <= now) {
        fireStep(nextK_, onsetSample(nextK_));
        ++nextK_;
    }
    double next = onsetSample(nextK_);
    for (int i = 0; i < seqNoteN_; ++i)
        if (!seqNotes_[i].tie) next = std::min(next, seqNotes_[i].off);
    return std::max(1, static_cast<int>(std::ceil(next - now)));
}

void Twin::fireStep(int64_t k, double onset) {
    const twin::PatternSet* ps = pats_.live();
    if (!ps) return;
    const double stepLen = seqStepBeats_ * spb_;
    int notes[twin::kStepNotes];
    int n = 0;
    float vel = 0.8f;
    double off = onset + 0.5 * stepLen;
    bool tie = false;
    const int want = twin::stepOf(get(twin::GSeqPattern), twin::kPatterns);
    if (want != curPat_ && pendPat_ != want) pendPat_ = want; // switches at the pattern's end
    if (twin::stepOf(get(twin::GSeqKind), 2) == 1) {
        // The arpeggiator: the held (or held-over) chord, one note — or all of it — per step.
        n = twin::arpNotes(arpList_, arpN_, twin::stepOf(get(twin::GArpMode), twin::kArpModes),
            1 + twin::stepOf(get(twin::GArpOct), 4), k - arpStartK_, 0x5EEDu, notes, twin::kStepNotes);
        vel = arpVel_;
        off = onset + std::max(0.05, static_cast<double>(get(twin::GArpGate))) * stepLen;
        playStep_ = arpN_ > 0 ? static_cast<int>((k - arpStartK_) % 64) : -1;
    } else {
        const twin::Pattern* P = &ps->pat[curPat_];
        int st = static_cast<int>(((k % P->length) + P->length) % P->length);
        if (st == 0 && pendPat_ >= 0) {
            curPat_ = pendPat_;
            pendPat_ = -1;
            P = &ps->pat[curPat_];
            st = static_cast<int>(((k % P->length) + P->length) % P->length);
        }
        playStep_ = st;
        const twin::Step& S = P->step[st];
        for (int i = 0; i < S.n; ++i) notes[n++] = std::clamp(S.note[i] + transpose_, 0, 127);
        vel = S.vel / 127.0f;
        tie = S.gate >= twin::kGateTie;
        off = onset + (tie ? stepLen : S.gate * 0.01 * stepLen);
        // Motion lanes: this step's values (a smooth lane glides on to the next step's).
        for (int l = 0; l < twin::kLanes; ++l) {
            const twin::Lane& L = P->lane[l];
            Motion& M = motion_[l];
            M.set = L.target >= 0 && L.val[st] != twin::kMotionUnset;
            if (!M.set) continue;
            M.target = L.target;
            M.from = L.val[st] / 65534.0f;
            const uint16_t nx = L.val[(st + 1) % P->length];
            M.to = nx == twin::kMotionUnset ? M.from : nx / 65534.0f;
            M.smooth = L.smooth != 0;
            M.t0 = onset;
            M.len = stepLen;
        }
    }
    // Tied notes that come back are held over; the rest of the last step's notes end here.
    for (int i = 0; i < seqNoteN_;) {
        SeqNote& sn = seqNotes_[i];
        bool again = false;
        for (int j = 0; j < n; ++j)
            if (notes[j] == sn.pitch) again = true;
        if (sn.tie && again) {
            for (int j = 0; j < n; ++j)
                if (notes[j] == sn.pitch) notes[j] = -1; // already sounding
            sn.off = off;
            sn.tie = tie;
            ++i;
        } else if (sn.tie || sn.off <= onset) {
            seqOff(sn.pitch);
            sn = seqNotes_[--seqNoteN_];
        } else
            ++i;
    }
    for (int j = 0; j < n; ++j) {
        if (notes[j] < 0 || seqNoteN_ >= 32) continue;
        seqOn(notes[j], vel);
        seqNotes_[seqNoteN_++] = {notes[j], off, tie};
    }
}



void Twin::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch < 0) {
        gexpr_.set(dim, value); // takes effect at the next control interval, through exAdd
        return;
    }
    if (dim < 0 || dim >= kExprDims) return;
    for (auto& V : voices_) {
        if (V.gate && V.note == pitch && V.pendNote < 0) V.ex.set(dim, value, ctl_[V.part].exAdd);
        if (V.pendNote == pitch) V.pendExpr[dim] = value; // its voice is still fading out
    }
}

void Twin::controller(int32_t ctl, float value) {
    if (ctl == 1) modWheel_ = std::clamp(value, 0.0f, 1.0f);
    else Instrument::controller(ctl, value);
}

void Twin::setTransport(double beatStart, double samplesPerBeat, bool playing) {
    // Whole samples: the sequencer's step times are whole samples too, so a block boundary that
    // lands a hair off one can't move a step by a sample (renders repeat at any block size).
    const double pos = std::round(beatStart * samplesPerBeat);
    const bool jumped = playing && playing_ && std::fabs(pos - (tPos_ + tFrames_)) > 2.0;
    if (jumped || playing != playing_) seqResync_ = true;
    if ((jumped || (playing && !playing_))) {
        bool any = false;
        for (const auto& V : voices_) any |= V.active;
        if (!any) resetAll(); // a silent start or jump re-seeds: renders repeat exactly
        wake();
    }
    for (auto& part : fx_)
        for (auto& d : part) d->setTransport(beatStart, samplesPerBeat, playing);
    beat_ = beatStart;
    spb_ = samplesPerBeat;
    tPos_ = pos;
    tFrames_ = 0.0;
    playing_ = playing;
}

// ---- render ------------------------------------------------------------------------------

// A patch-wide source's value (the same for every voice of the part).
float Twin::globalValue(int part, const twin::PartCtl& c, int src) const {
    using namespace twin;
    // The pad, or a motion lane playing it (FR-063).
    auto pad = [&](int g) { const float o = motionOvr_[part].pad[g - GPadX]; return o >= 0.0f ? o : get(g); };
    auto bip = [&](int g) { return (pad(g) - 0.5f) * 2.0f; };
    switch (src) {
    case SrcLfo1: return glfo_[part][0].val * c.lfoAmt[0];
    case SrcLfo2: return glfo_[part][1].val * c.lfoAmt[1];
    case SrcLfo3: return glfo_[part][2].val * c.lfoAmt[2];
    case SrcModWheel: return modWheel_;
    case SrcPitchWheel: return gexpr_.wheel;
    case SrcPadX: return bip(GPadX);
    case SrcPadY: return bip(GPadY);
    case SrcPadZ: return pad(GPadZ);
    default:
        if (src >= SrcMacro1 && src < SrcMacro1 + kMacros) return get(partIdx(part, PMacro0 + src - SrcMacro1));
        return 0.0f; // the sequencer: not yet
    }
}

// A per-note source's value for one voice.
float Twin::sourceValue(const twin::Voice& V, const twin::PartCtl& c, int src) const {
    using namespace twin;
    auto lfo = [&](int l) { return V.lfo[l].val * V.lfoFadeLvl[l] * c.lfoAmt[l]; };
    switch (src) {
    case SrcLfo1: return lfo(0);
    case SrcLfo2: return lfo(1);
    case SrcLfo3: return lfo(2);
    case SrcFiltEnv: return V.fenv.lvl;
    case SrcAmpEnv: return V.aenv.lvl;
    case SrcModEnv: return V.menv.lvl * (1.0f - c.mVel * (1.0f - V.vel));
    case SrcVelocity: return V.vel;
    case SrcKey: return std::clamp((static_cast<float>(V.note) - 60.0f) / 60.0f, -1.0f, 1.0f);
    case SrcPressure: return V.ex.pressure();
    case SrcSlide: return V.ex.slide();
    case SrcBend: return std::clamp(V.ex.bend() / kRoutePitchSt, -1.0f, 1.0f);
    case SrcRandom: return V.rnd;
    case SrcRelVelocity: return V.relVel;
    default: return 0.0f;
    }
}

namespace {

// A source that is the same for every voice of a part: its routes act on the whole part.
bool globalSource(int src, const twin::PartCtl& c) {
    using namespace twin;
    switch (src) {
    case SrcLfo1: return c.lfoGlobal[0];
    case SrcLfo2: return c.lfoGlobal[1];
    case SrcLfo3: return c.lfoGlobal[2];
    case SrcModWheel:
    case SrcPitchWheel:
    case SrcPadX:
    case SrcPadY:
    case SrcPadZ:
    case SrcSeqGate:
    case SrcSeqStep: return true;
    default: return src >= SrcMacro1 && src < SrcMacro1 + kMacros;
    }
}

} // namespace

// The part's routes from patch-wide sources, once per interval: offsets the part's PartCtl and
// every voice of it start from.
void Twin::partModulation(int part, float morph) {
    using namespace twin;
    const PartCtl& c = ctl_[part];
    float* off = partOff_[part];
    std::fill(off, off + kSideN, 0.0f);
    partMorphOff_[part] = partVib_[part] = partPitch_[part][0] = partPitch_[part][1] = 0.0f;
    bool any = false;
    for (int i = 0; i < routeN_[part]; ++i) {
        const LiveRoute& r = routes_[part][i];
        if (!globalSource(r.src, c)) continue;
        const float val = globalValue(part, c, r.src);
        if (val == 0.0f) continue;
        const float x = r.amt * val;
        const Dst d = dstAt(r.dst);
        any = true;
        switch (d.kind) {
        case DstMorph: partMorphOff_[part] += x; break;
        case DstPitch: partPitch_[part][0] += x * kRoutePitchSt, partPitch_[part][1] += x * kRoutePitchSt; break;
        case DstPitch1: partPitch_[part][0] += x * kRoutePitchSt; break;
        case DstPitch2: partPitch_[part][1] += x * kRoutePitchSt; break;
        case DstVibrato: partVib_[part] += x * kRouteVibratoSt; break;
        case DstSide: off[d.side] += x; break;
        default: break;
        }
    }
    partHasOff_[part] = any;
    (void)morph;
}

// One voice's routes from its own sources (envelopes, LFOs, velocity, key, expression, random):
// when any moves something, the voice evaluates its own values from the part's offsets plus its own.
void Twin::modulateVoice(twin::Voice& V, float morph, const twin::LiveRoute* routes, int n) {
    using namespace twin;
    const PartCtl& pc = ctl_[V.part];
    const PartCtl& cur = V.hasOwn ? V.own : pc; // this voice's LFO rates and fades last interval
    float mo = 0.0f, pitch[2] = {0.0f, 0.0f}, vib = 0.0f;
    bool any = false, sideAny = false;
    for (int i = 0; i < n; ++i) {
        if (globalSource(routes[i].src, pc)) continue; // already in the part's values
        const float v = sourceValue(V, cur, routes[i].src);
        if (v == 0.0f) continue;
        const float x = routes[i].amt * v;
        const Dst d = dstAt(routes[i].dst);
        any = true;
        switch (d.kind) {
        case DstMorph: mo += x; break;
        case DstPitch: pitch[0] += x * kRoutePitchSt, pitch[1] += x * kRoutePitchSt; break;
        case DstPitch1: pitch[0] += x * kRoutePitchSt; break;
        case DstPitch2: pitch[1] += x * kRoutePitchSt; break;
        case DstVibrato: vib += x * kRouteVibratoSt; break;
        case DstSide:
            if (!sideAny) std::copy(partOff_[V.part], partOff_[V.part] + kSideN, modOff_), sideAny = true;
            modOff_[d.side] += x;
            break;
        default: break;
        }
    }
    V.morphOff = mo;
    V.hasOwn = any;
    if (!any) return;
    const int p = V.part;
    evalPart(pn_, p, std::clamp(morph + partMorphOff_[p] + mo, 0.0f, 1.0f), spb_, sr_, V.own,
        sideAny ? modOff_ : (partHasOff_[p] ? partOff_[p] : nullptr), motionOvr_[p].n > 0 ? &motionOvr_[p] : nullptr);
    std::copy(pc.exAdd, pc.exAdd + kExprDims, V.own.exAdd); // expression is the part's, not a side's
    V.own.exCoef = pc.exCoef;
    V.own.semi[0] += partPitch_[p][0] + pitch[0];
    V.own.semi[1] += partPitch_[p][1] + pitch[1];
    V.own.vibExtra = partVib_[p] + vib;
}

// The part's effect values onto its devices (normalized params, smoothed by the devices).
void Twin::pushFx(int part) {
    const twin::PartCtl& c = ctl_[part];
    auto& fx = fx_[part];
    Device* mod = fx[c.modType == 1 ? FxFlanger : c.modType == 2 ? FxPhaser : FxChorus].get();
    if (mod == fx[FxChorus].get()) {
        mod->setParam(Chorus::Mode, c.modType == 3 ? 0.5f : 0.0f); // Classic, or Ensemble
        mod->setParam(Chorus::Rate, c.modRate);
        mod->setParam(Chorus::Amount, c.modDepth);
        mod->setParam(Chorus::Mix, c.modMix);
    } else if (mod == fx[FxFlanger].get()) {
        mod->setParam(Flanger::Rate, c.modRate);
        mod->setParam(Flanger::Depth, c.modDepth);
        mod->setParam(Flanger::Mix, c.modMix);
    } else {
        mod->setParam(Phaser::Rate, c.modRate);
        mod->setParam(Phaser::Depth, c.modDepth);
        mod->setParam(Phaser::Mix, c.modMix);
    }
    Device* dl = fx[FxDelay].get();
    dl->setParam(Delay::SyncMode, c.dlySync ? 1.0f : 0.0f);
    dl->setParam(Delay::TimeL, c.dlyTime);
    dl->setParam(Delay::TimeR, c.dlyTime);
    dl->setParam(Delay::DivL, c.dlyDiv);
    dl->setParam(Delay::DivR, c.dlyDiv);
    dl->setParam(Delay::Feedback, c.dlyFb);
    dl->setParam(Delay::PingPong, c.dlyPing ? 1.0f : 0.0f);
    dl->setParam(Delay::DryWet, c.dlyMix);
    Device* rv = fx[FxReverb].get();
    rv->setParam(Reverb::Size, c.revSize);
    rv->setParam(Reverb::Decay, 0.15f + 0.8f * c.revSize);
    rv->setParam(Reverb::HFDamp, c.revDamp);
    rv->setParam(Reverb::DryWet, c.revMix);
}

void Twin::updateControl() {
    using namespace twin;
    ctlInit_ = true;
    static constexpr float kBendRanges[4] = {2.0f, 5.0f, 12.0f, 24.0f};
    const float bendRange = kBendRanges[stepOf(get(GBendRange), 4)];
    const int layout = layoutMode();
    const int parts = layout == 0 ? 1 : 2;
    const int recLane = recLane_.load(std::memory_order_relaxed);
    const double now = tPos_ + tFrames_;
    for (int part = 0; part < parts; ++part) {
        // The sequencer's motion lanes (paused on a lane that is recording): this interval's values.
        MotionOverride& mo = motionOvr_[part];
        mo = MotionOverride{};
        if (seqRunning_)
            for (int l = 0; l < kLanes; ++l) {
                const Motion& M = motion_[l];
                if (!M.set || l == recLane) continue;
                const float t = M.smooth ? static_cast<float>(std::clamp((now - M.t0) / M.len, 0.0, 1.0)) : 0.0f;
                const float v = M.from + (M.to - M.from) * t;
                if (M.target == partIdx(part, PMorph)) mo.morph = v;
                if (M.target >= GPadX && M.target <= GPadZ) mo.pad[M.target - GPadX] = v;
                for (int sd = 0; sd < kSides; ++sd) {
                    const int s0 = sideIdx(part, sd, 0);
                    if (M.target >= s0 && M.target < s0 + kSideN && mo.n < 4) {
                        mo.side[mo.n] = sd;
                        mo.param[mo.n] = M.target - s0;
                        mo.v[mo.n++] = v;
                    }
                }
            }
        const MotionOverride* ovr = mo.n > 0 ? &mo : nullptr;
        const float morph = mo.morph >= 0.0f ? mo.morph : partMorph(pn_, part);
        morphNow_[part] = morph;
        PartCtl& c = ctl_[part];
        evalPart(pn_, part, morph, spb_, sr_, c, nullptr, ovr);
        routeN_[part] = liveRoutes(pn_, part, morph, routes_[part]);
        partModulation(part, morph);
        if (partHasOff_[part]) {
            evalPart(pn_, part, std::clamp(morph + partMorphOff_[part], 0.0f, 1.0f), spb_, sr_, c, partOff_[part], ovr);
            c.semi[0] += partPitch_[part][0];
            c.semi[1] += partPitch_[part][1];
            c.vibExtra = partVib_[part];
        }
        gexpr_.offsets(bendRange, c.exAdd);
        c.exCoef = exprSmoothCoef(sr_);
        if (c.fxOn) pushFx(part);
    }
    // Structural changes release (never cut) what no longer fits: a new layout, voice share or
    // cap releases everything; a part's new voice mode releases that part.
    const int cap = voiceCap();
    const int shape = layout * 1000 + stepOf(get(GShare), 5) * 100 + cap;
    if (lastLayout_ >= 0 && (shape != lastLayout_ || cap < lastCap_)) {
        for (auto& V : voices_)
            if (V.gate) release(V);
        heldN_[0] = heldN_[1] = 0;
    }
    // Back to Single: the Upper part isn't rendered any more, so its voices end here.
    if (parts == 1)
        for (auto& V : voices_)
            if (V.active && V.part != 0) {
                V.active = V.gate = false;
                V.pendNote = -1;
                V.part = 0;
            }
    for (int part = 0; part < kParts; ++part) {
        const int vm = ctl_[part].voiceMode;
        if (part < parts && lastMode_[part] >= 0 && vm != lastMode_[part]) {
            for (auto& V : voices_)
                if (V.gate && V.part == part) release(V);
            heldN_[part] = 0;
        }
        lastMode_[part] = vm;
    }
    lastLayout_ = shape;
    lastCap_ = cap;
    // The LFOs that only feed the matrix move once per interval (LFO 1 runs per sample, for vibrato).
    for (int part = 0; part < parts; ++part)
        for (int l = 1; l < 3; ++l)
            if (ctl_[part].lfoGlobal[l]) glfo_[part][l].step(ctl_[part].lfoHz[l] / sr_ * kCtl, ctl_[part].lfoShape[l]);
    for (auto& V : voices_) {
        if (!V.active) continue;
        const PartCtl& vc = V.hasOwn ? V.own : ctl_[V.part];
        for (int l = 1; l < 3; ++l) {
            if (!vc.lfoGlobal[l]) V.lfo[l].step(vc.lfoHz[l] / sr_ * kCtl, vc.lfoShape[l]);
            if (V.lfoFadeLvl[l] < 1.0f) V.lfoFadeLvl[l] = std::min(1.0f, V.lfoFadeLvl[l] + V.lfoFadeInc[l] * kCtl);
        }
        modulateVoice(V, morphNow_[V.part], routes_[V.part], routeN_[V.part]);
        voiceControl(V, V.hasOwn ? V.own : ctl_[V.part], sr_);
    }
    // LFO 1 in global mode: this interval's values, per part.
    for (int part = 0; part < parts; ++part)
        if (ctl_[part].lfoGlobal[0]) {
            Lfo& L = glfo_[part][0];
            const double inc = ctl_[part].lfoHz[0] / sr_;
            for (int i = 0; i < kCtl; ++i) gLfo1_[part][i] = L.step(inc, ctl_[part].lfoShape[0]);
        }
}

void Twin::renderSegment(float* out, int n) {
    const int os = os_;
    int active = 0;
    float pk = blockPeak_;
    const int parts = layoutMode() == 0 ? 1 : 2;
    for (int part = 0; part < parts; ++part) {
        const float* lfo1 = gLfo1_[part] + (twin::kCtl - ctlLeft_);
        std::fill(osL_, osL_ + n * os, 0.0f);
        std::fill(osR_, osR_ + n * os, 0.0f);
        bool sounding = false;
        for (auto& V : voices_) {
            if (!V.active || V.part != part) continue;
            int k = 0;
            while (k < n) {
                k = twin::renderVoice(V, V.hasOwn ? V.own : ctl_[V.part], k, n, os, sr_, modWheel_, fadeLen_, lfo1, osL_, osR_);
                if (k >= n) break;
                if (V.active && V.fade == 0 && V.pendNote >= 0) startPending(V);
                else break;
            }
            sounding = true;
            active += V.active ? 1 : 0;
        }
        if (os == 2) {
            dn_[part][0].process_block(dsL_, osL_, n);
            dn_[part][1].process_block(dsR_, osR_, n);
        } else {
            std::copy(osL_, osL_ + n, dsL_);
            std::copy(osR_, osR_ + n, dsR_);
        }
        for (int i = 0; i < n; ++i) {
            fxBuf_[2 * i] = dsL_[i] * kOutGain;
            fxBuf_[2 * i + 1] = dsR_[i] * kOutGain;
        }
        // The part's effects: Mod FX ▸ Delay ▸ Reverb. One switched on starts from silence.
        const twin::PartCtl& c = ctl_[part];
        const bool on[kFxN] = {c.fxOn && c.modOn && (c.modType == 0 || c.modType == 3), c.fxOn && c.modOn && c.modType == 1,
            c.fxOn && c.modOn && c.modType == 2, c.fxOn && c.dlyOn, c.fxOn && c.revOn};
        for (int f = 0; f < kFxN; ++f) {
            if (!on[f]) {
                fxWasOn_[part][f] = false;
                continue;
            }
            if (!fxWasOn_[part][f]) {
                static constexpr int kClear[kFxN] = {Chorus::A_Clear, Flanger::A_Clear, Phaser::A_Clear, Delay::A_ClearLoop, Reverb::A_KillTail};
                fx_[part][f]->deviceAction(kClear[f], 0, 0.0f);
                fx_[part][f]->wake();
                fxWasOn_[part][f] = true;
            }
            fx_[part][f]->process(fxBuf_, n);
        }
        (void)sounding;
        for (int i = 0; i < n; ++i) {
            out[2 * i] += fxBuf_[2 * i];
            out[2 * i + 1] += fxBuf_[2 * i + 1];
            pk = std::max(pk, std::max(std::fabs(fxBuf_[2 * i]), std::fabs(fxBuf_[2 * i + 1])));
        }
    }
    blockPeak_ = pk;
    activeVoices_.store(active, std::memory_order_relaxed);
}

// Picks up a new device rate; voices keep their notes, only rate-derived state restarts.
void Twin::applyRate() {
    const double sr = sampleRate_.load(std::memory_order_relaxed);
    if (sr == sr_) return;
    sr_ = sr;
    os_ = sr_ >= 88000.0 ? 1 : 2;
    fadeLen_ = std::max(1, static_cast<int>(0.004 * sr_));
    static const double kSteep[12] = {0.017347915108876406, 0.067150480426919179, 0.14330738338179819,
        0.23745131944299824, 0.34085550201503761, 0.44601111310335906, 0.54753112652956148, 0.6423859124721446,
        0.72968928615804163, 0.81029959388029904, 0.88644514917318362, 0.96150605146543733};
    for (auto& part : dn_)
        for (auto& d : part) {
            d.set_coefs(kSteep);
            d.clear_buffers();
        }
    ctlInit_ = false;
    ctlLeft_ = 0;
}

void Twin::render(float* out, int32_t frames) {
    SnapshotHistory<twin::PatternSet>::RenderScope patScope(pats_);
    dsp::ScopedNoDenormals ftz;
    applyRate();
    // Asleep — no voice, a quiet hangover, no sequencer that could play — so nothing to add
    // until a note, a pattern edit or a transport start wakes it (SC-006).
    if (idle_ && !seqActive()) {
        tFrames_ += frames;
        return;
    }
    for (int32_t done = 0; done < frames;) {
        if (ctlLeft_ == 0) {
            updateControl();
            ctlLeft_ = twin::kCtl;
        }
        const int toEvent = seqService(done); // fires due steps, says when the next one is
        const int n = std::min<int>({ctlLeft_, frames - done, toEvent});
        renderSegment(out + done * 2, n);
        ctlLeft_ -= n;
        done += n;
    }
    tFrames_ += frames;
    if (activeVoices_.load(std::memory_order_relaxed) == 0 && blockPeak_ <= 1.0e-5f && !seqRunning_) quiet_ += static_cast<uint64_t>(frames);
    else quiet_ = 0;
    // A delay holds its echoes in silence: while one is on, wait out its longest time too.
    bool delayOn = false;
    for (const auto& part : fxWasOn_) delayOn |= part[FxDelay];
    const uint64_t hang = kIdleHangover + (delayOn ? static_cast<uint64_t>(sampleRate_.load(std::memory_order_relaxed) * kDelayHoldSec) : 0);
    idle_ = quiet_ > hang;
    publishTelemetry(blockPeak_);
    blockPeak_ = 0.0f;
    heldPub_[0].store(heldBits_[0], std::memory_order_relaxed);
    heldPub_[1].store(heldBits_[1], std::memory_order_relaxed);
}

void Twin::publishTelemetry(float peak) {
    auto set = [&](int i, float v) { tele_[i].store(v, std::memory_order_relaxed); };
    set(ScStep, seqRunning_ ? static_cast<float>(playStep_) : -1.0f);
    set(ScPattern, static_cast<float>(curPat_));
    set(ScPending, static_cast<float>(pendPat_));
    set(ScRevision, static_cast<float>(rev_.load(std::memory_order_relaxed)));
    int lo = 0, up = 0;
    const twin::Voice* newest = nullptr;
    for (int v = 0; v < kMaxVoices; ++v) {
        const twin::Voice& V = voices_[v];
        if (V.active) {
            (V.part == 0 ? lo : up)++;
            if (!newest || V.order > newest->order) newest = &V;
        }
        const float base = std::clamp(twin::partMorph(pn_, V.part) + partMorphOff_[V.part], 0.0f, 1.0f);
        set(ScVoiceMorph0 + v, V.active ? std::clamp(base + V.morphOff, 0.0f, 1.0f) : -1.0f);
    }
    set(ScVoicesLo, static_cast<float>(lo));
    set(ScVoicesUp, static_cast<float>(up));
    for (int p = 0; p < twin::kParts; ++p)
        set(ScMorphLo + p, std::clamp(twin::partMorph(pn_, p) + partMorphOff_[p], 0.0f, 1.0f));
    // An LFO's phase: the shared one in global mode, else the newest voice's.
    for (int l = 0; l < 3; ++l) {
        const bool global = ctl_[0].lfoGlobal[l];
        const double ph = global ? glfo_[0][l].ph : newest ? newest->lfo[l].ph : 0.0;
        set(ScLfoPhase0 + l, static_cast<float>(ph));
    }
    set(ScPeakDb, peak > 1e-9f ? 20.0f * std::log10(peak) : -180.0f);
}

int32_t Twin::scopeRead(float* out, int32_t maxN) const {
    const int32_t n = std::min<int32_t>(maxN, kScopeN);
    for (int32_t i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
    return n;
}

int32_t Twin::heldNotes(int32_t* out, int32_t maxN) const {
    int32_t n = 0;
    for (int w = 0; w < 2; ++w) {
        const uint64_t bits = heldPub_[w].load(std::memory_order_relaxed);
        for (int b = 0; b < 64 && n < maxN; ++b)
            if (bits & (uint64_t{1} << b)) out[n++] = w * 64 + b;
    }
    return n;
}

// ---- project state ----------------------------------------------------------------------

std::vector<uint8_t> Twin::getState() const {
    std::vector<uint8_t> b;
    b.reserve(12 + kNumParams * sizeof(float));
    putU32(b, kStateMagic);
    putU32(b, static_cast<uint32_t>(kNumParams));
    for (int i = 0; i < kNumParams; ++i) {
        const float v = pn_[i].load(std::memory_order_relaxed);
        uint32_t u;
        std::memcpy(&u, &v, sizeof u);
        putU32(b, u);
    }
    const auto pat = twin::serializePatterns(*patAuth_);
    putU32(b, static_cast<uint32_t>(pat.size()));
    b.insert(b.end(), pat.begin(), pat.end());
    return b;
}

void Twin::setState(const uint8_t* data, int32_t size) {
    if (!data || size < 8 || getU32(data) != kStateMagic) return;
    const uint32_t count = getU32(data + 4);
    const size_t avail = (static_cast<size_t>(size) - 8) / sizeof(float);
    const int n = static_cast<int>(std::min<size_t>({count, avail, static_cast<size_t>(kNumParams)}));
    for (int i = 0; i < n; ++i) {
        const uint32_t u = getU32(data + 8 + i * sizeof(float));
        float v;
        std::memcpy(&v, &u, sizeof v);
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    // The pattern block after the params (a preset may carry only it: paramCount 0).
    const size_t at = 8 + static_cast<size_t>(count) * sizeof(float);
    if (static_cast<size_t>(size) >= at + 4) {
        const uint32_t bytes = getU32(data + at);
        if (static_cast<size_t>(size) >= at + 4 + bytes) setEditState(data + at + 4, static_cast<int32_t>(bytes));
    }
}

std::vector<uint8_t> Twin::editState() const {
    return twin::serializePatterns(*patAuth_);
}

void Twin::setEditState(const uint8_t* data, int32_t size) {
    auto next = std::make_shared<twin::PatternSet>();
    if (!twin::deserializePatterns(data, size, *next)) return;
    patAuth_ = next;
    publishPatterns();
}

void Twin::publishPatterns() {
    pats_.publish(std::make_shared<twin::PatternSet>(*patAuth_));
    rev_.fetch_add(1, std::memory_order_relaxed);
    wake();
}

// ---- the sequencer's editing channel (message thread) -------------------------------------
void Twin::action(int32_t id, int32_t iarg, float farg) {
    using namespace twin;
    Pattern& P = editPattern();
    auto stepOk = [&](int st) { return st >= 0 && st < kSteps; };
    bool changed = true;
    switch (id) {
    case A_SelectPattern:
        if (iarg >= 0 && iarg < kPatterns) editPat_ = iarg;
        changed = false;
        break;
    case A_SetLength: P.length = static_cast<uint8_t>(std::clamp(iarg, 1, kSteps)); break;
    case A_ClearStep:
        if (stepOk(iarg)) P.step[iarg].n = 0;
        break;
    case A_AddStepNote: {
        const int st = iarg >> 8, note = iarg & 0xFF;
        if (!stepOk(st) || note > 127) break;
        Step& S = P.step[st];
        bool has = false;
        for (int i = 0; i < S.n; ++i) has |= S.note[i] == note;
        if (!has && S.n < kStepNotes) S.note[S.n++] = static_cast<uint8_t>(note);
        if (farg > 0.0f) S.vel = static_cast<uint8_t>(std::clamp<int>(static_cast<int>(std::lround(farg * 127.0f)), 1, 127));
        break;
    }
    case A_RemoveStepNote: {
        const int st = iarg >> 8, note = iarg & 0xFF;
        if (!stepOk(st)) break;
        Step& S = P.step[st];
        int w = 0;
        for (int i = 0; i < S.n; ++i)
            if (S.note[i] != note) S.note[w++] = S.note[i];
        S.n = static_cast<uint8_t>(w);
        break;
    }
    case A_SetStepVelocity:
        if (stepOk(iarg)) P.step[iarg].vel = static_cast<uint8_t>(std::clamp<int>(static_cast<int>(std::lround(farg * 127.0f)), 1, 127));
        break;
    case A_SetStepGate:
        if (stepOk(iarg))
            P.step[iarg].gate = farg > 1.0f ? kGateTie : static_cast<uint8_t>(std::clamp<int>(static_cast<int>(std::lround(farg * 100.0f)), 1, 100));
        break;
    case A_StepRecordArm:
        recArmed_.store(iarg != 0, std::memory_order_relaxed);
        recStep_ = 0, recChordN_ = 0, recDown_[0] = recDown_[1] = 0;
        ringTail_.store(ringHead_.load(std::memory_order_acquire), std::memory_order_release);
        changed = false;
        break;
    case A_StepRecordAdvance:
        P.step[recStep_].n = 0;
        recStep_ = (recStep_ + 1) % P.length;
        break;
    case A_ClearPattern:
        if (iarg >= 0 && iarg < kPatterns) patAuth_->pat[iarg] = Pattern{};
        break;
    case A_CopyPattern: {
        const int from = iarg >> 8, to = iarg & 0xFF;
        if (from >= 0 && from < kPatterns && to >= 0 && to < kPatterns && from != to) patAuth_->pat[to] = patAuth_->pat[from];
        break;
    }
    case A_MotionTarget: {
        const int lane = iarg >> 16, param = iarg & 0xFFFF;
        if (lane >= 0 && lane < kLanes) P.lane[lane].target = param == 0xFFFF || param >= kNumParams ? int16_t{-1} : static_cast<int16_t>(param);
        break;
    }
    case A_MotionMode:
        if (iarg >= 0 && iarg < kLanes) P.lane[iarg].smooth = farg >= 0.5f ? 1 : 0;
        break;
    case A_MotionSet: {
        const int lane = iarg >> 8, st = iarg & 0xFF;
        if (lane >= 0 && lane < kLanes && stepOk(st))
            P.lane[lane].val[st] = farg < 0.0f ? kMotionUnset : static_cast<uint16_t>(std::lround(std::clamp(farg, 0.0f, 1.0f) * 65534.0f));
        break;
    }
    case A_MotionRecordArm:
        recLane_.store(iarg >= 0 && iarg < kLanes ? iarg : -1, std::memory_order_relaxed);
        changed = false;
        break;
    case A_ClearMotion:
        if (iarg >= 0 && iarg < kLanes) P.lane[iarg] = Lane{};
        break;
    case A_StepRecordPoll: {
        // A chord is the notes held together; it lands on the record step when the last one lifts.
        changed = false;
        const uint32_t head = ringHead_.load(std::memory_order_acquire);
        uint32_t tail = ringTail_.load(std::memory_order_relaxed);
        for (; tail != head; ++tail) {
            const uint32_t e = ring_[tail % kRing].load(std::memory_order_relaxed);
            const int pitch = static_cast<int>((e >> 8) & 0x7F);
            const uint64_t bit = uint64_t{1} << (pitch & 63);
            if (e & (1u << 16)) {
                recDown_[pitch >> 6] &= ~bit;
                if ((recDown_[0] | recDown_[1]) == 0 && recChordN_ > 0) {
                    Step& S = P.step[recStep_];
                    S.n = static_cast<uint8_t>(recChordN_);
                    std::copy(recChord_, recChord_ + recChordN_, S.note);
                    S.vel = static_cast<uint8_t>(std::clamp(recVel_, 1, 127));
                    recStep_ = (recStep_ + 1) % P.length;
                    recChordN_ = recVel_ = 0;
                    changed = true;
                }
            } else {
                recDown_[pitch >> 6] |= bit;
                bool has = false;
                for (int i = 0; i < recChordN_; ++i) has |= recChord_[i] == pitch;
                if (!has && recChordN_ < kStepNotes) recChord_[recChordN_++] = static_cast<uint8_t>(pitch);
                recVel_ = std::max(recVel_, static_cast<int>(e & 0x7F));
            }
        }
        ringTail_.store(tail, std::memory_order_release);
        break;
    }
    default: changed = false; break;
    }
    // The record cursor is message-thread state: published here, not by the render.
    tele_[ScRecStep].store(recArmed_.load(std::memory_order_relaxed) ? static_cast<float>(recStep_) : -1.0f, std::memory_order_relaxed);
    if (changed) publishPatterns();
}

std::shared_ptr<Instrument> Twin::clone() const {
    auto s = std::make_shared<Twin>();
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->patAuth_ = std::make_shared<twin::PatternSet>(*patAuth_);
    s->publishPatterns();
    s->setSampleRate(sampleRate_.load(std::memory_order_relaxed));
    return s;
}

} // namespace nota
