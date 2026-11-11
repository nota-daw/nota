// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Changing a track's MIDI In while a note it routes is held (#504): the note the old route
// started on the destination ends at the switch, whether MIDI In goes to None or to another
// track, or its track is removed; the new route plays; switching away and back before the next
// block leaves the note to end with its own note-off; the destination's own notes keep sounding.
// The source tracks are muted, so what reaches the master is the destination alone.

using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class MidiSourceSwitchTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0; // frames per beat at 120 BPM
    private const int Chunk = 256;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in SwitchReleasesHeldNote()) yield return r;
        foreach (var r in SwitchAndBackKeepsNote()) yield return r;
        foreach (var r in RemovedSourceReleases()) yield return r;
        foreach (var r in OwnNotesKeepSounding()) yield return r;
    }

    private static void Beats(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        int frames = (int)Math.Round(beats * Spb);
        for (int done = 0; done < frames; done += Chunk) e.RenderOffline(b, Chunk, Sr);
    }

    private static float Peak(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        float peak = 0;
        int frames = (int)Math.Round(beats * Spb);
        for (int done = 0; done < frames; done += Chunk)
        {
            e.RenderOffline(b, Chunk, Sr);
            foreach (var x in b) peak = Math.Max(peak, Math.Abs(x));
        }
        return peak;
    }

    // Sources a and b (muted, a armed) and destination d taking its MIDI from a.
    private static NotaEngine Routed(out int a, out int b, out int d)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        a = e.AddInstrumentTrack();
        b = e.AddInstrumentTrack();
        d = e.AddInstrumentTrack();
        foreach (int t in new[] { a, b, d }) e.SetTrackBuiltinInstrument(t, 0);
        e.SetTrackMute(a, true);
        e.SetTrackMute(b, true);
        e.SetTrackMidiSource(d, a);
        TrackArm.Set(e, a, true, additive: false);
        return e;
    }

    // A key held on the source, then the destination's MIDI In switched to None or to the other
    // track: the destination's note ends at once, the key still down; the new route plays.
    private static IEnumerable<(bool, string)> SwitchReleasesHeldNote()
    {
        foreach (bool toTrack in new[] { false, true })
        {
            using var e = Routed(out int a, out int b, out int d);
            e.NoteOn(60, 0.9f);
            float held = Peak(e, 0.5);
            e.SetTrackMidiSource(d, toTrack ? b : -1);
            Beats(e, 2.0);                     // past the release, the key still down
            float after = Peak(e, 0.5);
            string to = toTrack ? "another track" : "None";
            yield return (held > 0.01f && after < 1e-4f,
                $"MIDI In switched to {to} while a routed note is held releases it (peak {held:F3} → {after:G2})");
            yield return (e.GetTrackMidiSource(d) == (toTrack ? b : -1), $"MIDI In reads back as {to} after the switch");
            if (toTrack)
            {
                e.TrackNoteOn(b, 64, 0.9f);
                float routed = Peak(e, 0.5);
                e.TrackNoteOff(b, 64);
                Beats(e, 2.0);
                float ended = Peak(e, 0.5);
                yield return (routed > 0.01f && ended < 1e-4f,
                    $"the new MIDI In plays through the destination and ends with its note-off (peak {routed:F3} → {ended:G2})");
            }
            e.NoteOff(60);
            Beats(e, 2.0);
            float released = Peak(e, 0.5);
            yield return (released < 1e-4f, $"the old route's note-off after the switch leaves nothing sounding (peak {released:G2})");
        }
    }

    // Switched away and back between two blocks: nothing changed for the destination, so its
    // note keeps sounding and ends with the key.
    private static IEnumerable<(bool, string)> SwitchAndBackKeepsNote()
    {
        using var e = Routed(out int a, out int b, out int d);
        e.NoteOn(60, 0.9f);
        Beats(e, 0.5);
        e.SetTrackMidiSource(d, b);
        e.SetTrackMidiSource(d, a);
        Beats(e, 2.0);
        float kept = Peak(e, 0.5);
        e.NoteOff(60);
        Beats(e, 2.0);
        float released = Peak(e, 0.5);
        yield return (kept > 0.01f && released < 1e-4f,
            $"MIDI In switched away and back keeps the note until its own note-off (peak {kept:F3} → {released:G2})");
    }

    // The source track removed while its note is held: the destination's note ends; MIDI In
    // still names the removed track (the pickers say so), and undo brings the route back.
    private static IEnumerable<(bool, string)> RemovedSourceReleases()
    {
        using var e = Routed(out int a, out _, out int d);
        e.NoteOn(60, 0.9f);
        float held = Peak(e, 0.5);
        bool removed = e.RemoveTrack(a);
        Beats(e, 2.0);
        float after = Peak(e, 0.5);
        e.NoteOff(60);
        yield return (removed && held > 0.01f && after < 1e-4f,
            $"removing the MIDI In track while its note is held releases it (peak {held:F3} → {after:G2})");
        yield return (e.GetTrackMidiSource(d) == a, "MIDI In still names the removed track");
        e.Undo();
        TrackArm.Set(e, a, true, additive: false);
        e.NoteOn(62, 0.9f);
        float back = Peak(e, 0.5);
        e.NoteOff(62);
        yield return (back > 0.01f, $"undoing the removal plays the route again (peak {back:F3})");
    }

    // The destination's own note (Nota Remote on it) isn't part of the route: it keeps sounding
    // through the switch and ends with its own note-off.
    private static IEnumerable<(bool, string)> OwnNotesKeepSounding()
    {
        using var e = Routed(out _, out _, out int d);
        e.TrackNoteOn(d, 72, 0.9f);
        e.NoteOn(60, 0.9f);
        Beats(e, 0.5);
        e.SetTrackMidiSource(d, -1);
        Beats(e, 2.0);
        float own = Peak(e, 0.5);
        e.TrackNoteOff(d, 72);
        Beats(e, 2.0);
        float released = Peak(e, 0.5);
        e.NoteOff(60);
        yield return (own > 0.01f && released < 1e-4f,
            $"the destination's own note keeps sounding through the switch (peak {own:F3} → {released:G2})");
    }
}
