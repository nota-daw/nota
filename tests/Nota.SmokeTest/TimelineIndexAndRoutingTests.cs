// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TimelineIndex O(log N) clip/note/curve queries and RenderRoutingPlan
// O(1)/O(log T) compiled routing, nested group ancestry, sidechain consumer pass, and
// generation-guarded automation / CV / Arp-CC endpoints across 0 vs N worker threads (#1050).

using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class TimelineIndexAndRoutingTests
{
    private const int Block = 512, Rate = 48000;
    private const int EqKind = 0, CompressorKind = 1, DelayKind = 3, RackDeviceKind = 5, VoltKind = 6, ArpKind = 0;

    private static string ShortToneClip()
    {
        string path = Path.Combine(Path.GetTempPath(), "nota-timeline-tone-05s.wav");
        if (!File.Exists(path))
        {
            WavWriter.WriteStereo(path, 0.5, Rate, i =>
            {
                double s = 0.3 * Math.Sin(2.0 * Math.PI * 330.0 * i / Rate);
                return (s, s * 0.75);
            });
        }
        return path;
    }

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

    private static double PeakOf(float[] pcm)
    {
        double peak = 0.0;
        foreach (float x in pcm) peak = Math.Max(peak, Math.Abs(x));
        return peak;
    }

    // Exercise TimelineIndex across 400 audio clips (warped, unwarped, reversed, deactivated,
    // clip volume/pan envelopes) + 400 MIDI clips & 4,000 notes (overlapping long notes,
    // negative track offset, deactivated clip, clip volume envelopes, 2,000-pt automation
    // lane, 1,000-pt CC curve) + loop wrap, asserting non-silence and 0 vs 4 thread parity.
    private static (bool ok, string label) CheckLongTimelineClipAndCurveIndex()
    {
        static NotaEngine MakeLongSession(int threads, string wav)
        {
            var e = new NotaEngine { RenderThreads = threads };
            e.RenderOffline(new float[Block * 2], Block, Rate);
            e.SetBpm(120);
            e.SetMetronome(false);

            int aTrk = e.AddAudioTrack();
            e.SetTrackVolume(aTrk, 0.35f);
            for (int i = 0; i < 400; i++)
            {
                double start = i * 4.0;
                int ac = e.AddAudioClip(aTrk, wav, start);
                if (i % 3 == 0) e.SetClipWarp(aTrk, ac, true, 3);
                if (i % 5 == 0) e.SetClipReverse(aTrk, ac, true);
                if (i % 4 == 0)
                {
                    e.SetClipVolumeEnvelope(aTrk, ac,
                        [new AutomationPoint(0.0, 0.8f), new AutomationPoint(0.5, 0.4f), new AutomationPoint(1.0, 0.9f)]);
                    e.SetClipPanEnvelope(aTrk, ac,
                        [new AutomationPoint(0.0, -0.4f), new AutomationPoint(1.0, 0.4f)]);
                }
                if (i == 200) e.SetClipActive(aTrk, ac, false); // beat 800..801 deactivated
            }

            int mTrk = e.AddVoltSynthTrack();
            e.SetTrackVolume(mTrk, 0.25f);
            e.SetTrackMidiOffsetMs(mTrk, -8.0);
            for (int i = 0; i < 400; i++)
            {
                double start = i * 4.0;
                int mc = e.AddMidiClip(mTrk, start, 3.0);
                var notes = new NotaNote[10];
                for (int k = 0; k < 10; k++)
                    notes[k] = new NotaNote(48 + (k % 12), k * 0.2, 0.35, 0.8f);
                // Sustained note spanning beat start..start+2.5 (overlap into mid-clip seeks).
                if (i % 10 == 0)
                    notes[0] = new NotaNote(36, 0.0, 2.5, 0.9f);
                e.SetClipNotes(mTrk, mc, notes);
                if (i % 4 == 0)
                    e.SetMidiClipEnvelope(mTrk, mc, MidiClipEnvelope.Volume,
                        [new AutomationPoint(0.0, 0.9f), new AutomationPoint(2.5, 0.5f)]);
                if (i == 0)
                {
                    var ccPts = new AutomationPoint[1000];
                    for (int p = 0; p < ccPts.Length; p++)
                        ccPts[p] = new AutomationPoint(p * (3.0 / 1000.0), 0.2f + 0.6f * ((p % 16) / 15f));
                    e.SetMidiClipController(mTrk, mc, 1, ccPts);
                }
                if (i == 200) e.SetClipActive(mTrk, mc, false); // beat 800..803 deactivated
            }

            // 2,000-point automation lane across beats 0..1600 (O(log P) binary search lookup).
            int lane = e.AddAutomationLane(mTrk, AutomationTarget.Volume, -1, -1);
            var lanePts = new AutomationPoint[2000];
            for (int p = 0; p < lanePts.Length; p++)
                lanePts[p] = new AutomationPoint(p * 0.8, 0.25f + 0.15f * MathF.Sin(p * 0.1f));
            e.SetAutomationPoints(mTrk, lane, lanePts);

            return e;
        }

        string wav = ShortToneClip();
        using var e0 = MakeLongSession(0, wav);
        using var e4 = MakeLongSession(4, wav);

        double[] sampleBeats = [0.0, 600.0, 1400.0];
        foreach (double beat in sampleBeats)
        {
            e0.Seek(beat); e0.Play();
            e4.Seek(beat); e4.Play();
            float[] r0 = RenderBlocks(e0, 16);
            float[] r4 = RenderBlocks(e4, 16);
            if (!r0.All(float.IsFinite) || PeakOf(r0) <= 0.01)
                return (false, $"long timeline renders non-silent finite audio at beat {beat}");
            if (!r0.SequenceEqual(r4))
                return (false, $"long timeline bit-identical 0 vs 4 threads at beat {beat}");
        }

        // Deactivated clip window around beat 800 (no other active clips in [800, 800.25]):
        // give 48 release blocks first so any earlier sustained voice decays, then verify beat 800.
        e0.StopTransport(); e4.StopTransport();
        RenderBlocks(e0, 48);
        RenderBlocks(e4, 48);
        e0.Seek(800.0); e0.Play();
        e4.Seek(800.0); e4.Play();
        float[] d0 = RenderBlocks(e0, 12);
        float[] d4 = RenderBlocks(e4, 12);
        if (!d0.All(float.IsFinite) || PeakOf(d0) > 1e-4)
            return (false, $"deactivated clip at beat 800 stays silent (peak={PeakOf(d0):E3})");
        if (!d0.SequenceEqual(d4))
            return (false, "deactivated clip window bit-identical 0 vs 4 threads");

        // Loop seam across clips 12..14 beats.
        e0.SetLoop(true, 12.0, 14.0); e0.Seek(13.8); e0.Play();
        e4.SetLoop(true, 12.0, 14.0); e4.Seek(13.8); e4.Play();
        float[] l0 = RenderBlocks(e0, 32);
        float[] l4 = RenderBlocks(e4, 32);
        if (!l0.All(float.IsFinite) || PeakOf(l0) <= 0.01)
            return (false, "long timeline loop wrap renders finite audio");
        if (!l0.SequenceEqual(l4))
            return (false, "long timeline loop wrap bit-identical 0 vs 4 threads");

        return (true, "TimelineIndex 400-clip / 4000-note / 2000-pt curve & loop parity (0 vs 4 threads)");
    }

    // 64-track session with depth-3 nested Groups, Returns, sidechain compressor consumer,
    // monitor input + arm, and live mid-playback SetTrackMidiSource switch (no graph republish).
    private static (bool ok, string label) CheckLargeRoutingPlanGroupsReturnsMidiAndMonitor()
    {
        const int inFrames = 256;

        static (NotaEngine e, int destInst, int srcB) MakeRoutedSession(int threads)
        {
            var e = new NotaEngine { RenderThreads = threads };
            e.SetMonitorInputForTest(Rate, inFrames);
            e.RenderOffline(new float[Block * 2], Block, Rate);
            e.SetBpm(120);
            e.SetMetronome(false);

            int rootG = e.AddGroupTrack();
            int midG = e.AddGroupTrack();
            int leafG = e.AddGroupTrack();
            e.SetTrackGroup(midG, rootG);
            e.SetTrackGroup(leafG, midG);
            e.AddBuiltinDevice(rootG, EqKind);

            int ret = e.AddReturnTrack();
            e.AddBuiltinDevice(ret, DelayKind);

            int srcA = e.AddBassSynthTrack();
            e.SetTrackMute(srcA, true);
            int ca = e.AddMidiClip(srcA, 0.0, 4.0);
            e.SetClipNotes(srcA, ca, [new NotaNote(45, 0.0, 1.5, 0.9f)]);

            int srcB = e.AddVoltSynthTrack();
            e.SetTrackMute(srcB, true);
            int cb = e.AddMidiClip(srcB, 0.0, 4.0);
            e.SetClipNotes(srcB, cb, [new NotaNote(60, 0.0, 1.5, 0.9f)]);

            int destInst = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(destInst, VoltKind);
            e.SetTrackGroup(destInst, leafG);
            e.SetTrackMidiSource(destInst, srcA);
            e.SetTrackSend(destInst, 0, 0.25f);
            e.SetTrackVolume(destInst, 0.3f);

            int scComp = e.AddBuiltinDevice(leafG, CompressorKind);
            e.SetDeviceSidechainSource(leafG, scComp, srcA);

            int monAudio = e.AddAudioTrack();
            e.SetTrackGroup(monAudio, midG);
            e.SetTrackMonitor(monAudio, true);
            e.SetTrackVolume(monAudio, 0.2f);

            // Pad graph up to ~64 tracks (idle & active) so RenderRoutingPlan O(log T) paths work at scale.
            for (int i = 0; i < 55; i++)
            {
                int filler = (i % 2 == 0) ? e.AddAudioTrack() : e.AddInstrumentTrack();
                e.SetTrackGroup(filler, (i % 3 == 0) ? rootG : -1);
                if (i == 0) e.SetTrackSend(filler, 0, 0.1f);
            }

            e.Seek(0);
            e.Play();
            return (e, destInst, srcB);
        }

        static float[] RenderWithMonitor(NotaEngine e, int blocks, ref long phaseIdx)
        {
            var all = new float[blocks * Block * 2];
            var buf = new float[Block * 2];
            var inBlk = new float[inFrames * 2];
            for (int b = 0; b < blocks; b++)
            {
                for (int p = 0; p < 2; p++)
                {
                    for (int i = 0; i < inFrames; i++)
                    {
                        float s = 0.15f * MathF.Sin(2f * MathF.PI * 220f * (phaseIdx + i) / Rate);
                        inBlk[i * 2] = s;
                        inBlk[i * 2 + 1] = s;
                    }
                    e.PushMonitorFramesForTest(inBlk, inFrames);
                    phaseIdx += inFrames;
                }
                e.RenderOffline(buf, Block, Rate);
                buf.CopyTo(all, b * Block * 2);
            }
            return all;
        }

        var (e0, dest0, srcB0) = MakeRoutedSession(0);
        var (e4, dest4, srcB4) = MakeRoutedSession(4);
        using var _0 = e0;
        using var _4 = e4;

        long ph0 = 0, ph4 = 0;
        float[] seg1_0 = RenderWithMonitor(e0, 16, ref ph0);
        float[] seg1_4 = RenderWithMonitor(e4, 16, ref ph4);
        if (!seg1_0.All(float.IsFinite) || PeakOf(seg1_0) <= 0.01)
            return (false, "64-track nested Group/Return/Sidechain/MIDI-In renders non-silent");
        if (!seg1_0.SequenceEqual(seg1_4))
            return (false, "64-track nested routing matches bit-identically 0 vs 4 threads");

        // Live mid-playback atomic MIDI-In switch (SetTrackMidiSource does not publish a new Graph).
        e0.SetTrackMidiSource(dest0, srcB0);
        e4.SetTrackMidiSource(dest4, srcB4);
        float[] seg2_0 = RenderWithMonitor(e0, 16, ref ph0);
        float[] seg2_4 = RenderWithMonitor(e4, 16, ref ph4);
        if (!seg2_0.All(float.IsFinite) || PeakOf(seg2_0) <= 0.01)
            return (false, "live SetTrackMidiSource switch renders cleanly");
        if (!seg2_0.SequenceEqual(seg2_4))
            return (false, "live SetTrackMidiSource switch matches 0 vs 4 threads bit-identically");

        return (true, "RenderRoutingPlan 64-track hierarchy, sidechain consumer, monitor & live MIDI-In parity");
    }

    // Compiled Automation / CV / Arp-CC targets on top-level and nested Rack targets,
    // including live RackDevAddChain / RackDevRemoveChain mid-playback to bump RackState::generation.
    private static (bool ok, string label) CheckGenerationGuardedCompiledEndpoints()
    {
        static (NotaEngine e, int fxTrack, int fxRack, int rackInst) MakeModSession(int threads)
        {
            var e = new NotaEngine { RenderThreads = threads };
            e.RenderOffline(new float[Block * 2], Block, Rate);
            e.SetBpm(120);
            e.SetMetronome(false);

            // Track 1: Volt + Arp MIDI effect -> Delay FX (CC target) + RackDevice + Modulator CV.
            int fxTrack = e.AddVoltSynthTrack();
            e.SetTrackVolume(fxTrack, 0.25f);
            int c1 = e.AddMidiClip(fxTrack, 0.0, 8.0);
            e.SetClipNotes(fxTrack, c1,
                [new NotaNote(48, 0.0, 4.0, 0.85f), new NotaNote(55, 0.5, 3.5, 0.85f), new NotaNote(60, 1.0, 3.0, 0.85f)]);

            int arp = e.AddMidiEffect(fxTrack, ArpKind);
            int eq = e.AddBuiltinDevice(fxTrack, EqKind);       // 0
            int dl = e.AddBuiltinDevice(fxTrack, DelayKind);    // 1

            // Add top-level Automation, CV link, and Arp-CC targeting Delay, plus Eq->Eq Param CV,
            // then fold Delay into a RackDevice so GroupDevices promotes them to nested addresses.
            int lane = e.AddAutomationLane(fxTrack, AutomationTarget.DeviceParam, dl, 1);
            e.SetAutomationPoints(fxTrack, lane,
                [new AutomationPoint(0.0, 0.2f), new AutomationPoint(4.0, 0.7f)]);
            int mod = e.ModulatorAdd(fxTrack, 0);
            int nestedCv = e.CvLinkAddToTarget(fxTrack, mod, 0, -1, dl, 2);
            if (nestedCv >= 0) e.SetCvLinkDepth(fxTrack, nestedCv, 0.35f);
            e.SetMidiEffectCcDest(fxTrack, arp, dl, 0);
            int paramCv = e.CvLinkAddFromParam(fxTrack, eq, 0, fxTrack, eq, 1);
            if (paramCv >= 0) e.SetCvLinkDepth(fxTrack, paramCv, 0.2f);

            int fxRack = e.GroupDevices(fxTrack, -1, 0, false, dl, dl); // slot 1 = RackDevice

            // Track 2: Instrument Rack with a Synth chain modulated + automated via nested endpoints.
            int rackInst = e.AddInstrumentRackTrack();
            e.RackAddChain(rackInst, VoltKind);
            e.SetTrackVolume(rackInst, 0.25f);
            int c2 = e.AddMidiClip(rackInst, 0.0, 8.0);
            e.SetClipNotes(rackInst, c2, [new NotaNote(64, 0.0, 6.0, 0.8f)]);
            int volLane = e.AddAutomationLane(rackInst, AutomationTarget.Volume, -1, -1);
            e.SetAutomationPoints(rackInst, volLane,
                [new AutomationPoint(0.0, 0.15f), new AutomationPoint(4.0, 0.35f)]);

            e.Seek(0);
            e.Play();
            return (e, fxTrack, fxRack, rackInst);
        }

        var (m0, ft0, fr0, ri0) = MakeModSession(0);
        var (m4, ft4, fr4, ri4) = MakeModSession(4);
        using var _0 = m0;
        using var _4 = m4;

        if (m0.AutomationLanePath(ft0, 0) != (0, 0) ||
            m0.CvLinkPath(ft0, 0).TargetChain != 0 ||
            m0.MidiEffectCcDestPath(ft0, 0) != (0, 0))
            return (false, "GroupDevices folded Automation / CV / Arp-CC targets into nested rack path");

        float[] pre0 = RenderBlocks(m0, 24);
        float[] pre4 = RenderBlocks(m4, 24);
        if (!pre0.All(float.IsFinite) || PeakOf(pre0) <= 0.01)
            return (false, "compiled automation / CV / Arp-CC targets render active signal");
        if (!pre0.SequenceEqual(pre4))
            return (false, "compiled automation / CV / Arp-CC matches 0 vs 4 threads before rack mutation");

        // Mutate rack chains mid-playback (commits new RackState::generation independently of publishRaw)
        // and verify fallback resolution + parity stay exact.
        int extraCh0 = m0.RackDevAddChain(ft0, fr0, -1);
        int extraCh4 = m4.RackDevAddChain(ft4, fr4, -1);
        m0.RackDevAddChainDevice(ft0, fr0, extraCh0, DelayKind);
        m4.RackDevAddChainDevice(ft4, fr4, extraCh4, DelayKind);
        m0.RackAddChain(ri0, VoltKind);
        m4.RackAddChain(ri4, VoltKind);
        m0.RackDevRemoveChain(ft0, fr0, extraCh0);
        m4.RackDevRemoveChain(ft4, fr4, extraCh4);

        float[] post0 = RenderBlocks(m0, 24);
        float[] post4 = RenderBlocks(m4, 24);
        if (!post0.All(float.IsFinite) || PeakOf(post0) <= 0.01)
            return (false, "generation-guarded compiled endpoints survive mid-playback rack swap");
        if (!post0.SequenceEqual(post4))
            return (false, "generation-guarded compiled endpoints match 0 vs 4 threads after rack swap");

        return (true, "Generation-guarded compiled Automation / CV / Arp-CC endpoints parity");
    }

    public static IEnumerable<(bool ok, string label)> Run()
    {
        yield return CheckLongTimelineClipAndCurveIndex();
        yield return CheckLargeRoutingPlanGroupsReturnsMidiAndMonitor();
        yield return CheckGenerationGuardedCompiledEndpoints();
    }
}
