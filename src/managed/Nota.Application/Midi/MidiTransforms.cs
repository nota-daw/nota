// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The nine clip transformations. Each is a pure function over the notes it is
// handed — no clip, track or engine awareness — so they are equally usable on a
// selection, a whole clip, or from a test.

namespace Nota.Application.Midi;

using U = MidiToolUtil;

public static class MidiTransforms
{
    // ---------------------------------------------------------------- Arpeggiate

    private static readonly string[] ArpStyles =
        { "Up", "Down", "Up-Down", "Down-Up", "Converge", "Diverge", "Random", "Chord" };

    public static MidiTool Arpeggiate { get; } = new(
        "arpeggiate", "Arpeggiate", MidiToolKind.Transform,
        "Breaks chords into a running sequence.",
        new[]
        {
            MidiToolParam.Rate("rate", "Rate"),
            MidiToolParam.Choice("style", "Style", ArpStyles, 0),
            MidiToolParam.Int("octaves", "Octaves", 1, 4, 1),
            MidiToolParam.Float("gate", "Gate", 0.05, 2.0, 0.9, "×"),
            MidiToolParam.Int("distance", "Distance", -12, 12, 12, " st"),
            MidiToolParam.Float("decay", "Vel Decay", -1, 1, 0, "×"),
        },
        RunArpeggiate);

    private static List<NotaNote> RunArpeggiate(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        double rate = s.RateBeats("rate");
        int style = s.Int("style");
        int octaves = s.Int("octaves");
        double gate = s["gate"];
        int distance = s.Int("distance");
        double decay = s["decay"];
        var rng = U.Rng(ctx, "arp");

        var chords = U.Chords(input);
        var outp = new List<NotaNote>();

        for (int c = 0; c < chords.Count; c++)
        {
            var chord = chords[c];
            double start = chord[0].StartBeat;
            double end = chord.Max(n => n.StartBeat + n.LengthBeats);
            if (c + 1 < chords.Count) end = Math.Min(end, chords[c + 1][0].StartBeat);
            if (end - start < rate * 0.5) end = start + rate;   // a stab still gets one step

            // Extend the chord across the octave range before ordering it.
            var pitches = new List<int>();
            foreach (int o in Enumerable.Range(0, octaves))
                foreach (var n in chord) pitches.Add(n.Pitch + o * distance);
            var seq = OrderArp(pitches, style, rng);
            if (seq.Count == 0) continue;

            double vel = chord.Average(n => (double)n.Velocity);
            int step = 0;
            for (double t = start; t < end - 1e-6; t += rate, step++)
            {
                double fade = 1 - decay * (end - start <= rate ? 0 : (t - start) / (end - start));
                double len = rate * gate;
                if (style == 7)   // Chord — every pitch of this step's cycle together
                {
                    int cycle = U.Mod(step, Math.Max(1, octaves));
                    foreach (var n in chord) outp.Add(U.Note(n.Pitch + cycle * distance, t, len, vel * fade));
                }
                else outp.Add(U.Note(seq[U.Mod(step, seq.Count)], t, len, vel * fade));
            }
        }
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }

    private static List<int> OrderArp(List<int> pitches, int style, Random rng)
    {
        var up = pitches.Distinct().OrderBy(p => p).ToList();
        if (up.Count == 0) return up;
        switch (style)
        {
            case 1: up.Reverse(); return up;
            case 2:   // Up-Down, endpoints played once
            {
                var r = new List<int>(up);
                for (int i = up.Count - 2; i >= 1; i--) r.Add(up[i]);
                return r;
            }
            case 3:
            {
                var down = Enumerable.Reverse(up).ToList();
                var r = new List<int>(down);
                for (int i = 1; i < up.Count - 1; i++) r.Add(up[i]);
                return r;
            }
            case 4:   // Converge — outside in
            {
                var r = new List<int>();
                for (int lo = 0, hi = up.Count - 1; lo <= hi; lo++, hi--)
                {
                    r.Add(up[lo]);
                    if (lo != hi) r.Add(up[hi]);
                }
                return r;
            }
            case 5:   // Diverge — inside out
            {
                var conv = OrderArp(pitches, 4, rng);
                conv.Reverse();
                return conv;
            }
            case 6:
            {
                var r = new List<int>(up);
                U.PartialShuffle(r, 1.0, rng);
                return r;
            }
            default: return up;
        }
    }

