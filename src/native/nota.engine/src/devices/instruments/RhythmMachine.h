// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Rhythm — the built-in drum machine (instrument kind 12). A self-contained 16-step
// groovebox: eight drum voices, each a compact synth engine (electronic drum generation —
// analog/FM kick, noise snare, metal hats…), programmed on an internal 16-step sequencer
// that plays synced to the DAW transport. Incoming MIDI notes also trigger the mapped voice
// live (finger-drumming). Any voice can instead play a loaded one-shot (a Start / Length
// region of it, forwards or reversed).
//
//   Voice engines (retriggered, monophonic): Kick (sine + pitch-drop + click), Snare (tone
//   sines + noise snap), Clap (noise bursts + tail), Rim (band-passed noise click), Closed/
//   Open Hat (HP noise — closed chokes open, 808-style), Tom (sine + pitch env), Perc (ring).
//   The engines live in RhythmVoiceDsp.h/.cpp, split out of this header.
//
//   Sequencer: setTransport() feeds the beat clock; a 1/16 step = 0.25 beat, 16 steps = 1 bar.
//   Swing delays odd 16ths; Humanize jitters velocity. Four pattern banks (A–D).
//
//   Bus: the voices sum through Glue (a gentle compressor) into Volume.
//
//   Voice FX: every voice has its own insert chain of built-in effects (the same devices a
//   Drum Rack pad chain holds — BuiltinDevices.h), run on that voice's stereo signal before the
//   sum, so a snare can have its reverb and a kick its saturation. The chains swap as immutable
//   snapshots (structural edits on the message thread, the audio thread reads one pointer) and
//   keep processing after a hit ends, so tails ring out. A voice without a chain skips it.
//
// The 7 per-voice knobs (Tune/Decay/Punch/Tone/Drive/Level/Pan) + 4 globals ride the plugin-
// param interface (normalized 0..1) → automation / persist / clone for free. Appended after
// them (param order is the persisted layout — append only): per-voice sample Start / Length /
// Reverse, then Glue (a bus compressor on the kit sum), then eight Macros.
//
//   Macros: eight knobs over the whole kit (params macro1..macro8, so they automate and learn
//   like any other). A mapping drives one target across [min, max] in the target's own units —
//   one of the machine's own params (a voice's Decay, Glue …; normalized) or a param of a
//   voice's FX device (the device's units) — so one knob can open every snare and clap reverb
//   at once. Mappings live in the FX snapshot (they follow their device when the chain is
//   edited) and are saved after the kit label, tagged, with the macros' names.
//
// The step patterns are structural state (getState/setState blob + the action() UI channel), not params. A step is on/off with
// a velocity (a "quiet" step is just a low one) and an accent flag. The voice FX chains and a
// kit label follow the pattern data in the same blob, tagged, so an older reader stops before
// them. Allocation-free on the audio thread. Name & DSP are Nota's own.
//
// Sleep: one-shots end, envelopes decay, voice-FX tails ring out — hasTail is true and
// isIdle engages only with no active voice, every voice-FX chain idle and a silent
// bus block, after a hangover. The gate is the active flag, not audibility: non-snare
// synth voices latch active after dying while still consuming the shared noise stream,
// so sleeping over them changed later hits (measured −14.8 dB; reverted) — sleep
// engages for fresh, snare-only and sample-voice kits instead.
// noteOn/action/pluginParamSet/transport-start wake.

#pragma once

