// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Phase 2 (#1050): thread-safe SilenceGate across the 10 newly-gated built-in synths,
// idle-clock continuity (Twin/Keys/Mosaic/Rhythm), RackInstrument/DrumRack/RackDevice
// recursive idle propagation, Group/Return ActiveBusMask skipping, and PluginSleepMode C ABI.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class Phase2SleepGateTests
{
    private const int Block = 512, Rate = 48000;
    private const int EqKind = 0, ReverbKind = 2, DelayKind = 3, RackDeviceKind = 5;

    private static float[] RenderBlocks(NotaEngine e, int blocks)
    {
        var all = new float[blocks * Block * 2];
        var buf = new float[Block * 2];
        for (int b = 0; b < blocks; b++)
        {
            e.RenderOffline(buf, Block, Rate);
            buf.CopyTo(all, b * Block * 2);
        }
        return all;
    }

    private static double PeakSlice(float[] pcm, int startBlock, int countBlocks)
    {
        int start = startBlock * Block * 2;
        int end = Math.Min(pcm.Length, (startBlock + countBlocks) * Block * 2);
        double peak = 0.0;
        for (int i = start; i < end; i++)
            peak = Math.Max(peak, Math.Abs(pcm[i]));
        return peak;
    }

    private static NotaEngine BuildAllBuiltInSynthsSession(int threads, PluginSleepMode mode, bool includeClockSynths)
    {
        var e = new NotaEngine { RenderThreads = threads };
        e.RenderOffline(new float[Block * 2], Block, Rate);
        e.SetBpm(120);

        int[] tracks =
        [
            e.AddInstrumentTrack(),        // 0: Synth
            e.AddBassSynthTrack(),         // 7: BassSynth
            e.AddVoltSynthTrack(),         // 6: VoltSynth
            e.AddMonolithTrack(),          // 13: Monolith
            e.AddOperatorSynthTrack(),     // 9: OperatorSynth
            e.AddWavetableSynthTrack(),    // 5: WavetableSynth
            e.AddFluxSynthTrack(),         // 11: FluxSynth
            e.AddPendulumSynthTrack(),     // 8: PendulumSynth
            e.AddPentadTrack(),            // 14: Pentad
            e.AddConsortTrack(),           // 15: Consort
        ];

        for (int i = 0; i < tracks.Length; i++)
        {
            int t = tracks[i];
            e.SetTrackVolume(t, 0.25f);
            e.SetPluginSleepMode(t, -1, mode);
            int clip1 = e.AddMidiClip(t, 0.0, 0.5);
            e.SetClipNotes(t, clip1, [new NotaNote(48 + i, 0.0, 0.25, 0.8f)]);
            int clip2 = e.AddMidiClip(t, 3.0, 0.5);
            e.SetClipNotes(t, clip2, [new NotaNote(60 + i, 0.0, 0.25, 0.8f)]);
        }

        if (includeClockSynths)
        {
            int[] clockTracks = [e.AddTwinTrack(), e.AddKeysTrack(), e.AddRhythmTrack()];
            foreach (int t in clockTracks)
            {
                e.SetTrackVolume(t, 0.25f);
                e.SetPluginSleepMode(t, -1, mode);
                int clip1 = e.AddMidiClip(t, 0.0, 0.5);
                e.SetClipNotes(t, clip1, [new NotaNote(48, 0.0, 0.25, 0.8f)]);
                int clip2 = e.AddMidiClip(t, 3.0, 0.5);
                e.SetClipNotes(t, clip2, [new NotaNote(60, 0.0, 0.25, 0.8f)]);
            }
        }

        e.Seek(0);
        e.Play();
        return e;
    }

    private static (bool ok, string label) CheckSleepModeApiContract()
    {
        using var e = new NotaEngine();
        int t = e.AddBassSynthTrack();
        int eq = e.AddBuiltinDevice(t, EqKind);

        if (e.GetPluginSleepMode(t, -1) != PluginSleepMode.Auto)
            return (false, "default instrument sleep mode is Auto");
        if (e.GetPluginSleepMode(t, eq) != PluginSleepMode.Auto)
            return (false, "default device sleep mode is Auto");

        e.SetPluginSleepMode(t, -1, PluginSleepMode.Never);
        e.SetPluginSleepMode(t, eq, PluginSleepMode.Never);
        if (e.GetPluginSleepMode(t, -1) != PluginSleepMode.Never || e.GetPluginSleepMode(t, eq) != PluginSleepMode.Never)
            return (false, "SetPluginSleepMode(Never) persists on instrument and device");

        e.SetPluginSleepMode(t, -1, PluginSleepMode.Auto);
        e.SetPluginSleepMode(t, eq, PluginSleepMode.Auto);
        if (e.GetPluginSleepMode(t, -1) != PluginSleepMode.Auto || e.GetPluginSleepMode(t, eq) != PluginSleepMode.Auto)
            return (false, "SetPluginSleepMode(Auto) restores default policy");

        bool rejectedTrack = false, rejectedDev = false, rejectedMode = false;
        try { e.SetPluginSleepMode(9999, -1, PluginSleepMode.Never); } catch (NotaEngineException) { rejectedTrack = true; }
        try { e.SetPluginSleepMode(t, 99, PluginSleepMode.Never); } catch (NotaEngineException) { rejectedDev = true; }
        try { e.SetPluginSleepMode(t, -1, (PluginSleepMode)7); } catch (NotaEngineException) { rejectedMode = true; }
        if (!rejectedTrack || !rejectedDev || !rejectedMode)
            return (false, "SetPluginSleepMode rejects invalid track, device index, and mode");

        if (e.GetPluginSleepMode(9999, -1) != PluginSleepMode.Auto || e.GetPluginSleepMode(t, 99) != PluginSleepMode.Auto)
            return (false, "GetPluginSleepMode falls back cleanly to Auto for invalid addresses");

        return (true, "PluginSleepMode C ABI & IAudioEngine contract");
    }

    private static (bool ok, string label) CheckBuiltInSynthsParityAndWake()
    {
        // 120 BPM => 24000 samples/beat. 4 beats (~96000 samples) = ~188 blocks of 512 frames:
        // covers beat 0 note -> tail release -> >=250ms quiet sleep -> beat 3 wake note.
        const int blocks = 190;
        using var serialAuto = BuildAllBuiltInSynthsSession(0, PluginSleepMode.Auto, includeClockSynths: true);
        using var parallelAuto = BuildAllBuiltInSynthsSession(4, PluginSleepMode.Auto, includeClockSynths: true);
        using var serialNever = BuildAllBuiltInSynthsSession(0, PluginSleepMode.Never, includeClockSynths: true);

        float[] a0 = RenderBlocks(serialAuto, blocks);
        float[] a4 = RenderBlocks(parallelAuto, blocks);
        float[] n0 = RenderBlocks(serialNever, blocks);

        if (!a0.All(float.IsFinite) || !n0.All(float.IsFinite))
            return (false, "13 built-in synths remain finite across sleep and wake");
        if (PeakSlice(a0, 0, 20) <= 0.005 || PeakSlice(a0, 140, 40) <= 0.005)
            return (false, $"synths sound on both attack (peak={PeakSlice(a0, 0, 20):0.000}) and post-sleep wake (peak={PeakSlice(a0, 140, 40):0.000})");
        if (!a0.SequenceEqual(a4))
            return (false, "10 newly-gated + 3 clock synths match bit-identically between 0 and 4 helper threads");
        // Before any sleep kicks in (while the beat-0 note is still held), Auto and Never match bit-identically.
        int preSleepSamples = 11 * Block * 2;
        if (!a0.AsSpan(0, preSleepSamples).SequenceEqual(n0.AsSpan(0, preSleepSamples)))
            return (false, "SilenceGate Auto matches Never bit-identically before entering sleep across all 13 built-in synths");

        // Verify per-synth wake + 0-vs-4 MT parity individually across all 10 newly gated synths.
        (string Name, Func<NotaEngine, int> Add)[] gatedSynths =
        [
            ("Synth", e => e.AddInstrumentTrack()),
            ("BassSynth", e => e.AddBassSynthTrack()),
            ("VoltSynth", e => e.AddVoltSynthTrack()),
            ("Monolith", e => e.AddMonolithTrack()),
            ("OperatorSynth", e => e.AddOperatorSynthTrack()),
            ("WavetableSynth", e => e.AddWavetableSynthTrack()),
            ("FluxSynth", e => e.AddFluxSynthTrack()),
            ("PendulumSynth", e => e.AddPendulumSynthTrack()),
            ("Pentad", e => e.AddPentadTrack()),
            ("Consort", e => e.AddConsortTrack()),
        ];
        foreach (var (name, addSynth) in gatedSynths)
        {
            static NotaEngine MakeOne(Func<NotaEngine, int> add, int threads)
            {
                var e = new NotaEngine { RenderThreads = threads };
                e.RenderOffline(new float[Block * 2], Block, Rate);
                e.SetBpm(120);
                int t = add(e);
                int c1 = e.AddMidiClip(t, 0.0, 0.5);
                e.SetClipNotes(t, c1, [new NotaNote(57, 0.0, 0.25, 0.85f)]);
                int c2 = e.AddMidiClip(t, 3.0, 0.5);
                e.SetClipNotes(t, c2, [new NotaNote(69, 0.0, 0.25, 0.85f)]);
                e.Seek(0);
                e.Play();
                return e;
            }
            using var s0 = MakeOne(addSynth, 0);
            using var s4 = MakeOne(addSynth, 4);
            float[] o0 = RenderBlocks(s0, blocks);
            float[] o4 = RenderBlocks(s4, blocks);
            if (!o0.All(float.IsFinite) || PeakSlice(o0, 0, 20) <= 0.005 || PeakSlice(o0, 140, 40) <= 0.005)
                return (false, $"{name} sounds on attack and wakes cleanly after quiet gap");
            if (!o0.SequenceEqual(o4))
                return (false, $"{name} matches bit-identically between 0 and 4 helper threads across sleep/wake");
        }

        // Verify per-synth clean wake on parameter change after settling deep into sleep.
        using var paramWake = new NotaEngine();
        int ops = paramWake.AddOperatorSynthTrack();
        RenderBlocks(paramWake, 48); // > 0.5 s idle -> sleeping
        paramWake.DeviceSetParam(ops, -1, 3, 0.7f);
        int clip = paramWake.AddMidiClip(ops, 0.0, 1.0);
        paramWake.SetClipNotes(ops, clip, [new NotaNote(60, 0.0, 0.5, 0.9f)]);
        paramWake.Seek(0);
        paramWake.Play();
        float[] woken = RenderBlocks(paramWake, 8);
        if (PeakSlice(woken, 0, 8) <= 0.005 || !woken.All(float.IsFinite))
            return (false, "OperatorSynth wakes immediately on param tweak + note after sleep");

        return (true, "all 10 newly-gated + clock synths pass sleep, wake, Auto/Never, and MT parity");
    }

    private static (bool ok, string label) CheckRackAndArpSleep()
    {
        NotaEngine MakeRackSession(int threads, PluginSleepMode mode)
        {
            var e = new NotaEngine { RenderThreads = threads };
            e.RenderOffline(new float[Block * 2], Block, Rate);
            e.SetBpm(120);

            int instRack = e.AddInstrumentRackTrack();
            e.RackAddChain(instRack, 14); // Pentad
            e.RackAddChainDevice(instRack, 0, ReverbKind);
            e.SetPluginSleepMode(instRack, -1, mode);
            int c1 = e.AddMidiClip(instRack, 0.0, 0.5);
            e.SetClipNotes(instRack, c1, [new NotaNote(52, 0.0, 0.25, 0.85f)]);
            int c2 = e.AddMidiClip(instRack, 3.0, 0.5);
            e.SetClipNotes(instRack, c2, [new NotaNote(64, 0.0, 0.25, 0.85f)]);

            int drumRack = e.AddDrumRackTrack();
            int padChain = e.RackAddChain(drumRack, 7); // BassSynth pad
            e.RackSetChainTriggerNote(drumRack, padChain, 36); // C1 trigger note
            e.SetPluginSleepMode(drumRack, -1, mode);
            int d1 = e.AddMidiClip(drumRack, 0.0, 0.5);
            e.SetClipNotes(drumRack, d1, [new NotaNote(36, 0.0, 0.2, 0.9f)]);
            int d2 = e.AddMidiClip(drumRack, 3.0, 0.5);
            e.SetClipNotes(drumRack, d2, [new NotaNote(36, 0.0, 0.2, 0.9f)]);

            int fxRack = e.AddBuiltinDevice(instRack, RackDeviceKind);
            int ch = e.RackDevAddChain(instRack, fxRack, -1);
            e.RackDevAddChainDevice(instRack, fxRack, ch, DelayKind);
            e.SetPluginSleepMode(instRack, fxRack, mode);

            // Track with an Arpeggiator holding a long chord must stay awake cleanly through steps.
            int arpTrack = e.AddInstrumentTrack();
            e.AddMidiEffect(arpTrack, 0); // Arpeggiator
            e.SetPluginSleepMode(arpTrack, -1, mode);
            int a1 = e.AddMidiClip(arpTrack, 0.0, 3.0);
            e.SetClipNotes(arpTrack, a1, [new NotaNote(60, 0.0, 2.5, 0.7f)]);

            e.Seek(0);
            e.Play();
            return e;
        }

        const int blocks = 190;
        using var e0 = MakeRackSession(0, PluginSleepMode.Auto);
        using var e4 = MakeRackSession(4, PluginSleepMode.Auto);
        using var n0 = MakeRackSession(0, PluginSleepMode.Never);

        float[] r0 = RenderBlocks(e0, blocks);
        float[] r4 = RenderBlocks(e4, blocks);
        float[] rn = RenderBlocks(n0, blocks);

        if (!r0.All(float.IsFinite) || PeakSlice(r0, 0, 20) <= 0.005 || PeakSlice(r0, 140, 40) <= 0.005)
            return (false, "RackInstrument, DrumRack, RackDevice, and Arpeggiator sound on attack and wake");
        if (!r0.SequenceEqual(r4))
            return (false, "RackInstrument / DrumRack / RackDevice sleep matches across 0 vs 4 helper threads");
        if (!r0.SequenceEqual(rn))
            return (false, "RackInstrument / DrumRack / RackDevice Auto matches Never sleep mode bit-identically");

        return (true, "RackInstrument, DrumRack, RackDevice, and Arpeggiator sleep & wake parity");
    }

    private static (bool ok, string label) CheckGroupAndReturnBusMask()
    {
        static (NotaEngine e, int idleGroup, int retIdle) MakeBusSession(int threads)
        {
            var e = new NotaEngine { RenderThreads = threads };
            e.RenderOffline(new float[Block * 2], Block, Rate);
            e.SetBpm(120);

            int parentGroup = e.AddGroupTrack();
            int childGroup = e.AddGroupTrack();
            int idleGroup = e.AddGroupTrack();
            e.SetTrackGroup(childGroup, parentGroup);
            e.AddBuiltinDevice(childGroup, EqKind);
            e.AddBuiltinDevice(parentGroup, ReverbKind);
            e.AddBuiltinDevice(idleGroup, EqKind);

            int retActive = e.AddReturnTrack();
            int retIdle = e.AddReturnTrack();
            e.AddBuiltinDevice(retActive, ReverbKind);
            e.AddBuiltinDevice(retIdle, DelayKind);

            int activeInst = e.AddBassSynthTrack();
            e.SetTrackGroup(activeInst, childGroup);
            e.SetTrackSend(activeInst, 0, 0.25f);
            int c1 = e.AddMidiClip(activeInst, 0.0, 0.5);
            e.SetClipNotes(activeInst, c1, [new NotaNote(45, 0.0, 0.3, 0.85f)]);
            int c2 = e.AddMidiClip(activeInst, 3.0, 0.5);
            e.SetClipNotes(activeInst, c2, [new NotaNote(57, 0.0, 0.3, 0.85f)]);

            // A completely silent track sitting inside idleGroup: must never activate idleGroup or retIdle.
            int quietInst = e.AddVoltSynthTrack();
            e.SetTrackGroup(quietInst, idleGroup);

            e.Seek(0);
            e.Play();
            return (e, idleGroup, retIdle);
        }

        const int blocks = 190;
        var (b0, idleGroup, retIdle) = MakeBusSession(0);
        var (b4, _, _) = MakeBusSession(4);
        using var _b0 = b0;
        using var _b4 = b4;

        float[] p0 = RenderBlocks(b0, blocks);
        float[] p4 = RenderBlocks(b4, blocks);
        if (!p0.All(float.IsFinite) || PeakSlice(p0, 0, 20) <= 0.005 || PeakSlice(p0, 140, 40) <= 0.005)
            return (false, "nested Group + Return submix buses render attack and post-sleep wake");
        if (!p0.SequenceEqual(p4))
            return (false, "ActiveBusMask Group/Return skipping matches bit-identically with helper threads");

        var meters = new TrackMeterReading[b0.TrackCount];
        int filled = b0.ReadTrackMeters(meters);
        if (filled != b0.TrackCount)
            return (false, "ReadTrackMeters covers all tracks after Group/Return skip");
        var idleGroupMeter = Array.Find(meters[..filled], m => m.TrackId == idleGroup).Meter;
        var idleReturnMeter = Array.Find(meters[..filled], m => m.TrackId == retIdle).Meter;
        if (idleGroupMeter.PeakL != 0f || idleGroupMeter.PeakR != 0f ||
            idleReturnMeter.PeakL != 0f || idleReturnMeter.PeakR != 0f)
            return (false, $"idle Group/Return meters zeroed while skipped (g={idleGroupMeter.PeakL}, r={idleReturnMeter.PeakL})");

        return (true, "ActiveBusMask nested Group & Return bus skipping parity");
    }

    public static IEnumerable<(bool ok, string label)> Run()
    {
        yield return CheckSleepModeApiContract();
        yield return CheckBuiltInSynthsParityAndWake();
        yield return CheckRackAndArpSleep();
        yield return CheckGroupAndReturnBusMask();
    }
}
