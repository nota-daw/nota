// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Sampler implementation. Linear-interpolated read head, loop/crossfade handling, glide and the TPT SVF
// stay std::-exact. Silent voices auto-sleep via isIdle() after a hangover.

#include "Sampler.h"

namespace nota {

Sampler::Sampler() {
    pn_[Volume].store(1.0f);
    pn_[Pan].store(0.5f);
    pn_[Transpose].store(0.5f);
    pn_[Detune].store(0.5f); // 0 st / 0 cents
    pn_[Start].store(0.0f);
    pn_[End].store(1.0f);
    pn_[Reverse].store(0.0f);
    pn_[LoopMode].store(0.0f);
    pn_[LoopStart].store(0.0f);
    pn_[LoopEnd].store(1.0f);
    pn_[Attack].store(0.0f);
    pn_[Decay].store(0.3f);
    pn_[Sustain].store(1.0f);
    pn_[Release].store(0.06f);
    pn_[FilterType].store(0.0f);
    pn_[Cutoff].store(1.0f);
    pn_[Resonance].store(0.0f);
    pn_[VoiceMode].store(0.0f);
    pn_[LoopXfade].store(0.1f);
    pn_[FilterKeyTrack].store(0.0f);
    pn_[VelAmount].store(1.0f);
    pn_[Gain].store(0.5f);
    pn_[Glide].store(0.0f);
    pn_[PitchTrack].store(1.0f); // 0 dB · no glide · chromatic
    pn_[EnvCutoff].store(0.0f);
    pn_[EnvAmount].store(0.75f);
    pn_[Output].store(1.0f); // env → cutoff off (+3 oct when on)
}

int32_t Sampler::scopeRead(float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    const int n = std::min<int>(maxN, kTele);
    for (int i = 0; i < n; ++i) out[i] = tele_[i].load(std::memory_order_relaxed);
    if (n > 6) tele_[6].store(0.0f, std::memory_order_relaxed); // peak-hold: a read clears it
    return n;
}

void Sampler::setSample(std::shared_ptr<SampleBuffer> s, int32_t rootNote, bool loop) {
    sample_ = std::move(s);
    rootNote_.store(std::clamp(rootNote, 0, 127), std::memory_order_relaxed);
    // The loop flag only corrects a disagreeing mode: on → forward (0.5) unless already
    // looping (ping-pong 1.0 stays), off → one-shot. A reload keeps the loop kind.
    if (loop != loopEnabled()) pn_[LoopMode].store(loop ? 0.5f : 0.0f, std::memory_order_relaxed);
    wake();
}

std::shared_ptr<Instrument> Sampler::clone() const {
    auto s = std::make_shared<Sampler>();
    s->sample_ = sample_;
    s->rootNote_.store(rootNote_.load(std::memory_order_relaxed), std::memory_order_relaxed);
    for (int i = 0; i < kNumParams; ++i)
        s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
    s->setSampleRate(sampleRate_);
    return s;
}

std::string Sampler::pluginParamId(int32_t i) const {
    static const char* ids[] = {"volume", "pan", "transpose", "detune", "start", "end", "reverse", "loopmode",
        "loopstart", "loopend", "attack", "decay", "sustain", "release", "filtertype", "cutoff", "resonance",
        "voicemode", "loopxfade", "keytrack", "velamount", "gain", "glide", "pitchtrack", "envcutoff", "envamount",
        "output"};
    return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
}

std::string Sampler::pluginParamName(int32_t i) const {
    // Display names group by their head in the automation menu (Sample › Start,
    // Filter › Cutoff …); the ids are what persists, so names may change.
    static const char* nm[] = {"Out Volume", "Out Pan", "Pitch Transpose", "Pitch Detune", "Sample Start", "Sample End",
        "Sample Reverse", "Loop Mode", "Loop Start", "Loop End", "Env Attack", "Env Decay", "Env Sustain",
        "Env Release", "Filter Type", "Filter Cutoff", "Filter Resonance", "Voice Mode", "Loop Crossfade",
        "Filter Keytrack", "Voice Velocity", "Sample Gain", "Voice Glide", "Pitch Keytrack", "Filter Env On",
        "Filter Env Amount", "Out Level"};
    return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
}

void Sampler::noteOn(int32_t pitch, float velocity) {
    wake();
    if (!sample_ || sample_->empty()) return;
    const int vm = voiceModeOf();
    const bool glide = pn_[Glide].load(std::memory_order_relaxed) > 0.001f;
    const double from = lastNote_ >= 0 ? static_cast<double>(lastNote_) : static_cast<double>(pitch);
    lastNote_ = pitch;
    if (vm == 1) {
        pushHeld(pitch);
        if (glide) { // legato: the sounding voice keeps its place and envelope and slides
            for (auto& v : voices_)
                if (v.active && v.stage != Stage::Release) {
                    v.pitch = pitch;
                    v.velocity = velocity;
                    return;
                }
        }
        for (auto& v : voices_)
            if (v.active && v.stage != Stage::Release) v.stage = Stage::Release; // Mono: release others
    } else if (vm == 2) {
        for (auto& v : voices_) v.active = false; // Choke: hard cut
    }
    Voice* v = findFreeVoice();
    const double startF = std::clamp(pn_[Start].load(std::memory_order_relaxed), 0.0f, 1.0f) * sample_->frames;
    const double endF = std::clamp(pn_[End].load(std::memory_order_relaxed), 0.0f, 1.0f) * sample_->frames;
    const bool rev = pn_[Reverse].load(std::memory_order_relaxed) > 0.5f;
    v->active = true;
    v->stage = Stage::Attack;
    v->env = 0.0f;
    v->pitch = pitch;
    v->velocity = velocity;
    v->dir = rev ? -1 : 1;
    v->note = glide ? from : static_cast<double>(pitch);
    v->pos = rev ? std::max(startF, endF - 1.0) : startF;
    v->ic1L = v->ic2L = v->ic1R = v->ic2R = 0.0;
}

void Sampler::noteOff(int32_t pitch) {
    wake();
    if (voiceModeOf() == 1) {
        const bool top = heldN_ > 0 && held_[heldN_ - 1] == pitch;
        popHeld(pitch);
        // Legato glide back to the note still held underneath.
        if (top && heldN_ > 0 && pn_[Glide].load(std::memory_order_relaxed) > 0.001f) {
            const int32_t back = held_[heldN_ - 1];
            for (auto& v : voices_)
                if (v.active && v.stage != Stage::Release && v.pitch == pitch) {
                    v.pitch = back;
                    lastNote_ = back;
                    return;
                }
        }
    }
    for (auto& v : voices_)
        if (v.active && v.pitch == pitch && v.stage != Stage::Release) v.stage = Stage::Release;
}

void Sampler::allNotesOff() {
    wake();
    for (auto& v : voices_) v.active = false;
    heldN_ = 0;
}

void Sampler::render(float* out, int32_t frames) {
    if (idle_) return;
    if (!sample_ || sample_->empty()) {
        publishIdle();
        // Nothing can sound without a sample: settle straight toward sleep.
        quietSamples_ += static_cast<uint64_t>(frames);
        if (quietSamples_ > kIdleSamples) idle_ = true;
        return;
    }
    const double N = static_cast<double>(sample_->frames);
    double startF = std::clamp(pn_[Start].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
    double endF = std::clamp(pn_[End].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
    if (endF <= startF + 1) endF = std::min(N, startF + 1);
    double loopA = std::clamp(pn_[LoopStart].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
    double loopB = std::clamp(pn_[LoopEnd].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
    loopA = std::clamp(loopA, startF, endF);
    loopB = std::clamp(loopB, startF, endF);
    if (loopB <= loopA + 1) loopB = std::min(endF, loopA + 1);
    const int loopMode = loopModeOf();
    const double loopLen = loopB - loopA;
    // Loop crossfade length (samples), capped to half the loop — forward loop only.
    double xf = pn_[LoopXfade].load(std::memory_order_relaxed) * 0.2 * sampleRate_;
    xf = std::clamp(xf, 0.0, loopLen * 0.5);

    const int32_t root = rootNote_.load(std::memory_order_relaxed);
    const double pitchOff = (pn_[Transpose].load(std::memory_order_relaxed) - 0.5) * 48.0 +
                            (pn_[Detune].load(std::memory_order_relaxed) - 0.5) * 1.0; // ±50 cents
    const double pTrack = pn_[PitchTrack].load(std::memory_order_relaxed);
    const double srcRatio = sample_->sourceSampleRate / sampleRate_;
    const float gain = pn_[Volume].load(std::memory_order_relaxed) *
                       static_cast<float>(std::pow(10.0, gainDb(pn_[Gain].load(std::memory_order_relaxed)) / 20.0)) *
                       pn_[Output].load(std::memory_order_relaxed);
    const double pan = pn_[Pan].load(std::memory_order_relaxed) * 2.0 - 1.0;
    const float gl = static_cast<float>(std::cos((pan + 1.0) * kPi / 4.0));
    const float gr = static_cast<float>(std::sin((pan + 1.0) * kPi / 4.0));

    const float atkRate = static_cast<float>(1.0 / (secOf(Attack, 0.0005, 4.0) * sampleRate_));
    const float decRate = static_cast<float>(1.0 / (secOf(Decay, 0.002, 6.0) * sampleRate_));
    const float relRate = static_cast<float>(1.0 / (secOf(Release, 0.002, 6.0) * sampleRate_));
    const float sustain = pn_[Sustain].load(std::memory_order_relaxed);
    const float velAmt = pn_[VelAmount].load(std::memory_order_relaxed); // 1 = full velocity→volume, 0 = flat

    // Glide: a one-pole slide of the voice's note toward its key (time ≈ to 63 %).
    const double glideSec = glideSeconds(pn_[Glide].load(std::memory_order_relaxed));
    const double glideK = glideSec > 0.0 ? 1.0 - std::exp(-static_cast<double>(kCtl) / (glideSec * sampleRate_)) : 1.0;

    // Filter (TPT SVF). Cutoff can key-track the note relative to the root and follow
    // the amp envelope; coefficients refresh every kCtl samples.
    const int fType =
        std::clamp(static_cast<int>(std::lround(pn_[FilterType].load(std::memory_order_relaxed) * 3.0f)), 0, 3);
    const double baseCut = expMap(pn_[Cutoff].load(std::memory_order_relaxed), 20.0, 20000.0);
    const double kTrack = pn_[FilterKeyTrack].load(std::memory_order_relaxed);
    const double k = 2.0 - 1.9 * pn_[Resonance].load(std::memory_order_relaxed);
    const bool envOn = pn_[EnvCutoff].load(std::memory_order_relaxed) > 0.5f;
    const double envOct = envOn ? envOctaves(pn_[EnvAmount].load(std::memory_order_relaxed)) : 0.0;

    int nActive = 0;
    float peak = 0.0f, ownPeak = 0.0f;
    for (auto& v : voices_) {
        if (!v.active) continue;
        ++nActive;
        double inc = 0.0, a1 = 0.0, a2 = 0.0, a3 = 0.0;
        for (int32_t i = 0; i < frames; ++i) {
            if ((i % kCtl) == 0) { // control rate: glide, pitch, filter coefficients
                v.note += (v.pitch - v.note) * glideK;
                if (std::abs(v.pitch - v.note) < 1e-4) v.note = v.pitch;
                inc = std::pow(2.0, ((v.note - root) * pTrack + pitchOff) / 12.0) * srcRatio;
                if (fType > 0) {
                    double fc = baseCut;
                    if (kTrack > 0.0) fc *= std::pow(2.0, kTrack * (v.note - root) / 12.0);
                    if (envOn) fc *= std::pow(2.0, envOct * v.env);
                    v.cutoff = std::clamp(fc, 20.0, sampleRate_ * 0.49);
                    const double g = std::tan(kPi * v.cutoff / sampleRate_);
                    a1 = 1.0 / (1.0 + g * (g + k));
                    a2 = g * a1;
                    a3 = g * a2;
                }
            }
            // envelope
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

            float ls, rs;
            readInterp(v.pos, ls, rs);
            // Forward-loop crossfade: as we approach loopB, blend in the loop head.
            if (loopMode == 1 && xf > 1.0 && v.dir > 0 && v.pos > loopB - xf) {
                const double t = (v.pos - (loopB - xf)) / xf; // 0..1 across the seam
                float hl, hr;
                readInterp(v.pos - loopLen, hl, hr);
                ls = static_cast<float>(ls * (1.0 - t) + hl * t);
                rs = static_cast<float>(rs * (1.0 - t) + hr * t);
            }

            if (fType > 0) {
                ls = svf(v.ic1L, v.ic2L, ls, a1, a2, a3, k, fType);
                rs = svf(v.ic1R, v.ic2R, rs, a1, a2, a3, k, fType);
            }

            const float amp = v.env * (velAmt * v.velocity + (1.0f - velAmt)) * gain;
            out[i * 2] += ls * amp * gl;
            out[i * 2 + 1] += rs * amp * gr;
            ownPeak = std::max(ownPeak, std::max(std::fabs(ls * amp * gl), std::fabs(rs * amp * gr)));

            // advance + loop / end handling
            v.pos += inc * v.dir;
            if (loopMode == 1) { // forward loop (either direction)
                if (v.dir > 0) {
                    if (v.pos >= loopB) v.pos -= loopLen;
                } else {
                    if (v.pos <= loopA) v.pos += loopLen;
                }
            } else if (loopMode == 2) { // ping-pong
                if (v.pos >= loopB) {
                    v.pos = loopB - (v.pos - loopB);
                    v.dir = -1;
                } else if (v.pos <= loopA) {
                    v.pos = loopA + (loopA - v.pos);
                    v.dir = 1;
                }
            } else { // one-shot (respects reverse)
                if (v.pos >= endF || v.pos < startF) {
                    v.active = false;
                    break;
                }
            }
        }
    }
    for (int32_t i = 0; i < frames * 2; ++i) peak = std::max(peak, std::abs(out[i]));
    // Publish the loudest active voice + the voice count for the UI.
    const Voice* best = nullptr;
    int still = 0;
    for (auto& v : voices_)
        if (v.active) {
            ++still;
            if (!best || v.env > best->env) best = &v;
        }
    playPos_.store(best ? static_cast<float>(best->pos / N) : -1.0f, std::memory_order_relaxed);
    activeVoices_.store(nActive, std::memory_order_relaxed);
    tele_[0].store(static_cast<float>(nActive), std::memory_order_relaxed);
    tele_[1].store(best ? static_cast<float>(best->pos / N) : -1.0f, std::memory_order_relaxed);
    tele_[2].store(best ? best->env : 0.0f, std::memory_order_relaxed);
    tele_[3].store(best ? static_cast<float>(static_cast<int>(best->stage)) : -1.0f, std::memory_order_relaxed);
    tele_[4].store(best ? static_cast<float>(best->note) : -1.0f, std::memory_order_relaxed);
    tele_[5].store(best && fType > 0 ? static_cast<float>(best->cutoff) : -1.0f, std::memory_order_relaxed);
    if (peak > tele_[6].load(std::memory_order_relaxed)) tele_[6].store(peak, std::memory_order_relaxed);
    tele_[7].store(static_cast<float>(heldN_), std::memory_order_relaxed);

    // Auto-sleep: a looping voice stays active until note-off, so no active voice plus
    // our own silent block means every tail (incl. release) has fully decayed.
    if (still == 0 && ownPeak <= 1.0e-5f) {
        quietSamples_ += static_cast<uint64_t>(frames);
        if (quietSamples_ > kIdleSamples) idle_ = true;
    } else {
        quietSamples_ = 0;
        idle_ = false;
    }
}

Sampler::Voice* Sampler::findFreeVoice() {
    for (auto& v : voices_)
        if (!v.active) return &v;
    Voice* q = &voices_[0];
    for (auto& v : voices_)
        if (v.env < q->env) q = &v;
    return q;
}

void Sampler::readInterp(double pos, float& l, float& r) const {
    const int64_t i0 = static_cast<int64_t>(pos);
    float l0, r0, l1, r1;
    sample_->readStereo(std::clamp<int64_t>(i0, 0, sample_->frames - 1), l0, r0);
    sample_->readStereo(std::clamp<int64_t>(i0 + 1, 0, sample_->frames - 1), l1, r1);
    const double frac = pos - i0;
    l = static_cast<float>(l0 + (l1 - l0) * frac);
    r = static_cast<float>(r0 + (r1 - r0) * frac);
}

float Sampler::svf(double& ic1, double& ic2, float xf, double a1, double a2, double a3, double k, int type) {
    const double x = xf;
    const double v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
    ic1 = 2.0 * v1 - ic1;
    ic2 = 2.0 * v2 - ic2;
    if (type == 1) return static_cast<float>(v2);              // low-pass
    if (type == 2) return static_cast<float>(x - k * v1 - v2); // high-pass
    return static_cast<float>(v1);                             // band-pass
}

void Sampler::publishIdle() {
    playPos_.store(-1.0f, std::memory_order_relaxed);
    activeVoices_.store(0, std::memory_order_relaxed);
    tele_[0].store(0.0f, std::memory_order_relaxed);
    tele_[1].store(-1.0f, std::memory_order_relaxed);
    tele_[2].store(0.0f, std::memory_order_relaxed);
    tele_[3].store(-1.0f, std::memory_order_relaxed);
    tele_[4].store(-1.0f, std::memory_order_relaxed);
    tele_[5].store(-1.0f, std::memory_order_relaxed);
}

void Sampler::pushHeld(int32_t p) {
    popHeld(p);
    if (heldN_ == kHeld) {
        std::memmove(held_, held_ + 1, sizeof(int32_t) * (kHeld - 1));
        --heldN_;
    }
    held_[heldN_++] = p;
}

void Sampler::popHeld(int32_t p) {
    for (int i = 0; i < heldN_; ++i)
        if (held_[i] == p) {
            std::memmove(held_ + i, held_ + i + 1, sizeof(int32_t) * (heldN_ - i - 1));
            --heldN_;
            return;
        }
}

} // namespace nota
