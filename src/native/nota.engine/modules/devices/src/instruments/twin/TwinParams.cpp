// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — the param table, project state and the instrument shell (see Twin.h).
// The three descriptor tables below ARE the persisted layout: their order matches the enums
// in Twin.h and only ever grows at the end.

#include "instruments/twin/Twin.h"
#include "instruments/twin/TwinMod.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

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

// The envelope shapes per side (#531), after the routing depths: straight curves and Exp on —
// the RC generator every Twin envelope has always been.
constexpr Desc kShapeDesc[] = {
    {"fenvdcurve", "FilterEnv Decay Curve", 0.5f, 0, D::Linear},
    {"fenvrcurve", "FilterEnv Release Curve", 0.5f, 0, D::Linear},
    {"fenvexp", "FilterEnv Exp", 1.0f, 2, D::Linear},
    {"aenvdcurve", "AmpEnv Decay Curve", 0.5f, 0, D::Linear},
    {"aenvrcurve", "AmpEnv Release Curve", 0.5f, 0, D::Linear},
    {"aenvexp", "AmpEnv Exp", 1.0f, 2, D::Linear},
    {"menvdcurve", "ModEnv Decay Curve", 0.5f, 0, D::Linear},
    {"menvrcurve", "ModEnv Release Curve", 0.5f, 0, D::Linear},
    {"menvexp", "ModEnv Exp", 1.0f, 2, D::Linear},
};
static_assert(sizeof(kShapeDesc) / sizeof(kShapeDesc[0]) == kSideN - kSideNv1, "shape table out of step with Twin.h");

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
            for (const Desc& d : kShapeDesc) t.push_back(make(sid, sname, d));
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
    return valueText(i, pn_[i].load(std::memory_order_relaxed));
}

int32_t Twin::pluginParamSteps(int32_t i) const {
    if (i < 0 || i >= kNumParams) return 0;
    for (int p = 0; p < twin::kParts; ++p) // a matrix slot names a source / destination, not a level
        if (const int r = i - twin::partIdx(p, twin::PRoute0); r >= 0 && r < 2 * twin::kRoutes) return 0;
    const int n = twin::paramTable()[static_cast<size_t>(i)].steps;
    return n > 1 ? n : 0;
}

std::string Twin::pluginParamStepText(int32_t i, int32_t step) const {
    const int n = pluginParamSteps(i);
    if (n < 2 || step < 0 || step >= n) return {};
    return valueText(i, static_cast<float>(step) / static_cast<float>(n - 1));
}

std::string Twin::valueText(int32_t i, float v) const {
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
    else
        std::snprintf(buf, sizeof buf, "%.0f%%", v * 100.0f);
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

} // namespace nota
