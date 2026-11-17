// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Rhythm implementation. Voices render through RhythmVoiceDsp (per-block param snapshots cross by value,
// the shared noise stream keeps the exact call order, so the sequence is unchanged).
// Drive/saturation stages use fast_math::tanhPrecise (see RhythmVoiceDsp.cpp);
// sequencer, glue, patterns and FX snapshots are untouched. Silent kits auto-sleep
// via isIdle() after a hangover — every voice-FX chain must report idle first, so
// tails (and frozen loops, which never report idle) are never cut.

#include "RhythmMachine.h"

#include "devices/Device.h"

namespace nota {

RhythmMachine::RhythmMachine() {
    // Sensible 808-ish defaults per voice.
    for (int v = 0; v < kVoices; ++v) {
        setP(v, Tune, 0.35f);
        setP(v, Decay, 0.5f);
        setP(v, Punch, 0.5f);
        setP(v, Tone, 0.5f);
        setP(v, Drive, 0.15f);
        setP(v, Level, 0.8f);
        setP(v, Pan, 0.5f);
        setX(v, Start, 0.0f);
        setX(v, Length, 1.0f);
        setX(v, Reverse, 0.0f);
    }
    // Per-voice character tweaks.
    setP(0, Tune, 0.22f);
    setP(0, Decay, 0.55f); // Kick — low, long
    setP(4, Decay, 0.14f); // Closed hat — short
    setP(5, Decay, 0.5f);  // Open hat — long
    pn_[Swing].store(0.0f);
    pn_[Humanize].store(0.0f);
    pn_[Accent].store(0.7f);
    pn_[Volume].store(0.8f);
    pn_[Glue].store(0.0f);
    for (int m = 0; m < kMacros; ++m) pn_[Macro0 + m].store(0.0f);
    pn_[Bank].store(0.0f);       // A
    pn_[BankSwitch].store(0.0f); // Step
    for (auto& p : pendingTrig_) p.store(-1.0f, std::memory_order_relaxed);
    for (auto& sc : scope_) sc.store(0.0f, std::memory_order_relaxed);
    scope_[S_Step].store(-1.0f, std::memory_order_relaxed);
    scope_[S_Bank].store(-1.0f, std::memory_order_relaxed); // stopped: scopeRead reports the param
    for (int v = 0; v < kVoices; ++v) scope_[S_Pos0 + v].store(-1.0f, std::memory_order_relaxed);
    clearAll();
    commitFx(std::make_shared<FxState>());
    // A minimal four-on-the-floor so a fresh instance makes a sound.
    for (int s = 0; s < 16; s += 4) {
        on_[0][0][s] = 1;
        vel_[0][0][s] = 200;
    }
    on_[0][4][2] = on_[0][4][6] = on_[0][4][10] = on_[0][4][14] = 1; // closed hat off-beats
    vel_[0][4][2] = vel_[0][4][6] = vel_[0][4][10] = vel_[0][4][14] = 150;
}

void RhythmMachine::setSampleRate(double sr) {
    sampleRate_ = sr > 0 ? sr : 44100.0;
    if (fxAuthoring_)
        for (auto& ch : fxAuthoring_->chain)
            for (auto& d : ch)
                if (d) d->setSampleRate(sampleRate_, kChunk);
    wake();
}

void RhythmMachine::setTransport(double beatStart, double spb, bool playing) {
    // A transport start wakes the machine: a stopped kit with a pattern must not sleep
    // through the downbeat.
    if (playing && !playing_) wake();
    // A seek or a loop wrap while playing: the block doesn't start where the last one ended. The
    // first step after it takes the bank wanted there, even in Pattern mode (a copy swapped in
    // mid-song has no transport yet, so it never reads as a jump and keeps its running switch).
    const bool jumped = playing && playing_ && spb > 0.0 && std::fabs(beatStart - beatStart_) * spb > 2.0;
    if (spb > 0.0) spb_ = spb;
    beatStart_ = beatStart;
    bankLane_.transport(beatStart);
    // Stopped: the playing bank is the param's, and the next start takes it at its first step.
    // Decided here, from the transport, not in render(): a copy swapped in mid-song (a sample
    // load) starts with no transport yet and must keep the running switch it was given.
    if (jumped) lastTrigStep_.store(kNoStep, std::memory_order_relaxed);
    if (!playing) {
        playBank_.store(editBank(), std::memory_order_relaxed);
        lastTrigStep_.store(kNoStep, std::memory_order_relaxed);
    }
    playing_ = playing;
    if (FxState* fx = fxStates_.live()) // tempo-synced voice FX (a synced Delay)
        for (auto& ch : fx->chain)
            for (auto& d : ch)
                if (d) d->setTransport(beatStart, spb, playing);
}

void RhythmMachine::setTransportInfo(const TransportInfo& ti) {
    if (FxState* fx = fxStates_.live())
        for (auto& ch : fx->chain)
            for (auto& d : ch)
                if (d) d->setTransportInfo(ti);
}

std::string RhythmMachine::pluginParamId(int32_t i) const {
    if (i < 0 || i >= kNumParams) return {};
    if (i == Bank) return "bank";
    if (i == BankSwitch) return "bank_switch";
    if (i >= Macro0) return "macro" + std::to_string(i - Macro0 + 1);
    if (i == Glue) return "glue";
    if (i >= kExtra) {
        static const char* x[] = {"start", "length", "reverse"};
        int k = i - kExtra;
        return "v" + std::to_string(k / kPX) + "_" + x[k % kPX];
    }
    if (i >= kVoices * kPV) {
        static const char* g[] = {"swing", "humanize", "accent", "volume"};
        return g[i - kVoices * kPV];
    }
    static const char* pid[] = {"tune", "decay", "punch", "tone", "drive", "level", "pan"};
    return "v" + std::to_string(i / kPV) + "_" + pid[i % kPV];
}

std::string RhythmMachine::pluginParamName(int32_t i) const {
    if (i < 0 || i >= kNumParams) return {};
    if (i == Bank) return "Perform Bank";
    if (i == BankSwitch) return "Perform Bank Switch";
    if (i >= Macro0) return "Macro " + std::to_string(i - Macro0 + 1);
    if (i == Glue) return "Master Glue";
    if (i >= kExtra) {
        static const char* x[] = {"Start", "Length", "Reverse"};
        int k = i - kExtra;
        return std::string(kVoiceNames[k / kPX]) + " " + x[k % kPX];
    }
    if (i >= kVoices * kPV) {
        static const char* g[] = {"Perform Swing", "Perform Humanize", "Perform Accent", "Master Volume"};
        return g[i - kVoices * kPV];
    }
    static const char* pn[] = {"Tune", "Decay", "Punch", "Tone", "Drive", "Level", "Pan"};
    return std::string(kVoiceNames[i / kPV]) + " " + pn[i % kPV];
}

void RhythmMachine::pluginParamSet(int32_t i, float v) {
    if (i < 0 || i >= kNumParams) return;
    v = std::clamp(v, 0.0f, 1.0f);
    pn_[i].store(v, std::memory_order_relaxed);
    if (i >= Macro0 && i < Macro0 + kMacros) applyMacro(i - Macro0, v);
    wake();
}

std::string RhythmMachine::pluginParamText(int32_t i) const {
    if (i == Bank) return std::string(1, static_cast<char>('A' + editBank()));
    if (i == BankSwitch) return get(BankSwitch) >= 0.5f ? "Pattern" : "Step";
    return {};
}

std::string RhythmMachine::pluginParamStepText(int32_t i, int32_t step) const {
    if (i == Bank && step >= 0 && step < kBanks) return std::string(1, static_cast<char>('A' + step));
    if (i == BankSwitch && (step == 0 || step == 1)) return step ? "Pattern" : "Step";
    return {};
}

void RhythmMachine::pluginParamLane(int32_t i, const AutomationLane& lane, double beatFrom, double beatTo) {
    if (i == Bank) bankLane_.fill(lane, beatFrom, beatTo, 0.25); // a 1/16 step
}

int RhythmMachine::stepBank(long absStep, int barStep) {
    (void)barStep;
    float v = 0.0f;
    const int want = bankLane_.at(absStep, v) ? bankOf(v) : editBank();
    const long last = lastTrigStep_.load(std::memory_order_relaxed);
    lastTrigStep_.store(absStep, std::memory_order_relaxed);
    // Pattern mode keeps the playing bank while the steps run on inside one pattern cycle; a new
    // cycle (however it was reached — a skipped step 1 included), a jump back (loop wrap, seek)
    // or a start takes the wanted bank. A forward gap inside the cycle (the steps a replacement
    // copy didn't see, a short seek) keeps the queue, and so does the same step again (a copy
    // swapped in mid-step arms the step it starts in).
    auto cycle = [](long s) { return s >= 0 ? s / kSteps : -((-s + kSteps - 1) / kSteps); };
    const bool take = get(BankSwitch) < 0.5f || last == kNoStep || absStep < last || cycle(absStep) != cycle(last);
    if (take) playBank_.store(want, std::memory_order_relaxed);
    return playBank_.load(std::memory_order_relaxed);
}

int32_t RhythmMachine::pluginParamIndexOfId(const std::string& id) const {
    for (int32_t i = 0; i < kNumParams; ++i)
        if (pluginParamId(i) == id) return i;
    return -1;
}

void RhythmMachine::action(int32_t id, int32_t iarg, float farg) {
    // Any edit (or audition) wakes the machine — a sleeping kit must catch the change.
    wake();
    if (id != A_SelectVoice && id != A_Audition) rev_.fetch_add(1, std::memory_order_relaxed);
    const int eb = editBank(); // edits land in the bank the param holds (the card's)
    switch (id) {
    case A_ToggleStep: {
        int v = iarg / kSteps, s = iarg % kSteps;
        if (valid(v, s)) {
            uint8_t& o = on_[eb][v][s];
            o = o ? 0 : 1;
            if (o && vel_[eb][v][s] == 0) vel_[eb][v][s] = 180;
        }
        break;
    }
    case A_SetVel: {
        int v = iarg / kSteps, s = iarg % kSteps;
        if (valid(v, s)) vel_[eb][v][s] = (uint8_t)std::clamp((int)std::lround(farg * 255.0f), 0, 255);
        break;
    }
    case A_ToggleAccent: {
        int v = iarg / kSteps, s = iarg % kSteps;
        if (valid(v, s)) acc_[eb][v][s] = acc_[eb][v][s] ? 0 : 1;
        break;
    }
    case A_SelectBank: // the Bank param (specs/100): MCP and older callers
        if (iarg >= 0 && iarg < kBanks) pn_[Bank].store(bankNorm(iarg), std::memory_order_relaxed);
        break;
    case A_ClearBank: {
        int b = (iarg >= 0 && iarg < kBanks) ? iarg : eb;
        for (int v = 0; v < kVoices; ++v)
            for (int s = 0; s < kSteps; ++s) {
                on_[b][v][s] = 0;
                acc_[b][v][s] = 0;
            }
        break;
    }
    case A_SelectVoice:
        if (iarg >= 0 && iarg < kVoices) selVoice_ = (uint8_t)iarg;
        break;
    case A_SetSource:
        if (iarg >= 0 && iarg < kVoices) src_[iarg] = (farg > 0.5f && smp_[iarg]) ? (uint8_t)Sample : (uint8_t)Synth;
        break;
    case A_CopyBank: { // iarg = src * kBanks + dst: the whole bank (steps, velocities, accents)
        int from = iarg / kBanks, to = iarg % kBanks;
        if (from >= 0 && from < kBanks && to >= 0 && to < kBanks && from != to) {
            std::memcpy(on_[to], on_[from], sizeof(on_[0]));
            std::memcpy(vel_[to], vel_[from], sizeof(vel_[0]));
            std::memcpy(acc_[to], acc_[from], sizeof(acc_[0]));
        }
        break;
    }
    case A_Audition: // play a voice now (the kit list, a pad click); consumed by render()
        if (iarg >= 0 && iarg < kVoices)
            pendingTrig_[iarg].store(std::clamp(farg, 0.0f, 1.0f), std::memory_order_relaxed);
        break;
    case A_ClearVoice:
        if (iarg >= 0 && iarg < kVoices)
            for (int s = 0; s < kSteps; ++s) {
                on_[eb][iarg][s] = 0;
                acc_[eb][iarg][s] = 0;
            }
        break;
    default: break;
    }
}

void RhythmMachine::setVoiceSample(int v, std::shared_ptr<SampleBuffer> b) {
    if (v < 0 || v >= kVoices) return;
    smp_[v] = std::move(b);
    src_[v] = smp_[v] ? (uint8_t)Sample : (uint8_t)Synth;
    wake();
}

int32_t RhythmMachine::voiceDeviceCount(int v) const {
    FxState* fx = fxStates_.live();
    return (fx && v >= 0 && v < kVoices) ? static_cast<int32_t>(fx->chain[v].size()) : 0;
}

Device* RhythmMachine::voiceDevice(int v, int d) const {
    FxState* fx = fxStates_.live();
    if (!fx || v < 0 || v >= kVoices || d < 0 || d >= static_cast<int>(fx->chain[v].size())) return nullptr;
    return fx->chain[v][d].get();
}

int32_t RhythmMachine::addVoiceDevice(int v, int32_t kind) {
    if (v < 0 || v >= kVoices || voiceDeviceCount(v) >= kMaxFx) return -1;
    auto dev = makeChainDevice(kind);
    if (!dev) return -1;
    dev->setSampleRate(sampleRate_, kChunk);
    auto ns = copyFx();
    ns->chain[v].push_back(std::move(dev));
    const int32_t idx = static_cast<int32_t>(ns->chain[v].size()) - 1;
    commitFx(ns);
    wake();
    return idx;
}

bool RhythmMachine::removeVoiceDevice(int v, int d) {
    auto ns = copyFx();
    if (v < 0 || v >= kVoices || d < 0 || d >= static_cast<int>(ns->chain[v].size())) return false;
    ns->chain[v].erase(ns->chain[v].begin() + d);
    auto& mv = ns->maps; // a mapping follows its device: the removed one's go, later ones shift
    for (auto it = mv.begin(); it != mv.end();) {
        if (it->voice == v && it->device == d) {
            it = mv.erase(it);
            continue;
        }
        if (it->voice == v && it->device > d) --it->device;
        ++it;
    }
    commitFx(ns);
    wake();
    return true;
}

bool RhythmMachine::moveVoiceDevice(int v, int from, int to) {
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
        else if (from < to && m.device > from && m.device <= to)
            --m.device;
        else if (to < from && m.device >= to && m.device < from)
            ++m.device;
    }
    commitFx(ns);
    wake();
    return true;
}

