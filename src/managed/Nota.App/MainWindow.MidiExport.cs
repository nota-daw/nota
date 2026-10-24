// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Nota.Application;
using Nota.Application.Midi;

namespace Nota.App;

public partial class MainWindow
{
    private async Task ExportMidiAsync()
    {
        if (_vm is null) return;
        _editorRoll?.SettlePlacement();

        string projectDir = _vm.Settings.ResolvedProjectsFolder();
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            projectDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Nota Projects");
        Directory.CreateDirectory(projectDir);
        string defaultFileName = "Nota MIDI.mid";

        var availableTracks = new List<MidiExportTrack>();
        for (int i = 0; i < Engine.TrackCount; i++)
        {
            if (!Engine.TryGetTrackInfo(i, out var track)) continue;
            bool hasMidi = false;
            for (int c = 0; c < track.ClipCount; c++)
                if (Engine.TryGetClipInfo(track.Id, c, out var clip) && clip.IsMidi)
                {
                    hasMidi = true;
                    break;
                }
            if (!hasMidi) continue;
            string name = Engine.GetTrackName(track.Id);
            if (string.IsNullOrWhiteSpace(name)) name = $"Track {i + 1}";
            availableTracks.Add(new MidiExportTrack(track.Id, name));
        }

        var exportDialog = new MidiExportWindow(projectDir, defaultFileName, availableTracks);
        bool? dialogResult = await exportDialog.ShowDialog<bool>(this);
        if (dialogResult != true) return;

        var opt = exportDialog.Options;

        var file = new MidiFileData();
        if (opt.ExportTempo)
        {
            foreach (var point in _vm.Transport.TempoPoints)
                file.TempoPoints.Add(new MidiTempoPoint(point.Beat, point.Bpm));
        }

        double endBeat = 0;
        var partsByTrack = new Dictionary<int, MidiFilePart>();
        var trackOrder = new Dictionary<int, int>();
        for (int i = 0; i < Engine.TrackCount; i++)
        {
            if (!Engine.TryGetTrackInfo(i, out var track) || !opt.SelectedTrackIds.Contains(track.Id)) continue;
            for (int c = 0; c < track.ClipCount; c++)
            {
                if (!Engine.TryGetClipInfo(track.Id, c, out var clip) || !clip.IsMidi) continue;
                var notes = Engine.GetClipNotes(track.Id, c);
                var controllers = Engine.GetMidiClipControllers(track.Id, c);
                var selectedControllers = new List<(int Controller, AutomationPoint[] Points)>();
                foreach (int ctl in controllers)
                {
                    if (!opt.SelectedControllers.Contains(ctl)) continue;
                    var points = Engine.GetMidiClipController(track.Id, c, ctl);
                    if (points.Length == 0) continue;
                    selectedControllers.Add((ctl, points.Select(p =>
                        new AutomationPoint(p.Beat + clip.StartBeat, p.Value, p.Curve)).ToArray()));
                    foreach (var point in points) endBeat = Math.Max(endBeat, clip.StartBeat + point.Beat);
                }
                if (notes.Length == 0 && selectedControllers.Count == 0) continue;

                if (!partsByTrack.TryGetValue(track.Id, out var part))
                {
                    string name = Engine.GetTrackName(track.Id);
                    if (string.IsNullOrWhiteSpace(name)) name = $"Track {i + 1}";
                    part = new MidiFilePart
                    {
                        Name = name,
                        IsDrums = name.Contains("drum", StringComparison.OrdinalIgnoreCase)
                    };
                    partsByTrack.Add(track.Id, part);
                    trackOrder.Add(track.Id, i);
                }

                foreach (var note in notes)
                {
                    var shifted = note;
                    shifted.StartBeat += clip.StartBeat;
                    part.Notes.Add(shifted);
                }
                foreach (var (ctl, points) in selectedControllers)
                {
                    if (!part.Controllers.TryGetValue(ctl, out var destination))
                        part.Controllers[ctl] = destination = new List<AutomationPoint>();
                    destination.AddRange(points);
                }
                endBeat = Math.Max(endBeat, clip.StartBeat + clip.LengthBeats);
            }
        }
        foreach (var entry in partsByTrack.OrderBy(pair => trackOrder[pair.Key]))
        {
            entry.Value.Notes.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
            foreach (var points in entry.Value.Controllers.Values)
                points.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            file.Parts.Add(entry.Value);
        }
        if (file.Parts.Count == 0 && file.TempoPoints.Count == 0)
        {
            _vm.StatusText = "Nothing to export — the arrangement has no MIDI clips with notes, controller data, or tempo automation.";
            return;
        }
        if (opt.ExportTracksAsSeparateFiles && file.Parts.Count == 0)
        {
            _vm.StatusText = "Separate MIDI stems require at least one MIDI clip with notes or controller data.";
            return;
        }
        file.LengthBeats = Math.Max(4, endBeat);

        try
        {
            if (opt.ExportTracksAsSeparateFiles)
            {
                string dir = opt.ExportDirectory;
                Directory.CreateDirectory(dir);
                var invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*"));
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int exported = 0;
                foreach (var part in file.Parts)
                {
                    var singleFile = new MidiFileData { LengthBeats = file.LengthBeats };
                    if (opt.ExportTempo) singleFile.TempoPoints.AddRange(file.TempoPoints);
                    singleFile.Parts.Add(part);
                    string stem = new string(part.Name.Select(ch => char.IsControl(ch) || invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
                    if (string.IsNullOrWhiteSpace(stem)) stem = $"Track {exported + 1}";
                    string uniqueStem = stem;
                    int suffix = 2;
                    while (!usedNames.Add(uniqueStem)) uniqueStem = $"{stem} ({suffix++})";
                    string trackFileName = Path.Combine(dir, $"{uniqueStem}.mid");
                    await File.WriteAllBytesAsync(trackFileName, MidiFileWriter.Write(singleFile, opt.Format));
                    exported++;
                }
                _vm.StatusText = $"Exported {exported} MIDI track files to {dir}";
            }
            else
            {
                string outputPath = Path.Combine(opt.ExportDirectory, opt.FileName);
                await File.WriteAllBytesAsync(outputPath, MidiFileWriter.Write(file, opt.Format));
                _vm.StatusText = $"Exported MIDI {Path.GetFileName(outputPath)}";
            }
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"MIDI export failed: {ex.Message}";
        }
    }
}
