// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Aurora sound-preservation gate (specs/036-aurora-perf): every optimization step must
// prove it did not change the sound. Ten factory presets cover all DSP blocks (both
// oscillators, sub, all five warp modes, filter types/slopes, routing, matrix sources,
// chorus voices, reverb modes, drive modes, mono). Each preset renders a fixed chord on
// two fresh engines: the renders must null below −120 dB (determinism — catches
// uninitialized state or order dependence introduced by hoisting).
// Cross-build before/after proof uses dump/compare modes (raw float32, temp only):
// dump on the base engine, compare on the feature engine, gate −120 dB (Tier-1) or
// −50 dB (Tier-2). Also runnable alone: `--aurora-null`, `--aurora-null-dump <dir>`,
// `--aurora-null-compare <dir> [gateDb]`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class AuroraNullTests
{
    private const int Sr = 48000;
    private const int Aurora = 5;
    private const double Tier1Db = -120.0;

    // (preset name, param id/value pairs) — mirrors FactoryPresetCatalog aurora entries.
    private static readonly (string Name, (string Id, float V)[] Params)[] Presets = new[]
    {
        ("Super Saw", new[] { ("table", 0f), ("position", 0.9f), ("osc2on", 1f), ("osc2table", 0f),
            ("osc2position", 0.9f), ("osc2level", 0.8f), ("osc2detune", 0.62f), ("unison", 1f),
            ("unidetune", 0.4f), ("unispread", 0.9f), ("cutoff", 0.8f), ("resonance", 0.14f),
            ("fil1env", 0.55f), ("attack", 0f), ("decay", 0.4f), ("sustain", 0.85f), ("release", 0.28f),
            ("fxdrive", 0.2f), ("fxchorus", 0.26f), ("fxreverb", 0.24f), ("gain", 0.72f) }),
        ("Warm Pad", new[] { ("table", 0f), ("position", 0.30f), ("warp", 0.50f), ("unison", 0.333f),
            ("unidetune", 0.26f), ("unispread", 0.55f), ("attack", 0.50f), ("decay", 0.40f),
            ("sustain", 0.85f), ("release", 0.60f), ("cutoff", 0.50f), ("resonance", 0.10f),
            ("fil1env", 0.60f), ("gain", 0.80f) }),
        ("Sub Bass", new[] { ("table", 0f), ("position", 0.6f), ("osc1oct", 0.333f), ("sublevel", 0.85f),
            ("suboct", 0.5f), ("cutoff", 0.42f), ("resonance", 0.2f), ("fil1env", 0.72f), ("fil1slope", 1f),
            ("attack", 0f), ("decay", 0.3f), ("sustain", 0.6f), ("release", 0.18f), ("mono", 1f),
            ("unison", 0f), ("gain", 0.85f) }),
        ("Reese Bass", new[] { ("table", 0f), ("position", 0.8f), ("osc1oct", 0.333f), ("osc2on", 1f),
            ("osc2table", 0f), ("osc2position", 0.8f), ("osc2level", 0.85f), ("osc2oct", 0.333f),
            ("osc2detune", 0.62f), ("unison", 0.333f), ("unidetune", 0.22f), ("unispread", 0.4f),
            ("cutoff", 0.34f), ("resonance", 0.26f), ("fil1slope", 1f), ("fil1env", 0.64f),
            ("attack", 0f), ("decay", 0.32f), ("sustain", 0.7f), ("release", 0.16f), ("mono", 1f),
            ("fxdrive", 0.24f), ("fxdrivemode", 0.5f), ("gain", 0.82f) }),
        ("Acid Table", new[] { ("table", 0.333f), ("position", 0.46f), ("osc1oct", 0.333f),
            ("osc1warpmode", 0.75f), ("warp", 0.4f), ("cutoff", 0.30f), ("resonance", 0.72f),
            ("fil1slope", 1f), ("fil1env", 0.9f), ("env2attack", 0f), ("env2decay", 0.26f),
            ("env2sustain", 0.04f), ("env2release", 0.16f), ("attack", 0f), ("decay", 0.3f),
            ("sustain", 0.45f), ("release", 0.14f), ("mono", 1f), ("mtx4_3", 0.7f),
            ("fxdrive", 0.32f), ("gain", 0.84f) }),
        ("Wobble Wave", new[] { ("table", 0.333f), ("position", 0.55f), ("osc1oct", 0.333f),
            ("sublevel", 0.5f), ("suboct", 0.5f), ("cutoff", 0.34f), ("resonance", 0.44f),
            ("fil1slope", 1f), ("lfo1sync", 0.5714f), ("lfo1shape", 0f), ("lfo1depth", 0.85f),
            ("mtx2_3", 0.92f), ("attack", 0f), ("decay", 0.3f), ("sustain", 0.62f), ("release", 0.16f),
            ("mono", 1f), ("fxdrive", 0.3f), ("gain", 0.84f) }),
        ("Fold Lead", new[] { ("table", 0.333f), ("position", 0.5f), ("osc1warpmode", 1f),
            ("warp", 0.6f), ("cutoff", 0.82f), ("resonance", 0.22f), ("fil1env", 0.6f),
            ("attack", 0.01f), ("decay", 0.3f), ("sustain", 0.7f), ("release", 0.22f),
            ("lfo1depth", 0.6f), ("mtx2_3", 0.68f), ("mtx6_0", 0.54f), ("fxdrive", 0.3f),
            ("fxdrivemode", 1f), ("bendrange", 4f / 11f), ("gain", 0.72f) }),
        ("Glass Choir", new[] { ("table", 0.667f), ("position", 0.55f), ("osc2on", 1f),
            ("osc2table", 0f), ("osc2level", 0.55f), ("osc2oct", 0.667f), ("osc2detune", 0.56f),
            ("unison", 0.333f), ("unidetune", 0.34f), ("unispread", 0.7f), ("cutoff", 0.72f),
            ("resonance", 0.12f), ("fil1env", 0.62f), ("attack", 0.35f), ("decay", 0.5f),
            ("sustain", 0.8f), ("release", 0.6f), ("env2attack", 0.4f), ("env2sustain", 0.6f),
            ("mtx1_2", 0.78f), ("fxchorus", 0.4f), ("fxchorusvoices", 1f), ("fxreverb", 0.4f),
            ("fxreverbmode", 0.5f), ("gain", 0.78f) }),
        ("Aurora Sweep", new[] { ("table", 1f), ("position", 0.1f), ("osc1warpmode", 0.25f),
            ("warp", 0.55f), ("unison", 0.5f), ("unidetune", 0.3f), ("unispread", 0.75f),
            ("attack", 0.3f), ("decay", 0.55f), ("sustain", 0.8f), ("release", 0.7f),
            ("env2attack", 0.7f), ("env2decay", 0.7f), ("env2sustain", 0.9f), ("env2release", 0.8f),
            ("cutoff", 0.5f), ("resonance", 0.2f), ("fil1env", 0.72f), ("mtx1_2", 0.92f),
            ("fxchorus", 0.3f), ("fxreverb", 0.5f), ("fxreverbmode", 0.5f), ("mac0val", 1f),
            ("mac0dest", 4f / 11f), ("mac0amt", 0.9f), ("gain", 0.74f) }),
        ("Formant Pad", new[] { ("table", 0.667f), ("position", 0.42f), ("osc1warpmode", 0.5f),
            ("warp", 0.62f), ("unison", 0.667f), ("unidetune", 0.22f), ("unispread", 0.8f),
            ("attack", 0.46f), ("decay", 0.5f), ("sustain", 0.82f), ("release", 0.7f),
            ("cutoff", 0.58f), ("resonance", 0.24f), ("fil1type", 1f), ("fil1env", 0.56f),
            ("lfo1rate", 0.16f), ("lfo1depth", 0.5f), ("mtx2_2", 0.70f), ("fxreverb", 0.46f),
            ("fxreverbmode", 0.5f), ("fxreverbsize", 0.8f), ("gain", 0.76f) }),
    };

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var p in Presets)
        {
            var a = RenderPreset(p);
            var b = RenderPreset(p);
            if (a.Length == 0 || b.Length == 0)
            {
                yield return (false, $"aurora null: {p.Name} failed to render (unknown param id?)");
                continue;
            }
            double res = ResidualDb(a, b);
            double peak = Peak(a);
            bool finite = AllFinite(a) && AllFinite(b);
            yield return (finite && peak > 0.02 && res < Tier1Db,
                $"aurora null: {p.Name} renders deterministically (residual {res:0} dBFS, peak {peak:0.000})");
        }
    }

    // Dump one raw-float32 render per preset (temp comparison artifact, not committed).
    public static int Dump(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var p in Presets)
        {
            var buf = RenderPreset(p);
            if (buf.Length == 0) { Console.WriteLine($"dump failed: {p.Name}"); return 1; }
            var bytes = new byte[buf.Length * 4];
            Buffer.BlockCopy(buf, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(dir, Sanitize(p.Name) + ".f32"), bytes);
        }
        return 0;
    }

    // Null current renders against a Dump dir. Gate defaults to Tier-1 (−120 dB).
    public static IEnumerable<(bool ok, string label)> Compare(string dir, double gateDb = Tier1Db)
    {
        foreach (var p in Presets)
        {
            string path = Path.Combine(dir, Sanitize(p.Name) + ".f32");
            if (!File.Exists(path))
            {
                yield return (false, $"aurora null: {p.Name} has no reference render in {dir}");
                continue;
            }
            var raw = File.ReadAllBytes(path);
            var reference = new float[raw.Length / 4];
            Buffer.BlockCopy(raw, 0, reference, 0, raw.Length);
            var now = RenderPreset(p);
            if (now.Length == 0 || now.Length != reference.Length)
            {
                yield return (false, $"aurora null: {p.Name} render failed or changed length");
                continue;
            }
            double res = ResidualDb(now, reference);
            yield return (res < gateDb,
                $"aurora null: {p.Name} matches reference (residual {res:0} dBFS vs gate {gateDb:0} dB)");
        }
    }

    private static string Sanitize(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    private static int Pi(NotaEngine e, int t, string id)
    {
        int n = e.PluginParamCount(t, -1);
        for (int i = 0; i < n; i++) if (e.PluginParamId(t, -1, i) == id) return i;
        return -1;
    }

    // A fixed 4-note chord on a fresh engine: fresh state each call is what makes the
    // double render a determinism proof. Empty array = unknown param id (preset drift).
    private static float[] RenderPreset((string Name, (string Id, float V)[] Params) p)
    {
        using var eng = new NotaEngine();
        eng.SetBpm(120);
        int t = eng.AddInstrumentTrack();
        eng.SetTrackBuiltinInstrument(t, Aurora);
        foreach (var (id, v) in p.Params)
        {
            int idx = Pi(eng, t, id);
            if (idx < 0) return Array.Empty<float>();
            eng.PluginParamSet(t, -1, idx, v);
        }
        int clip = eng.AddMidiClip(t, 0, 4);
        eng.SetClipNotes(t, clip, new[]
        {
            new NotaNote(48, 0, 4, 0.8f), new NotaNote(55, 0, 4, 0.75f),
            new NotaNote(60, 0, 4, 0.7f), new NotaNote(64, 0, 4, 0.7f),
        });
        const int frames = (int)(2.5 * Sr), block = 512;
        var buf = new float[frames * 2];
        var chunk = new float[block * 2];
        eng.Seek(0);
        eng.Play();
        for (int done = 0; done < frames;)
        {
            int m = Math.Min(block, frames - done);
            eng.RenderOffline(chunk, m, Sr);
            Array.Copy(chunk, 0, buf, done * 2, m * 2);
            done += m;
        }
        eng.StopTransport();
        return buf;
    }

    private static double ResidualDb(float[] a, float[] b)
    {
        double worst = 0;
        for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        return 20 * Math.Log10(Math.Max(worst, 1e-12));
    }

    private static double Peak(float[] buf)
    {
        double peak = 0;
        foreach (float v in buf) peak = Math.Max(peak, Math.Abs(v));
        return peak;
    }

    private static bool AllFinite(float[] buf)
    {
        foreach (float v in buf) if (!float.IsFinite(v)) return false;
        return true;
    }
}