void RhythmMachine::clearVoiceDevices(int v) {
    auto ns = copyFx();
    if (v < 0 || v >= kVoices || ns->chain[v].empty()) return;
    ns->chain[v].clear();
    std::erase_if(ns->maps, [v](const MacroMap& m) { return m.voice == v && m.device >= 0; });
    commitFx(ns);
    wake();
}

std::string RhythmMachine::macroName(int m) const {
    if (m < 0 || m >= kMacros) return {};
    return macroNames_[m].empty() ? "Macro " + std::to_string(m + 1) : macroNames_[m];
}

int32_t RhythmMachine::addMacroMapping(int macro, int voice, int device, int param, float lo, float hi) {
    if (macro < 0 || macro >= kMacros) return -1;
    if (device < 0) {
        if (param < 0 || param >= Macro0) return -1;
    } else if (!voiceDevice(voice, device) || param < 0 || param >= voiceDevice(voice, device)->paramCount())
        return -1;
    auto ns = copyFx();
    ns->maps.push_back({macro, device < 0 ? paramVoice(param) : voice, device, param, lo, hi, 0});
    const int32_t idx = static_cast<int32_t>(ns->maps.size()) - 1;
    commitFx(ns);
    applyMacro(macro, get(Macro0 + macro));
    wake();
    return idx;
}

