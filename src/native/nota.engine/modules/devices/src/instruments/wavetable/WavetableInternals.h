// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// WavetableInternals — shared block scratch & parameter snapshot layouts across WavetableSynth TUs.

#pragma once

#include "WavetableSynth.h"
#include <cstdint>
#include "nota/dsp/poly/PolyVoiceScheduler.h"
#include "common/EnvShape.h"
#include "common/DeviceMath.h"

namespace nota {

struct WavetableSynth::BatchWork {
    dsp::PolyVoiceScratchBank voiceScratch_{kModChunk};
    double lfo1[kModChunk] = {}, lfo2[kModChunk] = {};
    double f1[kModChunk] = {}, f2[kModChunk] = {}, r1[kModChunk] = {}, r2[kModChunk] = {};
    float mixL[kModChunk] = {}, mixR[kModChunk] = {};
};

struct WavetableSynth::BlockScalars {
    double mPitch = 0.0, mOsc2Pitch = 0.0, mPos = 0.0, mCut = 0.0, mReso = 0.0, mLevel = 0.0, mPan = 0.0;
    int b1 = 0, b2 = 0;
    const AuroraTable* ct1 = nullptr;
    const AuroraTable* ct2 = nullptr;
    int wm1 = 0, wm2 = 0;
    float basePos1 = 0.0f, basePos2 = 0.0f;
    double warp1 = 0.0, warp2 = 0.0;
    float lvl1 = 0.0f, lvl2 = 0.0f, subLvl = 0.0f;
    int subWave = 0;
    double m1 = 1.0, m2 = 1.0, subMul = 0.5;
    int uniN = 1;
    float uniNorm = 1.0f;
    double uniMaxDet = 1.0;
    bool stereoUni = false;
    float atk = 0.0f, dec = 0.0f, rel = 0.0f, sus = 0.0f, atk2 = 0.0f, dec2 = 0.0f, rel2 = 0.0f, sus2 = 0.0f;
    EnvBends bend1{}, bend2{};
    int t1type = 0, t2type = 0;
    bool slope1 = false, slope2 = false;
    double f1tgt = 0.0, f2tgt = 0.0, r1tgt = 0.0, r2tgt = 0.0, fSm = 0.0;
    double f1Env = 0.0, f1Lfo = 0.0, f1Key = 0.0, f2Env = 0.0, f2Lfo = 0.0, f2Key = 0.0;
    int r1 = 0, r2 = 0, rs = 0;
    bool series = false;
    int ls1 = 0, ls2 = 0;
    double lfo1Inc = 0.0, lfo2Inc = 0.0;
    float lfo1Depth = 0.0f, lfo2Depth = 0.0f, gain = 0.0f, modWheel = 0.0f;
    double bendMul = 1.0;
    float exprCoef = 0.0f;
    double maxCutoffHz = 21609.0;
    float opL = 0.0f, opR = 0.0f;
    bool driveOn = false, chorusOn = false, reverbOn = false;
    float drive = 0.0f;
    int driveMode = 0;
    float driveInvNorm = 1.0f, toneCoef = 0.0f, chAmt = 0.0f;
    double chRate = 0.0;
    int chVoices = 1;
    float rvAmt = 0.0f, revFb = 0.0f, revDamp = 0.0f;
    int amN = 0;
    bool pitchStatic = true, osc2PitchStatic = true;
    double invSr = 1.0 / 44100.0;
    bool settled = true;
    double f1base = 0.0, f2base = 0.0, reso1 = 0.0, reso2 = 0.0;
};

struct WavetableSynth::BlockArrays {
    struct ModEntry {
        uint8_t src;
        uint8_t dst;
        double amt;
    };
    double uniDetF[kUnison];
    float uniPanL[kUnison];
    float uniPanR[kUnison];
    float exAdd[kExprDims];
    ModEntry am[kSources * kDests];
};

struct WavetableSynth::BatchParams {
    BlockScalars s;
    BlockArrays a;
};

} // namespace nota