    // ------------------------------------------------------------------- Connect

    private static readonly string[] ConnectModes = { "Chromatic", "In scale" };

    public static MidiTool Connect { get; } = new(
        "connect", "Connect", MidiToolKind.Transform,
        "Fills the gaps between notes with runs that walk to the next pitch.",
        new[]
        {
            MidiToolParam.Rate("rate", "Rate", 5),
            MidiToolParam.Choice("mode", "Steps", ConnectModes, 1),
            MidiToolParam.Percent("fill", "Fill", 0.5),
            MidiToolParam.Float("gate", "Gate", 0.1, 1.5, 0.9, "×"),
            MidiToolParam.Float("velocity", "Velocity", 0.1, 1.5, 0.7, "×"),
            MidiToolParam.Int("maxsteps", "Max Steps", 1, 16, 4),
        },
        RunConnect);

    private static List<NotaNote> RunConnect(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        double rate = s.RateBeats("rate");
        bool inScale = s.Int("mode") == 1;
        double fill = s["fill"], gate = s["gate"], velScale = s["velocity"];
        int maxSteps = s.Int("maxsteps");
        var degrees = ctx.Degrees;

        var outp = U.Sorted(input);
        // Connect the top voice — a run under a sustained chord reads as a fill, not a mess.
        var line = new List<int>();   // indices into outp
        var chords = U.Chords(outp);
        foreach (var g in chords)
            line.Add(outp.FindIndex(n => n.Pitch == g[^1].Pitch && Math.Abs(n.StartBeat - g[^1].StartBeat) < 1e-9));

        var runs = new List<NotaNote>();
        for (int i = 0; i + 1 < line.Count; i++)
        {
            if (line[i] < 0 || line[i + 1] < 0) continue;
            var a = outp[line[i]];
            var b = outp[line[i + 1]];
            if (a.Pitch == b.Pitch) continue;

            // The run may eat into the source note as well as the silence after it, but
            // never all of it — one rate of the original always survives.
            double available = b.StartBeat - (a.StartBeat + rate);
            if (available < rate * 0.999) continue;
            int steps = Math.Min(maxSteps, (int)Math.Floor(available * Math.Clamp(fill, 0, 1) / rate + 1e-6));
            if (steps <= 0) continue;

            double runStart = b.StartBeat - steps * rate;
            if (a.StartBeat + a.LengthBeats > runStart)
            {
                a.LengthBeats = Math.Max(U.MinLength, runStart - a.StartBeat);
                outp[line[i]] = a;
            }

            for (int k = 0; k < steps; k++)
            {
                double f = (k + 1.0) / (steps + 1.0);
                int pitch;
                if (inScale)
                {
                    int da = MidiScales.DegreeOfPitch(degrees, ctx.ScaleRoot, a.Pitch);
                    int db = MidiScales.DegreeOfPitch(degrees, ctx.ScaleRoot, b.Pitch);
                    pitch = MidiScales.PitchAtDegree(degrees, ctx.ScaleRoot, (int)Math.Round(U.Lerp(da, db, f)), 0);
                }
                else pitch = (int)Math.Round(U.Lerp(a.Pitch, b.Pitch, f));
                // A run that arrives on the target early gives the landing away; step past it.
                if (pitch == b.Pitch) pitch += Math.Sign(a.Pitch - b.Pitch);
                runs.Add(U.Note(pitch, runStart + k * rate, rate * gate, a.Velocity * velScale));
            }
        }
        outp.AddRange(runs);
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }

    // ------------------------------------------------------------------ Ornament

    private static readonly string[] OrnamentTypes =
        { "Grace", "Trill", "Mordent", "Turn", "Flam", "Roll" };

