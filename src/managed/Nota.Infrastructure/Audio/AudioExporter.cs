// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.IO;

namespace Nota.Infrastructure;

/// <summary>IAudioExporter: renders the master bus (M6-4) or per-track stems (M6-5)
/// offline to WAV / FLAC / MP3 (the format rides on the request). Runs with the
/// audio backend stopped (exclusive engine access) and restarts it afterwards;
/// the caller suspends the UI clock first.</summary>
public sealed class AudioExporter : IAudioExporter
{
    private const int Chunk = 8192; // must stay <= engine kMaxBlock

    // −1 dBTP normalize target (true-peak ceiling), as a linear amplitude.
    private const float NormalizeTarget = 0.8912509f; // 10^(-1/20)

    public void ExportMaster(IAudioEngine engine, ExportRequest r, IProgress<double>? progress = null)
    {
        int sr = r.SampleRate;
        long totalFrames = FramesFor(r);

        bool wasRunning = engine.IsRunning; // restart afterwards only what was running
        var loop = (engine.LoopStart, engine.LoopEnd);   // the region to put back afterwards
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
                float peak = RenderToTemp(engine, tmp, totalFrames, sr, r.StartBeats, ref firstOverall, progress, 0.0, 0.7);
                Transcode(tmp, r.Path, sr, r, GainFor(peak), progress, 0.7, 0.3);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        else
        {
            RenderTrackRange(engine, r.Path, totalFrames, sr, r, ref firstOverall, progress,
                             0.0, 1.0);
        }

        Restore(engine, r, wasRunning, loop);
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

        bool wasRunning = engine.IsRunning;
        var loop = (engine.LoopStart, engine.LoopEnd);
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
            gain = GainFor(ScanPeak(engine, totalFrames, sr, r.StartBeats, ref firstOverall, progress, 0.0, 0.15));
            stemsBase = 0.15;
        }
        int written = 0;
        int count = tracks.Count;
        double stemsSpan = 1.0 - stemsBase;
        for (int k = 0; k < count; k++)
        {
            for (int j = 0; j < count; j++) engine.SetTrackMute(tracks[j].Id, j != k); // isolate track k
            string label = tracks[k].IsInstrument ? "Inst" : "Audio";
            string file = Path.Combine(r.Path, $"{k + 1:00} {label} {tracks[k].Id}{ExtensionFor(r)}");
            // Each stem occupies an equal slice of the stems progress span.
            RenderTrackRange(engine, file, totalFrames, sr, r, ref firstOverall,
                             progress, stemsBase + stemsSpan * k / count, stemsSpan / count, gain);
            written++;
        }

