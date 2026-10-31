// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — from params to sound: the A↔B morph and the mapping of a part's morphed values
// to the PartCtl its voices read (specs/055 R3). The matrix (US4) joins here.
//
// Morph rule: a continuous param sounds A + (B − A) · morph on its normalized value — the
// exponential params (cutoffs, times, rates) are exponential in that value, so they move in
// pitch / log-time; a stepped param takes A below 0.5 and B from 0.5. Morph 0 and 1 are
// exactly A and B.

#pragma once

#include "devices/instruments/Twin.h"
#include "devices/instruments/TwinVoice.h"

#include <atomic>

namespace nota {
namespace twin {

// ---- the modulation matrix (US4) ---------------------------------------------------------
// A slot is (source, destination, depth on A, depth on B); the depth morphs with the part.
// Both tables are persisted as indices (source / kSrcScale, destination / kDstScale) —
// APPEND ONLY. The card and MCP mirror them (Nota.Application/Twin/TwinMatrix.cs).
enum Src : int {
    SrcNone = 0,
    SrcLfo1,
    SrcLfo2,
    SrcLfo3,
    SrcFiltEnv,
    SrcAmpEnv,
    SrcModEnv,
    SrcVelocity,
    SrcRelVelocity, // the note-off's velocity, 0 while the key is down (FR-030)
    SrcKey,         // −1 (C−1) … +1 (C9), 0 at middle C
    SrcPressure,
    SrcSlide,
    SrcBend,        // per-note bend, ±24 st → ±1
    SrcModWheel,
    SrcPitchWheel,
    SrcPadX,
    SrcPadY,
    SrcPadZ,
    SrcRandom,      // one value per note, −1..1
    SrcMacro1,
    SrcSeqGate = SrcMacro1 + kMacros, // the sequencer (US8)
    SrcSeqStep,
    kSrcN
};
const char* srcName(int s);

// Destination kinds: the morph, pitches and vibrato are virtual; the rest are side params.
enum DstKind : uint8_t { DstNone, DstMorph, DstPitch, DstPitch1, DstPitch2, DstVibrato, DstSide };
struct Dst {
    DstKind kind;
    int side; // SideParam for DstSide
};
int dstCount();
Dst dstAt(int index);
const char* dstName(int index); // "Ladder Cutoff", "Morph", "Pitch"…
int dstIndexOfSide(int side);   // the destination index of a side param, or 0
constexpr int kDstMorph = 1, kDstPitch = 2, kDstVibrato = 5;

// The part's routes with a source, a destination and a non-zero depth (≤ kRoutes).
int liveRoutes(const std::atomic<float>* pn, int part, float morph, LiveRoute* out);

// The part's morph position, then its sounding PartCtl. spb: samples per beat (0 = no tempo),
// for the synced LFO rates. off: normalized offsets per side param (the matrix), or null.
float partMorph(const std::atomic<float>* pn, int part);
void evalPart(const std::atomic<float>* pn, int part, float morph, double spb, double sr, PartCtl& out,
    const float* off = nullptr, const MotionOverride* ovr = nullptr);

} // namespace twin
} // namespace nota
