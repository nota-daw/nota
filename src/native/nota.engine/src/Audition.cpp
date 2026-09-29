// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preset audition rig — see Audition.h.

#include "Audition.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <map>
#include <mutex>
#include <utility>

#include "AudioFile.h"
#include "Device.h"
#include "DeviceFactory.h"
#include "Instrument.h"
#include "MidiDevice.h"
#include "Sampler.h"
#include "TransportInfo.h"

namespace nota {

namespace {

constexpr int32_t kBlock = 256;
constexpr int32_t kMaxEvents = 1024;
constexpr double  kPi = 3.141592653589793;

// Note-offs before note-ons at a tie, so a same-pitch retrigger releases first.
bool evLess(const MidiEv& a, const MidiEv& b) { return a.off < b.off || (a.off == b.off && !a.on && b.on); }

// A struck, decaying harmonic tone at C4 — what the Sampler plays when no sample is given.
std::shared_ptr<SampleBuffer> keysTone(double sr) {
    auto s = std::make_shared<SampleBuffer>();
    const int64_t n = static_cast<int64_t>(2.2 * sr);
    s->channels = 1; s->frames = n; s->sourceSampleRate = sr; s->samples.resize(static_cast<size_t>(n));
    const double f0 = 261.6255653;
    for (int64_t i = 0; i < n; ++i) {
        const double t = static_cast<double>(i) / sr;
        double v = 0.0;
        for (int k = 1; k <= 8; ++k)   // brighter partials die faster
            v += std::sin(2.0 * kPi * f0 * k * (1.0 + 0.0004 * k * k) * t) * std::exp(-t * (1.2 + 0.9 * k)) / k;
        const double attack = std::min(1.0, t / 0.004);
        s->samples[static_cast<size_t>(i)] = static_cast<float>(v * 0.42 * attack);
    }
    s->buildPeakTable();
    return s;
}

// Any buffer → stereo at `sr` (linear interpolation), at most maxFrames long.
std::shared_ptr<SampleBuffer> toStereoAt(const SampleBuffer& src, double sr, int64_t maxFrames) {
    auto s = std::make_shared<SampleBuffer>();
    const double ratio = src.sourceSampleRate > 0 ? src.sourceSampleRate / sr : 1.0;
    const int64_t n = std::min<int64_t>(maxFrames, static_cast<int64_t>(src.frames / ratio));
    s->channels = 2; s->frames = std::max<int64_t>(0, n); s->sourceSampleRate = sr;
    s->samples.resize(static_cast<size_t>(s->frames * 2));
    for (int64_t i = 0; i < s->frames; ++i) {
        const double p = i * ratio;
        const int64_t i0 = static_cast<int64_t>(p);
        const float fr = static_cast<float>(p - i0);
        float l0, r0, l1, r1;
        src.readStereo(i0, l0, r0);
        src.readStereo(i0 + 1, l1, r1);
        s->samples[static_cast<size_t>(i * 2)] = l0 + (l1 - l0) * fr;
        s->samples[static_cast<size_t>(i * 2 + 1)] = r0 + (r1 - r0) * fr;
    }
    return s;
}

} // namespace

AuditionRig::AuditionRig(double sampleRate) : sr_(sampleRate > 0 ? sampleRate : 48000.0) {}
AuditionRig::~AuditionRig() = default;

bool AuditionRig::setInstrument(int32_t kind) {
    kit_ = nullptr;
    inst_ = makeBuiltinInstrument(kind);
    if (!inst_) return false;
    inst_->setSampleRate(sr_);
    if (kind == 1) setSamplerSample({});   // an empty Sampler would be silent
    return true;
}

bool AuditionRig::instrumentParam(const std::string& id, float value) {
    if (!inst_) return false;
    const int32_t i = inst_->pluginParamIndexOfId(id);
    if (i < 0) return false;
    inst_->pluginParamSet(i, value);
    return true;
}

int32_t AuditionRig::addDevice(int32_t kind) {
    auto d = makeBuiltinDevice(kind);
    if (!d) return -1;
    d->setSampleRate(sr_, kBlock);
    devices_.push_back(std::move(d));
    return static_cast<int32_t>(devices_.size()) - 1;
}

bool AuditionRig::deviceParam(int32_t index, const std::string& name, float value) {
    if (index < 0 || index >= static_cast<int32_t>(devices_.size())) return false;
    Device& d = *devices_[static_cast<size_t>(index)];
    for (int32_t p = 0, n = d.paramCount(); p < n; ++p)
        if (name == d.paramName(p)) { d.setParam(p, value); return true; }
    return false;
}

int32_t AuditionRig::addMidiEffect(int32_t kind) {
    auto m = makeMidiDevice(kind);
    if (!m) return -1;
    m->setSampleRate(sr_, kBlock);
    midi_.push_back(std::move(m));
    return static_cast<int32_t>(midi_.size()) - 1;
}

bool AuditionRig::midiParam(int32_t index, const std::string& name, float value) {
    if (index < 0 || index >= static_cast<int32_t>(midi_.size())) return false;
    MidiDevice& m = *midi_[static_cast<size_t>(index)];
    for (int32_t p = 0, n = m.paramCount(); p < n; ++p)
        if (name == m.paramName(p)) { m.setParam(p, value); return true; }
    return false;
}

bool AuditionRig::setSamplerSample(const std::string& path) {
    auto* sampler = dynamic_cast<Sampler*>(inst_.get());
    if (!sampler) return false;
    std::shared_ptr<SampleBuffer> buf = path.empty() ? nullptr : decodeAudioFile(path);
    if (buf && !buf->empty()) {
        buf->buildPeakTable();
        sampler->setSample(buf, 60, false);
        return true;
    }
    sampler->setSample(keysTone(sr_), 60, false);
    return path.empty();
}

bool AuditionRig::setSourceFile(const std::string& path, double maxSeconds) {
    auto buf = decodeAudioFile(path);
    if (!buf || buf->empty()) return false;
    source_ = toStereoAt(*buf, sr_, static_cast<int64_t>(std::max(0.5, maxSeconds) * sr_));
    return source_->frames > 0;
}

int64_t AuditionRig::render(const AuditionNote* notes, int32_t count, double bpm, double phraseBeats,
                            double maxTailSeconds, int32_t flags) {
    if (!inst_ && !source_) return -1;
    if (bpm <= 0) bpm = 120.0;
    const double spb = sr_ * 60.0 / bpm;
    const bool rolling = (flags & kRolling) != 0;

    // The note list as absolute sample positions; offs before ons at a tie.
    struct At { int64_t frame; MidiEv ev; };
    std::vector<At> timeline;
    if (inst_ && notes)
        for (int32_t i = 0; i < count; ++i) {
            const AuditionNote& n = notes[i];
            if (n.pitch < 0 || n.pitch > 127 || n.lengthBeats <= 0) continue;
            const int64_t on = static_cast<int64_t>(std::llround(n.startBeat * spb));
            const int64_t off = std::max(on + 1, static_cast<int64_t>(std::llround((n.startBeat + n.lengthBeats) * spb)));
            timeline.push_back({on, {0, true, n.pitch, std::clamp(n.velocity, 0.0f, 1.0f), static_cast<float>(n.lengthBeats)}});
            timeline.push_back({off, {0, false, n.pitch, 0.0f}});
        }
    std::stable_sort(timeline.begin(), timeline.end(), [](const At& a, const At& b) {
        return a.frame < b.frame || (a.frame == b.frame && !a.ev.on && b.ev.on);
    });

    int64_t phraseFrames = static_cast<int64_t>(std::ceil(std::max(0.0, phraseBeats) * spb));
    if (source_) phraseFrames = std::max(phraseFrames, source_->frames);
    const int64_t maxFrames = phraseFrames + static_cast<int64_t>(std::max(0.0, maxTailSeconds) * sr_);
    if (maxFrames <= 0) return -1;

    std::vector<float> out;
    out.reserve(static_cast<size_t>(maxFrames * 2));
    float buf[kBlock * 2];
    MidiEv evA[kMaxEvents], evB[kMaxEvents];
    size_t next = 0;
    int64_t quiet = 0, lastLoud = 0;
    const int64_t quietNeeded = static_cast<int64_t>(0.25 * sr_);
    bool endedQuiet = false;

    TransportInfo ti;
    ti.bpm = bpm;
    ti.isPlaying = true;

    for (int64_t pos = 0; pos < maxFrames; pos += kBlock) {
        if (cancelled_.load(std::memory_order_relaxed)) return -1;
        const int32_t n = static_cast<int32_t>(std::min<int64_t>(kBlock, maxFrames - pos));
        const double beat = pos / spb;
        std::memset(buf, 0, sizeof(float) * static_cast<size_t>(n) * 2);

        if (inst_) {
            int nEv = 0;
            while (next < timeline.size() && timeline[next].frame < pos + n) {
                if (nEv < kMaxEvents) {
                    evA[nEv] = timeline[next].ev;
                    evA[nEv].off = static_cast<int32_t>(std::max<int64_t>(0, timeline[next].frame - pos));
                    ++nEv;
                }
                ++next;
            }
            MidiEv* a = evA;
            MidiEv* b = evB;
            for (auto& m : midi_) {
                int outN = 0;
                m->process(a, nEv, b, outN, kMaxEvents, n, beat, spb, true);
                std::swap(a, b);
                nEv = outN;
            }
            std::sort(a, a + nEv, evLess);

            ti.ppqPosition = beat;
            ti.timeInSeconds = pos / sr_;
            ti.timeInSamples = pos;
            ti.isPlaying = rolling;
            inst_->setTransport(beat, spb, rolling);
            inst_->setTransportInfo(ti);
            int32_t cursor = 0;
            for (int k = 0; k < nEv; ++k) {
                const int32_t segEnd = std::clamp(a[k].off, 0, n);
                if (segEnd > cursor) { inst_->render(&buf[cursor * 2], segEnd - cursor); cursor = segEnd; }
                if (a[k].on) inst_->noteOn(a[k].pitch, a[k].vel);
                else         inst_->noteOff(a[k].pitch);
            }
            if (cursor < n) inst_->render(&buf[cursor * 2], n - cursor);
        }

        if (source_)
            for (int32_t i = 0; i < n; ++i) {
                float l, r;
                source_->readStereo(pos + i, l, r);
                buf[i * 2] += l; buf[i * 2 + 1] += r;
            }

        ti.ppqPosition = beat;
        ti.timeInSeconds = pos / sr_;
        ti.timeInSamples = pos;
        ti.isPlaying = true;
        for (auto& d : devices_) {
            d->setTransport(beat, spb, true);
            d->setTransportInfo(ti);
            d->process(buf, n);
        }

        float peak = 0.0f;
        for (int32_t i = 0; i < n * 2; ++i) {
            if (!std::isfinite(buf[i])) buf[i] = 0.0f;
            peak = std::max(peak, std::fabs(buf[i]));
        }
        out.insert(out.end(), buf, buf + n * 2);

        // Past the phrase, stop once the tail has been below −80 dB for a quarter second.
        if (peak > 1e-4f) { quiet = 0; lastLoud = pos + n; }
        else quiet += n;
        if (pos + n >= phraseFrames && quiet >= quietNeeded) { endedQuiet = true; break; }
    }

    int64_t frames = static_cast<int64_t>(out.size() / 2);
    if (endedQuiet) frames = std::min(frames, lastLoud + static_cast<int64_t>(0.05 * sr_));
    if (frames <= 0) return -1;
    out.resize(static_cast<size_t>(frames * 2));
    if (!endedQuiet) {   // cut by the tail budget: fade the last 300 ms
        const int64_t fade = std::min<int64_t>(frames, static_cast<int64_t>(0.3 * sr_));
        for (int64_t i = 0; i < fade; ++i) {
            const float g = static_cast<float>(i) / static_cast<float>(fade);
            const size_t k = static_cast<size_t>((frames - 1 - i) * 2);
            out[k] *= g; out[k + 1] *= g;
        }
    }
    // Never hand the preview voice a clipping buffer: a hot preset is brought back to −0.2 dB.
    float peak = 0.0f;
    for (float v : out) peak = std::max(peak, std::fabs(v));
    if (peak > 0.977f) { const float g = 0.977f / peak; for (float& v : out) v *= g; }

    auto res = std::make_shared<SampleBuffer>();
    res->channels = 2;
    res->frames = frames;
    res->sourceSampleRate = sr_;
    res->samples = std::move(out);
    res->buildPeakTable();
    result_ = std::move(res);
    return frames;
}

int32_t AuditionRig::peaks(float* out, int32_t maxPoints) const {
    if (!out || maxPoints <= 0 || !result_ || result_->frames <= 0) return 0;
    const SampleBuffer& b = *result_;
    const int32_t buckets = static_cast<int32_t>(std::min<int64_t>(maxPoints, b.frames));
    for (int32_t i = 0; i < buckets; ++i) {
        const int64_t f0 = b.frames * i / buckets, f1 = b.frames * (i + 1) / buckets;
        // Both channels, not the mid: a side-only or polarity-flipped signal must still show.
        float mn = 0.0f, mx = 0.0f;
        for (int64_t f = f0; f < std::max(f0 + 1, f1) && f < b.frames; ++f) {
            const float l = b.samples[static_cast<size_t>(f * 2)], r = b.samples[static_cast<size_t>(f * 2 + 1)];
            mn = std::min(mn, std::min(l, r));
            mx = std::max(mx, std::max(l, r));
        }
        out[i * 2] = mn; out[i * 2 + 1] = mx;
    }
    return buckets;
}

// --- demo-track sources ---------------------------------------------------------------

namespace {
std::mutex gSourceMu;
std::map<std::pair<std::string, int64_t>, std::shared_ptr<const SampleBuffer>> gSources;   // (key, rate)
constexpr size_t kSourceCacheMax = 24;
}

bool AuditionRig::addSourceFrom(const AuditionRig& part, float gain) {
    const auto& r = part.result_;
    if (!r || r->empty() || r->channels != 2) return false;
    if (!mixing_) {
        mixing_ = std::make_shared<SampleBuffer>();
        mixing_->channels = 2;
        mixing_->sourceSampleRate = sr_;
    }
    if (r->frames > mixing_->frames) {
        mixing_->frames = r->frames;
        mixing_->samples.resize(static_cast<size_t>(r->frames * 2), 0.0f);
    }
    for (size_t i = 0; i < r->samples.size(); ++i) mixing_->samples[i] += r->samples[i] * gain;
    source_ = mixing_;
    return true;
}

bool AuditionRig::useCachedSource(const std::string& key) {
    std::lock_guard<std::mutex> lock(gSourceMu);
    auto it = gSources.find({key, static_cast<int64_t>(sr_)});
    if (it == gSources.end()) return false;
    source_ = it->second;
    mixing_.reset();
    return true;
}

bool AuditionRig::cacheSource(const std::string& key, float targetPeak) {
    if (!mixing_ || mixing_->empty()) return false;
    // One level for every track, so an effect is compared on equal terms across genres.
    float peak = 0.0f;
    for (float v : mixing_->samples) peak = std::max(peak, std::fabs(v));
    if (peak > 1e-6f && targetPeak > 0.0f) {
        const float g = targetPeak / peak;
        for (float& v : mixing_->samples) v *= g;
    }
    std::shared_ptr<const SampleBuffer> done = std::move(mixing_);
    source_ = done;
    std::lock_guard<std::mutex> lock(gSourceMu);
    if (gSources.size() >= kSourceCacheMax) gSources.erase(gSources.begin());
    gSources[{key, static_cast<int64_t>(sr_)}] = done;
    return true;
}

// --- kits -----------------------------------------------------------------------------
// One-shot pads, the way a Drum Rack plays a kit: each pad restarts on its note, a pad in a
// choke group cuts the others in it (a 5 ms fade, no click), and each pad runs its own effect
// chain before its gain and pan. An idle pad's chain keeps running until its tail dies out.

class AuditionKit final : public Instrument {
public:
    explicit AuditionKit(double sr) : sr_(sr) { scratch_.resize(kBlock * 2); }

