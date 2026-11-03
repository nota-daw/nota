// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Splitting an audio clip writes a short, visible fade at the cut (issue #286): release on
// the left piece, attack on the right. The user drags those handles to change the length.
// Clips on one track cannot overlap, so this is not a constant-power overlap. Also runnable
// alone: `dotnet run --project tests/Nota.SmokeTest -- --split-fades`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SplitFadeTests
{
    private const int Sr = 44100, Block = 512;
    private const double FadeSeconds = 0.004;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_split_fade.wav");
        WavWriter.WriteSine(wav, seconds: 2.0, freq: 440, sampleRate: Sr);
        double expect = FadeSeconds * 120.0 / 60.0;

        using (var e = new NotaEngine())
        {
            yield return (e.SplitFades, "split fades default on");
            e.SetBpm(120);
            int t = e.AddAudioTrack();
            int c = e.AddAudioClip(t, wav, 0.0);
            e.TryGetClipInfo(t, c, out var info);
            double mid = info.StartBeat + info.LengthBeats * 0.5;
            int right = e.SplitClip(t, c, mid);
            var left = e.GetClipAdsr(t, c);
            var rght = e.GetClipAdsr(t, right);
            yield return (right == c + 1 && Near(left.ReleaseBeats, expect) && Near(rght.AttackBeats, expect)
                          && left.AttackBeats == 0 && rght.ReleaseBeats == 0,
                $"split writes a {FadeSeconds * 1000:0} ms fade ({left.ReleaseBeats:F4} / {rght.AttackBeats:F4} beats, want {expect:F4})");

            yield return (e.Undo() && e.TryGetTrackInfo(0, out var back) && back.ClipCount == 1
                          && e.GetClipAdsr(t, c).IsIdentity,
                "one undo restores the unsplit clip");
        }

        using (var e = new NotaEngine())
        {
            e.SetBpm(120);
            int t = e.AddAudioTrack();
            int c = e.AddAudioClip(t, wav, 0.0);
            e.SetClipAdsr(t, c, new ClipAdsr { AttackBeats = 0.5, DecayBeats = 0.25, Sustain = 1f, ReleaseBeats = 0.4 });
            e.TryGetClipInfo(t, c, out var info);
            int right = e.SplitClip(t, c, info.LengthBeats * 0.5);
            var left = e.GetClipAdsr(t, c);
            var rght = e.GetClipAdsr(t, right);
            yield return (right > c && Near(left.AttackBeats, 0.5) && Near(left.DecayBeats, 0.25) && Near(left.ReleaseBeats, expect)
                          && Near(rght.AttackBeats, expect) && rght.DecayBeats == 0 && Near(rght.ReleaseBeats, 0.4),
                "a split keeps the attack on the left and the release on the right, and fades the cut");
        }

        using (var e = new NotaEngine())
        {
            e.SetBpm(120);
            e.SplitFades = false;
            int t = e.AddAudioTrack();
            int c = e.AddAudioClip(t, wav, 0.0);
            e.TryGetClipInfo(t, c, out var info);
            int right = e.SplitClip(t, c, info.LengthBeats * 0.5);
            var left = e.GetClipAdsr(t, c);
            var rght = e.GetClipAdsr(t, right);
            yield return (right > c && left.IsIdentity && rght.IsIdentity, "the preference off leaves a hard cut");

            yield return (e.FadeSplits(new[] { t }, new[] { c }), "Fade at splits still fades a join when the preference is off");
            left = e.GetClipAdsr(t, c);
            rght = e.GetClipAdsr(t, right);
            yield return (Near(left.ReleaseBeats, expect) && Near(rght.AttackBeats, expect),
                "the command fades both sides of the join");

            var longer = left;
            longer.ReleaseBeats = 0.5;
            longer.Sustain = 1f;
            e.SetClipAdsr(t, c, longer);
            var shortIn = rght;
            shortIn.AttackBeats = expect * 0.25;
            shortIn.Sustain = 1f;
            e.SetClipAdsr(t, right, shortIn);
            yield return (e.FadeSplits(new[] { t }, new[] { right }), "a shorter fade is raised");
            yield return (Near(e.GetClipAdsr(t, c).ReleaseBeats, 0.5) && Near(e.GetClipAdsr(t, right).AttackBeats, expect),
                "a longer fade is not shortened");
            yield return (!e.FadeSplits(new[] { t }, new[] { c, right }), "nothing to change returns false, so there is no extra undo");
            yield return (e.Undo() && Near(e.GetClipAdsr(t, right).AttackBeats, expect * 0.25),
                "one undo puts the raised fade back");
        }

        string longWav = Path.Combine(Path.GetTempPath(), "nota_smoke_split_fade_long.wav");
        WavWriter.WriteSine(longWav, seconds: 3.0, freq: 440, sampleRate: Sr);
        using (var e = new NotaEngine())
        {
            e.SetBpm(120);
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, longWav, 0.0);
            yield return (e.SplitClipsInRange(new[] { t }, 2.0, 4.0), "range split");
            e.TryGetTrackInfo(0, out var ti);
            var a = e.GetClipAdsr(t, 0);
            var b = e.GetClipAdsr(t, 1);
            var c = e.GetClipAdsr(t, 2);
            yield return (ti.ClipCount == 3 && a.AttackBeats == 0 && Near(a.ReleaseBeats, expect)
                          && Near(b.AttackBeats, expect) && Near(b.ReleaseBeats, expect)
                          && Near(c.AttackBeats, expect) && c.ReleaseBeats == 0,
                "a range split fades each new cut and leaves the original ends alone");
        }

        using (var e = new NotaEngine())
        {
            e.SetBpm(120);
            int t = e.AddAudioTrack();
            int c = e.AddAudioClip(t, wav, 0.0);
            e.TryGetClipInfo(t, c, out var info);
            int other = e.AddAudioClip(t, wav, info.LengthBeats + 1.0);
            yield return (other >= 0 && !e.FadeSplits(new[] { t }, new[] { c }), "a gap is not a join");

            int mt = e.AddInstrumentTrack();
            int mc = e.AddMidiClip(mt, 0.0, 4.0);
            int mr = e.SplitClip(mt, mc, 2.0);
            yield return (mr == mc + 1 && !e.FadeSplits(new[] { mt }, new[] { mc, mr }), "MIDI splits are left alone");
        }

        using (var e = new NotaEngine())
        {
            e.SetBpm(120);
            int t = e.AddAudioTrack();
            int c = e.AddAudioClip(t, wav, 0.0);
            e.SetClipWarp(t, c, true, 0);
            e.SetClipWarpLength(t, c, 4.0);
            var whole = Render(e, 4);
            const double at = 1.37;
            int right = e.SplitClip(t, c, at);
            var split = Render(e, 4);
            int cut = (int)(at * Sr * 60.0 / 120.0);
            int guard = (int)(FadeSeconds * Sr) + 40;
            double body = 0;
            for (int i = 0; i < Math.Min(whole.Length, split.Length); i++)
            {
                int frame = i / 2;
                if (Math.Abs(frame - cut) <= guard) continue;
                body = Math.Max(body, Math.Abs(whole[i] - split[i]));
            }
            double hard = Rms(whole, cut - 80, cut + 80);
            double soft = Rms(split, cut - 80, cut + 80);
            yield return (right > c && body < 1e-4 && soft < hard * 0.75 && hard > 0.05,
                $"warped split dips only at the cut (body {body:E1}, seam {soft:F3} vs {hard:F3})");
        }
    }

    private static bool Near(double got, double want) => Math.Abs(got - want) < 1e-6;

    private static float[] Render(NotaEngine e, double beats)
    {
        int frames = (int)(beats * Sr * 60 / 120.0);
        var all = new float[frames * 2];
        var buf = new float[Block * 2];
        e.Seek(0); e.Play();
        for (int off = 0; off < frames; off += Block)
        {
            int n = Math.Min(Block, frames - off);
            e.RenderOffline(buf, n, Sr);
            Array.Copy(buf, 0, all, off * 2, n * 2);
        }
        e.StopTransport();
        return all;
    }

    private static double Rms(float[] interleaved, int f0, int f1)
    {
        double s = 0;
        int n = 0;
        f0 = Math.Max(0, f0);
        f1 = Math.Min(interleaved.Length / 2, f1);
        for (int f = f0; f < f1; f++)
        {
            float v = interleaved[f * 2];
            s += v * v;
            n++;
        }
        return n == 0 ? 0 : Math.Sqrt(s / n);
    }
}
