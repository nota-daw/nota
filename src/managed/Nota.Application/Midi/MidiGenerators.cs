// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The six clip generators. Unlike the transformations these write notes rather
// than rewrite them; Stacks and Seed still read the input as source material, so
// every generator takes the same signature. The clip's length is the canvas —
// a generator fills it and stops.

namespace Nota.Application.Midi;

using U = MidiToolUtil;

public static class MidiGenerators
{
    // -------------------------------------------------------------------- Rhythm

    public static MidiTool Rhythm { get; } = new(
        "rhythm", "Rhythm", MidiToolKind.Generate,
        "Lays down a pattern that leans on the strong beats, as densely as you ask.",
        new[]
        {
            MidiToolParam.Rate("rate", "Rate"),
            MidiToolParam.Int("steps", "Pattern", 1, 32, 16, " st"),
            MidiToolParam.Percent("density", "Density", 0.5),
            MidiToolParam.Percent("variation", "Variation", 0.15),
            MidiToolParam.Note("pitch", "Pitch", 36),
            MidiToolParam.Float("gate", "Gate", 0.05, 2.0, 0.5, "×"),
            MidiToolParam.Percent("accent", "Accent", 0.45),
            MidiToolParam.Percent("velocity", "Velocity", 0.85),
        },
        RunRhythm);

    private static List<NotaNote> RunRhythm(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        double rate = s.RateBeats("rate");
        int steps = s.Int("steps");
        double density = s["density"], variation = s["variation"], gate = s["gate"];
        double accent = s["accent"], velocity = s["velocity"];
        int pitch = s.Int("pitch");
        var rng = U.Rng(ctx, "rhythm");

        // One pass to settle the base bar, then tile it — variation re-rolls individual
        // steps on each repeat so the loop breathes without losing its shape.
        var basePattern = new bool[steps];
        for (int i = 0; i < steps; i++) basePattern[i] = Hit(i, steps, density, rng);
        // Anything but a near-empty pattern lands on the downbeat — a rhythm that skips
        // beat one reads as a mistake however the dice fell.
        if (density >= 0.2) basePattern[0] = true;

        var outp = new List<NotaNote>();
        int total = (int)Math.Ceiling(ctx.LengthBeats / rate);
        for (int i = 0; i < total; i++)
        {
            int slot = U.Mod(i, steps);
            bool hit = basePattern[slot];
            if (i >= steps && variation > 0 && rng.NextDouble() < variation) hit = Hit(slot, steps, density, rng);
            if (!hit) continue;
            double m = Metric(slot, steps);
            double vel = velocity * (1 - accent * (1 - m));
            outp.Add(U.Note(pitch, i * rate, rate * gate, vel));
        }
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }

    private static bool Hit(int i, int steps, double density, Random rng)
    {
        if (density <= 0) return false;
        double m = Metric(i, steps);
        // Low density only survives on strong beats; high density flattens the bias.
        return rng.NextDouble() < Math.Min(1.0, density * 1.35 * Math.Pow(m, 2.0 - 1.6 * density));
    }

    /// <summary>Metric strength of a step: 1 on the downbeat, falling off as the step lands
    /// on ever finer subdivisions. This is what keeps a sparse pattern musical.</summary>
    private static double Metric(int i, int steps)
    {
        if (i == 0) return 1.0;
        for (int d = 2; d <= 32; d *= 2)
        {
            double slot = steps / (double)d;
            if (slot < 1) break;
            if (Math.Abs(i % slot) < 1e-9) return Math.Max(0.15, 1.0 - Math.Log2(d) * 0.17);
        }
        return 0.12;
    }

    // ---------------------------------------------------------------------- Seed

    public static MidiTool Seed { get; } = new(
        "seed", "Seed", MidiToolKind.Generate,
        "Learns the clip's intervals and rhythm, then writes a new take in the same voice.",
        new[]
        {
            MidiToolParam.Percent("pitchvar", "Pitch Var", 0.5),
            MidiToolParam.Percent("rhythmvar", "Rhythm Var", 0.4),
            MidiToolParam.Float("density", "Density", 0.25, 2.5, 1.0, "×"),
            MidiToolParam.Int("range", "Range", 0, 24, 12, " st"),
            MidiToolParam.Toggle("scale", "In Scale", true),
            MidiToolParam.Percent("velocity", "Vel Var", 0.2),
        },
        RunSeed);

