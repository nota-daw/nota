// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin — the built-in two-part morphing analog poly (kind 18, specs/055). Nota's own DSP.
//
// The patch is up to two parts (Lower / Upper: Single, Layer or Split) sharing 12 voices.
// Every sound parameter of a part exists twice, as an A and a B plugin-param, and the part's
// Morph sweeps between them. Keeping the sides as ordinary params is the design's core: an
// automation lane, a MIDI-learned knob, a macro or a CV link on "Lower A · Ladder Cutoff"
// moves exactly that side, and presets, clone and undo need nothing new (research R2).
//
// Param layout (persisted order — APPEND ONLY, see the tables in Twin.cpp):
//   [globals][Lower: part params][Lower A][Lower B][Upper: part params][Upper A][Upper B]
// Matrix sources / destinations are stored as index / kSrcScale and index / kDstScale, so new
// entries fit without re-encoding saved patches. The sequencer's patterns are structural
// state (the state blob), not params.
//
// Files: this one (params, state, the voice pool and render), TwinVoice (one voice's DSP),
// TwinMod (morph and the param → sound mapping); the sequencer (TwinSeq) arrives with US8.
//
// Rendering: control values are refreshed every twin::kCtl samples on a grid counted from the
// last reset, never from block starts, so the same notes render the same whatever the host's
// block size. Voices run at ×2 (×1 from 88.2 kHz); one HIIR half-band decimator per channel
// brings the voice sum down. Deterministic: drift, oscillator start phases and noise come
// from seeded generators, re-seeded on allNotesOff and on a transport start or jump while
// silent, so a freeze equals the live render.

#pragma once

#include "devices/Device.h"
#include "devices/Instrument.h"
#include "devices/instruments/TwinSeq.h"
#include "devices/instruments/TwinVoice.h"
#include "engine/SnapshotHistory.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota {

namespace twin {

// How a continuous value maps to its musical units. The normalized value of a Pitch or LogTime
// param is already exponential in its unit, so interpolating normalized values (the morph)
// moves cutoffs in pitch and times logarithmically.
enum class Domain : uint8_t { Linear, Pitch, LogTime };

constexpr int kParts = 2;      // 0 = Lower, 1 = Upper
constexpr int kSides = 2;      // 0 = A, 1 = B
constexpr int kRoutes = 64;    // matrix slots per part
constexpr int kMacros = 8;     // per part
constexpr int kMaxVoices = 12; // shared by the parts
constexpr int kSrcScale = 63;  // matrix source index / kSrcScale = the stored value
constexpr int kDstScale = 255; // matrix destination index / kDstScale

// Patch-wide params.
enum Global : int {
    GMode = 0, // Single / Layer / Split
    GSplitKey,
    GShare, // Lower's voices: 2, 4, 6, 8, 10
    GVoices, // 4..12
    GPadX,
    GPadY,
    GPadZ,
    GBendRange, // ±2 / ±5 / ±12 / ±24
    GSeqOn,
    GSeqKind, // Seq / Arp
    GSeqPattern,
    GSeqRate,
    GSeqSwing,
    GSeqLatch,
    GSeqTranspose,
    GSeqTarget, // Lower / Upper / Both
    GArpMode,
    GArpOct,
    GArpGate,
    GArpHold,
    GView, // card size (editor state, not sound)
    kGlobalN
};

// Per part, side-less.
enum PartParam : int {
    PMorph = 0,
    PVoiceMode, // Poly / Mono / Legato / Unison
    PGlide,
    PGlideLegato,
    POctave, // −2..+2 (Split transpose)
    PVol,
    PPan,
    PFxOn, // the whole effects section
    PMacro0,
    PRoute0 = PMacro0 + kMacros, // slot n: PRoute0 + 2n = source, + 2n + 1 = destination
    kPartN = PRoute0 + 2 * kRoutes
};

// Per part per side: the morphed sound.
enum SideParam : int {
    O1Wave = 0,
    O1Pw,
    O1Metal,
    O1Coarse,
    O1Fine,
    O1Level,
    O1Bal, // 0 = multimode, 1 = ladder
    O2Wave,
    O2Pw,
    O2Metal,
    O2Coarse,
    O2Fine,
    O2Level,
    O2Bal,
    Sync,
    Fm21,
    FmFilt,
    O2Lo,
    NoiseLevel,
    NoiseColor,
    NoiseBal,
    MmCut,
    MmRes,
    MmMode,
    MmKey,
    MmEnv,
    LdCut,
    LdRes,
    LdSlope, // 12 / 24 dB
    LdKey,
    LdEnv,
    Route, // 0 = series (multimode → ladder), 1 = parallel
    Brute,
    Drive,
    FEnvA,
    FEnvD,
    FEnvS,
    FEnvR,
    FEnvVel,
    FEnvCurve,
    AEnvA,
    AEnvD,
    AEnvS,
    AEnvR,
    AEnvVel,
    AEnvCurve,
    MEnvDly,
    MEnvA,
    MEnvD,
    MEnvS,
    MEnvR,
    MEnvVel,
    MEnvLoop,
    Lfo1Shape,
    Lfo1Rate,
    Lfo1Sync,
    Lfo1Div,
    Lfo1Mode, // per voice / global
    Lfo1Retrig,
    Lfo1Fade,
    Lfo1Amt,
    Lfo2Shape,
    Lfo2Rate,
    Lfo2Sync,
    Lfo2Div,
    Lfo2Mode,
    Lfo2Retrig,
    Lfo2Fade,
    Lfo2Amt,
    Lfo3Shape,
    Lfo3Rate,
    Lfo3Sync,
    Lfo3Div,
    Lfo3Mode,
    Lfo3Retrig,
    Lfo3Fade,
    Lfo3Amt,
    Lfo3Sym,
    Lfo3Curve,
    VibAmt, // LFO 1 → pitch, scaled by the mod wheel
    Drift,
    UniDetune,
    Spread,
    SpreadMode, // by voice / by key / unison
    ModFxType,  // chorus / flanger / phaser / ensemble
    ModFxRate,
    ModFxDepth,
    ModFxMix,
    DlyTime,
    DlySync,
    DlyDiv,
    DlyFb,
    DlyPingPong,
    DlyMix,
    RevSize,
    RevDamp,
    RevMix,
    ModFxOn,
    DlyOn,
    RevOn,
    MatAmt0, // slot n's depth on this side (bipolar, 0.5 = none)
    kSideN = MatAmt0 + kRoutes
};

constexpr int kPartBlock = kPartN + kSides * kSideN;
constexpr int kNumParams = kGlobalN + kParts * kPartBlock;

constexpr int globalIdx(int g) { return g; }
constexpr int partIdx(int part, int p) { return kGlobalN + part * kPartBlock + p; }
constexpr int sideIdx(int part, int side, int s) { return kGlobalN + part * kPartBlock + kPartN + side * kSideN + s; }

struct ParamInfo {
    std::string id;   // stable: "mode", "lo.morph", "lo.a.ldcut", "up.b.m12amt"
    std::string name; // display: "Lower A · Ladder Cutoff" (the automation menu groups on the head before the last space)
    float def = 0.0f; // normalized default
    uint16_t steps = 0; // 0 = continuous, else the number of positions (morph switches at the midpoint)
    Domain domain = Domain::Linear;
};

// The whole table, built once on first use (message thread).
const std::vector<ParamInfo>& paramTable();

} // namespace twin

