// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI file import (.mid from the browser's Files tab or dragged in from Finder / Explorer).
// In the arrangement each note-bearing part of the file becomes a clip: the first lands on
// the dropped-on instrument track, the rest each get a new track below — the same spread the
// multi-file audio drop uses. A session slot takes the whole file merged into one clip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Nota.Application;
using Nota.Application.Midi;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private int ImportMidiFileToArrangement(BrowserItem item, int trackId, double beat)
    {
        if (_vm is null || ReadMidiFile(item) is not { } file) return -1;
        string baseName = Path.GetFileNameWithoutExtension(item.Path);
        bool multi = file.Parts.Count > 1;
        int firstTrack = -1;
        Engine.BeginUndoGroup();   // tracks + clips + notes undo as one step
        try
        {
            for (int i = 0; i < file.Parts.Count; i++)
            {
                var part = file.Parts[i];
                bool reuse = i == 0 && IsPlainInstrumentTrack(trackId);
                int t = reuse ? trackId : AddTrackForMidiPart(part, multi ? PartName(part, i) : baseName);
                if (t <= 0) continue;
                int clip = Engine.AddMidiClip(t, beat, file.LengthBeats);
                if (clip < 0) continue;
                Engine.SetClipNotes(t, clip, part.Notes.ToArray());
                foreach (var (ctl, points) in part.Controllers)
                    Engine.SetMidiClipController(t, clip, ctl, points.ToArray());
                Engine.SetClipName(t, clip, multi ? $"{baseName} · {PartName(part, i)}" : baseName);
                if (firstTrack < 0) firstTrack = t;
            }
        }
        finally { Engine.EndUndoGroup(); }
        ApplyImportedTempo(file);
        if (firstTrack > 0) _lastInstrumentTrackId = firstTrack;
        _vm.StatusText = multi ? $"Imported {item.Name} ({file.Parts.Count} parts)" : $"Imported {item.Name}";
        Timeline.Refresh();
        _session?.Refresh();
        if (_modular?.IsVisible == true) _modular.Refresh();
        return firstTrack;
    }

    private void ImportMidiFileToSlot(BrowserItem item, int trackId, int scene, bool instrumentTrack)
    {
        if (_vm is null) return;
        if (!instrumentTrack) { _vm.StatusText = "Drop MIDI files onto instrument slots."; return; }
        if (ReadMidiFile(item) is not { } file) return;
        Engine.BeginUndoGroup();
        try
        {
            Engine.AddSessionMidiClip(trackId, scene, file.LengthBeats);
            Engine.SetSessionNotes(trackId, scene, file.AllNotes().ToArray());
            var mergedControllers = new SortedDictionary<int, List<AutomationPoint>>();
            foreach (var part in file.Parts)
                foreach (var (ctl, points) in part.Controllers)
                {
                    if (!mergedControllers.TryGetValue(ctl, out var merged))
                        mergedControllers[ctl] = merged = new List<AutomationPoint>();
                    merged.AddRange(points);
                }
            foreach (var (ctl, points) in mergedControllers)
                Engine.SetSessionController(trackId, scene, ctl,
                    points.OrderBy(p => p.Beat).GroupBy(p => p.Beat).Select(g => g.Last()).ToArray());
        }
        finally { Engine.EndUndoGroup(); }
        ApplyImportedTempo(file);
        _vm.StatusText = $"Added {item.Name} to slot";
        Timeline.Refresh();
        _session?.Refresh();
        if (_modular?.IsVisible == true) _modular.Refresh();
    }

    // Parses the file, reporting a bad / empty one in the status bar instead of throwing.
    private MidiFileData? ReadMidiFile(BrowserItem item)
    {
        try
        {
            var file = MidiFileReader.Read(item.Path);
            if (file.Parts.Count > 0) return file;
            _vm!.StatusText = $"{item.Name} has no notes.";
        }
        catch (Exception ex) { _vm!.StatusText = $"Couldn't read {item.Name}: {ex.Message}"; }
        return null;
    }

    private void ApplyImportedTempo(MidiFileData file)
    {
        if (_vm is null || file.TempoPoints.Count == 0) return;
        _vm.Transport.SetTempoPoints(file.TempoPoints.Select(p => new TempoPoint(p.Beat, p.Bpm)).ToArray());
    }

    private static string PartName(MidiFilePart part, int index)
        => part.Name.Length > 0 ? part.Name : $"Part {index + 1}";

    // A drum part (GM channel 10) gets a Rhythm on its factory kit, anything else the default synth.
    private int AddTrackForMidiPart(MidiFilePart part, string name)
    {
        int t;
        if (part.IsDrums)
        {
            t = Engine.AddRhythmTrack();
            _kits.LoadInto(Engine, t, _kits.DefaultRhythmKit, out _);
        }
        else t = Engine.AddInstrumentTrack();
        if (t > 0 && name.Length > 0) Engine.SetTrackName(t, name);
        return t;
    }

    private bool IsPlainInstrumentTrack(int trackId)
    {
        if (trackId <= 0) return false;
        int n = Engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId)
                return ti.IsInstrument && !ti.IsGroup;
        return false;
    }
    private async Task ImportMidiFromPickerAsync()
    {
        if (_vm is null) return;
        Activate();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import MIDI",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("MIDI files") { Patterns = new[] { "*.mid", "*.midi", "*.smf" } } }
        });
        if (files.Count == 0) return;
        string? path = files[0].TryGetLocalPath();
        if (path is null) { _vm.StatusText = "Unsupported file location."; return; }

        MidiFileData midi;
        try { midi = MidiFileReader.Read(path); }
        catch (Exception ex) { _vm.StatusText = $"Couldn't read MIDI: {ex.Message}"; return; }

        var importDialog = new MidiImportWindow(midi);
        bool? result = await importDialog.ShowDialog<bool>(this);
        if (result != true) return;

        var opt = importDialog.Options;

        if (midi.Parts.Count == 0)
        {
            if (opt.ImportTempo && midi.TempoPoints.Count > 0)
                _vm.Transport.SetTempoPoints(midi.TempoPoints.Select(p => new TempoPoint(p.Beat, p.Bpm)).ToArray());
            _vm.StatusText = !opt.ImportTempo
                ? "Tempo import was disabled; there are no note or controller parts to import."
                : midi.TempoPoints.Count > 0
                    ? "Applied the MIDI tempo map; there are no note or controller parts to import."
                    : "This MIDI file contains no importable notes or controllers.";
            return;
        }

        int selectedTrack = Timeline.SelectedTrackId;
        double beat = Math.Max(0, Engine.PositionBeats);
        int firstTrack = -1;
        Engine.BeginUndoGroup();
        try
        {
            if (opt.MergeToSingleTrack)
            {
                var mergedPart = new MidiFilePart { Name = Path.GetFileNameWithoutExtension(path) };
                foreach (var part in midi.Parts)
                {
                    if (part.Channel >= 0 && !opt.SelectedChannels.Contains(part.Channel)) continue;
                    mergedPart.Notes.AddRange(part.Notes);
                    foreach (var (ctl, points) in part.Controllers)
                    {
                        if (opt.SelectedControllers.Contains(ctl))
                        {
                            if (!mergedPart.Controllers.TryGetValue(ctl, out var pts))
                                mergedPart.Controllers[ctl] = pts = new List<AutomationPoint>();
                            pts.AddRange(points);
                        }
                    }
                }
                mergedPart.Notes.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
                int trackId = IsPlainInstrumentTrack(selectedTrack) ? selectedTrack : AddTrackForMidiPart(mergedPart, mergedPart.Name);
                if (trackId > 0)
                {
                    int clip = Engine.AddMidiClip(trackId, beat, midi.LengthBeats);
                    if (clip >= 0)
                    {
                        Engine.SetClipNotes(trackId, clip, mergedPart.Notes.ToArray());
                        foreach (var (ctl, points) in mergedPart.Controllers)
                            Engine.SetMidiClipController(trackId, clip, ctl, points.ToArray());
                        Engine.SetClipName(trackId, clip, mergedPart.Name);
                        firstTrack = trackId;
                    }
                }
            }
            else
            {
                for (int i = 0; i < midi.Parts.Count; i++)
                {
                    var part = midi.Parts[i];
                    if (part.Channel >= 0 && !opt.SelectedChannels.Contains(part.Channel)) continue;
                    bool reuse = i == 0 && IsPlainInstrumentTrack(selectedTrack);
                    int trackId = reuse ? selectedTrack : AddTrackForMidiPart(part, PartName(part, i));
                    if (trackId <= 0) continue;
                    int clip = Engine.AddMidiClip(trackId, beat, midi.LengthBeats);
                    if (clip < 0) continue;
                    Engine.SetClipNotes(trackId, clip, part.Notes.ToArray());
                    foreach (var (ctl, points) in part.Controllers)
                    {
                        if (opt.SelectedControllers.Contains(ctl))
                            Engine.SetMidiClipController(trackId, clip, ctl, points.ToArray());
                    }
                    Engine.SetClipName(trackId, clip, part.Name.Length > 0 ? part.Name : Path.GetFileNameWithoutExtension(path));
                    if (firstTrack < 0) firstTrack = trackId;
                }
            }

            if (opt.ImportTempo && midi.TempoPoints.Count > 0)
            {
                if (opt.TempoMode == TempoImportMode.Replace)
                {
                    _vm.Transport.SetTempoPoints(midi.TempoPoints.Select(p => new TempoPoint(p.Beat, p.Bpm)).ToArray());
                }
                else
                {
                    var current = _vm.Transport.TempoPoints.ToList();
                    foreach (var p in midi.TempoPoints)
                    {
                        int at = current.FindIndex(cp => Math.Abs(cp.Beat - p.Beat) < 1e-4);
                        if (at >= 0) current[at] = new TempoPoint(p.Beat, p.Bpm);
                        else current.Add(new TempoPoint(p.Beat, p.Bpm));
                    }
                    current.Sort((a, b) => a.Beat.CompareTo(b.Beat));
                    _vm.Transport.SetTempoPoints(current);
                }
            }
        }
        finally { Engine.EndUndoGroup(); }

        Timeline.Refresh();
        _session?.Refresh();
        if (_modular?.IsVisible == true) _modular.Refresh();
        if (firstTrack > 0) _lastInstrumentTrackId = firstTrack;
        _vm.StatusText = $"Imported {Path.GetFileName(path)}";
    }

}
