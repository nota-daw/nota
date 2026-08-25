// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MCP tools — the Session (clip-launcher) view: a grid of slots (scene × track). A slot holds a
// looping MIDI or audio clip you launch/stop live; launches quantise to the global launch grid.
// Slot state: 0 empty, 1 filled, 2 queued (about to start), 3 playing, 4 recording.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class SessionTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record Note(int Pitch, double Start, double Length, float Velocity);
    public sealed record Slot(int TrackId, int Scene, string State, double LengthBeats);
    public sealed record SessionSnapshot(int SceneCount, Slot[] FilledSlots);

    private static string StateName(int s) => s switch { 4 => "recording", 3 => "playing", 2 => "queued", 1 => "filled", _ => "empty" };
    private static NotaNote ToEngine(Note n) => new(n.Pitch, n.Start, n.Length, Math.Clamp(n.Velocity, 0f, 1f));
    private static Note FromEngine(NotaNote n) => new(n.Pitch, n.StartBeat, n.LengthBeats, n.Velocity);

    [McpServerTool(Name = "get_session"), Description(
        "Snapshot of the Session (clip-launcher) grid: scene count and every non-empty slot "
        + "(track id, scene, state, loop length in beats). Call before launching or filling slots.")]
    public Task<SessionSnapshot> GetSession() => Read(() =>
    {
        int scenes = E.SceneCount, n = E.TrackCount;
        var slots = new List<Slot>();
        for (int i = 0; i < n; i++)
        {
            if (!E.TryGetTrackInfo(i, out var ti)) continue;
            for (int s = 0; s < scenes; s++)
            {
                int st = E.SessionSlotState(ti.Id, s);
                if (st != 0) slots.Add(new Slot(ti.Id, s, StateName(st), E.SessionSlotLength(ti.Id, s)));
            }
        }
        return new SessionSnapshot(scenes, slots.ToArray());
    });

    [McpServerTool(Name = "add_session_midi_clip"), Description("Create an empty looping MIDI clip in a slot (track × scene) of the given length in beats.")]
    public Task AddSessionMidiClip(int trackId, int scene, double lengthBeats = 4.0)
        => Mutate(() => E.AddSessionMidiClip(trackId, scene, Math.Max(0.25, lengthBeats)));

    [McpServerTool(Name = "add_session_audio_file"), Description("Load an audio file (WAV/FLAC/MP3) into a session slot as a looping clip.")]
    public Task<bool> AddSessionAudioFile(int trackId, int scene, string path) => Mutate(() => E.AddSessionAudioFile(trackId, scene, path));

    [McpServerTool(Name = "get_session_notes"), Description("Get the notes of a session MIDI slot (pitch, start, length in beats, velocity).")]
    public Task<Note[]> GetSessionNotes(int trackId, int scene)
        => Read(() => Array.ConvertAll(E.GetSessionNotes(trackId, scene), FromEngine));

    [McpServerTool(Name = "set_session_notes"), Description("Replace all notes in a session MIDI slot (the slot must already hold a MIDI clip).")]
    public Task SetSessionNotes(int trackId, int scene, Note[] notes)
        => Mutate(() => E.SetSessionNotes(trackId, scene, Array.ConvertAll(notes, ToEngine)));

    [McpServerTool(Name = "set_session_slot_length"), Description("Set a session slot's loop length in beats.")]
    public Task SetSessionSlotLength(int trackId, int scene, double lengthBeats)
        => Mutate(() => E.SetSessionSlotLength(trackId, scene, Math.Max(0.25, lengthBeats)));

    [McpServerTool(Name = "set_launch_quant"), Description("Set the global launch quantisation in beats (e.g. 4 = 1 bar, 1 = 1 beat, 0 = off).")]
    public Task SetLaunchQuant(double beats) => Mutate(() => E.SetLaunchQuant(Math.Max(0, beats)));

    [McpServerTool(Name = "launch_slot"), Description("Launch (start) the clip in a slot; obeys the launch quantisation.")]
    public Task LaunchSlot(int trackId, int scene) => Mutate(() => E.LaunchSlot(trackId, scene));

    [McpServerTool(Name = "stop_slot"), Description("Stop the currently-playing session clip on a track.")]
    public Task StopSlot(int trackId) => Mutate(() => E.StopSlot(trackId));

    [McpServerTool(Name = "launch_scene"), Description("Launch a whole scene (row) — every filled slot in that scene starts together.")]
    public Task LaunchScene(int scene) => Mutate(() => E.LaunchScene(scene));

    [McpServerTool(Name = "stop_scene"), Description("Stop every session clip in a scene (row).")]
    public Task StopScene(int scene) => Mutate(() => E.StopScene(scene));

    [McpServerTool(Name = "stop_all_session"), Description("Stop all playing session clips on every track.")]
    public Task StopAllSession() => Mutate(() => E.StopAllSession());

    [McpServerTool(Name = "record_session_slot"), Description("Arm-and-record a new take into a session slot (track must have a record input / be recordable).")]
    public Task RecordSessionSlot(int trackId, int scene) => Mutate(() => E.RecordSessionSlot(trackId, scene));

    [McpServerTool(Name = "stop_session_record"), Description("Stop the in-progress session slot recording and keep the take.")]
    public Task StopSessionRecord() => Mutate(() => E.StopSessionRecord());

    [McpServerTool(Name = "session_slot_to_arrangement"), Description("Copy a session slot's clip into the arrangement timeline at startBeat. Returns the new clip index.")]
    public Task<int> SessionSlotToArrangement(int trackId, int scene, double startBeat)
        => Mutate(() => E.SessionSlotToArrangement(trackId, scene, Math.Max(0, startBeat)));

    [McpServerTool(Name = "arrangement_clip_to_session"), Description("Copy an arrangement clip into a session slot (track × scene).")]
    public Task ArrangementClipToSession(int trackId, int clipIndex, int scene)
        => Mutate(() => E.ArrangementClipToSession(trackId, clipIndex, scene));
}
