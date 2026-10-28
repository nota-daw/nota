// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Lens tempo sync and key overlay (specs/050, #119): with Sync on, the scope's synced
// frame spans the song's beat grid (clicks land on their beat lines, this pass left of the
// playhead and the previous one right of it), a one-sample spike keeps its height at any span,
// a stopped transport rolls freely and a seek restarts the pass; a routed sidechain key gets
// its own trace (synced and triggered); and the audio is untouched. Also runnable alone:
// `--lens-sync`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LensSyncTests
{
    private const int LensKind = 22, Rate = 44100, Spb = Rate / 2;   // 120 BPM
    private const int RateP = 36, SyncP = 37, SpanP = 38, KeyShowP = 39, KeyGainP = 40, LayoutP = 41, TrigLevelP = 19;
    private const int M_KeyLive = 20, kScope = 21, L_Trace = 2, L_KeyTrace = 5, L_Sync = 6;
    private const int Head = 8, Cols = 1024, Col = 5;
    private const int S_Mode = 0, S_Playhead = 1, S_Bpm = 2, S_BeatsPerBar = 3, S_SpanBeats = 4, S_Start = 5, S_KeyLive = 6;

    public static IEnumerable<(bool, string)> Run()
    {
        var tmp = new List<string>();
        try
        {
            string beats = Clicks(tmp, 0.0), offBeats = Clicks(tmp, 0.5);
            foreach (var r in Params()) yield return r;
            foreach (var r in Grid(beats)) yield return r;
            foreach (var r in FreeAndSeek(beats)) yield return r;
            foreach (var r in Key(beats, offBeats)) yield return r;
            foreach (var r in Sound(beats, offBeats)) yield return r;
        }
        finally { foreach (var f in tmp) try { File.Delete(f); } catch (IOException) { } }
    }

    // 24 beats of one-sample 0.8 clicks, one per beat, `offset` beats late.
    private static string Clicks(List<string> tmp, double offset)
    {
        string path = Path.Combine(Path.GetTempPath(), "nota_lens_sync_" + Guid.NewGuid().ToString("N") + ".wav");
        tmp.Add(path);
        var buf = new float[24 * Spb];
        for (int b = 0; b < 24; b++) { int i = (int)Math.Round((b + offset) * Spb); if (i < buf.Length) buf[i] = 0.8f; }
        using var w = new Nota.Infrastructure.WavWriter(path, Rate, 1, WavBitDepth.Float32);
        w.WriteFrames(buf, buf.Length);
        return path;
    }

    // A Lens on a track playing `clip`; with `key`, a key track playing it comes first, so the
    // engine renders it before the Lens track and hands Lens the same block.
    private static (NotaEngine E, int T, int D) NewLens(string clip) => NewLens(clip, null, out _);
    private static (NotaEngine E, int T, int D) NewLens(string clip, string? key, out int keyTrack)
    {
        var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        keyTrack = -1;
        if (key != null) { keyTrack = e.AddAudioTrack(); e.AddAudioClip(keyTrack, key, startBeat: 0.0); }
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, clip, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, LensKind);
        e.DeviceSetParam(t, d, RateP, 1f);   // 60 fps: the shortest analysis interval
        return (e, t, d);
    }

    private static float SpanV(int step) => step / 6f;   // 0 = 1/4 beat … 4 = 1 bar … 6 = 4 bars

    // Play from `from` to `to` (beats), in 512-frame blocks.
    private static void Play(NotaEngine e, double from, double to, bool seek = true)
    {
        if (seek) { e.Seek(from); e.Play(); }
        var buf = new float[512 * 2];
        long n = (long)Math.Round((to - from) * Spb);
        while (n > 0) { int k = (int)Math.Min(512, n); e.RenderOffline(buf, k); n -= k; }
    }

    private static float[] Frame(IAudioEngine e, int t, int d)
    {
        System.Threading.Thread.Sleep(40);   // let the rate limiter open
        var f = new float[Head + Cols * Col];
        e.DeviceLayerWave(t, d, L_Sync, f, f.Length);
        return f;
    }

    // Centres of the runs of columns of one age whose max (main: 1, key: 3) passes 0.4.
    private static List<double> Peaks(float[] f, int age, int max = 1)
    {
        var res = new List<double>();
        int start = -1;
        for (int c = 0; c <= Cols; c++)
        {
            bool hit = c < Cols && (int)f[Head + c * Col + 4] == age && !float.IsNaN(f[Head + c * Col + 4]) && f[Head + c * Col + max] > 0.4f;
            if (hit && start < 0) start = c;
            if (!hit && start >= 0) { res.Add((start + c - 1) / 2.0); start = -1; }
        }
        return res;
    }

    private static bool Near(List<double> got, double[] want, double tol)
        => got.Count == want.Length && got.Zip(want).All(p => Math.Abs(p.First - p.Second) <= tol);

    private static string Show(List<double> v) => string.Join(", ", v.Select(x => x.ToString("0")));

    private static IEnumerable<(bool, string)> Params()
    {
        var (e, t, d) = NewLens(Clicks(new List<string>(), 0));
        using var _ = e;
        string[] names = { "Scope Sync", "Scope Span", "Scope Key", "Scope Key Gain", "Scope Layout" };
        float[] defs = { 0f, 4f / 6f, 1f, 0.5f, 0f };
        bool ok = e.DeviceParamCount(t, d) == 42;
        for (int i = 0; i < 5; i++)
            ok &= e.DeviceParamName(t, d, SyncP + i) == names[i] && Math.Abs(e.DeviceGetParam(t, d, SyncP + i) - defs[i]) < 1e-5;
        yield return (ok && e.DeviceAcceptsSidechain(t, d),
            "Lens appends Sync (off), Span (1 bar), Key (shown), Key Gain (0 dB) and Layout (Overlay), and takes a sidechain key");
    }

    private static IEnumerable<(bool, string)> Grid(string beats)
    {
        var (e, t, d) = NewLens(beats);
        using var _ = e;
        e.DeviceSetParam(t, d, SyncP, 1f);
        e.DeviceSetParam(t, d, SpanP, SpanV(4));
        Play(e, 0, 10);   // two and a half bars
        var f = Frame(e, t, d);
        yield return (f[S_Mode] == 1f && Math.Abs(f[S_Bpm] - 120) < 0.1 && f[S_BeatsPerBar] == 4 && f[S_SpanBeats] == 4 && f[S_Start] == 8
                      && Math.Abs(f[S_Playhead] - 0.5) < 0.01,
            $"Sync at 1 bar, beat 10: the window is bar 3 (beat {f[S_Start]}), playhead {f[S_Playhead]:0.000}, {f[S_Bpm]:0.#} BPM");
        var now = Peaks(f, 0);
        // The previous pass ends where this one starts: the click on that bar line can show at
        // both edges (one history bin holds it), so the right edge is left out.
        var before = Peaks(f, 1).Where(c => c < Cols - 2).ToList();
        yield return (Near(now, new[] { 0.0, 256 }, Cols * 0.01) && Near(before, new[] { 512.0, 768 }, Cols * 0.01),
            $"each click sits on its beat line (within 1 %): this pass at {Show(now)}, the previous one at {Show(before)}");

        // Four bars: drawn from the min/max history, the one-sample clicks keep their height.
        e.DeviceSetParam(t, d, SpanP, SpanV(6));
        Play(e, 0, 18);
        f = Frame(e, t, d);
        float top = 0;
        for (int c = 0; c < Cols; c++) if (!float.IsNaN(f[Head + c * Col + 4])) top = Math.Max(top, f[Head + c * Col + 1]);
        var all = Peaks(f, 0).Concat(Peaks(f, 1)).ToList();
        yield return (f[S_SpanBeats] == 16 && f[S_Start] == 16 && top > 0.79f && all.Count >= 15,
            $"at 4 bars a one-sample click still reaches {top:0.00} of 0.80, and every beat shows ({all.Count} marks)");

        // Half a beat: the short-span path reads the raw ring.
        e.DeviceSetParam(t, d, SpanP, SpanV(1));
        Play(e, 0, 10.25);
        f = Frame(e, t, d);
        now = Peaks(f, 0);
        yield return (f[S_SpanBeats] == 0.5f && f[S_Start] == 10 && Near(now, new[] { 0.0 }, Cols * 0.01),
            $"at 1/2 beat the click at beat 10 sits on the window's left edge ({Show(now)})");

    }

    private static IEnumerable<(bool, string)> FreeAndSeek(string beats)
    {
        // Stopped, only a track that still sounds runs its devices (an instrument, or a
        // monitored input); there the scope rolls freely.
        using (var ie = new NotaEngine())
        {
            ie.SetBpm(120); ie.SetTimeSignature(4, 4);
            int it = ie.AddInstrumentTrack();
            int idv = ie.AddBuiltinDevice(it, LensKind);
            ie.DeviceSetParam(it, idv, RateP, 1f);
            ie.DeviceSetParam(it, idv, SyncP, 1f);
            Play(ie, 0, 2);
            ie.StopTransport();
            Play(ie, 0, 0.5, seek: false);
            var fr = Frame(ie, it, idv);
            int filled = 0;
            for (int c = 0; c < Cols; c++) if (!float.IsNaN(fr[Head + c * Col + 4])) filled++;
            yield return (fr[S_Mode] == 0f && filled > Cols / 2,
                $"stopped, an instrument track's scope rolls freely over the last bar ({filled} of {Cols} columns)");
        }

        var (e, t, d) = NewLens(beats);
        using var _ = e;
        e.DeviceSetParam(t, d, SyncP, 1f);
        e.DeviceSetParam(t, d, SpanP, SpanV(4));

        // A seek while playing restarts the pass: nothing from before it under the new beat lines.
        Play(e, 0, 10);
        e.Seek(20.5);
        Play(e, 20.5, 21, seek: false);
        var f = Frame(e, t, d);
        int old = 0, early = 0, now = 0;
        for (int c = 0; c < Cols; c++)
        {
            float a = f[Head + c * Col + 4];
            if (float.IsNaN(a)) continue;
            if ((int)a == 1) old++;
            else if (c < 120) early++;
            else now++;
        }
        yield return (f[S_Mode] == 1f && f[S_Start] == 20 && old == 0 && early == 0 && now > 100,
            $"after a seek to beat 20.5 the pass restarts there ({now} new columns, {early + old} from before the seek)");
    }

    private static IEnumerable<(bool, string)> Key(string beats, string offBeats)
    {
        var (e, t, d) = NewLens(beats, offBeats, out int k);
        using var _ = e;
        e.SetDeviceSidechainSource(t, d, k);
        e.DeviceSetParam(t, d, SyncP, 1f);
        Play(e, 0, 10);
        var f = Frame(e, t, d);
        var sc = new float[kScope];
        int n = e.DeviceScope(t, d, sc, kScope);
        var keyNow = Peaks(f, 0, 3);
        var keyBefore = Peaks(f, 1, 3).Where(c => c < Cols - 2).ToList();
        var main = Peaks(f, 0);
        yield return (n == kScope && sc[M_KeyLive] == 1f && f[S_KeyLive] == 1f && e.DeviceSidechainSource(t, d) == k,
            "a routed key is reported as arriving");
        yield return (Near(keyNow, new[] { 128.0, 384 }, Cols * 0.01) && Near(keyBefore, new[] { 640.0, 896 }, Cols * 0.01)
                      && Near(main, new[] { 0.0, 256 }, Cols * 0.01),
            $"the key's off-beat clicks get their own trace ({Show(keyNow)} / {Show(keyBefore)}) beside the main ({Show(main)})");

        e.DeviceSetParam(t, d, KeyGainP, 1f);
        e.DeviceSetParam(t, d, LayoutP, 1f);
        var g = Frame(e, t, d);
        bool same = true;
        for (int i = Head; i < f.Length; i++) same &= f[i].Equals(g[i]);
        string txt = e.DeviceText(t, d, 2);
        yield return (same && txt.Contains("sync on") && txt.Contains("arriving") && txt.Contains("+24.0 dB") && txt.Contains("stacked"),
            $"Key Gain and Layout are display only, and the device text reports them: \"{txt[txt.IndexOf("sync", StringComparison.Ordinal)..]}\"");

        // Sync off: the key trace follows the main trace's trigger, sample for sample.
        string tone = Path.Combine(Path.GetTempPath(), "nota_lens_sync_tone_" + Guid.NewGuid().ToString("N") + ".wav");
        Nota.SmokeTest.WavWriter.WriteTones(tone, 4.0, new double[] { 220 }, 0.5, Rate);
        var (e2, t2, d2) = NewLens(tone, tone, out int k2);
        using var __ = e2;
        e2.SetDeviceSidechainSource(t2, d2, k2);
        e2.DeviceSetParam(t2, d2, TrigLevelP, 0.6f);
        Play(e2, 0, 160 * 512.0 / Spb);   // whole blocks, as a live callback gives
        System.Threading.Thread.Sleep(40);
        float[] mt = new float[512], kt = new float[512];
        int a = e2.DeviceLayerWave(t2, d2, L_Trace, mt, 512), b = e2.DeviceLayerWave(t2, d2, L_KeyTrace, kt, 512);
        // The key is the other track's post-FX audio, so its level can differ (pan law): compare
        // the shapes, the key scaled to the main's peak.
        double diff = 0, peak = 0, kpeak = 0;
        for (int i = 0; i < 512; i++) { peak = Math.Max(peak, Math.Abs(mt[i])); kpeak = Math.Max(kpeak, Math.Abs(kt[i])); }
        for (int i = 0; i < 512; i++) diff = Math.Max(diff, Math.Abs(mt[i] - kt[i] * (kpeak > 0 ? peak / kpeak : 0)));
        File.Delete(tone);
        yield return (a == 512 && b == 512 && peak > 0.1 && diff < 1e-3 * peak,
            $"Sync off, the key trace of the same audio lines up with the main trace (max diff {diff:0.0e+0}, peak {peak:0.00}, {a}/{b})");
    }

    private static IEnumerable<(bool, string)> Sound(string beats, string offBeats)
    {
        float[] Render(bool features, bool bypass)
        {
            var (e, t, d) = NewLens(beats);
            using var _ = e;
            int k = e.AddAudioTrack();
            e.AddAudioClip(k, offBeats, startBeat: 0.0);
            if (features) { e.SetDeviceSidechainSource(t, d, k); e.DeviceSetParam(t, d, SyncP, 1f); e.DeviceSetParam(t, d, LayoutP, 1f); }
            if (bypass) e.SetDeviceBypassed(t, d, true);
            e.Seek(0); e.Play();
            var all = new float[8 * 4096 * 2];
            var buf = new float[4096 * 2];
            for (int i = 0; i < 8; i++) { e.RenderOffline(buf, 4096); Array.Copy(buf, 0, all, i * buf.Length, buf.Length); }
            return all;
        }
        float[] plain = Render(false, true), on = Render(true, false);
        bool same = plain.Length == on.Length && plain.Zip(on).All(p => p.First.Equals(p.Second));
        yield return (same, "with Sync and a key, the audio through Lens is bit-identical to Lens bypassed");
    }
}
