// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The transport loop's seam (#29, .specify/bugs/loop-seam-note-cut): when the playhead wraps, a
// key held live keeps sounding, a release tail rings on, a clip note crossing the loop end
// still releases, an arpeggiator carries on with the held keys, and a note recorded across the
// seam is stored as two notes — up to the loop end, and on from the loop start.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LoopSeamTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0;   // frames per beat at 120 BPM
    private const int Chunk = 256;
    private const int SynthRelease = 4;              // Synth param index (normalized, exp 2 ms … 3 s)

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in HeldKey()) yield return r;
        foreach (var r in ReleaseTail()) yield return r;
        foreach (var r in ClipNoteAcrossEnd()) yield return r;
        foreach (var r in Arpeggiator()) yield return r;
        foreach (var r in RecordAcrossSeam()) yield return r;
        foreach (var r in RoutedAcrossSeam()) yield return r;
    }

    private static float Beats(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        int frames = (int)Math.Round(beats * Spb);
        double sq = 0; int n = 0;
        for (int done = 0; done < frames; done += Chunk)
        {
            e.RenderOffline(b, Chunk, Sr);
            foreach (var v in b) { sq += v * (double)v; n++; }
        }
        return n > 0 ? (float)Math.Sqrt(sq / n) : 0f;
    }

    // An armed Synth track with a 1-bar loop (beats 0–4), stopped at 0.
    private static NotaEngine Looped(out int track)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        track = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(track, 0);
        e.SetTrackArmed(track, true);
        e.SetLoop(true, 0.0, 4.0);
        e.StopTransport();
        e.Seek(0.0);
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        return e;
    }

    private static bool Near(double a, double b, double tol = 0.05) => Math.Abs(a - b) <= tol;

    private static List<double[]> Notes(NotaEngine e, int track, int pitch)
    {
        var all = new List<double[]>();
        for (int c = 0; c < 64 && e.TryGetClipInfo(track, c, out var ci); c++)
            foreach (var n in e.GetClipNotes(track, c))
                if (n.Pitch == pitch) all.Add(new[] { ci.StartBeat + n.StartBeat, n.LengthBeats });
        return all.OrderBy(x => x[0]).ToList();
    }

    // A key held across the wrap keeps sounding.
    private static IEnumerable<(bool, string)> HeldKey()
    {
        using var e = Looped(out _);
        e.Play();
        Beats(e, 3.0);
        e.NoteOn(60, 0.8f);
        float before = Beats(e, 0.75);   // to ~3.75
        Beats(e, 0.5);                   // across the seam, to ~0.25
        double pos = e.PositionBeats;
        float after = Beats(e, 0.5);
        e.NoteOff(60);
        e.StopTransport();
        yield return (pos < 1.0 && before > 1e-3 && after > before * 0.5,
            $"loop seam: a held key keeps sounding after the wrap (rms {before:F4} → {after:F4}, pos {pos:F2})");
    }

    // A note released just before the loop end rings out its release tail into the next pass.
    private static IEnumerable<(bool, string)> ReleaseTail()
    {
        using var e = Looped(out int t);
        e.PluginParamSet(t, -1, SynthRelease, 0.8f);   // ~0.7 s ≈ 1.4 beats at 120 BPM
        e.AddMidiClip(t, 0.0, 4.0);
        e.SetClipNotes(t, 0, new[] { new NotaNote(60, 3.0, 0.9, 0.9f) });
        e.Seek(2.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 2.0);                   // past the seam at 4.0, to ~0.0
        double pos = e.PositionBeats;
        float tail = Beats(e, 0.25);
        e.StopTransport();
        yield return (pos < 1.0 && tail > 1e-3,
            $"loop seam: a release tail rings on after the wrap (rms {tail:F4}, pos {pos:F2})");
    }

    // A clip note running past the loop end is released at the seam: nothing hangs.
    private static IEnumerable<(bool, string)> ClipNoteAcrossEnd()
    {
        using var e = Looped(out int t);
        e.AddMidiClip(t, 0.0, 8.0);
        e.SetClipNotes(t, 0, new[] { new NotaNote(60, 3.0, 3.0, 0.9f) });   // 3.0 → 6.0
        e.Seek(2.5);
        Beats(e, 0.01);
        e.Play();
        float sounding = Beats(e, 1.0);   // the note sounds from 3.0
        Beats(e, 1.0);                    // across the seam, to ~0.5
        float after = Beats(e, 1.0);      // 0.5 → 1.5: no note in the loop there
        e.StopTransport();
        yield return (sounding > 1e-3 && after < 1e-4,
            $"loop seam: a clip note crossing the loop end releases, no hanging note (rms {sounding:F4} → {after:F6})");
    }

    // An arpeggiator keeps arpeggiating a key held across the wrap (no pattern reset).
    private static IEnumerable<(bool, string)> Arpeggiator()
    {
        using var e = Looped(out int t);
        e.AddMidiEffect(t, 0);            // Nota Arp
        e.Play();
        Beats(e, 3.0);
        e.NoteOn(60, 0.8f);
        float before = Beats(e, 0.9);
        Beats(e, 0.3);                    // across the seam
        float after = Beats(e, 1.0);
        e.NoteOff(60);
        e.StopTransport();
        yield return (before > 1e-3 && after > before * 0.5,
            $"loop seam: the arpeggiator carries on with a key held across the wrap (rms {before:F4} → {after:F4})");
    }

    // A key held across the seam while recording is stored as two notes: 3.5 → 4.0 and 0.0 → 0.5.
    private static IEnumerable<(bool, string)> RecordAcrossSeam()
    {
        using var e = Looped(out int t);
        e.SetRecording(true);
        Beats(e, 3.5);
        e.NoteOn(64, 0.7f);
        Beats(e, 1.0);                    // across the seam, to ~0.5
        e.NoteOff(64);
        Beats(e, 0.2);
        e.Poll();
        e.SetRecording(false);
        e.Poll();
        var n = Notes(e, t, 64);
        string got = string.Join(", ", n.Select(x => $"{x[0]:F2}+{x[1]:F2}"));
        yield return (n.Count == 2 && Near(n[0][0], 0.0) && Near(n[0][1], 0.5) && Near(n[1][0], 3.5) && Near(n[1][1], 0.5),
            $"loop seam: a note recorded across the wrap is split at the loop end ({got})");
    }

    // The same for MIDI routed in from another track.
    private static IEnumerable<(bool, string)> RoutedAcrossSeam()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        int dst = e.AddInstrumentTrack();
        int src = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(dst, 0);
        e.SetTrackBuiltinInstrument(src, 0);
        e.SetTrackMidiSource(dst, src);
        e.SetTrackArmed(dst, true);
        e.SetLoop(true, 0.0, 4.0);
        e.StopTransport();
        e.Seek(0.0);
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        e.SetRecording(true);
        Beats(e, 3.5);
        e.TrackNoteOn(src, 71, 0.8f);
        Beats(e, 1.0);
        e.TrackNoteOff(src, 71);
        Beats(e, 0.2);
        e.Poll();
        e.SetRecording(false);
        e.Poll();
        var n = Notes(e, dst, 71);
        string got = string.Join(", ", n.Select(x => $"{x[0]:F2}+{x[1]:F2}"));
        yield return (n.Count == 2 && Near(n[0][0], 0.0) && Near(n[0][1], 0.5) && Near(n[1][0], 3.5) && Near(n[1][1], 0.5),
            $"loop seam: a routed note recorded across the wrap is split at the loop end ({got})");
    }
}
