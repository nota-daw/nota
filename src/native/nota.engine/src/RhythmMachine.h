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
// them. Header-only, allocation-free on the audio thread. Name & DSP are Nota's own.

#pragma once

#include "Instrument.h"
#include "SampleBuffer.h"
#include "BuiltinDevices.h"

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
    static constexpr int kPV = 7;                       // params per voice (v1 block)
    static constexpr int kPX = 3;                       // appended per-voice params (sample region)
    static constexpr int kLegacyParams = kVoices * kPV + 4;   // RTH1 blobs carry exactly these
    static constexpr int kExtra = kLegacyParams;              // first appended per-voice param
    static constexpr int kNumParamsV2 = kExtra + kVoices * kPX + 1;   // + Glue (RTH2 blobs)
    static constexpr int kMacros = 8;
    static constexpr int kNumParams = kNumParamsV2 + kMacros;         // + Macro 1..8 (RTH3)

    enum VP { Tune = 0, Decay, Punch, Tone, Drive, Level, Pan };
    enum XP { Start = 0, Length, Reverse };
    enum GP { Swing = kVoices * kPV, Humanize, Accent, Volume, Glue = kExtra + kVoices * kPX, Macro0 = kNumParamsV2 };

    // A macro mapping. device -1: param is one of the machine's own params (voice = the voice it
    // belongs to, -1 for a global); device ≥ 0: param of that device on the voice's FX chain.
    struct MacroMap {
        int32_t macro = 0, voice = 0, device = -1, param = 0;
        float   lo = 0.0f, hi = 1.0f;
        int32_t curve = 0;   // 0 Linear, 1 Exp, 2 Log, 3 S-curve (the rack's curves)
    };
    // scopeRead layout: the playing step (-1 stopped), the current bank, an edit revision that
    // bumps on every pattern change (so an editor re-reads the blob after an MCP edit), the
    // sounding-voice count, the glue gain reduction (dB, ≥ 0), then per voice a trigger flash
    // (1 on a hit, decaying) and the sample play position (0..1 of the file, -1 idle).
    enum Scope { S_Step = 0, S_Bank, S_Rev, S_Active, S_Glue, S_Flash0, S_Pos0 = S_Flash0 + kVoices, kScopeN = S_Pos0 + kVoices };

    // action() ids (UI editing channel).
    enum Act { A_ToggleStep = 0, A_SetVel, A_ToggleAccent, A_SelectBank, A_ClearBank, A_SelectVoice, A_SetSource,
               A_CopyBank, A_Audition, A_ClearVoice };
    enum Source { Synth = 0, Sample = 1 };

    RhythmMachine() {
        // Sensible 808-ish defaults per voice.
        for (int v = 0; v < kVoices; ++v) {
            setP(v, Tune, 0.35f); setP(v, Decay, 0.5f); setP(v, Punch, 0.5f);
            setP(v, Tone, 0.5f); setP(v, Drive, 0.15f); setP(v, Level, 0.8f); setP(v, Pan, 0.5f);
            setX(v, Start, 0.0f); setX(v, Length, 1.0f); setX(v, Reverse, 0.0f);
        }
        // Per-voice character tweaks.
        setP(0, Tune, 0.22f); setP(0, Decay, 0.55f);   // Kick — low, long
        setP(4, Decay, 0.14f);                          // Closed hat — short
        setP(5, Decay, 0.5f);                           // Open hat — long
        pn_[Swing].store(0.0f); pn_[Humanize].store(0.0f); pn_[Accent].store(0.7f); pn_[Volume].store(0.8f);
        pn_[Glue].store(0.0f);
        for (int m = 0; m < kMacros; ++m) pn_[Macro0 + m].store(0.0f);
        for (auto& p : pendingTrig_) p.store(-1.0f, std::memory_order_relaxed);
        for (auto& sc : scope_) sc.store(0.0f, std::memory_order_relaxed);
        scope_[S_Step].store(-1.0f, std::memory_order_relaxed);
        for (int v = 0; v < kVoices; ++v) scope_[S_Pos0 + v].store(-1.0f, std::memory_order_relaxed);
        clearAll();
        commitFx(std::make_shared<FxState>());
        // A minimal four-on-the-floor so a fresh instance makes a sound.
        for (int s = 0; s < 16; s += 4) { on_[0][0][s] = 1; vel_[0][0][s] = 200; }
        on_[0][4][2] = on_[0][4][6] = on_[0][4][10] = on_[0][4][14] = 1;   // closed hat off-beats
        vel_[0][4][2] = vel_[0][4][6] = vel_[0][4][10] = vel_[0][4][14] = 150;
    }

    int32_t kind() const override { return 12; }
    const char* displayName() const override { return "Nota Rhythm"; }

    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        if (fxAuthoring_)
            for (auto& ch : fxAuthoring_->chain) for (auto& d : ch) if (d) d->setSampleRate(sampleRate_, kChunk);
    }

    void setTransport(double beatStart, double spb, bool playing) override {
        if (spb > 0.0) spb_ = spb;
        beatStart_ = beatStart; playing_ = playing;
        if (FxState* fx = fxLive_.load(std::memory_order_acquire))   // tempo-synced voice FX (a synced Delay)
            for (auto& ch : fx->chain) for (auto& d : ch) if (d) d->setTransport(beatStart, spb, playing);
    }
    void setTransportInfo(const TransportInfo& ti) override {
        if (FxState* fx = fxLive_.load(std::memory_order_acquire))
            for (auto& ch : fx->chain) for (auto& d : ch) if (d) d->setTransportInfo(ti);
    }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    // Names group in the automation menu by their head: "Kick › Tune", "Perform › Swing",
    // "Master › Glue". Ids never change.
    std::string pluginParamId(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return {};
        if (i >= Macro0) return "macro" + std::to_string(i - Macro0 + 1);
        if (i == Glue) return "glue";
        if (i >= kExtra) { static const char* x[] = {"start","length","reverse"}; int k = i - kExtra; return "v" + std::to_string(k / kPX) + "_" + x[k % kPX]; }
        if (i >= kVoices * kPV) { static const char* g[] = {"swing","humanize","accent","volume"}; return g[i - kVoices * kPV]; }
        static const char* pid[] = {"tune","decay","punch","tone","drive","level","pan"};
        return "v" + std::to_string(i / kPV) + "_" + pid[i % kPV];
    }
    std::string pluginParamName(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return {};
        if (i >= Macro0) return "Macro " + std::to_string(i - Macro0 + 1);
        if (i == Glue) return "Master Glue";
        if (i >= kExtra) { static const char* x[] = {"Start","Length","Reverse"}; int k = i - kExtra; return std::string(kVoiceNames[k / kPX]) + " " + x[k % kPX]; }
        if (i >= kVoices * kPV) { static const char* g[] = {"Perform Swing","Perform Humanize","Perform Accent","Master Volume"}; return g[i - kVoices * kPV]; }
        static const char* pn[] = {"Tune","Decay","Punch","Tone","Drive","Level","Pan"};
        return std::string(kVoiceNames[i / kPV]) + " " + pn[i % kPV];
    }
    float pluginParamGet(int32_t i) const override { return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f; }
    void pluginParamSet(int32_t i, float v) override {
        if (i < 0 || i >= kNumParams) return;
        v = std::clamp(v, 0.0f, 1.0f);
        pn_[i].store(v, std::memory_order_relaxed);
        if (i >= Macro0) applyMacro(i - Macro0, v);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    // ---- UI editing channel ----------------------------------------------
    void action(int32_t id, int32_t iarg, float farg) override {
        if (id != A_SelectVoice && id != A_Audition) rev_.fetch_add(1, std::memory_order_relaxed);
        switch (id) {
            case A_ToggleStep: { int v = iarg / kSteps, s = iarg % kSteps; if (valid(v, s)) { uint8_t& o = on_[bank_][v][s]; o = o ? 0 : 1; if (o && vel_[bank_][v][s] == 0) vel_[bank_][v][s] = 180; } break; }
            case A_SetVel:     { int v = iarg / kSteps, s = iarg % kSteps; if (valid(v, s)) vel_[bank_][v][s] = (uint8_t)std::clamp((int)std::lround(farg * 255.0f), 0, 255); break; }
            case A_ToggleAccent: { int v = iarg / kSteps, s = iarg % kSteps; if (valid(v, s)) acc_[bank_][v][s] = acc_[bank_][v][s] ? 0 : 1; break; }
            case A_SelectBank:  if (iarg >= 0 && iarg < kBanks) bank_ = (uint8_t)iarg; break;
            case A_ClearBank:   { int b = (iarg >= 0 && iarg < kBanks) ? iarg : bank_; for (int v = 0; v < kVoices; ++v) for (int s = 0; s < kSteps; ++s) { on_[b][v][s] = 0; acc_[b][v][s] = 0; } break; }
            case A_SelectVoice: if (iarg >= 0 && iarg < kVoices) selVoice_ = (uint8_t)iarg; break;
            case A_SetSource:   if (iarg >= 0 && iarg < kVoices) src_[iarg] = (farg > 0.5f && smp_[iarg]) ? (uint8_t)Sample : (uint8_t)Synth; break;
            case A_CopyBank: {   // iarg = src * kBanks + dst: the whole bank (steps, velocities, accents)
                int from = iarg / kBanks, to = iarg % kBanks;
                if (from >= 0 && from < kBanks && to >= 0 && to < kBanks && from != to) {
                    std::memcpy(on_[to], on_[from], sizeof(on_[0])); std::memcpy(vel_[to], vel_[from], sizeof(vel_[0])); std::memcpy(acc_[to], acc_[from], sizeof(acc_[0]));
                }
                break;
            }
            case A_Audition:    // play a voice now (the kit list, a pad click); consumed by render()
                if (iarg >= 0 && iarg < kVoices) pendingTrig_[iarg].store(std::clamp(farg, 0.0f, 1.0f), std::memory_order_relaxed);
                break;
            case A_ClearVoice:  if (iarg >= 0 && iarg < kVoices) for (int s = 0; s < kSteps; ++s) { on_[bank_][iarg][s] = 0; acc_[bank_][iarg][s] = 0; } break;
            default: break;
        }
    }

    // ---- per-voice samples (Phase 2) — message thread; the engine clones before load ------
    void setVoiceSample(int v, std::shared_ptr<SampleBuffer> b) {
        if (v < 0 || v >= kVoices) return;
        smp_[v] = std::move(b);
        src_[v] = smp_[v] ? (uint8_t)Sample : (uint8_t)Synth;
    }
    int32_t activeVoiceCount() const override { return active_.load(std::memory_order_relaxed); }
    int32_t voiceSource(int v) const { return (v >= 0 && v < kVoices) ? src_[v] : 0; }
    int64_t voiceSampleId(int v) const { return (v >= 0 && v < kVoices && smp_[v]) ? smp_[v]->id : 0; }
    std::shared_ptr<SampleBuffer> voiceSampleBuf(int v) const { return (v >= 0 && v < kVoices) ? smp_[v] : nullptr; }

    // ---- per-voice FX chains (message thread; the audio thread reads the live snapshot) ----
    int32_t voiceDeviceCount(int v) const {
        FxState* fx = fxLive_.load(std::memory_order_acquire);
        return (fx && v >= 0 && v < kVoices) ? static_cast<int32_t>(fx->chain[v].size()) : 0;
    }
    Device* voiceDevice(int v, int d) const {
        FxState* fx = fxLive_.load(std::memory_order_acquire);
        if (!fx || v < 0 || v >= kVoices || d < 0 || d >= static_cast<int>(fx->chain[v].size())) return nullptr;
        return fx->chain[v][d].get();
    }
    int32_t addVoiceDevice(int v, int32_t kind) {
        if (v < 0 || v >= kVoices || voiceDeviceCount(v) >= kMaxFx) return -1;
        auto dev = makeChainDevice(kind);
        if (!dev) return -1;
        dev->setSampleRate(sampleRate_, kChunk);
        auto ns = copyFx();
        ns->chain[v].push_back(std::move(dev));
        const int32_t idx = static_cast<int32_t>(ns->chain[v].size()) - 1;
        commitFx(ns);
        return idx;
    }
    bool removeVoiceDevice(int v, int d) {
        auto ns = copyFx();
        if (v < 0 || v >= kVoices || d < 0 || d >= static_cast<int>(ns->chain[v].size())) return false;
        ns->chain[v].erase(ns->chain[v].begin() + d);
        auto& mv = ns->maps;   // a mapping follows its device: the removed one's go, later ones shift
        for (auto it = mv.begin(); it != mv.end();) {
            if (it->voice == v && it->device == d) { it = mv.erase(it); continue; }
            if (it->voice == v && it->device > d) --it->device;
            ++it;
        }
        commitFx(ns);
        return true;
    }
    bool moveVoiceDevice(int v, int from, int to) {
        auto ns = copyFx();
        if (v < 0 || v >= kVoices) return false;
        auto& c = ns->chain[v];
        const int n = static_cast<int>(c.size());
        if (from < 0 || from >= n) return false;
        to = std::clamp(to, 0, n - 1);
        if (to == from) return true;
        auto x = c[from];
        c.erase(c.begin() + from);
        c.insert(c.begin() + to, x);
        for (auto& m : ns->maps) {
            if (m.voice != v || m.device < 0) continue;
            if (m.device == from) m.device = to;
            else if (from < to && m.device > from && m.device <= to) --m.device;
            else if (to < from && m.device >= to && m.device < from) ++m.device;
        }
        commitFx(ns);
        return true;
    }
    void clearVoiceDevices(int v) {
        auto ns = copyFx();
        if (v < 0 || v >= kVoices || ns->chain[v].empty()) return;
        ns->chain[v].clear();
        std::erase_if(ns->maps, [v](const MacroMap& m) { return m.voice == v && m.device >= 0; });
        commitFx(ns);
    }
    // ---- macros (message thread; the audio thread reads the mappings in the FX snapshot) ----
    std::string macroName(int m) const {
        if (m < 0 || m >= kMacros) return {};
        return macroNames_[m].empty() ? "Macro " + std::to_string(m + 1) : macroNames_[m];
    }
    void setMacroName(int m, const std::string& n) { if (m >= 0 && m < kMacros) macroNames_[m] = n.substr(0, 32); }
    // Adds a mapping and applies the macro's current value to it. Returns its index, -1 when the
    // target doesn't exist (a macro can't drive a macro).
    int32_t addMacroMapping(int macro, int voice, int device, int param, float lo, float hi) {
        if (macro < 0 || macro >= kMacros) return -1;
        if (device < 0) { if (param < 0 || param >= Macro0) return -1; }
        else if (!voiceDevice(voice, device) || param < 0 || param >= voiceDevice(voice, device)->paramCount()) return -1;
        auto ns = copyFx();
        ns->maps.push_back({macro, device < 0 ? paramVoice(param) : voice, device, param, lo, hi, 0});
        const int32_t idx = static_cast<int32_t>(ns->maps.size()) - 1;
        commitFx(ns);
        applyMacro(macro, get(Macro0 + macro));
        return idx;
    }
    int32_t macroMappingCount() const { FxState* fx = fxLive_.load(std::memory_order_acquire); return fx ? static_cast<int32_t>(fx->maps.size()) : 0; }
    bool macroMapping(int i, MacroMap& out) const {
        FxState* fx = fxLive_.load(std::memory_order_acquire);
        if (!fx || i < 0 || i >= static_cast<int>(fx->maps.size())) return false;
        out = fx->maps[i];
        return true;
    }
    bool removeMacroMapping(int i) {
        auto ns = copyFx();
        if (i < 0 || i >= static_cast<int>(ns->maps.size())) return false;
        ns->maps.erase(ns->maps.begin() + i);
        commitFx(ns);
        return true;
    }
    bool setMacroMappingRange(int i, float lo, float hi) {
        auto ns = copyFx();
        if (i < 0 || i >= static_cast<int>(ns->maps.size())) return false;
        ns->maps[i].lo = lo; ns->maps[i].hi = hi;
        const int m = ns->maps[i].macro;
        commitFx(ns);
        applyMacro(m, get(Macro0 + m));
        return true;
    }
    bool setMacroMappingCurve(int i, int curve) {
        auto ns = copyFx();
        if (i < 0 || i >= static_cast<int>(ns->maps.size())) return false;
        ns->maps[i].curve = std::clamp(curve, 0, 3);
        const int m = ns->maps[i].macro;
        commitFx(ns);
        applyMacro(m, get(Macro0 + m));
        return true;
    }
    // Every mapping and name back to empty (a kit load starts the macros over). Values stay.
    void clearMacros() {
        auto ns = copyFx();
        ns->maps.clear();
        commitFx(ns);
        for (auto& n : macroNames_) n.clear();
    }

    // The factory kit the voices came from ("" = none / hand-built) — display metadata only.
    const std::string& kitName() const { return kit_; }
    void setKitName(const std::string& k) { kit_ = k.substr(0, 64); }

    // ---- project state ----------------------------------------------------
    // Layout: [u32 magic]['kNumParams' floats]['bank','sel',2 reserved]['on,vel,acc' per bank/voice/step]
    // ['src' per voice]. RTH1 (before the sample region + Glue) carries kLegacyParams floats.
    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b;
        b.reserve(8 + kNumParams * 4 + kBanks * kVoices * kSteps * 3);
        putU32(b, kMagic);
        for (int i = 0; i < kNumParams; ++i) { float v = pn_[i].load(std::memory_order_relaxed); uint8_t t[4]; std::memcpy(t, &v, 4); b.insert(b.end(), t, t + 4); }
        b.push_back(bank_); b.push_back(selVoice_); b.push_back(0); b.push_back(0);
        for (int bk = 0; bk < kBanks; ++bk) for (int v = 0; v < kVoices; ++v) for (int s = 0; s < kSteps; ++s) { b.push_back(on_[bk][v][s]); b.push_back(vel_[bk][v][s]); b.push_back(acc_[bk][v][s]); }
        for (int v = 0; v < kVoices; ++v) b.push_back(src_[v]);   // per-voice source (buffers persist separately)
        // Voice FX + kit label, tagged "RFX1": per voice a device count, then per device its
        // builtinKind, bypass flag and params; then the kit label.
        putU32(b, kFxMagic);
        FxState* fx = fxLive_.load(std::memory_order_acquire);
        for (int v = 0; v < kVoices; ++v) {
            const auto* ch = fx ? &fx->chain[v] : nullptr;
            const int n = ch ? static_cast<int>(ch->size()) : 0;
            b.push_back(static_cast<uint8_t>(n));
            for (int d = 0; d < n; ++d) {
                const Device* dev = (*ch)[d].get();
                putU32(b, static_cast<uint32_t>(dev->builtinKind()));
                b.push_back(dev->bypassed() ? 1 : 0);
                const int32_t pc = dev->paramCount();
                putU32(b, static_cast<uint32_t>(pc));
                for (int32_t p = 0; p < pc; ++p) { float x = dev->getParam(p); uint32_t u; std::memcpy(&u, &x, 4); putU32(b, u); }
            }
        }
        putU32(b, static_cast<uint32_t>(kit_.size()));
        b.insert(b.end(), kit_.begin(), kit_.end());
        // Macro mappings + names, tagged "RMC1".
        putU32(b, kMacMagic);
        const int nm = fx ? static_cast<int>(fx->maps.size()) : 0;
        putU32(b, static_cast<uint32_t>(nm));
        for (int i = 0; i < nm; ++i) {
            const auto& m = fx->maps[i];
            putU32(b, (uint32_t)m.macro); putU32(b, (uint32_t)m.voice); putU32(b, (uint32_t)m.device); putU32(b, (uint32_t)m.param);
            uint32_t u; std::memcpy(&u, &m.lo, 4); putU32(b, u); std::memcpy(&u, &m.hi, 4); putU32(b, u);
            putU32(b, (uint32_t)m.curve);
        }
        for (int m = 0; m < kMacros; ++m) { putU32(b, static_cast<uint32_t>(macroNames_[m].size())); b.insert(b.end(), macroNames_[m].begin(), macroNames_[m].end()); }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data || size < 4) return;
        int off = 0;
        uint32_t magic = 0; for (int i = 0; i < 4; ++i) magic |= (uint32_t)data[i] << (i * 8);
        off = 4;
        int nParams;
        if (magic == kMagic) nParams = kNumParams;
        else if (magic == kMagicV2) nParams = kNumParamsV2;
        else if (magic == kMagicV1) nParams = kLegacyParams;
        else return;   // unknown — keep defaults
        rev_.fetch_add(1, std::memory_order_relaxed);
        for (int i = 0; i < nParams && off + 4 <= size; ++i, off += 4) { float v; std::memcpy(&v, data + off, 4); if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
        if (off + 2 <= size) { bank_ = (uint8_t)std::min<int>(data[off], kBanks - 1); selVoice_ = (uint8_t)std::min<int>(data[off + 1], kVoices - 1); off += 4; }
        for (int bk = 0; bk < kBanks; ++bk) for (int v = 0; v < kVoices; ++v) for (int s = 0; s < kSteps; ++s) {
            if (off + 3 > size) return;
            on_[bk][v][s] = data[off]; vel_[bk][v][s] = data[off + 1]; acc_[bk][v][s] = data[off + 2]; off += 3;
        }
        for (int v = 0; v < kVoices; ++v) if (off < size) src_[v] = data[off++];   // source flags (buffers reloaded by the project)
        // Voice FX + kit label. A blob from before them simply ends here: the voices keep no FX.
        auto ns = std::make_shared<FxState>();
        std::string kit;
        auto getU32 = [&](uint32_t& out) { if (off + 4 > size) return false; out = 0; for (int i = 0; i < 4; ++i) out |= (uint32_t)data[off + i] << (i * 8); off += 4; return true; };
        uint32_t tag = 0;
        if (getU32(tag) && tag == kFxMagic) {
            for (int v = 0; v < kVoices && off < size; ++v) {
                const int n = data[off++];
                for (int d = 0; d < n; ++d) {
                    uint32_t kind = 0, pc = 0;
                    if (!getU32(kind) || off >= size) break;
                    const bool byp = data[off++] != 0;
                    if (!getU32(pc)) break;
                    auto dev = makeChainDevice(static_cast<int32_t>(kind));
                    if (dev) dev->setSampleRate(sampleRate_, kChunk);
                    for (uint32_t p = 0; p < pc; ++p) {
                        uint32_t u = 0; if (!getU32(u)) break;
                        float x; std::memcpy(&x, &u, 4);
                        if (dev && std::isfinite(x)) dev->setParam(static_cast<int32_t>(p), x);
                    }
                    if (dev) { dev->paramsRestored(static_cast<int32_t>(pc)); dev->setBypassed(byp); if ((int)ns->chain[v].size() < kMaxFx) ns->chain[v].push_back(std::move(dev)); }
                }
            }
            uint32_t kl = 0;
            if (getU32(kl) && kl <= 64 && off + (int)kl <= size) { kit.assign(reinterpret_cast<const char*>(data + off), kl); off += (int)kl; }
        }
        // Macro mappings + names. Absent before RMC1: no mappings, default names.
        std::string names[kMacros];
        if (getU32(tag) && tag == kMacMagic) {
            uint32_t n = 0;
            if (getU32(n))
                for (uint32_t i = 0; i < n && i < 4096; ++i) {
                    uint32_t f[7];
                    bool ok = true;
                    for (auto& x : f) ok = ok && getU32(x);
                    if (!ok) break;
                    MacroMap m;
                    m.macro = (int32_t)f[0]; m.voice = (int32_t)f[1]; m.device = (int32_t)f[2]; m.param = (int32_t)f[3];
                    std::memcpy(&m.lo, &f[4], 4); std::memcpy(&m.hi, &f[5], 4); m.curve = std::clamp((int32_t)f[6], 0, 3);
                    const bool target = m.device < 0 ? (m.param >= 0 && m.param < Macro0)
                                                     : (m.voice >= 0 && m.voice < kVoices && m.device < (int)ns->chain[m.voice].size());
                    if (m.macro >= 0 && m.macro < kMacros && target && std::isfinite(m.lo) && std::isfinite(m.hi)) ns->maps.push_back(m);
                }
            for (int m = 0; m < kMacros; ++m) {
                uint32_t l = 0;
                if (!getU32(l) || l > 32 || off + (int)l > size) break;
                names[m].assign(reinterpret_cast<const char*>(data + off), l); off += (int)l;
            }
        }
        commitFx(ns);
        kit_ = kit;
        for (int m = 0; m < kMacros; ++m) macroNames_[m] = names[m];
    }
    std::shared_ptr<Instrument> clone() const override {
        auto r = std::make_shared<RhythmMachine>();
        for (int i = 0; i < kNumParams; ++i) r->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        std::memcpy(r->on_, on_, sizeof(on_)); std::memcpy(r->vel_, vel_, sizeof(vel_)); std::memcpy(r->acc_, acc_, sizeof(acc_));
        r->bank_ = bank_; r->selVoice_ = selVoice_;
        r->rev_.store(rev_.load(std::memory_order_relaxed) + 1, std::memory_order_relaxed);
        for (int v = 0; v < kVoices; ++v) { r->smp_[v] = smp_[v]; r->src_[v] = src_[v]; }   // share sample buffers (immutable)
        r->setSampleRate(sampleRate_);
        // The voice FX are copied device by device (kind + params + bypass): a clone must not
        // share DSP state with the original, which keeps playing until the swap.
        auto ns = std::make_shared<FxState>();
        if (FxState* fx = fxLive_.load(std::memory_order_acquire))
            for (int v = 0; v < kVoices; ++v)
                for (auto& d : fx->chain[v]) {
                    auto c = d ? makeChainDevice(d->builtinKind()) : nullptr;
                    if (!c) continue;
                    c->setSampleRate(sampleRate_, kChunk);
                    const int32_t pc = d->paramCount();
                    for (int32_t p = 0; p < pc; ++p) c->setParam(p, d->getParam(p));
                    c->paramsRestored(pc);
                    c->setBypassed(d->bypassed());
                    ns->chain[v].push_back(std::move(c));
                }
        if (FxState* fx = fxLive_.load(std::memory_order_acquire)) ns->maps = fx->maps;
        r->commitFx(ns);
        r->kit_ = kit_;
        for (int m = 0; m < kMacros; ++m) r->macroNames_[m] = macroNames_[m];
        return r;
    }

    int32_t scopeRead(float* out, int32_t maxN) const override {
        const int n = std::min(maxN, (int)kScopeN);
        for (int i = 0; i < n; ++i) out[i] = scope_[i].load(std::memory_order_relaxed);
        if (n > S_Bank) out[S_Bank] = (float)bank_;
        if (n > S_Rev) out[S_Rev] = (float)(rev_.load(std::memory_order_relaxed) & 0xFFFFFF);
        return n;
    }

    // ---- note events (live finger-drumming) -------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        for (int v = 0; v < kVoices; ++v) if (kMidiMap[v] == pitch) { trigger(v, velocity); return; }
    }
    void noteOff(int32_t) override {}
    void allNotesOff() override { for (auto& vc : voices_) vc.active = false; }

    void render(float* out, int32_t frames) override {
        const float swing = get(Swing), human = get(Humanize);
        const float accentG = 1.0f + get(Accent) * 1.0f;   // up to +6 dB-ish
        const float vol = get(Volume) * 1.2f;
        const double stepBeats = 0.25;   // 1/16

        int flash[kVoices] = {0};
        // Auditions from the editor (message thread → here, lock-free).
        for (int v = 0; v < kVoices; ++v) {
            float a = pendingTrig_[v].exchange(-1.0f, std::memory_order_relaxed);
            if (a >= 0.0f) { trigger(v, a); flash[v] = 1; }
        }
        // Glue: a gentle feed-forward bus compressor on the kit sum (peak detector, 5 ms
        // attack, 150 ms release) — threshold −8 → −24 dB and ratio 1.5 → 4 as Glue rises.
        // The makeup follows 70 % of the average reduction (a ~0.7 s average), so the tails come
        // up while the hits never end louder than they went in. 0 = out of circuit.
        const float glue = get(Glue);
        const bool glueOn = glue > 0.001f;
        const float thrDb = -8.0f - glue * 16.0f, ratio = 1.5f + glue * 2.5f;
        const float atk = std::exp(-1.0f / (0.005f * (float)sampleRate_)), rel = std::exp(-1.0f / (0.150f * (float)sampleRate_));
        const float avgC = std::exp(-1.0f / (0.7f * (float)sampleRate_));
        float grMax = 0.0f;
        // Voices with an FX chain render into their own buffer and run through it; the rest
        // go straight to the sum. Chunks keep the buffers small and the chain's block bounded.
        FxState* fx = fxLive_.load(std::memory_order_acquire);
        bool hasFx[kVoices] = {false};
        bool anyFx = false;
        if (fx)
            for (int v = 0; v < kVoices; ++v) {
                for (auto& d : fx->chain[v]) if (d && !d->bypassed()) { hasFx[v] = true; break; }
                anyFx = anyFx || hasFx[v];
            }
        for (int32_t base = 0; base < frames; base += kChunk) {
            const int32_t n = std::min<int32_t>(kChunk, frames - base);
            std::memset(mix_, 0, sizeof(float) * n * 2);
            if (anyFx) for (int v = 0; v < kVoices; ++v) if (hasFx[v]) std::memset(vbuf_[v], 0, sizeof(float) * n * 2);
            for (int32_t j = 0; j < n; ++j) {
                const int32_t i = base + j;
                // --- sequencer clock ---
                if (playing_ && spb_ > 0.0) {
                    const double beat = beatStart_ + (double)i / spb_;
                    double pos = beat / stepBeats;              // in 16th-steps
                    long absStep = (long)std::floor(pos);
                    int barStep = (int)(((absStep % kSteps) + kSteps) % kSteps);
                    double frac = pos - std::floor(pos);        // 0..1 within the step
                    double trigFrac = (barStep & 1) ? (double)swing * 0.5 : 0.0;   // swing delays odd 16ths
                    if (absStep != lastAbsStep_) { lastAbsStep_ = absStep; stepArmed_ = true; }
                    if (stepArmed_ && frac >= trigFrac) {
                        stepArmed_ = false; curStep_ = barStep;
                        for (int v = 0; v < kVoices; ++v) if (on_[bank_][v][barStep]) {
                            float vv = vel_[bank_][v][barStep] / 255.0f;
                            if (acc_[bank_][v][barStep]) vv = std::min(1.0f, vv * accentG);
                            if (human > 0.0f) vv = std::clamp(vv + (noiseF() * 2.0f - 1.0f) * human * 0.25f, 0.0f, 1.0f);
                            trigger(v, vv);
                            flash[v] = 1;
                        }
                    }
                } else {
                    curStep_ = -1;
                }

                // --- voices ---
                for (int v = 0; v < kVoices; ++v) {
                    if (!voices_[v].active) continue;
                    float sl, sr;
                    voiceStereo(v, sl, sr);
                    if (!std::isfinite(sl) || !std::isfinite(sr)) { sl = sr = 0.0f; voices_[v].active = false; }
                    const float pan = std::clamp(get2(v, Pan) * 2.0f - 1.0f, -1.0f, 1.0f);
                    const float gl = (pan <= 0.0f ? 1.0f : 1.0f - pan);
                    const float gr = (pan >= 0.0f ? 1.0f : 1.0f + pan);
                    float* dst = hasFx[v] ? vbuf_[v] : mix_;
                    dst[j * 2] += sl * gl; dst[j * 2 + 1] += sr * gr;
                }
            }

            // --- voice FX: every chain runs each chunk, so its tail rings out after the hit ---
            if (anyFx)
                for (int v = 0; v < kVoices; ++v) {
                    if (!hasFx[v]) continue;
                    for (auto& d : fx->chain[v]) if (d && !d->bypassed()) d->process(vbuf_[v], n);
                    for (int32_t k = 0; k < n * 2; ++k) mix_[k] += std::isfinite(vbuf_[v][k]) ? vbuf_[v][k] : 0.0f;
                }

            // --- bus: glue + volume ---
            for (int32_t j = 0; j < n; ++j) {
                float l = mix_[j * 2], r = mix_[j * 2 + 1];
                if (glueOn) {
                    const float pk = std::max(std::fabs(l), std::fabs(r));
                    const float lvlDb = pk > 1e-6f ? 20.0f * std::log10(pk) : -120.0f;
                    const float over = std::max(0.0f, lvlDb - thrDb);
                    const float target = over * (1.0f - 1.0f / ratio);           // dB of reduction wanted
                    const float c = target > glueGr_ ? atk : rel;
                    glueGr_ = target + c * (glueGr_ - target);
                    glueAvg_ = glueGr_ + avgC * (glueAvg_ - glueGr_);
                    const float g = std::pow(10.0f, (std::min(glueAvg_ * 0.7f, glueGr_) - glueGr_) * 0.05f);
                    l *= g; r *= g;
                    grMax = std::max(grMax, glueGr_);
                } else { glueGr_ = 0.0f; glueAvg_ = 0.0f; }
                out[(base + j) * 2]     += l * vol;
                out[(base + j) * 2 + 1] += r * vol;
            }
        }

        // publish telemetry
        scope_[S_Step].store((float)(playing_ ? curStep_ : -1), std::memory_order_relaxed);
        scope_[S_Glue].store(grMax, std::memory_order_relaxed);
        int active = 0;
        for (int v = 0; v < kVoices; ++v) {
            float f = scope_[S_Flash0 + v].load(std::memory_order_relaxed) * 0.6f;
            if (flash[v]) f = 1.0f;
            scope_[S_Flash0 + v].store(f, std::memory_order_relaxed);
            const Voice& vc = voices_[v];
            if (vc.active) ++active;
            float pos = -1.0f;
            if (vc.active && vc.splay && smp_[v] && smp_[v]->frames > 0) pos = (float)(vc.spos / (double)smp_[v]->frames);
            scope_[S_Pos0 + v].store(pos, std::memory_order_relaxed);
        }
        active_.store(active, std::memory_order_relaxed);
    }

