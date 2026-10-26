// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Splitting a warped clip (#299, #259). The halves of a split, and what a range cut leaves,
// play the very stretch the clip played before — a slice of it, not a fresh stretch — so the
// cut is seamless and the edit costs nothing however long the clip is (it used to re-stretch
// both halves: ~13 s for a 20-minute clip).
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --warp-split`.

using System.Diagnostics;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class WarpSplitTests
{
    private const int Sr = 44100, Block = 512;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_warp_split.wav");
        WavWriter.WriteSine(wav, seconds: 30.0, freq: 330, sampleRate: Sr);

        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        int c = e.AddAudioClip(t, wav, 0.0);
        e.SetClipWarp(t, c, true, 3);
        e.SetClipWarpLength(t, c, 64);   // 30 s stretched over 64 beats (32 s): a real stretch
        var whole = Render(e, 16);

        var sw = Stopwatch.StartNew();
        int right = e.SplitClip(t, c, 7.3);
        long ms = sw.ElapsedMilliseconds;
        var split = Render(e, 16);
        double diff = MaxDiff(whole, split);
        yield return (right == c + 1 && diff < 1e-6, $"warp split: the halves play exactly what the clip played (max diff {diff:E1})");
        yield return TimingBudget.Check(right == c + 1, ms < 100, $"warp split of a 30 s clip doesn't re-stretch it ({ms} ms)");

        sw.Restart();
        bool cut = e.CutRange(new[] { t }, 9.0, 10.0);
        ms = sw.ElapsedMilliseconds;
        var afterCut = Render(e, 16);
        // Up to the cut and from its end on, the audio is the clip's own, untouched.
        int cutA = (int)(9.0 * Sr / 2) * 2, cutB = (int)(10.0 * Sr / 2) * 2;
        double before = MaxDiff(whole[..cutA], afterCut[..cutA]), after = MaxDiff(whole[(cutB + 2)..], afterCut[(cutB + 2)..]);
        yield return (cut && before < 1e-6 && after < 1e-6, $"warp range cut: what's left plays as before ({before:E1}, {after:E1})");
        yield return TimingBudget.Check(cut, ms < 100, $"warp range cut doesn't re-stretch ({ms} ms)");
    }

    // The first `beats` beats from the top, interleaved stereo.
    private static float[] Render(NotaEngine e, double beats)
    {
        int frames = (int)(beats * Sr / 2);
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

    private static double MaxDiff(float[] a, float[] b)
    {
        double m = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
        return m;
    }
}
