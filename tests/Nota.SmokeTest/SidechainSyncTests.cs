// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A sidechain key arrives in the same block (#676). A keyed device used to hear its source's
// previous block: a few ms live, but a whole 8192-frame chunk in an export, so a kick-keyed
// Compressor pumped ~170 ms late in the file while it sat on the kick in the app. Each check
// keys a hard Compressor on a held tone off a kick track (a 60 Hz burst on every beat) and finds
// where in the beat the ducking starts — from the difference between a render with the
// Compressor on and one with it bypassed, so nothing else in the mix counts. It must start just
// after the kick at any block size, with the keyed track before or after the kick, on a group
// and on a return. A key cycle still renders the same every time. Also runnable alone:
// `--sidechain-sync`.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SidechainSyncTests
{
    private const int Sr = 48000;
    private const double Bpm = 120;
    private const int CompKind = 1, Thresh = 0, Ratio = 1, Attack = 2, Release = 3;
    private const int Beats = 8;
    private static readonly int[] Blocks = { 256, 512, 1024, 4096, 8192 };

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var where in new[] { Where.After, Where.Before, Where.Group, Where.Return })
            foreach (var r in Onsets(where)) yield return r;
        foreach (var r in Cycle()) yield return r;
    }

    private enum Where { After, Before, Group, Return }

    private static string Wav(string name, Func<int, float> sample)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-scsync-{name}-{Guid.NewGuid():N}.wav");
        int n = (int)(Beats * Sr * 60.0 / Bpm);
        var x = new float[n * 2];
        for (int i = 0; i < n; i++) x[2 * i] = x[2 * i + 1] = sample(i);
        using (var w = new Nota.Infrastructure.WavWriter(path, Sr, 2, WavBitDepth.Float32)) w.WriteFrames(x, n);
        return path;
    }

    private static readonly Lazy<string> KickWav = new(() => Wav("kick", i =>
    {
        int spb = (int)(Sr * 60.0 / Bpm), k = i % spb;
        return k < Sr / 20 ? 0.8f * (float)Math.Sin(2 * Math.PI * 60 * k / Sr) : 0f; // 50 ms on each beat
    }));
    private static readonly Lazy<string> ToneWav = new(() => Wav("tone", i => 0.3f * (float)Math.Sin(2 * Math.PI * 220 * i / Sr)));

    // The kick and a held tone; a hard, fast Compressor keyed off the kick sits on the tone track
    // (After / Before: the tone track after or before the kick in the list), on a group holding it,
    // or on a return the tone sends to.
    private static NotaEngine Make(Where where, out int compTrack, out int comp)
    {
        var e = new NotaEngine();
        e.SetBpm(Bpm);
        int tone = -1, kick;
        if (where == Where.Before) tone = e.AddAudioTrack();
        kick = e.AddAudioTrack();
        e.AddAudioClip(kick, KickWav.Value, 0.0);
        if (where != Where.Before) tone = e.AddAudioTrack();
        e.AddAudioClip(tone, ToneWav.Value, 0.0);
        compTrack = tone;
        if (where == Where.Group)
        {
            compTrack = e.AddGroupTrack();
            e.SetTrackGroup(tone, compTrack);
        }
        else if (where == Where.Return)
        {
            compTrack = e.AddReturnTrack();
            e.SetTrackSend(tone, 0, 1f);
        }
        comp = e.AddBuiltinDevice(compTrack, CompKind);
        e.DeviceSetParam(compTrack, comp, Thresh, -40f);
        e.DeviceSetParam(compTrack, comp, Ratio, 10f);
        e.DeviceSetParam(compTrack, comp, Attack, 1f);
        e.DeviceSetParam(compTrack, comp, Release, 50f);
        e.SetDeviceSidechainSource(compTrack, comp, kick);
        return e;
    }

    // The whole clip from the start, in `block`-frame blocks: offline (an export's way) or as the
    // device callback renders.
    private static float[] Render(NotaEngine e, int block, bool live)
    {
        int frames = (int)(Beats * Sr * 60.0 / Bpm);
        var all = new float[frames * 2];
        var buf = new float[block * 2];
        e.RenderOffline(new float[128], 64, Sr); // sets the rate
        e.StopTransport();
        e.Seek(0);
        e.Play();
        for (int done = 0; done < frames;)
        {
            int m = Math.Min(block, frames - done);
            if (live) e.RenderDeviceBlock(buf, m); else e.RenderOffline(buf, m);
            Array.Copy(buf, 0, all, done * 2, m * 2);
            done += m;
        }
        e.StopTransport();
        return all;
    }

    // Where the Compressor starts ducking after each beat's kick, in ms, averaged over beats 1..6:
    // the first 64-frame window whose on − bypassed difference passes a fifth of that beat's peak.
    private static double OnsetMs(float[] on, float[] off)
    {
        const int w = 64;
        double spb = Sr * 60.0 / Bpm, sum = 0;
        int counted = 0;
        for (int b = 1; b <= 6; b++)
        {
            int s0 = (int)(b * spb), nw = (int)(spb / 2) / w;
            var env = new double[nw];
            double peak = 0;
            for (int k = 0; k < nw; k++)
            {
                double acc = 0;
                for (int i = s0 + k * w; i < s0 + (k + 1) * w; i++) { double d = on[i * 2] - off[i * 2]; acc += d * d; }
                env[k] = Math.Sqrt(acc / w);
                peak = Math.Max(peak, env[k]);
            }
            if (peak < 1e-4) continue;
            int first = Array.FindIndex(env, v => v > peak / 5);
            sum += first * w * 1000.0 / Sr;
            counted++;
        }
        return counted == 6 ? sum / counted : double.NaN;
    }

    private static IEnumerable<(bool, string)> Onsets(Where where)
    {
        using var e = Make(where, out int ct, out int comp);
        var onsets = new List<string>();
        double lo = double.MaxValue, hi = double.MinValue;
        bool all = true;
        foreach (var (block, live) in Blocks.Select(b => (b, false)).Append((512, true)))
        {
            e.SetDeviceBypassed(ct, comp, false);
            var on = Render(e, block, live);
            e.SetDeviceBypassed(ct, comp, true);
            var off = Render(e, block, live);
            double ms = OnsetMs(on, off);
            all &= !double.IsNaN(ms);
            lo = Math.Min(lo, ms);
            hi = Math.Max(hi, ms);
            onsets.Add($"{(live ? "live " : "")}{block}: {ms:F1}");
        }
        e.SetDeviceBypassed(ct, comp, false);
        string what = where switch
        {
            Where.After => "on a track after the kick",
            Where.Before => "on a track before the kick",
            Where.Group => "on a group",
            _ => "on a return",
        };
        yield return (all && hi < 15 && hi - lo < 2,
            $"sc sync: a kick-keyed Compressor {what} ducks on the kick at every block size, live and offline (onset ms — {string.Join(", ", onsets)})");
    }

    // Two tracks key each other: the cycle can't be ordered, so one key stays a block late, and
    // the export of it is the same from one fresh engine to the next — what each detector hears
    // doesn't depend on which render thread got there first. Also with a third track keyed off
    // one of them, rendered a level later.
    private static IEnumerable<(bool, string)> Cycle()
    {
        float[] Once()
        {
            using var e = new NotaEngine();
            e.SetBpm(Bpm);
            int a = e.AddAudioTrack(), b = e.AddAudioTrack(), c = e.AddAudioTrack();
            e.AddAudioClip(a, ToneWav.Value, 0.0);
            e.AddAudioClip(b, KickWav.Value, 0.0);
            e.AddAudioClip(c, ToneWav.Value, 0.0);
            foreach (var (t, src) in new[] { (a, b), (b, a), (c, b) })
            {
                int d = e.AddBuiltinDevice(t, CompKind);
                e.DeviceSetParam(t, d, Thresh, -40f);
                e.DeviceSetParam(t, d, Ratio, 10f);
                e.SetDeviceSidechainSource(t, d, src);
            }
            return Render(e, 8192, false);
        }
        var r1 = Once();
        bool same = true, finite = true;
        double sum = 0;
        for (int run = 0; run < 4; run++)
        {
            var r2 = Once();
            same &= r1.Length == r2.Length && r1.AsSpan().SequenceEqual(r2);
        }
        foreach (var v in r1) { finite &= float.IsFinite(v); sum += v * v; }
        yield return (same && finite && sum > 1,
            $"sc sync: two tracks keying each other (and a third keyed off one) render the same from every fresh engine (same {same}, finite {finite}, energy {sum:F1})");
    }
}
