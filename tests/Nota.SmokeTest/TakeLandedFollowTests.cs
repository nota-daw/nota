// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Follow Action when a recorded take lands (#481, specs/111): a Session slot's landing rule
// launches a scene once a fresh take recorded into it has become a playable clip — once per take,
// on the next launch boundary — and never for a take that did not land (count-in stopped, stopped
// while waiting, empty, overdub), a global stop, with Follow off, or on Undo / Redo / reopen. The
// rule belongs to the slot and follows its target scene through row edits. Device-free: MIDI takes
// from the live note path, audio takes from internal resampling.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TakeLandedFollowTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0;   // frames per beat at 120 BPM
    private const int Chunk = 256;

    private static NotaSlotLandingRule ToScene(int scene, double quant = -1) => new(SlotLandingAction.Scene, scene, quant);
    private static readonly NotaSlotLandingRule Next = new(SlotLandingAction.NextScene);
    private static readonly NotaSlotTakeLength OneBar = new(SlotTakeMode.Fixed, 1);

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Values()) yield return r;
        foreach (var r in IssueExample()) yield return r;
        foreach (var r in OwnQuantum()) yield return r;
        foreach (var r in OpenTake()) yield return r;
        foreach (var r in AudioTake()) yield return r;
        foreach (var r in NextSceneWraps()) yield return r;
        foreach (var r in NeverFires()) yield return r;
        foreach (var r in GlobalStops()) yield return r;
        foreach (var r in Retry()) yield return r;
        foreach (var r in UndoBeforeAndAfter()) yield return r;
        foreach (var r in OncePerTake()) yield return r;
        foreach (var r in SceneEdits()) yield return r;
        foreach (var r in SaveAndReopen()) yield return r;
    }

    private static NotaSlotLandingRule Get(NotaEngine e, int t, int s)
        => e.TryGetSessionSlotLandingRule(t, s, out var v) ? v : new NotaSlotLandingRule((SlotLandingAction)(-1));
    private static bool Same(NotaSlotLandingRule a, NotaSlotLandingRule b)
        => a.Action == b.Action && a.Scene == b.Scene && Math.Abs(a.QuantBeats - b.QuantBeats) < 1e-9;
    private static string Show(NotaSlotLandingRule v) => $"{v.LandingAction} {v.Scene} q{v.QuantBeats}";

    // Set, read back, refuse what's out of range; a change is one undo step.
    private static IEnumerable<(bool, string)> Values()
    {
        using var e = Armed(out int t, out _);
        yield return (Same(Get(e, t, 0), NotaSlotLandingRule.Off), $"a slot's rule starts Off ({Show(Get(e, t, 0))})");
        bool set = e.SetSessionSlotLandingRule(t, 0, ToScene(1, 4));
        yield return (set && Same(Get(e, t, 0), ToScene(1, 4)), $"Launch scene 2 on 1 bar reads back ({Show(Get(e, t, 0))})");
        e.SetSessionSlotLandingRule(t, 1, new NotaSlotLandingRule(SlotLandingAction.NextScene, 5, -7));
        yield return (Same(Get(e, t, 1), Next), $"Next scene stores no target and any negative quantum as Global ({Show(Get(e, t, 1))})");
        foreach (var (what, v) in new[]
        {
            ("action 7", new NotaSlotLandingRule { Action = 7 }), ("target past the last scene", ToScene(e.SceneCount)),
            ("negative target", ToScene(-1)), ("quantum 33", ToScene(1, 33)), ("quantum NaN", ToScene(1, double.NaN)),
        })
            yield return (!e.SetSessionSlotLandingRule(t, 0, v) && Same(Get(e, t, 0), ToScene(1, 4)), $"refused: {what}; the rule stays");
        yield return (!e.SetSessionSlotLandingRule(9999, 0, Next) && !e.SetSessionSlotLandingRule(t, e.SceneCount, Next)
                      && !e.TryGetSessionSlotLandingRule(t, e.SceneCount, out _),
            "refused: unknown track or slot");
        e.SetSessionSlotLandingRule(t, 2, ToScene(0));
        e.Undo();
        bool undone = Same(Get(e, t, 2), NotaSlotLandingRule.Off);
        e.Redo();
        yield return (undone && Same(Get(e, t, 2), ToScene(0)), "setting a rule is one Undo / Redo step");
    }

    // The issue's example: a 1-bar take into an empty slot with "launch scene 2"; the scene is queued
    // only once the take has landed, launches on the next bar, and exactly once.
    private static IEnumerable<(bool, string)> IssueExample()
    {
        using var e = Armed(out int t, out int other);
        e.SetSessionSlotTakeLength(t, 0, OneBar);
        e.SetSessionSlotLandingRule(t, 0, ToScene(1));
        Rolling(e);
        e.RecordSessionSlot(t, 0);                  // records from beat 4
        Beats(e, 3.6);                              // ~beat 4.1
        bool recording = e.SessionSlotState(t, 0) == 4;
        PlayNote(e);                                // ~4.6
        Beats(e, 2.9);                              // ~7.5: still recording
        bool notYet = !e.SessionSceneQueued(1);
        Beats(e, 0.7);                              // ~8.2: landed at 8, served on the next poll
        bool landed = !e.TryGetSessionRecordTarget(out _, out _, out _) && e.SessionSlotState(t, 0) != 0;
        bool queued = e.SessionSceneQueued(1) && e.SessionPlayingSlot(other) != 1;
        yield return (recording && notYet && landed && queued,
            $"the scene is queued only once the take has landed (rec {recording}, before {notYet}, landed {landed}, queued {queued})");
        Beats(e, 3.6);                              // ~11.8
        bool waits = e.SessionPlayingSlot(other) != 1;
        Beats(e, 0.5);                              // ~12.3
        yield return (waits && e.SessionPlayingSlot(other) == 1 && !e.SessionSceneQueued(1),
            $"it launches on the next bar after landing (waits {waits}, playing {e.SessionPlayingSlot(other)})");
        bool again = false;
        for (int i = 0; i < 16; i++) { Beats(e, 0.5); again |= e.SessionSceneQueued(1) || e.SessionSceneQueued(0); }
        yield return (!again && e.SessionPlayingSlot(other) == 1, "exactly once: nothing is queued again over the next two bars");
        Done(e);
    }

    // A rule with its own quantum: 2 bars, so a take landing at beat 8 launches at 16, not 12.
    private static IEnumerable<(bool, string)> OwnQuantum()
    {
        using var e = Armed(out int t, out int other);
        e.SetSessionSlotTakeLength(t, 0, OneBar);
        e.SetSessionSlotLandingRule(t, 0, ToScene(1, 8));
        Rolling(e);
        e.RecordSessionSlot(t, 0);
        Beats(e, 3.6);
        PlayNote(e);
        Beats(e, 7.6);                              // ~12.2
        bool waits = e.SessionSceneQueued(1) && e.SessionPlayingSlot(other) != 1;
        Beats(e, 4.0);                              // ~16.2
        yield return (waits && e.SessionPlayingSlot(other) == 1,
            $"a rule on 2 bars launches on the 2-bar boundary after landing (at 12 waiting {waits}, at 16 playing {e.SessionPlayingSlot(other)})");
        Done(e);
    }

    // An open take: the Stop request lands it, and only then is the scene queued.
    private static IEnumerable<(bool, string)> OpenTake()
    {
        using var e = Armed(out int t, out int other);
        e.SessionRecordLength = 0;
        e.SetSessionSlotLandingRule(t, 0, ToScene(1));
        Rolling(e);
        e.RecordSessionSlot(t, 0);
        Beats(e, 3.6);
        PlayNote(e);
        Beats(e, 6.0);                              // ~10.6, still recording
        bool notYet = !e.SessionSceneQueued(1) && e.TryGetSessionRecordTarget(out _, out _, out _);
        e.StopSessionRecord();
        Beats(e, 0.05);
        yield return (notYet && e.SessionSceneQueued(1) && e.SessionSlotState(t, 0) != 0,
            $"an open take queues the scene once the stop lands it (before {notYet}, queued {e.SessionSceneQueued(1)})");
        Beats(e, 2.0);
        yield return (e.SessionPlayingSlot(other) == 1, $"and the scene launches on the next bar ({e.SessionPlayingSlot(other)})");
        Done(e);
    }

    // An audio take fires the same way once its clip is built.
    private static IEnumerable<(bool, string)> AudioTake()
    {
        using var e = Resampling(out int rec);
        int other = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(other, 0);
        e.AddSessionMidiClip(other, 1, 4);
        e.SetSessionSlotTakeLength(rec, 0, OneBar);
        e.SetSessionSlotLandingRule(rec, 0, ToScene(1));
        Rolling(e);
        e.RecordSessionSlot(rec, 0);
        Beats(e, 3.6);                              // ~4.1
        bool recording = e.SessionSlotState(rec, 0) == 4;
        bool notYet = true;
        double landedAt = -1;
        for (int i = 0; i < 120 && landedAt < 0; i++) // the audio take lands near beat 8
        {
            notYet &= !e.SessionSceneQueued(1);
            Beats(e, 0.05);
            if (!e.TryGetSessionRecordTarget(out _, out _, out _) && e.SessionSlotState(rec, 0) is not (0 or 4 or 5))
                landedAt = e.PositionBeats;
        }
        Beats(e, 0.05);
        bool queued = e.SessionSceneQueued(1);
        Beats(e, 12.2 - e.PositionBeats);           // ~12.2
        yield return (recording && notYet && landedAt > 0 && queued && e.SessionPlayingSlot(other) == 1,
            $"an audio take launches the scene after it lands (rec {recording}, before {notYet}, landed at {landedAt:0.00}, queued {queued}, playing {e.SessionPlayingSlot(other)})");
        Done(e);
    }

    // "Next scene" from the last scene wraps to the first, as scene follow does.
    private static IEnumerable<(bool, string)> NextSceneWraps()
    {
        using var e = Armed(out int t, out int other);
        int last = e.SceneCount - 1;
        e.AddSessionMidiClip(other, 0, 4);
        e.SetSessionSlotTakeLength(t, last, OneBar);
        e.SetSessionSlotLandingRule(t, last, Next);
        Rolling(e);
        e.RecordSessionSlot(t, last);
        Beats(e, 3.6);
        PlayNote(e);
        Beats(e, 7.6);                              // ~12.2: landed at 8, launched at 12
        yield return (e.SessionPlayingSlot(other) == 0, $"next scene from the last scene launches the first ({e.SessionPlayingSlot(other)})");
        Done(e);
    }

    // Takes that never land, and rules that must not run.
    private static IEnumerable<(bool, string)> NeverFires()
    {
        // Count-in stopped.
        {
            using var e = Armed(out int t, out int other);
            e.SetSessionSlotLandingRule(t, 0, ToScene(1));
            e.SetCountIn(1);
            e.RecordSessionSlot(t, 0);
            Beats(e, 1.0);
            e.StopSessionRecord();
            Beats(e, 4.0);
            yield return (!e.SessionSceneQueued(1) && e.SessionPlayingSlot(other) != 1 && e.SessionSlotState(t, 0) == 0,
                "a take stopped in its count-in launches nothing");
            e.SetCountIn(0);
            Done(e);
        }
        // Stopped while waiting for its launch boundary.
        {
            using var e = Armed(out int t, out int other);
            e.SetSessionSlotLandingRule(t, 0, ToScene(1));
            Rolling(e);
            e.RecordSessionSlot(t, 0);              // waits for beat 4
            Beats(e, 1.0);
            e.StopSessionRecord();
            Beats(e, 8.0);
            yield return (!e.SessionSceneQueued(1) && e.SessionPlayingSlot(other) != 1 && e.SessionSlotState(t, 0) == 0,
                "a take stopped before its boundary launches nothing");
            Done(e);
        }
        // Empty MIDI take (no notes).
        {
            using var e = Armed(out int t, out int other);
            e.SetSessionSlotTakeLength(t, 0, OneBar);
            e.SetSessionSlotLandingRule(t, 0, ToScene(1));
            Rolling(e);
            e.RecordSessionSlot(t, 0);
            Beats(e, 12.0);                         // landed at 8 with nothing in it
            yield return (!e.SessionSceneQueued(1) && e.SessionPlayingSlot(other) != 1, "an empty MIDI take launches nothing");
            Done(e);
        }
        // Follow off globally.
        {
            using var e = Armed(out int t, out int other);
            e.SessionFollow = false;
            e.SetSessionSlotTakeLength(t, 0, OneBar);
            e.SetSessionSlotLandingRule(t, 0, ToScene(1));
            Rolling(e);
            e.RecordSessionSlot(t, 0);
            Beats(e, 3.6);
            PlayNote(e);
            Beats(e, 8.0);
            yield return (!e.SessionSceneQueued(1) && e.SessionPlayingSlot(other) != 1 && e.SessionSlotState(t, 0) != 0,
                "with Follow off globally the take lands and nothing launches");
            Done(e);
        }
        // Overdub into a slot that holds a clip.
        {
            using var e = Armed(out int t, out int other);
            e.AddSessionMidiClip(t, 0, 4);
            e.SetSessionNotes(t, 0, new[] { new NotaNote(48, 0.0, 1.0, 0.8f) });
            e.SetSessionSlotLandingRule(t, 0, ToScene(1));
            e.SessionRecordLength = 4;
            Rolling(e);
            e.RecordSessionSlot(t, 0);
            Beats(e, 3.6);
            PlayNote(e);
            Beats(e, 8.0);
            yield return (!e.SessionSceneQueued(1) && e.SessionPlayingSlot(other) != 1, "an overdub into a filled slot launches nothing");
            Done(e);
        }
    }

    // Stopping everything lands the take but never fires its rule.
    private static IEnumerable<(bool, string)> GlobalStops()
    {
        foreach (bool transport in new[] { true, false })
        {
            using var e = Armed(out int t, out int other);
            e.SessionRecordLength = 0;
            e.SetSessionSlotLandingRule(t, 0, ToScene(1));
            Rolling(e);
            e.RecordSessionSlot(t, 0);
            Beats(e, 3.6);
            PlayNote(e);
            Beats(e, 2.0);
            if (transport) e.StopTransport(); else e.StopAllSession();
            Beats(e, 6.0);
            bool kept = e.SessionSlotState(t, 0) != 0;
            yield return (kept && !e.SessionSceneQueued(1) && e.SessionPlayingSlot(other) != 1,
                $"{(transport ? "stopping the transport" : "Stop all clips")} keeps the take and launches nothing (kept {kept})");
            Done(e);
        }
    }

    // A retried take: the discarded attempt fires nothing; the retried take fires once when it lands.
    private static IEnumerable<(bool, string)> Retry()
    {
        using var e = Armed(out int t, out int other);
        e.SessionRecordLength = 0;
        e.SetSessionSlotLandingRule(t, 0, ToScene(1));
        Rolling(e);
        e.RecordSessionSlot(t, 0);
        Beats(e, 3.6);
        PlayNote(e);
        Beats(e, 1.0);                              // ~5.6
        e.RetryTake();                              // records again from beat 8
        Beats(e, 0.2);
        bool none = !e.SessionSceneQueued(1);
        Beats(e, 2.6);                              // ~8.4
        PlayNote(e);
        Beats(e, 1.0);                              // ~9.9
        bool stillNone = !e.SessionSceneQueued(1);
        e.StopSessionRecord();
        Beats(e, 0.05);
        bool queued = e.SessionSceneQueued(1);
        Beats(e, 2.5);
        yield return (none && stillNone && queued && e.SessionPlayingSlot(other) == 1,
            $"the retried attempt fires nothing; the retried take fires once it lands (after retry {none}, before stop {stillNone}, queued {queued})");
        Done(e);
    }

    // Undo before the boundary withdraws the launch; Undo / Redo after it launched change nothing.
    private static IEnumerable<(bool, string)> UndoBeforeAndAfter()
    {
        {
            using var e = Armed(out int t, out int other);
            e.SetSessionSlotTakeLength(t, 0, OneBar);
            e.SetSessionSlotLandingRule(t, 0, ToScene(1));
            Rolling(e);
            e.RecordSessionSlot(t, 0);
            Beats(e, 3.6);
            PlayNote(e);
            Beats(e, 3.6);                          // ~8.2: landed, queued for 12
            bool queued = e.SessionSceneQueued(1);
            while (e.SessionSlotState(t, 0) != 0 && e.Undo()) { }
            Beats(e, 0.1);
            bool withdrawn = !e.SessionSceneQueued(1);
            Beats(e, 4.5);                          // past 12
            yield return (queued && withdrawn && e.SessionPlayingSlot(other) != 1,
                $"undoing the take before its boundary withdraws the scene launch (queued {queued}, withdrawn {withdrawn}, playing {e.SessionPlayingSlot(other)})");
            Done(e);
        }
        {
            using var e = Armed(out int t, out int other);
            e.SetSessionSlotTakeLength(t, 0, OneBar);
            e.SetSessionSlotLandingRule(t, 0, ToScene(1));
            Rolling(e);
            e.RecordSessionSlot(t, 0);
            Beats(e, 3.6);
            PlayNote(e);
            Beats(e, 7.7);                          // ~12.3: launched
            bool launched = e.SessionPlayingSlot(other) == 1;
            while (e.SessionSlotState(t, 0) != 0 && e.Undo()) { }
            Beats(e, 2.0);
            bool afterUndo = !e.SessionSceneQueued(1) && !e.SessionSceneQueued(0) && e.SessionPlayingSlot(other) == 1;
            while (e.Redo()) { }
            Beats(e, 4.0);
            bool afterRedo = !e.SessionSceneQueued(1) && !e.SessionSceneQueued(0) && e.SessionPlayingSlot(other) == 1;
            yield return (launched && afterUndo && afterRedo,
                $"Undo / Redo after the launch neither reverse it nor fire again (launched {launched}, undo {afterUndo}, redo {afterRedo})");
            Done(e);
        }
    }

    // The rule stays with the slot: a new take after the clip is deleted fires once more.
    private static IEnumerable<(bool, string)> OncePerTake()
    {
        using var e = Armed(out int t, out int other);
        e.AddSessionMidiClip(other, 2, 4);
        e.SetSessionSlotTakeLength(t, 0, OneBar);
        e.SetSessionSlotLandingRule(t, 0, ToScene(1));
        Rolling(e);
        e.RecordSessionSlot(t, 0);
        Beats(e, 3.6);
        PlayNote(e);
        Beats(e, 7.7);                              // ~12.3: scene 2 launched
        bool first = e.SessionPlayingSlot(other) == 1;
        e.LaunchScene(2);
        e.ClearSessionSlot(t, 0);
        Beats(e, 4.0);                              // ~16.3: scene 3 plays
        bool moved = e.SessionPlayingSlot(other) == 2 && Same(Get(e, t, 0), ToScene(1));
        e.RecordSessionSlot(t, 0);                  // from 20
        Beats(e, 3.9);
        PlayNote(e);
        Beats(e, 7.7);                              // landed at 24, launched at 28
        yield return (first && moved && e.SessionPlayingSlot(other) == 1,
            $"after the clip is deleted the rule stays and a new take fires it again (first {first}, kept {moved}, again {e.SessionPlayingSlot(other)})");
        Done(e);
    }

    // A rule follows the scene it names through row edits; deleting that scene turns it Off.
    private static IEnumerable<(bool, string)> SceneEdits()
    {
        using var e = Armed(out int t, out _);
        e.SetSessionSlotLandingRule(t, 0, ToScene(2));
        e.SetSessionSlotLandingRule(t, 3, ToScene(1, 8));
        e.InsertScene(1);                           // old rows 1.. move down by one
        bool inserted = Same(Get(e, t, 0), ToScene(3)) && Same(Get(e, t, 4), ToScene(2, 8));
        e.MoveScene(3, 1);                          // the target row moves up to 1
        bool moved = Same(Get(e, t, 0), ToScene(1));
        e.RemoveScene(1);                           // the target is deleted
        bool off = Same(Get(e, t, 0), NotaSlotLandingRule.Off);
        yield return (inserted && moved && off,
            $"a rule follows its target through insert and move, and turns Off when it is deleted ({inserted}, {moved}, {off}: {Show(Get(e, t, 0))})");
    }

    // Rules survive save/reopen and reopening fires nothing; a project without them is all Off.
    private static IEnumerable<(bool, string)> SaveAndReopen()
    {
        using var e = Armed(out int t, out _);
        e.AddSessionMidiClip(t, 0, 4);
        e.SetSessionNotes(t, 0, new[] { new NotaNote(60, 0.0, 1.0, 0.8f) });
        e.SetSessionSlotLandingRule(t, 0, ToScene(2, 8)); // under a clip
        e.SetSessionSlotLandingRule(t, 1, Next);
        var w = new List<string>();
        var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), w);
        var saved = doc.Tracks.Find(x => x.SessionLandingRules is { Count: > 0 });
        yield return (saved?.SessionLandingRules?.Count == 2, $"two rules are written ({saved?.SessionLandingRules?.Count ?? 0})");
        string bundle = Path.Combine(Path.GetTempPath(), "nota-landing-" + Guid.NewGuid().ToString("N") + ".nota");
        ProjectService.Save(doc, bundle, e);
        using var e2 = new NotaEngine();
        var warnings = ProjectService.Apply(ProjectService.Load(bundle), e2, bundle);
        int t2 = FirstInstrument(e2);
        yield return (t2 > 0 && Same(Get(e2, t2, 0), ToScene(2, 8)) && Same(Get(e2, t2, 1), Next) && warnings.Count == 0,
            $"rules survive save/reopen ({Show(Get(e2, t2, 0))}, {Show(Get(e2, t2, 1))})");
        e2.SetBpm(120);
        Rolling(e2);
        bool any = false;
        for (int i = 0; i < 16; i++) { Beats(e2, 0.5); for (int s = 0; s < e2.SceneCount; s++) any |= e2.SessionSceneQueued(s); }
        yield return (!any, "reopening a project fires no rule");

        foreach (var tr in doc.Tracks) tr.SessionLandingRules = null; // a project from before #481
        string old = Path.Combine(Path.GetTempPath(), "nota-landing-old-" + Guid.NewGuid().ToString("N") + ".nota");
        ProjectService.Save(doc, old, e);
        using var e3 = new NotaEngine();
        ProjectService.Apply(ProjectService.Load(old), e3, old);
        int t3 = FirstInstrument(e3);
        bool allOff = t3 > 0;
        for (int s = 0; s < e3.SceneCount; s++) allOff &= Same(Get(e3, t3, s), NotaSlotLandingRule.Off);
        yield return (allOff, "a project without rules opens with every rule Off");
        try { Directory.Delete(bundle, true); Directory.Delete(old, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static int FirstInstrument(NotaEngine e)
    {
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) return ti.Id;
        return -1;
    }

    private static void PlayNote(NotaEngine e)
    {
        e.NoteOn(60, 0.8f);
        Beats(e, 0.5);
        e.NoteOff(60);
    }

    private static void Done(NotaEngine e)
    {
        e.StopSessionRecord();
        e.StopAllSession();
        e.StopTransport();
    }

    // Transport rolling from beat 0 with a 1-bar launch quantum and no count-in.
    private static void Rolling(NotaEngine e)
    {
        e.SetCountIn(0);
        e.SetLaunchQuant(4.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 0.5);
    }

    private static void Beats(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        int frames = (int)Math.Round(beats * Spb);
        for (int done = 0; done < frames; done += Chunk) { e.RenderOffline(b, Chunk, Sr); e.Poll(); }
    }

    // An armed instrument track for the takes, and another instrument track with a clip in scene 2
    // (index 1) — "the Chorus" — so a scene launch shows on it.
    private static NotaEngine Armed(out int track, out int other)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        track = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(track, 0);
        e.SetTrackArmed(track, true);
        other = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(other, 0);
        e.AddSessionMidiClip(other, 1, 4);
        while (e.SceneCount < 4) e.AddScene();
        e.StopTransport();
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        return e;
    }

    // An instrument track sounding a long note, recorded into an armed audio track.
    private static NotaEngine Resampling(out int rec)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        int src = e.AddBassSynthTrack();
        int mc = e.AddMidiClip(src, 0, 64);
        e.SetClipNotes(src, mc, new[] { new NotaNote(36, 0.0, 64.0, 1.0f) });
        rec = e.AddAudioTrack();
        e.SetTrackRecordInput(rec, src);
        e.SetTrackArmed(rec, true);
        while (e.SceneCount < 4) e.AddScene();
        e.StopTransport();
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        return e;
    }
}
