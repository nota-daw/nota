// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — morph evaluation and the param → PartCtl mapping (see TwinMod.h).

#include "devices/instruments/TwinMod.h"

#include <algorithm>
#include <cmath>
#include <string>

namespace nota {
namespace twin {

namespace {

// Which side params are stepped (they switch at the morph midpoint), from the param table.
struct SteppedTable {
    bool stepped[kSideN];
    SteppedTable() {
        const auto& t = paramTable();
        for (int s = 0; s < kSideN; ++s) stepped[s] = t[static_cast<size_t>(sideIdx(0, 0, s))].steps > 1;
    }
};
const SteppedTable& steppedTable() {
    static const SteppedTable t;
    return t;
}

} // namespace

float partMorph(const std::atomic<float>* pn, int part) {
    return pn[partIdx(part, PMorph)].load(std::memory_order_relaxed);
}

namespace {

const char* const kSrcNames[kSrcN] = {"None", "LFO 1", "LFO 2", "LFO 3", "Filter env", "Amp env", "Mod env",
    "Velocity", "Release velocity", "Key", "Pressure", "Slide", "Bend", "Mod wheel", "Pitch wheel", "Pad X",
    "Pad Y", "Pad Z", "Random", "Macro 1", "Macro 2", "Macro 3", "Macro 4", "Macro 5", "Macro 6", "Macro 7",
    "Macro 8", "Seq gate", "Seq step"};

// The continuous side params a route may reach, in persisted order — APPEND ONLY.
constexpr int kSideDsts[] = {O1Wave, O1Pw, O1Metal, O1Fine, O1Level, O1Bal, O2Wave, O2Pw, O2Metal, O2Fine, O2Level,
    O2Bal, Fm21, FmFilt, NoiseLevel, NoiseColor, NoiseBal, MmCut, MmRes, MmMode, MmKey, MmEnv, LdCut, LdRes, LdKey, LdEnv,
    Route, Brute, Drive, FEnvA, FEnvD, FEnvS, FEnvR, FEnvVel, FEnvCurve, AEnvA, AEnvD, AEnvS, AEnvR, AEnvVel,
    AEnvCurve, MEnvDly, MEnvA, MEnvD, MEnvS, MEnvR, MEnvVel, Lfo1Rate, Lfo1Fade, Lfo1Amt, Lfo2Rate, Lfo2Fade, Lfo2Amt,
    Lfo3Rate, Lfo3Fade, Lfo3Amt, Lfo3Sym, Lfo3Curve, VibAmt, Drift, UniDetune, Spread, ModFxRate, ModFxDepth,
    ModFxMix, DlyTime, DlyFb, DlyMix, RevSize, RevDamp, RevMix};
constexpr int kVirtualDsts = 6; // None, Morph, Pitch, Osc 1 pitch, Osc 2 pitch, Vibrato
constexpr int kSideDstN = sizeof(kSideDsts) / sizeof(kSideDsts[0]);
static_assert(kVirtualDsts + kSideDstN <= kDstScale + 1, "destinations outgrew their encoding");

} // namespace

const char* srcName(int s) { return s >= 0 && s < kSrcN ? kSrcNames[s] : ""; }

int dstCount() { return kVirtualDsts + kSideDstN; }

Dst dstAt(int i) {
    switch (i) {
    case 1: return {DstMorph, -1};
    case 2: return {DstPitch, -1};
    case 3: return {DstPitch1, -1};
    case 4: return {DstPitch2, -1};
    case 5: return {DstVibrato, -1};
    default:
        if (i >= kVirtualDsts && i < dstCount()) return {DstSide, kSideDsts[i - kVirtualDsts]};
        return {DstNone, -1};
    }
}

const char* dstName(int i) {
    static const char* const kVirtual[kVirtualDsts] = {"None", "Morph", "Pitch", "Osc 1 pitch", "Osc 2 pitch", "Vibrato"};
    if (i >= 0 && i < kVirtualDsts) return kVirtual[i];
    if (i >= dstCount()) return "";
    // The side param's display name after the "Lower A · " prefix: "Ladder Cutoff".
    const std::string& n = paramTable()[static_cast<size_t>(sideIdx(0, 0, kSideDsts[i - kVirtualDsts]))].name;
    const size_t dot = n.find(" · ");
    return dot == std::string::npos ? n.c_str() : n.c_str() + dot + 4;
}

int dstIndexOfSide(int side) {
    for (int i = 0; i < kSideDstN; ++i)
        if (kSideDsts[i] == side) return kVirtualDsts + i;
    return 0;
}

int liveRoutes(const std::atomic<float>* pn, int part, float morph, LiveRoute* out) {
    int n = 0;
    for (int r = 0; r < kRoutes; ++r) {
        const int src = stepOf(pn[partIdx(part, PRoute0 + 2 * r)].load(std::memory_order_relaxed), kSrcScale + 1);
        const int dst = stepOf(pn[partIdx(part, PRoute0 + 2 * r + 1)].load(std::memory_order_relaxed), kDstScale + 1);
        if (src <= SrcNone || src >= kSrcN || dst <= 0 || dst >= dstCount()) continue;
        const float a = pn[sideIdx(part, 0, MatAmt0 + r)].load(std::memory_order_relaxed);
        const float b = pn[sideIdx(part, 1, MatAmt0 + r)].load(std::memory_order_relaxed);
        const float amt = ((morph <= 0.0f ? a : morph >= 1.0f ? b : a + (b - a) * morph) - 0.5f) * 2.0f;
        if (amt == 0.0f) continue;
        out[n++] = {src, dst, amt};
    }
    return n;
}

void evalPart(const std::atomic<float>* pn, int part, float morph, double spb, double sr, PartCtl& c, const float* off,
    const MotionOverride* ovr) {
    const auto& st = steppedTable();
    const int a0 = sideIdx(part, 0, 0), b0 = sideIdx(part, 1, 0);
    // A motion lane sets a side's value outright, before the morph blends the sides.
    auto side = [&](int sd, int s) {
        if (ovr)
            for (int i = 0; i < ovr->n; ++i)
                if (ovr->param[i] == s && ovr->side[i] == sd) return ovr->v[i];
        return pn[(sd == 0 ? a0 : b0) + s].load(std::memory_order_relaxed);
    };
    auto S = [&](int s) {
        const float a = side(0, s);
        float v;
        if (morph <= 0.0f) v = a;
        else {
            const float b = side(1, s);
            v = morph >= 1.0f ? b : st.stepped[s] ? (morph < 0.5f ? a : b) : a + (b - a) * morph;
        }
        return off && off[s] != 0.0f ? std::clamp(v + off[s], 0.0f, 1.0f) : v;
    };
    auto P = [&](int p) { return pn[partIdx(part, p)].load(std::memory_order_relaxed); };

    for (int k = 0; k < 2; ++k) {
        const int o = k * (O2Wave - O1Wave);
        c.semi[k] = static_cast<float>(coarseSemis(S(O1Coarse + o))) + fineSemis(S(O1Fine + o));
        c.wave[k] = S(O1Wave + o);
        c.duty[k] = dutyOf(S(O1Pw + o));
        c.metal[k] = S(O1Metal + o);
        c.lvl[k] = S(O1Level + o);
        c.bal[k] = S(O1Bal + o);
    }
    c.sync = S(Sync) >= 0.5f;
    c.o2lo = S(O2Lo) >= 0.5f;
    c.fm21 = 4.0f * S(Fm21) * S(Fm21);
    c.fmFilt = 6.0f * S(FmFilt) * S(FmFilt);
    c.noiseLvl = S(NoiseLevel);
    c.noiseCol = S(NoiseColor);
    c.noiseBal = S(NoiseBal);

    c.mmCut = cutoffOct(S(MmCut));
    c.mmRes = S(MmRes);
    c.mmMode = S(MmMode);
    c.mmKey = S(MmKey);
    c.mmEnv = envAmtOct(S(MmEnv));
    c.ldCut = cutoffOct(S(LdCut));
    c.ldRes = S(LdRes);
    c.ld12 = S(LdSlope) < 0.5f;
    c.ldKey = S(LdKey);
    c.ldEnv = envAmtOct(S(LdEnv));
    c.route = S(Route);
    c.brute = S(Brute);
    c.drive = S(Drive);

    c.fA = attackSec(S(FEnvA)), c.fD = decaySec(S(FEnvD)), c.fS = S(FEnvS), c.fR = decaySec(S(FEnvR));
    c.fVel = S(FEnvVel), c.fCurve = S(FEnvCurve);
    c.aA = attackSec(S(AEnvA)), c.aD = decaySec(S(AEnvD)), c.aS = S(AEnvS), c.aR = decaySec(S(AEnvR));
    c.aVel = S(AEnvVel), c.aCurve = S(AEnvCurve);
    c.mDly = delaySec(S(MEnvDly));
    c.mA = attackSec(S(MEnvA)), c.mD = decaySec(S(MEnvD)), c.mS = S(MEnvS), c.mR = decaySec(S(MEnvR));
    c.mVel = S(MEnvVel);
    c.mLoop = S(MEnvLoop) >= 0.5f;

    for (int l = 0; l < 3; ++l) {
        const int o = l * (Lfo2Shape - Lfo1Shape);
        c.lfoShape[l] = stepOf(S(Lfo1Shape + o), 6);
        const bool sync = S(Lfo1Sync + o) >= 0.5f && spb > 1.0;
        c.lfoHz[l] = sync ? static_cast<float>(sr / (spb * syncBeats(S(Lfo1Div + o)))) : lfoHz(S(Lfo1Rate + o));
        c.lfoGlobal[l] = S(Lfo1Mode + o) >= 0.5f;
        c.lfoRetrig[l] = S(Lfo1Retrig + o) >= 0.5f;
        c.lfoFade[l] = fadeSec(S(Lfo1Fade + o));
        c.lfoAmt[l] = S(Lfo1Amt + o);
    }
    c.vibSemis = S(VibAmt);
    c.drift = S(Drift);
    c.uniDetune = S(UniDetune);
    c.spread = S(Spread);
    c.spreadMode = stepOf(S(SpreadMode), 3);

    c.fxOn = P(PFxOn) >= 0.5f;
    c.modOn = S(ModFxOn) >= 0.5f, c.dlyOn = S(DlyOn) >= 0.5f, c.revOn = S(RevOn) >= 0.5f;
    c.modType = stepOf(S(ModFxType), 4);
    c.modRate = S(ModFxRate), c.modDepth = S(ModFxDepth), c.modMix = S(ModFxMix);
    c.dlyTime = S(DlyTime), c.dlyDiv = S(DlyDiv), c.dlyFb = S(DlyFb), c.dlyMix = S(DlyMix);
    c.dlySync = S(DlySync) >= 0.5f, c.dlyPing = S(DlyPingPong) >= 0.5f;
    c.revSize = S(RevSize), c.revDamp = S(RevDamp), c.revMix = S(RevMix);

    c.voiceMode = stepOf(P(PVoiceMode), 4);
    c.glideSec = glideSec(P(PGlide));
    c.glideLegato = P(PGlideLegato) >= 0.5f;
    c.octave = stepOf(P(POctave), 5) - 2;
    c.gain = partGain(P(PVol));
    c.pan = (P(PPan) - 0.5f) * 2.0f;
}

} // namespace twin
} // namespace nota
