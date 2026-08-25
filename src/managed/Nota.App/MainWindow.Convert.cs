// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio clip → MIDI conversion ("Convert / Slice to New MIDI Track").
// Phase 1: Convert Drums + Slice, both onto a new Drum Rack. The clip's audio is pulled
// mono via GetClipAudioMono, analysed by AudioOnsets (pure permissive DSP), sliced to temp
// WAVs and loaded onto rack pads; a MIDI clip triggers them under the source clip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.App;

public partial class MainWindow
{
    // Audio → MIDI blocks the UI (it mutates the graph), so it runs behind a modal progress
    // dialog. The heavy analysis (onset/pitch/harmony DSP) runs on a background thread; the
    // engine mutations resume on the UI thread once the awaited analysis returns.
    private async void OnConvertClip(int trackId, int clipIndex, ClipConvertMode mode)
    {
        if (_vm is null) return;
        float[] mono;
        double sr, startBeat = 0, clipBeats = 0;
        try
        {
            int n = Engine.GetClipAudioMono(trackId, clipIndex, null, out sr);
            if (n <= 0 || sr <= 0) { _vm.StatusText = "This clip has no audio to convert."; return; }
            mono = new float[n];
            Engine.GetClipAudioMono(trackId, clipIndex, mono, out sr);

            // Map onsets onto the clip's *displayed* beat length, which already accounts for warp/
            // tempo (a re-warped clip spans a different number of beats). Linear source→beats within
            // the clip; falls back to raw source time at the project tempo when the length is unknown.
            if (Engine.TryGetClipInfo(trackId, clipIndex, out var ci)) { startBeat = ci.StartBeat; clipBeats = ci.LengthBeats; }
            if (clipBeats <= 0) clipBeats = n / sr * (double)_vm.Transport.Bpm / 60.0;
        }
        catch (Exception ex) { _vm.StatusText = $"Convert failed: {ex.Message}"; return; }

        try
        {
            await RunBlockingAsync("Convert to MIDI", "Analysing…", async prog =>
            {
                switch (mode)
                {
                    case ClipConvertMode.Drums:   await ConvertDrumsAsync(mono, sr, startBeat, clipBeats, prog); break;
                    case ClipConvertMode.Slice:   await SliceToMidiAsync(mono, sr, startBeat, clipBeats, prog); break;
                    case ClipConvertMode.Melody:  await ConvertMelodyAsync(mono, sr, startBeat, clipBeats, prog); break;
                    case ClipConvertMode.Harmony: await ConvertHarmonyAsync(mono, sr, startBeat, clipBeats, prog); break;
                    default: _vm.StatusText = "That converter isn't available yet."; break;
                }
            });
        }
        catch (Exception ex) { _vm.StatusText = $"Convert failed: {ex.Message}"; }
    }

    // Slice: chop the audio at onsets (or an even 16-slice grid), one pad per slice on ascending
    // notes from C1, and a MIDI clip that replays them in order.
    private async Task SliceToMidiAsync(float[] mono, double sr, double startBeat, double clipBeats, IProgress<ProgressReport> prog)
    {
        prog.Report(ProgressReport.Indeterminate("Detecting transients…"));
        var cuts = await Task.Run(() =>
        {
            var c = AudioOnsets.Detect(mono, sr);
            if (c.Count == 0 || c[0] > (int)(sr * 0.01)) c.Insert(0, 0);
            if (c.Count < 2)   // too few transients → even grid
            {
                c.Clear();
                for (int i = 0; i < 16; i++) c.Add((int)((long)mono.Length * i / 16));
            }
            return c;
        });

        const int firstNote = 36, maxPads = 64;
        int minSlice = (int)(sr * 0.005);
        double toBeat = mono.Length > 0 ? clipBeats / mono.Length : 0;   // source frame → clip beat
        int dr = Engine.AddDrumRackTrack();
        var notes = new List<NotaNote>();
        int pads = 0;
        for (int i = 0; i < cuts.Count && pads < maxPads; i++)
        {
            int a = cuts[i];
            int b = (i + 1 < cuts.Count) ? cuts[i + 1] : mono.Length;
            if (b - a < minSlice) continue;
            int note = firstNote + pads;
            if (LoadPad(dr, mono, sr, a, b, note) < 0) continue;
            notes.Add(new NotaNote(note, a * toBeat, Math.Max(0.05, (b - a) * toBeat), 0.9f));
            pads++;
            prog.Report(ProgressReport.At((double)(i + 1) / cuts.Count, $"Slicing pads… {pads}"));
        }
        FinishConvert(dr, startBeat, clipBeats, notes, pads == 0 ? "Nothing to slice." : $"Sliced into {pads} pads");
    }