    struct Pad {
        int32_t note = 36;
        std::shared_ptr<SampleBuffer> buf;
        float gl = 1.0f, gr = 1.0f;
        int32_t choke = 0;
        std::vector<std::shared_ptr<Device>> fx;
        double pos = -1.0;       // source frames; < 0 = silent
        float vel = 0.0f;
        float fade = 1.0f, fadeStep = 0.0f;   // choke fade-out
        int64_t quiet = 0;       // frames the pad (and its tail) has been silent
    };

    int32_t addPad(int32_t note, std::shared_ptr<SampleBuffer> buf, float gain, float pan, int32_t choke) {
        Pad p;
        p.note = note; p.buf = std::move(buf); p.choke = choke;
        pan = std::clamp(pan, -1.0f, 1.0f);
        p.gl = gain * std::min(1.0f, 1.0f - pan);
        p.gr = gain * std::min(1.0f, 1.0f + pan);
        p.quiet = kIdle;
        pads_.push_back(std::move(p));
        return static_cast<int32_t>(pads_.size()) - 1;
    }
    Pad* pad(int32_t i) { return i >= 0 && i < static_cast<int32_t>(pads_.size()) ? &pads_[static_cast<size_t>(i)] : nullptr; }

    void setSampleRate(double sr) override { sr_ = sr; }
    void noteOn(int32_t pitch, float velocity) override {
        for (auto& p : pads_) {
            if (p.note != pitch) continue;
            if (p.choke > 0)
                for (auto& o : pads_)
                    if (&o != &p && o.choke == p.choke && o.pos >= 0.0) o.fadeStep = 1.0f / static_cast<float>(0.005 * sr_);
            p.pos = 0.0; p.vel = velocity; p.fade = 1.0f; p.fadeStep = 0.0f; p.quiet = 0;
        }
    }
    void noteOff(int32_t) override {}
    void allNotesOff() override { for (auto& p : pads_) p.pos = -1.0; }