    private static List<NotaNote> RunSeed(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        var src = U.Sorted(input);
        if (src.Count < 2) return new List<NotaNote>(src);

        double pitchVar = s["pitchvar"], rhythmVar = s["rhythmvar"], density = s["density"];
        double velVar = s["velocity"];
        int range = s.Int("range");
        bool inScale = s.Flag("scale");
        var rng = U.Rng(ctx, "seed");

        // The clip's own vocabulary: which intervals it steps by, how long notes are, and
        // how far apart onsets fall. Drawing from these is what keeps the take in character.
        var intervals = new List<int>();
        var gaps = new List<double>();
        var lengths = new List<double>();
        for (int i = 1; i < src.Count; i++)
        {
            intervals.Add(src[i].Pitch - src[i - 1].Pitch);
            double g = src[i].StartBeat - src[i - 1].StartBeat;
            if (g > 1e-6) gaps.Add(g);
        }
        foreach (var n in src) lengths.Add(n.LengthBeats);
        if (gaps.Count == 0) gaps.Add(ctx.Grid);

        int low = src.Min(n => n.Pitch), high = src.Max(n => n.Pitch);
        int centre = (low + high) / 2;
        int lo = range > 0 ? centre - range : low;
        int hi = range > 0 ? centre + range : high;

        var outp = new List<NotaNote>();
        int pitch = src[0].Pitch;
        double t = src[0].StartBeat;
        int step = 0;

        while (t < ctx.LengthBeats - 1e-6)
        {
            double vel = src[step % src.Count].Velocity;
            if (velVar > 0) vel = Math.Clamp(vel + (rng.NextDouble() * 2 - 1) * velVar, 0.05, 1.0);

            double len = rng.NextDouble() < rhythmVar
                ? lengths[rng.Next(lengths.Count)]
                : src[step % src.Count].LengthBeats;

            int outPitch = inScale ? ctx.Snap(pitch) : pitch;
            outp.Add(U.Note(outPitch, t, len * (1.0 / Math.Max(0.25, density)), vel));

            // Step: either replay the source's next move, or draw a fresh one from the pool.
            int interval = rng.NextDouble() < pitchVar
                ? intervals[rng.Next(intervals.Count)]
                : intervals[step % intervals.Count];
            pitch += interval;
            // Fold back inside the range instead of clamping, which would flatten the line.
            while (pitch > hi) pitch -= 12;
            while (pitch < lo) pitch += 12;

            double gap = rng.NextDouble() < rhythmVar
                ? gaps[rng.Next(gaps.Count)]
                : gaps[step % gaps.Count];
            t += Math.Max(U.MinLength, gap / density);
            step++;
            if (step > 4096) break;
        }
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }

    // -------------------------------------------------------------------- Stacks

    private static readonly string[] StackIntervals = { "3rds", "4ths", "5ths", "Octaves", "Cluster" };
    private static readonly string[] StackVoicings = { "Close", "Open", "Drop 2", "Spread", "Power" };

    public static MidiTool Stacks { get; } = new(
        "stacks", "Stacks", MidiToolKind.Generate,
        "Grows every note into a chord, voiced the way you would play it.",
        new[]
        {
            MidiToolParam.Int("voices", "Voices", 1, 5, 2),
            MidiToolParam.Choice("interval", "Interval", StackIntervals, 0),
            MidiToolParam.Choice("voicing", "Voicing", StackVoicings, 0),
            MidiToolParam.Int("spread", "Spread", 0, 3, 0, " oct"),
            MidiToolParam.Float("falloff", "Vel Falloff", 0, 1, 0.15, "×"),
            MidiToolParam.Toggle("scale", "In Scale", true),
        },
        RunStacks);

    private static List<NotaNote> RunStacks(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int voices = s.Int("voices"), intervalMode = s.Int("interval"), voicing = s.Int("voicing");
        int spread = s.Int("spread");
        double falloff = s["falloff"];
        bool inScale = s.Flag("scale") && ctx.ScaleOn;
        var degrees = ctx.Degrees;

        // Diatonic stacking walks the scale a fixed number of degrees per voice. With no
        // scale to walk, a uniform semitone step would stack augmented triads out of
        // "3rds", so chromatic mode follows a chord-tone table instead.
        int degStep = intervalMode switch { 0 => 2, 1 => 3, 2 => 4, 3 => degrees.Length, _ => 1 };
        int[] semiStack = intervalMode switch
        {
            0 => new[] { 0, 4, 7, 11, 14, 17 },   // triad into the seventh and ninth
            1 => new[] { 0, 5, 10, 15, 20, 25 },
            2 => new[] { 0, 7, 14, 21, 28, 35 },
            3 => new[] { 0, 12, 24, 36, 48, 60 },
            _ => new[] { 0, 1, 2, 3, 4, 5 },
        };

        var outp = new List<NotaNote>();
        foreach (var root in U.Sorted(input))
        {
            var stack = new List<int> { root.Pitch };
            for (int v = 1; v <= voices; v++)
            {
                int p = inScale
                    ? MidiScales.PitchAtDegree(degrees, ctx.ScaleRoot,
                        MidiScales.DegreeOfPitch(degrees, ctx.ScaleRoot, root.Pitch) + degStep * v, 0)
                    : root.Pitch + semiStack[Math.Min(v, semiStack.Length - 1)];
                stack.Add(p + 12 * spread * v);
            }

            switch (voicing)
            {
                case 1:   // Open — lift every other voice an octave
                    for (int i = 1; i < stack.Count; i += 2) stack[i] += 12;
                    break;
                case 2:   // Drop 2 — the second voice from the top falls an octave
                    if (stack.Count >= 2) stack[^2] -= 12;
                    break;
                case 3:   // Spread — fan the stack out an octave per voice
                    for (int i = 0; i < stack.Count; i++) stack[i] += 12 * i;
                    break;
                case 4:   // Power — root, fifth, octave only
                    stack = new List<int> { root.Pitch, root.Pitch + 7, root.Pitch + 12 }
                        .Take(Math.Max(2, voices + 1)).ToList();
                    break;
            }

            for (int i = 0; i < stack.Count; i++)
                outp.Add(U.Note(stack[i], root.StartBeat, root.LengthBeats, root.Velocity * (1 - falloff * i / Math.Max(1, stack.Count - 1.0))));
        }
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }

