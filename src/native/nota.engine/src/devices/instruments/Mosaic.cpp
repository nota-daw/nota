// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic implementation. Read head, loop/crossfade handling, glide, zone thresholds, round-robin and the
// TPT
// SVF stay std::-exact — no shared-primitive migration. Silent voices auto-sleep via
// isIdle() after a hangover; release tails and one-shot ends are honored before sleep.

#include "Mosaic.h"

namespace nota {

Mosaic::Mosaic() {
    pn_[Volume].store(1.0f);
    pn_[Pan].store(0.5f);
    pn_[Transpose].store(0.5f);
    pn_[Detune].store(0.5f);
    pn_[Attack].store(0.0f);
    pn_[Decay].store(0.3f);
    pn_[Sustain].store(1.0f);
    pn_[Release].store(0.3f);
    pn_[FilterType].store(0.0f);
    pn_[Cutoff].store(1.0f);
    pn_[Resonance].store(0.0f);
    pn_[VoiceMode].store(0.0f);
    pn_[FilterKeyTrack].store(0.0f);
    pn_[VelAmount].store(0.7f);
    pn_[Glide].store(0.0f);
    pn_[PitchTrack].store(1.0f);
    pn_[EnvCutoff].store(0.0f);
    pn_[EnvAmount].store(0.75f);
    pn_[Output].store(0.79f);
    pn_[Polyphony].store(2.0f / 3.0f);      // 64
    pn_[PerKey].store(1.0f / 3.0f);         // 2
    pn_[OldFade].store(oldFadeNorm(0.020)); // 20 ms
    pn_[RelOn].store(1.0f);
    pn_[RelVol].store(static_cast<float>((-9.0 + 36.0) / 42.0)); // −9 dB on −36 … +6
    pn_[RelLen].store(0.25f);                                    // −6 dB after 2 s on 0 … −24
    pn_[VelCurve].store(0.5f);                                   // linear
    pn_[Pedal].store(1.0f);
    pn_[BendRange].store(2.0f / 24.0f);
    pn_[MpePressure].store(0.5f);
    pn_[MpeSlide].store(0.5f);
    pn_[View].store(0.0f); // L
    for (auto& v : voices_) v.active = false;
    for (int k = 0; k < 128; ++k) {
        noteOnAt_[k] = 0;
        noteVel_[k] = 0.0f;
    }
}

Mosaic::~Mosaic() {
    std::lock_guard<std::mutex> lk(holdMx_);
    if (pending_) pending_->cancel.store(true);
}

double Mosaic::velCurve(double vel, float c) {
    const double k = (std::clamp(c, 0.0f, 1.0f) - 0.5) * 2.0; // −1..+1
    const double e = std::pow(4.0, k);                        // 0.25 … 4
    return std::pow(std::clamp(vel, 0.0, 1.0), e);
}

void Mosaic::setProgram(const std::string& text) {
    auto p = mosaic::parseProgram(text);
    // Reuse what this instrument already holds (same resolved path) before the cache.
    std::shared_ptr<mosaic::Program> cur;
    {
        std::lock_guard<std::mutex> lk(holdMx_);
        cur = pending_;
    }
    if (cur) {
        std::map<std::string, std::shared_ptr<SampleBuffer>> have;
        {
            std::lock_guard<std::mutex> lk(cur->ownMx);
            for (size_t f = 0; f < cur->paths.size(); ++f)
                if (cur->owned[f]) have[cur->paths[f]] = cur->owned[f];
        }
        for (size_t f = 0; f < p->paths.size(); ++f) {
            auto it = have.find(p->paths[f]);
            if (it != have.end()) {
                mosaic::installSample(*p, f, it->second);
                p->done.fetch_add(1);
                p->bytesDone.fetch_add(p->diskBytes[f]);
            }
        }
        if (!cur->ready.load()) cur->cancel.store(true); // stop decoding what nobody plays
    }
    publish(p);
    mosaic::loadProgram(p);
    reap();
    wake();
}

std::string Mosaic::programText() const {
    std::lock_guard<std::mutex> lk(holdMx_);
    return pending_ ? pending_->text : std::string{};
}

Mosaic::Status Mosaic::status() {
    std::shared_ptr<mosaic::Program> p;
    {
        std::lock_guard<std::mutex> lk(holdMx_);
        p = pending_;
    }
    Status s;
    if (!p) {
        reap();
        return s;
    }
    s.state = p->paths.empty() ? 0 : (p->ready.load(std::memory_order_acquire) ? 2 : 1);
    s.filesDone = p->done.load();
    s.filesTotal = static_cast<int32_t>(p->paths.size());
    s.missing = p->missing.load();
    s.zones = static_cast<int32_t>(p->zones.size());
    s.serial = p->serial;
    s.diskDone = p->bytesDone.load();
    s.diskTotal = p->diskTotal;
    s.ram = p->ramBytes.load();
    reap();
    return s;
}

std::shared_ptr<SampleBuffer> Mosaic::zoneSample(int32_t zone) const {
    std::lock_guard<std::mutex> lk(holdMx_);
    if (!pending_ || zone < 0 || zone >= static_cast<int32_t>(pending_->zones.size())) return nullptr;
    const int32_t f = pending_->zones[zone].file;
    std::lock_guard<std::mutex> lk2(pending_->ownMx);
    return (f >= 0 && f < static_cast<int32_t>(pending_->owned.size())) ? pending_->owned[f] : nullptr;
}

std::vector<std::shared_ptr<SampleBuffer>> Mosaic::samples() const {
    std::lock_guard<std::mutex> lk(holdMx_);
    std::vector<std::shared_ptr<SampleBuffer>> out;
    if (!pending_) return out;
    std::lock_guard<std::mutex> lk2(pending_->ownMx);
    for (auto& b : pending_->owned)
        if (b) out.push_back(b);
    return out;
}

std::vector<uint8_t> Mosaic::getState() const {
    const std::string text = programText();
    std::vector<uint8_t> out;
    auto put32 = [&](uint32_t v) {
        for (int i = 0; i < 4; ++i) out.push_back(static_cast<uint8_t>(v >> (8 * i)));
    };
    put32(0x43534F4Du); // "MOSC"
    put32(1);
    put32(kNumParams);
    for (int i = 0; i < kNumParams; ++i) {
        float f = pn_[i].load(std::memory_order_relaxed);
        uint32_t u;
        std::memcpy(&u, &f, 4);
        put32(u);
    }
    put32(static_cast<uint32_t>(text.size()));
    out.insert(out.end(), text.begin(), text.end());
    return out;
}

void Mosaic::setState(const uint8_t* data, int32_t size) {
    if (!data || size < 12) return;
    size_t at = 0;
    auto get32 = [&](uint32_t& v) {
        if (at + 4 > static_cast<size_t>(size)) return false;
        v = 0;
        for (int i = 0; i < 4; ++i) v |= static_cast<uint32_t>(data[at + i]) << (8 * i);
        at += 4;
        return true;
    };
    uint32_t magic = 0, ver = 0, n = 0;
    if (!get32(magic) || magic != 0x43534F4Du || !get32(ver) || !get32(n)) return;
    for (uint32_t i = 0; i < n; ++i) {
        uint32_t u;
        if (!get32(u)) return;
        float f;
        std::memcpy(&f, &u, 4);
        if (i < static_cast<uint32_t>(kNumParams) && std::isfinite(f))
            pn_[i].store(std::clamp(f, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    uint32_t len = 0;
    if (!get32(len) || at + len > static_cast<size_t>(size)) return;
    setProgram(std::string(reinterpret_cast<const char*>(data + at), len));
}

std::shared_ptr<Instrument> Mosaic::clone() const {
    auto m = std::make_shared<Mosaic>();
    m->setSampleRate(sr_);
    for (int i = 0; i < kNumParams; ++i)
        m->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    std::shared_ptr<mosaic::Program> cur;
    {
        std::lock_guard<std::mutex> lk(holdMx_);
        cur = pending_;
    }
    if (cur) { // shares the decoded buffers through setProgram's reuse
        m->adopt(cur);
    }
    return m;
}

std::string Mosaic::pluginParamId(int32_t i) const {
    static const char* ids[kNumParams] = {"volume", "pan", "transpose", "detune", "attack", "decay", "sustain",
        "release", "filtertype", "cutoff", "resonance", "voicemode", "keytrack", "velamount", "glide", "pitchtrack",
        "envcutoff", "envamount", "output", "polyphony", "perkey", "oldfade", "relon", "relvol", "rellen", "velcurve",
        "pedal", "bendrange", "mpepressure", "mpeslide", "view"};
    return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
}

std::string Mosaic::pluginParamName(int32_t i) const {
    // Grouped by the head word in the automation menu; ids persist, names may change.
    static const char* nm[kNumParams] = {"Out Volume", "Out Pan", "Pitch Transpose", "Pitch Detune", "Env Attack",
        "Env Decay", "Env Sustain", "Env Release", "Filter Type", "Filter Cutoff", "Filter Resonance", "Voice Mode",
        "Filter Keytrack", "Voice Velocity", "Voice Glide", "Pitch Keytrack", "Filter Env On", "Filter Env Amount",
        "Out Level", "Voice Polyphony", "Voice Per Key", "Voice Old Fade", "Release Triggers", "Release Volume",
        "Release Length", "Voice Vel Curve", "Voice Pedal", "MPE Bend Range", "MPE Pressure", "MPE Slide", "View"};
    return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
}

int32_t Mosaic::pluginParamIndexOfId(const std::string& id) const {
    for (int32_t i = 0; i < kNumParams; ++i)
        if (pluginParamId(i) == id) return i;
    return -1;
}

int32_t Mosaic::scopeRead(float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    const int n = std::min<int>(maxN, kScope);
    for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
    if (n > 6) tele_[6].store(0.0f, std::memory_order_relaxed);
    return n;
}

void Mosaic::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch < 0) {
        if (dim == ExprSustain) cc64_ = value >= 0.5f;
        else
            gexpr_.set(dim, value);
        return;
    }
    if (dim < 0 || dim >= kExprDims) return;
    float add[kExprDims];
    gexpr_.offsets(static_cast<float>(bendSemis(get(BendRange))), add);
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.held) v.ex.set(dim, value, add);
}

void Mosaic::noteOn(int32_t pitch, float velocity) {
    wake();
    if (pitch < 0 || pitch > 127) return;
    const mosaic::Program* p = live();
    noteOnAt_[pitch] = clock_;
    noteVel_[pitch] = velocity;
    keyDown_[pitch] = true;
    lastNote_ = pitch;
    lastVel_ = velocity;
    if (!p || p->zones.empty()) return;
    const int vm = voiceModeOf();
    const bool glide = get(Glide) > 0.001f;
    const double from = glideFrom_ >= 0 ? static_cast<double>(glideFrom_) : static_cast<double>(pitch);
    glideFrom_ = pitch;
    if (vm == 1) {
        pushHeld(pitch);
        if (glide) { // legato: sounding voices slide, no new strike
            bool slid = false;
            for (auto& v : voices_)
                if (v.active && v.held && !v.relTrig && v.kill == 0) {
                    v.pitch = pitch;
                    slid = true;
                }
            if (slid) return;
        }
        for (auto& v : voices_)
            if (v.active && !v.relTrig && v.kill == 0) fade(v);
    } else if (vm == 2) {
        for (auto& v : voices_)
            if (v.active && v.kill == 0) fade(v, 0.004);
    }
    // Per key: older strikes of this key beyond the limit fade out.
    if (vm == 0) limitPerKey(pitch);
    const int vel7 = std::clamp(static_cast<int>(std::lround(velocity * 127.0f)), 0, 127);
    const uint32_t id = ++noteSerial_;
    spawn(*p, pitch, vel7, velocity, id, /*release*/ false, 1.0f, glide ? from : static_cast<double>(pitch));
}

void Mosaic::noteOff(int32_t pitch) {
    wake();
    if (pitch < 0 || pitch > 127) return;
    const int vm = voiceModeOf();
    if (vm == 1) {
        const bool top = heldN_ > 0 && held_[heldN_ - 1] == pitch;
        popHeld(pitch);
        if (top && heldN_ > 0 && get(Glide) > 0.001f) { // legato back to the key still down
            const int32_t back = held_[heldN_ - 1];
            bool slid = false;
            for (auto& v : voices_)
                if (v.active && v.held && v.pitch == pitch && v.kill == 0 && !v.relTrig) {
                    v.pitch = back;
                    slid = true;
                }
            if (slid) {
                glideFrom_ = back;
                return;
            }
        }
    }
    for (auto& v : voices_) {
        if (!v.active || v.pitch != pitch || !v.held || v.relTrig) continue;
        v.held = false;
        if (pedalDown_) v.sustained = true;
        else
            v.stage = Stage::Release;
    }
    // The release sample follows the key (or the pedal), even when the note has died away.
    if (keyDown_[pitch]) {
        keyDown_[pitch] = false;
        if (pedalDown_) pedalKeys_[pitch] = true;
        else
            releaseTrigger(pitch);
    }
}

void Mosaic::allNotesOff() {
    wake();
    for (auto& v : voices_) v.active = false;
    heldN_ = 0;
    cc64_ = false;
    pedalDown_ = false;
    gexpr_.reset();
    for (auto& k : pedalKeys_) k = false;
    for (auto& k : keyDown_) k = false;
}

void Mosaic::render(float* out, int32_t frames) {
    const mosaic::Program* p = live();
    if (idle_) {
        clock_ += frames;
        return;
    } // keep release-timing exact while asleep
    // A new program: voices keep sounding only if it still holds their sample (a zone
    // edit keeps the files) — a retired program's buffers live just two more renders.
    if (p != lastLive_) {
        for (auto& v : voices_) {
            if (!v.active) continue;
            bool kept = false;
            if (p)
                for (size_t f = 0; f < p->paths.size() && !kept; ++f)
                    kept = p->sample(static_cast<int32_t>(f)) == v.buf;
            if (!kept) v.active = false;
            else if (v.zone >= static_cast<int32_t>(p->zones.size()))
                v.zone = -1;
        }
        lastLive_ = p;
    }

    // The pedal: CC64 (when Pedal is on). Lifting it releases every held-over key.
    const bool pedal = get(Pedal) >= 0.5f && cc64_;
    if (pedalDown_ && !pedal) {
        for (auto& v : voices_)
            if (v.active && v.sustained) {
                v.sustained = false;
                v.stage = Stage::Release;
            }
        pedalDown_ = false;
        for (int k = 0; k < 128; ++k)
            if (pedalKeys_[k]) {
                pedalKeys_[k] = false;
                releaseTrigger(k);
            }
    }
    pedalDown_ = pedal;

    const double pitchOff = (get(Transpose) - 0.5) * 48.0 + (get(Detune) - 0.5) * 1.0;
    const double pTrack = get(PitchTrack);
    const float vol = get(Volume) * get(Output);
    const double pan = get(Pan) * 2.0 - 1.0;
    const float atkRate = static_cast<float>(1.0 / (expMap(get(Attack), 0.0005, 4.0) * sr_));
    const float decRate = static_cast<float>(1.0 / (expMap(get(Decay), 0.002, 6.0) * sr_));
    const float relRate = static_cast<float>(1.0 / (expMap(get(Release), 0.002, 6.0) * sr_));
    const float sustain = get(Sustain);
    const double glideSec = glideSeconds(get(Glide));
    const double glideK = glideSec > 0.0 ? 1.0 - std::exp(-static_cast<double>(kCtl) / (glideSec * sr_)) : 1.0;
    const int fType = std::clamp(static_cast<int>(std::lround(get(FilterType) * 3.0f)), 0, 3);
    const double baseCut = expMap(get(Cutoff), 20.0, 20000.0);
    const double kTrack = get(FilterKeyTrack);
    const double k = 2.0 - 1.9 * get(Resonance);
    const bool envOn = get(EnvCutoff) > 0.5f;
    const double envOct = envOn ? envOctaves(get(EnvAmount)) : 0.0;
    const float bendR = static_cast<float>(bendSemis(get(BendRange)));
    const float mpePress = get(MpePressure), mpeSlide = get(MpeSlide);
    float exAdd[kExprDims];
    gexpr_.offsets(bendR, exAdd);
    const float exCoef = exprSmoothCoef(sr_);

    int nActive = 0;
    float ownPeak = 0.0f;
    for (auto& v : voices_) {
        if (!v.active) continue;
        const SampleBuffer* sb = v.buf;
        if (!sb || sb->empty()) {
            v.active = false;
            continue;
        }
        ++nActive;
        const double srcRatio = sb->sourceSampleRate / sr_;
        const double loopLen = v.loopB - v.loopA;
        double inc = 0.0, a1 = 0, a2 = 0, a3 = 0;
        for (int32_t i = 0; i < frames; ++i) {
            v.ex.tick(exAdd, exCoef);
            if ((i % kCtl) == 0) {
                v.note += (v.pitch - v.note) * glideK;
                if (std::abs(v.pitch - v.note) < 1e-4) v.note = v.pitch;
                const double semis = (v.note - v.root) * pTrack + pitchOff + v.tuneSemis + v.ex.bend();
                inc = std::pow(2.0, semis / 12.0) * srcRatio;
                if (fType > 0) {
                    double fc = baseCut;
                    if (kTrack > 0.0) fc *= std::pow(2.0, kTrack * (v.note - 60.0) / 12.0);
                    if (envOn) fc *= std::pow(2.0, envOct * v.env);
                    const double slide = (v.ex.cur[ExprSlide] - 0.5) * 2.0; // −1..+1
                    fc *= std::pow(2.0, slide * (mpeSlide - 0.5) * 8.0);    // ±2 oct at the extremes
                    v.cutoff = std::clamp(fc, 20.0, sr_ * 0.49);
                    const double g = std::tan(kPi * v.cutoff / sr_);
                    a1 = 1.0 / (1.0 + g * (g + k));
                    a2 = g * a1;
                    a3 = g * a2;
                }
            }
            if (v.relTrig) { // a release sample plays through on its own level
                if (v.env < 1.0f) v.env = std::min(1.0f, v.env + atkRateRel_);
            } else
                switch (v.stage) {
                case Stage::Attack:
                    v.env += atkRate;
                    if (v.env >= 1.0f) {
                        v.env = 1.0f;
                        v.stage = Stage::Decay;
                    }
                    break;
                case Stage::Decay:
                    v.env -= decRate;
                    if (v.env <= sustain) {
                        v.env = sustain;
                        v.stage = Stage::Sustain;
                    }
                    break;
                case Stage::Sustain: break;
                case Stage::Release:
                    v.env -= relRate;
                    if (v.env <= 0.0f) {
                        v.env = 0.0f;
                        v.active = false;
                    }
                    break;
                }
            if (!v.active) break;
            float kg = 1.0f;
            if (v.kill > 0) {
                kg = static_cast<float>(v.kill) / static_cast<float>(v.killLen);
                if (--v.kill == 0) {
                    v.active = false;
                    break;
                }
            }

            float ls, rs;
            readHermite(*sb, v.pos, ls, rs);
            if (v.loopMode == 1 && v.xf > 1.0 && v.dir > 0 && v.pos > v.loopB - v.xf) {
                const double t = (v.pos - (v.loopB - v.xf)) / v.xf;
                float hl, hr;
                readHermite(*sb, v.pos - loopLen, hl, hr);
                ls = static_cast<float>(ls * (1.0 - t) + hl * t);
                rs = static_cast<float>(rs * (1.0 - t) + hr * t);
            }
            if (fType > 0) {
                ls = svf(v.ic1L, v.ic2L, ls, a1, a2, a3, k, fType);
                rs = svf(v.ic1R, v.ic2R, rs, a1, a2, a3, k, fType);
            }
            const float press = 1.0f + (v.ex.pressure() * (mpePress * 2.0f)); // up to +3 at full amount
            const float amp = v.env * v.level * vol * kg * press;
            out[i * 2] += ls * amp * v.gl;
            out[i * 2 + 1] += rs * amp * v.gr;
            ownPeak = std::max(ownPeak, std::max(std::fabs(ls * amp * v.gl), std::fabs(rs * amp * v.gr)));
            v.peak = std::max(v.peak * 0.9995f, std::abs(ls * amp));

            v.pos += inc * v.dir;
            if (v.loopMode == 1 && !v.relTrig) {
                if (v.pos >= v.loopB) v.pos -= loopLen;
            } else if (v.loopMode == 2 && !v.relTrig) {
                if (v.pos >= v.loopB) {
                    v.pos = v.loopB - (v.pos - v.loopB);
                    v.dir = -1;
                } else if (v.pos <= v.loopA && v.dir < 0) {
                    v.pos = v.loopA + (v.loopA - v.pos);
                    v.dir = 1;
                }
            } else {
                if (v.pos >= v.endF) {
                    v.active = false;
                    break;
                }
                // A one-shot's last 2 ms fade so a hard cut never clicks.
                const double left = (v.endF - v.pos) / std::max(1e-9, inc);
                if (left < fadeTail_ && v.kill == 0) {
                    v.kill = std::max(1, static_cast<int>(left));
                    v.killLen = v.kill;
                }
            }
        }
    }
    clock_ += frames;
    publishTelemetry(out, frames, nActive, pan);
    epoch_.fetch_add(1, std::memory_order_release);

    // Auto-sleep: release voices decay, one-shots end and kill-fades finish above, so no
    // active voice plus our own silent block means every tail has fully decayed.
    int still = 0;
    for (const auto& v : voices_)
        if (v.active) ++still;
    if (still == 0 && ownPeak <= 1.0e-5f) {
        quietSamples_ += static_cast<uint64_t>(frames);
        if (quietSamples_ > kIdleSamples) idle_ = true;
    } else {
        quietSamples_ = 0;
        idle_ = false;
    }
}

void Mosaic::adopt(const std::shared_ptr<mosaic::Program>& src) {
    // A clone: same text, same buffers — nothing to decode.
    auto p = mosaic::parseProgram(src->text);
    {
        std::lock_guard<std::mutex> lk(src->ownMx);
        for (size_t f = 0; f < p->paths.size() && f < src->owned.size(); ++f)
            if (src->owned[f]) {
                mosaic::installSample(*p, f, src->owned[f]);
                p->done.fetch_add(1);
                p->bytesDone.fetch_add(p->diskBytes[f]);
            }
    }
    publish(p);
    mosaic::loadProgram(p);
}

void Mosaic::publish(const std::shared_ptr<mosaic::Program>& p) {
    std::lock_guard<std::mutex> lk(holdMx_);
    if (pending_) retired_.emplace_back(epoch_.load(std::memory_order_acquire), pending_);
    pending_ = p;
    cur_.store(p.get(), std::memory_order_release);
}

const mosaic::Program* Mosaic::live() const {
    const mosaic::Program* p = cur_.load(std::memory_order_acquire);
    return p && p->ready.load(std::memory_order_acquire) ? p : nullptr;
}

void Mosaic::reap() {
    std::lock_guard<std::mutex> lk(holdMx_);
    const uint64_t now = epoch_.load(std::memory_order_acquire);
    retired_.erase(
        std::remove_if(retired_.begin(), retired_.end(), [&](auto& r) { return now >= r.first + 2; }), retired_.end());
}

void Mosaic::fade(Voice& v, double sec) {
    if (v.kill > 0) return;
    const double s = sec > 0 ? sec : oldFadeSec(get(OldFade));
    v.killLen = std::max(1, static_cast<int>(s * sr_));
    v.kill = v.killLen;
    v.held = false;
    v.sustained = false;
}

void Mosaic::limitPerKey(int32_t pitch) {
    const int lim = perKeyOf(get(PerKey));
    // Distinct strikes (note ids) of this key still sounding, oldest first.
    uint32_t ids[kPool];
    int n = 0;
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.kill == 0 && !v.relTrig) {
            bool seen = false;
            for (int i = 0; i < n; ++i)
                if (ids[i] == v.id) {
                    seen = true;
                    break;
                }
            if (!seen && n < kPool) ids[n++] = v.id;
        }
    std::sort(ids, ids + n);
    for (int i = 0; i + lim <= n; ++i) // keep lim − 1 old ones: the new strike is the lim-th
        for (auto& v : voices_)
            if (v.active && v.id == ids[i]) fade(v);
}