    void render(float* out, int32_t frames) override {
        for (auto& p : pads_) {
            if (p.pos < 0.0 && p.quiet >= kIdle) continue;
            std::fill(scratch_.begin(), scratch_.begin() + frames * 2, 0.0f);
            bool sounding = false;
            if (p.pos >= 0.0 && p.buf) {
                const SampleBuffer& b = *p.buf;
                const double ratio = b.sourceSampleRate > 0 ? b.sourceSampleRate / sr_ : 1.0;
                for (int32_t i = 0; i < frames; ++i) {
                    const int64_t i0 = static_cast<int64_t>(p.pos);
                    if (i0 >= b.frames || p.fade <= 0.0f) { p.pos = -1.0; break; }
                    const float fr = static_cast<float>(p.pos - i0);
                    float l0, r0, l1, r1;
                    b.readStereo(i0, l0, r0);
                    b.readStereo(i0 + 1, l1, r1);
                    const float g = p.vel * p.fade;
                    scratch_[static_cast<size_t>(i * 2)] = (l0 + (l1 - l0) * fr) * g;
                    scratch_[static_cast<size_t>(i * 2 + 1)] = (r0 + (r1 - r0) * fr) * g;
                    p.fade -= p.fadeStep;
                    p.pos += ratio;
                }
                sounding = true;
            }
            for (auto& d : p.fx) d->process(scratch_.data(), frames);
            float peak = 0.0f;
            for (int32_t i = 0; i < frames; ++i) {
                const float l = scratch_[static_cast<size_t>(i * 2)] * p.gl, r = scratch_[static_cast<size_t>(i * 2 + 1)] * p.gr;
                out[i * 2] += l; out[i * 2 + 1] += r;
                peak = std::max(peak, std::max(std::fabs(l), std::fabs(r)));
            }
            p.quiet = (sounding || peak > 1e-5f) ? 0 : p.quiet + frames;
        }
    }

