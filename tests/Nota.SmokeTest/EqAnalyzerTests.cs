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
        var mono = PinkSamples(n);
        var x = new float[n * 2];
        for (int i = 0; i < n; i++) x[2 * i] = x[2 * i + 1] = mono[i];
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, n);
        tmp.Add(path);
        return path;
    }

    // Seeded pink noise (Paul Kellet's filter), mono.
    private static float[] PinkSamples(int n)
    {
        var rnd = new Random(46);
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        var x = new float[n];
        for (int i = 0; i < n; i++)
        {
            double wv = rnd.NextDouble() * 2 - 1;
            b0 = 0.99886 * b0 + wv * 0.0555179; b1 = 0.99332 * b1 + wv * 0.0750759;
            b2 = 0.96900 * b2 + wv * 0.1538520; b3 = 0.86650 * b3 + wv * 0.3104856;
            b4 = 0.55000 * b4 + wv * 0.5329522; b5 = -0.7616 * b5 - wv * 0.0168980;
            double p = b0 + b1 + b2 + b3 + b4 + b5 + b6 + wv * 0.5362;
            b6 = wv * 0.115926;
            x[i] = (float)(p * 0.025);
        }
        return x;
    }

    // A seeded drum loop at 120 BPM: a kick on the beat (a falling 55 Hz sine), a snare on 2 and
    // 4 (noise and a 180 Hz body) and a closed hat on the eighths (bright noise), like the
    // Drum Rack loop the Peak line was first seen spiking on.
    private static string DrumLoop(List<string> tmp, double seconds = 5.0)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-eqa-drums-{Guid.NewGuid():N}.wav");
        int n = (int)(Rate * seconds), eighth = Rate / 4;
        var rnd = new Random(265);
        var x = new float[n * 2];
        double hp = 0, prev = 0;
        for (int i = 0; i < n; i++)
        {
            int step = i / eighth, at = i % eighth;
            double ts = at / (double)Rate, v = 0;
            double noise = rnd.NextDouble() * 2 - 1;
            hp = 0.85 * (hp + noise - prev); prev = noise;   // one-pole high-pass: the hat's brightness
            if (step % 2 == 0) v += 0.7 * Math.Sin(2 * Math.PI * (55 * ts + 40 * (1 - Math.Exp(-ts * 30)) / 30)) * Math.Exp(-ts * 9);
            if (step % 4 == 2) v += (0.35 * noise + 0.3 * Math.Sin(2 * Math.PI * 180 * ts)) * Math.Exp(-ts * 22);
            v += 0.25 * hp * Math.Exp(-ts * 70);
            x[2 * i] = x[2 * i + 1] = (float)v;
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
        // Flat: through the device, averaged over frames (timing doesn't matter for a mean).
        using (var r = new Rig(PinkNoise(tmp)))
        {
            r.Reset();
            r.Feed(8192);
            var sum = new double[Points];
            int frames = 0;
            for (; frames < 30; frames++)
            {
                r.Feed(1470);
                var s = r.Read();
                for (int i = 0; i < Points; i++) sum[i] += s[PostAt + i];
            }
            var avg = Band().Select(i => sum[i] / frames).ToList();
            double mid = avg.OrderBy(v => v).ElementAt(avg.Count / 2);
            double lo = avg.Min() - mid, hi = avg.Max() - mid;
            yield return (lo > -3 && hi < 3, $"pink noise reads flat from 100 Hz to 15 kHz ({lo:F1} / +{hi:F1} dB around {mid:F1} dB)");
        }
        // Steady (SC-003): the ballistics run on the display clock, so measure them on a fixed one,
        // 1470 new samples every 1/30 s, rather than on a CI runner's scheduling.
        {
            var curves = NotaEngine.SpectrumAnalyzeOffline(PinkSamples(Rate * 3), Rate, 1470, 1470.0 / Rate, 80);
            const int Settle = 10;   // the curve rises from the floor first
            double change = 0; int pairs = 0;
            for (int f = Settle + 1; f < curves.Length; f++, pairs++)
                foreach (int i in Band()) change += Math.Abs(curves[f][i] - curves[f - 1][i]);
            double meanChange = change / (Math.Max(1, pairs) * Band().Count);
            yield return (pairs >= 30 && meanChange < 1.5,
                $"pink noise holds steady (mean frame-to-frame change {meanChange:F2} dB over {pairs} display frames)");
        }
    }

    private static List<int> Band() => Enumerable.Range(0, Points).Where(i => PointHz(i) >= 100 && PointHz(i) <= 15000).ToList();

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

        // A large buffer (2048 frames ≈ 46 ms) with the display reading every ~10 ms: most reads
        // fall between two callbacks, which is not silence, so a steady tone doesn't dip. The tone
        // outlasts the loop: at most 8 s of 46 ms feeds (≈ 8.2 s of audio).
        using (var r = new Rig(Sine(tmp, 1000, seconds: 10.0)))
        {
            r.Reset();
            r.Feed(8192);
            var s = r.Read();
            int pk = Argmax(s, PostAt);
            float lo = float.MaxValue, hi = float.MinValue;
            var sw = Stopwatch.StartNew();
            long nextFeed = 0, lastFeed = 0, feedGap = 0;
            int counted = 0;
            // Only reads while the "callbacks" keep their period count: a stalled CI runner that
            // misses a feed by a whole period really has stopped the input for that long.
            while (sw.ElapsedMilliseconds < 8000 && counted < 60)
            {
                long now = sw.ElapsedMilliseconds;
                if (now >= nextFeed) { r.Feed(2048); feedGap = now - lastFeed; lastFeed = now; nextFeed += 46; }
                s = r.Read(10);
                if (feedGap > 70 || sw.ElapsedMilliseconds - lastFeed > 70) continue;
                lo = Math.Min(lo, s[PostAt + pk]);
                hi = Math.Max(hi, s[PostAt + pk]);
                counted++;
            }
            yield return (counted >= 20 && hi - lo < 1.0f,
                $"a steady tone in 2048-frame callbacks holds still between them ({hi - lo:F2} dB swing over {counted} reads)");
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
            // Read at display cadence: the ballistics run on wall-clock time and a read after a
            // gap over 1.2 s snaps everything to the floor, which a slow runner's long sleep can hit.
            var sw = Stopwatch.StartNew();
            float curve1 = float.NaN, peak1 = float.NaN, peak2 = float.NaN;
            while (sw.ElapsedMilliseconds < 2500)
            {
                Thread.Sleep(33);
                s = r.Read();
                if (float.IsNaN(curve1) && sw.ElapsedMilliseconds >= 900) { curve1 = s[PostAt + pk]; peak1 = s[PeakAt + pk]; }
                peak2 = s[PeakAt + pk];
            }
            yield return (curve1 < top - 20 && Math.Abs(peak1 - top) < 0.5 && peak2 < top - 10,
                $"Peak holds the top ({top:F1} → {peak1:F1} dB at 0.9 s while the curve is at {curve1:F1}) and then falls ({peak2:F1} dB at 2.5 s)");
            // Read at the display's cadence (a gap over 1.2 s restarts the analysis): once the curve
            // has fallen to the floor, the held peak is still there, so the card keeps drawing it.
            r.Reset();
            r.Feed(8192);
            s = r.Read();
            var gone = Stopwatch.StartNew();
            while (gone.ElapsedMilliseconds < 3500 && s[PostAt + pk] > -149.5f) s = r.Read(33);
            yield return (s[Analysed] > 0.5f && s[PostAt + pk] <= -149.5f && s[PeakAt + pk] > -140,
                $"the held peak keeps showing after the curve has gone (analysed {s[Analysed]}, curve {s[PostAt + pk]:F0} dB, peak {s[PeakAt + pk]:F0} dB)");
            r.E.DeviceSetParam(r.T, r.D, AnaPeak, 0);
            r.Reset();
            r.Feed(8192);
            s = r.Read();
            Thread.Sleep(600);
            s = r.Read();
            yield return (Math.Abs(s[PeakAt + pk] - s[PostAt + pk]) < 0.01, "Peak off: the peak curve is the curve");
        }

        // On drums the Peak line stays a line: no single point drops into a notch or sticks out as
        // a spike against its neighbours (it did on the Mac, as points' holds ran out apart).
        using (var r = new Rig(DrumLoop(tmp)))
        {
            r.E.DeviceSetParam(r.T, r.D, AnaPeak, 1);
            r.Reset();
            double notch = 0, spike = 0, below = 0;
            for (int f = 0; f < 130; f++)
            {
                r.Feed(1470);
                var s = r.Read(30);
                if (f < 30) continue;
                for (int i = 1; i < Points - 1; i++)
                {
                    if (PointHz(i) < 40 || PointHz(i) > 16000) continue;
                    float p = s[PeakAt + i], a = s[PeakAt + i - 1], b = s[PeakAt + i + 1];
                    notch = Math.Max(notch, Math.Min(a, b) - p);
                    spike = Math.Max(spike, p - Math.Max(a, b));
                    below = Math.Max(below, s[PostAt + i] - p);
                }
            }
            yield return (notch < 3 && spike < 3 && below <= 0.01,
                $"on a drum loop the Peak line has no notches or spikes (deepest notch {notch:F1} dB, tallest spike {spike:F1} dB) and never dips under the curve ({below:F2})");
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
            // The median of 21 updates: one read the OS preempted on a busy machine isn't the cost.
            var took = new List<double>();
            for (int i = 0; i < 21; i++)
            {
                r.Feed(2048);
                Thread.Sleep(35);
                var sw = Stopwatch.StartNew();
                r.E.DeviceScope(r.T, r.D, sc, sc.Length);
                took.Add(sw.Elapsed.TotalMilliseconds);
            }
            took.Sort();
            double median = took[took.Count / 2];
            yield return (median < 1.0, $"an EQ-8 analysis update takes {median:F3} ms (median of 21, under 1 ms; slowest {took[^1]:F3})");
        }
    }

    private static IEnumerable<(bool, string)> ParamsAndSound(List<string> tmp)
    {
        string wav = Sine(tmp, 440);
        using var e = new NotaEngine();
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, 0.0);
        int d = e.AddBuiltinDevice(t, Eq8Kind);
        yield return (e.DeviceParamCount(t, d) >= 62 && e.DeviceGetParam(t, d, AnaRange) == 1f && e.DeviceGetParam(t, d, AnaPeak) == 0f
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
        bool saved = dev.Params.Length >= 62 && dev.Params[AnaRange] == 2f && dev.Params[AnaPeak] == 1f;
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
