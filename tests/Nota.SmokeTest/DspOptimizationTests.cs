// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DSP optimization smoke tests: verifies that optimized FastMath, Delay invariant hoisting,
// Compressor log/exp fast paths, Eq chunked block processing, and Chamber unrolled hadamard8
// produce numerically stable, finite, non-glitching audio output without dropouts.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DspOptimizationTests
{
    private const int Chunk = 512, Chunks = 20;

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_dsp_" + Guid.NewGuid().ToString("N") + ".wav");
        WavWriter.WriteTones(wav, seconds: 2.0, freqs: new[] { 220.0, 880.0 }, amp: 0.2, sampleRate: 44100);
        try
        {
            yield return TestEqBlockProcessing(wav);
            yield return TestDelayTapeAndLfo(wav);
            yield return TestCompressorFastMath(wav);
            yield return TestChamberHadamard(wav);
            yield return TestVintageFastMath(wav);
            yield return TestAutoFilterFastMath(wav);
            yield return TestAutoPanFastMath(wav);
            yield return TestBassSynthPreDrive();
            yield return TestDynamicEqFastMath(wav);
            yield return TestCeilingFastMath(wav);
            yield return TestMultiCoreParallelRendering(wav);
            yield return TestTruePeakSimd(wav);
        }
        finally
        {
            try { File.Delete(wav); } catch { }
        }
    }

    private static (bool, string) TestEqBlockProcessing(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 0); // Eq (kind 0)
        if (d < 0) return (false, "Eq: device created");

        // Set up active bands with bells and shelves across channels
        e.DeviceSetParam(t, d, 0, 1.0f);   // Band 1 On
        e.DeviceSetParam(t, d, 2, 200.0f);  // Band 1 Freq
        e.DeviceSetParam(t, d, 3, 3.0f);   // Band 1 Gain
        e.DeviceSetParam(t, d, 5, 1.0f);   // Band 2 On
        e.DeviceSetParam(t, d, 7, 1000.0f); // Band 2 Freq
        e.DeviceSetParam(t, d, 8, -4.0f);  // Band 2 Gain

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 2.0;
        return (ok, $"Eq block-based filtering: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestDelayTapeAndLfo(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 3); // Delay (kind 3)
        if (d < 0) return (false, "Delay: device created");

        // Enable Tape saturation and Wow LFO
        e.DeviceSetParam(t, d, 14, 1.0f); // Tape mode on
        e.DeviceSetParam(t, d, 6, 0.6f);  // Feedback
        e.DeviceSetParam(t, d, 15, 0.7f); // Wow depth
        e.DeviceSetParam(t, d, 16, 0.4f); // Wow rate

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 2.5;
        return (ok, $"Delay tape saturation & fast LFO: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestCompressorFastMath(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 1); // Compressor (kind 1)
        if (d < 0) return (false, "Compressor: device created");

        // Threshold -20 dB, Ratio 4:1
        e.DeviceSetParam(t, d, 0, -20.0f);
        e.DeviceSetParam(t, d, 1, 4.0f);

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 1.5;
        return (ok, $"Compressor fast math detection & reduction: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestChamberHadamard(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 20); // Chamber (kind 20)
        if (d < 0) return (false, "Chamber: device created");

        // Decay 2.0s, Mix 0.5
        e.DeviceSetParam(t, d, 0, 2.0f);
        e.DeviceSetParam(t, d, 1, 0.5f);

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 2.0;
        return (ok, $"Chamber unrolled hadamard8 diffusion: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestVintageFastMath(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 8); // Vintage (kind 8)
        if (d < 0) return (false, "Vintage: device created");

        e.DeviceSetParam(t, d, 0, 0.7f); // Drive
        e.DeviceSetParam(t, d, 2, 0.5f); // Wow
        e.DeviceSetParam(t, d, 3, 0.5f); // Flutter

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 2.0;
        return (ok, $"Vintage fast tanh saturation & fast sin LFO: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestAutoFilterFastMath(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 7); // AutoFilter (kind 7)
        if (d < 0) return (false, "AutoFilter: device created");

        e.DeviceSetParam(t, d, 14, 0.5f); // Drive
        e.DeviceSetParam(t, d, 15, 1.0f); // Analog mode On

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 2.0;
        return (ok, $"AutoFilter fast tanh drive/analog & fast sin LFO: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestAutoPanFastMath(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 9); // AutoPan / Orbit (kind 9)
        if (d < 0) return (false, "AutoPan: device created");

        e.DeviceSetParam(t, d, 1, 0.8f); // Amount
        e.DeviceSetParam(t, d, 3, 0.5f); // Shape (sharpens wave, tests invTanhKk)

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 2.0;
        return (ok, $"AutoPan invTanhKk hoisting & fast sin LFO: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestBassSynthPreDrive()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddBassSynthTrack();
        if (t < 0) return (false, "BassSynth: track created");

        e.AddMidiClip(t, 0, 4);
        e.SetClipNotes(t, 0, new[] { new NotaNote(36, 0.0, 4.0, 1.0f) });

        // Set FilDrive (param 12 in BassSynth) -> preDrive = 1.0 + 0.5 * 11.0 = 6.5 (> 1.001)
        e.PluginParamSet(t, -1, 12, 0.5f);

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 2.0;
        return (ok, $"BassSynth preDrive makeup invariant hoisting: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestDynamicEqFastMath(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 13); // DynamicEq (kind 13)
        if (d < 0) return (false, "DynamicEq: device created");

        // Band 1: On=1, Mode=Duck(1), Thr=-20dB, Range=-6dB
        e.DeviceSetParam(t, d, 0, 1.0f);   // Band 1 On
        e.DeviceSetParam(t, d, 5, 1.0f);   // Band 1 Mode (Duck)
        e.DeviceSetParam(t, d, 6, -20.0f); // Band 1 Threshold
        e.DeviceSetParam(t, d, 7, -6.0f);  // Band 1 Range

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp < 2.0;
        return (ok, $"DynamicEq fast linToDb detector & exp2f modulation: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestCeilingFastMath(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 14); // Ceiling (kind 14)
        if (d < 0) return (false, "Ceiling: device created");

        // Ceiling=-0.5 dB, Soft clip=0.8
        e.DeviceSetParam(t, d, 0, -0.5f); // Ceiling dB
        e.DeviceSetParam(t, d, 4, 0.8f);  // Soft (Glue)

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.01 && maxAmp <= 1.05;
        return (ok, $"Ceiling fast tanh soft clip & linToDb: finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestMultiCoreParallelRendering(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(128);

        // Add 16 independent tracks (stressing thread pool parallel dispatch)
        const int numTracks = 16;
        int[] trackIds = new int[numTracks];
        for (int i = 0; i < numTracks; ++i)
        {
            int t = e.AddAudioTrack();
            trackIds[i] = t;
            e.AddAudioClip(t, wav, startBeat: 0.0);
            e.SetTrackVolume(t, 0.15f);
            e.SetTrackPan(t, (i % 2 == 0) ? -0.4f : 0.4f);
        }

        // Add return track with Reverb
        int ret = e.AddReturnTrack();
        e.AddBuiltinDevice(ret, 2); // Reverb
        for (int i = 0; i < numTracks; ++i)
        {
            e.SetTrackSend(trackIds[i], 0, 0.1f);
        }

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        bool ok = allFinite && maxAmp > 0.05 && maxAmp < 5.0;
        return (ok, $"Multi-core parallel track rendering (16 tracks + send bus): finite ({allFinite}), peak {maxAmp:F3}");
    }

    private static (bool, string) TestTruePeakSimd(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        int d = e.AddBuiltinDevice(t, 14); // Ceiling (kind 14)
        if (d < 0) return (false, "Ceiling: device created");

        // Enable True-Peak (param 7 = 1.0f), Ceiling = -1.0 dB, Gain = +6 dB to force overs
        e.DeviceSetParam(t, d, 7, 1.0f);   // TruePeak on
        e.DeviceSetParam(t, d, 0, -1.0f);  // Ceil -1 dB
        e.DeviceSetParam(t, d, 1, 6.0f);   // Gain +6 dB

        float[] buf = new float[Chunk * 2];
        e.Play();
        bool allFinite = true;
        double maxAmp = 0.0;

        for (int i = 0; i < Chunks; ++i)
        {
            e.RenderOffline(buf, Chunk);
            for (int s = 0; s < Chunk * 2; ++s)
            {
                float v = buf[s];
                if (!float.IsFinite(v)) allFinite = false;
                maxAmp = Math.Max(maxAmp, Math.Abs(v));
            }
        }
        e.StopTransport();

        // -1.0 dBFS is ~0.891 linear, with inter-sample peak limit tolerance <= 1.05
        bool ok = allFinite && maxAmp > 0.05 && maxAmp <= 1.05;
        return (ok, $"Ceiling True-Peak SIMD interpolation: finite ({allFinite}), peak {maxAmp:F3} (<= 1.05)");
    }
}