    public static MidiTool Ornament { get; } = new(
        "ornament", "Ornament", MidiToolKind.Transform,
        "Decorates notes with grace notes, trills and rolls.",
        new[]
        {
            MidiToolParam.Choice("type", "Type", OrnamentTypes, 0),
            MidiToolParam.Rate("rate", "Rate", 5),
            MidiToolParam.Percent("chance", "Chance", 1.0),
            MidiToolParam.Int("interval", "Interval", -4, 4, 1, " deg"),
            MidiToolParam.Float("velocity", "Velocity", 0.1, 1.5, 0.75, "×"),
            MidiToolParam.Toggle("scale", "In Scale", true),
        },
        RunOrnament);

    private static List<NotaNote> RunOrnament(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int type = s.Int("type");
        double rate = s.RateBeats("rate");
        double chance = s["chance"], velScale = s["velocity"];
        int interval = s.Int("interval");
        bool inScale = s.Flag("scale");
        var rng = U.Rng(ctx, "ornament");
        var degrees = ctx.Degrees;

        int Aux(int pitch, int steps)
        {
            if (!inScale) return pitch + steps;
            int d = MidiScales.DegreeOfPitch(degrees, ctx.ScaleRoot, pitch);
            return MidiScales.PitchAtDegree(degrees, ctx.ScaleRoot, d + steps, 0);
        }

        var outp = new List<NotaNote>();
        foreach (var n in U.Sorted(input))
        {
            if (rng.NextDouble() > chance) { outp.Add(n); continue; }
            int aux = Aux(n.Pitch, interval == 0 ? 1 : interval);
            double len = n.LengthBeats;
            double step = Math.Min(rate, len / 2);
            double vel = n.Velocity * velScale;

            switch (type)
            {
                case 0:   // Grace — a flick into the note, stealing a sliver off the front
                    outp.Add(U.Note(aux, n.StartBeat, step, vel));
                    outp.Add(U.Note(n.Pitch, n.StartBeat + step, len - step, n.Velocity));
                    break;

                case 1:   // Trill — alternate for the note's whole length
                {
                    int k = 0;
                    for (double t = n.StartBeat; t < n.StartBeat + len - 1e-6; t += step, k++)
                        outp.Add(U.Note(k % 2 == 0 ? n.Pitch : aux,
                                        t, Math.Min(step, n.StartBeat + len - t),
                                        k % 2 == 0 ? n.Velocity : vel));
                    break;
                }

                case 2:   // Mordent — main, aux, main
                    outp.Add(U.Note(n.Pitch, n.StartBeat, step, n.Velocity));
                    outp.Add(U.Note(aux, n.StartBeat + step, step, vel));
                    outp.Add(U.Note(n.Pitch, n.StartBeat + step * 2, Math.Max(U.MinLength, len - step * 2), n.Velocity));
                    break;

                case 3:   // Turn — upper, main, lower, main
                {
                    int upper = Aux(n.Pitch, Math.Abs(interval) == 0 ? 1 : Math.Abs(interval));
                    int lower = Aux(n.Pitch, -(Math.Abs(interval) == 0 ? 1 : Math.Abs(interval)));
                    double q = Math.Min(step, len / 4);
                    outp.Add(U.Note(upper, n.StartBeat, q, vel));
                    outp.Add(U.Note(n.Pitch, n.StartBeat + q, q, n.Velocity));
                    outp.Add(U.Note(lower, n.StartBeat + q * 2, q, vel));
                    outp.Add(U.Note(n.Pitch, n.StartBeat + q * 3, Math.Max(U.MinLength, len - q * 3), n.Velocity));
                    break;
                }

                case 4:   // Flam — a ghost a hair ahead, the main note untouched
                    outp.Add(U.Note(n.Pitch, Math.Max(0, n.StartBeat - step * 0.5), step * 0.5, vel * 0.6));
                    outp.Add(n);
                    break;

                default:  // Roll — ratchet the same pitch across the note
                    for (double t = n.StartBeat; t < n.StartBeat + len - 1e-6; t += step)
                        outp.Add(U.Note(n.Pitch, t, Math.Min(step, n.StartBeat + len - t) * 0.9,
                                        U.Lerp(n.Velocity, vel, (t - n.StartBeat) / Math.Max(1e-6, len))));
                    break;
            }
        }
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }

    // ------------------------------------------------------------------ Quantize

    public static MidiTool Quantize { get; } = new(
        "quantize", "Quantize", MidiToolKind.Transform,
        "Pulls notes onto the grid, with swing and a strength short of rigid.",
        new[]
        {
            MidiToolParam.Rate("rate", "Grid"),
            MidiToolParam.Percent("amount", "Amount", 1.0),
            MidiToolParam.Float("swing", "Swing", -0.75, 0.75, 0, "×"),
            MidiToolParam.Toggle("ends", "Quantize Ends", false),
            MidiToolParam.Float("humanize", "Humanize", 0, 0.25, 0, " b"),
        },
        RunQuantize);

    private static List<NotaNote> RunQuantize(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        double grid = s.RateBeats("rate");
        double amount = s["amount"], swing = s["swing"], humanize = s["humanize"];
        bool ends = s.Flag("ends");
        var rng = U.Rng(ctx, "quantize");

        // Swing pushes every second grid slot late by a fraction of the slot.
        double Target(double beat)
        {
            double idx = Math.Round(beat / grid);
            double t = idx * grid;
            if (Math.Abs(swing) > 1e-6 && U.Mod((int)idx, 2) == 1) t += grid * 0.5 * swing;
            return t;
        }

        var outp = new List<NotaNote>();
        foreach (var n in input)
        {
            var q = n;
            double target = Target(n.StartBeat);
            q.StartBeat = n.StartBeat + (target - n.StartBeat) * amount;
            if (humanize > 0) q.StartBeat += (rng.NextDouble() * 2 - 1) * humanize;
            if (ends)
            {
                double endTarget = Target(n.StartBeat + n.LengthBeats);
                double end = (n.StartBeat + n.LengthBeats) + (endTarget - (n.StartBeat + n.LengthBeats)) * amount;
                q.LengthBeats = Math.Max(U.MinLength, end - q.StartBeat);
            }
            outp.Add(q);
        }
        return U.Tidy(outp, ctx.LengthBeats);
    }

    // ----------------------------------------------------------------- Recombine

    private static readonly string[] RecombineModes =
        { "Shuffle Pitch", "Shuffle Rhythm", "Shuffle Both", "Rotate Pitch", "Reverse Pitch", "Mirror Pitch", "Reverse Time" };

    public static MidiTool Recombine { get; } = new(
        "recombine", "Recombine", MidiToolKind.Transform,
        "Pulls pitch and rhythm apart and puts them back together differently.",
        new[]
        {
            MidiToolParam.Choice("mode", "Mode", RecombineModes, 0),
            MidiToolParam.Percent("amount", "Amount", 0.6),
            MidiToolParam.Int("rotate", "Rotate", -16, 16, 1),
            MidiToolParam.Int("chunk", "Chunk", 1, 8, 1),
        },
        RunRecombine);