        for (int j = 0; j < count; j++) engine.SetTrackMute(tracks[j].Id, savedMute[j]); // restore mutes
        Restore(engine, r, wasRunning, loop);
        progress?.Report(1.0);
        return written;
    }

    // Renders `totalFrames` of the current graph to one output file (WAV / FLAC /
    // MP3 per the request). Seeks to the range start and plays (audio clips only render while
    // playing). The first render overall sets the export sample rate; later ones
    // inherit it. Reports progress into [baseFrac, baseFrac+spanFrac] as the render
    // advances (throttled to whole-percent changes).
    private static void RenderTrackRange(IAudioEngine engine, string path, long totalFrames,
                                         int sr, in ExportRequest r, ref bool firstOverall,
                                         IProgress<double>? progress, double baseFrac, double spanFrac,
                                         float gain = 1f)
    {
        engine.StopTransport();
        engine.Seek(r.StartBeats);
        engine.Play();

        var buf = new float[Chunk * 2];
        ISink? sink = null;
        try
        {
            sink = OpenSink(path, sr, in r, gain);
            long remaining = totalFrames;
            long done = 0;
            int lastPct = -1;
            while (remaining > 0)
            {
                int m = (int)System.Math.Min(Chunk, remaining);
                if (firstOverall) { engine.RenderOffline(buf, m, sr); firstOverall = false; } // sets export SR
                else engine.RenderOffline(buf, m);
                sink.Write(buf, m);
                remaining -= m;
                done += m;
                if (progress is not null && totalFrames > 0)
                {
                    int pct = (int)(100.0 * done / totalFrames);
                    if (pct != lastPct) { lastPct = pct; progress.Report(baseFrac + spanFrac * done / totalFrames); }
                }
            }
            sink.Dispose();
        }
        catch
        {
            // Never leave a half-written FLAC/MP3 behind: release the errored encoder
            // without finishing (WAV keeps its historic partial-file behaviour).
            sink?.Fail();
            if (r.Format != ExportFormat.Wav && File.Exists(path))
                try { File.Delete(path); } catch { /* best effort */ }
            throw;
        }
        engine.StopTransport();
    }

    // Renders the current graph once to a temp float32 WAV, returning its true peak.
    // Used by master normalize so the transcode pass scales the exact samples measured.
    private static float RenderToTemp(IAudioEngine engine, string tmpPath, long totalFrames, int sr, double startBeats,
                                      ref bool firstOverall, IProgress<double>? progress,
                                      double baseFrac, double spanFrac)
    {
        engine.StopTransport();
        engine.Seek(startBeats);
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
    // writes the final file in the request's format, applying `gain`. Pure I/O — no
    // engine.
    private static void Transcode(string tmpPath, string outPath, int sr, in ExportRequest r,
                                  float gain, IProgress<double>? progress,
                                  double baseFrac, double spanFrac)
    {
        const int Header = 44;
        using var fs = new FileStream(tmpPath, FileMode.Open, FileAccess.Read);
        long totalFrames = System.Math.Max(0, (fs.Length - Header) / (2 * sizeof(float)));
        fs.Seek(Header, SeekOrigin.Begin);

        var bytes = new byte[Chunk * 2 * sizeof(float)];
        var buf = new float[Chunk * 2];
        ISink? sink = null;
        try
        {
            sink = OpenSink(outPath, sr, in r, gain);
            long done = 0;
            int lastPct = -1;
            while (done < totalFrames)
            {
                int m = (int)System.Math.Min(Chunk, totalFrames - done);
                int want = m * 2 * sizeof(float);
                int got = fs.Read(bytes, 0, want);
                if (got < want) m = got / (2 * sizeof(float));
                System.Buffer.BlockCopy(bytes, 0, buf, 0, m * 2 * sizeof(float));
                sink.Write(buf, m);
                done += m;
                if (progress is not null && totalFrames > 0)
                {
                    int pct = (int)(100.0 * done / totalFrames);
                    if (pct != lastPct) { lastPct = pct; progress.Report(baseFrac + spanFrac * done / totalFrames); }
                }
                if (m == 0) break;
            }
            sink.Dispose();
        }
        catch
        {
            sink?.Fail();
            if (r.Format != ExportFormat.Wav && File.Exists(outPath))
                try { File.Delete(outPath); } catch { /* best effort */ }
            throw;
        }
    }

    // Renders the current graph once without writing, returning its true peak (for
    // normalize). Same render path as the write pass, so the measured peak matches.
    private static float ScanPeak(IAudioEngine engine, long totalFrames, int sr, double startBeats, ref bool firstOverall,
                                  IProgress<double>? progress, double baseFrac, double spanFrac)
    {
        engine.StopTransport();
        engine.Seek(startBeats);
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

    private static string ExtensionFor(in ExportRequest r)
        => r.Format switch { ExportFormat.Flac => ".flac", ExportFormat.Mp3 => ".mp3", _ => ".wav" };

    // --- Output sinks ----------------------------------------------------------
    // One output file, two implementations: WavWriter (gain/dither applied inside
    // it, unchanged M6-4 path) and the native FLAC/MP3 encoder (gain applied to
    // the float buffer here — the ABI takes a plain stream). Dither rides only on
    // 16-bit targets, WAV or FLAC; MP3 has no integer stage.

    private interface ISink : IDisposable
    {
        void Write(float[] interleaved, int frames);
        void Fail();   // failure-path release: no finish, no success semantics
    }

    private sealed class WavSink : ISink
    {
        private readonly WavWriter _w;
        public WavSink(string path, int sr, WavBitDepth depth, float gain, bool dither)
            => _w = new WavWriter(path, sr, 2, depth, gain, dither);
        public void Write(float[] interleaved, int frames) => _w.WriteFrames(interleaved, frames);
        public void Dispose() => _w.Dispose();
        public void Fail() => _w.Dispose();   // historic WAV behaviour: partial file stays
    }

    private sealed class NativeSink : ISink
    {
        private readonly NativeEncoder _e;
        private readonly float _gain;
        public NativeSink(NativeEncoder e, float gain) { _e = e; _gain = gain; }
        public void Write(float[] interleaved, int frames)
        {
            if (_gain != 1f)
                for (int i = 0; i < frames * 2; i++) interleaved[i] *= _gain;
            _e.WriteFrames(interleaved, frames);
        }
        public void Dispose() => _e.Dispose();   // close: flush + finish
        public void Fail() => _e.Discard();      // free without finishing
    }

    private static ISink OpenSink(string path, int sr, in ExportRequest r, float gain)
    {
        switch (r.Format)
        {
            case ExportFormat.Flac:
                return new NativeSink(new NativeEncoder(path, sr, 2, r.Depth,
                                        dither: r.Dither && r.Depth == WavBitDepth.Pcm16), gain);
            case ExportFormat.Mp3:
                return new NativeSink(new NativeEncoder(path, sr, 2, r.Mp3BitrateKbps), gain);
            default:
                return new WavSink(path, sr, r.Depth, gain, r.Dither);
        }
    }

    private static long FramesFor(ExportRequest r)
        => (long)System.Math.Ceiling(r.TotalBeats * (r.SampleRate * 60.0 / r.Bpm));

    private static void Restore(IAudioEngine engine, ExportRequest r, bool wasRunning, (double Start, double End) loop)
    {
        engine.StopTransport();
        engine.Seek(0);
        engine.SetLoop(r.RestoreLoop, loop.Start, loop.End);   // the user's region, not a fixed 1–5 (#102)
        engine.SetMetronome(r.RestoreMetronome);
        // Resume live audio at the device sample rate — only if it was live: an engine that
        // was never started (headless, no output device) has nothing to resume.
        if (wasRunning) engine.Start();
    }
}
