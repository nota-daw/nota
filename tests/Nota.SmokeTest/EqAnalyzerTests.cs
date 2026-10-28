// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The EQ cards' spectrum analyzer (specs/046, #265): sines read at their level and frequency,
// pink noise reads flat, a steady signal holds still, the curve rises at once and falls 20 dB in
// about 300 ms, the peak holds, silence falls away, Off analyses nothing, and the appended Range
// and Peak params default on an older project without touching the sound. The analysis runs on
// the reading thread at up to 30 updates a second against the wall clock, so each read waits a
// frame. Also runnable alone: `--eq-analyzer`.

using System.Diagnostics;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class EqAnalyzerTests
{
    private const int Eq8Kind = 0, Eq3Kind = 16;
    private const int Points = 256, Tele = 16, PreAt = Tele, PostAt = Tele + Points, PeakAt = Tele + 2 * Points;
    private const int Scope8 = Tele + 3 * Points, Scope3 = Tele + Points;
    private const int Analyzer = 59, AnaRange = 60, AnaPeak = 61, Analysed = 5;
    private const int Rate = 44100;
    private const double SineDb = -12.0;

    private static double PointHz(int i) => 20.0 * Math.Pow(1000.0, i / (double)(Points - 1));

    public static IEnumerable<(bool, string)> Run()
    {
        var tmp = new List<string>();
        try
        {
            foreach (var r in Sines(tmp)) yield return r;
            foreach (var r in Pink(tmp)) yield return r;
            foreach (var r in Ballistics(tmp)) yield return r;
            foreach (var r in ParamsAndSound(tmp)) yield return r;
        }
        finally { foreach (var f in tmp) try { File.Delete(f); } catch (IOException) { } }
    }

    // ---- signals ----------------------------------------------------------------------------

    private static string Sine(List<string> tmp, double hz, double seconds = 2.0)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-eqa-{hz:0}-{Guid.NewGuid():N}.wav");
        int n = (int)(Rate * seconds);
        double a = Math.Pow(10, SineDb / 20);
        var x = new float[n * 2];
        for (int i = 0; i < n; i++) x[2 * i] = x[2 * i + 1] = (float)(a * Math.Sin(2 * Math.PI * hz * i / Rate));
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, n);
        tmp.Add(path);
        return path;
    }

    // Paul Kellet's pink filter over seeded white noise, about −20 dBFS RMS.
    private static string PinkNoise(List<string> tmp, double seconds = 4.0)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-eqa-pink-{Guid.NewGuid():N}.wav");
        int n = (int)(Rate * seconds);
        var rnd = new Random(46);
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        var x = new float[n * 2];
        for (int i = 0; i < n; i++)
        {
            double wv = rnd.NextDouble() * 2 - 1;
            b0 = 0.99886 * b0 + wv * 0.0555179; b1 = 0.99332 * b1 + wv * 0.0750759;
            b2 = 0.96900 * b2 + wv * 0.1538520; b3 = 0.86650 * b3 + wv * 0.3104856;
            b4 = 0.55000 * b4 + wv * 0.5329522; b5 = -0.7616 * b5 - wv * 0.0168980;
            double p = b0 + b1 + b2 + b3 + b4 + b5 + b6 + wv * 0.5362;
            b6 = wv * 0.115926;
            x[2 * i] = x[2 * i + 1] = (float)(p * 0.025);
        }
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, n);
        tmp.Add(path);
        return path;
    }

    private sealed class Rig : IDisposable
    {
        public readonly NotaEngine E = new();
        public readonly int T, D;
        private readonly float[] _sc;
        private bool _playing;
        public Rig(string wav, int kind = Eq8Kind)
        {
            E.SetBpm(120);
            T = E.AddAudioTrack();
            E.AddAudioClip(T, wav, 0.0);
            D = E.AddBuiltinDevice(T, kind);
            _sc = new float[kind == Eq8Kind ? Scope8 : Scope3];
        }
        /// <summary>Plays the next <paramref name="frames"/> of the clip through the device, carrying
        /// on from the last call (no seek, so no start ramp in the analysed audio).</summary>
        public void Feed(int frames)
        {
            var b = new float[frames * 2];
            if (!_playing) { E.Seek(0); E.Play(); _playing = true; }
            E.RenderOffline(b, frames);
        }
        /// <summary>Waits a display frame, then reads the telemetry (which runs the analysis).</summary>
        public float[] Read(int waitMs = 35)
        {
            Thread.Sleep(waitMs);
            E.DeviceScope(T, D, _sc, _sc.Length);
            return _sc;
        }
        public void Reset()
        {
            E.DeviceAction(T, D, 0, 0, 0);
            if (_playing) E.StopTransport();
            _playing = false;
        }
        public void Dispose() => E.Dispose();
    }

    private static int Argmax(float[] s, int at)
    {
        int best = 0;
        for (int i = 1; i < Points; i++) if (s[at + i] > s[at + best]) best = i;
        return best;
    }

    // ---- tests ------------------------------------------------------------------------------

    private static IEnumerable<(bool, string)> Sines(List<string> tmp)
    {
        var bad = new List<string>();
        var lines = new List<string>();
        foreach (double hz in new[] { 50.0, 100.0, 250.0, 1000.0, 4000.0, 10000.0, 15000.0 })
        {
            // Within one display point, or one FFT bin where a bin is wider (the low end).
            double tol = Math.Max(1.5 / 25.5, Math.Log(1 + (Rate / 4096.0) / hz, 2));
            using var r = new Rig(Sine(tmp, hz));
            r.Reset();
            r.Feed(8192);
            var s = r.Read();
            int pk = Argmax(s, PostAt);
            double at = PointHz(pk), lvl = s[PostAt + pk];
            lines.Add($"{hz:0} Hz → {at:0} Hz {lvl:F1} dB");
            if (Math.Abs(Math.Log(at / hz, 2)) > tol || Math.Abs(lvl - SineDb) > 3) bad.Add($"{hz:0} Hz");
        }
        yield return (bad.Count == 0, $"a −12 dBFS sine reads at its frequency and level, 50 Hz–15 kHz ({string.Join(", ", lines)})");

        // Steady sine: the peak point holds still frame to frame.
        using (var r = new Rig(Sine(tmp, 1000)))
        {
            r.Reset();
            r.Feed(8192);
            var prev = (float[])r.Read().Clone();
            int pk = Argmax(prev, PostAt);
            double worst = 0;
            for (int f = 0; f < 8; f++)
            {
                r.Feed(1470);   // one 30 Hz frame of audio
                var s = r.Read();
                worst = Math.Max(worst, Math.Abs(s[PostAt + pk] - prev[PostAt + pk]));
                Array.Copy(s, prev, s.Length);
            }
            yield return (worst < 0.5, $"a steady sine holds still (largest frame-to-frame change {worst:F2} dB at its peak)");
            // A flat EQ: the output reads as the input. Before the fix the post ring ran on from the
            // pre pass, so half of each ring was stale and the two read apart.
            double gap = Math.Abs(prev[PostAt + pk] - prev[PreAt + pk]);
            yield return (gap < 0.1, $"through a flat EQ-8 the output spectrum matches the input ({gap:F3} dB apart)");
        }

        // EQ-3 reads the same as EQ-8 on the same signal.
        using (var a = new Rig(Sine(tmp, 1000)))
        using (var b = new Rig(Sine(tmp, 1000), Eq3Kind))
        {
            a.Reset(); b.Reset();
            a.Feed(8192); b.Feed(8192);
            var s8 = a.Read(); var s3 = b.Read(0);
            int p8 = Argmax(s8, PostAt), p3 = Argmax(s3, Tele);
            double d = Math.Abs(s8[PostAt + p8] - s3[Tele + p3]);
            yield return (p8 == p3 && d < 1, $"EQ-3 uses the same analyzer: the 1 kHz sine at point {p3} vs {p8}, {d:F2} dB apart");
        }
    }

    private static IEnumerable<(bool, string)> Pink(List<string> tmp)
    {
        using var r = new Rig(PinkNoise(tmp));
        r.Reset();
        r.Feed(8192);
        var sum = new double[Points];
        var prev = (float[])r.Read().Clone();
        double change = 0; int frames = 0;
        for (int f = 0; f < 30; f++)
        {
            r.Feed(1470);
            var s = r.Read();
            for (int i = 0; i < Points; i++)
            {
                sum[i] += s[PostAt + i];
                if (PointHz(i) >= 100 && PointHz(i) <= 15000) change += Math.Abs(s[PostAt + i] - prev[PostAt + i]);
            }
            frames++;
            Array.Copy(s, prev, s.Length);
        }
        var band = Enumerable.Range(0, Points).Where(i => PointHz(i) >= 100 && PointHz(i) <= 15000).ToList();
        var avg = band.Select(i => sum[i] / frames).ToList();
        double mid = avg.OrderBy(v => v).ElementAt(avg.Count / 2);
        double lo = avg.Min() - mid, hi = avg.Max() - mid;
        yield return (lo > -3 && hi < 3, $"pink noise reads flat from 100 Hz to 15 kHz ({lo:F1} / +{hi:F1} dB around {mid:F1} dB)");
        double meanChange = change / (frames * band.Count);
        yield return (meanChange < 1.5, $"pink noise holds steady (mean frame-to-frame change {meanChange:F2} dB)");
    }

    private static IEnumerable<(bool, string)> Ballistics(List<string> tmp)
    {
        // Rise at once, then fall 20 dB in ~300 ms once the input stops.
        using (var r = new Rig(Sine(tmp, 1000)))
        {
            r.Reset();
            r.Feed(8192);
            var s = r.Read();
            int pk = Argmax(s, PostAt);
            float top = s[PostAt + pk];
            var sw = Stopwatch.StartNew();
            double dropAt = -1;
            while (sw.ElapsedMilliseconds < 1500)
            {
                s = r.Read(10);
                if (top - s[PostAt + pk] >= 20) { dropAt = sw.Elapsed.TotalMilliseconds; break; }
            }
            yield return (dropAt >= 230 && dropAt <= 420, $"after the input stops, the curve falls 20 dB in {dropAt:0} ms (target 300)");
            Thread.Sleep(2600);
            s = r.Read();
            yield return (s[Analysed] < 0.5f && s[PostAt + pk] < -120, $"silence falls away instead of freezing (analysed {s[Analysed]}, {s[PostAt + pk]:F0} dB)");
        }

        // Peak hold: the peak stays ~1.5 s after the curve falls, then falls too.
        using (var r = new Rig(Sine(tmp, 1000)))
        {
            r.E.DeviceSetParam(r.T, r.D, AnaPeak, 1);
            r.Reset();
            r.Feed(8192);
            var s = r.Read();
            int pk = Argmax(s, PostAt);
            float top = s[PostAt + pk];
            Thread.Sleep(900);
            s = r.Read();
            float curve1 = s[PostAt + pk], peak1 = s[PeakAt + pk];
            Thread.Sleep(1600);
            s = r.Read();
            float peak2 = s[PeakAt + pk];
            yield return (curve1 < top - 20 && Math.Abs(peak1 - top) < 0.5 && peak2 < top - 10,
                $"Peak holds the top ({top:F1} → {peak1:F1} dB at 0.9 s while the curve is at {curve1:F1}) and then falls ({peak2:F1} dB at 2.5 s)");
            r.E.DeviceSetParam(r.T, r.D, AnaPeak, 0);
            r.Reset();
            r.Feed(8192);
            s = r.Read();
            Thread.Sleep(600);
            s = r.Read();
            yield return (Math.Abs(s[PeakAt + pk] - s[PostAt + pk]) < 0.01, "Peak off: the peak curve is the curve");
        }

        // Pre shows the input with its peak; Off analyses nothing.
        using (var r = new Rig(Sine(tmp, 1000)))
        {
            r.E.DeviceSetParam(r.T, r.D, Analyzer, 0);
            r.Reset();
            r.Feed(8192);
            var s = r.Read();
            int pk = Argmax(s, PreAt);
            yield return (s[Analysed] > 0.5f && Math.Abs(s[PreAt + pk] - SineDb) < 3 && s[PostAt + pk] < -140,
                $"Pre analyses the input alone ({s[PreAt + pk]:F1} dB in, {s[PostAt + pk]:F0} dB out)");
            r.E.DeviceSetParam(r.T, r.D, Analyzer, 2);
            r.Reset();
            r.Feed(8192);
            s = r.Read();
            yield return (s[Analysed] < 0.5f && s[PreAt + pk] < -140, "Off analyses nothing");
        }

        // One analysis update (two curves) is cheap.
        using (var r = new Rig(Sine(tmp, 1000)))
        {
            r.Reset();
            var sc = new float[Scope8];
            double worst = 0;
            for (int i = 0; i < 10; i++)
            {
                r.Feed(2048);
                Thread.Sleep(35);
                var sw = Stopwatch.StartNew();
                r.E.DeviceScope(r.T, r.D, sc, sc.Length);
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            yield return (worst < 1.0, $"an EQ-8 analysis update takes {worst:F3} ms at most (under 1 ms)");
        }
    }

    private static IEnumerable<(bool, string)> ParamsAndSound(List<string> tmp)
    {
        string wav = Sine(tmp, 440);
        using var e = new NotaEngine();
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, 0.0);
        int d = e.AddBuiltinDevice(t, Eq8Kind);
        yield return (e.DeviceParamCount(t, d) == 62 && e.DeviceGetParam(t, d, AnaRange) == 1f && e.DeviceGetParam(t, d, AnaPeak) == 0f
                      && e.DeviceParamName(t, d, AnaRange) == "Analyzer Range" && e.DeviceParamName(t, d, AnaPeak) == "Analyzer Peak",
            "EQ-8 appends Analyzer Range (default 90 dB) and Analyzer Peak (default off)");

        float[] Render()
        {
            var b = new float[8192 * 2];
            e.Seek(0); e.Play(); e.RenderOffline(b, 8192); e.StopTransport();
            return b;
        }
        e.DeviceSetParam(t, d, 3, 6f);   // band 1 shelf +6 dB, so the EQ does something
        Render(); Render();              // let the gain glide settle
        var before = Render();
        bool repeatable = before.AsSpan().SequenceEqual(Render());
        e.DeviceSetParam(t, d, AnaRange, 2f);
        e.DeviceSetParam(t, d, AnaPeak, 1f);
        var after = Render();
        yield return (repeatable && before.AsSpan().SequenceEqual(after), $"Range and Peak are display only: the sound is bit-identical (render repeatable: {repeatable})");

        var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
        var dev = doc.Tracks.SelectMany(tr => tr.Devices).First(x => x.BuiltinKind == Eq8Kind);
        bool saved = dev.Params.Length == 62 && dev.Params[AnaRange] == 2f && dev.Params[AnaPeak] == 1f;
        dev.Params = dev.Params[..60];   // as written before 0.58.1
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-eqa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var old = new NotaEngine();
            ProjectService.Apply(doc, old, dir);
            int ot = -1;
            for (int i = 0; i < old.TrackCount; i++)
                if (old.TryGetTrackInfo(i, out var ti) && !ti.IsInstrument && !ti.IsReturn && !ti.IsGroup) { ot = ti.Id; break; }
            int od = 0;
            yield return (saved && ot >= 0 && old.DeviceGetParam(ot, od, AnaRange) == 1f && old.DeviceGetParam(ot, od, AnaPeak) == 0f
                          && old.DeviceGetParam(ot, od, 3) == 6f,
                "Range and Peak are saved; an older project opens with the defaults and its bands unchanged");
        }
        finally { Directory.Delete(dir, true); }
    }
}
