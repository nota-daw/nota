// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — realtime render core: poll, block render, offline bounce, command/live-MIDI drain, per-track raw renderers, mixer processBlock, metronome & test tone. RT-hot; do not add allocations.

#include "Engine.h"
#include "AudioFile.h"
#include "Compressor.h"
#include "CoreAudioBackend.h"
#include "Delay.h"
#include "DrumRack.h"
#include "Eq.h"
#include "PluginHostBridge.h"
#include "Reverb.h"
#include "Sampler.h"
#include "Synth.h"
#include "Utility.h"
#include "WarpStream.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#if defined(__SSE__) || defined(_M_X64) || defined(_M_IX86)
#include <xmmintrin.h>
#endif

namespace nota {

namespace {
// Flush denormals to zero for the duration of the audio callback. Denormal
// numbers (the tiny values reverb/delay/filter tails decay into) are much slower
// to compute on some FPUs and can spike CPU into a dropout; the mix never needs
// that sub-noise-floor precision. RAII so the caller's FP env is always restored.
struct ScopedNoDenormals {
#if defined(__aarch64__)
    uint64_t saved;
    ScopedNoDenormals() {
        __asm__ volatile("mrs %0, fpcr" : "=r"(saved));
        uint64_t fz = saved | (1ull << 24);   // FPCR.FZ (flush-to-zero)
        __asm__ volatile("msr fpcr, %0" : : "r"(fz));
    }
    ~ScopedNoDenormals() { __asm__ volatile("msr fpcr, %0" : : "r"(saved)); }
#elif defined(__SSE__) || defined(_M_X64) || defined(_M_IX86)
    unsigned saved;
    ScopedNoDenormals() { saved = _mm_getcsr(); _mm_setcsr(saved | 0x8040); } // FTZ|DAZ
    ~ScopedNoDenormals() { _mm_setcsr(saved); }
#else
    ScopedNoDenormals() {}
#endif
};
constexpr double kTwoPi = 6.283185307179586;
constexpr float  kToneGain = 0.2f;
constexpr float  kMetroGain = 0.5f;
// Sort note events by sample offset; at ties, note-offs before note-ons so a
// same-block same-pitch retrigger releases the old voice before the new one.
inline bool midiEvLess(const MidiEv& a, const MidiEv& b) {
    return a.off < b.off || (a.off == b.off && !a.on && b.on);
}
}

// Runs the block's sorted note events through the track's MIDI FX chain (bypass =
// passthrough). Ping-pongs between evs and scratch (both size >= 1024); the final
// list is left in evs. beatStart is the block's beat position for the active clock.
int Engine::applyMidiEffects(Track& t, MidiEv* evs, int n, MidiEv* scratch,
                             int32_t frames, double beatStart, double spb, bool playing) {
    MidiEv* a = evs; MidiEv* b = scratch;
    for (auto& md : t.midiEffects) {
        if (!md) continue;
        int outN = 0;
        md->process(a, n, b, outN, 1024, frames, beatStart, spb, playing);
        std::swap(a, b);
        n = outN;
    }
    if (a != evs) for (int i = 0; i < n; ++i) evs[i] = a[i];
    return n;
}

// Map/CC routing: a MIDI effect (the arpeggiator) drives a target audio-device param
// on the same track from its per-step CC lane. Runs each block after the MIDI chain,
// before the device chain processes, so the modulation applies this block.
void Engine::applyMidiCcRouting(Track& t) {
    for (auto& md : t.midiEffects) {
        if (!md) continue;
        const int di = md->ccDestDevice(), pi = md->ccDestParam();
        if (di < 0 || pi < 0 || di >= static_cast<int>(t.devices.size())) continue;
        auto* d = t.devices[di].get();
        if (!d) continue;
        const float mn = d->paramMin(pi), mx = d->paramMax(pi);
        const float mod = std::clamp(md->ccValue() * md->ccDepth(), 0.0f, 1.0f);
        d->setParam(pi, mn + (mx - mn) * mod);
    }
}

void Engine::poll() {
    if (audioRecording_) drainInputQueue(); // keep the capture buffer flowing while recording

    // Automation write/record (M9-C). Message thread: a transport stop ends any
    // latched gesture, and active gestures are sampled at the live playhead.
    {
        const bool playing = transport_.uiIsPlaying();
        const bool rec = autoRecord_.load(std::memory_order_relaxed);
        if (!playing && wasPlaying_) finishAllWrites();    // stop ends latched writes
        wasPlaying_ = playing;

        // Plugin-GUI gestures (M9-C): grabbing a knob in the plugin's own editor
        // begins/ends a write for that param, so it records (or overrides) just like
        // our own faders — a mouse gesture, so never latched. Instruments/devices are
        // stable across snapshots, so a copied track list stays valid while
        // begin/end republish.
        {
            auto snapshot = authoring_->tracks;
            for (auto& tptr : snapshot) {
                const int32_t tid = tptr->id();
                if (auto& inst = tptr->instrument) {
                    int gb = inst->takePluginGestureBegin();
                    if (gb >= 0) beginAutomationWrite(tid, 3, -1, -1, pluginParamId(tid, -1, gb).c_str(), false);
                    int ge = inst->takePluginGestureEnd();
                    if (ge >= 0) endAutomationWrite(tid, 3, -1, -1, pluginParamId(tid, -1, ge).c_str());
                }
                for (int32_t di = 0; di < static_cast<int32_t>(tptr->devices.size()); ++di) {
                    auto& d = tptr->devices[di];
                    if (!d) continue;
                    int gb = d->takePluginGestureBegin();
                    if (gb >= 0) beginAutomationWrite(tid, 3, di, -1, pluginParamId(tid, di, gb).c_str(), false);
                    int ge = d->takePluginGestureEnd();
                    if (ge >= 0) endAutomationWrite(tid, 3, di, -1, pluginParamId(tid, di, ge).c_str());
                }
            }
        }

        if (playing && rec && !activeWrites_.empty())
            sampleAutomationWritesAt(transport_.uiPositionBeats());
    }

    RecordedNote rn;
    std::vector<RecordedNote> batch;
    while (recorded_.pop(rn)) batch.push_back(rn);
    if (batch.empty()) return;

    // Session-slot recording (M5-4): notes are already loop-local; append into the slot.
    if (recordSessionScene_ >= 0 && recordSessionTrackId_ > 0) {
        auto st = findTrackAuthoring(recordSessionTrackId_);
        if (!st || recordSessionScene_ >= static_cast<int32_t>(st->sessionSlots.size())) return;
        auto nt = cloneTrack(*st);
        SessionSlot& s = nt->sessionSlots[recordSessionScene_];
        const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
        for (auto& r : batch) {
            Note n; n.pitch = r.pitch;
            n.startBeat = std::fmod(r.startBeat < 0 ? 0 : r.startBeat, L);
            n.lengthBeats = r.lengthBeats > 0.0 ? r.lengthBeats : 0.1;
            n.velocity = r.velocity;
            s.midi.notes.push_back(n);
        }
        republishWithTrackRaw(recordSessionTrackId_, nt); // grows the live take, not a new undo step
        return;
    }

    if (recordTrackId_ <= 0 || recordClipIndex_ < 0) return;
    auto t = findTrackAuthoring(recordTrackId_);
    if (!t || recordClipIndex_ >= static_cast<int32_t>(t->midiClips.size())) return;
    auto nt = cloneTrack(*t);
    MidiClip& clip = nt->midiClips[recordClipIndex_];
    for (auto& r : batch) {
        Note n; n.pitch = r.pitch;
        n.startBeat = r.startBeat - clip.startBeat;
        n.lengthBeats = r.lengthBeats > 0.0 ? r.lengthBeats : 0.1;
        n.velocity = r.velocity;
        clip.notes.push_back(n);
        // Grow the clip so freshly recorded notes fall inside it (gating in
        // renderInstrumentRaw drops notes past lengthBeats).
        const double end = n.startBeat + n.lengthBeats;
        if (end > clip.lengthBeats) clip.lengthBeats = end;
    }
    republishWithTrackRaw(recordTrackId_, nt); // grows the live take, not a new undo step
}

// --- audio thread ----------------------------------------------------------

void Engine::render(float* out, int32_t numFrames) {
    ScopedNoDenormals noDenormals;
    const auto t0 = std::chrono::steady_clock::now();
    processBlock(out, numFrames);
    // DSP load = render time / block budget, smoothed (Phase 11). Live-device
    // path only; renderOffline stays untimed.
    const double sr = sampleRate();
    if (sr > 0.0 && numFrames > 0) {
        const double elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        const double load = elapsed / (numFrames / sr);
        const float prev = cpuLoad_.load(std::memory_order_relaxed);
        cpuLoad_.store(static_cast<float>(prev * 0.9 + load * 0.1), std::memory_order_relaxed);
    }
}

void Engine::renderOffline(float* out, int32_t frames, double sampleRate) {
    if (sampleRate > 0.0) transport_.setSampleRate(sampleRate);
    renderOffline(out, frames);
}

void Engine::renderOffline(float* out, int32_t frames) {
    stopPreview(); // audition must never bleed into a bounce (M7-4a)
    if (transport_.sampleRate() <= 0) transport_.setSampleRate(44100.0);
    const double sr = transport_.sampleRate();
    // Re-prepare every node for the render rate ONCE, only when it actually changed (an export
    // bounces in chunks: re-preparing every chunk would reset each instrument/device/plugin's
    // DSP state and click at each block seam). This also re-rates the master + racks + MIDI FX
    // and reconfigures warp streams (their pitch/window sizes are baked from the device rate —
    // a higher export rate would otherwise play warped clips too fast).
    if (sr != preparedSR_) {
        propagateSampleRate(sr);
        reconfigureAllWarpStreams();
        recomputePdc();
        preparedSR_ = sr;
    }
    processBlock(out, frames);
}

void Engine::drainCommands() {
    Command c;
    while (commands_.pop(c)) {
        switch (c.type) {
            case CommandType::SetToneEnabled:   toneEnabled_ = (c.i0 != 0); break;
            case CommandType::SetFrequency:     frequency_ = c.f0; break;
            case CommandType::TransportPlay:    transport_.play(); break;
            case CommandType::TransportStop:    transport_.stop(); break;
            case CommandType::SetBpm:           transport_.setBpm(c.d0); break;
            case CommandType::SetTimeSignature: transport_.setTimeSignature(c.i0, c.i1); break;
            case CommandType::SetLoop:          transport_.setLoop(c.i0 != 0, c.d0, c.d1); break;
            case CommandType::SetMetronome:     transport_.setMetronome(c.i0 != 0); break;
            case CommandType::Seek:             transport_.seekBeats(c.d0); break;
            case CommandType::None:             break;
        }
    }
}

void Engine::drainLiveMidi(double blockStartBeat, bool playing) {
    liveCount_ = 0;
    const bool rec = recording_.load(std::memory_order_relaxed);
    if (rec != prevRecording_)
        for (int i = 0; i < 128; ++i) { pendingActive_[i] = false; recRoutedActive_[i] = false; }
    prevRecording_ = rec;

    // When recording into a Session slot, time notes against the slot's own loop
    // clock (localBeats holds this block's start position) instead of the global
    // transport, so overdubs land at the right spot in the loop.
    SessionPlayer* slot = recordSlotPlayer_.load(std::memory_order_acquire);
    const double slotLen = recordSlotLen_.load(std::memory_order_relaxed);
    const double nowBeat = slot ? (slotLen > 0.0 ? std::fmod(slot->localBeats, slotLen) : slot->localBeats)
                                : blockStartBeat;

    MidiEvent e;
    while (liveMidi_.pop(e)) {
        if (liveCount_ < kMaxLive) liveEvents_[liveCount_++] = e;
        if (rec && playing && e.pitch >= 0 && e.pitch < 128) {
            if (e.on) {
                pendingActive_[e.pitch] = true;
                pendingStart_[e.pitch] = nowBeat;
                pendingVel_[e.pitch] = e.velocity;
            } else if (pendingActive_[e.pitch]) {
                double len = nowBeat - pendingStart_[e.pitch];
                if (slot && len < 0.0) len += slotLen; // wrapped past the loop end
                RecordedNote rn{e.pitch, pendingStart_[e.pitch], len, pendingVel_[e.pitch]};
                recorded_.push(rn);
                pendingActive_[e.pitch] = false;
            }
        }
    }
}

// Print routed-in MIDI (a source's post-FX note events) into the record track's take, pairing
// note-on/off across blocks into RecordedNotes at absolute beats. Audio thread; recorded_ has a
// single producer (this + drainLiveMidi both run in processBlock).
void Engine::captureRoutedNotes(const MidiEv* evs, int n, double blockStart, double spb) {
    for (int k = 0; k < n; ++k) {
        const int p = evs[k].pitch;
        if (p < 0 || p >= 128) continue;
        const double beat = (blockStart + evs[k].off) / spb;
        if (evs[k].on) {
            recRoutedActive_[p] = true;
            recRoutedStart_[p] = beat;
            recRoutedVel_[p] = evs[k].vel;
        } else if (recRoutedActive_[p]) {
            recorded_.push(RecordedNote{p, recRoutedStart_[p], std::max(0.0, beat - recRoutedStart_[p]), recRoutedVel_[p]});
            recRoutedActive_[p] = false;
        }
    }
}

// Append a track's own note events (clips while playing + live input when armed/auditioned).
int Engine::gatherInstrumentNotes(Track& t, MidiEv* evs, int n, int32_t frames,
                                  double blockStart, double spb, bool playing) {
    if (playing) {
        for (const MidiClip& clip : t.midiClips) {
            // Deactivated clip (key 0): emit NO new note-ons, but still emit note-offs — so a
            // note already sounding when the clip was switched off releases instead of hanging.
            const bool clipActive = clip.active;
            const double clipStart = clip.startBeat * spb;
            const double clipEnd = clipStart + clip.lengthBeats * spb;
            const bool hasVel = !clip.velocityEnvelope.points.empty();
            for (const Note& note : clip.notes) {
                // Clip length gates note starts (M4-2): notes outside don't play.
                if (note.startBeat < 0.0 || note.startBeat >= clip.lengthBeats) continue;
                const double onS = clipStart + note.startBeat * spb;
                // A trimmed clip cuts the tail: a note that runs past the clip's (possibly
                // shortened) end stops at the boundary instead of sounding its full length.
                double offS = onS + note.lengthBeats * spb;
                if (offS > clipEnd) offS = clipEnd;
                // Velocity envelope (M9 follow-up): scale by the curve at the note's
                // clip-local start beat.
                float vel = note.velocity;
                if (hasVel) vel *= std::clamp(clip.velocityEnvelope.valueAt(note.startBeat), 0.0f, 1.0f);
                if (clipActive && onS >= blockStart && onS < blockStart + frames && n < 1024)
                    evs[n++] = {static_cast<int32_t>(onS - blockStart), true, note.pitch, vel};
                if (offS >= blockStart && offS < blockStart + frames && n < 1024)
                    evs[n++] = {static_cast<int32_t>(offS - blockStart), false, note.pitch, 0.0f};
            }
        }
    }
    // Live notes reach a track when it's armed, or when it's the audition target
    // (so clicking a rack/drum pad plays it without arming/recording).
    if (t.armed() || t.id() == auditionTrackId_.load(std::memory_order_relaxed)) {
        for (int i = 0; i < liveCount_ && n < 1024; ++i)
            evs[n++] = {0, liveEvents_[i].on, liveEvents_[i].pitch, liveEvents_[i].velocity};
    }
    return n;
}

// A track's own MIDI *output* for this block: its clips + live input, drum-rack swing, then
// its own MIDI FX chain. This is what feeds the track's instrument and, via routing, any
// destination it forwards to — so the FX (arp etc.) run exactly once per block.
int Engine::computeInstrumentMidi(Track& t, MidiEv* evs, int32_t frames,
                                  double blockStart, double spb, bool playing, bool arrangementActive) {
    // Arrangement clips gather only when the Arrangement is active; live input (armed/audition)
    // is independent, so a session-only jam still lets you play instruments live.
    int n = gatherInstrumentNotes(t, evs, 0, frames, blockStart, spb, playing && arrangementActive);
    std::sort(evs, evs + n, midiEvLess);

    // Drum Rack swing + humanize: nudge note-on sample offsets. Off-beat 1/16 hits
    // are pushed late by swing; humanize adds a small random jitter. The instrument
    // already receives these at sample accuracy (segmented render below), so shaping
    // the offsets here is all it takes. Off-events keep their time (drum one-shots
    // ignore note length). Delays that would spill past the block clamp to its end.
    if (auto* dr = dynamic_cast<DrumRack*>(t.instrument.get())) {
        const float sw = dr->swing(), hz = dr->humanize();
        if ((sw > 0.0f || hz > 0.0f) && spb > 0.0) {
            static uint32_t rng = 0x9E3779B9u;
            const double sixteenth = spb * 0.25;   // samples per 1/16 note
            for (int k = 0; k < n; ++k) {
                if (!evs[k].on) continue;
                double delay = 0.0;
                if (sw > 0.0f) {
                    const long idx = std::lround((blockStart + evs[k].off) / sixteenth);
                    if (idx & 1L) delay += (double)sw * sixteenth * 0.6;   // up to 60% of a 1/16
                }
                if (hz > 0.0f) {
                    rng = rng * 1664525u + 1013904223u;
                    const double j = ((rng >> 9) & 0xFFFF) / 65535.0;       // 0..1
                    delay += j * (double)hz * sixteenth * 0.35;
                }
                evs[k].off = std::clamp(evs[k].off + (int32_t)std::llround(delay), 0, frames - 1);
            }
            std::sort(evs, evs + n, midiEvLess);
        }
    }

    // MIDI FX chain (arpeggiator etc.): transform the note events before the
    // instrument. Driven by the transport beat when playing, else the device's own
    // clock. The chain may consume/generate events, so re-sort afterwards.
    if (!t.midiEffects.empty()) {
        MidiEv scratch[1024];
        n = applyMidiEffects(t, evs, n, scratch, frames, spb > 0.0 ? blockStart / spb : 0.0, spb, playing);
        applyMidiCcRouting(t);
        std::sort(evs, evs + n, midiEvLess);
    }
    t.updateMidiCv(evs, n);   // MIDI→CV modulators read the post-FX note stream
    return n;
}

// MIDI pre-pass: compute each non-session instrument track's post-FX MIDI output into its
// per-position buffer (blockMidi_). Runs once per block so each track's MIDI FX advance once;
// the render pass then feeds instruments from these buffers (own + routed sources').
void Engine::computeBlockMidi(Graph* g, int32_t frames, double blockStart, double spb, bool playing, bool arrangementActive) {
    const size_t nt = g ? g->tracks.size() : 0;
    if (blockMidi_.size() < nt) blockMidi_.resize(nt);
    if (blockMidiN_.size() < nt) blockMidiN_.resize(nt);
    for (size_t i = 0; i < nt; ++i) {
        blockMidiN_[i] = 0;
        Track& t = *g->tracks[i];
        // Session-active tracks feed via renderSessionSlotRaw (their own clock), so skip here.
        const bool sessionActive = t.sessionPlayer && t.sessionPlayer->playing.load(std::memory_order_relaxed) >= 0;
        if (t.type() != TrackType::Instrument || !t.instrument || sessionActive) continue;
        auto& buf = blockMidi_[i];
        if (buf.size() < 1024) buf.resize(1024);
        blockMidiN_[i] = computeInstrumentMidi(t, buf.data(), frames, blockStart, spb, playing, arrangementActive);
    }
}

// A device that takes a MIDI key (Nota Auto Shift's MIDI target) gets its source track's
// post-FX notes for this block, from the pre-pass (blockMidi_). Audio thread.
void Engine::feedMidiKey(Graph* g, Device& d, int32_t srcTrackId) {
    if (!g) return;
    const size_t nt = std::min(g->tracks.size(), blockMidiN_.size());
    for (size_t j = 0; j < nt; ++j)
        if (g->tracks[j] && g->tracks[j]->id() == srcTrackId) { d.setMidiKey(blockMidi_[j].data(), blockMidiN_[j]); return; }
}

// Renders the instrument's dry stereo (pre-fader) into `dst` (zeroed here) from this block's
// precomputed MIDI: the track's own post-FX output plus the post-FX output of any track routing
// its MIDI here (so a source's arp/MIDI-FX drive this instrument too). Fader/pan and the device
// chain are applied by the caller (processBlock).
void Engine::renderInstrumentRaw(Graph* g, Track& t, float* dst, int32_t frames,
                                 double blockStart, double spb, bool playing) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    Instrument* inst = t.instrument.get();
    if (!inst) return;
    inst->setTransport(spb > 0.0 ? blockStart / spb : 0.0, spb, playing);   // generative synths (Pendulum)