private:
    static constexpr uint32_t kMagicV1 = 0x31485452;   // "RTH1" — 60 params
    static constexpr uint32_t kMagicV2 = 0x32485452;   // "RTH2" — + sample region, Glue
    static constexpr uint32_t kMagic   = 0x33485452;   // "RTH3" — + Macro 1..8
    static constexpr uint32_t kFxMagic = 0x31584652;   // "RFX1" — voice FX chains + kit label, after the pattern
    static constexpr uint32_t kMacMagic = 0x31434D52;  // "RMC1" — macro mappings + names, after the kit label
    static constexpr int kChunk = 256;                  // render chunk = the voice FX's max block
    static constexpr int kMaxFx = 8;                    // devices per voice chain

    struct FxState { std::vector<std::shared_ptr<Device>> chain[kVoices]; std::vector<MacroMap> maps; };

    // The voice a param belongs to (-1 for the globals).
    static int paramVoice(int p) {
        if (p < kVoices * kPV) return p / kPV;
        if (p >= kExtra && p < Glue) return (p - kExtra) / kPX;
        return -1;
    }
    static float macroCurve(float v, int32_t curve) {
        v = std::clamp(v, 0.0f, 1.0f);
        switch (curve) {
            case 1:  return v * v;
            case 2:  return std::sqrt(v);
            case 3:  return v * v * (3.0f - 2.0f * v);
            default: return v;
        }
    }
    // Push macro m's value to its targets. Any thread (automation plays on the audio thread):
    // it only reads the live snapshot and stores atomics / device params.
    void applyMacro(int m, float value) {
        FxState* fx = fxLive_.load(std::memory_order_acquire);
        if (!fx) return;
        for (const auto& mp : fx->maps) {
            if (mp.macro != m) continue;
            const float t = mp.lo + macroCurve(value, mp.curve) * (mp.hi - mp.lo);
            if (mp.device < 0) {
                if (mp.param >= 0 && mp.param < Macro0) pn_[mp.param].store(std::clamp(t, 0.0f, 1.0f), std::memory_order_relaxed);
            } else if (mp.voice >= 0 && mp.voice < kVoices && mp.device < static_cast<int>(fx->chain[mp.voice].size())) {
                if (auto& d = fx->chain[mp.voice][mp.device]) d->setParam(mp.param, t);
            }
        }
    }
    std::shared_ptr<FxState> copyFx() const { return fxAuthoring_ ? std::make_shared<FxState>(*fxAuthoring_) : std::make_shared<FxState>(); }
    // Publish a new FX snapshot. The last few stay alive so a block still reading an older
    // one never sees it freed (the RackCore scheme).
    void commitFx(std::shared_ptr<FxState> ns) {
        fxAuthoring_ = ns;
        fxStates_.push_back(ns);
        if (fxStates_.size() > 16) fxStates_.erase(fxStates_.begin());
        fxLive_.store(ns.get(), std::memory_order_release);
    }
    static void putU32(std::vector<uint8_t>& b, uint32_t v) { for (int i = 0; i < 4; ++i) b.push_back((uint8_t)(v >> (i * 8))); }
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr const char* kVoiceNames[kVoices] = { "Kick", "Snare", "Clap", "Rim", "Closed Hat", "Open Hat", "Tom", "Perc" };
    static constexpr int kMidiMap[kVoices] = { 36, 38, 39, 37, 42, 46, 45, 41 };

    struct Voice {
        bool active = false;
        float vel = 0.0f;
        double ph = 0.0, ph2 = 0.0;
        float env = 0.0f, env2 = 0.0f;     // amp + secondary (noise/tail) env
        float pitchEnv = 0.0f;
        float lp = 0.0f, lp2 = 0.0f, hp = 0.0f, hpPrev = 0.0f;   // one-pole filter states (lp2: a sample's right channel)
        float bp = 0.0f, bp2 = 0.0f;                 // band-pass (SVF) states
        int   burst = 0; float burstT = 0.0f;        // clap multi-burst
        double spos = 0.0; bool splay = false;       // sample-voice playback
        double sBeg = 0.0, sEnd = 0.0; bool srev = false;   // the played region, direction
    };

    // --- helpers -----------------------------------------------------------
    void  setP(int v, int p, float val) { pn_[v * kPV + p].store(val, std::memory_order_relaxed); }
    void  setX(int v, int p, float val) { pn_[kExtra + v * kPX + p].store(val, std::memory_order_relaxed); }
    float getX(int v, int p) const { return pn_[kExtra + v * kPX + p].load(std::memory_order_relaxed); }
    float get(int i) const { return pn_[i].load(std::memory_order_relaxed); }
    float get2(int v, int p) const { return pn_[v * kPV + p].load(std::memory_order_relaxed); }
    static bool valid(int v, int s) { return v >= 0 && v < kVoices && s >= 0 && s < kSteps; }
    void clearAll() { std::memset(on_, 0, sizeof(on_)); std::memset(vel_, 0, sizeof(vel_)); std::memset(acc_, 0, sizeof(acc_)); }
    float noiseF() { rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5; return (rng_ & 0xFFFFFF) / (float)0x1000000; }
    static float expMap(float v, float lo, float hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0f, 1.0f)); }
    // per-sample exponential decay coefficient for a given time constant (seconds to ~-60 dB).
    float decayCoef(float seconds) const { return std::exp(-6.9077f / (std::max(1e-4f, seconds) * (float)sampleRate_)); }

    void trigger(int v, float velocity) {
        Voice& vc = voices_[v];
        vc.active = true; vc.vel = std::clamp(velocity, 0.0f, 1.0f);
        vc.ph = 0.0; vc.ph2 = 0.0; vc.env = 1.0f; vc.env2 = 1.0f; vc.pitchEnv = 1.0f;
        vc.burst = 0; vc.burstT = 0.0f;
        vc.splay = (src_[v] == Sample && smp_[v] && !smp_[v]->empty());
        vc.spos = 0.0; vc.lp = 0.0f; vc.lp2 = 0.0f;
        if (vc.splay) {
            // The played region: Start .. Start + Length of the file (at least ~1 ms); Reverse
            // plays it back to front.
            const double n = (double)smp_[v]->frames;
            const double st = std::clamp((double)getX(v, Start), 0.0, 1.0) * n;
            const double en = std::clamp(st + (double)getX(v, Length) * n, std::min(n, st + 48.0), n);
            vc.sBeg = st; vc.sEnd = en; vc.srev = getX(v, Reverse) >= 0.5f;
            vc.spos = vc.srev ? std::max(st, en - 1.0) : st;
        }
        if (v == 4) voices_[5].active = false;   // closed hat chokes open hat (808)
    }

    // One stereo frame from a sample-voice one-shot (a stereo file keeps its image): pitch
    // (Tune) → playback rate, amp env (Decay), Tone → LP, Drive → tanh, Level. Stops at the
    // end of the region.
    void sampleVoice(int v, float& outL, float& outR) {
        Voice& vc = voices_[v];
        auto& b = *smp_[v];
        const float tune = get2(v, Tune), decay = get2(v, Decay), tone = get2(v, Tone), drive = get2(v, Drive);
        float l, r; b.readStereo((int64_t)vc.spos, l, r);
        const double rate = (b.sourceSampleRate > 0 ? b.sourceSampleRate / sampleRate_ : 1.0) * std::exp2((tune - 0.5) * 2.0);
        // A 64-frame fade into the region's far edge, so a Length cut never clicks.
        const double left = vc.srev ? vc.spos - vc.sBeg : vc.sEnd - vc.spos;
        if (left < 64.0 * rate) { const float f = (float)std::max(0.0, left / (64.0 * rate)); l *= f; r *= f; }
        vc.spos += vc.srev ? -rate : rate;
        // Tone as a gentle one-pole low-pass (0 = dark, 1 = open).
        const float lpc = 0.05f + tone * 0.95f;
        vc.lp += lpc * (l - vc.lp); vc.lp2 += lpc * (r - vc.lp2);
        l = vc.lp; r = vc.lp2;
        if (drive > 0.001f) { const float g = 1.0f + drive * 3.0f; l = std::tanh(l * g); r = std::tanh(r * g); }
        vc.env *= decayCoef(expMap(decay, 0.05f, 2.0f));
        if ((vc.srev ? vc.spos < vc.sBeg : vc.spos >= vc.sEnd) || vc.env < 1e-4f) { vc.active = false; vc.splay = false; }
        const float k = vc.env * vc.vel * get2(v, Level);
        outL = l * k; outR = r * k;
    }

    void voiceStereo(int v, float& l, float& r) {
        if (voices_[v].splay) { sampleVoice(v, l, r); return; }
        l = r = voiceSample(v);
    }

    // One mono sample for voice v, advancing its state. Params denormalized inline.
    float voiceSample(int v) {
        Voice& vc = voices_[v];
        const float tune = get2(v, Tune), decay = get2(v, Decay), punch = get2(v, Punch);
        const float tone = get2(v, Tone), drive = get2(v, Drive), level = get2(v, Level);
        const double invSr = 1.0 / sampleRate_;
        float out = 0.0f;

        switch (v) {
            case 0: case 6: {   // Kick / Tom — sine with a pitch drop + click
                const float base = (v == 0) ? expMap(tune, 30.0f, 120.0f) : expMap(tune, 80.0f, 300.0f);
                const float pdrop = (v == 0) ? 3.0f : 2.0f;
                float f = base * (1.0f + vc.pitchEnv * pdrop);
                vc.ph += (double)f * invSr; if (vc.ph >= 1.0) vc.ph -= 1.0;
                float s = (float)std::sin(kTwoPi * vc.ph);
                float click = (vc.env > 0.6f ? (noiseF() * 2.0f - 1.0f) : 0.0f) * punch * 0.6f;
                s = std::tanh((s + click) * (1.0f + drive * 4.0f));
                vc.pitchEnv *= decayCoef(0.03f + 0.05f * tune);
                vc.env *= decayCoef(expMap(decay, 0.06f, 1.2f));
                out = s * vc.env * vc.vel;
                break;
            }
            case 1: {   // Snare — two tone sines + noise snap
                float f1 = expMap(tune, 140.0f, 330.0f);
                vc.ph += (double)f1 * invSr; if (vc.ph >= 1.0) vc.ph -= 1.0;
                vc.ph2 += (double)(f1 * 1.6f) * invSr; if (vc.ph2 >= 1.0) vc.ph2 -= 1.0;
                float body = 0.5f * ((float)std::sin(kTwoPi * vc.ph) + (float)std::sin(kTwoPi * vc.ph2));
                float n = noiseF() * 2.0f - 1.0f;
                vc.hp = 0.85f * (vc.hp + n - vc.hpPrev); vc.hpPrev = n;   // high-passed noise
                float mix = (1.0f - tone) * body + tone * vc.hp;         // tone = body↔noise
                mix = std::tanh(mix * (1.0f + drive * 3.0f));
                vc.env *= decayCoef(expMap(decay, 0.05f, 0.5f) * (0.4f + 0.6f * (1.0f - punch)));
                vc.env2 *= decayCoef(0.02f);
                out = (mix * vc.env + n * vc.env2 * punch * 0.5f) * vc.vel;
                break;
            }
            case 2: {   // Clap — three noise bursts + a short tail
                vc.burstT -= (float)invSr;
                if (vc.burst < 3 && vc.burstT <= 0.0f) { vc.env = 1.0f; vc.burst++; vc.burstT = 0.012f; }
                float n = noiseF() * 2.0f - 1.0f;
                vc.bp += (float)(expMap(tune, 700.0f, 1800.0f) * invSr) * (n - vc.bp);   // colour
                float s = std::tanh(vc.bp * (1.0f + drive * 3.0f));
                vc.env *= decayCoef((vc.burst < 3) ? 0.01f : expMap(decay, 0.05f, 0.4f));
                out = s * vc.env * vc.vel * 2.0f;
                break;
            }
            case 3: {   // Rim — band-passed noise click
                float n = noiseF() * 2.0f - 1.0f + (vc.env > 0.7f ? 1.0f : 0.0f);
                float g = std::tan((float)(3.14159265f * expMap(tune, 900.0f, 2600.0f) * invSr));
                float a1 = 1.0f / (1.0f + g * (g + 0.6f));
                float hp = (n - (0.6f + g) * vc.bp - vc.bp2) * a1;
                float bpv = g * hp + vc.bp; vc.bp = g * hp + bpv;
                float lp = g * bpv + vc.bp2; vc.bp2 = g * bpv + lp;
                vc.env *= decayCoef(expMap(decay, 0.02f, 0.12f));
                out = std::tanh(bpv * (1.5f + drive * 3.0f)) * vc.env * vc.vel;
                break;
            }
            case 4: case 5: {   // Closed / Open Hat — high-passed metallic noise
                // a cluster of square oscillators → metallic; then high-pass.
                float metal = 0.0f;
                static const float rat[6] = { 1.0f, 1.34f, 1.61f, 1.93f, 2.44f, 2.77f };
                float baseF = expMap(tune, 320.0f, 900.0f);
                vc.ph += (double)(baseF) * invSr; if (vc.ph >= 1.0) vc.ph -= 1.0;
                for (int k = 0; k < 6; ++k) metal += ((std::fmod(vc.ph * rat[k], 1.0) < 0.5) ? 1.0f : -1.0f);
                metal *= 1.0f / 6.0f;
                float n = noiseF() * 2.0f - 1.0f;
                float sig = (1.0f - tone) * metal + tone * n;
                vc.hp = 0.93f * (vc.hp + sig - vc.hpPrev); vc.hpPrev = sig;   // bright HP
                float dcy = (v == 4) ? expMap(decay, 0.02f, 0.16f) : expMap(decay, 0.08f, 0.7f);
                vc.env *= decayCoef(dcy);
                out = std::tanh(vc.hp * (1.0f + drive * 2.0f)) * vc.env * vc.vel * 0.7f;
                break;
            }
            default: {   // 7 Perc — ring/FM metallic
                float fc = expMap(tune, 200.0f, 1200.0f);
                vc.ph += (double)fc * invSr; if (vc.ph >= 1.0) vc.ph -= 1.0;
                vc.ph2 += (double)(fc * (1.0f + tone * 2.5f)) * invSr; if (vc.ph2 >= 1.0) vc.ph2 -= 1.0;
                float s = (float)std::sin(kTwoPi * vc.ph) * (float)std::sin(kTwoPi * vc.ph2);   // ring mod
                s = std::tanh(s * (1.0f + drive * 3.0f) + punch * (noiseF() * 2.0f - 1.0f) * (vc.env > 0.7f ? 0.5f : 0.0f));
                vc.env *= decayCoef(expMap(decay, 0.05f, 0.6f));
                out = s * vc.env * vc.vel;
                break;
            }
        }
        if (vc.env < 1e-4f && vc.env2 < 1e-4f) vc.active = false;
        return out * level;
    }

    double sampleRate_ = 44100.0;
    double spb_ = 22050.0, beatStart_ = 0.0;
    bool   playing_ = false;
    long   lastAbsStep_ = -999999;
    bool   stepArmed_ = false;
    int    curStep_ = -1;
    uint32_t rng_ = 0x1234567u;

    Voice voices_[kVoices];
    uint8_t on_[kBanks][kVoices][kSteps];
    uint8_t vel_[kBanks][kVoices][kSteps];
    uint8_t acc_[kBanks][kVoices][kSteps];
    uint8_t bank_ = 0, selVoice_ = 0;
    std::shared_ptr<SampleBuffer> smp_[kVoices];   // per-voice sample (Phase 2); null = synth
    uint8_t src_[kVoices] = {0};                    // per-voice source: 0 Synth, 1 Sample

    float glueGr_ = 0.0f, glueAvg_ = 0.0f;          // glue gain reduction + its average, dB (audio thread)
    std::atomic<float> pendingTrig_[kVoices];       // auditions waiting for render(), -1 = none
    std::atomic<uint32_t> rev_{0};                  // pattern edit revision (scope S_Rev)
    std::atomic<int32_t> active_{0};                // sounding voices, last block

    std::atomic<float> scope_[kScopeN];
    std::atomic<float> pn_[kNumParams];

    std::shared_ptr<FxState>              fxAuthoring_;   // voice FX (message thread)
    std::vector<std::shared_ptr<FxState>> fxStates_;      // recent snapshots kept alive
    std::atomic<FxState*>                 fxLive_{nullptr};
    std::string kit_;                                     // factory kit label (message thread)
    std::string macroNames_[kMacros];                     // custom macro names ("" = "Macro N"; message thread)
    float mix_[kChunk * 2];                               // audio thread: the chunk's sum
    float vbuf_[kVoices][kChunk * 2];                     // audio thread: FX voices, pre-chain
};

} // namespace nota