bool RhythmMachine::macroMapping(int i, MacroMap& out) const {
    FxState* fx = fxStates_.live();
    if (!fx || i < 0 || i >= static_cast<int>(fx->maps.size())) return false;
    out = fx->maps[i];
    return true;
}

bool RhythmMachine::removeMacroMapping(int i) {
    auto ns = copyFx();
    if (i < 0 || i >= static_cast<int>(ns->maps.size())) return false;
    ns->maps.erase(ns->maps.begin() + i);
    commitFx(ns);
    wake();
    return true;
}

bool RhythmMachine::setMacroMappingRange(int i, float lo, float hi) {
    auto ns = copyFx();
    if (i < 0 || i >= static_cast<int>(ns->maps.size())) return false;
    ns->maps[i].lo = lo;
    ns->maps[i].hi = hi;
    const int m = ns->maps[i].macro;
    commitFx(ns);
    applyMacro(m, get(Macro0 + m));
    wake();
    return true;
}

bool RhythmMachine::setMacroMappingCurve(int i, int curve) {
    auto ns = copyFx();
    if (i < 0 || i >= static_cast<int>(ns->maps.size())) return false;
    ns->maps[i].curve = std::clamp(curve, 0, 3);
    const int m = ns->maps[i].macro;
    commitFx(ns);
    applyMacro(m, get(Macro0 + m));
    wake();
    return true;
}

