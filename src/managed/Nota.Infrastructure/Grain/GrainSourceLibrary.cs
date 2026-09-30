// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Materializes Nota Grain's factory sources on disk, the way KitLibrary does the drum kits:
// a source is rendered the first time something asks for it (a preset applied, a preview)
// and after its recipe changes, and read from disk every other time. Each file has a stamp
// beside it holding the renderer version and the source's revision.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nota.Infrastructure.Kits;

namespace Nota.Infrastructure.Grain;

public static class GrainSourceLibrary
{
    /// <summary>Bump when the shared rendering (levelling, format) changes; a single
    /// source's recipe edit bumps its own <see cref="GrainSource.Rev"/>.</summary>
    private const int RendererVersion = 1;

    /// <summary>The loudness every source is levelled to: its loud passages sit at the
    /// built-in pad's RMS, so a preset sounds as loud whichever source it plays.</summary>
    private const double TargetRms = 0.14;
    private const double PeakCeiling = 0.89;   // −1 dBFS

    private static readonly object Gate = new();

    /// <summary>A managed folder under the Nota data dir — regenerable, so not the user's samples.</summary>
    public static string Root => NotaPaths.SubDir("grain-sources");

    public static string PathOf(GrainSource s) => Path.Combine(Root, Sanitize(s.Name) + ".wav");
    private static string StampOf(GrainSource s) => Path.Combine(Root, "." + s.Id + ".stamp");
    private static string StampFor(GrainSource s) => $"v{RendererVersion}-{s.Id}-r{s.Rev}-{s.Root}-{s.Seconds}-{s.GainDb}";

    public static bool IsRendered(GrainSource s)
    {
        try { return File.Exists(PathOf(s)) && File.Exists(StampOf(s)) && File.ReadAllText(StampOf(s)).Trim() == StampFor(s); }
        catch { return false; }
    }

    /// <summary>The source's WAV path, rendering it first if it is missing or stale; null for
    /// an unknown id or a failed render.</summary>
    public static string? Ensure(string id)
    {
        var s = GrainSources.ById(id);
        if (s is null) return null;
        lock (Gate)
        {
            if (IsRendered(s)) return PathOf(s);
            try
            {
                var path = PathOf(s);
                var data = Render(s);
                var tmp = path + ".part";
                using (var w = new WavWriter(tmp, SourceBuffer.Rate, 2, WavBitDepth.Pcm24))
                    w.WriteFrames(data, data.Length / 2);
                File.Move(tmp, path, overwrite: true);   // never leave a half-written sample behind
                File.WriteAllText(StampOf(s), StampFor(s));
                return path;
            }
            catch { return null; }
        }
    }

    /// <summary>Renders every source that is missing or stale; returns how many were written.</summary>
    public static int EnsureAll()
    {
        int n = 0;
        foreach (var s in GrainSources.All)
        {
            bool was = IsRendered(s);
            if (Ensure(s.Id) is not null && !was) n++;
        }
        return n;
    }

    /// <summary>Synthesizes the source and levels it; interleaved stereo frames.</summary>
    public static float[] Render(GrainSource s)
    {
        var b = new SourceBuffer(s.Seconds, Fnv(s.Id));
        s.Render(b);
        double gain = LevelGain(b.L, b.R, KitDsp.Db(s.GainDb));
        var inter = new float[b.N * 2];
        for (int i = 0; i < b.N; i++)
        {
            inter[2 * i] = (float)(b.L[i] * gain);
            inter[2 * i + 1] = (float)(b.R[i] * gain);
        }
        return inter;
    }

    // Loudness by the loud passages (the 85th-percentile 50 ms window) rather than the
    // whole file, so a decaying pluck is levelled by its note, not by its silence.
    private static double LevelGain(double[] l, double[] r, double extra)
    {
        int win = SourceBuffer.Rate / 20;
        var rms = new List<double>();
        double peak = 0;
        for (int s = 0; s + win <= l.Length; s += win)
        {
            double acc = 0;
            for (int i = s; i < s + win; i++)
            {
                acc += (l[i] * l[i] + r[i] * r[i]) * 0.5;
                peak = Math.Max(peak, Math.Max(Math.Abs(l[i]), Math.Abs(r[i])));
            }
            rms.Add(Math.Sqrt(acc / win));
        }
        if (rms.Count == 0 || peak < 1e-9) return 1;
        rms.Sort();
        double loud = rms[(int)(rms.Count * 0.85)];
        double g = loud > 1e-9 ? TargetRms * extra / loud : 1;
        return Math.Min(g, PeakCeiling / peak);
    }

    private static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(Path.GetInvalidFileNameChars().AsSpan().Contains(c) ? '-' : c);
        return sb.ToString();
    }
}