#include "devices/Instrument.h"
#include "media/SampleBuffer.h"
#include "devices/BuiltinDevices.h"
#include "engine/SnapshotHistory.h"
#include "RhythmVoiceDsp.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class RhythmMachine final : public Instrument {
  public:
    static constexpr int kVoices = 8;
    static constexpr int kSteps = 16;
    static constexpr int kBanks = 4;
    static constexpr int kPV = 7;                                   // params per voice (v1 block)
    static constexpr int kPX = 3;                                   // appended per-voice params (sample region)
    static constexpr int kLegacyParams = kVoices * kPV + 4;         // RTH1 blobs carry exactly these
    static constexpr int kExtra = kLegacyParams;                    // first appended per-voice param
    static constexpr int kNumParamsV2 = kExtra + kVoices * kPX + 1; // + Glue (RTH2 blobs)
    static constexpr int kMacros = 8;
    static constexpr int kNumParams = kNumParamsV2 + kMacros; // + Macro 1..8 (RTH3)

    enum VP { Tune = 0, Decay, Punch, Tone, Drive, Level, Pan };
    enum XP { Start = 0, Length, Reverse };
    enum GP { Swing = kVoices * kPV, Humanize, Accent, Volume, Glue = kExtra + kVoices * kPX, Macro0 = kNumParamsV2 };

    // A macro mapping. device -1: param is one of the machine's own params (voice = the voice it
    // belongs to, -1 for a global); device ≥ 0: param of that device on the voice's FX chain.
    struct MacroMap {
        int32_t macro = 0, voice = 0, device = -1, param = 0;
        float lo = 0.0f, hi = 1.0f;
        int32_t curve = 0; // 0 Linear, 1 Exp, 2 Log, 3 S-curve (the rack's curves)
    };
    // scopeRead layout: the playing step (-1 stopped), the current bank, an edit revision that
    // bumps on every pattern change (so an editor re-reads the blob after an MCP edit), the
    // sounding-voice count, the glue gain reduction (dB, ≥ 0), then per voice a trigger flash
    // (1 on a hit, decaying) and the sample play position (0..1 of the file, -1 idle).
    enum Scope {
        S_Step = 0,
        S_Bank,
        S_Rev,
        S_Active,
        S_Glue,
        S_Flash0,
        S_Pos0 = S_Flash0 + kVoices,
        kScopeN = S_Pos0 + kVoices
    };

    // action() ids (UI editing channel).
    enum Act {
        A_ToggleStep = 0,
        A_SetVel,
        A_ToggleAccent,
        A_SelectBank,
        A_ClearBank,
        A_SelectVoice,
        A_SetSource,
        A_CopyBank,
        A_Audition,
        A_ClearVoice
    };
    enum Source { Synth = 0, Sample = 1 };

    RhythmMachine();

    int32_t kind() const override { return 12; }
    const char* displayName() const override { return "Nota Rhythm"; }
    bool handlesSustain() const override { return true; } // drums ignore the pedal (#99)

    void setSampleRate(double sr) override;
    void setTransport(double beatStart, double spb, bool playing) override;
    void setTransportInfo(const TransportInfo& ti) override;

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    // Names group in the automation menu by their head: "Kick › Tune", "Perform › Swing",
    // "Master › Glue". Ids never change.
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override;
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    // ---- UI editing channel ----------------------------------------------
    void action(int32_t id, int32_t iarg, float farg) override;

    // ---- per-voice samples (Phase 2) — message thread; the engine clones before load ------
    void setVoiceSample(int v, std::shared_ptr<SampleBuffer> b);
    int32_t activeVoiceCount() const override { return active_.load(std::memory_order_relaxed); }
    int32_t voiceSource(int v) const { return (v >= 0 && v < kVoices) ? src_[v] : 0; }
    int64_t voiceSampleId(int v) const { return (v >= 0 && v < kVoices && smp_[v]) ? smp_[v]->id : 0; }
    std::shared_ptr<SampleBuffer> voiceSampleBuf(int v) const { return (v >= 0 && v < kVoices) ? smp_[v] : nullptr; }

    // ---- per-voice FX chains (message thread; the audio thread reads the live snapshot) ----
    int32_t voiceDeviceCount(int v) const;
    Device* voiceDevice(int v, int d) const;
    int32_t addVoiceDevice(int v, int32_t kind);
    bool removeVoiceDevice(int v, int d);
    bool moveVoiceDevice(int v, int from, int to);
    void clearVoiceDevices(int v);
    // ---- macros (message thread; the audio thread reads the mappings in the FX snapshot) ----
    std::string macroName(int m) const;
    void setMacroName(int m, const std::string& n) {
        if (m >= 0 && m < kMacros) macroNames_[m] = n.substr(0, 32);
    }
    // Adds a mapping and applies the macro's current value to it. Returns its index, -1 when the
    // target doesn't exist (a macro can't drive a macro).
    int32_t addMacroMapping(int macro, int voice, int device, int param, float lo, float hi);
    int32_t macroMappingCount() const {
        FxState* fx = fxStates_.live();
        return fx ? static_cast<int32_t>(fx->maps.size()) : 0;
    }
    bool macroMapping(int i, MacroMap& out) const;
    bool removeMacroMapping(int i);
    bool setMacroMappingRange(int i, float lo, float hi);
    bool setMacroMappingCurve(int i, int curve);
    // Every mapping and name back to empty (a kit load starts the macros over). Values stay.
    void clearMacros();

    // The factory kit the voices came from ("" = none / hand-built) — display metadata only.
    const std::string& kitName() const { return kit_; }
    void setKitName(const std::string& k) { kit_ = k.substr(0, 64); }

    // ---- project state ----------------------------------------------------
    // Layout: [u32 magic]['kNumParams' floats]['bank','sel',2 reserved]['on,vel,acc' per bank/voice/step]
    // ['src' per voice]. RTH1 (before the sample region + Glue) carries kLegacyParams floats.
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    int32_t scopeRead(float* out, int32_t maxN) const override;

    // ---- note events (live finger-drumming) -------------------------------
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t) override { wake(); }
    void allNotesOff() override;

    void render(float* out, int32_t frames) override;

    // Sleep contract: see the header note. noteOn/action/pluginParamSet wake, as does a
    // transport start (a stopped machine with a pattern must not sleep through the downbeat).
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override {
        idle_ = false;
        quietSamples_ = 0;
    }

  private:
    static constexpr uint32_t kMagicV1 = 0x31485452;  // "RTH1" — 60 params
    static constexpr uint32_t kMagicV2 = 0x32485452;  // "RTH2" — + sample region, Glue
    static constexpr uint32_t kMagic = 0x33485452;    // "RTH3" — + Macro 1..8
    static constexpr uint32_t kFxMagic = 0x31584652;  // "RFX1" — voice FX chains + kit label, after the pattern
    static constexpr uint32_t kMacMagic = 0x31434D52; // "RMC1" — macro mappings + names, after the kit label
    static constexpr int kChunk = 256;                // render chunk = the voice FX's max block
    static constexpr int kMaxFx = 8;                  // devices per voice chain
    static constexpr uint64_t kIdleSamples = 16384;   // hangover before sleep

    struct FxState {
        std::vector<std::shared_ptr<Device>> chain[kVoices];
        std::vector<MacroMap> maps;
    };

    // The voice a param belongs to (-1 for the globals).
    static int paramVoice(int p);
    static float macroCurve(float v, int32_t curve);
    // Push macro m's value to its targets. Any thread (automation plays on the audio thread):
    // it only reads the live snapshot and stores atomics / device params.
    void applyMacro(int m, float value);
    std::shared_ptr<FxState> copyFx() const {
        return fxAuthoring_ ? std::make_shared<FxState>(*fxAuthoring_) : std::make_shared<FxState>();
    }
    // Publish a new FX snapshot. Older ones stay alive until no render can still be
    // reading them (the RackCore scheme).
    void commitFx(std::shared_ptr<FxState> ns);
    static void putU32(std::vector<uint8_t>& b, uint32_t v) {
        for (int i = 0; i < 4; ++i) b.push_back((uint8_t)(v >> (i * 8)));
    }
    static constexpr const char* kVoiceNames[kVoices] = {
        "Kick", "Snare", "Clap", "Rim", "Closed Hat", "Open Hat", "Tom", "Perc"};
    static constexpr int kMidiMap[kVoices] = {36, 38, 39, 37, 42, 46, 45, 41};

    // --- voice-DSP bridge --------------------------------------------------
    // Snapshot one voice's synthesis params for the block (floats cross by value).
    RhythmVoiceDsp::Params voiceParams(int v) const {
        return {
            get2(v, Tune), get2(v, Decay), get2(v, Punch), get2(v, Tone), get2(v, Drive), get2(v, Level), sampleRate_};
    }
    void triggerVoice(int v, float velocity) {
        vdsp_.trigger(v, velocity, src_[v] == Sample && smp_[v] && !smp_[v]->empty(), smp_[v].get(), getX(v, Start),
            getX(v, Length), getX(v, Reverse) >= 0.5f);
    }

    // --- helpers -----------------------------------------------------------
    void setP(int v, int p, float val) { pn_[v * kPV + p].store(val, std::memory_order_relaxed); }
    void setX(int v, int p, float val) { pn_[kExtra + v * kPX + p].store(val, std::memory_order_relaxed); }
    float getX(int v, int p) const { return pn_[kExtra + v * kPX + p].load(std::memory_order_relaxed); }
    float get(int i) const { return pn_[i].load(std::memory_order_relaxed); }
    float get2(int v, int p) const { return pn_[v * kPV + p].load(std::memory_order_relaxed); }
    static bool valid(int v, int s) { return v >= 0 && v < kVoices && s >= 0 && s < kSteps; }
    void clearAll() {
        std::memset(on_, 0, sizeof(on_));
        std::memset(vel_, 0, sizeof(vel_));
        std::memset(acc_, 0, sizeof(acc_));
    }

    double sampleRate_ = 44100.0;
    double spb_ = 22050.0, beatStart_ = 0.0;
    bool playing_ = false;
    long lastAbsStep_ = -999999;
    bool stepArmed_ = false;
    int curStep_ = -1;

    RhythmVoiceDsp vdsp_; // the drum voices + their noise stream
    uint8_t on_[kBanks][kVoices][kSteps];
    uint8_t vel_[kBanks][kVoices][kSteps];
    uint8_t acc_[kBanks][kVoices][kSteps];
    uint8_t bank_ = 0, selVoice_ = 0;
    std::shared_ptr<SampleBuffer> smp_[kVoices]; // per-voice sample (Phase 2); null = synth
    uint8_t src_[kVoices] = {0};                 // per-voice source: 0 Synth, 1 Sample

    float glueGr_ = 0.0f, glueAvg_ = 0.0f;    // glue gain reduction + its average, dB (audio thread)
    std::atomic<float> pendingTrig_[kVoices]; // auditions waiting for render(), -1 = none
    std::atomic<uint32_t> rev_{0};            // pattern edit revision (scope S_Rev)
    std::atomic<int32_t> active_{0};          // sounding voices, last block

    std::atomic<float> scope_[kScopeN];
    std::atomic<float> pn_[kNumParams];

    std::shared_ptr<FxState> fxAuthoring_; // voice FX (message thread)
    SnapshotHistory<FxState> fxStates_;    // live snapshot + retirees (audio reads fxStates_.live())
    std::string kit_;                      // factory kit label (message thread)
    std::string macroNames_[kMacros];      // custom macro names ("" = "Macro N"; message thread)
    float mix_[kChunk * 2];                // audio thread: the chunk's sum
    float vbuf_[kVoices][kChunk * 2];      // audio thread: FX voices, pre-chain

    // Auto-sleep hangover.
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
};

} // namespace nota