Mosaic::Voice* Mosaic::victim() {
    Voice* q = nullptr;
    auto rank = [](const Voice& v) { return v.held ? 2 : (v.sustained ? 1 : 0); };
    for (auto& v : voices_) {
        if (!v.active || v.kill > 0) continue;
        if (!q) {
            q = &v;
            continue;
        }
        if (rank(v) != rank(*q)) {
            if (rank(v) < rank(*q)) q = &v;
            continue;
        }
        if (std::abs(v.peak - q->peak) > 1e-4f) {
            if (v.peak < q->peak) q = &v;
            continue;
        }
        if (v.age < q->age) q = &v;
    }
    return q;
}

Mosaic::Voice* Mosaic::freeSlot() {
    for (auto& v : voices_)
        if (!v.active) return &v;
    Voice* q = &voices_[0]; // everything busy (fades included): the quietest outright
    for (auto& v : voices_)
        if (v.peak < q->peak) q = &v;
    return q;
}

float Mosaic::xfadeGain(int x, int inLo, int inHi, int outLo, int outHi) {
    double g = 1.0;
    if (inLo >= 0 && inHi > inLo) {
        if (x <= inLo) return 0.0f;
        if (x < inHi) g *= std::sin(0.5 * kPi * (x - inLo) / double(inHi - inLo));
    }
    if (outLo >= 0 && outHi > outLo) {
        if (x >= outHi) return 0.0f;
        if (x > outLo) g *= std::cos(0.5 * kPi * (x - outLo) / double(outHi - outLo));
    }
    return static_cast<float>(g);
}

