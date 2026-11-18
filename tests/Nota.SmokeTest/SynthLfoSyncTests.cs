// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A tempo-synced LFO in Nota Aurora and Nota Volt follows the song position while the
// transport plays (#676), as the effects' LFOs do. Its phase used to free-run from whatever
// played before, so a pulse on the beat while working landed somewhere else in the export.
// Each check routes a synced LFO to Level on a held note and finds where in the beat the dip
// lands: the same place after any history, from any start point, at any block size and
// sample rate. With the transport stopped a synced LFO still moves.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SynthLfoSyncTests
{
    private const int Sr = 48000;
    private const double Bpm = 120;
    private const int Bins = 64; // dip position resolution: 1/64 beat

    private static int Pi(NotaEngine e, int t, string id)
    {
        int n = e.PluginParamCount(t, -1);
        for (int i = 0; i < n; i++) if (e.PluginParamId(t, -1, i) == id) return i;
        throw new InvalidOperationException($"no param {id}");
    }

    // A held C3 for 64 beats with LFO `lfo` (1 or 2) synced to one beat, sine, full depth, on Level.
    private static NotaEngine Make(bool aurora, int lfo, out int t)
    {
        var e = new NotaEngine();
        e.SetBpm(Bpm);
        int tt = aurora ? e.AddWavetableSynthTrack() : e.AddVoltSynthTrack();
        void P(string id, float v) => e.PluginParamSet(tt, -1, Pi(e, tt, id), v);
        P($"lfo{lfo}depth", 1f);
        P($"lfo{lfo}shape", 0f);
        P($"lfo{lfo}sync", 3f / 7f);                // 1 beat
        P($"mtx{lfo + 1}_5", 0f);                  // LFO → Level, full
        int c = e.AddMidiClip(tt, 0, 64);
        e.SetClipNotes(tt, c, new[] { new NotaNote(48, 0, 64, 1f) });
        t = tt;
        return e;
    }

    private static float[] Render(NotaEngine e, bool offline, int block, int frames, int sr = 0)
    {
        var all = new float[frames * 2];
        var buf = new float[block * 2];
        for (int done = 0; done < frames;)
        {
            int m = Math.Min(block, frames - done);
            if (!offline) e.RenderDeviceBlock(buf, m);
            else if (sr > 0) { e.RenderOffline(buf, m, sr); sr = 0; }
            else e.RenderOffline(buf, m);
            Array.Copy(buf, 0, all, done * 2, m * 2);
            done += m;
        }
        return all;
    }

    // Where in the beat (0..1) the dip lands, over beats [from, from + 12) of a render that
    // started at beat `startBeat`.
    private static double DipPhase(float[] st, int sr, double startBeat, int from)
    {
        double spb = sr * 60.0 / Bpm;
        var acc = new double[Bins];
        int win = (int)(spb / Bins);
        for (int b = from; b < from + 12; b++)
            for (int k = 0; k < Bins; k++)
            {
                int s0 = (int)Math.Round((b - startBeat) * spb) + k * win;
                double sum = 0;
                for (int i = s0; i < s0 + win; i++) sum += st[i * 2] * st[i * 2];
                acc[k] += sum;
            }
        int best = 0;
        for (int k = 1; k < Bins; k++) if (acc[k] < acc[best]) best = k;
        return best / (double)Bins;
    }

    // Distance between two beat fractions, around the cycle.
    private static double Apart(double a, double b) { double d = Math.Abs(a - b) % 1.0; return Math.Min(d, 1.0 - d); }

    private static void Play(NotaEngine e, double beat) { e.StopTransport(); e.Seek(beat); e.Play(); }

    public static IEnumerable<(bool, string)> Run()
    {
        const double Tol = 1.5 / Bins;
        foreach (var (aurora, lfo) in new[] { (true, 1), (true, 2), (false, 1), (false, 2) })
        {
            string name = $"{(aurora ? "Aurora" : "Volt")} LFO {lfo}";
            var seen = new List<(string, double)>();
            // Histories: none, then plays that stop off the beat, so a free-running phase drifts.
            foreach (double pre in new[] { 0.0, 1.37, 2.91 })
            {
                using var e = Make(aurora, lfo, out _);
                e.RenderOffline(new float[128], 64, Sr); // the device's rate
                if (pre > 0) { Play(e, 10); Render(e, false, 512, (int)(pre * Sr)); }
                Play(e, 0);
                seen.Add(($"play after {pre:0.00} s", DipPhase(Render(e, false, 512, Sr * 14), Sr, 0, 8)));
                Render(e, false, 512, 777); // stop off the beat
                Play(e, 0);
                seen.Add(($"play again after {pre:0.00} s", DipPhase(Render(e, false, 512, Sr * 14), Sr, 0, 8)));
                Play(e, 9.5);                             // start mid-song, off the beat
                seen.Add(($"play from beat 9.5 after {pre:0.00} s", DipPhase(Render(e, false, 512, Sr * 10), Sr, 9.5, 12)));
                Play(e, 0);                               // an export: 8192-frame blocks at 44.1 kHz
                seen.Add(($"export after {pre:0.00} s", DipPhase(Render(e, true, 8192, 44100 * 14, 44100), 44100, 0, 8)));
            }
            double first = seen[0].Item2;
            var off = seen.Where(s => Apart(s.Item2, first) > Tol).ToList();
            yield return (off.Count == 0,
                $"{name} synced to the beat: the pulse lands at the same place in the beat after any history, from any start, in an export ({first:0.000}{(off.Count > 0 ? "; off: " + string.Join(", ", off.Select(o => $"{o.Item1} {o.Item2:0.000}")) : "")})");
        }

        // With the transport stopped a synced LFO keeps moving (a note played from the keyboard).
        foreach (bool aurora in new[] { true, false })
        {
            using var e = Make(aurora, 1, out int t);
            e.RenderOffline(new float[128], 64, Sr);
            e.TrackNoteOn(t, 48, 1f);
            var st = Render(e, false, 512, Sr * 3);
            double spb = Sr * 60.0 / Bpm, lo = double.MaxValue, hi = 0;
            for (int k = 0; k < 16; k++)
            {
                int s0 = (int)(spb * 2 + k * spb / 16);
                double sum = 0;
                for (int i = s0; i < s0 + (int)(spb / 16); i++) sum += st[i * 2] * st[i * 2];
                lo = Math.Min(lo, sum); hi = Math.Max(hi, sum);
            }
            e.TrackNoteOff(t, 48);
            yield return (hi > 0 && lo < hi * 0.5,
                $"{(aurora ? "Aurora" : "Volt")}: a synced LFO still moves with the transport stopped (quietest/loudest 1/16 beat {lo / Math.Max(hi, 1e-30):0.00})");
        }
    }
}
