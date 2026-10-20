// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic — the built-in multisample instrument (kind 17). A *program* maps many
// samples onto the keyboard: zones (a sample on a key × velocity rectangle with its own
// root, tune, gain, pan, start/end and loop) gathered in groups (layers with a shared
// gain / tune and a round-robin). On a note-on every zone that covers the key, the
// velocity and the group's current round-robin step sounds at once; crossfade ranges
// blend neighbouring layers. Zones flagged as release triggers sound when the key (or the
// pedal) lets go — the piano's damper thump — quieter the longer the note was held.
//
// The program itself (text grammar, path roots, sample cache, async loader) lives in
// MosaicProgram.h/.cpp, split out of this header and included below.
//
// Voices: Poly (16 / 32 / 64 / 128), Mono (legato glide) or Choke. The pool steals released
// voices first, then the quietest, then the oldest, each with a short fade (Old fade); a
// key keeps at most Per key strikes ringing. The sustain pedal (CC64) holds released keys.
// Amp ADSR, a TPT state-variable filter (key-tracking, env → cutoff), glide, Vel → Vol and a
// velocity curve are shared by every zone, like Nota Sampler's. MPE: bend retunes the note
// (the wheel by Bend range), pressure adds level, slide opens the filter.
//
// Threads. The message thread parses and publishes; loader threads decode; the audio thread
// reads the live program through an atomic raw pointer. Programs are kept alive by the
// instrument and retired with the audio thread's render epoch (freed once two renders have
// passed), the engine's own preview pattern, so the audio thread never frees memory.
// All continuous controls are plugin params (normalized 0..1, stable ids, APPEND-ONLY).
// The Hermite read head, loop/crossfade handling, glide, zone
// thresholds and the SVF stay std::-exact — no FastMath migration, no shared-primitive
// migration of the readers. Silent voices auto-sleep via isIdle(); release tails and
// one-shot ends are honored before sleep.

#pragma once

#include "devices/Instrument.h"
#include "MosaicProgram.h"
#include "engine/NoteExpression.h"
#include "media/SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace nota {

class Mosaic final : public Instrument {
  public:
    enum Param {
        Volume = 0,
        Pan,
        Transpose,
        Detune,
        Attack,
        Decay,
        Sustain,
        Release,
        FilterType,
        Cutoff,
        Resonance,
        VoiceMode,
        FilterKeyTrack,
        VelAmount,
        Glide,
        PitchTrack,
        EnvCutoff,
        EnvAmount,
        Output,
        Polyphony,
        PerKey,
        OldFade,
        RelOn,
        RelVol,
        RelLen,
        VelCurve,
        Pedal,
        BendRange,
        MpePressure,
        MpeSlide,
        View,
        kNumParams
    };
    static constexpr int kPool = 160; // voices incl. those fading out after a steal

    Mosaic();
    ~Mosaic() override;