void Mosaic::spawn(const mosaic::Program& p, int32_t pitch, int vel7, float velocity, uint32_t id, bool release,
    float relGain, double fromNote) {
    // One random number per event, shared by every group (SFZ lorand / hirand).
    rng_ = rng_ * 1664525u + 1013904223u;
    const float r = static_cast<float>((rng_ >> 8) & 0xFFFFFF) / 16777216.0f;
    // Round-robin: each group with matching zones steps once per event.
    const size_t ng = std::min(p.groups.size(), kGroups);
    bool touched[kGroups] = {};
    int32_t step[kGroups];
    for (size_t g = 0; g < ng; ++g) step[g] = 1;
    for (const auto& z : p.zones) {
        if ((z.trig == 1) != release || pitch < z.klo || pitch > z.khi || vel7 < z.vlo || vel7 > z.vhi) continue;
        const size_t g = static_cast<size_t>(z.group);
        if (g >= kGroups || touched[g]) continue;
        touched[g] = true;
        const auto& gr = p.groups[g];
        if (gr.seqLen <= 1) {
            step[g] = 1;
            continue;
        }
        if (gr.rrMode == 0) {
            rrStep_[g] = rrStep_[g] % gr.seqLen + 1;
            step[g] = rrStep_[g];
        } else {
            rng_ = rng_ * 1664525u + 1013904223u;
            int s = 1 + static_cast<int>((rng_ >> 8) % static_cast<uint32_t>(gr.seqLen));
            if (gr.rrMode == 2 && s == rrLast_[g]) s = s % gr.seqLen + 1;
            rrLast_[g] = s;
            step[g] = s;
        }
    }
    const double velAmt = get(VelAmount);
    const double curved = velCurve(velocity, get(VelCurve));
    const float velGain = static_cast<float>(velAmt * curved + (1.0 - velAmt));
    const int limit = polyOf(get(Polyphony));
    const double masterPan = get(Pan) * 2.0 - 1.0;
    float add[kExprDims];
    gexpr_.offsets(static_cast<float>(bendSemis(get(BendRange))), add);
    for (size_t zi = 0; zi < p.zones.size(); ++zi) {
        const auto& z = p.zones[zi];
        if ((z.trig == 1) != release || pitch < z.klo || pitch > z.khi || vel7 < z.vlo || vel7 > z.vhi) continue;
        const auto& gr = p.groups[static_cast<size_t>(z.group)];
        if (gr.seqLen > 1 && static_cast<size_t>(z.group) < kGroups && z.seq != step[z.group]) continue;
        if (z.rlo > 0.0f || z.rhi < 1.0f) {
            if (r < z.rlo || r >= z.rhi) continue;
        }
        const SampleBuffer* sb = p.sample(z.file);
        if (!sb || sb->empty()) continue;
        const float xg =
            xfadeGain(pitch, z.xkil, z.xkih, z.xkol, z.xkoh) * xfadeGain(vel7, z.xvil, z.xvih, z.xvol, z.xvoh);
        if (xg <= 0.0f) continue;
        // Exclusive groups: this zone chokes every voice whose off_by names its group.
        if (z.excl != 0)
            for (auto& v : voices_)
                if (v.active && v.zone >= 0 && v.zone < static_cast<int32_t>(p.zones.size()) &&
                    p.zones[v.zone].offBy == z.excl) {
                    if (p.zones[v.zone].offMode == 1) {
                        v.held = false;
                        v.sustained = false;
                        v.stage = Stage::Release;
                    } else
                        fade(v, 0.006);
                }
        // Make room within the polyphony.
        while (liveCount() >= limit) {
            Voice* q = victim();
            if (!q) break;
            fade(*q);
        }
        Voice& v = *freeSlot();
        const double N = static_cast<double>(sb->frames);
        v = Voice{};
        v.active = true;
        v.held = !release;
        v.relTrig = release;
        v.pitch = pitch;
        v.root = z.root;
        v.zone = static_cast<int32_t>(zi);
        v.id = id;
        v.age = ++ageSerial_;
        v.buf = sb;
        v.startF = std::clamp(z.start, 0.0, std::max(0.0, N - 1));
        v.endF = z.end > 0 ? std::clamp(z.end, v.startF + 1, N) : N;
        v.loopMode = z.loopMode;
        v.loopA = z.loopStart >= 0 ? std::clamp(z.loopStart, v.startF, v.endF - 1) : v.startF;
        v.loopB = z.loopEnd > 0 ? std::clamp(z.loopEnd, v.loopA + 1, v.endF) : v.endF;
        v.xf = std::clamp(static_cast<double>(z.xfade) * sb->sourceSampleRate, 0.0, (v.loopB - v.loopA) * 0.5);
        v.pos = v.startF;
        v.dir = 1;
        v.tuneSemis = (z.tune + gr.tune) / 100.0;
        v.note = release ? static_cast<double>(pitch) : fromNote;
        const double zg = std::pow(10.0, (z.gainDb + gr.gainDb) / 20.0);
        v.level = static_cast<float>(zg) * xg * velGain * relGain;
        const double pn = std::clamp(masterPan + z.pan, -1.0, 1.0);
        v.gl = static_cast<float>(std::cos((pn + 1.0) * kPi / 4.0));
        v.gr = static_cast<float>(std::sin((pn + 1.0) * kPi / 4.0));
        v.env = release ? 1.0f : 0.0f; // a release sample starts at its own level
        v.stage = Stage::Attack;
        v.ex.reset(add);
        v.peak = v.level;
    }
}

