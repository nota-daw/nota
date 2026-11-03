// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Effects on an audio track ring out after Stop (#449). A stopped audio track used to skip
// its whole chain, so a reverb on it fell to digital silence the instant the transport
// stopped, and a plug-in never saw a stopped block. Now the chain runs on silence until its
// output has been quiet for a moment, then the track goes back to costing nothing.
// Also runnable alone: `dotnet Nota.SmokeTest.dll --audio-tails`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class AudioTailTests
{
    private const int Rate = 44100, Block = 512, Reverb = 2, Delay = 3;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        string wav = Wav();
        try
        {
            // Play 1 s of a 2 s tone through a long reverb, stop, listen 1 s.
            var (wet, wetQuiet) = AfterStop(wav, withReverb: true, seconds: 1, thenSeconds: 40);
            double tail = Rms(wet, 0, Rate / 4), later = Rms(wet, Rate / 2, Rate);
            yield return (tail > 1e-3 && later > 0 && later < tail,
                $"a reverb on an audio track rings out after Stop (first 0.25 s rms {tail:F4}, 0.5–1 s {later:G3})");
            yield return (wetQuiet,
                "…and the track falls to exact silence once the tail has died away");

            var (dry, _) = AfterStop(wav, withReverb: false, seconds: 1, thenSeconds: 0);
            yield return (Rms(dry, 0, Rate) == 0, "an audio track without effects is silent at once after Stop");

            yield return (NeverPlayed(), "an audio track with a reverb that never played stays silent while stopped");

            // Greptile on #469: an edit after Stop makes a new version of the track; the tail
            // has to ring on through it rather than reset.
            var (edited, _) = AfterStop(wav, withReverb: true, seconds: 1, thenSeconds: 0, editAfterStop: true);
            yield return (Rms(edited, Rate / 2, Rate) > 1e-4,
                $"…and keeps ringing when a clip is edited after Stop (0.5–1 s rms {Rms(edited, Rate / 2, Rate):G3})");

            // Two 2 s delays in series: the first echo comes out about 4 s after the input, so
            // more than 3 s of silence at the output is not the end of the tail.
            double late = LateEcho(wav);
            yield return (late > 1e-3, $"two 2 s delays in series still sound their echo ~4 s after Stop (rms {late:F4})");
        }
        finally { try { File.Delete(wav); } catch { } }
    }

    // The first second after Stop, and whether the last 2 s of `thenSeconds` more are all zero.
    private static (float[] After, bool QuietAtEnd) AfterStop(string wav, bool withReverb, int seconds, int thenSeconds,
        bool editAfterStop = false)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, 0.0);
        if (withReverb)
        {
            int d = e.AddBuiltinDevice(t, Reverb);
            SetByName(e, t, d, "decay", 0.95f);
            SetByName(e, t, d, "dry/wet", 0.6f);
        }
        e.Seek(0.0);
        e.Play();
        Render(e, seconds * Rate);
        e.StopTransport();
        if (editAfterStop)
        {
            Render(e, Block);
            e.SetClipGain(t, 0, 0.5f);
        }
        var after = Render(e, Rate);
        bool quiet = true;
        if (thenSeconds > 0)
        {
            Render(e, (thenSeconds - 2) * Rate);
            var end = Render(e, 2 * Rate);
            quiet = end.All(v => v == 0f);
        }
        return (after, quiet);
    }

    // RMS of the 3.5–4.5 s after Stop, with 0.25 s of the tone played into two delays in series.
    private static double LateEcho(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, 0.0);
        for (int k = 0; k < 2; k++)
        {
            int d = e.AddBuiltinDevice(t, Delay);
            SetByName(e, t, d, "sync", 0f);
            SetByName(e, t, d, "time l", 1f);
            SetByName(e, t, d, "time r", 1f);
            SetByName(e, t, d, "feedback", 0f);
            SetByName(e, t, d, "wow depth", 0f);
            SetByName(e, t, d, "wet only", 1f);
        }
        e.Seek(0.0);
        e.Play();
        Render(e, Rate / 4);
        e.StopTransport();
        var after = Render(e, 5 * Rate);
        return Rms(after, Rate * 7 / 2, Rate * 9 / 2);
    }

    private static bool NeverPlayed()
    {
        using var e = new NotaEngine();
        int t = e.AddAudioTrack();
        e.AddBuiltinDevice(t, Reverb);
        return Render(e, Rate).All(v => v == 0f);
    }

    private static void SetByName(NotaEngine e, int t, int d, string name, float norm)
    {
        for (int p = 0; p < e.DeviceParamCount(t, d); p++)
            if (e.DeviceParamName(t, d, p).Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                float lo = e.DeviceParamMin(t, d, p), hi = e.DeviceParamMax(t, d, p);
                e.DeviceSetParam(t, d, p, lo + (hi - lo) * norm);
                return;
            }
    }

    private static float[] Render(NotaEngine e, int frames)
    {
        var all = new float[frames * 2];
        var b = new float[Block * 2];
        for (int done = 0; done < frames; done += Block)
        {
            int n = Math.Min(Block, frames - done);
            e.RenderOffline(b, n);
            Array.Copy(b, 0, all, done * 2, n * 2);
        }
        return all;
    }

    private static double Rms(float[] st, int fromFrame, int toFrame)
    {
        double s = 0;
        int n = 0;
        for (int i = fromFrame * 2; i < toFrame * 2 && i < st.Length; i++) { s += st[i] * (double)st[i]; n++; }
        return Math.Sqrt(s / Math.Max(1, n));
    }

    private static string Wav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-tail-{Guid.NewGuid():N}.wav");
        var x = new float[Rate * 2 * 2];
        for (int i = 0; i < x.Length / 2; i++) x[2 * i] = x[2 * i + 1] = 0.4f * (float)Math.Sin(2 * Math.PI * 330 * i / Rate);
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, x.Length / 2);
        return path;
    }
}