    // ----------------------------------------------------------------- Euclidean

    public static MidiTool Euclidean { get; } = new(
        "euclidean", "Euclidean", MidiToolKind.Generate,
        "Spreads a number of hits as evenly as a number of steps allows.",
        new[]
        {
            MidiToolParam.Int("steps", "Steps", 1, 32, 16),
            MidiToolParam.Int("pulses", "Pulses", 0, 32, 5),
            MidiToolParam.Int("rotation", "Rotate", -31, 31, 0),
            MidiToolParam.Rate("rate", "Rate"),
            MidiToolParam.Note("pitch", "Pitch", 36),
            MidiToolParam.Float("gate", "Gate", 0.05, 2.0, 0.5, "×"),
            MidiToolParam.Percent("velocity", "Velocity", 0.85),
            MidiToolParam.Int("accents", "Accents", 0, 32, 0),
            MidiToolParam.Percent("accentvel", "Accent Vel", 1.0),
        },
        RunEuclidean);

    private static List<NotaNote> RunEuclidean(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int steps = s.Int("steps");
        int pulses = Math.Min(s.Int("pulses"), steps);
        int rotation = s.Int("rotation");
        double rate = s.RateBeats("rate"), gate = s["gate"];
        int pitch = s.Int("pitch");
        double velocity = s["velocity"], accentVel = s["accentvel"];
        int accents = Math.Min(s.Int("accents"), pulses);

        var pattern = U.Euclid(steps, pulses, rotation);
        // A second Euclidean ride over the hits themselves picks which ones land hard.
        var accentPattern = accents > 0 ? U.Euclid(Math.Max(1, pulses), accents, 0) : Array.Empty<bool>();

        var outp = new List<NotaNote>();
        int total = (int)Math.Ceiling(ctx.LengthBeats / rate);
        int hitIndex = 0;
        for (int i = 0; i < total; i++)
        {
            if (!pattern[U.Mod(i, steps)]) continue;
            bool accent = accentPattern.Length > 0 && accentPattern[U.Mod(hitIndex, accentPattern.Length)];
            outp.Add(U.Note(pitch, i * rate, rate * gate, accent ? accentVel : velocity));
            hitIndex++;
        }
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }

    // ------------------------------------------------------------- Melodic Steps

    private static readonly string[] Contours =
        { "Up", "Down", "Arch", "Valley", "Zigzag", "Random Walk", "Random" };

    public static MidiTool MelodicSteps { get; } = new(
        "melodicsteps", "Melodic Steps", MidiToolKind.Generate,
        "A step sequencer in scale degrees — pick a contour and a span, get a line.",
        new[]
        {
            MidiToolParam.Int("steps", "Steps", 1, 32, 8),
            MidiToolParam.Rate("rate", "Rate", 3),
            MidiToolParam.Choice("contour", "Contour", Contours, 2),
            MidiToolParam.Int("range", "Range", 1, 21, 7, " deg"),
            MidiToolParam.Note("root", "Root", 60),
            MidiToolParam.Percent("density", "Density", 0.85),
            MidiToolParam.Float("gate", "Gate", 0.05, 2.0, 0.9, "×"),
            MidiToolParam.Percent("velocity", "Velocity", 0.8),
            MidiToolParam.Percent("velvar", "Vel Var", 0.15),
        },
        RunMelodicSteps);

