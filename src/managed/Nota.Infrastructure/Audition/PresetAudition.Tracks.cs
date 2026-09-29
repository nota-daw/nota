// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The demo tracks an audio-effect preset is heard through: three single parts (drums, keys,
// bass) and short genre loops, each composed here from Nota's own factory sounds — a kit for
// the drums, factory presets for the rest. A track renders part by part into the rig's source
// on first use and is then kept, normalized to one peak level so an effect compares fairly
// across tracks, for the rest of the session (see IAuditionRig.CacheSource).

using System.Collections.Generic;
using Nota.Application;
using Nota.Infrastructure.Kits;

namespace Nota.Infrastructure;

public sealed partial class PresetAudition
{
    private const float SourcePeak = 0.6f;   // ≈ −4.4 dBFS: headroom for effects that add gain

    // A part: a factory preset id ("bass/Finger Bass") or a kit ("kit:volta"), and its notes.
    private sealed record DemoPart(string Sound, float Gain, AuditionNote[] Notes);
    private sealed record DemoTrack(AuditionTrack Info, double Bpm, double Beats, DemoPart[] Parts);

    private const string Parts = "Parts", Genres = "Genres";

    private static readonly DemoTrack[] DemoTracks =
    {
        new(new("drums", "Drums", Parts, "an analog kit, groove and fill"), 110, 8, new[]
        {
            new DemoPart("kit:volta", 1f, KitGroove(0)),
        }),
        new(new("keys", "Keys", Parts, "electric piano chords and a melody"), 110, 8, new[]
        {
            new DemoPart("operator/E-Piano", 1f, Cat(
                Chord(0, 1.4, 0.75f, 48, 60, 64, 67, 71), new[] { N(1.5, 0.45, 76, 0.7f), N(2, 0.45, 74, 0.65f), N(2.5, 0.9, 72, 0.7f) },
                Chord(4, 1.4, 0.75f, 45, 57, 60, 64, 67), new[] { N(5.5, 0.45, 72, 0.7f), N(6, 0.45, 71, 0.65f) },
                Chord(6.5, 1.2, 0.8f, 43, 55, 59, 62, 65))),
        }),
        new(new("bass", "Bass", Parts, "a finger-bass line"), 110, 8, new[]
        {
            new DemoPart("bass/Finger Bass", 1f, new[]
            {
                N(0, 0.9, 36, 0.9f), N(1, 0.4, 36, 0.7f), N(1.5, 0.45, 48, 0.8f), N(2, 0.9, 43, 0.85f), N(3, 0.45, 41, 0.7f), N(3.5, 0.45, 40, 0.75f),
                N(4, 0.9, 33, 0.9f), N(5, 0.4, 33, 0.7f), N(5.5, 0.45, 45, 0.8f), N(6, 0.9, 41, 0.85f), N(7, 0.45, 43, 0.75f), N(7.5, 0.45, 35, 0.7f),
            }),
        }),

        // Pop — C G Am F, acoustic kit, finger bass, poly keys.
        new(new("pop", "Pop", Genres, "acoustic kit, bass and keys · 110 BPM"), 110, 8, new[]
        {
            new DemoPart("kit:atelier", 0.9f, Cat(
                Hits(Kick, 0.95f, 0, 1.5, 2, 4, 5.5, 6), Hits(Snare, 0.85f, 1, 3, 5, 7), Hits(Crash, 0.6f, 0),
                Every(HatC, 0.5, 0, 8, 0.6f, 0.45f))),
            new DemoPart("bass/Finger Bass", 0.8f, Cat(Pulse(0.5, 0.4, 0.8f, (0, 36), (2, 31), (4, 33), (6, 29)))),
            new DemoPart("pentad/Poly Keys", 0.55f, Cat(
                Chord(0, 1.9, 0.6f, 60, 64, 67), Chord(2, 1.9, 0.6f, 59, 62, 67), Chord(4, 1.9, 0.6f, 57, 60, 64), Chord(6, 1.9, 0.6f, 57, 60, 65))),
        }),

        // House — four on the floor, offbeat hats and bass, organ stabs.
        new(new("house", "House", Genres, "four-on-the-floor, organ stabs · 124 BPM"), 124, 8, new[]
        {
            new DemoPart("kit:kompakt", 0.9f, Cat(
                Hits(Kick, 0.95f, 0, 1, 2, 3, 4, 5, 6, 7), Hits(Clap, 0.8f, 1, 3, 5, 7),
                Every(HatO, 1, 0.5, 8, 0.55f, 0.55f), Every(HatP, 0.5, 0.25, 8, 0.3f, 0.3f))),
            new DemoPart("bass/Stab Bass", 0.75f, Cat(Pulse(1, 0.35, 0.85f, (0.5, 33), (4.5, 38)))),
            new DemoPart("operator/Drawbar Organ", 0.5f, Cat(
                Chord(0.75, 0.3, 0.7f, 57, 60, 64, 67), Chord(1.5, 0.3, 0.6f, 57, 60, 64, 67), Chord(2.75, 0.3, 0.7f, 57, 60, 64, 67),
                Chord(4.75, 0.3, 0.7f, 57, 60, 62, 65), Chord(5.5, 0.3, 0.6f, 57, 60, 62, 65), Chord(6.75, 0.6, 0.7f, 57, 60, 62, 65))),
        }),

        // Techno — driven kick, sixteenth hats, an acid line with slides.
        new(new("techno", "Techno", Genres, "warehouse kick and an acid line · 130 BPM"), 130, 8, new[]
        {
            new DemoPart("kit:bunker", 0.9f, Cat(
                Hits(Kick, 1f, 0, 1, 2, 3, 4, 5, 6, 7), Hits(Clap, 0.75f, 1, 3, 5, 7),
                Every(HatC, 0.25, 0, 8, 0.55f, 0.3f), Every(HatO, 1, 0.5, 8, 0.45f, 0.45f))),
            new DemoPart("bass/Acid 303", 0.7f, Acid()),
        }),

        // Hip-hop — boom bap with the kit's swing, Wurly chords and a bass line.
        new(new("hiphop", "Hip-Hop", Genres, "boom bap, swung, Wurly chords · 90 BPM"), 90, 8, new[]
        {
            new DemoPart("kit:crate", 0.95f, Cat(
                Hits(Kick, 0.95f, 0, 0.75, 2.5, 4, 4.75, 6.5, 7.25), Hits(Snare, 0.9f, 1, 3, 5, 7),
                Every(HatC, 0.5, 0, 8, 0.55f, 0.4f))),
            new DemoPart("operator/Wurly", 0.55f, Cat(Chord(0, 3.8, 0.6f, 50, 53, 57, 60, 64), Chord(4, 3.8, 0.6f, 43, 53, 57, 59, 64))),
            new DemoPart("bass/Finger Bass", 0.8f, new[]
            {
                N(0, 1.4, 38, 0.9f), N(2.5, 0.7, 38, 0.8f), N(3.25, 0.6, 41, 0.75f), N(4, 1.9, 31, 0.9f), N(6.5, 0.6, 31, 0.8f), N(7.25, 0.6, 33, 0.75f),
            }),
        }),

        // Trap — half-time, rolling hats, a gliding 808 and FM bells.
        new(new("trap", "Trap", Genres, "half-time, hat rolls, 808 and bells · 140 BPM"), 140, 8, new[]
        {
            new DemoPart("kit:neon", 0.9f, Cat(
                Hits(Kick, 0.95f, 0, 2.5, 4, 6.75), Hits(Clap, 0.9f, 2, 6),
                Every(HatC, 0.5, 0, 3, 0.55f, 0.4f), Every(HatC, 0.25, 3, 4, 0.5f, 0.35f), Every(HatC, 0.5, 4, 7, 0.55f, 0.4f),
                Every(HatC, 1.0 / 6, 7, 7.5, 0.5f, 0.4f), Hits(HatO, 0.5f, 7.5))),
            new DemoPart("bass/808 Sub", 0.85f, new[] { N(0, 1.4, 29, 0.95f), N(2.5, 0.9, 29, 0.85f), N(3.5, 0.45, 32, 0.8f), N(4, 2.2, 25, 0.95f), N(6.5, 1.2, 27, 0.85f) }),
            new DemoPart("operator/FM Bell", 0.4f, Seq(0.8f, 0.5, 0.6f, 77, 80, 84, 80, 77, 80, 85, 84, 77, 80, 84, 80, 75, 77, 80, 72)),
        }),

        // Drum & bass — two-step breaks, a reese bass and a pad, four bars.
        new(new("dnb", "Drum & Bass", Genres, "two-step breaks, reese and pad · 172 BPM"), 172, 16, new[]
        {
            new DemoPart("kit:breakline", 0.9f, Cat(
                Bars(4, Hits(Kick, 0.95f, 0, 2.5), Hits(Snare, 0.9f, 1, 3), Hits(Snare, 0.3f, 3.75), Every(HatC, 0.5, 0, 4, 0.5f, 0.35f)),
                Hits(Crash, 0.6f, 0), Hits(TomHi, 0.7f, 15.25), Hits(TomLo, 0.75f, 15.5))),
            new DemoPart("bass/Reese Bass", 0.7f, new[] { N(0, 3.9, 33, 0.9f), N(4, 3.9, 29, 0.9f), N(8, 3.9, 36, 0.9f), N(12, 3.5, 31, 0.9f) }),
            new DemoPart("aurora/Glacier Pad", 0.4f, Cat(
                Chord(0, 3.9, 0.5f, 57, 60, 64), Chord(4, 3.9, 0.5f, 53, 57, 60), Chord(8, 3.9, 0.5f, 55, 60, 64), Chord(12, 3.9, 0.5f, 55, 59, 62))),
        }),

        // Synthwave — gated 80s kit, octave bass, strings and a sync lead.
        new(new("synthwave", "Synthwave", Genres, "gated 80s kit, octave bass, sync lead · 100 BPM"), 100, 8, new[]
        {
            new DemoPart("kit:linnwood", 0.85f, Cat(
                Hits(Kick, 0.95f, 0, 1, 2, 3, 4, 5, 6, 7), Hits(Snare, 0.9f, 1, 3, 5, 7), Every(HatC, 0.5, 0, 8, 0.5f, 0.4f))),
            new DemoPart("monolith/Fat Bass", 0.7f, Octaves(0.5, (0, 33), (2, 29), (4, 36), (6, 31))),
            new DemoPart("pentad/Analog Strings", 0.4f, Cat(
                Chord(0, 1.9, 0.55f, 57, 60, 64), Chord(2, 1.9, 0.55f, 53, 57, 60), Chord(4, 1.9, 0.55f, 55, 60, 64), Chord(6, 1.9, 0.55f, 55, 59, 62))),
            new DemoPart("pentad/Sync Lead", 0.45f, new[] { N(4, 0.7, 76, 0.75f), N(4.75, 0.25, 74, 0.7f), N(5, 0.95, 72, 0.75f), N(6, 0.45, 71, 0.7f), N(6.5, 1.4, 72, 0.8f) }),
        }),

        // Ambient — slow pad chords and sparse vibraphone, no drums.
        new(new("ambient", "Ambient", Genres, "pad and vibraphone, no drums · 80 BPM"), 80, 8, new[]
        {
            new DemoPart("aurora/Glacier Pad", 0.6f, Cat(Chord(0, 3.9, 0.55f, 48, 55, 64, 71), Chord(4, 3.9, 0.55f, 45, 52, 60, 67, 71))),
            new DemoPart("physical/Vibraphone", 0.55f, new[]
            {
                N(0.5, 0.9, 76, 0.6f), N(1.5, 1.2, 79, 0.55f), N(3, 1, 74, 0.6f), N(4.5, 0.9, 72, 0.6f), N(5.5, 1.2, 71, 0.55f), N(7, 1, 67, 0.6f),
            }),
        }),
    };

