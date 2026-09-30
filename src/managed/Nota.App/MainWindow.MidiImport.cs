// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI file import (.mid from the browser's Files tab or dragged in from Finder / Explorer).
// In the arrangement each note-bearing part of the file becomes a clip: the first lands on
// the dropped-on instrument track, the rest each get a new track below — the same spread the
// multi-file audio drop uses. A session slot takes the whole file merged into one clip.

using System;
using System.IO;
using Nota.Application;
using Nota.Application.Midi;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private void ImportMidiFileToArrangement(BrowserItem item, int trackId, double beat)
    {
        if (_vm is null || ReadMidiFile(item) is not { } file) return;
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
                Engine.SetClipName(t, clip, multi ? $"{baseName} · {PartName(part, i)}" : baseName);
                if (firstTrack < 0) firstTrack = t;
            }
        }
        finally { Engine.EndUndoGroup(); }
        if (firstTrack > 0) _lastInstrumentTrackId = firstTrack;
        _vm.StatusText = multi ? $"Imported {item.Name} ({file.Parts.Count} parts)" : $"Imported {item.Name}";
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
        }
        finally { Engine.EndUndoGroup(); }
        _vm.StatusText = $"Added {item.Name} to slot";
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
}
