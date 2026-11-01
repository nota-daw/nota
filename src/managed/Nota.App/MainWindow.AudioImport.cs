// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Background audio import. A long WAV used to freeze the UI while it decoded, tempo-
// detected and time-stretched on the UI thread. Now:
//   1. the target track appears at once (translucent, "Processing…") with a placeholder clip;
//   2. a worker decodes the file block by block — the placeholder's waveform fills in as it
//      goes (instantly when the analysis cache already knows the file) — then detects tempo;
//   3. back on the UI thread the decoded buffer is placed (no disk I/O) and — if the sample
//      index calls it a loop (or a long take with a tempo) — warped to the project tempo, and
//      optionally transposed to the project key; the warp cache builds on the engine's
//      worker thread so the UI keeps drawing. A one-shot is never warped;
//   4. the track turns opaque and is selected.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Samples;
using Nota.Infrastructure;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private readonly AudioAnalysisCache _analysis = new();
    private ISampleIndex SampleIndex => App.Services.GetRequiredService<ISampleIndex>();

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
            LengthBeats = 4, Label = Loc.F("{0} · Processing…", name),   // real length arrives with the header
        };
        Timeline.AddPendingImport(pending);
        Timeline.Select(t, -1);   // the track it lands on (a new one included) is selected right away
        _session?.Refresh();
        _vm.StatusText = Loc.F("Importing {0}…", name);

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
            pending.Label = Loc.F("{0} · Processing… {1:0} %", name, tick.Fraction * 100);
            Timeline.UpdatePendingImport();
        });

        IAudioImport? job = null;
        bool placed = false;
        try
        {
            string? project = _projectPath;
            (job, double bpm, SampleInfo? info) = await Task.Run(() => DecodeImport(path, project, progress, cts.Token));
            if (job is null) { _vm.StatusText = Loc.F("Failed to load {0}", name); return; }
            if (!Alive()) { _vm.StatusText = Loc.F("Import of {0} cancelled", name); return; }

            int clip = Engine.AddImportedAudioClip(t, job, beat);
            job.Dispose(); job = null;   // the engine holds its own reference to the buffer
            if (clip < 0) { _vm.StatusText = Loc.F("Failed to load {0}", name); return; }
            placed = true;
            pending.ClipPlaced = true;

            var settings = App.Services.GetRequiredService<ISettingsService>().Current;
            double warpBpm = WarpTempoFor(info, bpm, settings.SamplesWarpLoops);
            // Only material that plays in a key moves to it: a hit (an 808 included) would be
            // varispeeded — shorter, brighter, another sound — so one-shots keep their pitch.
            int semis = settings.SamplesMatchKey && _vm.Transport.Key is { } projectKey
                        && info is { Kind: not SampleKind.OneShot, Key: { } sampleKey }
                ? sampleKey.SemitonesTo(projectKey) : 0;
            if (warpBpm > 0 || semis != 0)
            {
                // Warp with the tempo the index / worker measured, deferring the (heavy) stretch
                // to the engine's background build below instead of one UI-thread block. The
                // transpose rides the same deferred build (a warped clip keeps its length).
                Engine.SetDeferWarpBuild(true);
                try
                {
                    if (warpBpm > 0) warpBpm = Engine.AutoWarpClipAtBpm(t, clip, warpBpm);
                    if (semis != 0) Engine.SetClipPitch(t, clip, semis);
                }
                finally { Engine.SetDeferWarpBuild(false); }
                Timeline.Refresh();
                await BuildImportWarpAsync(name, Alive);
            }
            if (!Alive()) return;

            Timeline.RemovePendingImport(pending);
            Timeline.Select(t, clip);   // the finished import becomes the active track
            _vm.StatusText = ImportedText(name, info, warpBpm, semis);
        }
        catch (OperationCanceledException)
        {
            _vm.StatusText = Loc.F("Import of {0} cancelled", name);
        }
        catch (Exception ex)
        {
            _vm.StatusText = Loc.F("Failed to load {0}: {1}", name, ex.Message);
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

    // The tempo to warp a dropped sample to (0 = leave it unwarped). A one-shot never warps;
    // a loop warps at the tempo the index settled (its name, or its length) unless the user
    // turned that off; a long take warps at its heard tempo, as imports always have. With no
    // index entry (the file couldn't be analysed) the worker's measured tempo decides.
    private static double WarpTempoFor(SampleInfo? info, double measured, bool warpLoops) => info switch
    {
        null => measured,
        { Kind: SampleKind.OneShot } => 0,
        { Kind: SampleKind.Loop } => warpLoops ? info.Bpm : 0,
        _ => info.Bpm,
    };

    private string ImportedText(string name, SampleInfo? info, double warpedBpm, int semis)
    {
        var parts = new System.Collections.Generic.List<string> { Loc.F("Imported {0}", name) };
        double project = (double)_vm!.Transport.Bpm;
        if (warpedBpm > 0)
            parts.Add(info is { Kind: SampleKind.Loop, Bpm: > 0 } && Math.Abs(info.Bpm - project) > 0.01
                ? Loc.F("loop warped {0} \u2192 {1}\u2009BPM", SampleInfo.FormatBpm(info.Bpm), SampleInfo.FormatBpm(project))
                : string.Format(NotaNum.Culture, Loc.T("warped to tempo ({0:0.0}\u2009BPM)"), warpedBpm));
        else if (info?.Kind == SampleKind.OneShot) parts.Add(Loc.T("one-shot, not warped"));
        if (semis != 0 && _vm.Transport.Key is { } k)
            parts.Add(Loc.F("transposed {0}\u2009st to {1}", (semis > 0 ? "+" : "\u2212") + Math.Abs(semis), k.Long));
        return string.Join(" · ", parts);
    }

    // Worker thread: fingerprint + cache lookup, block-wise decode with throttled waveform
    // ticks, analysis (via the sample index), cache write. Returns (null, 0, null) when the
    // file can't be decoded.
    private (IAudioImport? job, double bpm, SampleInfo? info) DecodeImport(string path, string? project,
                                                                          IProgress<ImportTick> progress, CancellationToken ct)
    {
        string? key = AudioAnalysisCache.Fingerprint(path);
        var cached = key is null ? null : _analysis.TryLoad(key, project);
        var job = Engine.OpenAudioImport(path);
        if (job is null) return (null, 0, null);
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
                if (r < 0) { job.Dispose(); return (null, 0, null); }
                if (r == 0) break;
                if (sinceTick.ElapsedMilliseconds >= ImportTickMs) { Report(); sinceTick.Restart(); }
            }
            total = job.TotalFrames;   // a truncated file reports fewer frames once done
            Report();

            // The library index usually knows a sample already (the Files tab scanned it); a file
            // from elsewhere is analysed now — from the buffer just decoded — and remembered.
            var index = SampleIndex;
            var info = index.Get(path);
            SampleAnalysis? measured = null;
            if (info is null && (measured = job.Analyze()) is not null) info = index.Store(path, measured);
            double bpm = cached?.Bpm ?? measured?.Bpm ?? job.DetectTempo();
            if (key is not null && cached is null) _analysis.Store(key, new AudioAnalysis(job.ReadPeakTable(), bpm), project);
            return (job, bpm, info);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    // Build the deferred warp cache on the engine's worker thread, polling for progress. The
    // UI thread only installs the finished cache, so a long file no longer stutters the app.
    private async Task BuildImportWarpAsync(string name, Func<bool> alive)
    {
        long total = Engine.WarpBuildRemaining;
        await PollWarpBuildAsync(alive, remaining =>
        {
            double frac = total > 0 ? Math.Clamp(1.0 - (double)remaining / total, 0.0, 1.0) : 1.0;
            if (_vm is not null) _vm.StatusText = Loc.F("Warping {0}… {1:0}\u2009%", name, frac * 100);
        });
    }

    // Drive the background warp build until every stale cache is rebuilt (or alive() turns
    // false). Several loops may run at once (an import + an editor edit): each poll is cheap
    // and idempotent, so they simply share the work.
    private async Task PollWarpBuildAsync(Func<bool>? alive = null, Action<long>? progress = null)
    {
        long remaining;
        while ((remaining = Engine.WarpBuildPoll()) > 0 && (alive?.Invoke() ?? true))
        {
            progress?.Invoke(remaining);
            await Task.Delay(WarpPollMs);
        }
    }

    private const int WarpPollMs = 15;

    private bool TrackExists(int trackId)
    {
        int n = Engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return true;
        return false;
    }
}
