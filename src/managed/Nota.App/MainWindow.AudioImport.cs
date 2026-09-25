// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Background audio import. A long WAV used to freeze the UI while it decoded, tempo-
// detected and time-stretched on the UI thread. Now:
//   1. the target track appears at once (translucent, "Processing…") with a placeholder clip;
//   2. a worker decodes the file block by block — the placeholder's waveform fills in as it
//      goes (instantly when the analysis cache already knows the file) — then detects tempo;
//   3. back on the UI thread the decoded buffer is placed (no disk I/O) and auto-warped with
//      the measured tempo, the warp cache building in small steps so the UI keeps drawing;
//   4. the track turns opaque and is selected.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.App;

public partial class MainWindow
{
    private readonly AudioAnalysisCache _analysis = new();

    // Bumped on New/Open: an import still running belongs to the previous graph (its track id
    // may now name a different track), so it bows out instead of placing its clip.
    private int _importEpoch;

    private const int ImportPeakBuckets = 2048;      // placeholder waveform resolution (as the lane's)
    private const long ImportBlockFrames = 1 << 17;  // ~3 s of 44.1 kHz per decode step
    private const int ImportTickMs = 50;             // placeholder repaint cadence while decoding

    private readonly record struct ImportTick(float[] Peaks, int Count, double Fraction, long TotalFrames, double SampleRate);

    /// <summary>Imports an audio file without blocking the UI. <paramref name="trackId"/> ≤ 0
    /// puts it on a new audio track. Never throws; reports through the status bar.</summary>
    private async Task ImportAudioInBackgroundAsync(string path, int trackId, double beat)
    {
        if (_vm is null) return;
        string name = System.IO.Path.GetFileName(path);
        int epoch = _importEpoch;
        bool ownsTrack = trackId <= 0;
        int t = ownsTrack ? Engine.AddAudioTrack() : trackId;
        var pending = new ArrangementView.PendingImport
        {
            TrackId = t, OwnsTrack = ownsTrack, StartBeat = beat,
            LengthBeats = 4, Label = $"{name} · Processing…",   // real length arrives with the header
        };
        Timeline.AddPendingImport(pending);
        _session?.Refresh();
        _vm.StatusText = $"Importing {name}…";

        // Not disposed: Progress<T> posts ticks asynchronously, so one can still arrive after
        // this method returned — `finished` makes it a no-op instead of touching a dead import.
        var cts = new CancellationTokenSource();
        bool finished = false;
        bool Alive() => epoch == _importEpoch && TrackExists(t);
        var progress = new Progress<ImportTick>(tick =>
        {
            if (finished) return;
            if (!Alive()) { cts.Cancel(); return; }   // track deleted / undone, or another project opened
            if (tick.SampleRate > 0)
                pending.LengthBeats = tick.TotalFrames / tick.SampleRate * (double)_vm.Transport.Bpm / 60.0;
            pending.Peaks = tick.Peaks;
            pending.PeakCount = tick.Count;
            pending.Label = $"{name} · Processing… {tick.Fraction * 100:0} %";
            Timeline.UpdatePendingImport();
        });

        IAudioImport? job = null;
        bool placed = false;
        try
        {
            string? project = _projectPath;
            (job, double bpm) = await Task.Run(() => DecodeImport(path, project, progress, cts.Token));
            if (job is null) { _vm.StatusText = $"Failed to load {name}"; return; }
            if (!Alive()) { _vm.StatusText = $"Import of {name} cancelled"; return; }

            int clip = Engine.AddImportedAudioClip(t, job, beat);
            job.Dispose(); job = null;   // the engine holds its own reference to the buffer
            if (clip < 0) { _vm.StatusText = $"Failed to load {name}"; return; }
            placed = true;
            pending.ClipPlaced = true;

            if (bpm > 0)
            {
                // Warp with the tempo the worker measured, deferring the (heavy) stretch so it
                // can be built in slices below instead of in one UI-thread block.
                Engine.SetDeferWarpBuild(true);
                try { bpm = Engine.AutoWarpClipAtBpm(t, clip, bpm); }
                finally { Engine.SetDeferWarpBuild(false); }
                Timeline.Refresh();
                await BuildImportWarpAsync(name, Alive);
            }
            if (!Alive()) return;

            Timeline.RemovePendingImport(pending);
            Timeline.Select(t, clip);   // the finished import becomes the active track
            _vm.StatusText = bpm > 0
                ? string.Format(NotaNum.Culture, "Imported {0} · warped to tempo ({1:0.0} BPM)", name, bpm)
                : $"Imported {name}";
        }
        catch (OperationCanceledException)
        {
            _vm.StatusText = $"Import of {name} cancelled";
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Failed to load {name}: {ex.Message}";
        }
        finally
        {
            finished = true;
            job?.Dispose();
            Timeline.RemovePendingImport(pending);
            // Nothing landed on a track made just for this import → don't leave an empty row.
            if (!placed && ownsTrack && Alive()) { Engine.RemoveTrack(t); Timeline.Refresh(); }
            _session?.Refresh();
        }
    }

    // Worker thread: fingerprint + cache lookup, block-wise decode with throttled waveform
    // ticks, tempo detection, cache write. Returns (null, 0) when the file can't be decoded.
    private (IAudioImport? job, double bpm) DecodeImport(string path, string? project,
                                                         IProgress<ImportTick> progress, CancellationToken ct)
    {
        string? key = AudioAnalysisCache.Fingerprint(path);
        var cached = key is null ? null : _analysis.TryLoad(key, project);
        var job = Engine.OpenAudioImport(path);
        if (job is null) return (null, 0);
        try
        {
            if (cached is not null) job.SeedPeakTable(cached.PeakTable);
            long total = job.TotalFrames;
            double sr = job.SampleRate;
            void Report()
            {
                var peaks = new float[ImportPeakBuckets * 2];
                int n = job.ReadPeaks(peaks, ImportPeakBuckets);
                double frac = total > 0 ? (double)job.DecodedFrames / total : 0;
                progress.Report(new ImportTick(peaks, n, frac, total, sr));
            }
            Report();
            var sinceTick = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int r = job.Step(ImportBlockFrames);
                if (r < 0) { job.Dispose(); return (null, 0); }
                if (r == 0) break;
                if (sinceTick.ElapsedMilliseconds >= ImportTickMs) { Report(); sinceTick.Restart(); }
            }
            total = job.TotalFrames;   // a truncated file reports fewer frames once done
            Report();

            double bpm = cached?.Bpm ?? job.DetectTempo();
            if (key is not null && cached is null) _analysis.Store(key, new AudioAnalysis(job.ReadPeakTable(), bpm), project);
            return (job, bpm);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    // Build the deferred warp cache in slices on the UI thread, yielding at Background
    // priority between them so input and rendering always go first.
    private async Task BuildImportWarpAsync(string name, Func<bool> alive)
    {
        long total = Engine.WarpBuildRemaining;
        long remaining = total;
        while (remaining > 0 && alive())
        {
            remaining = Engine.WarpBuildStep(48000);
            double frac = total > 0 ? Math.Clamp(1.0 - (double)remaining / total, 0.0, 1.0) : 1.0;
            if (_vm is not null) _vm.StatusText = $"Warping {name}… {frac * 100:0} %";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }

    private bool TrackExists(int trackId)
    {
        int n = Engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return true;
        return false;
    }
}
