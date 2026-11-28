// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — pattern serialization and the arpeggiator's note order (see TwinSeq.h).

#include "instruments/twin/TwinSeq.h"
#include "instruments/twin/Twin.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <vector>

namespace nota {
namespace twin {

namespace {

constexpr uint32_t kMagic = 0x31505754; // "TWP1"

void put8(std::vector<uint8_t>& b, uint8_t v) {
    b.push_back(v);
}
void put16(std::vector<uint8_t>& b, uint16_t v) {
    b.push_back(static_cast<uint8_t>(v));
    b.push_back(static_cast<uint8_t>(v >> 8));
}
void put32(std::vector<uint8_t>& b, uint32_t v) {
    for (int i = 0; i < 4; ++i) b.push_back(static_cast<uint8_t>(v >> (8 * i)));
}

struct Reader {
    const uint8_t* p;
    int32_t left;
    bool ok = true;
    uint8_t u8() {
        if (left < 1) return ok = false, 0;
        --left;
        return *p++;
    }
    uint16_t u16() {
        const uint16_t lo = u8(), hi = u8();
        return static_cast<uint16_t>(lo | (hi << 8));
    }
    uint32_t u32() {
        uint32_t v = 0;
        for (int i = 0; i < 4; ++i) v |= static_cast<uint32_t>(u8()) << (8 * i);
        return v;
    }
};

uint32_t hash(uint32_t x) {
    x ^= x >> 16;
    x *= 0x7feb352dU;
    x ^= x >> 15;
    x *= 0x846ca68bU;
    x ^= x >> 16;
    return x;
}

} // namespace

std::vector<uint8_t> serializePatterns(const PatternSet& ps) {
    std::vector<uint8_t> b;
    b.reserve(4 + kPatterns * (1 + kSteps * 9 + kLanes * (3 + 2 * kSteps)));
    put32(b, kMagic);
    for (const Pattern& p : ps.pat) {
        put8(b, p.length);
        for (const Step& s : p.step) {
            put8(b, s.n);
            for (uint8_t n : s.note) put8(b, n);
            put8(b, s.vel);
            put8(b, s.gate);
        }
        for (const Lane& l : p.lane) {
            put16(b, static_cast<uint16_t>(l.target));
            put8(b, l.smooth);
            for (uint16_t v : l.val) put16(b, v);
        }
    }
    return b;
}

bool deserializePatterns(const uint8_t* data, int32_t size, PatternSet& out) {
    Reader r{data, size};
    if (!data || r.u32() != kMagic) return false;
    PatternSet ps;
    for (Pattern& p : ps.pat) {
        p.length = static_cast<uint8_t>(std::clamp<int>(r.u8(), 1, kSteps));
        for (Step& s : p.step) {
            s.n = static_cast<uint8_t>(std::min<int>(r.u8(), kStepNotes));
            for (uint8_t& n : s.note) n = static_cast<uint8_t>(std::min<int>(r.u8(), 127));
            s.vel = static_cast<uint8_t>(std::clamp<int>(r.u8(), 1, 127));
            s.gate = static_cast<uint8_t>(std::clamp<int>(r.u8(), 1, kGateTie));
        }
        for (Lane& l : p.lane) {
            l.target = static_cast<int16_t>(r.u16());
            l.smooth = r.u8() ? 1 : 0;
            for (uint16_t& v : l.val) v = r.u16();
        }
    }
    if (!r.ok) return false;
    out = ps;
    return true;
}

int arpNotes(const int* held, int n, int mode, int octaves, int64_t k, uint32_t seed, int* out, int maxN) {
    if (n <= 0 || maxN <= 0) return 0;
    octaves = std::clamp(octaves, 1, 4);
    // The run: the held notes in the mode's order, repeated an octave up per octave.
    int sorted[32];
    const int m = std::min(n, 32);
    std::memcpy(sorted, held, sizeof(int) * static_cast<size_t>(m));
    if (mode != ArpAsPlayed) std::sort(sorted, sorted + m);
    if (mode == ArpChord) {
        const int oct = static_cast<int>(((k % octaves) + octaves) % octaves);
        const int c = std::min(m, maxN);
        for (int i = 0; i < c; ++i) out[i] = std::min(127, sorted[i] + 12 * oct);
        return c;
    }
    const int len = m * octaves;
    auto at = [&](int i) { return std::min(127, sorted[i % m] + 12 * (i / m)); };
    int idx;
    switch (mode) {
    case ArpDown: idx = len - 1 - static_cast<int>(((k % len) + len) % len); break;
    case ArpUpDown: {
        const int cyc = len > 1 ? 2 * len - 2 : 1;
        const int pos = static_cast<int>(((k % cyc) + cyc) % cyc);
        idx = pos < len ? pos : cyc - pos;
        break;
    }
    case ArpRandom:
        idx = static_cast<int>(hash(seed ^ static_cast<uint32_t>(k) * 0x9E3779B9U) % static_cast<uint32_t>(len));
        break;
    default: idx = static_cast<int>(((k % len) + len) % len); break; // Up, As played
    }
    out[0] = at(idx);
    return 1;
}

} // namespace twin

// ---- the sequencer (US8) -------------------------------------------------------------------
// Steps sit on a grid counted from the song's start (AR-9: the transport is the only clock), so a
// step's sample is the same however the host splits its blocks; a pattern's step is that index
// modulo its length. Odd steps lean late by the swing. While the transport is stopped the
// sequencer is silent and the keys play the instrument as usual.

bool Twin::seqActive() const {
    return get(twin::GSeqOn) >= 0.5f && spb_ > 1.0 && (playing_ || freeRun_);
}

// Latch (Seq) or Hold (Arp): the sequence keeps running after the keys are up.
bool Twin::seqLatched() const {
    return twin::stepOf(get(twin::GSeqKind), 2) == 1 ? get(twin::GArpHold) >= 0.5f : get(twin::GSeqLatch) >= 0.5f;
}

// A key with the transport stopped starts the sequencer on its own clock, from its first step.
void Twin::freeStart() {
    releaseSeqNotes();
    seqRunning_ = false; // seqService re-syncs: pattern, first step, motion
    seqResync_ = true;
    freeRun_ = true;
    freeT_ = 0.0;
    arpN_ = 0;
    arpStartK_ = 0;
}

void Twin::pluginParamLane(int32_t i, const AutomationLane& lane, double beatFrom, double beatTo) {
    if (i == twin::GSeqPattern) patLane_.fill(lane, beatFrom, beatTo, stepBeats());
}

int Twin::wantPattern(int64_t k) const {
    float v = get(twin::GSeqPattern);
    if (playing_ && seqStepBeats_ == stepBeats()) patLane_.at(k, v); // k counts song steps then
    return twin::stepOf(v, twin::kPatterns);
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
    freeStop(); // Latch / Hold switched off with no key down ends a key-run sequence
    if (!seqActive()) {
        if (seqRunning_) {
            releaseSeqNotes();
            seqRunning_ = false;
            playStep_ = -1;
            for (auto& m : motion_) m.set = false;
        }
        return INT32_MAX;
    }
    const double now = clockAt(offset); // absolute sample: the song's, or the free clock's
    const double sb = stepBeats();
    if (seqResync_ || sb != seqStepBeats_ || !seqRunning_) {
        if (seqResync_) releaseSeqNotes();
        seqStepBeats_ = sb;
        nextK_ = static_cast<int64_t>(std::ceil(now / spb_ / sb - 1e-9));
        while (onsetSample(nextK_) < now) ++nextK_;
        if (!seqRunning_) {
            curPat_ = wantPattern(nextK_);
            pendPat_ = -1;
            patStartK_ = 0; // a pattern started from stopped sits on the song's grid
        }
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
    const int want = wantPattern(k);
    pendPat_ = want == curPat_ ? -1 : want; // switches at the pattern's end; picking the playing one cancels
    if (twin::stepOf(get(twin::GSeqKind), 2) == 1) {
        // The arpeggiator: the held (or held-over) chord, one note — or all of it — per step.
        n = twin::arpNotes(arpList_, arpN_, twin::stepOf(get(twin::GArpMode), twin::kArpModes),
            1 + twin::stepOf(get(twin::GArpOct), 4), k - arpStartK_, 0x5EEDu, notes, twin::kStepNotes);
        vel = arpVel_;
        off = onset + std::max(0.05, static_cast<double>(get(twin::GArpGate))) * stepLen;
        playStep_ = arpN_ > 0 ? static_cast<int>((k - arpStartK_) % 64) : -1;
        for (auto& m : motion_) m.set = false; // the arpeggiator has no motion: the knobs are back
        seqStepV_ = 0.0f;
    } else {
        auto stepIn = [&](const twin::Pattern& P) {
            const int64_t r = k - patStartK_;
            return static_cast<int>(((r % P.length) + P.length) % P.length);
        };
        const twin::Pattern* P = &ps->pat[curPat_];
        int st = stepIn(*P);
        if (st == 0 && pendPat_ >= 0) {
            curPat_ = pendPat_;
            pendPat_ = -1;
            patStartK_ = k; // the queued pattern starts on its first step, whatever its length
            P = &ps->pat[curPat_];
            st = 0;
        }
        playStep_ = st;
        seqStepV_ = P->length > 1 ? static_cast<float>(st) / static_cast<float>(P->length - 1) : 0.0f;
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
        } else if (sn.tie || sn.off <= onset || again) {
            // A note still sounding (a long gate swung past this onset) that this step plays again
            // ends here, so its own note-off can't cut the new one short.
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
    else
        Instrument::controller(ctl, value);
}

void Twin::setTransport(double beatStart, double samplesPerBeat, bool playing) {
    // Whole samples: the sequencer's step times are whole samples too, so a block boundary that
    // lands a hair off one can't move a step by a sample (renders repeat at any block size).
    const double pos = std::round(beatStart * samplesPerBeat);
    const bool jumped = playing && playing_ && std::fabs(pos - (tPos_ + tFrames_)) > 2.0;
    if (jumped || playing != playing_) seqResync_ = true;
    patLane_.transport(beatStart);
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
    // Stopping with keys still held carries on on the free clock; starting hands over to the song.
    if (playing_ && !playing) {
        freeRun_ = arpKeys_ > 0 || transKeys_ > 0;
        freeT_ = 0.0;
    } else if (playing)
        freeRun_ = false;
    playing_ = playing;
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
    case A_SetLength:
        P.length = static_cast<uint8_t>(std::clamp(iarg, 1, kSteps));
        if (recStep_ >= P.length) recStep_ = 0; // the record cursor stays on the pattern
        break;
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
        if (farg > 0.0f)
            S.vel = static_cast<uint8_t>(std::clamp<int>(static_cast<int>(std::lround(farg * 127.0f)), 1, 127));
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
        if (stepOk(iarg))
            P.step[iarg].vel =
                static_cast<uint8_t>(std::clamp<int>(static_cast<int>(std::lround(farg * 127.0f)), 1, 127));
        break;
    case A_SetStepGate:
        if (stepOk(iarg))
            P.step[iarg].gate =
                farg > 1.0f
                    ? kGateTie
                    : static_cast<uint8_t>(std::clamp<int>(static_cast<int>(std::lround(farg * 100.0f)), 1, 100));
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
        if (from >= 0 && from < kPatterns && to >= 0 && to < kPatterns && from != to)
            patAuth_->pat[to] = patAuth_->pat[from];
        break;
    }
    case A_MotionTarget: {
        const int lane = iarg >> 16, param = iarg & 0xFFFF;
        if (lane >= 0 && lane < kLanes)
            P.lane[lane].target = param == 0xFFFF || param >= kNumParams ? int16_t{-1} : static_cast<int16_t>(param);
        break;
    }
    case A_MotionMode:
        if (iarg >= 0 && iarg < kLanes) P.lane[iarg].smooth = farg >= 0.5f ? 1 : 0;
        break;
    case A_MotionSet: {
        const int lane = iarg >> 8, st = iarg & 0xFF;
        if (lane >= 0 && lane < kLanes && stepOk(st))
            P.lane[lane].val[st] = farg < 0.0f
                                       ? kMotionUnset
                                       : static_cast<uint16_t>(std::lround(std::clamp(farg, 0.0f, 1.0f) * 65534.0f));
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
    tele_[ScRecStep].store(
        recArmed_.load(std::memory_order_relaxed) ? static_cast<float>(recStep_) : -1.0f, std::memory_order_relaxed);
    if (changed) publishPatterns();
}

} // namespace nota
