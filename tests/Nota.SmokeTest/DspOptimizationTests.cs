// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DSP optimization paths are transparent: the same session built twice in two fresh
// engines renders bit-identically (guarding the parallel Pass 1 leaf rendering against
// scheduling-dependent races and the SIMD mixer against elementwise drift), and an
// empty session renders exact zeros (the silence fast-path never invents signal).

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DspOptimizationTests
{
    private const int Block = 512;

    private static float[] Render(NotaEngine e, int blocks)
    {
        var all = new float[blocks * Block * 2];
        var b = new float[Block * 2];
        for (int i = 0; i < blocks; i++) { e.RenderOffline(b, Block); Array.Copy(b, 0, all, i * Block * 2, Block * 2); }
        return all;
    }
    private static double Rms(float[] st)
    {
        double s = 0;
        foreach (var v in st) s += v * (double)v;
        return Math.Sqrt(s / Math.Max(1, st.Length));
    }
    private static double MaxDiff(float[] a, float[] b)
    {
        double d = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) d = Math.Max(d, Math.Abs(a[i] - b[i]));
        return d;
    }

    // The same 3-track session (synth + audio + send/return), built twice from scratch.
    private static void BuildThreeTrack(NotaEngine e)
    {
        e.SetBpm(120);
        int src = e.AddInstrumentTrack();
        int clip = e.AddMidiClip(src, 0.0, 4.0);
        e.SetClipNotes(src, clip, new[] { new NotaNote(60, 0.0, 4.0, 0.9f), new NotaNote(64, 2.0, 2.0, 0.8f) });
        e.AddBuiltinDevice(src, 0);   // EQ-8, flat by default
        int audio = e.AddAudioTrack();
        e.AddBuiltinDevice(audio, 4); // Utility, unity gain
        int ret = e.AddReturnTrack();
        e.AddBuiltinDevice(ret, 4);   // Utility, unity gain, no tail
        e.SetTrackSend(src, 0, 0.5f);
    }
    private static float[] RenderThreeTrack()
    {
        using var e = new NotaEngine();
        BuildThreeTrack(e);
        e.Seek(0); e.Play();
        var out_ = Render(e, 24);
        e.StopTransport();
        return out_;
    }

    private static float[] RenderEightTracks()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        for (int i = 0; i < 8; i++)
        {
            int t = e.AddInstrumentTrack();
            int clip = e.AddMidiClip(t, 0.0, 4.0);
            e.SetClipNotes(t, clip, new[] { new NotaNote(60 + i, 0.0, 4.0, 0.8f) });
        }
        e.Seek(0); e.Play();
        var out_ = Render(e, 12);
        e.StopTransport();
        return out_;
    }

    public static IEnumerable<(bool, string)> Run()
    {
        // Determinism: identical sessions in fresh engines render identical block sequences.
        var a = RenderThreeTrack();
        var b = RenderThreeTrack();
        double diff = MaxDiff(a, b), rms = Rms(a);
        yield return (rms > 1e-3 && diff == 0.0,
            $"same 3-track session (synth + audio + send/return) renders bit-identically twice (rms {rms:F4}, maxdiff {diff:E1})");

        // Multi-track parallel determinism: 8 instruments spread across pool workers.
        var c = RenderEightTracks();
        var d = RenderEightTracks();
        double diff8 = MaxDiff(c, d), rms8 = Rms(c);
        yield return (rms8 > 1e-3 && diff8 == 0.0,
            $"8 instrument tracks on different notes render bit-identically twice (rms {rms8:F4}, maxdiff {diff8:E1})");

        // Silence transparency: tracks but no clips/notes render exact zeros.
        using (var e = new NotaEngine())
        {
            e.AddInstrumentTrack();
            e.AddAudioTrack();
            var silent = Render(e, 8);
            int nonzero = 0;
            foreach (var v in silent) if (v != 0.0f) nonzero++;
            yield return (nonzero == 0,
                $"empty session (tracks, no clips) renders exact zeros for 8 blocks (nonzero {nonzero} of {silent.Length})");
        }
    }
}