    public IReadOnlyList<AuditionTrack> Tracks { get; } = Array.ConvertAll(DemoTracks, t => t.Info);

    private static DemoTrack? TrackById(string id) => Array.Find(DemoTracks, t => t.Info.Id == id);

    public string AutoTrack(int effectKind) => effectKind switch
    {
        // Dynamics and rhythm effects want transients.
        1 or 11 or 19 or 21 => "drums",
        // Time, modulation and pitch effects want sustained, spaced notes (tails heard alone).
        2 or 3 or 9 or 10 or 15 or 20 or 23 or 24 or 25 => "keys",
        // Tone shaping (EQ, drive, filters, limiting, level) wants a full mix.
        _ => "pop",
    };

    /// <summary>Makes the track the rig's source: from the session cache, else rendered part
    /// by part (worker thread) and cached.</summary>
    private bool UseTrack(IAuditionRig rig, DemoTrack t)
    {
        string key = "track:" + t.Info.Id;
        if (rig.UseCachedSource(key)) return true;
        int mixed = 0;
        foreach (var part in t.Parts)
        {
            using var pr = rig.CreatePart();
            if (!SetupPart(pr, part.Sound)) continue;
            if (pr.Render(part.Notes, t.Bpm, t.Beats, 1.5, false) <= 0) continue;
            if (rig.AddSourceFrom(pr, part.Gain)) mixed++;
        }
        return mixed > 0 && rig.CacheSource(key, SourcePeak);
    }