void RhythmMachine::clearMacros() {
    auto ns = copyFx();
    ns->maps.clear();
    commitFx(ns);
    for (auto& n : macroNames_) n.clear();
    wake();
}

std::vector<uint8_t> RhythmMachine::getState() const {
    std::vector<uint8_t> b;
    b.reserve(8 + kNumParams * 4 + kBanks * kVoices * kSteps * 3);
    putU32(b, kMagic);
    for (int i = 0; i < kNumParams; ++i) {
        float v = pn_[i].load(std::memory_order_relaxed);
        uint8_t t[4];
        std::memcpy(t, &v, 4);
        b.insert(b.end(), t, t + 4);
    }
    b.push_back(static_cast<uint8_t>(editBank())); // kept for older readers; RTH4 reads the param
    b.push_back(selVoice_);
    b.push_back(0);
    b.push_back(0);
    for (int bk = 0; bk < kBanks; ++bk)
        for (int v = 0; v < kVoices; ++v)
            for (int s = 0; s < kSteps; ++s) {
                b.push_back(on_[bk][v][s]);
                b.push_back(vel_[bk][v][s]);
                b.push_back(acc_[bk][v][s]);
            }
    for (int v = 0; v < kVoices; ++v) b.push_back(src_[v]); // per-voice source (buffers persist separately)
    // Voice FX + kit label, tagged "RFX1": per voice a device count, then per device its
    // builtinKind, bypass flag and params; then the kit label.
    putU32(b, kFxMagic);
    FxState* fx = fxStates_.live();
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
            for (int32_t p = 0; p < pc; ++p) {
                float x = dev->getParam(p);
                uint32_t u;
                std::memcpy(&u, &x, 4);
                putU32(b, u);
            }
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
        putU32(b, (uint32_t)m.macro);
        putU32(b, (uint32_t)m.voice);
        putU32(b, (uint32_t)m.device);
        putU32(b, (uint32_t)m.param);
        uint32_t u;
        std::memcpy(&u, &m.lo, 4);
        putU32(b, u);
        std::memcpy(&u, &m.hi, 4);
        putU32(b, u);
        putU32(b, (uint32_t)m.curve);
    }
    for (int m = 0; m < kMacros; ++m) {
        putU32(b, static_cast<uint32_t>(macroNames_[m].size()));
        b.insert(b.end(), macroNames_[m].begin(), macroNames_[m].end());
    }
    return b;
}