    // Convert Drums: classify onsets into kick/snare/hat, voice each class with its strongest hit as
    // a pad, and place a MIDI note per onset at the class's pad note.
    private async Task ConvertDrumsAsync(float[] mono, double sr, double startBeat, double clipBeats, IProgress<ProgressReport> prog)
    {
        prog.Report(ProgressReport.Indeterminate("Detecting hits…"));
        var (onsets, cls, vel) = await Task.Run(() =>
        {
            var on = AudioOnsets.Detect(mono, sr);
            var cl = new DrumClass[on.Count];
            var ve = new float[on.Count];
            for (int i = 0; i < on.Count; i++)
            {
                cl[i] = AudioOnsets.Classify(mono, sr, on[i]);
                ve[i] = AudioOnsets.Strength(mono, sr, on[i]);
            }
            return (on, cl, ve);
        });

        double toBeat = mono.Length > 0 ? clipBeats / mono.Length : 0;   // source frame → clip beat
        double noteLen = Math.Min(0.1, clipBeats);
        // Per class: its GM-ish pad note + the strongest onset index (to voice the pad).
        var padNote = new Dictionary<DrumClass, int> { [DrumClass.Kick] = 36, [DrumClass.Snare] = 38, [DrumClass.Hat] = 42 };
        var best = new Dictionary<DrumClass, (int onset, float str)>();
        for (int i = 0; i < onsets.Count; i++)
            if (!best.TryGetValue(cls[i], out var cur) || vel[i] > cur.str) best[cls[i]] = (onsets[i], vel[i]);

        prog.Report(ProgressReport.Indeterminate("Building pads…"));
        int dr = Engine.AddDrumRackTrack();
        // Build one pad per class that occurred, voiced by its strongest hit.
        var loaded = new HashSet<DrumClass>();
        foreach (var (c, info) in best)
        {
            int a = info.onset, b = Math.Min(mono.Length, a + (int)(sr * 0.20));
            if (LoadPad(dr, mono, sr, a, b, padNote[c]) >= 0) loaded.Add(c);
        }
        var notes = new List<NotaNote>();
        for (int i = 0; i < onsets.Count; i++)
        {
            if (!loaded.Contains(cls[i])) continue;
            notes.Add(new NotaNote(padNote[cls[i]], onsets[i] * toBeat, noteLen, Math.Clamp(0.3f + vel[i] * 0.7f, 0.2f, 1f)));
        }
        FinishConvert(dr, startBeat, clipBeats, notes, notes.Count == 0 ? "No drum hits found." : $"Converted {notes.Count} hits");
    }

    // Convert Melody: YIN pitch per frame → median-smoothed MIDI pitch → note segmentation, onto a
    // new Nota Synth track. Monophonic MVP — best on clean single-note lines / vocals.
    private async Task ConvertMelodyAsync(float[] mono, double sr, double startBeat, double clipBeats, IProgress<ProgressReport> prog)
    {
        const double minHz = 65, maxHz = 1100;
        int hop = Math.Max(1, (int)(sr * 0.010));   // 10 ms
        int win = (int)(sr / minHz);                // one lowest-note period of comparison window
        int need = win + (int)(sr / minHz);
        int frames = mono.Length > need ? (mono.Length - need) / hop + 1 : 0;
        if (frames < 2) { Status("No melody found."); return; }

        prog.Report(ProgressReport.At(0, "Detecting pitch…"));
        var (pitch, rms) = await Task.Run(() =>
        {
            var midi = new int[frames];
            var r = new float[frames];
            const float floor = 0.008f;             // silence gate
            int lastPct = -1;
            for (int i = 0; i < frames; i++)
            {
                int p = i * hop;
                double e = 0; for (int j = 0; j < win; j++) { float s = mono[p + j]; e += (double)s * s; }
                r[i] = (float)Math.Sqrt(e / win);
                if (r[i] < floor) midi[i] = -1;
                else
                {
                    double f0 = AudioPitch.YinHz(mono, p, win, sr, 0.15, minHz, maxHz);
                    midi[i] = f0 > 0 ? (int)Math.Round(69 + 12 * Math.Log2(f0 / 440.0)) : -1;
                }
                int pct = (int)(100.0 * (i + 1) / frames);
                if (pct != lastPct) { lastPct = pct; prog.Report(ProgressReport.At((double)(i + 1) / frames, "Detecting pitch…")); }
            }
            return (MedianFilter(midi, 2), r);      // ±2-frame median kills octave/jitter blips
        });

        double toBeat = mono.Length > 0 ? clipBeats / mono.Length : 0;
        int minFrames = Math.Max(1, (int)(0.06 * sr / hop));   // drop notes shorter than ~60 ms
        var notes = new List<NotaNote>();
        int cur = -1, curStart = 0; float peak = 0;
        void Flush(int endFrame)
        {
            if (cur >= 12 && cur <= 108 && endFrame - curStart >= minFrames)
            {
                double a = (double)curStart * hop, b = (double)endFrame * hop;
                notes.Add(new NotaNote(cur, a * toBeat, Math.Max(0.05, (b - a) * toBeat),
                                       Math.Clamp(0.25f + peak * 4f, 0.25f, 1f)));
            }
        }
        for (int i = 0; i < frames; i++)
        {
            if (pitch[i] != cur) { Flush(i); cur = pitch[i]; curStart = i; peak = rms[i]; }
            else peak = Math.Max(peak, rms[i]);
        }
        Flush(frames);

        if (notes.Count == 0) { Status("No melody found."); return; }
        int t = Engine.AddInstrumentTrack();        // Nota Synth
        int mc = Engine.AddMidiClip(t, startBeat, Math.Max(0.25, clipBeats));
        Engine.SetClipNotes(t, mc, notes.ToArray());
        Timeline.Refresh(); Timeline.Select(t, mc); ShowDevices(t); _session?.Refresh();
        Status($"Converted melody: {notes.Count} notes → new track {t}");
    }