    private static List<NotaNote> RunRecombine(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int mode = s.Int("mode");
        double amount = s["amount"];
        int rotate = s.Int("rotate"), chunk = Math.Max(1, s.Int("chunk"));
        var rng = U.Rng(ctx, "recombine");

        var notes = U.Sorted(input);
        if (notes.Count < 2) return notes;

        // Chunking shuffles phrases instead of single notes, which keeps motifs intact.
        var pitchChunks = Chunked(notes.Select(n => n.Pitch).ToList(), chunk);
        var pitches = notes.Select(n => n.Pitch).ToList();

        switch (mode)
        {
            case 0:
                U.PartialShuffle(pitchChunks, amount, rng);
                pitches = pitchChunks.SelectMany(x => x).ToList();
                break;
            case 1:
            {
                var slots = Chunked(notes.Select(n => (n.StartBeat, n.LengthBeats)).ToList(), chunk);
                U.PartialShuffle(slots, amount, rng);
                var flat = slots.SelectMany(x => x).ToList();
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = notes[i];
                    n.StartBeat = flat[i].StartBeat;
                    n.LengthBeats = flat[i].LengthBeats;
                    notes[i] = n;
                }
                return U.Deoverlap(U.Tidy(notes, ctx.LengthBeats));
            }
            case 2:
            {
                U.PartialShuffle(pitchChunks, amount, rng);
                pitches = pitchChunks.SelectMany(x => x).ToList();
                var slots = Chunked(notes.Select(n => (n.StartBeat, n.LengthBeats)).ToList(), chunk);
                U.PartialShuffle(slots, amount, rng);
                var flat = slots.SelectMany(x => x).ToList();
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = notes[i];
                    n.Pitch = pitches[i];
                    n.StartBeat = flat[i].StartBeat;
                    n.LengthBeats = flat[i].LengthBeats;
                    notes[i] = n;
                }
                return U.Deoverlap(U.Tidy(notes, ctx.LengthBeats));
            }
            case 3:
                pitches = Enumerable.Range(0, pitches.Count).Select(i => pitches[U.Mod(i + rotate, pitches.Count)]).ToList();
                break;
            case 4:
                pitches.Reverse();
                break;
            case 5:   // Mirror — invert around the average pitch, snapped back into key
            {
                double axis = pitches.Average();
                pitches = pitches.Select(p => ctx.Snap((int)Math.Round(2 * axis - p))).ToList();
                break;
            }
            default:  // Reverse Time — the phrase played backwards in place
            {
                double lo = notes.Min(n => n.StartBeat);
                double hi = notes.Max(n => n.StartBeat + n.LengthBeats);
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = notes[i];
                    n.StartBeat = lo + hi - (n.StartBeat + n.LengthBeats);
                    notes[i] = n;
                }
                return U.Deoverlap(U.Tidy(notes, ctx.LengthBeats));
            }
        }

        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            n.Pitch = pitches[i];
            notes[i] = n;
        }
        return U.Deoverlap(U.Tidy(notes, ctx.LengthBeats));
    }

    private static List<List<T>> Chunked<T>(List<T> items, int size)
    {
        var outp = new List<List<T>>();
        for (int i = 0; i < items.Count; i += size)
            outp.Add(items.GetRange(i, Math.Min(size, items.Count - i)));
        return outp;
    }

    // ---------------------------------------------------------------------- Span

    private static readonly string[] SpanModes = { "To Next Note", "Scale Length", "Fixed" };

    public static MidiTool Span { get; } = new(
        "span", "Span", MidiToolKind.Transform,
        "Rewrites note lengths — from staccato through legato to overlapping.",
        new[]
        {
            MidiToolParam.Choice("mode", "Mode", SpanModes, 0),
            MidiToolParam.Float("amount", "Amount", 0.05, 2.0, 1.0, "×"),
            MidiToolParam.Float("fixed", "Length", 0.03125, 8.0, 0.25, " b"),
            MidiToolParam.Float("min", "Min", 0.015625, 2.0, 0.0625, " b"),
            MidiToolParam.Toggle("perpitch", "Per Pitch", false),
        },
        RunSpan);

    private static List<NotaNote> RunSpan(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int mode = s.Int("mode");
        double amount = s["amount"], fixedLen = s["fixed"], min = s["min"];
        bool perPitch = s.Flag("perpitch");

        var notes = U.Sorted(input);
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            double len = mode switch
            {
                1 => n.LengthBeats * amount,
                2 => fixedLen * amount,
                _ => NextStart(notes, i, perPitch, ctx.LengthBeats) is var next && next > n.StartBeat
                        ? (next - n.StartBeat) * amount
                        : n.LengthBeats * amount,
            };
            n.LengthBeats = Math.Max(min, len);
            notes[i] = n;
        }
        return U.Tidy(notes, ctx.LengthBeats);
    }

    private static double NextStart(List<NotaNote> notes, int i, bool perPitch, double clipLen)
    {
        for (int j = i + 1; j < notes.Count; j++)
        {
            if (notes[j].StartBeat <= notes[i].StartBeat + 1e-9) continue;
            if (perPitch && notes[j].Pitch != notes[i].Pitch) continue;
            return notes[j].StartBeat;
        }
        return clipLen;
    }

    // --------------------------------------------------------------------- Strum

    private static readonly string[] StrumDirections = { "Up", "Down", "Alternate", "Centre Out", "Random" };

    public static MidiTool Strum { get; } = new(
        "strum", "Strum", MidiToolKind.Transform,
        "Rolls the notes of a chord out in time instead of hitting them together.",
        new[]
        {
            MidiToolParam.Float("amount", "Amount", 0, 1.0, 0.125, " b"),
            MidiToolParam.Choice("direction", "Direction", StrumDirections, 0),
            MidiToolParam.Float("tension", "Tension", -1, 1, 0, ""),
            MidiToolParam.Float("velslope", "Vel Slope", -1, 1, 0, ""),
            MidiToolParam.Toggle("keepend", "Keep Ends", true),
        },
        RunStrum);

    private static List<NotaNote> RunStrum(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        double amount = s["amount"], tension = s["tension"], velSlope = s["velslope"];
        int direction = s.Int("direction");
        bool keepEnd = s.Flag("keepend");
        var rng = U.Rng(ctx, "strum");

        var outp = new List<NotaNote>();
        var chords = U.Chords(input);
        for (int c = 0; c < chords.Count; c++)
        {
            var chord = chords[c];
            int n = chord.Count;
            if (n < 2) { outp.AddRange(chord); continue; }

            var order = Enumerable.Range(0, n).ToList();
            bool down = direction == 1 || (direction == 2 && c % 2 == 1);
            if (down) order.Reverse();
            if (direction == 3) order = order.OrderBy(i => Math.Abs(i - (n - 1) / 2.0)).ToList();
            if (direction == 4) U.PartialShuffle(order, 1.0, rng);

            for (int k = 0; k < n; k++)
            {
                var note = chord[order[k]];
                double f = U.Bend(k / (double)(n - 1), tension);
                double offset = amount * f;
                double end = note.StartBeat + note.LengthBeats;
                note.StartBeat += offset;
                note.LengthBeats = keepEnd
                    ? Math.Max(U.MinLength, end - note.StartBeat)
                    : note.LengthBeats;
                note.Velocity = (float)Math.Clamp(note.Velocity * (1 + velSlope * (f - 0.5) * 2), 0.01, 1.0);
                outp.Add(note);
            }
        }
        return U.Tidy(outp, ctx.LengthBeats);
    }

    // ----------------------------------------------------------------- Time Warp

    private static readonly string[] WarpShapes = { "Curve", "S-Curve", "Sine", "Swing", "Snap to Grid" };

    public static MidiTool TimeWarp { get; } = new(
        "timewarp", "Time Warp", MidiToolKind.Transform,
        "Bends the time axis so a phrase accelerates, drags or breathes.",
        new[]
        {
            MidiToolParam.Choice("shape", "Shape", WarpShapes, 0),
            MidiToolParam.Float("curve", "Curve", -1, 1, 0.4, ""),
            MidiToolParam.Percent("amount", "Amount", 1.0),
            MidiToolParam.Int("cycles", "Cycles", 1, 8, 1),
            MidiToolParam.Toggle("lengths", "Warp Lengths", true),
        },
        RunTimeWarp);

    private static List<NotaNote> RunTimeWarp(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int shape = s.Int("shape");
        double curve = s["curve"], amount = s["amount"];
        int cycles = s.Int("cycles");
        bool warpLengths = s.Flag("lengths");

        var notes = U.Sorted(input);
        if (notes.Count == 0) return notes;
        double lo = notes.Min(n => n.StartBeat);
        double hi = Math.Max(lo + 1e-6, notes.Max(n => n.StartBeat + n.LengthBeats));
        double span = hi - lo;

        double Warp(double t)
        {
            t = Math.Clamp(t, 0, 1);
            double w = shape switch
            {
                0 => U.Bend(t, curve),
                1 => U.SCurve(t, curve),
                2 => t + Math.Sin(t * Math.PI * 2 * cycles) / (Math.PI * 2 * cycles) * curve,
                3 => SwingWarp(t, curve, cycles),
                _ => Math.Round(t * span / Math.Max(1e-6, ctx.Grid)) * ctx.Grid / span,
            };
            return U.Lerp(t, Math.Clamp(w, 0, 1), amount);
        }

        // Stretch each note by how far its own span moved apart, so a note sitting in a
        // squeezed region shortens and one in a stretched region opens up. Measuring the
        // span rather than the slope at the start matters: the slope is zero at the head
        // of a power curve, which would crush every note on the downbeat to nothing.
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            double t0 = (n.StartBeat - lo) / span;
            double t1 = (n.StartBeat + n.LengthBeats - lo) / span;
            double w0 = Warp(t0);
            if (warpLengths)
            {
                double stretched = (Warp(Math.Min(1, t1)) - w0) * span;
                n.LengthBeats = Math.Max(U.MinLength, stretched > 1e-6 ? stretched : n.LengthBeats * 0.25);
            }
            n.StartBeat = lo + w0 * span;
            notes[i] = n;
        }
        return U.Deoverlap(U.Tidy(notes, ctx.LengthBeats));
    }

    private static double SwingWarp(double t, double curve, int cycles)
    {
        // Delay the back half of every cycle — the same shape a swing grid has.
        double period = 1.0 / Math.Max(1, cycles * 2);
        int slot = (int)Math.Floor(t / period);
        double frac = t / period - slot;
        double shift = slot % 2 == 1 ? 0 : curve * 0.5;
        return Math.Clamp((slot + frac + shift * (1 - frac)) * period, 0, 1);
    }

    // ----------------------------------------------------------- Velocity Shaper

    private static readonly string[] VelShapes =
        { "Ramp Up", "Ramp Down", "Sine", "Triangle", "Random", "Accent", "Flatten" };

    public static MidiTool VelocityShaper { get; } = new(
        "velshaper", "Velocity Shaper", MidiToolKind.Transform,
        "Draws a curve through the velocities instead of editing them one by one.",
        new[]
        {
            MidiToolParam.Choice("shape", "Shape", VelShapes, 2),
            MidiToolParam.Rate("period", "Period", 0),
            MidiToolParam.Percent("depth", "Depth", 0.35),
            MidiToolParam.Percent("centre", "Centre", 0.7),
            MidiToolParam.Float("phase", "Phase", 0, 1, 0, ""),
            MidiToolParam.Percent("random", "Random", 0),
            MidiToolParam.Percent("blend", "Blend", 1.0),
        },
        RunVelocityShaper);

    private static List<NotaNote> RunVelocityShaper(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int shape = s.Int("shape");
        double period = s.RateBeats("period");
        double depth = s["depth"], centre = s["centre"], phase = s["phase"], random = s["random"], blend = s["blend"];
        var rng = U.Rng(ctx, "velshaper");

        var notes = U.Sorted(input);

        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            double cycle = n.StartBeat / period;
            double t = (cycle - Math.Floor(cycle) + phase) % 1.0;
            double unit = shape switch
            {
                0 => t,
                1 => 1 - t,
                2 => 0.5 + 0.5 * Math.Sin(t * Math.PI * 2),
                3 => 1 - Math.Abs(t * 2 - 1),
                4 => rng.NextDouble(),
                5 => t < 1e-6 ? 1.0 : 0.35,   // the downbeat of each period lands hard
                _ => 0.5,
            };
            double shaped = Math.Clamp(centre + (unit - 0.5) * depth * 2, 0.01, 1.0);
            if (random > 0) shaped = Math.Clamp(shaped + (rng.NextDouble() * 2 - 1) * random * 0.5, 0.01, 1.0);
            n.Velocity = (float)Math.Clamp(U.Lerp(n.Velocity, shaped, blend), 0.01, 1.0);
            notes[i] = n;
        }
        return U.Tidy(notes, ctx.LengthBeats);
    }
}