void RhythmMachine::setState(const uint8_t* data, int32_t size) {
    if (!data || size < 4) return;
    int off = 0;
    uint32_t magic = 0;
    for (int i = 0; i < 4; ++i) magic |= (uint32_t)data[i] << (i * 8);
    off = 4;
    int nParams;
    if (magic == kMagic) nParams = kNumParams;
    else if (magic == kMagicV3)
        nParams = kNumParamsV3;
    else if (magic == kMagicV2)
        nParams = kNumParamsV2;
    else if (magic == kMagicV1)
        nParams = kLegacyParams;
    else
        return; // unknown — keep defaults
    rev_.fetch_add(1, std::memory_order_relaxed);
    for (int i = 0; i < nParams && off + 4 <= size; ++i, off += 4) {
        float v;
        std::memcpy(&v, data + off, 4);
        if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    if (magic != kMagic) { // before the Bank params: the bank byte below, switching per step
        pn_[Bank].store(0.0f, std::memory_order_relaxed);
        pn_[BankSwitch].store(0.0f, std::memory_order_relaxed);
    }
    if (off + 2 <= size) {
        if (magic != kMagic) pn_[Bank].store(bankNorm(std::min<int>(data[off], kBanks - 1)), std::memory_order_relaxed);
        selVoice_ = (uint8_t)std::min<int>(data[off + 1], kVoices - 1);
        off += 4;
    }
    for (int bk = 0; bk < kBanks; ++bk)
        for (int v = 0; v < kVoices; ++v)
            for (int s = 0; s < kSteps; ++s) {
                if (off + 3 > size) return;
                on_[bk][v][s] = data[off];
                vel_[bk][v][s] = data[off + 1];
                acc_[bk][v][s] = data[off + 2];
                off += 3;
            }
    for (int v = 0; v < kVoices; ++v)
        if (off < size) src_[v] = data[off++]; // source flags (buffers reloaded by the project)
    // Voice FX + kit label. A blob from before them simply ends here: the voices keep no FX.
    auto ns = std::make_shared<FxState>();
    std::string kit;
    auto getU32 = [&](uint32_t& out) {
        if (off + 4 > size) return false;
        out = 0;
        for (int i = 0; i < 4; ++i) out |= (uint32_t)data[off + i] << (i * 8);
        off += 4;
        return true;
    };
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
                    uint32_t u = 0;
                    if (!getU32(u)) break;
                    float x;
                    std::memcpy(&x, &u, 4);
                    if (dev && std::isfinite(x)) dev->setParam(static_cast<int32_t>(p), x);
                }
                if (dev) {
                    dev->paramsRestored(static_cast<int32_t>(pc));
                    dev->setBypassed(byp);
                    if ((int)ns->chain[v].size() < kMaxFx) ns->chain[v].push_back(std::move(dev));
                }
            }
        }
        uint32_t kl = 0;
        if (getU32(kl) && kl <= 64 && off + (int)kl <= size) {
            kit.assign(reinterpret_cast<const char*>(data + off), kl);
            off += (int)kl;
        }
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
                m.macro = (int32_t)f[0];
                m.voice = (int32_t)f[1];
                m.device = (int32_t)f[2];
                m.param = (int32_t)f[3];
                std::memcpy(&m.lo, &f[4], 4);
                std::memcpy(&m.hi, &f[5], 4);
                m.curve = std::clamp((int32_t)f[6], 0, 3);
                const bool target =
                    m.device < 0 ? (m.param >= 0 && m.param < Macro0)
                                 : (m.voice >= 0 && m.voice < kVoices && m.device < (int)ns->chain[m.voice].size());
                if (m.macro >= 0 && m.macro < kMacros && target && std::isfinite(m.lo) && std::isfinite(m.hi))
                    ns->maps.push_back(m);
            }
        for (int m = 0; m < kMacros; ++m) {
            uint32_t l = 0;
            if (!getU32(l) || l > 32 || off + (int)l > size) break;
            names[m].assign(reinterpret_cast<const char*>(data + off), l);
            off += (int)l;
        }
    }
    commitFx(ns);
    kit_ = kit;
    for (int m = 0; m < kMacros; ++m) macroNames_[m] = names[m];
    wake();
}

std::shared_ptr<Instrument> RhythmMachine::clone() const {
    auto r = std::make_shared<RhythmMachine>();
    for (int i = 0; i < kNumParams; ++i)
        r->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    std::memcpy(r->on_, on_, sizeof(on_));
    std::memcpy(r->vel_, vel_, sizeof(vel_));
    std::memcpy(r->acc_, acc_, sizeof(acc_));
    // A running bank switch carries over (a sample load swaps in this copy mid-song): the bank
    // playing and the last step, so a bank queued in Pattern mode still waits for its cycle.
    r->playBank_.store(playBank_.load(std::memory_order_relaxed), std::memory_order_relaxed);
    r->lastTrigStep_.store(lastTrigStep_.load(std::memory_order_relaxed), std::memory_order_relaxed);
    r->selVoice_ = selVoice_;
    r->rev_.store(rev_.load(std::memory_order_relaxed) + 1, std::memory_order_relaxed);
    for (int v = 0; v < kVoices; ++v) {
        r->smp_[v] = smp_[v];
        r->src_[v] = src_[v];
    } // share sample buffers (immutable)
    r->setSampleRate(sampleRate_);
    // The voice FX are copied device by device (kind + params + bypass): a clone must not
    // share DSP state with the original, which keeps playing until the swap.
    auto ns = std::make_shared<FxState>();
    if (FxState* fx = fxStates_.live())
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
    if (FxState* fx = fxStates_.live()) ns->maps = fx->maps;
    r->commitFx(ns);
    r->kit_ = kit_;
    for (int m = 0; m < kMacros; ++m) r->macroNames_[m] = macroNames_[m];
    return r;
}