class Twin final : public Instrument {
  public:
    static constexpr int kNumParams = twin::kNumParams;
    static constexpr uint32_t kStateMagic = 0x314E5754; // "TWN1"

    Twin();

    int32_t kind() const override { return 18; }
    const char* displayName() const override { return "Nota Twin"; }

    // The voices pick the rate up at the next render; the effect devices are re-prepared here —
    // the engine changes rates with rendering suspended (Engine::propagateSampleRate), and their
    // delay / reverb lines are sized to the rate.
    void setSampleRate(double sr) override;
    void setTransportInfo(const TransportInfo& ti) override;
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void noteOffVelocity(int32_t pitch, float velocity) override;
    void allNotesOff() override;
    void controller(int32_t ctl, float value) override; // CC1 = the mod wheel
    // Per-note expression (MPE, poly aftertouch) and the instrument-wide wheel / pressure / CC74.
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    bool supportsMpe() const override { return true; }
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override;
    void render(float* out, int32_t frames) override;

    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }
    // Live view for the card (contracts/c-abi.md § scopeRead, append-only): see the Scope enum.
    enum Scope : int {
        ScStep = 0,     // the sequencer (US8): playing step, −1 stopped
        ScPattern,      // playing pattern
        ScPending,      // pending pattern, −1 none
        ScRevision,     // pattern edit revision
        ScVoicesLo,     // sounding voices, Lower
        ScVoicesUp,     // … Upper
        ScMorphLo,      // the part's live morph (knob + patch-wide routes)
        ScMorphUp,
        ScVoiceMorph0,  // per voice: its live morph, −1 idle (12 values)
        ScLfoPhase0 = ScVoiceMorph0 + twin::kMaxVoices, // LFO 1–3 phase 0..1 (Lower)
        ScPeakDb = ScLfoPhase0 + 3,                     // output peak, dBFS
        ScRecStep,      // the step the next recorded chord lands on (−1 = not recording)
        ScLastKey,      // the last key played (−1 none): the card's split-point learn
        kScopeN
    };
    // The sequencer's editing channel (contracts/c-abi.md, append-only). Each edit is one undo
    // step (editState); they act on the pattern selected for editing (A_SelectPattern).
    enum Act : int32_t {
        A_SelectPattern = 0, // iarg pattern 0..7
        A_SetLength,         // iarg steps 1..64
        A_ClearStep,         // iarg step
        A_AddStepNote,       // iarg step << 8 | note, farg velocity 0..1
        A_RemoveStepNote,    // iarg step << 8 | note
        A_SetStepVelocity,   // iarg step, farg 0..1
        A_SetStepGate,       // iarg step, farg 0..1 (> 1 = tie into the next step)
        A_StepRecordArm,     // iarg 1 = arm (from step 1), 0 = off
        A_StepRecordAdvance, // a rest at the record cursor
        A_ClearPattern,      // iarg pattern
        A_CopyPattern,       // iarg from << 8 | to
        A_MotionTarget,      // iarg lane << 16 | param index (0xFFFF = none)
        A_MotionMode,        // iarg lane, farg 0 stepped / 1 smooth
        A_MotionSet,         // iarg lane << 8 | step, farg 0..1 (< 0 = unset)
        A_MotionRecordArm,   // iarg lane (−1 = off): the lane's playback pauses while it records
        A_ClearMotion,       // iarg lane
        A_StepRecordPoll,    // the card's tick: turns the notes played while armed into steps
    };
    void action(int32_t id, int32_t iarg, float farg) override;
    std::vector<uint8_t> editState() const override;
    void setEditState(const uint8_t* data, int32_t size) override;
    int32_t scopeRead(float* out, int32_t maxN) const override;
    int32_t heldNotes(int32_t* out, int32_t maxN) const override;

    // Sleep contract: the voices are the tail; once none sounds and the output has been
    // quiet for a hangover (the decimator drain), the instrument may sleep. Notes, param
    // edits and transport starts wake it.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override {
        idle_ = false;
        quiet_ = 0;
    }

    // ---- parameters -----------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams && std::isfinite(v))
            pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        wake();
    }
    std::string pluginParamText(int32_t i) const override;
    float pluginParamDefault(int32_t i) const override;
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    // ---- project state --------------------------------------------------------
    // [u32 "TWN1"][u32 paramCount][paramCount × f32 LE][u32 patternBytes][pattern data].
    // setState takes a shorter / longer param array and a missing pattern block (FR-052).
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

  private:
    static constexpr int kMaxVoices = twin::kMaxVoices;
    static constexpr int kMaxHeld = 32;
    static constexpr float kOutGain = 0.353f;         // voice sum → output: Init sits with Volt's Init
    static constexpr uint64_t kIdleHangover = 16384; // frames of silence before sleep
    static constexpr double kDelayHoldSec = 2.5;     // + this with a delay on (its longest time is 2 s)

    float get(int i) const { return pn_[i].load(std::memory_order_relaxed); }
    int voiceCap() const { return 4 + twin::stepOf(get(twin::GVoices), 9); }
    // Audio thread.
    void applyRate();
    void resetAll();
    void computeStatics(int v);
    void updateControl();
    float sourceValue(const twin::Voice& V, const twin::PartCtl& c, int src) const;
    float globalValue(int part, const twin::PartCtl& c, int src) const;
    void modulateVoice(twin::Voice& V, float morph, const twin::LiveRoute* routes, int n);
    void partModulation(int part, float morph);
    void pushFx(int part);
    void renderSegment(float* out, int n);
    int allocate(int first, int count, bool& fade);
    void startVoice(int v, int note, float vel, int s, int S, bool glide);
    void startPending(twin::Voice& V);
    void wakeVoice(twin::Voice& V);
    void release(twin::Voice& V);
    void pushHeld(int part, int note);
    void removeHeld(int part, int note);
    // Parts (US9): the layout, each part's share of the voices, which parts a key / the sequencer plays.
    struct Slice {
        int first, count;
    };
    int layoutMode() const; // 0 Single, 1 Layer, 2 Split
    Slice slice(int part) const;
    int keyParts(int pitch, int* out) const;
    int seqParts(int* out) const;
    void keyOnPart(int part, int pitch, float vel);
    void keyOffPart(int part, int pitch);
    // Notes: from the keyboard (keyOn / keyOff, with the held-key stack) or from the sequencer.
    void keyOn(int pitch, float vel);
    void keyOff(int pitch);
    void seqOn(int pitch, float vel);
    void seqOff(int pitch);
    // The sequencer (audio thread): fire what is due at `offset` samples into this render call
    // and return how far the next event is.
    bool seqActive() const;
    int seqService(int offset);
    void fireStep(int64_t k, double onset);
    void releaseSeqNotes();
    double stepBeats() const;
    double onsetSample(int64_t k) const;
    // Patterns (message thread).
    void publishPatterns();
    twin::Pattern& editPattern() { return patAuth_->pat[editPat_]; }

    std::atomic<float> pn_[kNumParams];
    std::atomic<double> sampleRate_{44100.0};
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<uint64_t> heldPub_[2] = {0, 0};
    std::atomic<float> tele_[kScopeN] = {};
    void publishTelemetry(float peak);

    // ---- audio-thread state ----
    twin::Voice voices_[kMaxVoices];
    twin::PartCtl ctl_[twin::kParts];
    twin::Lfo glfo_[twin::kParts][3]; // LFOs in global mode, per part
    twin::LiveRoute routes_[twin::kParts][twin::kRoutes];
    int routeN_[twin::kParts] = {0, 0};
    float modOff_[twin::kSideN] = {}; // scratch: one voice's side offsets
    // What the routes from patch-wide sources (wheels, pad, macros, global LFOs) do to a whole
    // part this interval; the part's own PartCtl already includes it, and so do its voices.
    float partOff_[twin::kParts][twin::kSideN] = {};
    float partMorphOff_[twin::kParts] = {}, partPitch_[twin::kParts][2] = {}, partVib_[twin::kParts] = {};
    bool partHasOff_[twin::kParts] = {};
    float gLfo1_[twin::kParts][twin::kCtl] = {};
    float morphNow_[twin::kParts] = {}; // each part's morph this interval (knob or motion lane)
    double sr_ = 0.0;
    int os_ = 2;
    int fadeLen_ = 176;
    int ctlLeft_ = 0; // samples until the next control refresh
    bool ctlInit_ = false;
    int lastMode_[twin::kParts] = {-1, -1}, lastCap_ = -1, lastLayout_ = -1;
    uint64_t orderCtr_ = 0;
    int held_[twin::kParts][kMaxHeld] = {};
    int heldN_[twin::kParts] = {};
    uint64_t heldBits_[2] = {0, 0};
    float modWheel_ = 0.0f;
    GlobalExpr gexpr_;
    // transport
    double spb_ = 0.0, beat_ = 0.0, tPos_ = 0.0, tFrames_ = 0.0;
    bool playing_ = false;
    // decimation, per part
    hiir::Downsampler2xFpu<12> dn_[twin::kParts][2];
    float osL_[twin::kCtl * twin::kMaxOs] = {}, osR_[twin::kCtl * twin::kMaxOs] = {};
    float dsL_[twin::kCtl] = {}, dsR_[twin::kCtl] = {};
    // effects, per part: Chorus, Flanger, Phaser, Delay, Reverb — built once, allocation-free after
    enum Fx { FxChorus = 0, FxFlanger, FxPhaser, FxDelay, FxReverb, kFxN };
    std::unique_ptr<Device> fx_[twin::kParts][kFxN];
    bool fxWasOn_[twin::kParts][kFxN] = {};
    float fxBuf_[twin::kCtl * 2] = {};
    // ---- the sequencer ----
    // message thread: the authoring copy, what the edits address, step record
    std::shared_ptr<twin::PatternSet> patAuth_;
    int editPat_ = 0, recStep_ = 0;
    uint8_t recChord_[twin::kStepNotes] = {};
    int recChordN_ = 0, recVel_ = 0;
    uint64_t recDown_[2] = {0, 0};
    std::atomic<bool> recArmed_{false};
    std::atomic<int> recLane_{-1};
    std::atomic<uint32_t> rev_{0};
    // audio → message: notes played while recording (pitch << 8 | velocity, bit 16 = note-off)
    static constexpr uint32_t kRing = 256;
    std::atomic<uint32_t> ring_[kRing] = {};
    std::atomic<uint32_t> ringHead_{0}, ringTail_{0};
    // audio thread
    SnapshotHistory<twin::PatternSet> pats_;
    bool seqResync_ = true, seqRunning_ = false;
    double seqStepBeats_ = 0.25;
    int64_t nextK_ = 0, arpStartK_ = 0;
    int curPat_ = 0, pendPat_ = -1, playStep_ = -1;
    struct SeqNote {
        int pitch;
        double off; // absolute sample of its note-off (a tie: until the next step decides)
        bool tie;
    };
    SeqNote seqNotes_[32] = {};
    int seqNoteN_ = 0;
    int transpose_ = 0, transKeys_ = 0;
    int arpList_[32] = {};
    int arpN_ = 0, arpKeys_ = 0;
    float arpVel_ = 0.8f;
    struct Motion {
        int target = -1;  // param index
        float from = 0.0f, to = 0.0f;
        double t0 = 0.0, len = 1.0; // the step's start and length, absolute samples
        bool smooth = false, set = false;
    };
    Motion motion_[twin::kLanes];
    twin::MotionOverride motionOvr_[twin::kParts];
    // sleep
    bool idle_ = false;
    uint64_t quiet_ = 0;
    float blockPeak_ = 0.0f;
};

} // namespace nota