    int32_t kind() const override { return 4; }
    const char* displayName() const override { return "Kit"; }

private:
    static constexpr int64_t kIdle = 48000;   // ~1 s of silence ends a pad's tail
    double sr_;
    std::vector<Pad> pads_;
    std::vector<float> scratch_;
};

bool AuditionRig::useKit() {
    auto k = std::make_shared<AuditionKit>(sr_);
    kit_ = k.get();
    inst_ = std::move(k);
    return true;
}

int32_t AuditionRig::kitAddPad(int32_t note, const std::string& path, float gain, float pan, int32_t choke) {
    if (!kit_) return -1;
    auto buf = decodeAudioFile(path);
    if (!buf || buf->empty()) return -1;
    return kit_->addPad(note, std::move(buf), gain, pan, choke);
}

int32_t AuditionRig::kitPadAddDevice(int32_t pad, int32_t kind) {
    auto* p = kit_ ? kit_->pad(pad) : nullptr;
    if (!p) return -1;
    auto d = makeBuiltinDevice(kind);
    if (!d) return -1;
    d->setSampleRate(sr_, kBlock);
    p->fx.push_back(std::move(d));
    return static_cast<int32_t>(p->fx.size()) - 1;
}

bool AuditionRig::kitPadDeviceParam(int32_t pad, int32_t device, const std::string& name, float value) {
    auto* p = kit_ ? kit_->pad(pad) : nullptr;
    if (!p || device < 0 || device >= static_cast<int32_t>(p->fx.size())) return false;
    Device& d = *p->fx[static_cast<size_t>(device)];
    for (int32_t i = 0, n = d.paramCount(); i < n; ++i)
        if (name == d.paramName(i)) { d.setParam(i, value); return true; }
    return false;
}

} // namespace nota
