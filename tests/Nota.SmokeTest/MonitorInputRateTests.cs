// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Live monitoring of a capture device at another rate than the engine's (#563). The capture
// device was opened at its own rate (a MacBook microphone at 48 kHz under a 44.1 kHz engine)
// and its frames were played one for one: the monitored input ran at the wrong speed, and
// the cushion logic trimmed the growing backlog (or re-primed an empty ring) several times a
// second — a click each time. Recording was clean: the take keeps the input's rate. Now the
// monitor resamples, steered by the queue so the two clocks can't drift apart.
// Also runnable alone: `dotnet Nota.SmokeTest.dll --monitor-input-rate`.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MonitorInputRateTests
{
    private const double Hz = 330.0;
    private const float Amp = 0.4f;
    private const int InBlock = 512, OutBlock = 512;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var (inRate, outRate) in new[] { (48000.0, 44100.0), (44100.0, 48000.0) })
        {
            var x = Monitor(inRate, inRate, outRate, seconds: 4);
            var (jump, rms, pitch) = Measure(x, (int)outRate, from: (int)outRate, to: x.Length / 2);
            yield return (rms > 0.1 && jump < 0.01,
                $"monitoring a {inRate / 1000:0.#} kHz input under a {outRate / 1000:0.#} kHz engine is continuous (rms {rms:F3}, worst jump {jump:G3})");
            yield return (Math.Abs(pitch - Hz) < 1.0,
                $"…and at the input's pitch ({pitch:F1} Hz, want {Hz})");
        }

        // The capture clock runs 200 ppm fast against the output: over 20 s that is ~190 frames
        // the queue would gain. The drift control absorbs it without a trim (a click).
        {
            const double outRate = 44100.0;
            var x = Monitor(48000.0, 48000.0 * (1 + 200e-6), outRate, seconds: 20);
            var (jump, rms, pitch) = Measure(x, (int)outRate, from: (int)outRate, to: x.Length / 2);
            yield return (rms > 0.1 && jump < 0.01 && Math.Abs(pitch - Hz * (1 + 200e-6)) < 1.0,
                $"…and a capture clock 200 ppm fast is followed for 20 s without a click (worst jump {jump:G3}, {pitch:F2} Hz)");
        }

        // Decimating, nothing above the engine's Nyquist may fold back as a false tone (#620
        // review): a 96 kHz input under a 44.1 kHz engine low-passes before it decimates. 30 kHz
        // would alias to 14.1 kHz; it must be gone, while 15 kHz passes whole.
        {
            const double outRate = 44100.0;
            var hi = Monitor(96000.0, 96000.0, outRate, seconds: 2, tone: 30000.0);
            var (_, hiRms, _) = Measure(hi, (int)outRate, from: (int)outRate, to: hi.Length / 2);
            var mid = Monitor(96000.0, 96000.0, outRate, seconds: 2, tone: 15000.0);
            var (_, midRms, _) = Measure(mid, (int)outRate, from: (int)outRate, to: mid.Length / 2);
            double db = 20 * Math.Log10(Math.Max(hiRms, 1e-9) / Math.Max(midRms, 1e-9));
            yield return (midRms > 0.18 && db < -60,
                $"…and a 96 kHz input's 30 kHz tone doesn't alias into the band ({db:F0} dB against 15 kHz at rms {midRms:F3})");
        }

        // The rates match: the direct path, as before.
        {
            const double outRate = 44100.0;
            var x = Monitor(outRate, outRate, outRate, seconds: 3);
            var (jump, rms, pitch) = Measure(x, (int)outRate, from: (int)outRate, to: x.Length / 2);
            yield return (rms > 0.1 && jump < 0.01 && Math.Abs(pitch - Hz) < 1.0,
                $"…and an input at the engine's rate still monitors directly (worst jump {jump:G3}, {pitch:F1} Hz)");
        }
    }

    // An audio track monitoring the hardware input for `seconds` of output at `outRate`. The
    // "device" reports `nominalIn` and delivers InBlock-frame blocks of a sine on its own clock,
    // `actualIn` frames a second; each output block renders once the input blocks captured by
    // its end have arrived (a capture callback lands just before the output one).
    private static float[] Monitor(double nominalIn, double actualIn, double outRate, int seconds, double tone = Hz)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        // Before monitoring is on: it would otherwise open the machine's real input, whose
        // blocks would land in the same ring as the test's.
        e.SetMonitorInputForTest(nominalIn, InBlock);
        int t = e.AddAudioTrack();
        e.SetTrackMonitor(t, true);
        int total = (int)(seconds * outRate);
        var all = new float[total * 2];
        var buf = new float[OutBlock * 2];
        var block = new float[InBlock * 2];
        long inDone = 0;
        for (int done = 0; done < total; done += OutBlock)
        {
            double until = (done + OutBlock) / outRate;
            while ((inDone + InBlock) / actualIn <= until)
            {
                for (int i = 0; i < InBlock; i++)
                    block[i * 2] = block[i * 2 + 1] = Amp * (float)Math.Sin(2 * Math.PI * tone * (inDone + i) / nominalIn);
                e.PushMonitorFramesForTest(block, InBlock);
                inDone += InBlock;
            }
            int n = Math.Min(OutBlock, total - done);
            e.RenderOffline(buf, n, outRate);
            Array.Copy(buf, 0, all, done * 2, n * 2);
        }
        return all;
    }

    // Worst second difference and RMS of the left channel over [from, to), and its pitch from
    // the rising zero crossings (first to last, so a partial cycle doesn't count).
    private static (double Jump, double Rms, double Pitch) Measure(float[] x, int rate, int from, int to)
    {
        double jump = 0, sq = 0;
        int first = -1, last = -1, crossings = 0;
        for (int i = from; i < to; i++)
        {
            double l = x[i * 2];
            jump = Math.Max(jump, Math.Abs(l - 2 * x[(i - 1) * 2] + x[(i - 2) * 2]));
            sq += l * l;
            if (x[(i - 1) * 2] < 0 && l >= 0)
            {
                if (first < 0) first = i;
                else crossings++;
                last = i;
            }
        }
        double pitch = last > first ? crossings * (double)rate / (last - first) : 0;
        return (jump, Math.Sqrt(sq / (to - from)), pitch);
    }
}
