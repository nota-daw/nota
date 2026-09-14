// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private async void OnImportClicked(object? sender, RoutedEventArgs e) => await DoImportAsync();

    private async Task DoImportAsync()
    {
        if (_vm is null) return;
        // Bring the app to the front first: if it isn't the active application when the
        // native file panel opens (common under `dotnet run` / launched from an IDE), the
        // panel can appear unfocused and refuse clicks. Activating makes the window key.
        Activate();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import audio",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Audio") { Patterns = new[] { "*.wav", "*.flac", "*.mp3" } }
            }
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) { _vm.StatusText = "Unsupported file location."; return; }

        string name = System.IO.Path.GetFileName(path);
        // Decode + tempo-detect run on the UI thread (they mutate the engine), but they can be
        // slow for a long file — surface a background strip in the status bar so it never looks
        // frozen. Yield first so the strip paints before the (blocking) engine calls.
        await RunBackgroundAsync($"Importing {name}…", async prog =>
        {
            await Task.Yield();
            int trackId = Engine.AddAudioTrack();
            int clip = Engine.AddAudioClip(trackId, path, 0.0);
            if (clip < 0) { _vm.StatusText = $"Failed to load {name}"; return; }

            prog.Report(ProgressReport.Indeterminate("Analysing tempo…"));
            await Task.Yield();
            double bpm = AutoWarpImported(trackId, clip);
            Timeline.Refresh();
            _vm.StatusText = bpm > 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "Imported {0} · warped to tempo ({1:0.0} BPM)", name, bpm)
                : $"Imported {name}";
        });
    }

    // Auto-warp a freshly imported audio clip so it conforms to the project tempo.
    // Returns the detected BPM (0 when detection fails and the clip is left as-is).
    private double AutoWarpImported(int trackId, int clipIndex)
    {
        try { return clipIndex >= 0 ? Engine.AutoWarpClip(trackId, clipIndex) : 0.0; }
        catch { return 0.0; }
    }

    // --- Export master to WAV (M6-4) ---------------------------------------

    private async Task DoExportAsync()
    {
        if (_vm is null) return;

        double endBeats = ProjectEndBeats();
        if (endBeats <= 0.0)
        {
            _vm.StatusText = "Nothing to export — the arrangement is empty.";
            return;
        }

        double bpm = (double)_vm.Transport.Bpm;
        const double loopBeats = 4 * 4;   // transport loop region (0..4 bars)
        var opts = await new ExportWindow(endBeats, loopBeats, bpm).ShowDialog<ExportOptions?>(this);
        if (opts is null) return;

        Activate(); // keep the native panel focused (see DoImportAsync)
        // Stems bounce one WAV per track into a folder; the master bounce writes one file.
        string? path;
        if (opts.Stems)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Export stems to folder", AllowMultiple = false,
            });
            path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
        else
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export master to WAV",
                DefaultExtension = "wav",
                SuggestedFileName = "Nota Export.wav",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("WAV audio") { Patterns = new[] { "*.wav" } }
                },
            });
            path = file?.TryGetLocalPath();
        }
        if (path is null) return;

        // Stop the audio backend and suspend the UI clock so the render thread
        // has exclusive engine access (see MainWindowViewModel.SuspendEnginePolling).
        // A modal progress dialog blocks the UI for the bounce and shows its progress.
        string noun = opts.Stems ? "stems" : "master";
        _vm.SuspendEnginePolling = true;
        try
        {
            var request = new ExportRequest(
                path, opts.RangeBeats + opts.TailBeats, bpm, opts.SampleRate, opts.Depth,
                RestoreLoop: _vm.Transport.LoopOn, RestoreMetronome: _vm.Transport.MetronomeOn,
                Normalize: opts.Normalize, Dither: opts.Dither);
            await RunBlockingAsync("Export", opts.Stems ? "Exporting stems…" : "Exporting…", async prog =>
            {
                var frac = new Progress<double>(f =>
                    prog.Report(ProgressReport.At(f, $"Exporting {noun}… {f * 100:0}%")));
                if (opts.Stems)
                {
                    int count = await Task.Run(() => _exporter.ExportStems(Engine, request, frac));
                    _vm.StatusText = $"Exported {count} stem{(count == 1 ? "" : "s")} to {System.IO.Path.GetFileName(path)}";
                }
                else
                {
                    await Task.Run(() => _exporter.ExportMaster(Engine, request, frac));
                    _vm.StatusText = $"Exported {System.IO.Path.GetFileName(path)}";
                }
            });
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Export failed: {ex.Message}";
        }
        finally
        {
            _vm.SuspendEnginePolling = false;
        }
    }

    /// <summary>Last clip end across all tracks, in beats (0 if the arrangement is empty).</summary>
    private double ProjectEndBeats()
    {
        double end = 0.0;
        int tracks = Engine.TrackCount;
        for (int i = 0; i < tracks; i++)
        {
            if (!Engine.TryGetTrackInfo(i, out var t)) continue;
            for (int c = 0; c < t.ClipCount; c++)
                if (Engine.TryGetClipInfo(t.Id, c, out var clip))
                    end = Math.Max(end, clip.StartBeat + clip.LengthBeats);
        }
        return end;
    }

    // --- project save / open (M7-6a) ---------------------------------------

    // Folder holding the currently-open `.nota` bundle (null = never saved).
    private string? _projectPath;

    /// <summary>Reflect the open project in the window + title-bar caption
    /// ("Nota — Untitled" until the bundle is named).</summary>
    private void UpdateWindowTitle()
    {
        string name = _projectPath is { Length: > 0 } p
            ? System.IO.Path.GetFileNameWithoutExtension(p.TrimEnd('/', '\\'))
            : "Untitled";
        string title = $"Nota — {name}";
        Title = title;
        DocTitleText.Text = title;   // frameless title-bar caption mirrors the window title
    }

    /// <summary>A crash-recovery snapshot to offer on open (set by App at launch). M7-7.</summary>
    public RecoveryInfo? PendingRecovery { get; init; }

    private void OnMenuNew(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        _links.Clear();    // no live-freeze links (cleared first: stale ids must not hit the new graph)
        Engine.Reset();
        // Reset transport to defaults (these setters drive the engine + UI).
        _vm.Transport.Bpm = 120;
        _vm.Transport.MasterVolume = 1.0;
        _vm.Transport.MetronomeOn = false;
        _vm.Transport.LoopOn = false;
        _vm.Transport.TimeSigNumerator = 4;
        _vm.Transport.TimeSigDenominator = 4;
        _projectPath = null;
        _learn?.Clear();   // start with a clean MIDI-map for the new project
        Timeline.ClearSections();   // …and a clean song structure
        RefreshAfterLoad();
        UpdateWindowTitle();
        _vm.StatusText = "New project.";
    }

    private void OnMenuOpen(object? sender, EventArgs e) => _ = DoOpenAsync();
    private void OnMenuSave(object? sender, EventArgs e) => _ = DoSaveAsync(saveAs: false);
    private void OnMenuSaveAs(object? sender, EventArgs e) => _ = DoSaveAsync(saveAs: true);

    private async Task DoOpenAsync()
    {
        if (_vm is null) return;
        Activate(); // keep the native panel focused (see DoImportAsync)
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Nota project (.nota folder)",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var dir = folders[0].TryGetLocalPath();
        if (dir is null) { _vm.StatusText = "Unsupported project location."; return; }
        OpenProject(dir);
    }

    // Bumped on every load so a warp-cache build from a previous open bows out if the user
    // opens another project before it finished (both loops run on the UI thread).
    private int _warpBuildGen;

    /// <summary>Loads a .nota bundle and applies it to the engine (menu + browser). M7-4b.</summary>
    private void OpenProject(string dir)
    {
        if (_vm is null) return;
        _warpBuildGen++;
        Engine.StopPreview();
        try
        {
            // Defer the (heavy) warp-clip stretching so applying the project is fast — the
            // arrangement appears at once and the caches then fill in the background below.
            Engine.SetDeferWarpBuild(true);
            ProjectLoadResult result;
            try { result = _projects.Load(Engine, dir); }
            finally { Engine.SetDeferWarpBuild(false); }
            _links.Clear();   // the old project's live-freeze ids mean nothing in the loaded graph
            // Load resets the engine but leaves transport; restore it via the VM.
            _vm.Transport.Bpm = (decimal)result.Transport.Bpm;
            _vm.Transport.MasterVolume = result.Transport.MasterVolume;
            _vm.Transport.MetronomeOn = result.Transport.MetronomeOn;
            _vm.Transport.LoopOn = result.Transport.LoopOn;
            _vm.Transport.TimeSigNumerator = result.Transport.TimeSigNumerator;
            _vm.Transport.TimeSigDenominator = result.Transport.TimeSigDenominator;
            _projectPath = dir;
            _learn?.LoadMappings(dir);   // MIDI-learn mappings ride in the bundle sidecar
            _modular?.LoadLayout(dir);   // modular-editor node/island positions (sidecar)
            Timeline.LoadSections(dir);  // arrangement song sections (sidecar)
            LoadFreezeLinks(dir);        // live-freeze links (v1.1) — re-sleep linked sources
            RecordRecentProject(dir);    // surface it on the welcome screen next launch
            RefreshAfterLoad();
            UpdateWindowTitle();
            var warnings = result.Warnings;
            App.Services.GetRequiredService<ILogSink>()
                .Info($"Opened project '{System.IO.Path.GetFileName(dir)}' · {warnings.Count} warning(s)");
            _vm.StatusText = warnings.Count == 0
                ? $"Opened {System.IO.Path.GetFileName(dir)}"
                : $"Opened {System.IO.Path.GetFileName(dir)} — {warnings.Count} item(s) downgraded: {warnings[0]}";
            _ = BuildWarpCachesAsync();   // fill the deferred warp caches in the background
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Open project failed", ex);
            _vm.StatusText = $"Open failed: {ex.Message}";
        }
    }

    // Fill the warp-clip caches deferred during load, incrementally on the UI thread so the
    // status-bar strip advances and the app stays responsive. Warped clips are silent until
    // their cache lands (a few seconds for a big project); everything else plays immediately.
    private async Task BuildWarpCachesAsync()
    {
        if (_vm is null) return;
        int gen = _warpBuildGen;
        long total = Engine.WarpBuildRemaining;
        if (total <= 0) return;
        await RunBackgroundAsync("Preparing warped clips…", async prog =>
        {
            // ~a few ms of stretching per step keeps each UI frame short; yield so it paints.
            const int chunk = 48000;   // device frames per step
            long remaining = total;
            while (remaining > 0 && gen == _warpBuildGen)   // bail if another project opened
            {
                remaining = Engine.WarpBuildStep(chunk);
                double frac = Math.Clamp(1.0 - (double)remaining / total, 0.0, 1.0);
                prog.Report(ProgressReport.At(frac, $"Preparing warped clips… {frac * 100:0}%"));
                await Task.Yield();
            }
        });
    }

    private async Task DoSaveAsync(bool saveAs)
    {
        if (_vm is null) return;

        string? dir = _projectPath;
        if (saveAs || dir is null)
        {
            Activate();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Nota project",
                DefaultExtension = "nota",
                SuggestedFileName = "Untitled.nota",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Nota project") { Patterns = new[] { "*.nota" } }
                },
            });
            var chosen = file?.TryGetLocalPath();
            if (chosen is null) return;
            if (!chosen.EndsWith(".nota", StringComparison.OrdinalIgnoreCase)) chosen += ".nota";
            // The save panel may drop an empty placeholder file at the path; the
            // bundle is a folder, so clear it before writing.
            if (System.IO.File.Exists(chosen)) System.IO.File.Delete(chosen);
            dir = chosen;
        }

        try
        {
            var warnings = _projects.Save(Engine, TransportSnapshot(), dir);
            _learn?.SaveMappings(dir);   // MIDI-learn mappings ride in the bundle sidecar
            _modular?.SaveLayout(dir);   // modular-editor node/island positions (sidecar)
            Timeline.SaveSections(dir);  // arrangement song sections (sidecar)
            SaveFreezeLinks(dir);        // live-freeze links (v1.1) ride in the bundle sidecar
            _projectPath = dir;
            RecordRecentProject(dir);    // surface it on the welcome screen next launch
            UpdateWindowTitle();
            App.Services.GetRequiredService<ILogSink>()
                .Info($"Saved project '{System.IO.Path.GetFileName(dir)}' · {warnings.Count} warning(s)");
            _vm.StatusText = warnings.Count == 0
                ? $"Saved {System.IO.Path.GetFileName(dir)}"
                : $"Saved {System.IO.Path.GetFileName(dir)} — {warnings.Count} unsupported item(s) skipped: {warnings[0]}";
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Save project failed", ex);
            _vm.StatusText = $"Save failed: {ex.Message}";
        }
    }

    // A load/new swaps the whole graph and invalidates old track ids, so clear
    // per-clip editor state and rebuild every view (cf. RefreshAfterUndoRedo).
    private void RefreshAfterLoad()
    {
        _editorRoll = null;
        _clipEditor = null;
        _editorTrackId = -1;
        _editorClipIndex = -1;
        DetailPanel.IsVisible = false;
        _deviceChain?.ForgetCardState();   // per-track preset labels belong to the old project
        Timeline.Refresh();
        _session?.Refresh();
    }
}