void Mosaic::releaseTrigger(int32_t pitch) {
    if (get(RelOn) < 0.5f) return;
    const mosaic::Program* p = live();
    if (!p) return;
    bool has = false;
    for (const auto& z : p->zones)
        if (z.trig == 1 && pitch >= z.klo && pitch <= z.khi) {
            has = true;
            break;
        }
    if (!has) return;
    const double heldSec = static_cast<double>(clock_ - noteOnAt_[pitch]) / sr_;
    const double db = relVolDb(get(RelVol)) + relLenDb(get(RelLen)) * std::clamp(heldSec / 2.0, 0.0, 1.0);
    const float g = static_cast<float>(std::pow(10.0, db / 20.0));
    const float vel = noteVel_[pitch];
    const int vel7 = std::clamp(static_cast<int>(std::lround(vel * 127.0f)), 0, 127);
    spawn(*p, pitch, vel7, vel, ++noteSerial_, true, g, pitch);
}

void Mosaic::readHermite(const SampleBuffer& b, double pos, float& l, float& r) {
    const int64_t i1 = static_cast<int64_t>(std::floor(pos));
    const float t = static_cast<float>(pos - static_cast<double>(i1));
    float l0, r0, l1, r1, l2, r2, l3, r3;
    b.readStereo(i1 - 1, l0, r0);
    b.readStereo(i1, l1, r1);
    b.readStereo(i1 + 1, l2, r2);
    b.readStereo(i1 + 2, l3, r3);
    auto h = [t](float y0, float y1, float y2, float y3) {
        const float c1 = 0.5f * (y2 - y0), c2 = y0 - 2.5f * y1 + 2.0f * y2 - 0.5f * y3,
                    c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
        return ((c3 * t + c2) * t + c1) * t + y1;
    };
    l = h(l0, l1, l2, l3);
    r = h(r0, r1, r2, r3);
}

