// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.IO;

namespace Nota.Infrastructure;

/// <summary>IAudioExporter: renders the master bus (M6-4) or per-track stems (M6-5)
/// offline to WAV. Runs with the audio backend stopped (exclusive engine access)
/// and restarts it afterwards; the caller suspends the UI clock first.</summary>
public sealed class WavAudioExporter : IAudioExporter
{
    private const int Chunk = 8192; // must stay <= engine kMaxBlock

    // −1 dBTP normalize target (true-peak ceiling), as a linear amplitude.
    private const float NormalizeTarget = 0.8912509f; // 10^(-1/20)

    public void ExportMaster(IAudioEngine engine, ExportRequest r, IProgress<double>? progress = null)
    {
        int sr = r.SampleRate;
        long totalFrames = FramesFor(r);

        engine.Stop();               // halt the audio backend: no audio thread during render
        engine.SetLoop(false, 0, 0); // don't wrap; render the whole range once
        engine.SetMetronome(false);  // keep clicks out of the bounce
        engine.StopTransport();

        bool firstOverall = true;
        if (r.Normalize)
        {
            // Normalize needs the whole-render true peak before it can pick the make-up
            // gain, and the engine's render isn't guaranteed bit-identical across two
            // passes — so render ONCE to a temp float file (scanning the true peak as we
            // go), then transcode those exact samples with the gain applied. This can't
            // overshoot the ceiling the way a scan-then-re-render pass could.
            string tmp = r.Path + ".norm.tmp";
            try
            {
                float peak = RenderToTemp(engine, tmp, totalFrames, sr, ref firstOverall, progress, 0.0, 0.7);
                Transcode(tmp, r.Path, sr, r.Depth, GainFor(peak), r.Dither, progress, 0.7, 0.3);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        else
        {
            RenderTrackRange(engine, r.Path, totalFrames, sr, r.Depth, ref firstOverall, progress,
                             0.0, 1.0, 1f, r.Dither);
        }

        Restore(engine, r);
        progress?.Report(1.0);
    }

    public int ExportStems(IAudioEngine engine, ExportRequest r, IProgress<double>? progress = null)
    {
        int sr = r.SampleRate;
        long totalFrames = FramesFor(r);

        // Non-return tracks are the stem targets; returns fold into each stem via
        // sends (they're exempt from the solo/mute gate).
        var tracks = new List<NotaTrackInfo>();
        int n = engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && !ti.IsReturn) tracks.Add(ti);

        var savedMute = new bool[tracks.Count];
        for (int i = 0; i < tracks.Count; i++) savedMute[i] = tracks[i].Muted != 0;

        engine.Stop();
        engine.SetLoop(false, 0, 0);
        engine.SetMetronome(false);
        engine.StopTransport();

        Directory.CreateDirectory(r.Path);
        bool firstOverall = true;
        // Normalize applies one shared make-up gain (from the full-mix true peak) to every
        // stem, so the stems still sum to a −1 dBTP master and keep their relative balance.
        float gain = 1f;
        double stemsBase = 0.0;
        if (r.Normalize)
        {
            gain = GainFor(ScanPeak(engine, totalFrames, sr, ref firstOverall, progress, 0.0, 0.15));
            stemsBase = 0.15;
        }
        int written = 0;
        int count = tracks.Count;
        double stemsSpan = 1.0 - stemsBase;
        for (int k = 0; k < count; k++)
        {
            for (int j = 0; j < count; j++) engine.SetTrackMute(tracks[j].Id, j != k); // isolate track k
            string label = tracks[k].IsInstrument ? "Inst" : "Audio";
            string file = Path.Combine(r.Path, $"{k + 1:00} {label} {tracks[k].Id}.wav");
            // Each stem occupies an equal slice of the stems progress span.
            RenderTrackRange(engine, file, totalFrames, sr, r.Depth, ref firstOverall,
                             progress, stemsBase + stemsSpan * k / count, stemsSpan / count, gain, r.Dither);
            written++;
        }

        for (int j = 0; j < count; j++) engine.SetTrackMute(tracks[j].Id, savedMute[j]); // restore mutes
        Restore(engine, r);
        progress?.Report(1.0);
        return written;
    }

    // Renders `totalFrames` of the current graph to one WAV. Seeks to 0 and plays
    // (audio clips only render while playing). The first render overall sets the
    // export sample rate; later ones inherit it. Reports progress into [baseFrac,
    // baseFrac+spanFrac] as the render advances (throttled to whole-percent changes).
    private static void RenderTrackRange(IAudioEngine engine, string path, long totalFrames,
                                         int sr, WavBitDepth depth, ref bool firstOverall,
                                         IProgress<double>? progress, double baseFrac, double spanFrac,
                                         float gain = 1f, bool dither = false)
    {
        engine.StopTransport();
        engine.Seek(0);
        engine.Play();

        var buf = new float[Chunk * 2];
        using var wav = new WavWriter(path, sr, 2, depth, gain, dither);
        long remaining = totalFrames;
        long done = 0;
        int lastPct = -1;
        while (remaining > 0)
        {
            int m = (int)System.Math.Min(Chunk, remaining);
            if (firstOverall) { engine.RenderOffline(buf, m, sr); firstOverall = false; } // sets export SR
            else engine.RenderOffline(buf, m);
            wav.WriteFrames(buf, m);
            remaining -= m;
            done += m;
            if (progress is not null && totalFrames > 0)
            {
                int pct = (int)(100.0 * done / totalFrames);
                if (pct != lastPct) { lastPct = pct; progress.Report(baseFrac + spanFrac * done / totalFrames); }
            }
        }
        engine.StopTransport();
    }

    // Renders the current graph once to a temp float32 WAV, returning its true peak.
    // Used by master normalize so the transcode pass scales the exact samples measured.
    private static float RenderToTemp(IAudioEngine engine, string tmpPath, long totalFrames, int sr,
                                      ref bool firstOverall, IProgress<double>? progress,
                                      double baseFrac, double spanFrac)
    {
        engine.StopTransport();
        engine.Seek(0);
        engine.Play();

        var scanner = new TruePeakScanner();
        var buf = new float[Chunk * 2];
        using var wav = new WavWriter(tmpPath, sr, 2, WavBitDepth.Float32);
        long remaining = totalFrames;
        long done = 0;
        int lastPct = -1;
        while (remaining > 0)
        {
            int m = (int)System.Math.Min(Chunk, remaining);
            if (firstOverall) { engine.RenderOffline(buf, m, sr); firstOverall = false; }
            else engine.RenderOffline(buf, m);
            scanner.Feed(buf, m);
            wav.WriteFrames(buf, m);
            remaining -= m;
            done += m;
            if (progress is not null && totalFrames > 0)
            {
                int pct = (int)(100.0 * done / totalFrames);
                if (pct != lastPct) { lastPct = pct; progress.Report(baseFrac + spanFrac * done / totalFrames); }
            }
        }
        engine.StopTransport();
        return scanner.Peak;
    }

    // Reads a temp float32 WAV (written by RenderToTemp, canonical 44-byte header) and
    // writes the final WAV applying `gain` (+ optional dither). Pure I/O — no engine.
    private static void Transcode(string tmpPath, string outPath, int sr, WavBitDepth depth,
                                  float gain, bool dither, IProgress<double>? progress,
                                  double baseFrac, double spanFrac)
    {
        const int Header = 44;
        using var fs = new FileStream(tmpPath, FileMode.Open, FileAccess.Read);
        long totalFrames = System.Math.Max(0, (fs.Length - Header) / (2 * sizeof(float)));
        fs.Seek(Header, SeekOrigin.Begin);

        var bytes = new byte[Chunk * 2 * sizeof(float)];
        var buf = new float[Chunk * 2];
        using var wav = new WavWriter(outPath, sr, 2, depth, gain, dither);
        long done = 0;
        int lastPct = -1;
        while (done < totalFrames)
        {
            int m = (int)System.Math.Min(Chunk, totalFrames - done);
            int want = m * 2 * sizeof(float);
            int got = fs.Read(bytes, 0, want);
            if (got < want) m = got / (2 * sizeof(float));
            System.Buffer.BlockCopy(bytes, 0, buf, 0, m * 2 * sizeof(float));
            wav.WriteFrames(buf, m);
            done += m;
            if (progress is not null && totalFrames > 0)
            {
                int pct = (int)(100.0 * done / totalFrames);
                if (pct != lastPct) { lastPct = pct; progress.Report(baseFrac + spanFrac * done / totalFrames); }
            }
            if (m == 0) break;
        }
    }

    // Renders the current graph once without writing, returning its true peak (for
    // normalize). Same render path as the write pass, so the measured peak matches.
    private static float ScanPeak(IAudioEngine engine, long totalFrames, int sr, ref bool firstOverall,
                                  IProgress<double>? progress, double baseFrac, double spanFrac)
    {
        engine.StopTransport();
        engine.Seek(0);
        engine.Play();

        var scanner = new TruePeakScanner();
        var buf = new float[Chunk * 2];
        long remaining = totalFrames;
        long done = 0;
        int lastPct = -1;
        while (remaining > 0)
        {
            int m = (int)System.Math.Min(Chunk, remaining);
            if (firstOverall) { engine.RenderOffline(buf, m, sr); firstOverall = false; } // sets export SR
            else engine.RenderOffline(buf, m);
            scanner.Feed(buf, m);
            remaining -= m;
            done += m;
            if (progress is not null && totalFrames > 0)
            {
                int pct = (int)(100.0 * done / totalFrames);
                if (pct != lastPct) { lastPct = pct; progress.Report(baseFrac + spanFrac * done / totalFrames); }
            }
        }
        engine.StopTransport();
        return scanner.Peak;
    }

    // Make-up gain that lifts (or lowers) `peak` to the −1 dBTP target. Silence → unity.
    private static float GainFor(float peak)
        => peak > 1e-6f ? NormalizeTarget / peak : 1f;

    private static long FramesFor(ExportRequest r)
        => (long)System.Math.Ceiling(r.TotalBeats * (r.SampleRate * 60.0 / r.Bpm));

    private static void Restore(IAudioEngine engine, ExportRequest r)
    {
        engine.StopTransport();
        engine.Seek(0);
        engine.SetLoop(r.RestoreLoop, 0, 16);
        engine.SetMetronome(r.RestoreMetronome);
        engine.Start();              // resume live audio at the device sample rate
    }
}
