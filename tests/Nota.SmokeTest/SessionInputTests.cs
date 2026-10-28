// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session launches and recording input (milestone 0.58.1): a clip launched on a muted,
// solo-gated or looped track starts on time instead of waiting forever (#343), an audio take
// in a Session slot replaces the clip its track was playing (#337), and an audio track can
// take one channel of a stereo input as a mono source in the middle (#340), and a plain click
// on an arm button arms that track alone, ⌘-click more than one, and selecting an instrument
// track arms it (#332), and disarming a track while a key is held releases its note (#366).

using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class SessionInputTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0;   // frames per beat at 120 BPM
    private const int Chunk = 256;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in LaunchWhileSilent()) yield return r;
        foreach (var r in LaunchInsideShortLoop()) yield return r;
        foreach (var r in AudioTakeReplacesPlayingSlot()) yield return r;
        foreach (var r in MonoInput()) yield return r;
        foreach (var r in ExclusiveArm()) yield return r;
        foreach (var r in DisarmReleasesHeldNotes()) yield return r;
        foreach (var r in DisarmKeepsKeyForOtherTrack()) yield return r;
        foreach (var r in DisarmSparesClipNotes()) yield return r;
        foreach (var r in DisarmAfterEditAndInSession()) yield return r;
    }

    private static void Beats(NotaEngine e, double beats, float[]? keep = null)
    {
        var b = keep ?? new float[Chunk * 2];
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

    // A rolling engine (1-bar launch quantum) with one instrument track holding a 4-beat clip.
    private static NotaEngine Rolling(out int track)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        e.SetLaunchQuant(4.0);
        track = e.AddInstrumentTrack();
        e.AddSessionMidiClip(track, 0, 4.0);
        e.SetSessionNotes(track, 0, new[] { new NotaNote(60, 0.0, 1.0, 0.9f) });
        e.Seek(0.0);
        e.Play();
        Beats(e, 1.0);   // past the start, so a launch waits for the next bar line
        return e;
    }

    // #343: the launch used to apply only on tracks that rendered this block, so a muted track
    // (or one gated by another track's solo) kept it queued forever.
    private static IEnumerable<(bool, string)> LaunchWhileSilent()
    {
        using (var e = Rolling(out int t))
        {
            e.SetTrackMute(t, true);
            e.LaunchSlot(t, 0);
            Beats(e, 3.5);   // over the bar line at beat 4
            yield return (e.SessionSlotState(t, 0) == 3, $"a muted track's launch starts on the bar (state {e.SessionSlotState(t, 0)})");
            Beats(e, 2.0);
            double pos = e.SessionSlotPosition(t);
            yield return (Math.Abs(pos - 2.5) < 0.1, $"…and its clip keeps time while muted (at {pos:F2} of 4 beats, want 2.5)");
        }
        using (var e = Rolling(out int t))
        {
            // The clip's note (beats 0–1) ends while the track is muted: it mustn't sound on
            // once the track is heard again.
            e.LaunchSlot(t, 0);
            Beats(e, 3.5);   // launched at beat 4, now half a beat into the note
            float sounding = Peak(e, 0.25);
            e.SetTrackMute(t, true);
            Beats(e, 1.5);   // past the note's end
            e.SetTrackMute(t, false);
            Beats(e, 0.5);   // the release tail
            float after = Peak(e, 0.5);
            yield return (sounding > 0.01f && after < sounding * 0.05f,
                $"a note that ends while muted doesn't sound on after unmute (peak {after:F4} vs {sounding:F4} playing)");
        }
        using (var e = Rolling(out int t))
        {
            int other = e.AddInstrumentTrack();
            e.SetTrackSolo(other, true);
            e.LaunchSlot(t, 0);
            Beats(e, 3.5);
            yield return (e.SessionSlotState(t, 0) == 3, $"a launch starts while another track is soloed (state {e.SessionSlotState(t, 0)})");
            e.StopSlot(t);
            Beats(e, 4.0);
            yield return (e.SessionSlotState(t, 0) == 1, $"…and so does its stop (state {e.SessionSlotState(t, 0)})");
        }
    }

    // #343: a transport loop holding no bar line (here beats 4.5–7.5 with a 1-bar quantum)
    // never reached a launch point; the loop's wrap now counts as one.
    private static IEnumerable<(bool, string)> LaunchInsideShortLoop()
    {
        using var e = Rolling(out int t);
        e.SetLoop(true, 4.5, 7.5);
        e.Seek(4.5);
        Beats(e, 0.5);
        e.LaunchSlot(t, 0);
        Beats(e, 3.0);   // one wrap
        yield return (e.SessionSlotState(t, 0) == 3, $"a launch inside a loop without a bar line starts at the wrap (state {e.SessionSlotState(t, 0)})");
    }

    // #337: an audio take into an empty slot left the track's playing clip running until the
    // take stopped. A track plays one slot at a time.
    private static IEnumerable<(bool, string)> AudioTakeReplacesPlayingSlot()
    {
        string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_session_take.wav");
        WavWriter.WriteSine(wav, 2.0, 220.0, 48000);
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        e.SetLaunchQuant(0.0);
        int src = e.AddInstrumentTrack();   // an internal source: no capture device needed
        int aud = e.AddAudioTrack();
        e.SetTrackRecordInput(aud, src);
        e.SetTrackArmed(aud, true);
        yield return (e.AddSessionAudioFile(aud, 0, wav), "audio slot filled");
        e.LaunchSlot(aud, 0);
        Beats(e, 1.0);
        yield return (e.SessionSlotState(aud, 0) == 3, "the audio clip plays");
        e.RecordSessionSlot(aud, 1);
        Beats(e, 0.25);
        yield return (e.SessionSlotState(aud, 1) == 4, $"the next slot records (state {e.SessionSlotState(aud, 1)})");
        yield return (e.SessionSlotState(aud, 0) == 1, $"…and the playing clip stopped when the take began (state {e.SessionSlotState(aud, 0)})");
        e.StopSessionRecord();
        e.StopTransport();
        File.Delete(wav);
    }

    // #340: a mic on input 1 of a stereo device was recorded and heard on the left only.
    private static IEnumerable<(bool, string)> MonoInput()
    {
        using (var e = new NotaEngine())
        {
            int mon = e.AddAudioTrack();
            yield return (e.GetTrackInputChannels(mon) == InputChannels.Mono1, "a new audio track takes input 1 (mono)");
            var leftOnly = new float[8192 * 2];
            for (int i = 0; i < 8192; i++) leftOnly[i * 2] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / 48000f);
            e.PushMonitorFramesForTest(leftOnly, 8192);   // test hook: no capture device is opened
            e.SetTrackRecordInput(mon, 0);
            e.SetTrackMonitor(mon, true);
            var buf = new float[2048 * 2];
            (double L, double R) Heard()
            {
                e.PushMonitorFramesForTest(leftOnly, 8192);
                e.RenderOffline(buf, 2048, Sr);   // the ring keeps only a short cushion: measure this block
                double l = 0, r = 0;
                for (int i = 0; i < 2048; i++) { l += buf[i * 2] * (double)buf[i * 2]; r += buf[i * 2 + 1] * (double)buf[i * 2 + 1]; }
                return (Math.Sqrt(l / 2048), Math.Sqrt(r / 2048));
            }
            var mono = Heard();
            yield return (mono.R > 0.1 && Math.Abs(mono.L - mono.R) < 0.02 * mono.L,
                $"input 1 is heard on both sides (L {mono.L:F3}, R {mono.R:F3})");
            e.SetTrackInputChannels(mon, InputChannels.Stereo);
            var pair = Heard();
            yield return (pair.L > 0.1 && pair.R < 1e-4, $"the stereo pair keeps the sides apart (L {pair.L:F3}, R {pair.R:F5})");
            e.SetTrackInputChannels(mon, InputChannels.Mono2);
            var two = Heard();
            yield return (two.L < 1e-4 && two.R < 1e-4, $"input 2 alone hears the silent right input (L {two.L:F5}, R {two.R:F5})");
        }

        // Saved with the project; a project from before the setting keeps the stereo pair.
        using var src = new NotaEngine();
        int a = src.AddAudioTrack();
        src.SetTrackInputChannels(a, InputChannels.Mono2);
        var doc = ProjectService.Capture(src, new TransportState(120.0, 1.0, false, false), new List<string>());
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var dst = new NotaEngine())
            {
                ProjectService.Apply(doc, dst, dir);
                int id = FirstAudio(dst);
                yield return (dst.GetTrackInputChannels(id) == InputChannels.Mono2, $"input 2 survives save and load ({dst.GetTrackInputChannels(id)})");
            }
            foreach (var t in doc.Tracks) t.InputChannels = null;   // as written before 0.58
            using (var old = new NotaEngine())
            {
                ProjectService.Apply(doc, old, dir);
                int id = FirstAudio(old);
                yield return (old.GetTrackInputChannels(id) == InputChannels.Stereo, $"an older project keeps the stereo pair ({old.GetTrackInputChannels(id)})");
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    // #332: a plain arm click picks the one track the keyboard plays; ⌘-click adds tracks.
    private static IEnumerable<(bool, string)> ExclusiveArm()
    {
        using var e = new NotaEngine();
        int keys = e.AddInstrumentTrack(), drums = e.AddInstrumentTrack(), aud = e.AddAudioTrack();
        bool Armed(int id) { for (int i = 0; i < e.TrackCount; i++) if (e.TryGetTrackInfo(i, out var t) && t.Id == id) return t.Armed != 0; return false; }
        TrackArm.Set(e, keys, true, additive: false);
        bool changed = TrackArm.Set(e, drums, true, additive: false);
        yield return (changed && Armed(drums) && !Armed(keys), "a plain arm click arms that track alone");
        TrackArm.Set(e, aud, true, additive: true);
        yield return (Armed(drums) && Armed(aud), "⌘-click arms another track and keeps the first");
        TrackArm.Set(e, aud, false, additive: false);
        yield return (Armed(drums) && !Armed(aud), "disarming leaves the other tracks armed");

        // Arm-on-select: the selection moves the one arm to the instrument track you're on…
        TrackArm.Set(e, drums, false, additive: false);
        yield return (TrackArm.FollowSelection(e, keys) && Armed(keys), "selecting an instrument track arms it");
        yield return (TrackArm.FollowSelection(e, drums) && Armed(drums) && !Armed(keys), "…and selecting another moves the arm to it");
        // …but leaves alone an audio track, and a set armed by hand.
        yield return (!TrackArm.FollowSelection(e, aud) && !Armed(aud) && Armed(drums), "selecting an audio track doesn't arm it");
        TrackArm.Set(e, keys, true, additive: true);
        yield return (!TrackArm.FollowSelection(e, drums) && Armed(keys) && Armed(drums), "two armed tracks stay armed when the selection moves");
        int synth = e.AddInstrumentTrack();
        yield return (!TrackArm.FollowSelection(e, synth) && !Armed(synth), "…and a new track doesn't take the arm from them");
        TrackArm.Set(e, keys, false, additive: false);
        TrackArm.Set(e, drums, false, additive: false);
        TrackArm.Set(e, aud, true, additive: false);
        yield return (!TrackArm.FollowSelection(e, synth) && Armed(aud), "an armed audio track keeps its arm");
    }

    // A key held on an armed track, then the track disarmed (by its button, or by the arm moving
    // to another selected track) before the key comes up: disarming releases the note (#366).
    private static IEnumerable<(bool, string)> DisarmReleasesHeldNotes()
    {
        foreach (bool bySelection in new[] { false, true })
        {
            using var e = new NotaEngine();
            int keys = e.AddInstrumentTrack(), other = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(keys, 0);
            e.SetTrackBuiltinInstrument(other, 0);
            TrackArm.Set(e, keys, true, additive: false);
            e.NoteOn(60, 0.9f);
            float held = Peak(e, 0.5);
            if (bySelection) TrackArm.FollowSelection(e, other);
            else TrackArm.Set(e, keys, false, additive: false);
            Beats(e, 2.0);                     // past the release, the key still down
            float after = Peak(e, 0.5);
            e.NoteOff(60);
            float released = Peak(e, 0.5);
            string how = bySelection ? "the arm moving to another track" : "its arm button";
            yield return (held > 0.01f && after < 1e-4f && released < 1e-4f,
                $"disarming by {how} while a key is held releases the note (peak {held:F3} → {after:G2})");
        }
    }

    // A track edited while a key is held (its object replaced), a Session overdub, and many
    // tracks disarmed at once still release the held note (#366).
    private static IEnumerable<(bool, string)> DisarmAfterEditAndInSession()
    {
        using (var e = new NotaEngine())
        {
            int keys = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(keys, 0);
            int clip = e.AddMidiClip(keys, 16.0, 4.0);
            TrackArm.Set(e, keys, true, additive: false);
            e.NoteOn(60, 0.9f);
            float held = Peak(e, 0.5);
            e.SetClipNotes(keys, clip, new[] { new NotaNote(67, 0.0, 1.0, 0.8f) });
            Beats(e, 0.5);
            TrackArm.Set(e, keys, false, additive: false);
            Beats(e, 2.0);
            float after = Peak(e, 0.5);
            e.NoteOff(60);
            yield return (held > 0.01f && after < 1e-4f,
                $"a track edited while a key is held still releases it on disarm (peak {held:F3} → {after:G2})");
        }
        using (var e = new NotaEngine())
        {
            e.SetBpm(120);
            e.SetMetronome(false);
            e.SetLaunchQuant(0.0);
            int keys = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(keys, 0);
            TrackArm.Set(e, keys, true, additive: false);
            e.Seek(0);
            Beats(e, 0.01);
            e.RecordSessionSlot(keys, 0);
            Beats(e, 1.0);
            e.NoteOn(64, 0.9f);
            float held = Peak(e, 0.5);
            TrackArm.Set(e, keys, false, additive: false);
            Beats(e, 2.0);
            float after = Peak(e, 0.5);
            e.NoteOff(64);
            Beats(e, 0.2);
            e.StopSessionRecord();
            e.StopAllSession();
            e.StopTransport();
            yield return (held > 0.01f && after < 1e-4f,
                $"disarming during a Session overdub releases the held note (peak {held:F3} → {after:G2})");
        }
        using (var e = new NotaEngine())
        {
            var all = new List<int>();
            for (int k = 0; k < 20; k++)
            {
                int t = e.AddInstrumentTrack();
                e.SetTrackBuiltinInstrument(t, 0);
                TrackArm.Set(e, t, true, additive: k > 0);
                all.Add(t);
            }
            int last = e.AddInstrumentTrack();
            e.NoteOn(60, 0.5f);
            float held = Peak(e, 0.5);
            TrackArm.Set(e, last, true, additive: false); // disarms all twenty at once
            Beats(e, 1.0);
            float after = Peak(e, 0.5);
            e.NoteOff(60);
            yield return (held > 0.01f && after < 1e-4f,
                $"twenty tracks disarmed at once all release the held note (peak {held:F3} → {after:G2})");
        }
    }

    // The key stays down for the other armed track: a take started there while it's still held
    // records it (#366).
    private static IEnumerable<(bool, string)> DisarmKeepsKeyForOtherTrack()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        int a = e.AddInstrumentTrack(), b = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(a, 0);
        e.SetTrackBuiltinInstrument(b, 0);
        TrackArm.Set(e, a, true, additive: false);
        TrackArm.Set(e, b, true, additive: true);
        e.Play();
        Beats(e, 1.0);
        e.NoteOn(62, 0.8f);
        Beats(e, 0.5);
        TrackArm.Set(e, a, false, additive: true);
        Beats(e, 0.5);
        e.SetRecording(true);              // the take goes to b, the key still down
        Beats(e, 1.0);
        e.NoteOff(62);
        Beats(e, 0.2);
        e.Poll();
        e.SetRecording(false);
        e.Poll();
        bool got = false;
        for (int c = 0; c < 8 && e.TryGetClipInfo(b, c, out _); c++)
            got |= e.GetClipNotes(b, c).Any(n => n.Pitch == 62);
        yield return (got, "disarming one track leaves the key held for a take started on another armed track");
        e.StopTransport();
    }

    // Disarming releases only the live notes that track was sounding: its clip's note on a
    // pitch another track holds from Nota Remote keeps playing (#366).
    private static IEnumerable<(bool, string)> DisarmSparesClipNotes()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        int a = e.AddInstrumentTrack(), b = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(a, 0);
        e.SetTrackBuiltinInstrument(b, 0);
        int clip = e.AddMidiClip(a, 0.0, 8.0);
        e.SetClipNotes(a, clip, new[] { new NotaNote(64, 0.0, 8.0, 0.8f) });
        TrackArm.Set(e, a, true, additive: false);
        e.Seek(0.0);
        e.Play();
        Beats(e, 0.5);
        e.TrackNoteOn(b, 64, 0.8f);
        Beats(e, 0.5);
        TrackArm.Set(e, a, false, additive: false);
        float peak = 0;
        var buf = new float[Chunk * 2];
        for (int k = 0; k < 64; k++)
        {
            e.RenderOffline(buf, Chunk, Sr);
            if (k >= 8 && e.TryGetTrackMeter(a, out var m)) peak = Math.Max(peak, m.Peak);
        }
        e.TrackNoteOff(b, 64);
        yield return (peak > 0.01f, $"disarming leaves the track's clip note sounding (peak {peak:F3})");
        e.StopTransport();
    }

    private static int FirstAudio(NotaEngine e)
    {
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var t) && !t.IsInstrument && !t.IsReturn && !t.IsGroup) return t.Id;
        return -1;
    }
}