float Mosaic::svf(double& ic1, double& ic2, float xf, double a1, double a2, double a3, double k, int type) {
    const double x = xf;
    const double v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
    ic1 = 2.0 * v1 - ic1;
    ic2 = 2.0 * v2 - ic2;
    if (type == 1) return static_cast<float>(v2);
    if (type == 2) return static_cast<float>(x - k * v1 - v2);
    return static_cast<float>(v1);
}

void Mosaic::publishTelemetry(const float* out, int32_t frames, int nActive, double /*pan*/) {
    float peak = 0.0f;
    for (int32_t i = 0; i < frames * 2; ++i) peak = std::max(peak, std::abs(out[i]));
    int held = 0, sus = 0, rel = 0;
    const Voice* best = nullptr;
    int n = 0;
    uint32_t keys[8] = {};
    for (auto& v : voices_) {
        if (!v.active) continue;
        if (v.relTrig) ++rel;
        else if (v.held)
            ++held;
        else if (v.sustained)
            ++sus;
        if ((v.held || v.sustained) && v.kill == 0 && v.pitch >= 0 && v.pitch < 128)
            keys[v.pitch >> 4] |= 1u << (v.pitch & 15);
        if (!best || v.peak * v.env > best->peak * best->env) best = &v;
        if (n < kScZones) {
            const int st = v.relTrig || v.kill > 0 || (!v.held && !v.sustained) ? 2 : (v.held ? 0 : 1);
            const float code = static_cast<float>(v.zone * 4 + st);
            bool dup = false;
            for (int i = 0; i < n; ++i)
                if (tele_[kScHead + i].load(std::memory_order_relaxed) == code) {
                    dup = true;
                    break;
                }
            if (!dup) tele_[kScHead + n++].store(code, std::memory_order_relaxed);
        }
    }
    tele_[0].store(static_cast<float>(nActive), std::memory_order_relaxed);
    tele_[1].store(static_cast<float>(polyOf(get(Polyphony))), std::memory_order_relaxed);
    tele_[2].store(static_cast<float>(held), std::memory_order_relaxed);
    tele_[3].store(static_cast<float>(sus), std::memory_order_relaxed);
    tele_[4].store(static_cast<float>(rel), std::memory_order_relaxed);
    tele_[5].store(pedalDown_ ? 1.0f : 0.0f, std::memory_order_relaxed);
    if (peak > tele_[6].load(std::memory_order_relaxed)) tele_[6].store(peak, std::memory_order_relaxed);
    const double bn = best && best->buf ? static_cast<double>(best->buf->frames) : 1.0;
    tele_[7].store(best ? static_cast<float>(best->pos / bn) : -1.0f, std::memory_order_relaxed);
    tele_[8].store(best ? best->env : 0.0f, std::memory_order_relaxed);
    tele_[9].store(best ? static_cast<float>(best->relTrig ? 3 : static_cast<int>(best->stage)) : -1.0f,
        std::memory_order_relaxed);
    tele_[10].store(best ? static_cast<float>(best->note) : -1.0f, std::memory_order_relaxed);
    const int fType = std::clamp(static_cast<int>(std::lround(get(FilterType) * 3.0f)), 0, 3);
    tele_[11].store(best && fType > 0 ? static_cast<float>(best->cutoff) : -1.0f, std::memory_order_relaxed);
    tele_[12].store(best ? static_cast<float>(best->zone) : -1.0f, std::memory_order_relaxed);
    tele_[13].store(static_cast<float>(lastNote_), std::memory_order_relaxed);
    tele_[14].store(static_cast<float>(std::lround(lastVel_ * 127.0f)), std::memory_order_relaxed);
    tele_[15].store(static_cast<float>(n), std::memory_order_relaxed);
    for (int i = 0; i < 8; ++i) tele_[kScKeys + i].store(static_cast<float>(keys[i]), std::memory_order_relaxed);
}

void Mosaic::pushHeld(int32_t p) {
    popHeld(p);
    if (heldN_ == kHeld) {
        std::memmove(held_, held_ + 1, sizeof(int32_t) * (kHeld - 1));
        --heldN_;
    }
    held_[heldN_++] = p;
}

void Mosaic::popHeld(int32_t p) {
    for (int i = 0; i < heldN_; ++i)
        if (held_[i] == p) {
            std::memmove(held_ + i, held_ + i + 1, sizeof(int32_t) * (heldN_ - i - 1));
            --heldN_;
            return;
        }
}

} // namespace nota
