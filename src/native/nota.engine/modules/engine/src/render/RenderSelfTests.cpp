// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — realtime render core: poll, block render, offline bounce, command/live-MIDI drain, per-track raw renderers, mixer processBlock, metronome & test tone. RT-hot; do not add allocations.

#include "core/EngineRuntime.h"
#include "tracks/TrackOps.h"
#include "session/SessionOps.h"
#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/EngineTypes.h"
#include "model/ArticulationTable.h"
#include "render/RenderHelpers.h"
#include "model/ArticulationSchedule.h"
#include "render/RenderGraphExecutor.h"
#include "render/RenderRoutingPlan.h"
#include "model/TimelineIndex.h"
#include "core/EnginePrelude.h"
#include "nota/devices/racks/RackCore.h"       // IWYU pragma: keep
#include "nota/devices/racks/RackInstrument.h" // IWYU pragma: keep
#include "session/SessionPlayer.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <vector>
#include <utility>
#include <memory>

namespace nota::detail {

// #436: an instrument that takes timed events (a hosted plug-in) renders once per block, each
// event stamped with its offset, in the same order the segmented render delivers them; an
// instrument that doesn't is still rendered in segments with every event at offset 0.
bool EngineRuntime::timedEventsSelfTest() {
    struct Probe final : Instrument {
        bool timed = false;
        std::vector<int32_t> renders;               // frames per render() call
        std::vector<std::array<int32_t, 4>> events; // kind (1 on, 0 off, 2 ctl, 3 live), pitch, offset, renders so far
        bool takesTimedEvents() const override { return timed; }
        void setSampleRate(double) override {}
        void noteOn(int32_t p, float) override {
            events.push_back({1, p, eventOffset, static_cast<int32_t>(renders.size())});
        }
        void noteOff(int32_t p) override {
            events.push_back({0, p, eventOffset, static_cast<int32_t>(renders.size())});
        }
        void allNotesOff() override {}
        void controller(int32_t ctl, float) override {
            events.push_back({2, ctl, eventOffset, static_cast<int32_t>(renders.size())});
        }
        void render(float*, int32_t frames) override { renders.push_back(frames); }
    };
    constexpr int32_t frames = 64;
    // Off before on at a shared offset (midiEvLess order); the last note-off sits on the block's end.
    const MidiEv evs[] = {{0, true, 60, 1.0f, 0.0f}, {10, false, 60, 0.0f, 0.0f}, {10, true, 62, 1.0f, 0.0f},
        {frames, false, 62, 0.0f, 0.0f}};
    const ExprEv xev[] = {{20, -1, kCtlDimBase + 1, 0.5f}};
    float dst[frames * 2] = {};
    auto run = [&](Probe& p) {
        playEvents(p, dst, frames, evs, 4, xev, 1, nullptr, 0, 0.0, 0.0, false,
            [&] { p.events.push_back({3, -1, p.eventOffset, static_cast<int32_t>(p.renders.size())}); });
    };
    using E = std::array<int32_t, 4>;

    Probe timed;
    timed.timed = true;
    run(timed);
    const std::vector<E> wantTimed = {
        {1, 60, 0, 0}, {3, -1, 0, 0}, {0, 60, 10, 0}, {1, 62, 10, 0}, {2, 1, 20, 0}, {0, 62, 0, 1}};
    if (timed.renders != std::vector<int32_t>{frames} || timed.events != wantTimed || timed.eventOffset != 0)
        return false;

    Probe seg;
    run(seg);
    const std::vector<E> wantSeg = {
        {1, 60, 0, 0}, {3, -1, 0, 0}, {0, 60, 0, 1}, {1, 62, 0, 1}, {2, 1, 0, 2}, {0, 62, 0, 3}};
    return seg.renders == std::vector<int32_t>{10, 10, 44} && seg.events == wantSeg;
}

// specs/085: the articulation scheduler, block by block at exact samples, and delivery through
// playEvents. Returns 0 on success, else the number of the first check that failed.
int32_t EngineRuntime::articulationSelfTest() {
    constexpr double sr = 48000.0, spb = 24000.0; // 120 BPM
    constexpr int32_t B = 512;
    using AK = ArticulationActionKind;
    // 1 Long: latching, keyswitches 24/1 + 35/1 + CC58=81. 2 Legato: latching, CC58=76, lead 30 ms,
    // offset -150 ms. 3 Pizz: momentary, keyswitch 30/127, channel 2. Default = 1.
    const ArticulationAction actions[] = {
        {AK::Keyswitch, 0, 24, 1},
        {AK::Keyswitch, 0, 35, 1},
        {AK::ControlChange, 0, 58, 81},
        {AK::ControlChange, 0, 58, 76},
        {AK::Keyswitch, 2, 30, 127},
    };
    const ArticulationEntry entries[] = {
        {1, ArticulationTrigger::Latching, 0.0, 0.0, 0, 3},
        {2, ArticulationTrigger::Latching, -150.0, 30.0, 3, 1},
        {3, ArticulationTrigger::Momentary, 0.0, 0.0, 4, 1},
    };
    auto table = ArticulationTable::build(1, entries, 3, actions, 5);
    if (!table) return 1;

    auto makeClip = [&](std::vector<std::pair<double, int32_t>> notes, double len = 1.0) {
        MidiClip c;
        c.startBeat = 0.0;
        c.lengthBeats = 8.0;
        for (auto [beat, art] : notes) {
            Note n;
            n.pitch = 60;
            n.startBeat = beat;
            n.lengthBeats = len;
            n.articulation = art;
            c.notes.push_back(n);
        }
        c.articulations = buildArticulationTimeline(c, table);
        return c;
    };
    // Beat 0 Long, beat 1 Long again (no new switch), beat 2 Legato, beat 3 Pizz (momentary),
    // beat 4 a note with no articulation (the default: Long again, a change from Legato).
    std::vector<MidiClip> clips{makeClip({{0.0, 1}, {1.0, 1}, {2.0, 2}, {3.0, 3}, {4.0, 0}})};

    struct Abs {
        double at;
        ArtEvKind kind;
        int d1, d2, ch;
    };
    // Play [from, to) in blocks; the first block is a chase when `chase`.
    auto play = [&](double from, double to, double trackMs, bool chase, std::vector<Abs>& got, int* dropped = nullptr) {
        ArtEv buf[kMaxBlockArt];
        for (double s = from; s < to; s += B) {
            ArticulationBlock b{B, s, spb, sr, trackMs, chase && s == from, false, 0.0};
            b.chaseStart = chase ? from : -1.0;
            int d = 0;
            const int n = scheduleArticulations(clips, *table, b, buf, kMaxBlockArt, &d);
            if (dropped) *dropped += d;
            for (int i = 0; i < n; ++i)
                got.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        }
    };
    auto count = [](const std::vector<Abs>& v, ArtEvKind k, int d1) {
        int c = 0;
        for (auto& e : v) c += e.kind == k && e.d1 == d1;
        return c;
    };
    auto at = [](const std::vector<Abs>& v, ArtEvKind k, int d1, int d2 = -1) {
        for (auto& e : v)
            if (e.kind == k && e.d1 == d1 && (d2 < 0 || e.d2 == d2)) return e.at;
        return -1.0;
    };

    // ---- a straight run from the start ----
    std::vector<Abs> run;
    play(0.0, 6 * spb, 0.0, true, run);
    // 2: Long at 0 in map order — key 24, key 35, CC58=81 — and its keys released 20 ms later.
    if (run.size() < 3 || run[0].at != 0.0 || run[0].kind != ArtEvKind::KeyOn || run[0].d1 != 24 || run[1].d1 != 35 ||
        run[2].kind != ArtEvKind::Control || run[2].d1 != 58 || run[2].d2 != 81)
        return 2;
    if (at(run, ArtEvKind::KeyOff, 24) != 960.0 || at(run, ArtEvKind::KeyOff, 35) != 960.0) return 3;
    // 4: the second Long note sends nothing (still in force): Long's keys go out twice in all — at
    // beat 0 and again at beat 4 where the default (Long) follows Legato.
    if (count(run, ArtEvKind::KeyOn, 24) != 2) return 4;
    // 5: Legato: offset -150 ms and lead 30 ms → 48000 − 7200 − 1440 = 39360.
    if (at(run, ArtEvKind::Control, 58, 76) != 39360.0) return 5;
    // 6: Pizz is momentary on channel 2: pressed with its note (beat 3), released at its end.
    double pOn = -1, pOff = -1;
    for (auto& e : run)
        if (e.d1 == 30 && e.ch == 2) (e.kind == ArtEvKind::KeyOn ? pOn : pOff) = e.at;
    if (pOn != 72000.0 || pOff != 96000.0) return 6;
    // 7: the default after Legato: Long again at beat 4.
    if (at(run, ArtEvKind::Control, 58, 81) != 0.0) return 7; // first one at 0 …
    {
        int n81 = 0;
        for (auto& e : run) n81 += e.kind == ArtEvKind::Control && e.d1 == 58 && e.d2 == 81 && e.at == 96000.0;
        if (n81 != 1) return 7; // … and one at beat 4
    }
    // 8: nothing else: 2 × Long (3 actions + 2 releases), Legato (1), Pizz (on + off).
    if (run.size() != 2 * 5 + 1 + 2) return 8;

    // ---- a track offset moves every switch with the notes ----
    std::vector<Abs> shifted;
    play(0.0, 6 * spb, -100.0, true, shifted);
    if (at(shifted, ArtEvKind::Control, 58, 76) != 39360.0 - 4800.0) return 9;
    double sOn = -1;
    for (auto& e : shifted)
        if (e.d1 == 30 && e.kind == ArtEvKind::KeyOn) sOn = e.at;
    if (sOn != 72000.0 - 4800.0) return 10;

    // ---- a seek into the Legato phrase: what is in force goes out once, at the landing ----
    std::vector<Abs> seek;
    const double land = 2.5 * spb; // 60000: past Legato's switch (39360), before Pizz
    play(land, land + 4 * B, 0.0, true, seek);
    if (seek.size() != 1 || seek[0].at != land || seek[0].kind != ArtEvKind::Control || seek[0].d2 != 76) return 11;
    // 12: without a chase, nothing is re-sent mid-phrase.
    std::vector<Abs> quiet;
    play(land, land + 4 * B, 0.0, false, quiet);
    if (!quiet.empty()) return 12;
    // 13: landing inside a held momentary note chases its switch too.
    std::vector<Abs> inPizz;
    play(3.5 * spb, 3.5 * spb + B, 0.0, true, inPizz);
    if (count(inPizz, ArtEvKind::KeyOn, 30) != 1 || at(inPizz, ArtEvKind::KeyOn, 30) != 3.5 * spb) return 13;

    // ---- a loop wrap onto a change at the loop start: no stale chase ahead of it ----
    {
        std::vector<Abs> wrap;
        ArtEv buf[kMaxBlockArt];
        ArticulationBlock b{B, 4 * spb, spb, sr, 0.0, true, true, 6 * spb}; // looping 4..6, just wrapped
        const int n = scheduleArticulations(clips, *table, b, buf, kMaxBlockArt);
        for (int i = 0; i < n; ++i)
            wrap.push_back({b.blockStart + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        // 14: Long's change lands on the block's first sample; the chase (Legato, in force before) is skipped.
        if (count(wrap, ArtEvKind::Control, 58) != 1 || at(wrap, ArtEvKind::Control, 58, 81) != 4 * spb) return 14;
    }
    // 15: a seam lets go of a key pressed just before it.
    {
        std::vector<MidiClip> one{makeClip({{0.0, 1}})};
        ArtEv buf[kMaxBlockArt];
        ArticulationBlock b{B, 0.0, spb, sr, 0.0, false, true, 100.0}; // the key went down at 0, the seam came at 100
        const int n = scheduleArticulations(one, *table, b, buf, kMaxBlockArt);
        int offsAt0 = 0;
        for (int i = 0; i < n; ++i) offsAt0 += buf[i].kind == ArtEvKind::KeyOff && buf[i].off == 0;
        if (offsAt0 != 2) return 15;
    }
    // 16: releases sort before presses at the same sample.
    {
        std::vector<MidiClip> tight{makeClip({{0.0, 1}, {0.04, 3}}, 0.01)}; // Pizz 20 ms in = Long's release
        ArtEv buf[kMaxBlockArt];
        ArticulationBlock b{2048, 0.0, spb, sr, 0.0, false, false, 0.0};
        const int n = scheduleArticulations(tight, *table, b, buf, kMaxBlockArt);
        int firstOn = -1, lastOff = -1;
        for (int i = 0; i < n; ++i)
            if (buf[i].off == 960) {
                if (buf[i].kind == ArtEvKind::KeyOff) lastOff = i;
                else if (firstOn < 0)
                    firstOn = i;
            }
        if (firstOn < 0 || lastOff < 0 || lastOff > firstOn) return 16;
    }
    // 17: a full buffer drops and counts, never overruns.
    {
        ArtEv small[2];
        int dropped = 0;
        ArticulationBlock b{B, 0.0, spb, sr, 0.0, false, false, 0.0};
        const int n = scheduleArticulations(clips, *table, b, small, 2, &dropped);
        if (n != 2 || dropped != 1) return 17;
    }
    // 18: an inactive clip sends nothing.
    {
        std::vector<MidiClip> off = clips;
        off[0].active = false;
        ArtEv buf[kMaxBlockArt];
        ArticulationBlock b{B, 0.0, spb, sr, 0.0, true, false, 0.0};
        if (scheduleArticulations(off, *table, b, buf, kMaxBlockArt) != 0) return 18;
    }

    // ---- delivery: ahead of the note at the same offset, on its channel, channel reset after ----
    struct Probe final : Instrument {
        std::vector<std::array<int32_t, 5>> events; // kind (1 on, 0 off, 2 cc, 3 program), d1, d2, offset, channel
        bool articulations = false;
        bool takesTimedEvents() const override { return true; }
        bool takesArticulations() const override { return articulations; }
        void setSampleRate(double) override {}
        void noteOn(int32_t p, float v) override {
            events.push_back({1, p, static_cast<int32_t>(std::lround(v * 127.0f)), eventOffset, eventChannel});
        }
        void noteOff(int32_t p) override { events.push_back({0, p, 0, eventOffset, eventChannel}); }
        void allNotesOff() override {}
        void controller(int32_t c, float v) override {
            events.push_back({2, c, static_cast<int32_t>(std::lround(v * 127.0f)), eventOffset, eventChannel});
        }
        void programChange(int32_t p) override { events.push_back({3, p, 0, eventOffset, eventChannel}); }
        void noteExpression(int32_t p, int32_t, float v) override {
            events.push_back({4, p, static_cast<int32_t>(std::lround(v * 127.0f)), eventOffset, eventChannel});
        }
        void render(float*, int32_t) override {}
    };
    Probe p;
    if (p.takesArticulations()) return 19; // the default is no: a built-in never gets keyswitches
    const MidiEv evs[] = {{100, true, 60, 0.5f, 0.0f}};
    const ArtEv aev[] = {{100, 0, ArtEvKind::KeyOn, 0, 24, 1}, {100, 1, ArtEvKind::Control, 5, 58, 81},
        {100, 2, ArtEvKind::Program, 0, 12, 0}};
    float dst[B * 2] = {};
    playEvents(p, dst, B, evs, 1, nullptr, 0, aev, 3, 0.0, spb, true, [] {});
    using E5 = std::array<int32_t, 5>;
    const std::vector<E5> want = {{1, 24, 1, 100, 0}, {2, 58, 81, 100, 5}, {3, 12, 0, 100, 0}, {1, 60, 64, 100, 0}};
    if (p.events != want) return 20;
    if (p.eventChannel != 0) return 21;

    // 22: changing only a note's articulation makes the published timeline stale (publishRaw
    // rebuilds it), and the rebuilt one switches there.
    {
        MidiClip c = makeClip({{0.0, 1}, {1.0, 1}});
        MidiClip edited = c;
        edited.notes[1].articulation = 2;
        if (articulationNotesKey(c) == articulationNotesKey(edited)) return 22;
        if (!articulationTimelineCurrent(c, table) || articulationTimelineCurrent(edited, table)) return 22;
        auto rebuilt = buildArticulationTimeline(edited, table);
        if (rebuilt->changes.size() != 2 || rebuilt->changes[1].entryId != 2) return 22;
    }

    // ---- Session slots (T042): the slot's clip looped every 2 beats on its own clock ----
    {
        MidiClip slot = makeClip({{0.0, 1}, {1.0, 2}}); // Long at 0, Legato at 1, each pass
        constexpr double L = 2.0;
        auto playSlot = [&](bool loop, double from, double to, std::vector<Abs>& got) {
            ArtEv buf[kMaxBlockArt];
            for (double s = from; s < to; s += B) {
                ArticulationBlock b{B, s, spb, sr, 0.0, false, false, 0.0};
                const int n = scheduleSlotArticulations(slot, L, loop, *table, b, buf, kMaxBlockArt);
                for (int i = 0; i < n; ++i) // the last block runs past `to`: count only what falls before it
                    if (s + buf[i].off < to)
                        got.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
            }
        };
        std::vector<Abs> looped;
        playSlot(true, 0.0, 3 * L * spb, looped);
        // 23: each pass switches to Long at its start and to Legato (−150 ms, lead 30 ms) after —
        // three passes, three of each, at their places (Legato: pass k at k·48000 + 24000 − 8640).
        int longs = 0, legatos = 0;
        bool placed = true;
        for (auto& e : looped) {
            if (e.kind == ArtEvKind::KeyOn && e.d1 == 24) {
                ++longs;
                placed &= std::fmod(e.at, L * spb) == 0.0;
            }
            if (e.kind == ArtEvKind::Control && e.d2 == 76) {
                ++legatos;
                placed &= std::fmod(e.at, L * spb) == 24000.0 - 8640.0;
            }
        }
        if (longs != 3 || legatos != 3 || !placed) return 23;
        // 24: a one-shot slot plays its single pass.
        std::vector<Abs> once;
        playSlot(false, 0.0, 3 * L * spb, once);
        if (count(once, ArtEvKind::KeyOn, 24) != 1 || count(once, ArtEvKind::Control, 58) != 2) return 24;
        // 25: a launch sends the switch whose lead falls before it, and an early switch reaches back
        // across the loop end. Legato on beat 0 (−150 ms, lead 30 ms) and Long on beat 1 of each pass:
        // the launch block's chase sends Legato at 0; after that, each pass's Legato goes out at the
        // end of the pass before (k·48000 − 8640) and its Long at k·48000 + 24000.
        MidiClip early = makeClip({{0.0, 2}, {1.0, 1}});
        std::vector<Abs> back;
        ArtEv buf[kMaxBlockArt];
        for (double s = 0.0; s < 2 * L * spb; s += B) {
            ArticulationBlock b{B, s, spb, sr, 0.0, s == 0.0, false, 0.0};
            const int n = scheduleSlotArticulations(early, L, true, *table, b, buf, kMaxBlockArt);
            for (int i = 0; i < n; ++i)
                if (s + buf[i].off < 2 * L * spb)
                    back.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        }
        std::vector<double> legatoAt, longAt;
        for (auto& e : back) {
            if (e.kind == ArtEvKind::Control && e.d2 == 76) legatoAt.push_back(e.at);
            if (e.kind == ArtEvKind::KeyOn && e.d1 == 24) longAt.push_back(e.at);
        }
        if (legatoAt != std::vector<double>{0.0, L * spb - 8640.0, 2 * L * spb - 8640.0} ||
            longAt != std::vector<double>{24000.0, L * spb + 24000.0})
            return 25;
    }

    // 26: a chase in a block shorter than 20 ms lets go exactly 20 ms later, in a subsequent block.
    {
        std::vector<Abs> got;
        const double land2 = 0.5 * spb; // inside the first Long phrase
        play(land2, land2 + 3 * B, 0.0, true, got);
        if (count(got, ArtEvKind::KeyOn, 24) != 1 || count(got, ArtEvKind::KeyOff, 24) != 1 ||
            at(got, ArtEvKind::KeyOn, 24) != land2 || at(got, ArtEvKind::KeyOff, 24) != land2 + 960.0)
            return 26;
    }

    // 27: overlapping clips share the instrument's state. Clip A: Long at beats 0 and 2; clip B:
    // Legato at beat 1. Beat 2's Long must be sent again — B changed the articulation in between.
    {
        std::vector<MidiClip> overlap{makeClip({{0.0, 1}, {2.0, 1}}), makeClip({{1.0, 2}})};
        std::vector<Abs> got;
        ArtEv buf[kMaxBlockArt];
        for (double s = 0.0; s < 3 * spb; s += B) {
            ArticulationBlock b{B, s, spb, sr, 0.0, s == 0.0, false, 0.0};
            const int n = scheduleArticulations(overlap, *table, b, buf, kMaxBlockArt);
            for (int i = 0; i < n; ++i)
                got.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        }
        int longs = 0, legatos = 0;
        for (auto& e : got) {
            longs += e.kind == ArtEvKind::Control && e.d2 == 81;
            legatos += e.kind == ArtEvKind::Control && e.d2 == 76;
        }
        if (longs != 2 || legatos != 1 || at(got, ArtEvKind::Control, 58, 81) != 0.0) return 27;
    }

    // 28: two overlapping momentary notes on the same key: the shorter one ending doesn't let go of
    // the key the longer one still holds. Pizz (key 30, channel 2) 3.0–5.0 and 3.5–3.75 beats.
    {
        MidiClip c;
        c.startBeat = 0.0;
        c.lengthBeats = 8.0;
        for (auto [start, len] : {std::pair{3.0, 2.0}, std::pair{3.5, 0.25}}) {
            Note n;
            n.pitch = 60;
            n.startBeat = start;
            n.lengthBeats = len;
            n.articulation = 3;
            c.notes.push_back(n);
        }
        c.articulations = buildArticulationTimeline(c, table);
        std::vector<MidiClip> one{c};
        std::vector<Abs> got;
        ArtEv buf[kMaxBlockArt];
        for (double s = 0.0; s < 6 * spb; s += B) {
            ArticulationBlock b{B, s, spb, sr, 0.0, false, false, 0.0};
            const int n = scheduleArticulations(one, *table, b, buf, kMaxBlockArt);
            for (int i = 0; i < n; ++i)
                got.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        }
        int earlyOff = 0, finalOff = 0;
        for (auto& e : got)
            if (e.kind == ArtEvKind::KeyOff && e.d1 == 30) {
                earlyOff += e.at == 3.75 * spb;
                finalOff += e.at == 5.0 * spb;
            }
        if (earlyOff != 0 || finalOff != 1) return 28;
    }

    // 29: a short slot with a large offset: a one-beat loop (24000 samples = 500 ms) with a +1500 ms
    // momentary articulation — each pass's switch lands three passes later and must not be lost.
    {
        const ArticulationAction late[] = {{AK::Keyswitch, 0, 40, 100}};
        const ArticulationEntry lateE[] = {{1, ArticulationTrigger::Momentary, 1500.0, 0.0, 0, 1}};
        auto lateTable = ArticulationTable::build(0, lateE, 1, late, 1);
        if (!lateTable) return 29;
        MidiClip slot;
        slot.lengthBeats = 1.0;
        Note n;
        n.pitch = 60;
        n.startBeat = 0.0;
        n.lengthBeats = 0.25;
        n.articulation = 1;
        slot.notes.push_back(n);
        slot.articulations = buildArticulationTimeline(slot, lateTable);
        std::vector<double> ons;
        ArtEv buf[kMaxBlockArt];
        for (double s = 0.0; s < 6 * spb; s += B) {
            ArticulationBlock b{B, s, spb, sr, 0.0, false, false, 0.0};
            const int cnt = scheduleSlotArticulations(slot, 1.0, true, *lateTable, b, buf, kMaxBlockArt);
            for (int i = 0; i < cnt; ++i)
                if (buf[i].kind == ArtEvKind::KeyOn && s + buf[i].off < 6 * spb) ons.push_back(s + buf[i].off);
        }
        // Passes 0, 1, 2 land at 72000, 96000, 120000; pass 3 would land at 144000, past the window.
        if (ons != std::vector<double>{72000.0, 96000.0, 120000.0}) return 29;

        // 30: a 31.25 ms loop requires looking 48 passes back for this same offset. Its release
        // must also survive. Read just the block containing the first on, then the first off.
        slot.lengthBeats = 0.0625;
        slot.notes[0].lengthBeats = 0.03125;
        slot.articulations = buildArticulationTimeline(slot, lateTable);
        for (bool release : {false, true}) {
            const double due = 72000.0 + (release ? 750.0 : 0.0);
            ArticulationBlock b{B, due - 100, spb, sr, 0.0, false, false, 0.0};
            const int cnt = scheduleSlotArticulations(slot, 0.0625, true, *lateTable, b, buf, kMaxBlockArt);
            if (cnt != 1 || buf[0].off != 100 || buf[0].kind != (release ? ArtEvKind::KeyOff : ArtEvKind::KeyOn))
                return 30;
        }
    }

    // 31: different momentary articulations in separate clips can hold the same explicit key.
    {
        ArticulationAction keys[] = {{AK::Keyswitch, 2, 30, 100}, {AK::Keyswitch, 2, 30, 100}};
        const ArticulationEntry entries31[] = {
            {1, ArticulationTrigger::Momentary, 0, 0, 0, 1}, {2, ArticulationTrigger::Momentary, 0, 0, 1, 1}};
        for (bool defaultChannel : {false, true}) {
            keys[0].channel = defaultChannel ? 0 : 2;
            keys[1].channel = defaultChannel ? 1 : 2;
            auto shared = ArticulationTable::build(0, entries31, 2, keys, 2);
            auto a = makeClip({{0.0, 1}}, 2.0), b = makeClip({{0.5, 2}}, 0.5);
            a.articulations = buildArticulationTimeline(a, shared);
            b.articulations = buildArticulationTimeline(b, shared);
            ArtEv buf[kMaxBlockArt];
            ArticulationBlock block{B, spb - 100, spb, sr};
            if (scheduleArticulations({a, b}, *shared, block, buf, kMaxBlockArt) != 0) return 31;
            block.blockStart = 2 * spb - 100;
            const int cnt = scheduleArticulations({a, b}, *shared, block, buf, kMaxBlockArt);
            if (cnt != 1 || buf[0].off != 100 || buf[0].kind != ArtEvKind::KeyOff || buf[0].channel != keys[0].channel)
                return 31;
        }
    }

    // Exercise Session rendering itself, capturing the events delivered to a timed instrument.
    auto slotEngine = std::make_unique<EngineRuntime>();
    slotEngine->transport_.setSampleRate(sr);
    Track track(1, TrackType::Instrument);
    auto probe = std::make_shared<Probe>();
    track.instrument = probe;
    track.sessionSlots.write().resize(1);
    track.sessionPlayer->playing.store(0);
    auto& slot = track.sessionSlots.write()[0];
    slot.hasClip = true;
    slot.lengthBeats = 1.0;
    Note note;
    note.startBeat = 0.5;
    note.lengthBeats = 0.25;
    note.articulation = 1;
    auto expr = std::make_shared<NoteExpr>();
    expr->dim[ExprPressure] = {{0.0f, 0.25f}, {0.25f, 0.75f}};
    note.expr = expr;
    slot.midi.notes = {note};
    // 32: both positive and negative track + articulation offsets move opening expression with
    // its note. It starts at 0.25 pressure at the note-on, never partway through the curve.
    for (double offset : {-100.0, 100.0}) {
        const ArticulationEntry entry{1, ArticulationTrigger::Latching, offset, 0, 0, 0};
        track.articulationTable = ArticulationTable::build(0, &entry, 1, nullptr, 0);
        track.setMidiOffsetMs(offset);
        track.sessionPlayer->localBeats = offset < 0 ? 0.09375 : 0.890625;
        probe->events.clear();
        slotEngine->renderSessionSlotRaw(track, dst, B, spb);
        const int32_t atOff = offset < 0 ? 150 : 225;
        const E5 on{1, 60, 102, atOff, 0}, pressure{4, 60, 32, atOff, 0};
        if (std::find(probe->events.begin(), probe->events.end(), on) == probe->events.end() ||
            std::find(probe->events.begin(), probe->events.end(), pressure) == probe->events.end())
            return 32;
    }
    // 33: a note release more than one pass after its start must still reach the instrument.
    track.articulationTable.reset();
    track.setMidiOffsetMs(0);
    slot.midi.notes[0].expr.reset();
    slot.midi.notes[0].startBeat = 0;
    slot.midi.notes[0].lengthBeats = 3;
    track.sessionPlayer->localBeats = 3.0 - 0.015625;
    probe->events.clear();
    slotEngine->renderSessionSlotRaw(track, dst, B, spb);
    if (std::find(probe->events.begin(), probe->events.end(), E5{0, 60, 0, 375, 0}) == probe->events.end()) return 33;

    // 34: even one-frame render segments preserve the full chased-key hold.
    {
        ArtEv buf[kMaxBlockArt];
        const double land34 = 0.5 * spb;
        ArticulationBlock b{1, land34, spb, sr, 0.0, true};
        b.chaseStart = land34;
        if (scheduleArticulations(clips, *table, b, buf, kMaxBlockArt) != 3) return 34;
        b.chase = false;
        b.blockStart = land34 + 960;
        const int cnt = scheduleArticulations(clips, *table, b, buf, kMaxBlockArt);
        if (cnt != 2 || buf[0].kind != ArtEvKind::KeyOff || buf[0].off != 0 || buf[1].kind != ArtEvKind::KeyOff)
            return 34;
    }
    // 35: launch and legato launch restore a pre-launch switch through the real Session renderer,
    // and its release reaches the next block rather than being shortened to this block.
    {
        const ArticulationAction key{AK::Keyswitch, 0, 24, 100};
        const ArticulationEntry entry{1, ArticulationTrigger::Latching, 0, 30, 0, 1};
        track.articulationTable = ArticulationTable::build(0, &entry, 1, &key, 1);
        slot.midi.notes[0].lengthBeats = 1;
        slot.midi.articulations = buildArticulationTimeline(slot.midi, track.articulationTable);
        probe->articulations = true;
        for (bool legato : {false, true}) {
            track.sessionPlayer->localBeats = legato ? 0.5 : 0.0;
            track.sessionPlayer->requestLaunch(0, 0, legato);
            if (!track.sessionPlayer->maybeApply(0, B / spb, 0)) return 35;
            probe->events.clear();
            slotEngine->renderSessionSlotRaw(track, dst, B, spb);
            if (std::find(probe->events.begin(), probe->events.end(), E5{1, 24, 100, 0, 0}) == probe->events.end())
                return 35;
            track.sessionPlayer->publish(B / spb);
            probe->events.clear();
            slotEngine->renderSessionSlotRaw(track, dst, B, spb);
            if (std::find(probe->events.begin(), probe->events.end(), E5{0, 24, 0, 448, 0}) == probe->events.end())
                return 35;
        }
    }
    // 36: Session releases count rendered frames, even when tempo changes between the press and
    // release at a nonzero legato position. Check both faster and slower tempo, natural switches,
    // and clearing the map while the key is held. Every release still lands at frame 960.
    {
        const ArticulationAction keys[] = {{AK::Keyswitch, 0, 24, 100}, {AK::Keyswitch, 0, 25, 100}};
        const ArticulationEntry entries36[] = {
            {1, ArticulationTrigger::Latching, 0, 0, 0, 1}, {2, ArticulationTrigger::Latching, 0, 0, 1, 1}};
        const auto mapped = ArticulationTable::build(0, entries36, 2, keys, 2);
        slot.midi.notes = makeClip({{0.0, 1}, {0.5, 2}}, 0.25).notes;
        slot.midi.articulations = buildArticulationTimeline(slot.midi, mapped);
        for (bool chase : {false, true})
            for (double nextSpb : {spb / 2, spb * 2})
                for (bool clearMap : {false, true}) {
                    track.articulationTable = mapped;
                    track.sessionPlayer->localBeats = chase ? 8.125 : 8.0;
                    track.sessionPlayer->requestLaunch(0, 0, true);
                    if (!track.sessionPlayer->maybeApply(0, B / spb, 0)) return 36;
                    if (!chase) track.sessionPlayer->playedBeats.store(1.0); // normal beat-8 switch, not a chase
                    probe->events.clear();
                    slotEngine->renderSessionSlotRaw(track, dst, B, spb);
                    if (std::count(probe->events.begin(), probe->events.end(), E5{1, 24, 100, 0, 0}) != 1 ||
                        track.sessionPlayer->articulationReleases.count != 1)
                        return 36;
                    track.sessionPlayer->publish(B / spb);
                    if (clearMap) track.articulationTable.reset();
                    probe->events.clear();
                    slotEngine->renderSessionSlotRaw(track, dst, B, nextSpb);
                    if (std::count(probe->events.begin(), probe->events.end(), E5{0, 24, 0, 448, 0}) != 1 ||
                        track.sessionPlayer->articulationReleases.count != 0)
                        return 36;
                }
    }
    // 37: a bounded pending queue never presses a key whose release can't be retained. Due offs
    // have priority, and an output buffer too small for them carries the rest to the next block.
    {
        std::vector<ArticulationAction> keys;
        for (int i = 0; i <= kMaxBlockArt; ++i) keys.push_back({AK::Keyswitch, 1 + i / 128, i % 128, 100});
        const ArticulationEntry entry{1, ArticulationTrigger::Latching, 0, 0, 0, static_cast<int32_t>(keys.size())};
        auto many = ArticulationTable::build(0, &entry, 1, keys.data(), static_cast<int32_t>(keys.size()));
        MidiClip clip = makeClip({{0.0, 1}});
        clip.articulations = buildArticulationTimeline(clip, many);
        ArticulationReleaseState pending;
        ArticulationBlock b{1, 0, spb, sr};
        b.releases = &pending;
        ArtEv buf[kMaxBlockArt];
        int dropped = 0;
        if (scheduleSlotArticulations(clip, 8, true, *many, b, buf, kMaxBlockArt, &dropped) != kMaxBlockArt ||
            dropped != 1 || pending.count != kMaxBlockArt)
            return 37;
        if (scheduleArticulationReleases(pending, 960, buf, kMaxBlockArt) != kMaxBlockArt || pending.count != 0)
            return 37;
        for (int i = 0; i < kMaxBlockArt; ++i)
            if (buf[i].kind != ArtEvKind::KeyOff || buf[i].off != 959 ||
                buf[i].channel != keys[static_cast<size_t>(i)].channel ||
                buf[i].data1 != keys[static_cast<size_t>(i)].data1)
                return 37;
        pending.pending[0] = {0, 2, 30};
        pending.pending[1] = {0, 3, 31};
        pending.count = 2;
        if (scheduleArticulationReleases(pending, 1, buf, 1) != 1 || pending.count != 1 || buf[0].data1 != 30)
            return 37;
        if (scheduleArticulationReleases(pending, 1, buf, 1) != 1 || pending.count != 0 || buf[0].data1 != 31)
            return 37;
        track.sessionPlayer->articulationReleases.count = 1;
        track.sessionPlayer->requestLaunch(0, 0, true);
        if (!track.sessionPlayer->maybeApply(0, 1, 0) || track.sessionPlayer->articulationReleases.count != 0)
            return 37;
        track.sessionPlayer->articulationReleases.count = 1;
        track.sessionPlayer->requestStopNow();
        if (!track.sessionPlayer->maybeApply(0, 1, 0) || track.sessionPlayer->articulationReleases.count != 0)
            return 37;
    }
    // 38: mute/solo gating flushes an old latching hold. Its obsolete release must never resume
    // after unmuting and cut a new momentary hold of the same key.
    for (bool soloGate : {false, true}) {
        auto engine = std::make_unique<EngineRuntime>();
        const int id = engine->devices().addInstrumentTrack(), other = engine->tracks().addAudioTrack();
        auto target = engine->cloneTrack(*engine->findTrackAuthoring(id));
        auto capture = std::make_shared<Probe>();
        capture->articulations = true;
        target->instrument = capture;
        const ArticulationAction key{AK::Keyswitch, 0, 24, 100};
        const ArticulationEntry entries38[] = {
            {1, ArticulationTrigger::Latching, 0, 0, 0, 1}, {2, ArticulationTrigger::Momentary, 0, 0, 0, 1}};
        target->articulationTable = ArticulationTable::build(0, entries38, 2, &key, 1);
        auto& slots = target->sessionSlots.write();
        slots.resize(1);
        auto& clip = slots[0];
        clip.hasClip = true;
        clip.lengthBeats = 4;
        clip.midi.notes = makeClip({{0.0, 1}, {0.046875, 2}}, 1.0).notes; // momentary starts at frame 1125
        engine->republishWithTrack(id, target);
        engine->session().setLaunchQuant(0);
        engine->transportPlay();
        engine->session().launchSlot(id, 0);
        engine->renderOffline(dst, B, sr); // first 512 frames: latching key has 448 frames left
        if (target->sessionPlayer->articulationReleases.count != 1) return 38;
        if (soloGate) engine->tracks().setTrackSolo(other, true);
        else
            engine->tracks().setTrackMute(id, true);
        engine->renderOffline(dst, 256, sr); // the mute flushes voices; the slot still advances
        if (soloGate) engine->tracks().setTrackSolo(other, false);
        else
            engine->tracks().setTrackMute(id, false);
        capture->events.clear();
        engine->renderOffline(dst, B, sr); // momentary key at offset 357, obsolete off would be 448
        if (target->sessionPlayer->articulationReleases.count != 0 ||
            std::count(capture->events.begin(), capture->events.end(), E5{1, 24, 100, 357, 0}) != 1 ||
            std::any_of(
                capture->events.begin(), capture->events.end(), [](const E5& e) { return e[0] == 0 && e[1] == 24; }))
            return 38;
    }
    // 39: a rack hands a keyswitch to its plug-in chains even behind a chain MIDI effect, on its
    // channel and ahead of the note; a built-in chain gets none.
    {
        auto rack = std::make_shared<RackInstrument>();
        rack->setSampleRate(sr);
        auto plug = std::make_shared<Probe>();
        plug->articulations = true;
        auto builtin = std::make_shared<Probe>();
        const int32_t pc = rack->addChainInstrument(plug);
        rack->addChainInstrument(builtin);
        if (rack->addChainMidiDevice(pc, 4) < 0) return 39; // Velocity: a pass-through MIDI effect
        if (!rack->takesBlockMidi() || !rack->takesArticulations()) return 39;
        const MidiEv on[] = {{100, true, 60, 0.5f, 0.0f}};
        const ArtEv ks[] = {{100, 0, ArtEvKind::KeyOn, 2, 24, 100}};
        playEvents(*rack, dst, B, on, 1, nullptr, 0, ks, 1, 0.0, spb, true, [] {});
        const auto plugOn = [&](int32_t pitch) {
            return std::find_if(plug->events.begin(), plug->events.end(), [&](const E5& e) {
                return e[0] == 1 && e[1] == pitch;
            }) - plug->events.begin();
        };
        const auto ksAt = plugOn(24), noteAt = plugOn(60);
        if (ksAt >= static_cast<std::ptrdiff_t>(plug->events.size()) || plug->events[static_cast<size_t>(ksAt)][4] != 2)
            return 39;
        if (noteAt >= static_cast<std::ptrdiff_t>(plug->events.size()) || noteAt < ksAt) return 39;
        if (std::any_of(builtin->events.begin(), builtin->events.end(), [](const E5& e) { return e[1] == 24; }))
            return 39;
        if (plug->eventChannel != 0) return 39;
        // The keyswitch's release reaches the plug-in too, and the note it selected plays on.
        const ArtEv ksOff[] = {{10, 0, ArtEvKind::KeyOff, 2, 24, 0}};
        plug->events.clear();
        playEvents(*rack, dst, B, nullptr, 0, nullptr, 0, ksOff, 1, 0.0, spb, true, [] {});
        if (plug->events != std::vector<E5>{{0, 24, 0, 0, 2}}) return 39;
    }
    return 0;
}

// A replaced graph is freed once two renders have finished after it — not kept for the
// session, and not before a render that may still be walking it is done.
bool EngineRuntime::graphRetireSelfTest() {
    if (io_.isRunning() || !authoring_) return false;
    std::vector<float> buf(64 * 2);
    auto first = std::make_shared<Graph>(*authoring_); // a copy: no undo entry holds it
    const std::weak_ptr<Graph> held = first;
    publishRaw(std::move(first));
    publishRaw(std::make_shared<Graph>(*authoring_)); // replaced, but no render has passed yet
    // A recording slot's player outlives its track the same way (#438 review): the next take
    // retires it, and a render that read the old pointer finishes before it goes.
    auto player = std::make_shared<SessionPlayer>();
    const std::weak_ptr<SessionPlayer> heldPlayer = player;
    recordSlotHist_.publish(std::move(player)); // its track is gone: the history is the last owner
    recordSlotHist_.publish(std::make_shared<SessionPlayer>());
    if (held.expired() || heldPlayer.expired()) return false;
    renderOffline(buf.data(), 64);
    renderOffline(buf.data(), 64);
    publishRaw(std::make_shared<Graph>(*authoring_)); // frees what the two renders moved past
    recordSlotHist_.publish(std::make_shared<SessionPlayer>());
    return held.expired() && heldPlayer.expired();
}

} // namespace nota::detail