    MidiEv evs[1024];
    int n = 0;
    if (g) {
        const int32_t srcId = t.midiFromTrackId();   // "MIDI In": the track we receive from (-1 = none)
        // Recording the destination prints the routed MIDI into its take (arm the destination).
        const bool captureRouted = srcId >= 0 && t.id() == recordTrackId_ && playing
                                   && recording_.load(std::memory_order_relaxed) && spb > 0.0;
        const size_t nt = std::min(g->tracks.size(), blockMidiN_.size());
        for (size_t j = 0; j < nt; ++j) {
            Track* p = g->tracks[j].get();
            if (!p) continue;
            const bool self = (p == &t);
            // Destination reads its source's post-FX output. A muted source still forwards MIDI:
            // its own render is skipped, but its buffer was computed in the pre-pass.
            const bool routed = !self && srcId >= 0 && p->type() == TrackType::Instrument && p->id() == srcId;
            if (!self && !routed) continue;
            const int m = blockMidiN_[j];
            const MidiEv* srcEvs = blockMidi_[j].data();
            for (int k = 0; k < m && n < 1024; ++k) evs[n++] = srcEvs[k];
            if (routed && captureRouted) captureRoutedNotes(srcEvs, m, blockStart, spb);
        }
    } else {
        n = computeInstrumentMidi(t, evs, frames, blockStart, spb, playing,
                                  arrangementActive_.load(std::memory_order_relaxed));   // fallback: no graph
    }