    // Convert Harmony: STFT peak-picking (AudioHarmony) → polyphonic note spans on a new Nota Synth
    // track. Approximate (MVP) — best on sustained chords / pads.
    private async Task ConvertHarmonyAsync(float[] mono, double sr, double startBeat, double clipBeats, IProgress<ProgressReport> prog)
    {
        prog.Report(ProgressReport.Indeterminate("Detecting harmony…"));
        var spans = await Task.Run(() => AudioHarmony.Detect(mono, sr));
        if (spans.Count == 0) { Status("No harmony found."); return; }
        double toBeat = mono.Length > 0 ? clipBeats / mono.Length : 0;
        var notes = new List<NotaNote>(spans.Count);
        foreach (var s in spans)
            notes.Add(new NotaNote(s.Pitch, s.StartSample * toBeat, Math.Max(0.05, (s.EndSample - s.StartSample) * toBeat), s.Velocity));
        int t = Engine.AddInstrumentTrack();        // Nota Synth (polyphonic)
        int mc = Engine.AddMidiClip(t, startBeat, Math.Max(0.25, clipBeats));
        Engine.SetClipNotes(t, mc, notes.ToArray());
        Timeline.Refresh(); Timeline.Select(t, mc); ShowDevices(t); _session?.Refresh();
        Status($"Converted harmony: {notes.Count} notes → new track {t}");
    }

    private void Status(string s) { if (_vm is not null) _vm.StatusText = s; }

    // Median filter with radius r (odd window). Preserves array length.
    private static int[] MedianFilter(int[] a, int r)
    {
        var outp = new int[a.Length];
        var w = new List<int>(2 * r + 1);
        for (int i = 0; i < a.Length; i++)
        {
            w.Clear();
            for (int k = Math.Max(0, i - r); k <= Math.Min(a.Length - 1, i + r); k++) w.Add(a[k]);
            w.Sort();
            outp[i] = w[w.Count / 2];
        }
        return outp;
    }

    // Writes a mono slice to a temp WAV and loads it onto a Drum Rack pad at `note`. Returns the
    // chain index or -1. The sampler decodes synchronously, so the temp file is deleted right away.
    private int LoadPad(int drumTrack, float[] mono, double sr, int start, int end, int note)
    {
        int len = Math.Max(1, end - start);
        var buf = new float[len];
        Array.Copy(mono, start, buf, 0, Math.Min(len, mono.Length - start));
        string dir = Path.Combine(Path.GetTempPath(), "nota-slices");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "slice_" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var w = new WavWriter(path, (int)Math.Round(sr), 1, WavBitDepth.Float32)) w.WriteFrames(buf, len);
            return Engine.RackAddSamplerChain(drumTrack, path, note, false);
        }
        finally { try { File.Delete(path); } catch { /* best-effort */ } }
    }

    private void FinishConvert(int dr, double startBeat, double clipBeats, List<NotaNote> notes, string status)
    {
        if (notes.Count == 0) { Engine.RemoveTrack(dr); if (_vm is not null) _vm.StatusText = status; Timeline.Refresh(); return; }
        double lenBeats = Math.Max(0.25, clipBeats);
        int mc = Engine.AddMidiClip(dr, startBeat, lenBeats);
        Engine.SetClipNotes(dr, mc, notes.ToArray());
        Timeline.Refresh();
        Timeline.Select(dr, mc);
        ShowDevices(dr);
        _session?.Refresh();
        if (_vm is not null) _vm.StatusText = $"{status} → new Drum Rack track {dr}";
    }
}
