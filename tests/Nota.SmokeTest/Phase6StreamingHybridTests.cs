// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Phase 6 (#1050, specs/119): Disk-streamed audio sources (`StreamingAudioSource`)
// and low-latency live + anticipative lookahead buffering (`HybridBufferController`).

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class Phase6StreamingHybridTests
{
    private const int Rate = 48000;
    private const int EqKind = 0, ReverbKind = 2;

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"nota-phase6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string longWav = Path.Combine(tmpDir, "stem_45s_stereo.wav");
            WriteLargeStemWav(longWav, 45.0, Rate);

            yield return CheckStreamingMemoryReductionAndParity(longWav);
            yield return CheckRtAsyncStreamingNoUnderruns(longWav);
            yield return CheckAudioImportStreaming(longWav);
            yield return CheckHybridLiveBypassAndLookaheadHits(longWav);
            yield return CheckHybridInvalidationAndParity(longWav);
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }

    private static void WriteLargeStemWav(string path, double seconds, int sr)
    {
        long frames = (long)(seconds * sr);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var w = new BinaryWriter(fs);
        int dataBytes = checked((int)(frames * 4));
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(sr);
        w.Write(sr * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);

        // Write in buffered chunks using an exact phase-continuous sweep + stereo offset.
        var chunk = new byte[16384 * 4];
        long done = 0;
        double phaseL = 0.0, phaseR = 0.0;
        double twoPiOverSr = 2.0 * Math.PI / sr;
        while (done < frames)
        {
            int n = (int)Math.Min(16384, frames - done);
            for (int i = 0; i < n; i++)
            {
                long f = done + i;
                double t = (double)f / sr;
                double freqL = 220.0 + (t * 6.0);
                double freqR = 330.0 + (t * 4.0);
                double env = 0.25 + 0.5 * Math.Abs(Math.Sin(2.0 * Math.PI * 0.25 * t));
                short sL = (short)(Math.Clamp(Math.Sin(phaseL) * env, -1.0, 1.0) * short.MaxValue);
                short sR = (short)(Math.Clamp(Math.Sin(phaseR) * env, -1.0, 1.0) * short.MaxValue);
                phaseL += twoPiOverSr * freqL;
                phaseR += twoPiOverSr * freqR;
                int off = i * 4;
                chunk[off] = (byte)(sL & 0xff);
                chunk[off + 1] = (byte)((sL >> 8) & 0xff);
                chunk[off + 2] = (byte)(sR & 0xff);
                chunk[off + 3] = (byte)((sR >> 8) & 0xff);
            }
            w.Write(chunk, 0, n * 4);
            done += n;
        }
    }

    private static (bool Ok, string Label) CheckStreamingMemoryReductionAndParity(string longWav)
    {
        using var eMem = new NotaEngine { SampleLoadPolicy = SampleLoadPolicy.MemoryOnly, RenderThreads = 0 };
        using var eStrm = new NotaEngine { SampleLoadPolicy = SampleLoadPolicy.Auto, RenderThreads = 4 };
        using var eStrm0 = new NotaEngine { SampleLoadPolicy = SampleLoadPolicy.Auto, RenderThreads = 0 };

        foreach (var e in new[] { eMem, eStrm, eStrm0 })
        {
            e.RenderOffline(new float[512 * 2], 512, Rate);
            e.SetBpm(120);
            e.SetMetronome(false);
        }

        // Default 16 MiB threshold triggers automatically for 45s 48kHz stereo (~16.5 MiB f32).
        int tMem = eMem.AddAudioTrack();
        int cMem0 = eMem.AddAudioClip(tMem, longWav, 0.0);
        int cMem1 = eMem.AddAudioClipEx(tMem, longWav, 4.0, 480000, 96000, 0.75f);
        eMem.SetClipReverse(tMem, cMem1, true);

        int tStrm = eStrm.AddAudioTrack();
        int cStrm0 = eStrm.AddAudioClip(tStrm, longWav, 0.0);
        int cStrm1 = eStrm.AddAudioClipEx(tStrm, longWav, 4.0, 480000, 96000, 0.75f);
        eStrm.SetClipReverse(tStrm, cStrm1, true);

        int tStrm0 = eStrm0.AddAudioTrack();
        int cStrm0_0 = eStrm0.AddAudioClip(tStrm0, longWav, 0.0);
        int cStrm0_1 = eStrm0.AddAudioClipEx(tStrm0, longWav, 4.0, 480000, 96000, 0.75f);
        eStrm0.SetClipReverse(tStrm0, cStrm0_1, true);

        eMem.TryGetAudioClipInfo(tMem, cMem0, out var infoMem);
        eStrm.TryGetAudioClipInfo(tStrm, cStrm0, out var infoStrm);

        bool memIsStrm = eMem.IsSampleStreaming(infoMem.SampleId);
        bool strmIsStrm = eStrm.IsSampleStreaming(infoStrm.SampleId);
        var stMem = eMem.GetStreamingStats();
        var stStrm = eStrm.GetStreamingStats();

        // Peaks & RMS overview parity.
        var pkMem = new float[256 * 2];
        var rmsMem = new float[256];
        var pkStrm = new float[256 * 2];
        var rmsStrm = new float[256];
        int nMem = eMem.GetClipPeaks(tMem, cMem0, pkMem, 256, rmsMem);
        int nStrm = eStrm.GetClipPeaks(tStrm, cStrm0, pkStrm, 256, rmsStrm);
        double maxPeakDiff = 0.0;
        for (int i = 0; i < Math.Min(nMem, nStrm) * 2; i++)
            maxPeakDiff = Math.Max(maxPeakDiff, Math.Abs(pkMem[i] - pkStrm[i]));
        for (int i = 0; i < Math.Min(nMem, nStrm); i++)
            maxPeakDiff = Math.Max(maxPeakDiff, Math.Abs(rmsMem[i] - rmsStrm[i]));

        // ReadSample full extraction parity (for project bundling).
        float[] fullMem = eMem.ReadSample(infoMem.SampleId);
        float[] fullStrm = eStrm.ReadSample(infoStrm.SampleId);
        double maxReadDiff = fullMem.Length == fullStrm.Length ? 0.0 : 1.0;
        if (fullMem.Length == fullStrm.Length)
            for (int i = 0; i < fullMem.Length; i += 17)
                maxReadDiff = Math.Max(maxReadDiff, Math.Abs(fullMem[i] - fullStrm[i]));

        // Offline render across start, mid-file offset+reverse, and seek (0 vs 4 threads vs RAM).
        static float[] BounceSegments(NotaEngine e)
        {
            var dst = new float[32 * 512 * 2];
            var tmp = new float[512 * 2];
            e.Seek(0.0);
            e.Play();
            for (int b = 0; b < 16; b++)
            {
                e.RenderOffline(tmp, 512, Rate);
                tmp.CopyTo(dst, b * 512 * 2);
            }
            e.Seek(4.0);
            for (int b = 16; b < 32; b++)
            {
                e.RenderOffline(tmp, 512, Rate);
                tmp.CopyTo(dst, b * 512 * 2);
            }
            return dst;
        }

        float[] outMem = BounceSegments(eMem);
        float[] outStrm4 = BounceSegments(eStrm);
        float[] outStrm0 = BounceSegments(eStrm0);

        double maxAudioDiff = 0.0;
        double peak = 0.0;
        for (int i = 0; i < outMem.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(outMem[i]));
            maxAudioDiff = Math.Max(maxAudioDiff, Math.Abs(outMem[i] - outStrm4[i]));
            maxAudioDiff = Math.Max(maxAudioDiff, Math.Abs(outStrm0[i] - outStrm4[i]));
        }

        double memSavePct = stMem.ResidentSampleBytes > 0
            ? (1.0 - (double)stStrm.ResidentSampleBytes / stMem.ResidentSampleBytes) * 100.0
            : 0.0;

        bool ok = !memIsStrm && strmIsStrm && stStrm.ActiveStreams >= 1 && memSavePct >= 80.0 &&
                  maxPeakDiff < 1e-5 && maxReadDiff == 0.0 && peak > 0.05 && maxAudioDiff == 0.0;
        return (ok, $"phase6 disk streaming RAM (-{memSavePct:0.0}%, {stStrm.ResidentSampleBytes / 1024} KiB vs {stMem.ResidentSampleBytes / 1024} KiB) & bit-exact parity (diff={maxAudioDiff:0.0e0}, pkDiff={maxPeakDiff:0.0e0})");
    }

    private static (bool Ok, string Label) CheckRtAsyncStreamingNoUnderruns(string longWav)
    {
        using var e = new NotaEngine
        {
            SampleLoadPolicy = SampleLoadPolicy.Auto,
            RenderThreads = 2,
            HybridBuffering = HybridBufferMode.Off
        };
        e.RenderOffline(new float[512 * 2], 512, Rate);
        e.SetBpm(120);
        e.SetMetronome(false);

        int t = e.AddAudioTrack();
        e.AddAudioClip(t, longWav, 0.0);

        // Seek far past the head cache (> 20s) to force async ring streaming in real-time callbacks.
        e.Seek(44.0); // beat 44 @ 120 bpm = 22.0 s into file
        e.Play();
        Thread.Sleep(25); // allow message-thread prime + async prefetch ahead of playhead

        var buf = new float[512 * 2];
        double peak = 0.0;
        for (int b = 0; b < 40; b++)
        {
            e.RenderDeviceBlock(buf, 512);
            foreach (float x in buf) peak = Math.Max(peak, Math.Abs(x));
            if (b % 10 == 0) Thread.Sleep(4);
        }
        e.StopTransport();

        var st = e.GetStreamingStats();
        bool ok = st.ActiveStreams == 1 && st.RtUnderruns == 0 && st.PagesFaulted > 0 && peak > 0.05;
        return (ok, $"phase6 RT async disk streaming zero underruns (faulted={st.PagesFaulted}, async={st.PagesServedAsync}, sync={st.PagesServedSync}, underruns={st.RtUnderruns}, peak={peak:0.000})");
    }

    private static (bool Ok, string Label) CheckAudioImportStreaming(string longWav)
    {
        using var e = new NotaEngine { SampleLoadPolicy = SampleLoadPolicy.Auto, RenderThreads = 2 };
        e.RenderOffline(new float[512 * 2], 512, Rate);
        e.SetBpm(120);
        int t = e.AddAudioTrack();

        using var job = e.OpenAudioImport(longWav);
        if (job == null) return (false, "phase6 AudioImportJob open returned null");
        while (!job.IsDone)
        {
            int rc = job.Step(1 << 16);
            if (rc < 0) return (false, "phase6 AudioImportJob step error");
        }
        var midPeaks = new float[64 * 2];
        int pkCount = job.ReadPeaks(midPeaks, 64);
        int clip = e.AddImportedAudioClip(t, job, 0.0);
        e.TryGetAudioClipInfo(t, clip, out var info);

        bool streamed = e.IsSampleStreaming(info.SampleId);
        e.Seek(10.0);
        e.Play();
        var pcm = new float[512 * 2];
        e.RenderOffline(pcm, 512, Rate);
        double peak = pcm.Max(Math.Abs);

        bool ok = clip >= 0 && pkCount == 64 && streamed && peak > 0.05;
        return (ok, $"phase6 background AudioImportJob streaming (>16 MiB auto-stream={streamed}, peaks={pkCount}, peak={peak:0.000})");
    }

    private static (bool Ok, string Label) CheckHybridLiveBypassAndLookaheadHits(string longWav)
    {
        using var e = new NotaEngine { RenderThreads = 4, HybridBuffering = HybridBufferMode.Auto };
        e.StreamThresholdBytes = 256 * 1024;
        e.RenderOffline(new float[128 * 2], 128, Rate);
        e.SetBpm(120);
        e.SetMetronome(false);

        // Two heavy non-live Arrangement tracks (eligible for anticipative buffering).
        int tInst = e.AddVoltSynthTrack();
        e.AddBuiltinDevice(tInst, EqKind);
        e.AddBuiltinDevice(tInst, ReverbKind);
        int cInst = e.AddMidiClip(tInst, 0.0, 16.0);
        e.SetClipNotes(tInst, cInst,
        [
            new NotaNote(48, 0.0, 4.0, 0.8f),
            new NotaNote(55, 0.0, 4.0, 0.8f),
            new NotaNote(60, 0.0, 4.0, 0.8f)
        ]);

        int tAud = e.AddAudioTrack();
        e.AddAudioClip(tAud, longWav, 0.0);

        // One record-armed live track that MUST bypass anticipative lookahead.
        int tLive = e.AddInstrumentTrack();
        e.SetTrackArmed(tLive, true);

        e.Seek(0.0);
        e.Play();

        var buf = new float[128 * 2];
        e.RenderDeviceBlock(buf, 128); // Block 0 hands off non-live tracks to background lookahead
        Thread.Sleep(20);             // Let background worker pre-render >= 1024-frame chunks

        for (int b = 0; b < 24; b++) e.RenderDeviceBlock(buf, 128);
        var stMid = e.GetHybridStats();

        // Mute the non-live tracks briefly and inject a live note on the armed track: must respond immediately.
        e.SetTrackMute(tInst, true);
        e.SetTrackMute(tAud, true);
        e.NoteOn(72, 0.95f);
        e.RenderDeviceBlock(buf, 128);
        double livePeakImmediate = buf.Max(Math.Abs);
        e.NoteOff(72);

        e.SetTrackMute(tInst, false);
        e.SetTrackMute(tAud, false);
        e.StopTransport();

        bool ok = stMid.Active == 1 && stMid.AnticipativeTracks >= 2 && stMid.LiveTracks >= 1 &&
                  stMid.CacheHits > 0 && livePeakImmediate > 0.01;
        return (ok, $"phase6 hybrid lookahead (hits={stMid.CacheHits}, ant={stMid.AnticipativeTracks}, live={stMid.LiveTracks}, immediateLivePeak={livePeakImmediate:0.000})");
    }

    private static (bool Ok, string Label) CheckHybridInvalidationAndParity(string longWav)
    {
        float[] RunSession(HybridBufferMode mode, out NotaHybridBufferStats stats, out long invAfterEdits)
        {
            using var e = new NotaEngine { RenderThreads = 4, HybridBuffering = mode };
            e.StreamThresholdBytes = 256 * 1024;
            e.RenderOffline(new float[128 * 2], 128, Rate);
            e.SetBpm(120);
            e.SetMetronome(false);

            int t1 = e.AddVoltSynthTrack();
            int eq = e.AddBuiltinDevice(t1, EqKind);
            int c1 = e.AddMidiClip(t1, 0.0, 16.0);
            e.SetClipNotes(t1, c1,
            [
                new NotaNote(45, 0.0, 2.0, 0.8f),
                new NotaNote(52, 0.0, 2.0, 0.8f),
                new NotaNote(57, 0.5, 2.0, 0.7f)
            ]);

            int t2 = e.AddAudioTrack();
            e.AddAudioClip(t2, longWav, 0.0);

            e.Seek(0.0);
            e.Play();

            var pcm = new float[32 * 128 * 2];
            var blk = new float[128 * 2];
            e.RenderDeviceBlock(blk, 128);
            blk.CopyTo(pcm, 0);
            if (mode == HybridBufferMode.Auto) Thread.Sleep(25);

            for (int b = 1; b < 32; b++)
            {
                e.RenderDeviceBlock(blk, 128);
                blk.CopyTo(pcm, b * 128 * 2);
                if (mode == HybridBufferMode.Auto && (b % 6 == 0)) Thread.Sleep(4);
            }
            stats = e.GetHybridStats();

            // Exercise explicit invalidation triggers (seek, device param edit, track arming).
            long inv0 = stats.Invalidations;
            e.Seek(1.5);
            e.RenderDeviceBlock(blk, 128);
            long inv1 = e.GetHybridStats().Invalidations;

            e.DeviceSetParam(t1, eq, 0, 0.65f);
            e.RenderDeviceBlock(blk, 128);
            long inv2 = e.GetHybridStats().Invalidations;

            e.SetTrackArmed(t1, true);
            e.RenderDeviceBlock(blk, 128);
            invAfterEdits = e.GetHybridStats().Invalidations - inv0;
            bool invMonotonic = inv1 > inv0 && inv2 >= inv1;
            if (!invMonotonic) invAfterEdits = -1;

            e.StopTransport();
            return pcm;
        }

        var pcmOff = RunSession(HybridBufferMode.Off, out _, out _);
        var pcmAuto = RunSession(HybridBufferMode.Auto, out var stAuto, out long invDelta);

        double maxDiff = 0.0;
        double peak = 0.0;
        for (int i = 0; i < pcmOff.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(pcmOff[i]));
            maxDiff = Math.Max(maxDiff, Math.Abs(pcmOff[i] - pcmAuto[i]));
        }

        bool ok = stAuto.CacheHits > 0 && invDelta > 0 && pcmAuto.All(float.IsFinite) &&
                  peak > 0.02 && maxDiff < 1e-4;
        return (ok, $"phase6 hybrid invalidation & parity (hits={stAuto.CacheHits}, invDelta={invDelta}, maxDiff={maxDiff:0.0e0}, peak={peak:0.000})");
    }
}