int32_t RhythmMachine::scopeRead(float* out, int32_t maxN) const {
    const int n = std::min(maxN, (int)kScopeN);
    for (int i = 0; i < n; ++i) out[i] = scope_[i].load(std::memory_order_relaxed);
    // The playing bank: in Step mode the param's (the steps take it at the next one, so the two
    // differ for a step at most), so the card follows a click or a lane without waiting for a
    // render; in Pattern mode the bank the steps play, which trails a queued pick until the
    // pattern start — the param's again when stopped.
    if (n > S_Bank && (out[S_Bank] < 0.0f || get(BankSwitch) < 0.5f)) out[S_Bank] = (float)editBank();
    if (n > S_Rev) out[S_Rev] = (float)(rev_.load(std::memory_order_relaxed) & 0xFFFFFF);
    return n;
}

void RhythmMachine::noteOn(int32_t pitch, float velocity) {
    wake();
    for (int v = 0; v < kVoices; ++v)
        if (kMidiMap[v] == pitch) {
            triggerVoice(v, velocity);
            return;
        }
}

void RhythmMachine::allNotesOff() {
    wake();
    vdsp_.allOff();
}

void RhythmMachine::render(float* out, int32_t frames) {
    const SnapshotHistory<FxState>::RenderScope done(fxStates_);
    const int32_t tapAt = taps_.take(frames); // this piece's first frame in the block's key buffers
    if (idle_) {
        // Mirror the sequencer clock over the skipped span (no step can trigger while
        // idle — a sounding step would have kept us awake — but arming and the step
        // display must resume exactly where the transport is).
        if (playing_ && spb_ > 0.0) {
            const float swing = get(Swing);
            const double stepBeats = 0.25; // 1/16 (mirrors the clock below)
            for (int32_t i = 0; i < frames; ++i) {
                const double beat = beatStart_ + (double)i / spb_;
                double pos = beat / stepBeats; // in 16th-steps
                long absStep = (long)std::floor(pos);
                int barStep = (int)(((absStep % kSteps) + kSteps) % kSteps);
                double frac = pos - std::floor(pos); // 0..1 within the step
                double trigFrac = (barStep & 1) ? (double)swing * 0.5 : 0.0;
                if (absStep != lastAbsStep_) {
                    lastAbsStep_ = absStep;
                    stepArmed_ = true;
                }
                if (stepArmed_ && frac >= trigFrac) {
                    stepArmed_ = false;
                    curStep_ = barStep;
                    stepBank(absStep, barStep); // the bank follows its lane while asleep too
                }
            }
        } else {
            curStep_ = -1;
        }
        scope_[S_Bank].store(playing_ ? (float)playBank_.load(std::memory_order_relaxed) : -1.0f, std::memory_order_relaxed);
        advanceClock(frames);
        return;
    }
    const float swing = get(Swing), human = get(Humanize);
    const float accentG = 1.0f + get(Accent) * 1.0f; // up to +6 dB-ish
    const float vol = get(Volume) * 1.2f;
    const double stepBeats = 0.25; // 1/16

    int flash[kVoices] = {0};
    // Auditions from the editor (message thread → here, lock-free).
    for (int v = 0; v < kVoices; ++v) {
        float a = pendingTrig_[v].exchange(-1.0f, std::memory_order_relaxed);
        if (a >= 0.0f) {
            triggerVoice(v, a);
            flash[v] = 1;
        }
    }
    // Glue: a gentle feed-forward bus compressor on the kit sum (peak detector, 5 ms
    // attack, 150 ms release) — threshold −8 → −24 dB and ratio 1.5 → 4 as Glue rises.
    // The makeup follows 70 % of the average reduction (a ~0.7 s average), so the tails come
    // up while the hits never end louder than they went in. 0 = out of circuit.
    const float glue = get(Glue);
    const bool glueOn = glue > 0.001f;
    const float thrDb = -8.0f - glue * 16.0f, ratio = 1.5f + glue * 2.5f;
    const float atk = std::exp(-1.0f / (0.005f * (float)sampleRate_)),
                rel = std::exp(-1.0f / (0.150f * (float)sampleRate_));
    const float avgC = std::exp(-1.0f / (0.7f * (float)sampleRate_));
    float grMax = 0.0f;
    // Voices with an FX chain render into their own buffer and run through it; the rest
    // go straight to the sum. Chunks keep the buffers small and the chain's block bounded.
    FxState* fx = fxStates_.live();
    bool hasFx[kVoices] = {false};
    bool anyFx = false;
    if (fx)
        for (int v = 0; v < kVoices; ++v) {
            for (auto& d : fx->chain[v])
                if (d && !d->bypassed()) {
                    hasFx[v] = true;
                    break;
                }
            anyFx = anyFx || hasFx[v];
        }
    // Per-block voice param snapshots (floats cross into the voice DSP by value).
    RhythmVoiceDsp::Params vp[kVoices];
    for (int v = 0; v < kVoices; ++v) vp[v] = voiceParams(v);
    // Voices a sidechain key listens to (specs/050): where their Pre / Post go this block.
    // A keyed voice renders into its own buffer like a voice with FX does, and its Pre / Post
    // are taken from that buffer once per chunk — the per-sample voice loop stays as it is.
    const PartTap* tap[kVoices] = {};
    bool own[kVoices] = {false};
    bool anyOwn = anyFx;
    for (int v = 0; v < kVoices; ++v) own[v] = hasFx[v];
    for (int32_t t = 0; t < taps_.count(); ++t)
        if (const int32_t v = partIndex(taps_.at(t).part); v >= 0) {
            tap[v] = &taps_.at(t);
            own[v] = anyOwn = true;
        }
    float ownPeak = 0.0f;
    for (int32_t base = 0; base < frames; base += kChunk) {
        const int32_t n = std::min<int32_t>(kChunk, frames - base);
        std::memset(mix_, 0, sizeof(float) * n * 2);
        if (anyOwn)
            for (int v = 0; v < kVoices; ++v)
                if (own[v]) std::memset(vbuf_[v], 0, sizeof(float) * n * 2);
        for (int32_t j = 0; j < n; ++j) {
            const int32_t i = base + j;
            // --- sequencer clock ---
            if (playing_ && spb_ > 0.0) {
                const double beat = beatStart_ + (double)i / spb_;
                double pos = beat / stepBeats; // in 16th-steps
                long absStep = (long)std::floor(pos);
                int barStep = (int)(((absStep % kSteps) + kSteps) % kSteps);
                double frac = pos - std::floor(pos);                         // 0..1 within the step
                double trigFrac = (barStep & 1) ? (double)swing * 0.5 : 0.0; // swing delays odd 16ths
                if (absStep != lastAbsStep_) {
                    lastAbsStep_ = absStep;
                    stepArmed_ = true;
                }
                if (stepArmed_ && frac >= trigFrac) {
                    stepArmed_ = false;
                    curStep_ = barStep;
                    const int bk = stepBank(absStep, barStep);
                    for (int v = 0; v < kVoices; ++v)
                        if (on_[bk][v][barStep]) {
                            float vv = vel_[bk][v][barStep] / 255.0f;
                            if (acc_[bk][v][barStep]) vv = std::min(1.0f, vv * accentG);
                            if (human > 0.0f)
                                vv = std::clamp(vv + (vdsp_.noiseF() * 2.0f - 1.0f) * human * 0.25f, 0.0f, 1.0f);
                            triggerVoice(v, vv);
                            flash[v] = 1;
                        }
                }
            } else {
                curStep_ = -1;
            }

            // --- voices ---
            for (int v = 0; v < kVoices; ++v) {
                if (!vdsp_.voices[v].active) continue;
                float sl, sr;
                vdsp_.voiceStereo(v, sl, sr, vp[v], smp_[v].get());
                if (!std::isfinite(sl) || !std::isfinite(sr)) {
                    sl = sr = 0.0f;
                    vdsp_.voices[v].active = false;
                }
                const float pan = std::clamp(get2(v, Pan) * 2.0f - 1.0f, -1.0f, 1.0f);
                const float gl = (pan <= 0.0f ? 1.0f : 1.0f - pan);
                const float gr = (pan >= 0.0f ? 1.0f : 1.0f + pan);
                float* dst = own[v] ? vbuf_[v] : mix_;
                dst[j * 2] += sl * gl;
                dst[j * 2 + 1] += sr * gr;
            }
        }

        // --- voice FX: every chain runs each chunk, so its tail rings out after the hit ---
        // (and keyed voices: Pre before the FX, Post after them)
        if (anyOwn)
            for (int v = 0; v < kVoices; ++v) {
                if (!own[v]) continue;
                if (tap[v])
                    for (int32_t k = 0; k < n * 2; ++k) tap[v]->pre[(tapAt + base) * 2 + k] += vbuf_[v][k];
                if (hasFx[v])
                    for (auto& d : fx->chain[v])
                        if (d && !d->bypassed()) d->process(vbuf_[v], n);
                for (int32_t k = 0; k < n * 2; ++k) mix_[k] += std::isfinite(vbuf_[v][k]) ? vbuf_[v][k] : 0.0f;
                if (tap[v])
                    for (int32_t k = 0; k < n * 2; ++k)
                        tap[v]->post[(tapAt + base) * 2 + k] += std::isfinite(vbuf_[v][k]) ? vbuf_[v][k] : 0.0f;
            }

        // --- bus: glue + volume ---
        for (int32_t j = 0; j < n; ++j) {
            float l = mix_[j * 2], r = mix_[j * 2 + 1];
            if (glueOn) {
                const float pk = std::max(std::fabs(l), std::fabs(r));
                const float lvlDb = pk > 1e-6f ? 20.0f * std::log10(pk) : -120.0f;
                const float over = std::max(0.0f, lvlDb - thrDb);
                const float target = over * (1.0f - 1.0f / ratio); // dB of reduction wanted
                const float c = target > glueGr_ ? atk : rel;
                glueGr_ = target + c * (glueGr_ - target);
                glueAvg_ = glueGr_ + avgC * (glueAvg_ - glueGr_);
                const float g = std::pow(10.0f, (std::min(glueAvg_ * 0.7f, glueGr_) - glueGr_) * 0.05f);
                l *= g;
                r *= g;
                grMax = std::max(grMax, glueGr_);
            } else {
                glueGr_ = 0.0f;
                glueAvg_ = 0.0f;
            }
            ownPeak = std::max(ownPeak, std::max(std::fabs(l), std::fabs(r)));
            out[(base + j) * 2] += l * vol;
            out[(base + j) * 2 + 1] += r * vol;
        }
    }

    // publish telemetry
    scope_[S_Step].store((float)(playing_ ? curStep_ : -1), std::memory_order_relaxed);
    scope_[S_Bank].store(playing_ ? (float)playBank_.load(std::memory_order_relaxed) : -1.0f, std::memory_order_relaxed);
    advanceClock(frames);
    scope_[S_Glue].store(grMax, std::memory_order_relaxed);
    int active = 0;
    for (int v = 0; v < kVoices; ++v) {
        float f = scope_[S_Flash0 + v].load(std::memory_order_relaxed) * 0.6f;
        if (flash[v]) f = 1.0f;
        scope_[S_Flash0 + v].store(f, std::memory_order_relaxed);
        const RhythmVoiceDsp::Voice& vc = vdsp_.voices[v];
        if (vc.active) ++active;
        float pos = -1.0f;
        if (vc.active && vc.splay && smp_[v] && smp_[v]->frames > 0) pos = (float)(vc.spos / (double)smp_[v]->frames);
        scope_[S_Pos0 + v].store(pos, std::memory_order_relaxed);
    }
    active_.store(active, std::memory_order_relaxed);

    // Auto-sleep: one-shot voices end and envelopes decay above; a chain that cannot
    // report idle (or a frozen loop, which never does) blocks sleep, so FX tails ring out.
    // The gate is the voices' active flag — not audibility. Non-snare synth voices latch
    // active after dying (env2 never decays for them) while still consuming the shared
    // noise stream, so an audibility gate froze the stream and changed later hits
    // (measured −14.8 dB on wake; reverted). Flag-gated sleep engages for fresh,
    // snare-only and sample-voice kits; the silent-bus hangover still guards every tail.
    bool fxIdle = true;
    if (fx)
        for (int v = 0; v < kVoices && fxIdle; ++v)
            for (auto& d : fx->chain[v])
                if (d && !d->bypassed() && !d->isIdle()) {
                    fxIdle = false;
                    break;
                }
    if (active == 0 && fxIdle && ownPeak <= 1.0e-5f) {
        quietSamples_ += static_cast<uint64_t>(frames);
        if (quietSamples_ > kIdleSamples) idle_ = true;
    } else {
        quietSamples_ = 0;
        idle_ = false;
    }
}