    private bool SetupPart(IAuditionRig rig, string sound)
    {
        if (sound.StartsWith("kit:", StringComparison.Ordinal))
            return KitCatalog.ById(sound[4..]) is { } kit && SetupKit(rig, kit, rhythm: false);
        var doc = _factory.Document(sound);
        if (doc is null || !rig.SetInstrument(doc.BuiltinKind)) return false;
        foreach (var (id, v) in doc.NamedParams ?? new Dictionary<string, float>())
            if (!InstrumentView.IsViewParam(id)) rig.InstrumentParam(id, v);
        return true;
    }

    // --- note builders ------------------------------------------------------------------

    private static IEnumerable<AuditionNote> Hits(int note, float vel, params double[] beats)
    {
        foreach (var b in beats) yield return N(b, 0.2, note, vel);
    }

    // A hit every `step` beats over [from, to); on-beat hits at `onVel`, the rest at `offVel`.
    private static IEnumerable<AuditionNote> Every(int note, double step, double from, double to, float onVel, float offVel)
    {
        for (double b = from; b < to - 1e-9; b += step)
            yield return N(b, Math.Min(0.2, step * 0.8), note, Math.Abs(b - Math.Round(b)) < 1e-6 ? onVel : offVel);
    }

    // The same one-bar pattern repeated for `bars` bars.
    private static IEnumerable<AuditionNote> Bars(int bars, params IEnumerable<AuditionNote>[] bar)
    {
        var one = Cat(bar);
        for (int k = 0; k < bars; k++)
            foreach (var x in one) yield return x with { StartBeat = x.StartBeat + 4 * k };
    }

