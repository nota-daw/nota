// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Test material for Nota Aurora's custom wavetables (specs/053, quickstart § 2 and § 4):
// generated, deterministic files for each way a file becomes a table, and the projects the
// UI budget is measured with. `--aurora-fixtures <dir>` writes the files;
// `--aurora-ui-fixture <dir> [--builtin]` writes the measurement project (with --builtin the
// v27-era baseline on the built-in tables, without it the same project with a generated
// 256-frame table on both oscillators of every Aurora).

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class AuroraFixtures
{
    public const int Frame = 2048;

    /// <summary>Writes the quickstart § 2 files into <paramref name="dir"/>; returns their paths by name.</summary>
    public static Dictionary<string, string> Write(string dir)
    {
        Directory.CreateDirectory(dir);
        var paths = new Dictionary<string, string>();
        string P(string name) => paths[name] = Path.Combine(dir, name);

        WriteMono(P("cycle-2048.wav"), Saw(Frame, 1));
        AuroraTableWav.Write(P("table-64x2048.wav"), Morph(64));
        WriteMono(P("table-300x2048.wav"), Morph(300));
        WriteMono(P("cycle-600.wav"), Saw(600, 1));
        WriteMono(P("long-90s.wav"), Sweep(44100 * 90, 44100));
        WriteMono(P("silence.wav"), new float[44100]);
        WriteMono(P("sweep-3s.wav"), Sweep(44100 * 3, 44100));
        // A two-second stereo "vowel": a buzz through two moving formants, as FLAC.
        using (var enc = new NativeEncoder(P("vowel-2s.flac"), 44100, 2, WavBitDepth.Pcm16))
        {
            var v = Vowel(44100 * 2, 44100);
            var st = new float[v.Length * 2];
            for (int i = 0; i < v.Length; i++) { st[2 * i] = v[i]; st[2 * i + 1] = v[i] * 0.8f; }
            enc.WriteFrames(st, v.Length);
            enc.Close();
        }
        File.WriteAllBytes(P("broken.wav"), "RIFF\u0010\0\0\0WAVEjunkjunk"u8.ToArray());
        File.WriteAllBytes(P("x.aiff"), "FORM\0\0\0\u0004AIFF"u8.ToArray());
        return paths;
    }

    /// <summary>One band-limited saw cycle of n samples (harmonics up to `n/2`), peak about 0.6.</summary>
    public static float[] Saw(int n, int cycles)
    {
        var x = new float[n];
        int top = Math.Min(n / 2 - 1, 512);
        for (int i = 0; i < n; i++)
        {
            double s = 0, ph = 2 * Math.PI * cycles * i / n;
            for (int h = 1; h <= top; h++) s += Math.Sin(h * ph) / h;
            x[i] = (float)(0.35 * s);
        }
        return x;
    }

    /// <summary>A bright table: frame f of `count` moves from a saw to a pulse; every
    /// harmonic up to 1023 present, so a high note would fold back without band-limiting.</summary>
    public static float[] Morph(int count)
    {
        var t = new float[count * Frame];
        var saw = new double[Frame];
        var sq = new double[Frame];
        for (int i = 0; i < Frame; i++)
        {
            double ph = (i + 0.5) / Frame;
            saw[i] = 2 * ph - 1;                 // naive: full bandwidth on purpose
            sq[i] = ph < 0.5 ? 0.8 : -0.8;
        }
        for (int f = 0; f < count; f++)
        {
            double m = count > 1 ? (double)f / (count - 1) : 0;
            for (int i = 0; i < Frame; i++) t[f * Frame + i] = (float)(saw[i] * (1 - m) + sq[i] * m) * 0.7f;
        }
        return t;
    }

    /// <summary>An exponential sine sweep from 60 Hz to 6 kHz, at 0.5.</summary>
    public static float[] Sweep(int n, int sr)
    {
        var x = new float[n];
        double f0 = 60, f1 = 6000, T = (double)n / sr, k = Math.Log(f1 / f0);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double ph = 2 * Math.PI * f0 * T / k * (Math.Exp(t / T * k) - 1);
            x[i] = (float)(0.5 * Math.Sin(ph));
        }
        return x;
    }

    private static float[] Vowel(int n, int sr)
    {
        var x = new float[n];
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr, m = t / ((double)n / sr);
            ph += 110.0 / sr;
            double s = 0;
            for (int h = 1; h <= 60; h++)
            {
                double fh = 110.0 * h;
                double a1 = Math.Exp(-Math.Pow((fh - (700 - 400 * m)) / 120, 2));
                double a2 = Math.Exp(-Math.Pow((fh - (1200 + 900 * m)) / 180, 2));
                s += (a1 + 0.6 * a2 + 0.02) * Math.Sin(2 * Math.PI * h * ph);
            }
            x[i] = (float)(0.3 * s);
        }
        return x;
    }

    public static void WriteMono(string path, float[] samples)
    {
        using var w = new Nota.Infrastructure.WavWriter(path, 44100, 1, WavBitDepth.Float32);
        w.WriteFrames(samples, samples.Length);
        w.Close();
    }

    // ---- the UI measurement project (quickstart § 4) ----

    /// <summary>Four Auroras on Formant / Chroma with both positions under LFO modulation, and a
    /// four-bar six-note chord loop. Without <paramref name="builtin"/> each oscillator of each
    /// Aurora also holds a generated 256-frame table.</summary>
    public static int WriteUiProject(string dir, bool builtin)
    {
        using var eng = new NotaEngine();
        eng.SetBpm(120);
        var tables = builtin ? null : new[] { Morph(256), Sweep256() };
        for (int k = 0; k < 4; k++)
        {
            int t = eng.AddWavetableSynthTrack();
            Set(eng, t, "table", 0.667f);          // Osc 1 Formant
            Set(eng, t, "osc2on", 1f);
            Set(eng, t, "osc2table", 1f);          // Osc 2 Chroma
            Set(eng, t, "osc2level", 0.6f);
            Set(eng, t, "lfo1rate", 0.5f);         // ≈ 1 Hz
            Set(eng, t, "lfo1depth", 1f);
            Set(eng, t, "lfo2rate", 0.6157f);      // ≈ 2 Hz
            Set(eng, t, "lfo2depth", 1f);
            Set(eng, t, "mtx2_2", 0.9f);           // LFO 1 → Position
            Set(eng, t, "mtx3_2", 0.8f);           // LFO 2 → Position
            int clip = eng.AddMidiClip(t, 0, 16);
            eng.SetClipNotes(t, clip, new[]
            {
                new NotaNote(48, 0, 16, 0.8f), new NotaNote(55, 0, 16, 0.7f), new NotaNote(60, 0, 16, 0.7f),
                new NotaNote(64, 0, 16, 0.7f), new NotaNote(67, 0, 16, 0.7f), new NotaNote(71, 0, 16, 0.6f),
            });
            if (tables is null) continue;
            for (int o = 0; o < 2; o++)
            {
                if (eng.AuroraTableFromFrames(tables[o], 256, o == 0 ? "morph-256" : "sweep-256", 0, out var h) != 0) return 1;
                try { eng.AuroraSetTable(t, o, h, undoable: false); }
                finally { eng.AuroraTableFree(h); }
            }
        }
        var warnings = new List<string>();
        var doc = ProjectService.Capture(eng, new TransportState(120.0, 1.0, false, true), warnings);
        ProjectService.Save(doc, dir, eng);
        Console.WriteLine($"wrote {dir} ({(builtin ? "built-in tables, v27-era content" : "256-frame tables on both oscillators")}, format {doc.FormatVersion})");
        return 0;
    }

    private static float[] Sweep256()
    {
        // 256 frames, frame f a sine at harmonic 1 + f / 4 plus its octave: a slow spectral sweep.
        var t = new float[256 * Frame];
        for (int f = 0; f < 256; f++)
        {
            double h = 1 + f / 4.0;
            for (int i = 0; i < Frame; i++)
            {
                double ph = 2 * Math.PI * i / Frame;
                t[f * Frame + i] = (float)(0.6 * Math.Sin(Math.Round(h) * ph) + 0.3 * Math.Sin(2 * Math.Round(h) * ph));
            }
        }
        return t;
    }

    private static void Set(NotaEngine e, int t, string id, float v)
    {
        int n = e.PluginParamCount(t, -1);
        for (int i = 0; i < n; i++)
            if (e.PluginParamId(t, -1, i) == id) { e.PluginParamSet(t, -1, i, v); return; }
        throw new InvalidOperationException($"Aurora has no parameter '{id}'");
    }
}