int RhythmMachine::paramVoice(int p) {
    if (p < kVoices * kPV) return p / kPV;
    if (p >= kExtra && p < Glue) return (p - kExtra) / kPX;
    return -1;
}

float RhythmMachine::macroCurve(float v, int32_t curve) {
    v = std::clamp(v, 0.0f, 1.0f);
    switch (curve) {
    case 1: return v * v;
    case 2: return std::sqrt(v);
    case 3: return v * v * (3.0f - 2.0f * v);
    default: return v;
    }
}

void RhythmMachine::applyMacro(int m, float value) {
    FxState* fx = fxStates_.live();
    if (!fx) return;
    for (const auto& mp : fx->maps) {
        if (mp.macro != m) continue;
        const float t = mp.lo + macroCurve(value, mp.curve) * (mp.hi - mp.lo);
        if (mp.device < 0) {
            if (mp.param >= 0 && mp.param < Macro0)
                pn_[mp.param].store(std::clamp(t, 0.0f, 1.0f), std::memory_order_relaxed);
        } else if (mp.voice >= 0 && mp.voice < kVoices && mp.device < static_cast<int>(fx->chain[mp.voice].size())) {
            if (auto& d = fx->chain[mp.voice][mp.device]) d->setParam(mp.param, t);
        }
    }
}

void RhythmMachine::commitFx(std::shared_ptr<FxState> ns) {
    fxAuthoring_ = ns;
    fxStates_.publish(std::move(ns));
}

} // namespace nota