    // Repeated root notes every `step` beats from each (beat, pitch) until the next.
    private static IEnumerable<AuditionNote> Pulse(double step, double len, float vel, params (double At, int Pitch)[] roots)
    {
        for (int i = 0; i < roots.Length; i++)
        {
            double end = i + 1 < roots.Length ? roots[i + 1].At : 8;
            for (double b = roots[i].At; b < end - 1e-9; b += step) yield return N(b, len, roots[i].Pitch, vel);
        }
    }

    // Eighth-note octave bass (root, octave up, …) from each (beat, root) to the next.
    private static AuditionNote[] Octaves(double step, params (double At, int Pitch)[] roots)
    {
        var n = new List<AuditionNote>();
        for (int i = 0; i < roots.Length; i++)
        {
            double end = i + 1 < roots.Length ? roots[i + 1].At : 8;
            int k = 0;
            for (double b = roots[i].At; b < end - 1e-9; b += step, k++)
                n.Add(N(b, step * 0.8, roots[i].Pitch + (k % 2 == 1 ? 12 : 0), k % 2 == 0 ? 0.85f : 0.7f));
        }
        return n.ToArray();
    }

    // Sixteenths over A, with accents, octave jumps and overlapping (sliding) notes — twice.
    private static AuditionNote[] Acid()
    {
        int[] p = { 33, 33, 45, 33, 36, 33, 43, 33, 33, 45, 33, 40, 33, 31, 33, 45 };
        bool[] accent = { true, false, false, false, true, false, true, false, false, true, false, false, true, false, false, true };
        bool[] slide = { false, false, true, false, false, false, false, true, false, false, false, true, false, false, true, false };
        var n = new List<AuditionNote>();
        for (int rep = 0; rep < 2; rep++)
            for (int i = 0; i < 16; i++)
                n.Add(N(rep * 4 + i * 0.25, slide[i] ? 0.3 : 0.18, p[i], accent[i] ? 0.95f : 0.6f));
        return n.ToArray();
    }
}