    // ---- value maps (mirrored by MosaicModel.cs) -----------------------------------------
    static double glideSeconds(float v) {
        return v <= 0.001f ? 0.0 : 0.001 * std::pow(2000.0, std::clamp(v, 0.0f, 1.0f));
    }
    static double envOctaves(float v) { return (std::clamp(v, 0.0f, 1.0f) - 0.5) * 12.0; }
    static int polyOf(float v) {
        static const int n[] = {16, 32, 64, 128};
        return n[std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3)];
    }
    static int perKeyOf(float v) { return 1 + std::clamp(static_cast<int>(std::lround(v * 3.0f)), 0, 3); }
    static double oldFadeSec(float v) { return 0.001 * std::pow(200.0, std::clamp(v, 0.0f, 1.0f)); } // 1 … 200 ms
    static float oldFadeNorm(double s) {
        return static_cast<float>(std::log(std::max(0.001, s) / 0.001) / std::log(200.0));
    }
    static double relVolDb(float v) { return -36.0 + 42.0 * std::clamp(v, 0.0f, 1.0f); } // −36 … +6 dB
    static double relLenDb(float v) { return -24.0 * std::clamp(v, 0.0f, 1.0f); }        // drop after 2 s
    static double bendSemis(float v) { return std::round(24.0 * std::clamp(v, 0.0f, 1.0f)); }
    // Velocity curve: −1 soft (quiet notes louder) … +1 hard. v in 0..1.
    static double velCurve(double vel, float c);

    int32_t kind() const override { return 17; }
    const char* displayName() const override { return "Nota Mosaic"; }
    void setSampleRate(double sr) override {
        sr_ = sr > 0 ? sr : 44100.0;
        wake();
    }
    bool supportsMpe() const override { return true; }
    bool handlesSustain() const override { return true; } // holds released keys itself (CC64)

    // ---- the program (message thread) ---------------------------------------------------
    // Parses and starts loading `text`. A program whose files are all loaded already (a zone
    // edit, a clone) publishes at once; otherwise the instrument is silent until it is in.
    void setProgram(const std::string& text);

    // The program text (the newest one set, loaded or not).
    std::string programText() const;

    // Loading state: 0 empty · 1 loading · 2 ready. Polled by the editor; also frees retired
    // programs (message thread).
    struct Status {
        int32_t state = 0, filesDone = 0, filesTotal = 0, missing = 0, zones = 0, serial = 0;
        int64_t diskDone = 0, diskTotal = 0, ram = 0;
    };
    Status status();

    // The decoded buffer behind a zone of the newest program (the editor's waveform), or null.
    std::shared_ptr<SampleBuffer> zoneSample(int32_t zone) const;
    // Every decoded buffer the program holds (project save looks samples up by id).
    std::vector<std::shared_ptr<SampleBuffer>> samples() const;

    // ---- state: params + program text ----------------------------------------------------
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    // ---- plugin params ----------------------------------------------------------------------
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
    float pluginParamDefault(int32_t i) const override {
        static const Mosaic d;
        return d.pluginParamGet(i);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    // ---- telemetry --------------------------------------------------------------------------
    // [0] voices · [1] voice limit · [2] held · [3] held by the pedal · [4] release-trigger
    // voices · [5] pedal down · [6] output peak since the last read · [7] loudest voice's play
    // position 0..1 · [8] its envelope · [9] its stage (0 A · 1 D · 2 S · 3 R) · [10] its note
    // (fractional while gliding) · [11] its cutoff Hz (−1 off) · [12] its zone · [13] last
    // note · [14] last velocity 0..127 · [15] sounding entries N, then N entries
    // zone × 4 + state (0 held · 1 pedal · 2 release / fading); then [128 …] the keys held or
    // sustained now, 128 bits as 8 floats of 16 bits each.
    static constexpr int kScHead = 16, kScZones = 112, kScKeys = kScHead + kScZones, kScope = kScKeys + 8;
    int32_t scopeRead(float* out, int32_t maxN) const override;
    int32_t activeVoiceCount() const override { return static_cast<int32_t>(tele_[0].load(std::memory_order_relaxed)); }

    // ---- notes --------------------------------------------------------------------------------
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    // ---- render -----------------------------------------------------------------------------
    void render(float* out, int32_t frames) override;

    // Sleep contract: zones ring past note-off (loops, release triggers, one-shot ends —
    // hasTail true); isIdle engages only with no active voice and a silent block, after a
    // hangover. noteOn/noteOff/pluginParamSet/setProgram wake.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override {
        idle_ = false;
        quietSamples_ = 0;
    }

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kCtl = 16;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    enum class Stage { Attack, Decay, Sustain, Release };
    struct Voice {
        bool active = false, held = false, sustained = false, relTrig = false;
        int32_t pitch = 0, root = 60, zone = -1, dir = 1, loopMode = 0;
        uint32_t id = 0;
        uint64_t age = 0;
        const SampleBuffer* buf = nullptr;
        double pos = 0, note = 0, tuneSemis = 0, startF = 0, endF = 0, loopA = 0, loopB = 0, xf = 0, cutoff = 20000;
        float level = 1, gl = 1, gr = 1, env = 0, peak = 0;
        int kill = 0, killLen = 1;
        double ic1L = 0, ic2L = 0, ic1R = 0, ic2R = 0;
        Stage stage = Stage::Attack;
        VoiceExpr ex;
    };

    float get(int p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    int voiceModeOf() const { return std::clamp(static_cast<int>(std::lround(get(VoiceMode) * 2.0f)), 0, 2); }

    // ---- program publication (message thread) -------------------------------------------------
    void adopt(const std::shared_ptr<mosaic::Program>& src);
    // The newest program becomes the one the audio thread reads; it plays once its loaders
    // mark it ready. The one it replaces is kept until two more renders have finished.
    void publish(const std::shared_ptr<mosaic::Program>& p);
    // The program the audio thread plays now: the newest, once loaded (audio thread).
    const mosaic::Program* live() const;
    void reap();

    // ---- voices ---------------------------------------------------------------------------------
    void fade(Voice& v, double sec = -1);
    void limitPerKey(int32_t pitch);
    int liveCount() const {
        int c = 0;
        for (auto& v : voices_)
            if (v.active && v.kill == 0) ++c;
        return c;
    }
    // The voice to steal: released first, then the quietest, then the oldest.
    Voice* victim();
    Voice* freeSlot();

    static float xfadeGain(int x, int inLo, int inHi, int outLo, int outHi);

    // Starts the voices of a note (attack zones) or of a key's release (release zones).
    void spawn(const mosaic::Program& p, int32_t pitch, int vel7, float velocity, uint32_t id, bool release,
        float relGain, double fromNote);

    void releaseTrigger(int32_t pitch);

    // 4-point Hermite read of a stereo frame at a fractional position. Kept verbatim:
    // read-head math is position-critical.
    static void readHermite(const SampleBuffer& b, double pos, float& l, float& r);
    static float svf(double& ic1, double& ic2, float xf, double a1, double a2, double a3, double k, int type);

    void publishTelemetry(const float* out, int32_t frames, int nActive, double pan);

    void pushHeld(int32_t p);
    void popHeld(int32_t p);

    // Program (see the header): the newest set (pending_) and the one the audio thread plays.
    mutable std::mutex holdMx_;
    std::shared_ptr<mosaic::Program> pending_;
    std::vector<std::pair<uint64_t, std::shared_ptr<mosaic::Program>>> retired_;
    std::atomic<const mosaic::Program*> cur_{nullptr};
    std::atomic<uint64_t> epoch_{0};
    const mosaic::Program* lastLive_ = nullptr; // audio thread

    // Audio-thread state.
    Voice voices_[kPool];
    static constexpr int kHeld = 16;
    int32_t held_[kHeld] = {};
    int heldN_ = 0;
    int32_t glideFrom_ = -1, lastNote_ = -1;
    float lastVel_ = 0.0f;
    uint32_t noteSerial_ = 0, rng_ = 0x2545F491u;
    uint64_t ageSerial_ = 0, clock_ = 0;
    uint64_t noteOnAt_[128];
    float noteVel_[128];
    bool pedalKeys_[128] = {}, keyDown_[128] = {};
    static constexpr size_t kGroups = 512; // round-robin state for the first 512 groups
    int32_t rrStep_[kGroups] = {}, rrLast_[kGroups] = {};
    GlobalExpr gexpr_;
    bool cc64_ = false, pedalDown_ = false;
    double sr_ = 44100.0;
    double fadeTail_ = 96.0; // frames of fade before a one-shot's end
    float atkRateRel_ = 1.0f;
    mutable std::atomic<float> tele_[kScope] = {};
    std::atomic<float> pn_[kNumParams];

    // Auto-sleep hangover.
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
};

} // namespace nota
