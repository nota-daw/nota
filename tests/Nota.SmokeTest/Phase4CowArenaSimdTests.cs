// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Phase 4 (#1050): Track CoW (CowSlice) snapshot isolation & Undo/Redo parity,
// session-sized StereoScratchArena live-to-offline transitions (128 -> 8192 -> 16384),
// Eq per-band cache + double2 SIMD scalar reference parity (< -120 dBFS),
// gliding/M-S/AutoGain parity across 0 vs 4 worker threads, and SimdDispatch check.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class Phase4CowArenaSimdTests
{
    private const int Rate = 48000;
    private const int EqKind = 0, DelayKind = 3, RackDeviceKind = 5, VoltKind = 6;

    // Exact power-of-two frequencies & Q=1.0 match FastMath::log2f/exp2f without approximation error.
    private static readonly (int band, int type, double freq, double gainDb, double q, int slope)[] StaticEqBands =
    [
        (0, 0, 128.0, 0.0, 1.0, 1),   // LowCut 24 dB/oct
        (1, 1, 256.0, -3.5, 1.0, 0),  // LowShelf
        (2, 2, 1024.0, 6.0, 1.0, 0),  // Bell
        (3, 3, 4096.0, 0.0, 4.0, 0),  // Notch
        (4, 4, 8192.0, 2.5, 1.0, 0),  // HighShelf
    ];

    private static string MultiToneStereoWav()
    {
        string path = Path.Combine(Path.GetTempPath(), "nota-phase4-stereo-eq.wav");
        if (!File.Exists(path))
        {
            WavWriter.WriteStereo(path, 0.5, Rate, i =>
            {
                double l = 0.20 * Math.Sin(2.0 * Math.PI * 110.0 * i / Rate) +
                           0.18 * Math.Sin(2.0 * Math.PI * 1024.0 * i / Rate) +
                           0.12 * Math.Sin(2.0 * Math.PI * 7000.0 * i / Rate);
                double r = 0.16 * Math.Cos(2.0 * Math.PI * 256.0 * i / Rate) +
                           0.15 * Math.Sin(2.0 * Math.PI * 4096.0 * i / Rate) +
                           0.10 * Math.Sin(2.0 * Math.PI * 9500.0 * i / Rate);
                return (l, r);
            });
        }
        return path;
    }

    private static float[] RenderBlocks(NotaEngine e, int framesPerBlock, int blocks)
    {
        var all = new float[blocks * framesPerBlock * 2];
        var buf = new float[framesPerBlock * 2];
        for (int b = 0; b < blocks; b++)
        {
            e.RenderOffline(buf, framesPerBlock, Rate);
            buf.CopyTo(all, b * framesPerBlock * 2);
        }
        return all;
    }

    private static double PeakOf(float[] pcm)
    {
        double peak = 0.0;
        foreach (float x in pcm) peak = Math.Max(peak, Math.Abs(x));
        return peak;
    }

    private sealed class ScalarBiquad
    {
        public double B0 = 1.0, B1, B2, A1, A2;
        private double _z1, _z2;

        public double Run(double x)
        {
            double y = B0 * x + _z1;
            _z1 = B1 * x - A1 * y + _z2;
            _z2 = B2 * x - A2 * y;
            return y;
        }
    }

    private static void AddScalarEqBand(
        List<ScalarBiquad> chain, int type, double sr, double f0, double gainDb, double q, int slope)
    {
        static ScalarBiquad MakeOne(int ty, double sampleRate, double freq, double gDb, double qq)
        {
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cw = Math.Cos(w0), sw = Math.Sin(w0);
            double b0, b1, b2, a0, a1, a2;
            if (ty == 0 || ty == 5) // LowCut (HPF) / HighCut (LPF)
            {
                double alpha = sw / (2.0 * Math.Max(0.05, qq));
                if (ty == 0) { b0 = (1.0 + cw) * 0.5; b1 = -(1.0 + cw); b2 = (1.0 + cw) * 0.5; }
                else { b0 = (1.0 - cw) * 0.5; b1 = 1.0 - cw; b2 = (1.0 - cw) * 0.5; }
                a0 = 1.0 + alpha; a1 = -2.0 * cw; a2 = 1.0 - alpha;
            }
            else if (ty == 3) // Notch
            {
                double alpha = sw / (2.0 * Math.Max(0.05, qq));
                b0 = 1.0; b1 = -2.0 * cw; b2 = 1.0;
                a0 = 1.0 + alpha; a1 = -2.0 * cw; a2 = 1.0 - alpha;
            }
            else if (ty == 2) // Bell
            {
                double a = Math.Pow(10.0, gDb / 40.0);
                double alpha = sw / (2.0 * Math.Max(0.05, qq));
                b0 = 1.0 + alpha * a; b1 = -2.0 * cw; b2 = 1.0 - alpha * a;
                a0 = 1.0 + alpha / a; a1 = -2.0 * cw; a2 = 1.0 - alpha / a;
            }
            else // LowShelf (1) / HighShelf (4)
            {
                double a = Math.Pow(10.0, gDb / 40.0);
                double beta = Math.Sqrt(2.0 * a);
                double ap1 = a + 1.0, am1 = a - 1.0;
                if (ty == 1)
                {
                    b0 = a * (ap1 - am1 * cw + beta * sw);
                    b1 = 2.0 * a * (am1 - ap1 * cw);
                    b2 = a * (ap1 - am1 * cw - beta * sw);
                    a0 = ap1 + am1 * cw + beta * sw;
                    a1 = -2.0 * (am1 + ap1 * cw);
                    a2 = ap1 + am1 * cw - beta * sw;
                }
                else
                {
                    b0 = a * (ap1 + am1 * cw + beta * sw);
                    b1 = -2.0 * a * (am1 + ap1 * cw);
                    b2 = a * (ap1 + am1 * cw - beta * sw);
                    a0 = ap1 - am1 * cw + beta * sw;
                    a1 = 2.0 * (am1 - ap1 * cw);
                    a2 = ap1 - am1 * cw - beta * sw;
                }
            }
            return new ScalarBiquad { B0 = b0 / a0, B1 = b1 / a0, B2 = b2 / a0, A1 = a1 / a0, A2 = a2 / a0 };
        }

        if ((type == 0 || type == 5) && slope > 0)
        {
            double[] qs = slope == 1
                ? [0.54119610, 1.30656296]
                : [0.50979558, 0.60134489, 0.89997622, 2.56291545];
            double res = q / 0.70710678;
            for (int k = 0; k < qs.Length; k++)
            {
                double qk = Math.Clamp(k == qs.Length - 1 ? qs[k] * res : qs[k], 0.1, 40.0);
                chain.Add(MakeOne(type, sr, f0, 0.0, qk));
            }
            return;
        }
        chain.Add(MakeOne(type, sr, f0, gainDb, q));
    }

    // CoW multi-track session: untouched tracks share immutable CowSlice storage across edits,
    // Undo/Redo restores pre-edit playback cleanly, and 0 vs 4 threads stay bit-identical.
    private static (bool ok, string label) CheckCowTrackIsolationAndUndoParity()
    {
        const int block = 512;
        static (NotaEngine e, int instTrk, int audioTrk, int clipM, int modLane) MakeCowSession(int threads, string wav)
        {
            var e = new NotaEngine { RenderThreads = threads };
            e.RenderOffline(new float[block * 2], block, Rate);
            e.SetBpm(120);
            e.SetMetronome(false);

            for (int i = 0; i < 6; i++) e.AddScene();

            int instTrk = e.AddVoltSynthTrack();
            e.SetTrackVolume(instTrk, 0.30f);
            int firstClip = 0;
            for (int c = 0; c < 60; c++)
            {
                int mc = e.AddMidiClip(instTrk, c * 4.0, 2.0);
                e.SetClipNotes(instTrk, mc, [new NotaNote(48 + (c % 12), 0.0, 0.5, 0.8f)]);
                if (c == 0) firstClip = mc;
            }
            int mod = e.ModulatorAdd(instTrk, 0);
            int eq = e.AddBuiltinDevice(instTrk, EqKind);
            int cv = e.CvLinkAddToTarget(instTrk, mod, 0, -1, eq, 2);
            if (cv >= 0) e.SetCvLinkDepth(instTrk, cv, 0.25f);

            int audioTrk = e.AddAudioTrack();
            e.SetTrackVolume(audioTrk, 0.25f);
            for (int c = 0; c < 60; c++)
                e.AddAudioClip(audioTrk, wav, c * 4.0);
            int autoLane = e.AddAutomationLane(audioTrk, AutomationTarget.Volume, -1, -1);
            e.SetAutomationPoints(audioTrk, autoLane, [new AutomationPoint(0.0, 0.25f), new AutomationPoint(16.0, 0.35f)]);

            // Add 28 extra tracks with slots + clips to exercise shared CowSlices across publishes.
            for (int t = 0; t < 28; t++)
            {
                int k = (t % 2 == 0) ? e.AddAudioTrack() : e.AddInstrumentTrack();
                e.SetTrackVolume(k, 0.1f);
                if (t % 2 == 0) e.AddAudioClip(k, wav, 8.0 + t);
                else e.AddSessionMidiClip(k, 0, 2.0);
            }
            return (e, instTrk, audioTrk, firstClip, autoLane);
        }

        string wav = MultiToneStereoWav();
        var (e0, it0, at0, mc0, lane0) = MakeCowSession(0, wav);
        var (e4, it4, at4, mc4, lane4) = MakeCowSession(4, wav);
        using var _0 = e0;
        using var _4 = e4;

        e0.ClearUndoHistory();
        e4.ClearUndoHistory();

        static float[] PlayFromBeatZero(NotaEngine eng, int blocks)
        {
            eng.StopTransport();
            eng.Seek(0.0);
            eng.Play();
            return RenderBlocks(eng, block, blocks);
        }

        float[] base0 = PlayFromBeatZero(e0, 12);
        float[] base4 = PlayFromBeatZero(e4, 12);
        if (!base0.All(float.IsFinite) || PeakOf(base0) <= 0.01)
            return (false, "CoW 30-track session baseline renders finite non-silent audio");
        if (!base0.SequenceEqual(base4))
            return (false, "CoW 30-track session baseline parity 0 vs 4 threads");

        // Rapid edits across distinct slices: transpose MIDI notes on clip 0, add session gain, edit automation.
        e0.SetClipNotes(it0, mc0, [new NotaNote(67, 0.0, 0.5, 0.9f), new NotaNote(71, 0.25, 0.5, 0.85f)]);
        e4.SetClipNotes(it4, mc4, [new NotaNote(67, 0.0, 0.5, 0.9f), new NotaNote(71, 0.25, 0.5, 0.85f)]);
        e0.SetAutomationPoints(at0, lane0, [new AutomationPoint(0.0, 0.45f), new AutomationPoint(8.0, 0.15f)]);
        e4.SetAutomationPoints(at4, lane4, [new AutomationPoint(0.0, 0.45f), new AutomationPoint(8.0, 0.15f)]);
        e0.SetSessionSlotGain(at0, 0, 0.7f);
        e4.SetSessionSlotGain(at4, 0, 0.7f);

        float[] edited0 = PlayFromBeatZero(e0, 12);
        float[] edited4 = PlayFromBeatZero(e4, 12);
        if (!edited0.All(float.IsFinite) || PeakOf(edited0) <= 0.01 || edited0.SequenceEqual(base0))
            return (false, "CoW detached edits alter rendered audio without corrupting sibling snapshots");
        if (!edited0.SequenceEqual(edited4))
            return (false, "CoW multi-edit state parity 0 vs 4 threads");

        // Undo back all 3 edits and verify restoration + 0 vs 4 parity, then Redo and re-check parity.
        for (int u = 0; u < 3; u++) { e0.Undo(); e4.Undo(); }
        float[] undone0 = PlayFromBeatZero(e0, 12);
        float[] undone4 = PlayFromBeatZero(e4, 12);
        if (!undone0.SequenceEqual(undone4) || PeakOf(undone0) <= 0.01)
            return (false, "CoW multi-undo parity 0 vs 4 threads");

        for (int r = 0; r < 3; r++) { e0.Redo(); e4.Redo(); }
        float[] redone0 = PlayFromBeatZero(e0, 12);
        float[] redone4 = PlayFromBeatZero(e4, 12);
        if (!redone0.SequenceEqual(redone4) || PeakOf(redone0) <= 0.01)
            return (false, "CoW multi-redo parity 0 vs 4 threads");

        return (true, "CowSlice snapshot isolation, Undo/Redo restoration, and 0 vs 4 thread parity");
    }

    // Session-sized StereoScratchArena starts at 2048 frames and expands cleanly across
    // 128 -> 8192 -> 16384 -> 256 frame transitions for RackInstrument, DrumRack, and RackDevice.
    private static (bool ok, string label) CheckScratchArenaLiveToOfflineTransition()
    {
        static NotaEngine MakeRackSession(int threads)
        {
            var e = new NotaEngine { RenderThreads = threads };
            e.SetBpm(120);
            e.SetMetronome(false);

            // Track 1: Instrument Rack holding Volt + nested RackDevice inside chain.
            int ir = e.AddInstrumentRackTrack();
            int ch0 = e.RackAddChain(ir, VoltKind);
            e.RackAddChainDevice(ir, ch0, DelayKind);
            e.SetTrackVolume(ir, 0.25f);
            int c1 = e.AddMidiClip(ir, 0.0, 16.0);
            e.SetClipNotes(ir, c1, [new NotaNote(55, 0.0, 8.0, 0.85f), new NotaNote(62, 0.5, 7.5, 0.8f)]);

            // Top-level RackDevice on track 1 (Series + Parallel chains).
            int rd = e.AddBuiltinDevice(ir, RackDeviceKind);
            int rc = e.RackDevAddChain(ir, rd, -1);
            e.RackDevAddChainDevice(ir, rd, rc, EqKind);

            // Track 2: Drum Rack holding Volt pads + insert EQ.
            int dr = e.AddDrumRackTrack();
            int pd = e.RackAddChain(dr, VoltKind);
            e.RackSetChainTriggerNote(dr, pd, 36);
            e.RackAddChainDevice(dr, pd, EqKind);
            e.SetTrackVolume(dr, 0.2f);
            int c2 = e.AddMidiClip(dr, 0.0, 16.0);
            e.SetClipNotes(dr, c2, [new NotaNote(36, 0.0, 4.0, 0.9f), new NotaNote(36, 1.0, 4.0, 0.85f)]);

            e.Seek(0.0);
            e.Play();
            return e;
        }

        using var e0 = MakeRackSession(0);
        using var e4 = MakeRackSession(4);

        foreach (int blockSize in new[] { 128, 128, 8192, 16384, 256 })
        {
            var b0 = new float[blockSize * 2];
            var b4 = new float[blockSize * 2];
            e0.RenderOffline(b0, blockSize, Rate);
            e4.RenderOffline(b4, blockSize, Rate);
            if (!b0.All(float.IsFinite) || PeakOf(b0) <= 0.005)
                return (false, $"ScratchArena rack session at block {blockSize} renders finite audio");
            if (!b0.SequenceEqual(b4))
                return (false, $"ScratchArena rack transition 0 vs 4 parity at block {blockSize}");
        }

        return (true, "StereoScratchArena 128 -> 8192 -> 16384 -> 256 transition & rack parity");
    }

    // Eq per-band coefficient cache + double2 stereo SIMD vs scalar RBJ double reference (< -120 dBFS),
    // plus parameter glide, Mid/Side routing, AutoGain, and 0 vs 4 thread bit-identity.
    private static (bool ok, string label) CheckEqCacheVectorizedScalarAndMsParity()
    {
        const int block = 512, numBlocks = 8;
        string wav = MultiToneStereoWav();

        static (NotaEngine e, int trk, int eqIdx) MakeEqSession(int threads, string clipPath, bool enableEq)
        {
            var e = new NotaEngine { RenderThreads = threads };
            e.RenderOffline(new float[block * 2], block, Rate);
            e.SetBpm(120);
            e.SetMetronome(false);

            int trk = e.AddAudioTrack();
            e.SetTrackVolume(trk, 0.6f);
            e.AddAudioClip(trk, clipPath, 0.0);
            int eq = -1;
            if (enableEq)
            {
                eq = e.AddBuiltinDevice(trk, EqKind);
                for (int b = 0; b < 8; b++) e.DeviceSetParam(trk, eq, b * 5 + 0, 0.0f);
                foreach (var (band, type, freq, gainDb, q, slope) in StaticEqBands)
                {
                    e.DeviceSetParam(trk, eq, band * 5 + 0, 1.0f);
                    e.DeviceSetParam(trk, eq, band * 5 + 1, type);
                    e.DeviceSetParam(trk, eq, band * 5 + 2, (float)freq);
                    e.DeviceSetParam(trk, eq, band * 5 + 3, (float)gainDb);
                    e.DeviceSetParam(trk, eq, band * 5 + 4, (float)q);
                    e.DeviceSetParam(trk, eq, 40 + band, slope);
                    e.DeviceSetParam(trk, eq, 48 + band, 0.0f); // ChStereo
                }
            }
            e.Seek(0.0);
            e.Play();
            return (e, trk, eq);
        }

        using var dryEng = MakeEqSession(0, wav, false).e;
        var (eq0, trk0, eqIdx0) = MakeEqSession(0, wav, true);
        var (eq4, trk4, eqIdx4) = MakeEqSession(4, wav, true);
        using var _0 = eq0;
        using var _4 = eq4;

        float[] dry = RenderBlocks(dryEng, block, numBlocks);
        float[] wet0 = RenderBlocks(eq0, block, numBlocks);
        float[] wet4 = RenderBlocks(eq4, block, numBlocks);

        if (!wet0.All(float.IsFinite) || PeakOf(wet0) <= 0.01)
            return (false, "Static 8-band Eq renders finite audio");
        if (!wet0.SequenceEqual(wet4))
            return (false, "Static 8-band Eq matches 0 vs 4 threads bit-identically");

        // Independent C# scalar RBJ double chain over dry[] must match wet0 within < -120 dBFS (1e-6).
        var refL = new List<ScalarBiquad>();
        var refR = new List<ScalarBiquad>();
        foreach (var (_, type, freq, gainDb, q, slope) in StaticEqBands)
        {
            AddScalarEqBand(refL, type, Rate, freq, gainDb, q, slope);
            AddScalarEqBand(refR, type, Rate, freq, gainDb, q, slope);
        }

        double maxErr = 0.0;
        for (int i = 0; i < dry.Length / 2; i++)
        {
            double l = dry[i * 2], r = dry[i * 2 + 1];
            foreach (var sec in refL) l = sec.Run(l);
            foreach (var sec in refR) r = sec.Run(r);
            maxErr = Math.Max(maxErr, Math.Abs(wet0[i * 2] - (float)l));
            maxErr = Math.Max(maxErr, Math.Abs(wet0[i * 2 + 1] - (float)r));
        }
        if (maxErr >= 1.0e-6)
            return (false, $"Eq double2 SIMD matches scalar RBJ reference (< -120 dBFS, err={maxErr:E3})");

        // Exercise gliding + Mid/Side routing + AutoGain across blocks (0 vs 4 thread parity).
        eq0.DeviceSetParam(trk0, eqIdx0, 48 + 2, 1.0f); // Band 2 -> Mid
        eq4.DeviceSetParam(trk4, eqIdx4, 48 + 2, 1.0f);
        eq0.DeviceSetParam(trk0, eqIdx0, 48 + 3, 2.0f); // Band 3 -> Side
        eq4.DeviceSetParam(trk4, eqIdx4, 48 + 3, 2.0f);
        eq0.DeviceSetParam(trk0, eqIdx0, 58, 1.0f);     // AutoGain = On
        eq4.DeviceSetParam(trk4, eqIdx4, 58, 1.0f);
        eq0.DeviceSetParam(trk0, eqIdx0, 2 * 5 + 2, 1800.0f);
        eq4.DeviceSetParam(trk4, eqIdx4, 2 * 5 + 2, 1800.0f);

        float[] ms0 = RenderBlocks(eq0, block, 6);
        float[] ms4 = RenderBlocks(eq4, block, 6);
        if (!ms0.All(float.IsFinite) || PeakOf(ms0) <= 0.01)
            return (false, "Gliding M/S + AutoGain Eq renders finite audio");
        if (!ms0.SequenceEqual(ms4))
            return (false, "Gliding M/S + AutoGain Eq matches 0 vs 4 threads bit-identically");

        return (true, $"Eq BandCache + double2 SIMD scalar parity (err={maxErr:E2} < -120 dBFS) & M/S glide parity");
    }

    public static IEnumerable<(bool ok, string label)> Run()
    {
        yield return CheckCowTrackIsolationAndUndoParity();
        yield return CheckScratchArenaLiveToOfflineTransition();
        yield return CheckEqCacheVectorizedScalarAndMsParity();
        yield return (NotaEngine.FxSelfTest(), "SimdDispatch & native Eq scalar reference self-test (FxSelfTest)");
    }
}