    private static List<NotaNote> RunMelodicSteps(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int steps = s.Int("steps"), range = s.Int("range"), contour = s.Int("contour");
        double rate = s.RateBeats("rate"), gate = s["gate"];
        double density = s["density"], velocity = s["velocity"], velVar = s["velvar"];
        int rootNote = s.Int("root");
        var rng = U.Rng(ctx, "melodicsteps");
        var degrees = ctx.Degrees;
        int baseDegree = MidiScales.DegreeOfPitch(degrees, ctx.ScaleRoot, rootNote);

        // Settle the degree sequence once so the pattern repeats rather than wanders off.
        var seq = new int[steps];
        int walk = 0;
        for (int i = 0; i < steps; i++)
        {
            double f = steps == 1 ? 0 : i / (double)(steps - 1);
            seq[i] = contour switch
            {
                0 => (int)Math.Round(f * range),
                1 => (int)Math.Round((1 - f) * range),
                2 => (int)Math.Round(Math.Sin(f * Math.PI) * range),
                3 => (int)Math.Round((1 - Math.Sin(f * Math.PI)) * range),
                4 => i % 2 == 0 ? 0 : range,
                5 => walk = Math.Clamp(walk + rng.Next(-2, 3), 0, range),
                _ => rng.Next(0, range + 1),
            };
        }
        var rests = new bool[steps];
        for (int i = 0; i < steps; i++) rests[i] = rng.NextDouble() > density;

        var outp = new List<NotaNote>();
        int total = (int)Math.Ceiling(ctx.LengthBeats / rate);
        for (int i = 0; i < total; i++)
        {
            int slot = U.Mod(i, steps);
            if (rests[slot]) continue;
            int pitch = MidiScales.PitchAtDegree(degrees, ctx.ScaleRoot, baseDegree + seq[slot], 0);
            double vel = velocity + (velVar > 0 ? (rng.NextDouble() * 2 - 1) * velVar : 0);
            outp.Add(U.Note(pitch, i * rate, rate * gate, vel));
        }
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }

    // --------------------------------------------------------------------- Shape

    private static readonly string[] Shapes =
        { "Ramp Up", "Ramp Down", "Sine", "Triangle", "Exponential", "Logarithmic", "S-Curve", "Square", "Random Walk" };

    public static MidiTool Shape { get; } = new(
        "shape", "Shape", MidiToolKind.Generate,
        "Traces a curve across the clip and drops a note wherever it passes a step.",
        new[]
        {
            MidiToolParam.Choice("shape", "Shape", Shapes, 2),
            MidiToolParam.Rate("rate", "Rate", 4),
            MidiToolParam.Int("range", "Range", 1, 36, 12, " st"),
            MidiToolParam.Note("root", "Root", 60),
            MidiToolParam.Float("cycles", "Cycles", 0.25, 8, 1, "×"),
            MidiToolParam.Float("phase", "Phase", 0, 1, 0, ""),
            MidiToolParam.Float("gate", "Gate", 0.05, 2.0, 0.9, "×"),
            MidiToolParam.Percent("velocity", "Velocity", 0.8),
            MidiToolParam.Toggle("velfollow", "Vel Follows", false),
            MidiToolParam.Toggle("scale", "In Scale", true),
        },
        RunShape);

    private static List<NotaNote> RunShape(IReadOnlyList<NotaNote> input, MidiToolSettings s, MidiToolContext ctx)
    {
        int shape = s.Int("shape"), range = s.Int("range"), rootNote = s.Int("root");
        double rate = s.RateBeats("rate"), cycles = s["cycles"], phase = s["phase"];
        double gate = s["gate"], velocity = s["velocity"];
        bool velFollow = s.Flag("velfollow"), inScale = s.Flag("scale");
        var rng = U.Rng(ctx, "shape");

        var outp = new List<NotaNote>();
        int total = (int)Math.Ceiling(ctx.LengthBeats / rate);
        double walk = 0.5;

        for (int i = 0; i < total; i++)
        {
            double t = (i / (double)Math.Max(1, total) * cycles + phase) % 1.0;
            double unit = shape switch
            {
                0 => t,
                1 => 1 - t,
                2 => 0.5 + 0.5 * Math.Sin(t * Math.PI * 2),
                3 => 1 - Math.Abs(t * 2 - 1),
                4 => Math.Pow(t, 2.5),
                5 => Math.Pow(t, 1 / 2.5),
                6 => U.SCurve(t, 1.0),
                7 => t < 0.5 ? 0 : 1,
                _ => walk = Math.Clamp(walk + (rng.NextDouble() - 0.5) * 0.4, 0, 1),
            };
            int pitch = rootNote + (int)Math.Round(unit * range);
            if (inScale) pitch = ctx.Snap(pitch);
            double vel = velFollow ? 0.25 + unit * 0.75 : velocity;
            outp.Add(U.Note(pitch, i * rate, rate * gate, vel));
        }
        return U.Deoverlap(U.Tidy(outp, ctx.LengthBeats));
    }
}
