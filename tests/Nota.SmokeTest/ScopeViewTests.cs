// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Modular Scope view (specs/047): the engine records a Scope's input with each block's
// time step, reads back the newest entries, and keeps the view settings (window, sync, zoom,
// trigger, height) as modulator fields that clamp, save and default on older projects.
// ScopeTrace lays the history into the window: a 2 s window of a 1 Hz LFO shows two cycles,
// the rising-edge trigger holds a periodic trace still, and a short history right-aligns.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --scope`.

using System.Diagnostics;
using System.Text.RegularExpressions;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ScopeViewTests
{
    private const int Lfo = 0, Scope = 6, Block = 256;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in History()) yield return r;
        foreach (var r in Settings()) yield return r;
        foreach (var r in Trace()) yield return r;
    }

    private static int Rising(ReadOnlySpan<double> xs, ReadOnlySpan<float> ys, int n)
    {
        int c = 0;
        for (int i = 1; i < n; i++) if (ys[i - 1] < 0 && ys[i] >= 0 && xs[i] > 0 && xs[i] <= 1) c++;
        return c;
    }

    private static IEnumerable<(bool, string)> History()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        int lfo = e.ModulatorAdd(t, Lfo);
        e.ModulatorSet(t, lfo, 1, 0f);    // free running
        e.ModulatorSet(t, lfo, 2, 1f);    // 1 Hz
        int sc = e.ModulatorAdd(t, Scope);
        e.ModulatorSet(t, sc, 10, lfo);   // input = the LFO
        var v = new float[ScopeTrace.HistoryLength]; var dt = new float[ScopeTrace.HistoryLength];
        yield return (e.ModulatorScopeTimed(t, sc, v, dt) == 0, "scope: a new Scope has no history yet");

        var buf = new float[Block * 2];
        e.Seek(0); e.Play();
        const int blocks = 600;   // ~3 s
        for (int b = 0; b < blocks; b++) e.RenderOffline(buf, Block);
        e.StopTransport();
        int n = e.ModulatorScopeTimed(t, sc, v, dt);
        double sr = n > 0 && dt[0] > 0 ? Math.Round(Block / dt[0]) : 0;   // the render rate
        double sum = 0; float lo = 1, hi = -1; bool steps = true;
        for (int i = 0; i < n; i++)
        {
            sum += dt[i]; lo = Math.Min(lo, v[i]); hi = Math.Max(hi, v[i]);
            if (Math.Abs(dt[i] - Block / sr) > 1e-6) steps = false;
        }
        yield return (n == blocks && steps && (sr == 44100 || sr == 48000) && Math.Abs(sum - blocks * Block / sr) < 1e-3,
            $"scope: one entry per block with its time step ({n} entries for {blocks} blocks, {sum:F3} s at {sr:0} Hz, steps {(steps ? "= block" : "off")})");
        yield return (lo < -0.98f && hi > 0.98f, $"scope: the history is the LFO's swing ({lo:F2} … {hi:F2})");

        var v10 = new float[10]; var dt10 = new float[10];
        int m = e.ModulatorScopeTimed(t, sc, v10, dt10);
        yield return (m == 10 && v10.AsSpan().SequenceEqual(v.AsSpan(n - 10, 10)),
            "scope: a short read returns the newest entries, oldest → newest");

        // Past the ring's length the reader still gets the newest values, within the safe margin.
        for (int b = 0; b < ScopeTrace.HistoryLength; b++) e.RenderOffline(buf, Block);
        int full = e.ModulatorScopeTimed(t, sc, v, dt);
        float live = e.ModulatorValue(t, lfo);
        yield return (full > ScopeTrace.HistoryLength - 300 && full < ScopeTrace.HistoryLength,
            $"scope: a full ring reads {full} of {ScopeTrace.HistoryLength} (a margin the audio thread may be writing)");
        yield return (Math.Abs(v[full - 1] - e.ModulatorValue(t, sc)) < 0.2f || Math.Abs(v[full - 1] - live) < 0.2f,
            "scope: the last entry is the current value");

        var dummy = new float[16];
        yield return (e.ModulatorScopeTimed(t, lfo, dummy, dummy) == 0 && e.ModulatorScopeTimed(t, 999, dummy, dummy) == 0,
            "scope: an LFO or a missing id has no history");
        yield return (e.ModulatorScope(t, sc, dummy) == 16, "scope: the untimed read still works (newest values)");

        // The history survives a republish of the track (another node added).
        int before = e.ModulatorScopeTimed(t, sc, v, dt);
        e.ModulatorAdd(t, Lfo);
        yield return (e.ModulatorScopeTimed(t, sc, v, dt) == before, "scope: adding another node keeps the trace");
    }

    private static IEnumerable<(bool, string)> Settings()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        int sc = e.ModulatorAdd(t, Scope);
        float F(int f) => e.ModulatorGet(t, sc, f);
        yield return (F(13) == 1000f && F(14) == 0f && F(15) == 4f && F(16) == 1f && F(17) == 0f && F(18) == 76f,
            "scope: defaults are a 1 s window, free, 4 beats, 1×, no trigger, 76 px");
        e.ModulatorSet(t, sc, 13, 20000f); bool msHi = F(13) == 8000f;
        e.ModulatorSet(t, sc, 13, 1f); bool msLo = F(13) == 50f;
        e.ModulatorSet(t, sc, 16, 3f); bool z4 = F(16) == 4f;
        e.ModulatorSet(t, sc, 16, 100f); bool z8 = F(16) == 8f;
        e.ModulatorSet(t, sc, 18, 500f); bool hHi = F(18) == 320f;
        e.ModulatorSet(t, sc, 15, 64f); bool bHi = F(15) == 32f;
        yield return (msHi && msLo && z4 && z8 && hHi && bHi, "scope: the window, zoom, beats and height clamp (8 s / 50 ms, 4× / 8×, 32 beats, 320 px)");

        e.ModulatorSet(t, sc, 13, 2500f); e.ModulatorSet(t, sc, 14, 1f); e.ModulatorSet(t, sc, 15, 2f);
        e.ModulatorSet(t, sc, 16, 2f); e.ModulatorSet(t, sc, 17, 1f); e.ModulatorSet(t, sc, 18, 180f);
        var w = new List<string>();
        var doc = ProjectService.Capture(e, new TransportState(120, 1, false, false), w);
        string dir = Path.Combine(Path.GetTempPath(), "nota-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            ProjectService.Save(doc, dir, e);
            using (var e2 = new NotaEngine())
            {
                ProjectService.Apply(ProjectService.Load(dir), e2, dir);
                e2.TryGetTrackInfo(0, out var ti);
                int id = e2.ModulatorIdAt(ti.Id, 0);
                float G(int f) => e2.ModulatorGet(ti.Id, id, f);
                yield return (G(13) == 2500f && G(14) == 1f && G(15) == 2f && G(16) == 2f && G(17) == 1f && G(18) == 180f,
                    "scope: the settings survive save and reopen");
            }
            // An older project has no Scope fields: it opens with the defaults.
            string manifest = Path.Combine(dir, ProjectService.ManifestName);
            string json = File.ReadAllText(manifest);
            string stripped = Regex.Replace(json, ",\\s*\"Scope(WindowMs|Sync|WindowBeats|Zoom|Trigger|Height)\"\\s*:\\s*[^,}\\]\\s]+", "", RegexOptions.IgnoreCase);
            File.WriteAllText(manifest, stripped);
            using (var e3 = new NotaEngine())
            {
                ProjectService.Apply(ProjectService.Load(dir), e3, dir);
                e3.TryGetTrackInfo(0, out var ti);
                int id = e3.ModulatorIdAt(ti.Id, 0);
                float G(int f) => e3.ModulatorGet(ti.Id, id, f);
                yield return (stripped != json && e3.ModulatorKind(ti.Id, id) == Scope
                              && G(13) == 1000f && G(14) == 0f && G(16) == 1f && G(17) == 0f && G(18) == 76f,
                    "scope: an older project opens with the default settings");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static IEnumerable<(bool, string)> Trace()
    {
        const int N = ScopeTrace.HistoryLength;
        const float step = 0.00267f;   // ~128 frames at 48 kHz
        var v = new float[N]; var dt = new float[N];
        var xs = new double[N]; var ys = new float[N];
        void Fill(int shift)
        {
            for (int i = 0; i < N; i++) { dt[i] = step; v[i] = (float)Math.Sin(2 * Math.PI * 1.0 * (i + shift) * step + 0.3); }
        }

        Fill(0);
        int n = ScopeTrace.Layout(v, dt, 2.0, false, xs, ys, out bool trig);
        int cycles = Rising(xs, ys, n);
        yield return (!trig && cycles == 2 && xs[0] <= 0 && Math.Abs(xs[n - 1] - 1) < 1e-9,
            $"trace: a 2 s window of a 1 Hz LFO shows two cycles, newest at the right ({cycles} rising crossings)");
        yield return (Math.Abs(ScopeTrace.WindowSeconds(true, 1000, 4, 120) - 2.0) < 1e-9 && Math.Abs(ScopeTrace.WindowSeconds(false, 250, 4, 120) - 0.25) < 1e-9,
            "trace: 4 beats at 120 BPM is 2 s; 250 ms is 0.25 s");

        // Trigger: across frames (the history moving on by 1..40 blocks) the trace starts at the
        // rising crossing each time, within a fraction of a block.
        double worst = 0; bool allTrig = true;
        for (int s = 1; s <= 40; s++)
        {
            Fill(s * 7);
            n = ScopeTrace.Layout(v, dt, 1.5, true, xs, ys, out trig);
            allTrig &= trig;
            // Where the drawn line crosses 0 between its first two points.
            double x0 = xs[0] + (xs[1] - xs[0]) * (-ys[0] / (double)(ys[1] - ys[0]));
            worst = Math.Max(worst, Math.Abs(x0) * 1.5);
        }
        yield return (allTrig && worst < step * 0.05, $"trace: the trigger holds a periodic shape still (start within {worst * 1000:F3} ms of the crossing)");

        var flat = Enumerable.Repeat(0.5f, 500).ToArray(); var fdt = Enumerable.Repeat(step, 500).ToArray();
        n = ScopeTrace.Layout(flat, fdt, 0.5, true, xs, ys, out trig);
        yield return (!trig && n > 2 && Math.Abs(xs[n - 1] - 1) < 1e-9, "trace: with no crossing the trigger runs free");

        n = ScopeTrace.Layout(flat.AsSpan(0, 100), fdt.AsSpan(0, 100), 8.0, false, xs, ys, out _);
        yield return (n == 100 && xs[0] > 0.9 && Math.Abs(xs[n - 1] - 1) < 1e-9, "trace: a short history draws right-aligned");

        Fill(0);
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 200; k++) ScopeTrace.Layout(v, dt, 8.0, true, xs, ys, out _);
        double ms = sw.Elapsed.TotalMilliseconds / 200;
        yield return (ms < 1.0, $"trace: laying out a full 8 s history takes {ms:F3} ms");

        yield return (ScopeTrace.BeatsLabel(0.25) == "1/16" && ScopeTrace.BeatsLabel(1) == "1/4" && ScopeTrace.BeatsLabel(4) == "1 bar"
                      && ScopeTrace.BeatsLabel(32) == "8 bars" && ScopeTrace.MsLabel(1500) == "1.5\u2009s" && ScopeTrace.MsLabel(50) == "50\u2009ms",
            "trace: window labels (1/16 … 8 bars, 50 ms … 1.5 s)");
    }
}
