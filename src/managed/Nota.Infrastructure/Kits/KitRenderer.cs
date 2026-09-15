// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Renders one KitPad recipe into a finished one-shot and writes it as a WAV.
//
// The signal path: synthesize the dry voice at 4x the target rate (KitVoices) → lo-fi →
// the "analog glue" tone stage → shaping filters → noise floor → decimate through a
// zero-phase low-pass → room → DC block, tail trim, normalize. Oversampling only the
// stages that actually generate harmonics (oscillators and saturation) keeps this cheap
// while removing the aliasing that gives synthetic percussion its glassy edge.

using System;
using System.IO;

namespace Nota.Infrastructure.Kits;

/// <summary>A rendered one-shot: interleaved float frames ready for the WAV writer.</summary>
public sealed record RenderedSample(float[] Interleaved, int Channels, int Frames, int SampleRate);

public static class KitRenderer
{
    /// <summary>Render rate of the shipped kits. 48 kHz is the engine's usual device rate,
    /// so pads play back without resampling on most setups.</summary>
    public const int SampleRate = 48000;

    private const int Oversample = 4;

    /// <summary>Synthesizes <paramref name="pad"/>. <paramref name="seed"/> makes the
    /// result reproducible — the same kit always renders to the same audio.</summary>
    public static RenderedSample Render(KitPad pad, uint seed)
    {
        double osSr = SampleRate * (double)Oversample;
        int osLen = Math.Max(Oversample * 64, (int)(pad.Length * osSr));
        var buf = new double[osLen];
        var rng = new Rng(seed);

        KitVoices.Render(pad, buf, osSr, rng);

        if (pad.Lofi > 0 || pad.Bits < 24 || pad.CrushHz > 0)
        {
            int hold = pad.CrushHz > 0 ? Math.Max(1, (int)(osSr / pad.CrushHz)) : 1;
            double bits = pad.Bits < 24 ? pad.Bits : 24 - pad.Lofi * 12;
            KitDsp.Crush(buf, bits, hold);
        }

        ApplyGlue(pad, buf, osSr, rng);

        var mono = KitDsp.Decimate(buf, Oversample, SampleRate);

        double[][] chans = pad.Room > 0
            ? ApplyRoom(pad, mono, SampleRate)
            : new[] { mono };

        foreach (var c in chans)
        {
            var dc = new DcBlock(SampleRate);
            for (int i = 0; i < c.Length; i++) c[i] = dc.Process(c[i]);
        }

        int frames = KitDsp.TrimTail(chans, -66, SampleRate);

        double peak = 0;
        foreach (var c in chans)
            for (int i = 0; i < frames; i++) peak = Math.Max(peak, Math.Abs(c[i]));
        double gain = peak > 1e-9 ? KitDsp.Db(pad.PeakDb) / peak : 0;

        int ch = chans.Length;
        var inter = new float[frames * ch];
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < ch; c++)
                inter[i * ch + c] = (float)(chans[c][i] * gain);

        return new RenderedSample(inter, ch, frames, SampleRate);
    }

    /// <summary>Renders <paramref name="pad"/> straight to a 24-bit WAV at
    /// <paramref name="path"/>. 24-bit because these files are a library the user will
    /// keep processing — quantization noise has no business being baked in.</summary>
    public static void RenderToFile(KitPad pad, uint seed, string path)
    {
        var s = Render(pad, seed);
        var tmp = path + ".part";
        using (var w = new WavWriter(tmp, s.SampleRate, s.Channels, WavBitDepth.Pcm24))
            w.WriteFrames(s.Interleaved, s.Frames);
        File.Move(tmp, path, overwrite: true);   // never leave a half-written sample behind
    }

    // The "analog glue": a low-mid lift and an upper-treble roll-off around a gentle
    // asymmetric saturation, plus an optional noise bed. None of it is dramatic on its
    // own; together it is most of the difference between a synthetic hit and one that
    // sounds like it came off a machine with transformers in it.
    private static void ApplyGlue(KitPad pad, double[] buf, double sr, Rng rng)
    {
        double w = Math.Clamp(pad.Warmth, 0, 1);
        if (w > 0)
        {
            var shelf = Biquad.LowShelf(160, 1.6 * w, sr);
            var air = Biquad.HighShelf(9000, -3.2 * w, sr);
            var body = Biquad.Peaking(420, 0.8, 1.1 * w, sr);
            for (int i = 0; i < buf.Length; i++)
            {
                double x = air.Process(body.Process(shelf.Process(buf[i])));
                buf[i] = KitDsp.Saturate(x, w * 0.18, 0.8);
            }
        }

        if (pad.HpHz > 0)
        {
            var hp = Biquad.HighPass(pad.HpHz, 0.7071, sr);
            for (int i = 0; i < buf.Length; i++) buf[i] = hp.Process(buf[i]);
        }
        if (pad.LpHz > 0)
        {
            var lp = Biquad.LowPass(pad.LpHz, 0.7071, sr);
            for (int i = 0; i < buf.Length; i++) buf[i] = lp.Process(buf[i]);
        }

        if (pad.Hiss > 0)
        {
            // Gate the bed with the hit's own envelope: a noise floor that keeps running
            // under silence is hiss, not character, and it would defeat the tail trim.
            var tilt = Biquad.LowPass(6000, 0.7071, sr);
            double env = 0, atk = 0.002, rel = 0.0004;
            for (int i = 0; i < buf.Length; i++)
            {
                double a = Math.Abs(buf[i]);
                env += (a > env ? atk : rel) * (a - env);
                buf[i] += tilt.Process(rng.NextGauss()) * pad.Hiss * 0.02 * Math.Sqrt(Math.Min(1, env * 4));
            }
        }
    }

    // A short stereo room. The dry hit stays centred and only the room spreads, so the
    // sample still collapses cleanly to mono.
    private static double[][] ApplyRoom(KitPad pad, double[] mono, double sr)
    {
        // Give the tail somewhere to go — the dry render usually ends near silence.
        int extra = (int)(sr * (0.25 + pad.RoomDecay * 1.4));
        int n = mono.Length + extra;
        var l = new double[n];
        var r = new double[n];
        var room = new Room(pad.RoomSize, pad.RoomDecay, pad.RoomDamp, sr);
        var preLp = Biquad.LowPass(7000, 0.7071, sr);
        var preHp = Biquad.HighPass(180, 0.7071, sr);
        int preDelay = (int)(sr * 0.006 * (0.4 + pad.RoomSize));
        double send = pad.Room;
        double width = Math.Clamp(pad.Width, 0, 1);
        // Gated reverb: cut the tail dead after RoomGate seconds. A studio trick of the
        // 80s that a plain decay cannot imitate — the tail is loud right up to the cut.
        int gateAt = pad.RoomGate > 0 ? (int)(sr * pad.RoomGate) : int.MaxValue;
        int gateFade = Math.Max(1, (int)(sr * 0.012));

        for (int i = 0; i < n; i++)
        {
            double dry = i < mono.Length ? mono[i] : 0;
            double x = i >= preDelay && i - preDelay < mono.Length ? mono[i - preDelay] : 0;
            room.Process(preHp.Process(preLp.Process(x)) * send, out double wl, out double wr);
            double g = 0.9;
            if (i >= gateAt) g *= i >= gateAt + gateFade ? 0 : 1.0 - (double)(i - gateAt) / gateFade;
            double mid = (wl + wr) * 0.5;
            l[i] = dry + (mid + (wl - mid) * width) * g;
            r[i] = dry + (mid + (wr - mid) * width) * g;
        }
        return new[] { l, r };
    }
}