    std::sort(evs, evs + n, midiEvLess);

    int32_t cursor = 0;
    for (int k = 0; k < n; ++k) {
        const int32_t segEnd = std::clamp(evs[k].off, 0, frames);
        if (segEnd > cursor) { inst->render(&dst[cursor * 2], segEnd - cursor); cursor = segEnd; }
        if (evs[k].on) inst->noteOn(evs[k].pitch, evs[k].vel);
        else           inst->noteOff(evs[k].pitch);
    }
    if (cursor < frames) inst->render(&dst[cursor * 2], frames - cursor);

    // MIDI clip volume envelope (M9 follow-up): scale the instrument output per
    // sample within each clip's time window (0..1 curve in clip-local beats).
    if (playing) {
        for (const MidiClip& clip : t.midiClips) {
            if (!clip.active || clip.volumeEnvelope.points.empty()) continue;
            const double clipStart = clip.startBeat * spb;
            const double clipEnd = clipStart + clip.lengthBeats * spb;
            for (int32_t i = 0; i < frames; ++i) {
                const double p = blockStart + i;
                if (p < clipStart || p >= clipEnd) continue;
                const float g = std::clamp(clip.volumeEnvelope.valueAt((p - clipStart) / spb), 0.0f, 1.0f);
                dst[i * 2] *= g; dst[i * 2 + 1] *= g;
            }
        }
    }
}

