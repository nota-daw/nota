// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shared machinery for the clip tools: chord grouping, note hygiene (sort, clamp,
// de-overlap), and the shaping curves the time/velocity tools draw through.

namespace Nota.Application.Midi;

public static class MidiToolUtil
{
    /// <summary>Notes starting within this many beats of each other count as one chord.</summary>
    public const double ChordTolerance = 1.0 / 64;

    public const double MinLength = 1.0 / 64;

    public static List<NotaNote> Sorted(IEnumerable<NotaNote> notes)
    {
        var list = new List<NotaNote>(notes);
        list.Sort(static (a, b) =>
        {
            int c = a.StartBeat.CompareTo(b.StartBeat);
            return c != 0 ? c : a.Pitch.CompareTo(b.Pitch);
        });
        return list;
    }

    public static NotaNote Note(int pitch, double start, double length, double velocity) => new(
        Math.Clamp(pitch, 0, 127),
        start,
        Math.Max(MinLength, length),
        (float)Math.Clamp(velocity, 0.01, 1.0));

    /// <summary>Drops notes that start past the clip, trims those that overrun it, and
    /// clamps pitch and velocity. Every tool ends with this so nothing has to be careful.</summary>
    public static List<NotaNote> Tidy(IEnumerable<NotaNote> notes, double lengthBeats)
    {
        var outp = new List<NotaNote>();
        foreach (var n in notes)
        {
            double start = Math.Max(0, n.StartBeat);
            if (start >= lengthBeats - 1e-9) continue;
            double len = Math.Min(Math.Max(MinLength, n.LengthBeats), lengthBeats - start);
            outp.Add(Note(n.Pitch, start, len, n.Velocity));
        }
        return Sorted(outp);
    }

    /// <summary>Removes exact duplicates (same pitch at the same instant) and shortens a
    /// note that runs into the next one on the same pitch, which is what a MIDI stream
    /// needs to stay unambiguous.</summary>
    public static List<NotaNote> Deoverlap(List<NotaNote> notes)
    {
        var sorted = Sorted(notes);
        var byPitch = new Dictionary<int, int>();   // pitch -> index of the last note kept
        var result = new List<NotaNote>(sorted.Count);
        foreach (var n in sorted)
        {
            if (byPitch.TryGetValue(n.Pitch, out int prev))
            {
                var p = result[prev];
                if (Math.Abs(p.StartBeat - n.StartBeat) < 1e-6) continue;   // duplicate
                double maxLen = n.StartBeat - p.StartBeat;
                if (p.LengthBeats > maxLen) { p.LengthBeats = Math.Max(MinLength, maxLen); result[prev] = p; }
            }
            result.Add(n);
            byPitch[n.Pitch] = result.Count - 1;
        }
        return result;
    }

    /// <summary>Groups notes into chords by start time. Each group is ascending by pitch.</summary>
    public static List<List<NotaNote>> Chords(IReadOnlyList<NotaNote> notes, double tolerance = ChordTolerance)
    {
        var groups = new List<List<NotaNote>>();
        var sorted = Sorted(notes);
        foreach (var n in sorted)
        {
            if (groups.Count > 0 && n.StartBeat - groups[^1][0].StartBeat <= tolerance) groups[^1].Add(n);
            else groups.Add(new List<NotaNote> { n });
        }
        foreach (var g in groups) g.Sort(static (a, b) => a.Pitch.CompareTo(b.Pitch));
        return groups;
    }

    /// <summary>A seed that varies per tool so two tools on the same clip seed don't draw
    /// the same sequence.</summary>
    public static Random Rng(MidiToolContext ctx, string salt)
    {
        unchecked
        {
            int h = ctx.Seed * 397;
            foreach (char c in salt) h = h * 31 + c;
            return new Random(h);
        }
    }

    /// <summary>Bends 0..1 through a tension curve. <paramref name="tension"/> 0 is linear,
    /// +1 pushes everything late (ease-in), -1 pulls it early (ease-out).</summary>
    public static double Bend(double t, double tension)
    {
        t = Math.Clamp(t, 0, 1);
        if (Math.Abs(tension) < 1e-6) return t;
        double k = 1 + Math.Abs(tension) * 3;
        return tension > 0 ? Math.Pow(t, k) : 1 - Math.Pow(1 - t, k);
    }

    /// <summary>A symmetric S-curve on 0..1; <paramref name="amount"/> 0 is linear.</summary>
    public static double SCurve(double t, double amount)
    {
        t = Math.Clamp(t, 0, 1);
        double s = t * t * (3 - 2 * t);
        return t + (s - t) * Math.Clamp(amount, -1, 1);
    }

    public static int Mod(int a, int n) => ((a % n) + n) % n;

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Fisher-Yates over a fraction of the list: <paramref name="amount"/> 1 fully
    /// shuffles, 0 leaves it alone, and values between swap that share of the positions —
    /// which is what makes "a bit more random" a usable knob.</summary>
    public static void PartialShuffle<T>(IList<T> items, double amount, Random rng)
    {
        amount = Math.Clamp(amount, 0, 1);
        if (items.Count < 2 || amount <= 0) return;
        for (int i = items.Count - 1; i > 0; i--)
        {
            if (rng.NextDouble() > amount) continue;
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Bjorklund's algorithm: spreads <paramref name="pulses"/> hits as evenly as
    /// possible over <paramref name="steps"/> slots, then rotates the result.</summary>
    public static bool[] Euclid(int steps, int pulses, int rotation)
    {
        steps = Math.Max(1, steps);
        pulses = Math.Clamp(pulses, 0, steps);
        var pattern = new bool[steps];
        if (pulses == 0) return pattern;
        if (pulses == steps) { Array.Fill(pattern, true); return Rotate(pattern, rotation); }

        // Bjorklund's distribution, in closed form: step i is a hit when it opens a new
        // group. Hits the downbeat first, which is what makes it usable as a rhythm.
        for (int i = 0; i < steps; i++) pattern[i] = i * pulses % steps < pulses;
        return Rotate(pattern, rotation);
    }

    public static bool[] Rotate(bool[] pattern, int by)
    {
        int n = pattern.Length;
        if (n == 0) return pattern;
        var outp = new bool[n];
        for (int i = 0; i < n; i++) outp[i] = pattern[Mod(i + by, n)];
        return outp;
    }
}
