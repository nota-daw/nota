// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Phase 5 (#1050, specs/118): Parallel multi-group/sub-group + return DAG waves,
// intra-track multi-chain racks (Instrument Rack, Drum Rack, Effect Rack),
// deterministic polyphonic synth voice batching (Aurora/Wavetable, Twin, Consort),
// and sample-identical parity across 0 vs N worker threads (AR-6).

using System.Diagnostics;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class Phase5RenderDagPlanarTests
{
    private const int Rate = 48000;
    private const int EqKind = 0, CompKind = 1, ReverbKind = 2, DelayKind = 3, RackDeviceKind = 5, AmpKind = 6;
    private const int SynthKind = 0, VoltKind = 6;

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return CheckGroupReturnSidechainDagParity();
        foreach (var item in CheckRackChainParallelParity()) yield return item;
        foreach (var item in CheckPolySynthVoiceBatchParity()) yield return item;
    }

    private static (float[] Pcm, double WorkerMs) RenderOfflineMeasured(NotaEngine e, int frames, int blocks)
    {
        var all = new float[blocks * frames * 2];
        var buf = new float[frames * 2];
        double cpu0 = e.RenderThreadsCpuSeconds;
        for (int b = 0; b < blocks; b++)
        {
            e.RenderOffline(buf, frames, Rate);
            buf.CopyTo(all, b * frames * 2);
        }
        double workerMs = (e.RenderThreadsCpuSeconds - cpu0) * 1000.0;
        return (all, workerMs);
    }

    private static double PeakOf(float[] pcm)
    {
        double peak = 0.0;
        foreach (float x in pcm) peak = Math.Max(peak, Math.Abs(x));
        return peak;
    }

    private static void SetParamByName(NotaEngine e, int trackId, int deviceIndex, string paramName, float normValue)
    {
        int n = e.PluginParamCount(trackId, deviceIndex);
        for (int i = 0; i < n; i++)
        {
            if (e.PluginParamName(trackId, deviceIndex, i).Contains(paramName, StringComparison.OrdinalIgnoreCase))
            {
                e.PluginParamSet(trackId, deviceIndex, i, normValue);
                return;
            }
        }
        throw new InvalidOperationException($"Parameter '{paramName}' not found on track {trackId} device {deviceIndex}");
    }

    // Nested groups (depth 1 & 2) + multiple return buses with intra-depth return-to-return sidechain + leaf sidechain.
    private static (bool Ok, string Label) CheckGroupReturnSidechainDagParity()
    {
        static (float[] Pcm, double WorkerMs) BuildAndRender(int threads)
        {
            using var e = new NotaEngine { RenderThreads = threads };
            e.RenderOffline(new float[512 * 2], 512, Rate);
            e.SetBpm(120);
            e.SetMetronome(false);

            int outerGrp = e.AddGroupTrack();
            int innerA = e.AddGroupTrack();
            int innerB = e.AddGroupTrack();
            e.SetTrackGroup(innerA, outerGrp);
            e.SetTrackGroup(innerB, outerGrp);
            e.AddBuiltinDevice(outerGrp, EqKind);
            e.AddBuiltinDevice(innerA, CompKind);
            e.AddBuiltinDevice(innerB, AmpKind);

            int ret0 = e.AddReturnTrack();
            int ret1 = e.AddReturnTrack();
            e.AddBuiltinDevice(ret0, ReverbKind);
            int ret1Comp = e.AddBuiltinDevice(ret1, CompKind);
            e.SetDeviceSidechainSource(ret1, ret1Comp, ret0);

            int[] leaves =
            [
                e.AddVoltSynthTrack(),
                e.AddWavetableSynthTrack(),
                e.AddVoltSynthTrack(),
                e.AddVoltSynthTrack(),
                e.AddVoltSynthTrack(),
                e.AddWavetableSynthTrack()
            ];
            for (int i = 0; i < leaves.Length; i++)
            {
                int t = leaves[i];
                e.SetTrackGroup(t, (i % 2 == 0) ? innerA : innerB);
                e.SetTrackVolume(t, 0.25f);
                e.SetTrackPan(t, (i - 2.5f) * 0.2f);
                e.AddBuiltinDevice(t, EqKind);
                int comp = e.AddBuiltinDevice(t, CompKind);
                if (i >= 3) e.SetDeviceSidechainSource(t, comp, leaves[i - 3]);
                e.AddBuiltinDevice(t, ReverbKind);
                e.SetTrackSend(t, 0, 0.20f);
                e.SetTrackSend(t, 1, 0.15f);
                int clip = e.AddMidiClip(t, 0.0, 8.0);
                e.SetClipNotes(t, clip,
                [
                    new NotaNote(48 + i * 2, 0.0, 6.0, 0.8f),
                    new NotaNote(55 + i * 2, 0.0, 6.0, 0.7f),
                    new NotaNote(60 + i, 0.25, 6.0, 0.75f),
                    new NotaNote(64 + i, 0.5, 6.0, 0.75f)
                ]);
            }

            e.Seek(0.0);
            e.Play();
            return RenderOfflineMeasured(e, 512, 384);
        }

        var r0 = BuildAndRender(0);
        var r4 = BuildAndRender(4);
        bool ok = r0.Pcm.All(float.IsFinite) && PeakOf(r0.Pcm) > 0.01 && r0.Pcm.SequenceEqual(r4.Pcm) && r4.WorkerMs > 0.0;
        return (ok, $"phase5 group/return/sidechain DAG parity (0 vs 4 threads bit-identical, peak={PeakOf(r0.Pcm):0.000}, worker={r4.WorkerMs:0.0} ms)");
    }

    // Multi-chain racks (>= 2 active chains) fan out across audio workers and match serial output bit-for-bit.
    private static IEnumerable<(bool Ok, string Label)> CheckRackChainParallelParity()
    {
        // 1) Instrument Rack (6 Synth/Volt chains with inserts on one track).
        {
            static (float[] Pcm, double WorkerMs) RenderInstRack(int threads)
            {
                using var e = new NotaEngine { RenderThreads = threads };
                e.RenderOffline(new float[512 * 2], 512, Rate);
                e.SetBpm(120);
                int r = e.AddInstrumentRackTrack();
                for (int c = 0; c < 6; c++)
                {
                    int ci = e.RackAddChain(r, (c % 2 == 0) ? VoltKind : SynthKind);
                    e.RackAddChainDevice(r, ci, EqKind);
                    e.RackAddChainDevice(r, ci, CompKind);
                    e.RackAddChainDevice(r, ci, ReverbKind);
                    e.RackSetChainPan(r, ci, (c - 2.5f) * 0.25f);
                }
                int clip = e.AddMidiClip(r, 0.0, 4.0);
                e.SetClipNotes(r, clip,
                [
                    new NotaNote(48, 0.0, 2.0, 0.85f),
                    new NotaNote(55, 0.0, 2.0, 0.80f),
                    new NotaNote(60, 0.0, 2.0, 0.75f),
                    new NotaNote(64, 0.0, 2.0, 0.75f)
                ]);
                e.Seek(0.0);
                e.Play();
                return RenderOfflineMeasured(e, 512, 48);
            }

            var s0 = RenderInstRack(0);
            var s4 = RenderInstRack(4);
            bool ok = s0.Pcm.All(float.IsFinite) && PeakOf(s0.Pcm) > 0.01 && s0.Pcm.SequenceEqual(s4.Pcm) && s4.WorkerMs > 0.0;
            yield return (ok, $"phase5 InstrumentRack 6-chain parallel parity (0 vs 4 threads bit-identical, peak={PeakOf(s0.Pcm):0.000}, worker={s4.WorkerMs:0.0} ms)");
        }

        // 2) Drum Rack (6 pads hit together with inserts).
        {
            static float[] RenderDrumRack(int threads)
            {
                using var e = new NotaEngine { RenderThreads = threads };
                e.RenderOffline(new float[512 * 2], 512, Rate);
                e.SetBpm(120);
                int r = e.AddDrumRackTrack();
                var notes = new NotaNote[6];
                for (int c = 0; c < 6; c++)
                {
                    int ci = e.RackAddChain(r, VoltKind);
                    e.RackSetChainTriggerNote(r, ci, 36 + c);
                    e.RackAddChainDevice(r, ci, EqKind);
                    e.RackAddChainDevice(r, ci, CompKind);
                    notes[c] = new NotaNote(36 + c, 0.0, 1.5, 0.8f);
                }
                int clip = e.AddMidiClip(r, 0.0, 4.0);
                e.SetClipNotes(r, clip, notes);
                e.Seek(0.0);
                e.Play();
                return RenderOfflineMeasured(e, 512, 32).Pcm;
            }

            var d0 = RenderDrumRack(0);
            var d4 = RenderDrumRack(4);
            bool ok = d0.All(float.IsFinite) && PeakOf(d0) > 0.01 && d0.SequenceEqual(d4);
            yield return (ok, $"phase5 DrumRack 6-pad parallel parity (0 vs 4 threads bit-identical, peak={PeakOf(d0):0.000})");
        }

        // 3) Audio Effect Rack (6 parallel chains with FX inserts).
        {
            static float[] RenderEffectRack(int threads)
            {
                using var e = new NotaEngine { RenderThreads = threads };
                e.RenderOffline(new float[512 * 2], 512, Rate);
                e.SetBpm(120);
                int t = e.AddVoltSynthTrack();
                int rk = e.AddBuiltinDevice(t, RackDeviceKind);
                for (int c = 0; c < 6; c++)
                {
                    int ci = e.RackDevAddChain(t, rk, -1);
                    e.RackDevAddChainDevice(t, rk, ci, EqKind);
                    e.RackDevAddChainDevice(t, rk, ci, CompKind);
                    e.RackDevAddChainDevice(t, rk, ci, AmpKind);
                    e.RackDevAddChainDevice(t, rk, ci, DelayKind);
                }
                int clip = e.AddMidiClip(t, 0.0, 4.0);
                e.SetClipNotes(t, clip, [new NotaNote(52, 0.0, 2.0, 0.85f), new NotaNote(64, 0.0, 2.0, 0.8f)]);
                e.Seek(0.0);
                e.Play();
                return RenderOfflineMeasured(e, 512, 32).Pcm;
            }

            var e0 = RenderEffectRack(0);
            var e4 = RenderEffectRack(4);
            bool ok = e0.All(float.IsFinite) && PeakOf(e0) > 0.01 && e0.SequenceEqual(e4);
            yield return (ok, $"phase5 EffectRack 6-chain parallel parity (0 vs 4 threads bit-identical, peak={PeakOf(e0):0.000})");
        }
    }

    // Single-track heavy polyphony across Aurora (Wavetable), Twin, and Consort matches 0 vs 4 threads sample-for-sample.
    private static IEnumerable<(bool Ok, string Label)> CheckPolySynthVoiceBatchParity()
    {
        static NotaNote[] ChordNotes(int count, int basePitch = 48)
        {
            var notes = new NotaNote[count];
            for (int i = 0; i < count; i++) notes[i] = new NotaNote(basePitch + i * 2, 0.0, 2.0, 0.7f);
            return notes;
        }

        // 1) Aurora / WavetableSynth (12 active voices)
        {
            static (float[] Pcm, double WorkerMs) RenderAurora(int threads)
            {
                using var e = new NotaEngine { RenderThreads = threads };
                e.RenderOffline(new float[512 * 2], 512, Rate);
                e.SetBpm(120);
                int t = e.AddWavetableSynthTrack();
                int clip = e.AddMidiClip(t, 0.0, 4.0);
                e.SetClipNotes(t, clip, ChordNotes(12, 42));
                e.Seek(0.0);
                e.Play();
                return RenderOfflineMeasured(e, 512, 48);
            }

            var w0 = RenderAurora(0);
            var w4 = RenderAurora(4);
            bool ok = w0.Pcm.All(float.IsFinite) && PeakOf(w0.Pcm) > 0.01 && w0.Pcm.SequenceEqual(w4.Pcm) && w4.WorkerMs > 0.0;
            yield return (ok, $"phase5 Aurora (Wavetable) 12-voice parallel batch parity (0 vs 4 threads bit-identical, peak={PeakOf(w0.Pcm):0.000}, worker={w4.WorkerMs:0.0} ms)");
        }

        // 2) Twin (10 active voices)
        {
            static float[] RenderTwin(int threads)
            {
                using var e = new NotaEngine { RenderThreads = threads };
                e.RenderOffline(new float[512 * 2], 512, Rate);
                e.SetBpm(120);
                int t = e.AddTwinTrack();
                int clip = e.AddMidiClip(t, 0.0, 4.0);
                e.SetClipNotes(t, clip, ChordNotes(10, 44));
                e.Seek(0.0);
                e.Play();
                return RenderOfflineMeasured(e, 512, 32).Pcm;
            }

            var t0 = RenderTwin(0);
            var t4 = RenderTwin(4);
            bool ok = t0.All(float.IsFinite) && PeakOf(t0) > 0.01 && t0.SequenceEqual(t4);
            yield return (ok, $"phase5 Twin 10-voice parallel batch parity (0 vs 4 threads bit-identical, peak={PeakOf(t0):0.000})");
        }

        // 3) Consort in True-Poly mode (10 active voices)
        {
            static float[] RenderConsort(int threads)
            {
                using var e = new NotaEngine { RenderThreads = threads };
                e.RenderOffline(new float[512 * 2], 512, Rate);
                e.SetBpm(120);
                int t = e.AddConsortTrack();
                SetParamByName(e, t, -1, "True-Poly", 1.0f);
                int clip = e.AddMidiClip(t, 0.0, 4.0);
                e.SetClipNotes(t, clip, ChordNotes(10, 44));
                e.Seek(0.0);
                e.Play();
                return RenderOfflineMeasured(e, 512, 32).Pcm;
            }

            var c0 = RenderConsort(0);
            var c4 = RenderConsort(4);
            bool ok = c0.All(float.IsFinite) && PeakOf(c0) > 0.01 && c0.SequenceEqual(c4);
            yield return (ok, $"phase5 Consort True-Poly 10-voice parallel batch parity (0 vs 4 threads bit-identical, peak={PeakOf(c0):0.000})");
        }
    }
}