// Renders the playing Session slot's MIDI (looped) through the instrument into
// `dst` (zeroed here), on its own loop clock. M5-2.
void Engine::renderSessionSlotRaw(Track& t, float* dst, int32_t frames, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    Instrument* inst = t.instrument.get();
    auto& sp = *t.sessionPlayer;
    const int32_t slot = sp.playing.load(std::memory_order_relaxed);
    if (!inst || slot < 0 || slot >= static_cast<int32_t>(t.sessionSlots.size())) return;
    const SessionSlot& s = t.sessionSlots[slot];
    if (!s.hasClip) return;

    const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    const double p = sp.localBeats;
    const double db = frames / spb;

    MidiEv evs[1024];
    int n = 0;
    const long i0 = static_cast<long>(std::floor(p / L));
    for (long it = i0; it <= i0 + 1; ++it) {
        for (const Note& note : s.midi.notes) {
            if (note.startBeat < 0.0 || note.startBeat >= L) continue;
            const double onAbs = it * L + note.startBeat;
            const double offAbs = onAbs + note.lengthBeats;
            if (onAbs >= p && onAbs < p + db && n < 1024)
                evs[n++] = {static_cast<int32_t>((onAbs - p) * spb), true, note.pitch, note.velocity};
            if (offAbs >= p && offAbs < p + db && n < 1024)
                evs[n++] = {static_cast<int32_t>((offAbs - p) * spb), false, note.pitch, 0.0f};
        }
    }
    // Live monitoring while overdub-recording into this slot (M5-4).
    if (recordSlotPlayer_.load(std::memory_order_acquire) == &sp)
        for (int i = 0; i < liveCount_ && n < 1024; ++i)
            evs[n++] = {0, liveEvents_[i].on, liveEvents_[i].pitch, liveEvents_[i].velocity};

    std::sort(evs, evs + n, midiEvLess);

    // MIDI FX chain (arpeggiator etc.) on the slot's own loop clock.
    if (!t.midiEffects.empty()) {
        MidiEv scratch[1024];
        n = applyMidiEffects(t, evs, n, scratch, frames, p, spb, true);
        applyMidiCcRouting(t);
        std::sort(evs, evs + n, midiEvLess);
    }

    int32_t cursor = 0;
    for (int k = 0; k < n; ++k) {
        const int32_t segEnd = std::clamp(evs[k].off, 0, frames);
        if (segEnd > cursor) { inst->render(&dst[cursor * 2], segEnd - cursor); cursor = segEnd; }
        if (evs[k].on) inst->noteOn(evs[k].pitch, evs[k].vel);
        else           inst->noteOff(evs[k].pitch);
    }
    if (cursor < frames) inst->render(&dst[cursor * 2], frames - cursor);
    sp.localBeats = p + db;
}

// Renders an audio track's playing Session slot (pre-fader), looping the captured
// take over the slot's length (M5-4). Position tracks the slot's own loop clock.
void Engine::renderSessionAudioSlotRaw(Track& t, float* dst, int32_t frames, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    auto& sp = *t.sessionPlayer;
    const int32_t slot = sp.playing.load(std::memory_order_relaxed);
    if (slot < 0 || slot >= static_cast<int32_t>(t.sessionSlots.size())) return;
    const SessionSlot& s = t.sessionSlots[slot];
    if (!s.hasClip || !s.audio.sample) return;

    const SampleBuffer& sb = *s.audio.sample;
    const double sr = transport_.sampleRate();
    const double ratio = sr > 0 ? sb.sourceSampleRate / sr : 1.0; // source frames per device frame
    const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    const float gain = s.audio.gain;

    for (int32_t i = 0; i < frames; ++i) {
        double loopBeat = std::fmod(sp.localBeats + i / spb, L);
        if (loopBeat < 0.0) loopBeat += L;
        const double srcPos = loopBeat * spb * ratio; // -> source frames
        const int64_t i0 = static_cast<int64_t>(srcPos);
        if (i0 < 0 || i0 >= sb.frames) continue;
        const double frac = srcPos - i0;
        float l0, r0, l1, r1;
        sb.readStereo(i0, l0, r0);
        sb.readStereo(i0 + 1, l1, r1);
        dst[i * 2]     = static_cast<float>(l0 + (l1 - l0) * frac) * gain;
        dst[i * 2 + 1] = static_cast<float>(r0 + (r1 - r0) * frac) * gain;
    }
    sp.localBeats += frames / spb;
}

