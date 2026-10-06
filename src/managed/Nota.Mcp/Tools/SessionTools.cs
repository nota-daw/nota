// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — the Session (clip-launcher) view: a grid of slots (scene × track). A slot holds a
// looping MIDI or audio clip you launch/stop live; launches quantise to the global launch grid
// (or the clip's own quantum). Clips carry a name, colour and launch properties (launch mode,
// legato, loop, follow actions); scenes carry a name, colour, tempo / signature and a scene
// follow action. Slot state: 0 empty, 1 filled, 2 queued (about to start), 3 playing,
// 4 recording.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class SessionTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record Note(int Pitch, double Start, double Length, float Velocity);
    public sealed record FollowAction(string Action, int Chance);
    public sealed record Slot(int TrackId, int Scene, string State, double LengthBeats, string Name, int Color,
        string LaunchMode, double? QuantBeats, bool Legato, bool Loop, float VelocityAmount,
        FollowAction A, FollowAction B, double FollowBeats, int JumpScene);
    public sealed record Scene(int Index, string Name, int Color, double? Tempo, string? Signature, double? FollowNextAfterBeats);
    public sealed record TrackState(int TrackId, int PlayingScene, double PositionBeats);
    public sealed record SlotRef(int TrackId, int Scene);
    public sealed record SessionSnapshot(int SceneCount, double LaunchQuantBeats, bool FollowActions, double RecordLengthBeats,
        Scene[] Scenes, Slot[] FilledSlots, TrackState[] Playing, SlotRef[] NoStopButton);

    private static readonly string[] Modes = { "trigger", "gate", "toggle", "repeat" };
    private static readonly string[] Actions = { "none", "stop", "again", "previous", "next", "first", "last", "any", "other", "jump" };

    private static string StateName(int s) => s switch { 4 => "recording", 3 => "playing", 2 => "queued", 1 => "filled", _ => "empty" };
    private static NotaNote ToEngine(Note n) => new(n.Pitch, n.Start, n.Length, Math.Clamp(n.Velocity, 0f, 1f));
    private static Note FromEngine(NotaNote n) => new(n.Pitch, n.StartBeat, n.LengthBeats, n.Velocity);

    private static int Parse(string? value, string[] names, string what)
    {
        int i = Array.IndexOf(names, (value ?? "").Trim().ToLowerInvariant());
        if (i < 0) throw new ArgumentException($"Unknown {what} \"{value}\" — use one of: {string.Join(", ", names)}.");
        return i;
    }

    [McpServerTool(Name = "get_session"), Description(
        "Snapshot of the Session (clip-launcher) grid: scene count + scene names/colours/tempo/signature/follow, "
        + "the launch quantum, the global follow-action switch, the fixed record length, every non-empty slot "
        + "(track id, scene, state, loop length in beats, name, colour, launch mode, quantum, legato, loop, velocity "
        + "amount, follow actions A/B with chances and time), which scene each track is playing (with its position "
        + "in the loop), and empty slots whose stop button was removed. Call before launching or filling slots.")]
    public Task<SessionSnapshot> GetSession() => Read(() =>
    {
        int scenes = E.SceneCount, n = E.TrackCount;
        var slots = new List<Slot>();
        var playing = new List<TrackState>();
        var noStop = new List<SlotRef>();
        for (int i = 0; i < n; i++)
        {
            if (!E.TryGetTrackInfo(i, out var ti) || ti.IsReturn || ti.IsGroup) continue;
            int p = E.SessionPlayingSlot(ti.Id);
            if (p >= 0) playing.Add(new TrackState(ti.Id, p, Math.Round(E.SessionSlotPosition(ti.Id), 3)));
            for (int s = 0; s < scenes; s++)
            {
                int st = E.SessionSlotState(ti.Id, s);
                if (st == 0)
                {
                    if (!E.GetSessionSlotStopButton(ti.Id, s)) noStop.Add(new SlotRef(ti.Id, s));
                    continue;
                }
                E.TryGetSessionClipProps(ti.Id, s, out var c);
                slots.Add(new Slot(ti.Id, s, StateName(st), E.SessionSlotLength(ti.Id, s), E.GetSessionClipName(ti.Id, s), c.Color,
                    Modes[Math.Clamp(c.LaunchMode, 0, 3)], c.QuantBeats < 0 ? null : c.QuantBeats, c.Legato != 0, c.Loop != 0, c.VelocityAmount,
                    new FollowAction(Actions[Math.Clamp(c.FollowA, 0, 9)], c.ChanceA), new FollowAction(Actions[Math.Clamp(c.FollowB, 0, 9)], c.ChanceB),
                    c.FollowBeats, c.JumpScene));
            }
        }
        var sceneList = new List<Scene>();
        for (int s = 0; s < scenes; s++)
        {
            E.TryGetSceneProps(s, out var p);
            sceneList.Add(new Scene(s, E.GetSceneName(s), p.Color, p.Tempo > 0 ? p.Tempo : null,
                p.SigNum > 0 && p.SigDen > 0 ? $"{p.SigNum}/{p.SigDen}" : null, p.Follow != 0 ? p.FollowBeats : null));
        }
        return new SessionSnapshot(scenes, E.LaunchQuant, E.SessionFollow, E.SessionRecordLength,
            sceneList.ToArray(), slots.ToArray(), playing.ToArray(), noStop.ToArray());
    });

    // ---- slots -------------------------------------------------------------------------------

    [McpServerTool(Name = "add_session_midi_clip"), Description("Create an empty looping MIDI clip in a slot (track × scene) of the given length in beats.")]
    public Task AddSessionMidiClip(int trackId, int scene, double lengthBeats = 4.0)
        => Mutate(() => E.AddSessionMidiClip(trackId, scene, Math.Max(0.25, lengthBeats)));

    [McpServerTool(Name = "add_session_audio_file"), Description("Load an audio file (WAV/FLAC/MP3) into a session slot as a looping clip.")]
    public Task<bool> AddSessionAudioFile(int trackId, int scene, string path) => Mutate(() => E.AddSessionAudioFile(trackId, scene, path));

    [McpServerTool(Name = "clear_session_slot"), Description("Delete the clip in a session slot (stops it first if it is playing). Returns false if the slot was already empty.")]
    public Task<bool> ClearSessionSlot(int trackId, int scene) => Mutate(() => E.ClearSessionSlot(trackId, scene));

    [McpServerTool(Name = "copy_session_slot"), Description(
        "Copy a slot's clip (notes or audio, name, colour, launch properties) onto another slot, overwriting it. "
        + "Both tracks must be the same kind (MIDI→MIDI, audio→audio). move=true clears the source afterwards. One undo step.")]
    public Task<bool> CopySessionSlot(int srcTrackId, int srcScene, int dstTrackId, int dstScene, bool move = false) => Mutate(() =>
    {
        E.BeginUndoGroup();
        try
        {
            bool ok = E.CopySessionSlot(srcTrackId, srcScene, dstTrackId, dstScene);
            if (ok && move && (srcTrackId, srcScene) != (dstTrackId, dstScene)) E.ClearSessionSlot(srcTrackId, srcScene);
            return ok;
        }
        finally { E.EndUndoGroup(); }
    });

    [McpServerTool(Name = "get_session_notes"), Description("Get the notes of a session MIDI slot (pitch, start, length in beats, velocity).")]
    public Task<Note[]> GetSessionNotes(int trackId, int scene)
        => Read(() => Array.ConvertAll(E.GetSessionNotes(trackId, scene), FromEngine));

    [McpServerTool(Name = "set_session_notes"), Description("Replace all notes in a session MIDI slot (the slot must already hold a MIDI clip).")]
    public Task SetSessionNotes(int trackId, int scene, Note[] notes)
        => Mutate(() => E.SetSessionNotes(trackId, scene, Array.ConvertAll(notes, ToEngine)));

    [McpServerTool(Name = "set_session_slot_length"), Description("Set a session slot's loop length in beats.")]
    public Task SetSessionSlotLength(int trackId, int scene, double lengthBeats)
        => Mutate(() => E.SetSessionSlotLength(trackId, scene, Math.Max(0.25, lengthBeats)));

    [McpServerTool(Name = "set_session_clip"), Description(
        "Edit a filled session slot's clip; omit any argument to leave it unchanged. name: the clip name (empty = unnamed). "
        + "color: a track-palette index (base*3+shade, 0..23), -1 = the track's colour. launchMode: trigger | gate (plays while "
        + "held) | toggle (second launch stops) | repeat (retriggers each quantum while held). quantBeats: the clip's own launch "
        + "quantum in beats (0 = immediate), -1 = global. legato: start at the outgoing clip's position. loop: false = one-shot. "
        + "velocityAmount 0..1: how far launch velocity scales the clip. followA/followB: none | stop | again | previous | next | "
        + "first | last | any | other | jump; chanceA/chanceB: relative weights 0..100; followBeats: when the follow action fires "
        + "(0 = after one pass); jumpScene: the target of 'jump'. Follow actions run only while the global switch is on.")]
    public Task SetSessionClip(int trackId, int scene, string? name = null, int? color = null, string? launchMode = null,
        double? quantBeats = null, bool? legato = null, bool? loop = null, float? velocityAmount = null,
        string? followA = null, int? chanceA = null, string? followB = null, int? chanceB = null,
        double? followBeats = null, int? jumpScene = null) => Mutate(() =>
    {
        if (!E.TryGetSessionClipProps(trackId, scene, out var p))
            throw new ArgumentException($"Slot (track {trackId}, scene {scene}) is empty.");
        E.BeginUndoGroup();
        try
        {
            if (name is not null) E.SetSessionClipName(trackId, scene, name);
            if (color is { } c) p.Color = c;
            if (launchMode is not null) p.LaunchMode = Parse(launchMode, Modes, "launch mode");
            if (quantBeats is { } q) p.QuantBeats = q < 0 ? -1 : q;
            if (legato is { } l) p.Legato = l ? 1 : 0;
            if (loop is { } lp) p.Loop = lp ? 1 : 0;
            if (velocityAmount is { } v) p.VelocityAmount = Math.Clamp(v, 0f, 1f);
            if (followA is not null) p.FollowA = Parse(followA, Actions, "follow action");
            if (followB is not null) p.FollowB = Parse(followB, Actions, "follow action");
            if (chanceA is { } ca) p.ChanceA = Math.Clamp(ca, 0, 100);
            if (chanceB is { } cb) p.ChanceB = Math.Clamp(cb, 0, 100);
            if (followBeats is { } fb) p.FollowBeats = Math.Max(0, fb);
            if (jumpScene is { } js) p.JumpScene = Math.Max(0, js);
            E.SetSessionClipProps(trackId, scene, p);
        }
        finally { E.EndUndoGroup(); }
    });

    [McpServerTool(Name = "set_session_slot_stop_button"), Description(
        "An EMPTY slot's stop button. on=true (default): launching that scene stops the track. on=false: the scene leaves "
        + "the track playing — e.g. keep the drums going while scenes change.")]
    public Task SetSessionSlotStopButton(int trackId, int scene, bool on) => Mutate(() => E.SetSessionSlotStopButton(trackId, scene, on));

    // ---- scenes ------------------------------------------------------------------------------

    [McpServerTool(Name = "set_scene"), Description(
        "Edit a scene row; omit any argument to leave it unchanged. name (empty = unnamed). color: a track-palette index, "
        + "-1 = none. tempo: BPM applied when the scene launches (0 = no change). signature: \"3/4\" etc. applied on launch "
        + "(\"\" = no change). followNextAfterBeats: launch the next scene this many beats after this one starts (0 = off).")]
    public Task SetScene(int scene, string? name = null, int? color = null, double? tempo = null, string? signature = null,
        double? followNextAfterBeats = null) => Mutate(() =>
    {
        if (!E.TryGetSceneProps(scene, out var p)) throw new ArgumentException($"No scene {scene}.");
        E.BeginUndoGroup();
        try
        {
            if (name is not null) E.SetSceneName(scene, name);
            if (color is { } c) p.Color = c;
            if (tempo is { } t) p.Tempo = Math.Max(0, t);
            if (signature is not null)
            {
                var parts = signature.Split('/');
                if (signature.Trim().Length == 0) { p.SigNum = 0; p.SigDen = 0; }
                else if (parts.Length == 2 && int.TryParse(parts[0], out int num) && int.TryParse(parts[1], out int den)) { p.SigNum = num; p.SigDen = den; }
                else throw new ArgumentException($"Signature \"{signature}\" should look like 3/4.");
            }
            if (followNextAfterBeats is { } f) { p.Follow = f > 0 ? 1 : 0; if (f > 0) p.FollowBeats = f; }
            E.SetSceneProps(scene, p);
        }
        finally { E.EndUndoGroup(); }
    });

    [McpServerTool(Name = "insert_scene"), Description("Insert an empty scene row at index `at` (later rows shift down; playing clips keep playing). Returns the new row.")]
    public Task<int> InsertScene(int at) => Mutate(() => E.InsertScene(at));

    [McpServerTool(Name = "duplicate_scene"), Description("Copy a scene row (its clips and properties) to a new row just below it. Returns the new row.")]
    public Task<int> DuplicateScene(int scene) => Mutate(() => E.DuplicateScene(scene));

    [McpServerTool(Name = "capture_scene"), Description("Capture and insert scene: a new row at `at` holding the clips playing right now. Returns the new row.")]
    public Task<int> CaptureScene(int at) => Mutate(() => E.CaptureScene(at));

    [McpServerTool(Name = "move_scene"), Description("Move a scene row (its clips on every track and its properties) to another index.")]
    public Task<bool> MoveScene(int from, int to) => Mutate(() => E.MoveScene(from, to));

    [McpServerTool(Name = "delete_scene"), Description("Delete a scene row and its clips (at least one scene always stays).")]
    public Task<bool> DeleteScene(int scene) => Mutate(() => E.RemoveScene(scene));

    // ---- launching ---------------------------------------------------------------------------

    [McpServerTool(Name = "set_launch_quant"), Description("Set the global launch quantisation in beats (e.g. 4 = 1 bar, 1 = 1 beat, 0 = off).")]
    public Task SetLaunchQuant(double beats) => Mutate(() => E.SetLaunchQuant(Math.Max(0, beats)));

    [McpServerTool(Name = "set_session_follow"), Description("Turn follow actions on or off for every clip and scene (the global Follow switch).")]
    public Task SetSessionFollow(bool on) => Mutate(() => E.SessionFollow = on);

    [McpServerTool(Name = "launch_slot"), Description(
        "Launch (start) the clip in a slot; obeys its launch quantum. velocity 0..1 scales the clip by its velocity amount. "
        + "Launching an empty slot stops the track. A gate / repeat clip keeps playing until release_slot.")]
    public Task LaunchSlot(int trackId, int scene, float velocity = 1f) => Mutate(() => E.LaunchSlotVelocity(trackId, scene, Math.Clamp(velocity, 0f, 1f)));

    [McpServerTool(Name = "release_slot"), Description("Release a slot's launch button: stops a gate or repeat clip (no effect on other launch modes).")]
    public Task ReleaseSlot(int trackId, int scene) => Mutate(() => E.ReleaseSlot(trackId, scene));

    [McpServerTool(Name = "stop_slot"), Description("Stop the currently-playing session clip on a track.")]
    public Task StopSlot(int trackId) => Mutate(() => E.StopSlot(trackId));

    [McpServerTool(Name = "launch_scene"), Description(
        "Launch a whole scene (row) — every filled slot in that scene starts together; tracks with an empty slot stop "
        + "unless that slot's stop button was removed. The scene's tempo / signature apply as it starts.")]
    public Task LaunchScene(int scene) => Mutate(() => E.LaunchScene(scene));

    [McpServerTool(Name = "stop_scene"), Description("Stop every session clip in a scene (row).")]
    public Task StopScene(int scene) => Mutate(() => E.StopScene(scene));

    [McpServerTool(Name = "stop_all_session"), Description("Stop all playing session clips on every track.")]
    public Task StopAllSession() => Mutate(() => E.StopAllSession());

    [McpServerTool(Name = "back_to_arrangement"), Description(
        "Hand tracks back to the Arrangement. With a trackId: stop just that track's session clip now. Without: stop every "
        + "session clip and re-activate the Arrangement timeline.")]
    public Task BackToArrangement(int? trackId = null) => Mutate(() =>
    {
        if (trackId is { } id) E.TrackBackToArrangement(id);
        else E.BackToArrangement();
    });

    // ---- recording ---------------------------------------------------------------------------

    [McpServerTool(Name = "record_session_slot"), Description(
        "Record a take into a session slot (MIDI: overdub, or a fresh take in an empty slot; audio: capture the track's input). "
        + "A fresh take runs until stop_session_record and is cut to whole bars, or to the fixed record length.")]
    public Task RecordSessionSlot(int trackId, int scene) => Mutate(() => E.RecordSessionSlot(trackId, scene));

    [McpServerTool(Name = "record_session_scene"), Description(
        "Session Record: record into the first armed track that has an empty slot in this scene. Returns that track id, or 0 "
        + "if no armed track has room.")]
    public Task<int> RecordSessionScene(int scene) => Mutate(() => E.RecordSessionScene(scene));

    [McpServerTool(Name = "set_session_record_length"), Description("Fixed session record length in beats (e.g. 16 = 4 bars; 0 = off): a take stops by itself after it.")]
    public Task SetSessionRecordLength(double beats) => Mutate(() => E.SessionRecordLength = Math.Max(0, beats));

    [McpServerTool(Name = "stop_session_record"), Description("Stop the in-progress session slot recording and keep the take.")]
    public Task StopSessionRecord() => Mutate(() => E.StopSessionRecord());

    // ---- transfer ----------------------------------------------------------------------------

    [McpServerTool(Name = "session_slot_to_arrangement"), Description("Copy a session slot's clip (MIDI or audio, with its name) into the arrangement timeline at startBeat. Returns the new clip index.")]
    public Task<int> SessionSlotToArrangement(int trackId, int scene, double startBeat)
        => Mutate(() => E.SessionSlotToArrangement(trackId, scene, Math.Max(0, startBeat)));

    [McpServerTool(Name = "arrangement_clip_to_session"), Description("Copy an arrangement clip (MIDI or audio, with its name) into a session slot (track × scene).")]
    public Task<bool> ArrangementClipToSession(int trackId, int clipIndex, int scene) => Mutate(() =>
    {
        if (E.TryGetClipInfo(trackId, clipIndex, out var ci) && !ci.IsMidi) return E.ArrangementAudioClipToSession(trackId, clipIndex, scene);
        E.ArrangementClipToSession(trackId, clipIndex, scene);
        return E.SessionSlotState(trackId, scene) != 0;
    });
}