// Renders a track's dry audio clips (pre-fader) into `dst` (zeroed here). Also drives
// Consolidate's offline bounce (consolidateRange) over a detached clip list.
void Engine::renderAudioClipsRaw(const std::vector<AudioClip>& clips, float* dst, int32_t frames,
                                 double blockStart, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    const double sr = transport_.sampleRate();
    for (const AudioClip& clip : clips) {
        if (!clip.active) continue;   // deactivated clip (key 0): stays on the timeline but silent
        const double startSamples = clip.startBeat * spb;
        // Clip envelopes (M9 follow-up): 0..1 volume + -1..1 pan curves in clip-local
        // beats, applied per sample. Empty = fast path (no cost). Pan uses a balance
        // law (unity at centre) so it composes with the track's own pan stage.
        const bool hasVol = !clip.volumeEnvelope.points.empty();
        const bool hasPan = !clip.panEnvelope.points.empty();
        const bool hasEnv = hasVol || hasPan;
        auto applyEnv = [&](double p, float& l, float& r) {
            const double b = (p - startSamples) / spb;
            if (hasVol) { const float g = std::clamp(clip.volumeEnvelope.valueAt(b), 0.0f, 1.0f); l *= g; r *= g; }
            if (hasPan) {
                const float pan = std::clamp(clip.panEnvelope.valueAt(b), -1.0f, 1.0f);
                if (pan > 0.0f) l *= (1.0f - pan); else if (pan < 0.0f) r *= (1.0f + pan);
            }
        };

        // Warped clips: copy straight from the offline stretch cache.
        // The cache holds the played window [warpPlayStart, warpPlayEndEff] pre-rendered
        // at the device rate, so there is no realtime stretch here — just a memcpy-cheap
        // read (no per-clip-start priming spike). The clip's on-timeline length is the
        // cache's frame count (both are warpPlayLen×spb once rebuilt for the tempo).
        if (clip.warpEnabled && clip.warpCache && clip.warpCache->frames > 0) {
            const WarpCache& wc = *clip.warpCache;
            const double clipDeviceLen = static_cast<double>(wc.frames);
            const double ovStart = std::max(blockStart, startSamples);
            const double ovEnd   = std::min(blockStart + frames, startSamples + clipDeviceLen);
            if (ovEnd <= ovStart) continue;
            const int32_t dstOff  = static_cast<int32_t>(std::llround(ovStart - blockStart));
            const int32_t segLen  = static_cast<int32_t>(std::llround(ovEnd - ovStart));
            if (segLen <= 0 || dstOff < 0 || dstOff + segLen > frames) continue;
            const int64_t cacheBase = std::llround(ovStart - startSamples);   // 0-based into the cache
            const float* csamp = wc.samples.data();
            const float g = clip.gain;
            for (int32_t j = 0; j < segLen; ++j) {
                // Reverse reads the same cache back-to-front: the window is already the
                // played length, so mirroring the index is exact (no resample).
                const int64_t ci = clip.reversed ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                if (hasEnv) applyEnv(ovStart + j, l, r);
                dst[(dstOff + j) * 2]     += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
            continue;
        }
        // Warp enabled but the cache isn't ready yet (a rebuild is pending after an
        // edit/tempo change): stay silent rather than fall through and play the clip
        // unwarped (wrong length/pitch) for the brief window until the cache publishes.
        if (clip.warpEnabled) continue;

        // Unwarped clips resample at their natural rate with varispeed transpose
        // folded into the read ratio.
        if (!clip.sample) continue;
        const SampleBuffer& sb = *clip.sample;
        const double ratio = sb.sourceSampleRate / sr * clip.pitchRatio();
        const int64_t len = clip.effectiveLength();
        const double clipDeviceLen = len / ratio;
        for (int32_t i = 0; i < frames; ++i) {
            const double p = blockStart + i;
            if (p < startSamples || p >= startSamples + clipDeviceLen) continue;
            // Reverse walks the source region from its last frame back to its first;
            // the interpolation is unchanged (srcPos is still a real source position).
            double srcPos = clip.reversed
                ? clip.sourceOffsetFrames + (len - 1) - (p - startSamples) * ratio
                : clip.sourceOffsetFrames + (p - startSamples) * ratio;
            if (srcPos < clip.sourceOffsetFrames) srcPos = clip.sourceOffsetFrames;
            const int64_t i0 = static_cast<int64_t>(srcPos);
            const double frac = srcPos - i0;
            float l0, r0, l1, r1;
            sb.readStereo(i0, l0, r0);
            sb.readStereo(i0 + 1, l1, r1);
            float l = static_cast<float>(l0 + (l1 - l0) * frac) * clip.gain;
            float r = static_cast<float>(r0 + (r1 - r0) * frac) * clip.gain;
            // Short edge fade (~3 ms) so a clip that starts/ends mid-waveform doesn't click.
            const double edge = std::min(3.0 * sr / 1000.0, clipDeviceLen * 0.5);
            if (edge > 1.0) {
                const double into = p - startSamples;
                double eg = 1.0;
                if (into < edge) eg = into / edge;
                else if (into > clipDeviceLen - edge) eg = (clipDeviceLen - into) / edge;
                eg = std::clamp(eg, 0.0, 1.0);
                l *= (float)eg; r *= (float)eg;
            }
            applyEnv(p, l, r);
            dst[i * 2]     += l;
            dst[i * 2 + 1] += r;
        }
    }
}

// Freeze playback (M7): fill `dst` from the track's frozen buffer for this segment.
// The buffer is indexed by beat (frame = beat·frozenSpb), so it resolves correctly at
// any render sample rate (an export bounce at a different rate resamples for free).
void Engine::fillFrozen(Track& t, float* dst, int32_t frames, double blockStart, double spb) {
    const auto bufPtr = t.frozenBuf;              // copy the shared_ptr (keeps it alive)
    if (!bufPtr || spb <= 0.0 || t.frozenSpb <= 0.0) { std::fill_n(dst, frames * 2, 0.0f); return; }
    const std::vector<float>& buf = *bufPtr;
    const int64_t n = static_cast<int64_t>(buf.size() / 2);
    for (int32_t i = 0; i < frames; ++i) {
        const double beat = (blockStart + i) / spb;
        const double srcF = beat * t.frozenSpb;
        const int64_t i0 = static_cast<int64_t>(srcF);
        if (srcF < 0.0 || i0 >= n) { dst[i * 2] = 0.0f; dst[i * 2 + 1] = 0.0f; continue; }
        const int64_t i1 = (i0 + 1 < n) ? i0 + 1 : i0;
        const float fr = static_cast<float>(srcF - i0);
        dst[i * 2]     = buf[i0 * 2]     + (buf[i1 * 2]     - buf[i0 * 2])     * fr;
        dst[i * 2 + 1] = buf[i0 * 2 + 1] + (buf[i1 * 2 + 1] - buf[i0 * 2 + 1]) * fr;
    }
}

// Is this track currently driven by a Session slot (which owns its own loop
// clock)? Such tracks are exempt from the transport-loop / stop voice flush.
static inline bool sessionActiveOf(const Track& t) {
    return t.sessionPlayer && t.sessionPlayer->playing.load(std::memory_order_relaxed) >= 0;
}

void Engine::processBlock(float* out, int32_t numFrames) {
    drainCommands();
    for (int32_t i = 0; i < numFrames * 2; ++i) out[i] = 0.0f;

    const double sr = transport_.sampleRate();
    const double spb = transport_.samplesPerBeat();
    const bool playing = transport_.isPlaying();

    drainLiveMidi(transport_.playheadSamples() / spb, playing);

    Graph* g = liveGraph_.load(std::memory_order_acquire);

    // Flush stuck instrument voices on a play→stop edge (pause/stop): otherwise a
    // held note keeps ringing forever since its note-off never arrives.
    if (!playing && renderWasPlaying_ && g)
        for (auto& tptr : g->tracks)
            if (tptr->instrument && !sessionActiveOf(*tptr)) {
                tptr->instrument->allNotesOff();
                for (auto& md : tptr->midiEffects) if (md) md->reset();   // flush arp held/pending
            }
    renderWasPlaying_ = playing;

    // Split the block at loop boundaries so the loop is sample-accurate: nothing
    // past the loop end sounds, and the playhead lands exactly on loopStart so a
    // note at the loop start (beat 0) retriggers every cycle.
    int32_t done = 0;
    int32_t loopSeams[16]; int32_t nLoopSeams = 0;   // block offsets where the loop wrapped
    while (done < numFrames) {
        if (playing && transport_.isLooping()
            && transport_.playheadSamples() >= transport_.loopEndSamples() - 0.5) {
            transport_.wrapToLoopStart();
            if (g) for (auto& tptr : g->tracks)
                if (tptr->instrument && !sessionActiveOf(*tptr)) {
                    tptr->instrument->allNotesOff();
                    for (auto& md : tptr->midiEffects) if (md) md->reset();
                }
            if (nLoopSeams < 16) loopSeams[nLoopSeams++] = done;   // declick this seam below
        }
        int32_t seg = std::min(numFrames - done, kMaxBlock);
        if (playing && transport_.isLooping()) {
            const double toEnd = transport_.loopEndSamples() - transport_.playheadSamples();
            if (toEnd > 0.5 && toEnd < seg) seg = std::max(1, static_cast<int32_t>(std::lround(toEnd)));
        }
        const double segStart = transport_.playheadSamples();
        if (g && sr > 0.0) mixGraph(g, out + done * 2, seg, segStart, playing, spb);
        if (playing) renderMetronome(out + done * 2, seg, segStart);
        transport_.advanceBy(seg);
        done += seg;
    }

    // Declick each loop seam: the playhead jumps and sounding voices are cut at the
    // wrap, so the mix has a hard discontinuity there. Fade the mix down over the last
    // ~1.5 ms before the seam and up over the first ~1.5 ms after it — a tiny notch that
    // removes the click for instruments and audio alike, every cycle.
    if (nLoopSeams > 0) {
        const int32_t kD = std::max(8, std::min(96, static_cast<int32_t>(sr * 0.0015)));
        for (int32_t si = 0; si < nLoopSeams; ++si) {
            const int32_t s = loopSeams[si];
            const int32_t kb = std::min(kD, s), ka = std::min(kD, numFrames - s);
            for (int32_t m = 0; m < kb; ++m) {                     // fade out into the seam
                const double gN = 0.5 - 0.5 * std::cos(kTwoPi * 0.5 * (double)(kb - m) / (kb + 1));
                out[(s - kb + m) * 2] *= (float)gN; out[(s - kb + m) * 2 + 1] *= (float)gN;
            }
            for (int32_t m = 0; m < ka; ++m) {                     // fade in out of the seam
                const double gN = 0.5 - 0.5 * std::cos(kTwoPi * 0.5 * (double)(m + 1) / (ka + 1));
                out[(s + m) * 2] *= (float)gN; out[(s + m) * 2 + 1] *= (float)gN;
            }
        }
    }

    renderTone(out, numFrames);
    renderPreview(out, numFrames); // audition voice (M7-4a), pre-master

    // Master effect chain (post-mix, pre-fader): run the whole mix through the master
    // track's devices, in kMaxBlock chunks so each device sees at most a full block.
    if (g && g->masterTrack) {
        // The master runs after the transport advanced past this block; recover the
        // block's start beat so tempo-synced master devices line up.
        const double masterStartBeat = spb > 0.0 ? transport_.positionBeats() - numFrames / spb : 0.0;
        for (auto& d : g->masterTrack->devices) {
            if (!d || d->bypassed()) continue;
            for (int32_t off = 0; off < numFrames; off += kMaxBlock) {
                const int32_t n = std::min(numFrames - off, kMaxBlock);
                if (spb > 0.0) d->setTransport(masterStartBeat + off / spb, spb, playing);
                d->process(out + off * 2, n);
            }
        }
    }

    const float mv = masterVolume_.load(std::memory_order_relaxed);
    float mpkL = 0.0f, mpkR = 0.0f;
    double msqL = 0.0, msqR = 0.0;
    for (int32_t i = 0; i < numFrames; ++i) {
        // Master safety net: a NaN slips straight through std::clamp (every compare
        // with NaN is false, so clamp returns it unchanged) and hits the speakers as
        // full-scale noise. Squash any non-finite sample to silence *before* clamping
        // so a single bad value from a device/feedback path can't blast the output.
        float l = out[i * 2]     * mv;
        float r = out[i * 2 + 1] * mv;
        if (!std::isfinite(l)) l = 0.0f;
        if (!std::isfinite(r)) r = 0.0f;
        l = std::clamp(l, -1.0f, 1.0f);
        r = std::clamp(r, -1.0f, 1.0f);
        out[i * 2] = l; out[i * 2 + 1] = r;
        const float al = std::fabs(l), ar = std::fabs(r);
        if (al > mpkL) mpkL = al;
        if (ar > mpkR) mpkR = ar;
        msqL += l * (double)l; msqR += r * (double)r;
    }
    masterPeakL_.store(mpkL, std::memory_order_relaxed);
    masterPeakR_.store(mpkR, std::memory_order_relaxed);
    masterRmsL_.store(numFrames ? static_cast<float>(std::sqrt(msqL / numFrames)) : 0.0f, std::memory_order_relaxed);
    masterRmsR_.store(numFrames ? static_cast<float>(std::sqrt(msqR / numFrames)) : 0.0f, std::memory_order_relaxed);
}

// Fill `dst` with what a monitoring audio track hears: the hardware input pulled for this
// segment, or its source track's previous-block post-fader route tap. Guards against the
// obvious feedback loops (itself, master, or a group it sits inside) by staying silent.
void Engine::renderMonitorInput(Graph* g, const Track& t, float* dst, int32_t frames) {
    const int32_t src = t.recordInputSource();
    if (src == 0) { std::copy_n(monitorHwBuf_.data(), frames * 2, dst); return; }
    std::fill_n(dst, frames * 2, 0.0f);
    if (src < 0 || src == t.id()) return;
    for (int32_t a = t.groupId(), hops = 0; a >= 0 && hops < 64; ++hops) {   // ancestor group?
        if (a == src) return;
        int32_t parent = -1;
        for (auto& o : g->tracks) if (o->id() == a) { parent = o->groupId(); break; }
        a = parent;
    }
    const int32_t slot = routeSlotForTrack(src);
    if (slot >= 0) std::copy_n(routeBus_[slot].data(), frames * 2, dst);
}

void Engine::mixGraph(Graph* g, float* out, int32_t frames, double blockStart, bool playing, double spb) {
    {
        // Automation (M9): read-mode block-rate eval — write each active lane's
        // value into its target atomic before the mix reads it. Always runs (also
        // when stopped: the playhead is static, so scrub shows automation too).
        if (spb > 0.0) applyAutomation(g, blockStart / spb);

        // CV modulation (Phase 3): drive modulated device params to base∘mod for this
        // segment. The device atomic holds the live modulated value continuously (no
        // restore), so the UI shows the movement smoothly; the base lives in the link.
        if (spb > 0.0) {
            const double srMod = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
            updateModulators(g, blockStart / spb, blockStart / srMod, frames / srMod);   // stateful + Math
            applyModulation(g, blockStart / spb, blockStart / srMod);
        }

        // Transport snapshot for this segment: pushed to every instrument/device
        // before it runs so hosted plugins drive their own tempo-synced behaviour off
        // the DAW clock (AudioPlayHead). Built-ins ignore it; racks forward to children.
        const TransportInfo ti = transport_.transportInfo(blockStart);

        // Internal resampling (record from another track/send/master): tap the source's
        // post-fader output into the input ring while a take is rolling.
        const int32_t recSrc = internalRecordSource_.load(std::memory_order_relaxed);

        // Live input monitoring: this segment's hardware input (shared by every track
        // monitoring it), and the set of tracks whose output some monitoring track hears —
        // those keep rendering while solo-gated, feeding only their route tap / record tap.
        pullMonitorInput(frames);
        constexpr int kMaxFeeds = 16;
        int32_t feedIds[kMaxFeeds]; int numFeeds = 0;
        if (recSrc > 0) feedIds[numFeeds++] = recSrc;
        for (auto& t : g->tracks)
            if (t->type() == TrackType::Audio && t->monitor() && t->recordInputSource() > 0 && numFeeds < kMaxFeeds)
                feedIds[numFeeds++] = t->recordInputSource();
        auto feedsMonitor = [&](int32_t id) {
            for (int i = 0; i < numFeeds; ++i) if (feedIds[i] == id) return true;
            return false;
        };
        bool anySolo = false;
        int32_t numReturns = 0;
        for (auto& t : g->tracks) {
            if (t->solo()) anySolo = true;
            if (t->type() == TrackType::Return) ++numReturns;
        }
        if (numReturns > kMaxReturns) numReturns = kMaxReturns;
        for (int b = 0; b < numReturns; ++b)          // clear send buses for this block (M6-1)
            std::fill_n(returnBus_[b].data(), frames * 2, 0.0f);
        // Phase B/D: clear this block's route-write buffers (post + pre) for active slots.
        for (int s = 0; s < kMaxRoutes; ++s)
            if (routeSlotTrackId_[s].load(std::memory_order_relaxed) >= 0) {
                std::fill_n(routeBusNext_[s].data(), frames * 2, 0.0f);
                std::fill_n(routeBusPreNext_[s].data(), frames * 2, 0.0f);
            }

        // Group submix setup: discover Group tracks, their effective (ancestor-folded)
        // mute/solo, and depth. Leaves fold into their parent group's bus (else master);
        // groups are then processed deepest-first so children land before their parent.
        Track*  grpT[kMaxGroups];
        int32_t grpId[kMaxGroups], grpParent[kMaxGroups], grpDepth[kMaxGroups];
        bool    grpMuteE[kMaxGroups], grpSoloE[kMaxGroups];
        int     numGroups = 0, maxDepth = 0;
        for (auto& tptr : g->tracks) {
            if (tptr->type() != TrackType::Group || numGroups >= kMaxGroups) continue;
            grpT[numGroups] = tptr.get();
            grpId[numGroups] = tptr->id();
            grpParent[numGroups] = tptr->groupId();
            ++numGroups;
        }
        auto slotOf = [&](int32_t id) -> int {
            if (id < 0) return -1;
            for (int i = 0; i < numGroups; ++i) if (grpId[i] == id) return i;
            return -1;
        };
        for (int i = 0; i < numGroups; ++i) {
            bool m = false, s = false; int depth = 0;
            for (int32_t a = grpId[i]; a >= 0; ) {           // walk self + ancestor groups
                int si = slotOf(a); if (si < 0) break;
                m |= grpT[si]->mute(); s |= grpT[si]->solo(); ++depth;
                a = grpParent[si];
            }
            grpMuteE[i] = m; grpSoloE[i] = s; grpDepth[i] = depth;
            if (depth > maxDepth) maxDepth = depth;
        }
        for (int i = 0; i < numGroups; ++i) std::fill_n(groupBus_[i].data(), frames * 2, 0.0f);
        // A leaf's effective mute/solo folds in any ancestor group's flag.
        auto leafMute = [&](Track& t) { if (t.mute()) return true; int s = slotOf(t.groupId()); return s >= 0 && grpMuteE[s]; };
        auto leafSolo = [&](Track& t) { if (t.solo()) return true; int s = slotOf(t.groupId()); return s >= 0 && grpSoloE[s]; };

        // Session vs Arrangement (M5): when the Arrangement isn't active (a session-only jam),
        // tracks with no launched session clip stay silent instead of playing their timeline.
        const bool arrangementActive = arrangementActive_.load(std::memory_order_relaxed);

        // MIDI pre-pass: each instrument track's post-FX output for this block, so the render
        // below can feed instruments their own notes plus any routed-in source's notes.
        computeBlockMidi(g, frames, blockStart, spb, playing, arrangementActive);

        // Pass 1: leaf tracks -> parent group bus (or master), tapping post-fader sends.
        for (auto& tptr : g->tracks) {
            Track& t = *tptr;
            if (t.type() == TrackType::Return) continue;  // return buses run in pass 2
            if (t.type() == TrackType::Group) continue;   // group submixes run in pass 1.5
            // Freeze (M7): a frozen track plays its captured buffer instead of the live
            // instrument + device chain (sounds only while rolling, like an audio clip).
            const bool frozenActive = t.frozen() && t.frozenBuf;
            // While this track is being frozen, render + capture it regardless of the
            // mixer mute/solo gate (freeze captures the sound the track makes, not the mix)
            // — otherwise a soloed sibling would punch silent gaps into the buffer.
            const bool capturing = (t.id() == freezeCaptureTrackId_.load(std::memory_order_relaxed));
            const bool audible = !leafMute(t) && (!anySolo || leafSolo(t));
            // Soloing a monitoring (or recording) track must not silence what it listens to.
            const bool feeding = !audible && !leafMute(t) && feedsMonitor(t.id());
            if (!audible && !capturing && !feeding) { t.setMeter(0, 0, 0, 0); continue; }
            const bool toMix = audible || capturing;   // false: render only for the taps
            // Destination: this leaf's parent group's submix bus, else the master mix.
            const int destSlot = slotOf(t.groupId());
            float* dest = destSlot >= 0 ? groupBus_[destSlot].data() : out;

            // 0) Session view (M5-2): apply quantized launch/stop; a playing slot
            //    overrides this track's arrangement content. (Skipped for a frozen or
            //    capturing track — the buffer/arrangement is the source of truth.)
            // Input monitoring (Monitor "In"): the track plays its record-input source live
            // in place of its clips — hardware input this segment, or the source track's
            // post-fader tap from the previous one. Master (-1) would feed back: silent.
            const bool monitoring = !frozenActive && !capturing && t.type() == TrackType::Audio && t.monitor();

            bool sessionActive = false;
            if (!frozenActive && !capturing && !monitoring) if (auto& sp = t.sessionPlayer) {
                if (sp->maybeApply(blockStart / spb, frames / spb, launchQuant_.load(std::memory_order_relaxed))) {
                    if (t.instrument) t.instrument->allNotesOff();
                    for (auto& md : t.midiEffects) if (md) md->reset();   // clock switch → flush arp
                }
                sessionActive = sp->playing.load(std::memory_order_relaxed) >= 0;
            }

            // 1) Render the track's dry stereo (pre-fader) into scratch_.
            if (frozenActive) {
                if (!playing) { t.setMeter(0, 0, 0, 0); continue; } // frozen sounds only while rolling
                fillFrozen(t, scratch_.data(), frames, blockStart, spb);
            } else {
                if (t.instrument) {
                    t.instrument->setTransportInfo(ti);   // hosted-plugin sync
                    // React (Nota Flux): hand a listening instrument its source track's
                    // previous-block, post-fader signal just before it renders.
                    const int32_t iSc = t.instrument->sidechainSourceTrackId();
                    if (iSc >= 0) {
                        const int32_t s = routeSlotForTrack(iSc);
                        t.instrument->setSidechain(s < 0 ? nullptr : routeBus_[s].data(), frames);
                    }
                }
                if (monitoring)
                    renderMonitorInput(g, t, scratch_.data(), frames);
                else if (sessionActive && t.type() == TrackType::Instrument)
                    renderSessionSlotRaw(t, scratch_.data(), frames, spb);
                else if (sessionActive && t.type() == TrackType::Audio)
                    renderSessionAudioSlotRaw(t, scratch_.data(), frames, spb);
                else if (t.type() == TrackType::Instrument)
                    renderInstrumentRaw(g, t, scratch_.data(), frames, blockStart, spb, playing);
                else if (playing && arrangementActive)
                    renderAudioClipsRaw(t.clips, scratch_.data(), frames, blockStart, spb);
                else
                    { t.setMeter(0, 0, 0, 0); continue; } // stopped / session-only audio track: nothing to do
            }

            // 1b) Pre-FX tap (Phase D): if this track feeds a sidechain, capture its
            //     dry pre-fader signal now, before its own devices colour it.
            {
                const int32_t preSlot = routeSlotForTrack(t.id());
                if (preSlot >= 0) {
                    float* pre = routeBusPreNext_[preSlot].data();
                    for (int32_t i = 0; i < frames * 2; ++i) pre[i] += scratch_[i];
                }
            }

            // 2) Insert effect chain (M3), in place. Hand each sidechain consumer
            //    its source track's signal (previous block) just before it runs —
            //    the pre-FX or post-FX tap per the device's setting (Phase D). A frozen
            //    track skips this: its buffer already baked in the devices.
            if (!frozenActive)
            for (auto& d : t.devices)
                if (d && !d->bypassed()) {
                    const int32_t src = d->sidechainSourceTrackId();
                    if (src >= 0) {
                        const int32_t s = routeSlotForTrack(src);
                        const float* sc = s < 0 ? nullptr
                            : (d->sidechainTapPre() ? routeBusPre_[s].data() : routeBus_[s].data());
                        d->setSidechain(sc, frames);
                        if (d->wantsMidiKey()) feedMidiKey(g, *d, src);
                    }
                    if (spb > 0.0) d->setTransport(blockStart / spb, spb, playing);  // tempo-synced devices
                    d->setTransportInfo(ti);                                          // hosted-plugin sync
                    d->process(scratch_.data(), frames);
                }

            // 2b) Plugin delay compensation (M3-7): align to the latest track.
            if (!frozenActive && t.pdc) t.pdc->process(scratch_.data(), frames);

            // 2c) Freeze capture (M7): stash this track's post-device, pre-fader signal
            //     into the freeze buffer. Sequential from a seek(0) render, so the write
            //     cursor tracks the playhead frame.
            if (capturing) {
                int64_t c = freezeCursor_;
                for (int32_t i = 0; i < frames && c < freezeCapFrames_; ++i, ++c) {
                    freezeCaptureBuf_[c * 2]     = scratch_[i * 2];
                    freezeCaptureBuf_[c * 2 + 1] = scratch_[i * 2 + 1];
                }
                freezeCursor_ = c;
            }

            // 3) Channel fader + equal-power pan into the master mix + sends.
            const float vol = t.volume();
            const float pan = std::clamp(t.pan(), -1.0f, 1.0f);
            const double theta = (pan + 1.0) * (kTwoPi / 8.0); // 0..pi/2
            const float gl = static_cast<float>(std::cos(theta)) * vol;
            const float gr = static_cast<float>(std::sin(theta)) * vol;
            float sends[kMaxReturns]; bool anySend = false;   // post-fader send taps (M6-1)
            for (int b = 0; b < numReturns; ++b) { sends[b] = t.send(b); if (sends[b] != 0.0f) anySend = true; }
            const int32_t routeSlot = routeSlotForTrack(t.id());   // Phase B: this track is a sidechain source?
            float* route = routeSlot >= 0 ? routeBusNext_[routeSlot].data() : nullptr;
            const bool tapRec = (recSrc > 0 && t.id() == recSrc);   // internal resampling source
            float pkL = 0.0f, pkR = 0.0f;
            double sqL = 0.0, sqR = 0.0;
            int64_t recDropped = 0;
            for (int32_t i = 0; i < frames; ++i) {
                const float l = scratch_[i * 2]     * gl;
                const float r = scratch_[i * 2 + 1] * gr;
                if (tapRec && !inputQueue_.push(InputFrame{ l, r })) ++recDropped;   // record this track's output
                if (route) { route[i * 2] += l; route[i * 2 + 1] += r; }   // tap post-fader → route bus
                if (!toMix) continue;                                         // solo-gated feeder: taps only
                dest[i * 2]     += l;
                dest[i * 2 + 1] += r;
                if (anySend)
                    for (int b = 0; b < numReturns; ++b) if (sends[b] != 0.0f) {
                        returnBus_[b][i * 2]     += l * sends[b];
                        returnBus_[b][i * 2 + 1] += r * sends[b];
                    }
                const float al = std::fabs(l), ar = std::fabs(r);
                if (al > pkL) pkL = al;
                if (ar > pkR) pkR = ar;
                sqL += l * (double)l;
                sqR += r * (double)r;
            }
            if (recDropped) inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
            if (!toMix) { t.setMeter(0, 0, 0, 0); continue; }
            t.setMeter(pkL, pkR,
                       static_cast<float>(std::sqrt(sqL / frames)),
                       static_cast<float>(std::sqrt(sqR / frames)));
        }

        // Pass 1.5: group submixes. Process deepest-first so a nested group folds into its
        // parent's bus before the parent reads it; top-level groups fold into the master.
        for (int depth = maxDepth; depth >= 1; --depth)
        for (int gi = 0; gi < numGroups; ++gi) {
            if (grpDepth[gi] != depth) continue;
            Track& t = *grpT[gi];
            if (grpMuteE[gi]) { t.setMeter(0, 0, 0, 0); continue; }   // muted group (or muted ancestor)

            std::copy_n(groupBus_[gi].data(), frames * 2, scratch_.data());   // this group's summed children
            {
                const int32_t preSlot = routeSlotForTrack(t.id());   // Phase D pre-FX tap
                if (preSlot >= 0) {
                    float* pre = routeBusPreNext_[preSlot].data();
                    for (int32_t i = 0; i < frames * 2; ++i) pre[i] += scratch_[i];
                }
            }
            for (auto& d : t.devices)
                if (d && !d->bypassed()) {
                    const int32_t src = d->sidechainSourceTrackId();
                    if (src >= 0) {
                        const int32_t s = routeSlotForTrack(src);
                        const float* sc = s < 0 ? nullptr
                            : (d->sidechainTapPre() ? routeBusPre_[s].data() : routeBus_[s].data());
                        d->setSidechain(sc, frames);
                        if (d->wantsMidiKey()) feedMidiKey(g, *d, src);
                    }
                    if (spb > 0.0) d->setTransport(blockStart / spb, spb, playing);
                    d->setTransportInfo(ti);
                    d->process(scratch_.data(), frames);
                }
            if (t.pdc) t.pdc->process(scratch_.data(), frames);

            const float vol = t.volume();
            const float pan = std::clamp(t.pan(), -1.0f, 1.0f);
            // Unity-center balance (not the −3 dB equal-power leaf law) so that grouping is
            // level-transparent: the group just sums its already-panned children and passes
            // them through at unity when centred.
            const float gl = (pan <= 0.0f ? 1.0f : 1.0f - pan) * vol;
            const float gr = (pan >= 0.0f ? 1.0f : 1.0f + pan) * vol;
            const int   pslot = slotOf(grpParent[gi]);
            float* dest = pslot >= 0 ? groupBus_[pslot].data() : out;   // parent group bus, else master
            const int32_t routeSlot = routeSlotForTrack(t.id());
            float* route = routeSlot >= 0 ? routeBusNext_[routeSlot].data() : nullptr;
            const bool tapRec = (recSrc > 0 && t.id() == recSrc);
            float pkL = 0.0f, pkR = 0.0f;
            double sqL = 0.0, sqR = 0.0;
            int64_t recDropped = 0;
            for (int32_t i = 0; i < frames; ++i) {
                const float l = scratch_[i * 2]     * gl;
                const float r = scratch_[i * 2 + 1] * gr;
                dest[i * 2]     += l;
                dest[i * 2 + 1] += r;
                if (tapRec && !inputQueue_.push(InputFrame{ l, r })) ++recDropped;
                if (route) { route[i * 2] += l; route[i * 2 + 1] += r; }
                const float al = std::fabs(l), ar = std::fabs(r);
                if (al > pkL) pkL = al;
                if (ar > pkR) pkR = ar;
                sqL += l * (double)l;
                sqR += r * (double)r;
            }
            if (recDropped) inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
            t.setMeter(pkL, pkR,
                       static_cast<float>(std::sqrt(sqL / frames)),
                       static_cast<float>(std::sqrt(sqR / frames)));
        }

        // Pass 2: return buses -> device chain -> master (M6-1). Exempt from the
        // solo gate (a soloed track's reverb/delay still sounds); a return only
        // honours its own mute. Runs even when stopped so effect tails ring out.
        for (auto& tptr : g->tracks) {
            Track& t = *tptr;
            if (t.type() != TrackType::Return) continue;
            const int32_t bus = t.returnIndex();
            if (bus < 0 || bus >= numReturns || t.mute()) { t.setMeter(0, 0, 0, 0); continue; }

            std::copy_n(returnBus_[bus].data(), frames * 2, scratch_.data());
            {
                const int32_t preSlot = routeSlotForTrack(t.id());   // Phase D pre-FX tap
                if (preSlot >= 0) {
                    float* pre = routeBusPreNext_[preSlot].data();
                    for (int32_t i = 0; i < frames * 2; ++i) pre[i] += scratch_[i];
                }
            }
            for (auto& d : t.devices)
                if (d && !d->bypassed()) {
                    const int32_t src = d->sidechainSourceTrackId();
                    if (src >= 0) {
                        const int32_t s = routeSlotForTrack(src);
                        const float* sc = s < 0 ? nullptr
                            : (d->sidechainTapPre() ? routeBusPre_[s].data() : routeBus_[s].data());
                        d->setSidechain(sc, frames);
                        if (d->wantsMidiKey()) feedMidiKey(g, *d, src);
                    }
                    if (spb > 0.0) d->setTransport(blockStart / spb, spb, playing);
                    d->setTransportInfo(ti);                                          // hosted-plugin sync
                    d->process(scratch_.data(), frames);
                }
            if (t.pdc) t.pdc->process(scratch_.data(), frames);

            const float vol = t.volume();
            const float pan = std::clamp(t.pan(), -1.0f, 1.0f);
            const double theta = (pan + 1.0) * (kTwoPi / 8.0);
            const float gl = static_cast<float>(std::cos(theta)) * vol;
            const float gr = static_cast<float>(std::sin(theta)) * vol;
            const int32_t routeSlot = routeSlotForTrack(t.id());
            float* route = routeSlot >= 0 ? routeBusNext_[routeSlot].data() : nullptr;
            const bool tapRec = (recSrc > 0 && t.id() == recSrc);   // internal resampling source (a return)
            float pkL = 0.0f, pkR = 0.0f;
            double sqL = 0.0, sqR = 0.0;
            int64_t recDropped = 0;
            for (int32_t i = 0; i < frames; ++i) {
                const float l = scratch_[i * 2]     * gl;
                const float r = scratch_[i * 2 + 1] * gr;
                out[i * 2]     += l;
                out[i * 2 + 1] += r;
                if (tapRec && !inputQueue_.push(InputFrame{ l, r })) ++recDropped;
                if (route) { route[i * 2] += l; route[i * 2 + 1] += r; }
                const float al = std::fabs(l), ar = std::fabs(r);
                if (al > pkL) pkL = al;
                if (ar > pkR) pkR = ar;
                sqL += l * (double)l;
                sqR += r * (double)r;
            }
            if (recDropped) inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
            t.setMeter(pkL, pkR,
                       static_cast<float>(std::sqrt(sqL / frames)),
                       static_cast<float>(std::sqrt(sqR / frames)));
        }

        // Internal resampling from the master bus: capture the fully-mixed output.
        if (recSrc == -1) {
            int64_t recDropped = 0;
            for (int32_t i = 0; i < frames; ++i)
                if (!inputQueue_.push(InputFrame{ out[i * 2], out[i * 2 + 1] })) ++recDropped;
            if (recDropped) inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
        }

        // Phase B/D: publish this block's route taps (post + pre) for next block.
        for (int s = 0; s < kMaxRoutes; ++s)
            if (routeSlotTrackId_[s].load(std::memory_order_relaxed) >= 0) {
                routeBus_[s].swap(routeBusNext_[s]);
                routeBusPre_[s].swap(routeBusPreNext_[s]);
            }
    }
}

void Engine::renderMetronome(float* out, int32_t numFrames, double blockStartSamples) {
    if (!transport_.metronomeEnabled()) return;
    const double sr = transport_.sampleRate();
    const double spb = transport_.samplesPerBeat();
    const int beatsPerBar = transport_.beatsPerBar();
    const int clickLen = static_cast<int>(0.03 * sr);

    for (int32_t i = 0; i < numFrames; ++i) {
        const double p = blockStartSamples + i;
        if (p >= 0.0) {
            const int64_t beat = static_cast<int64_t>(p / spb);
            if (beat != lastBeatEmitted_) {
                lastBeatEmitted_ = beat;
                clickRemaining_ = clickLen;
                clickPhase_ = 0.0;
                clickFreq_ = (beat % beatsPerBar == 0) ? 1500.0 : 1000.0;
            }
        }
        if (clickRemaining_ > 0) {
            const float env = static_cast<float>(clickRemaining_) / static_cast<float>(clickLen);
            const float s = static_cast<float>(std::sin(clickPhase_)) * env * kMetroGain;
            out[i * 2] += s; out[i * 2 + 1] += s;
            clickPhase_ += kTwoPi * clickFreq_ / sr;
            if (clickPhase_ >= kTwoPi) clickPhase_ -= kTwoPi;
            --clickRemaining_;
        }
    }
}

void Engine::renderTone(float* out, int32_t numFrames) {
    const double sr = transport_.sampleRate();
    const float target = toneEnabled_ ? 1.0f : 0.0f;
    if (sr <= 0.0 || (target == 0.0f && toneLevel_ == 0.0f)) { tonePhase_ = 0.0; return; }
    const double inc = kTwoPi * static_cast<double>(frequency_) / sr;
    const float step = static_cast<float>(1.0 / (sr * 0.01));   // ~10 ms fade in/out
    for (int32_t i = 0; i < numFrames; ++i) {
        toneLevel_ = target > toneLevel_ ? std::min(target, toneLevel_ + step) : std::max(target, toneLevel_ - step);
        const float s = static_cast<float>(std::sin(tonePhase_)) * kToneGain * toneLevel_;
        out[i * 2] += s; out[i * 2 + 1] += s;
        tonePhase_ += inc;
        if (tonePhase_ >= kTwoPi) tonePhase_ -= kTwoPi;
    }
}


} // namespace nota
