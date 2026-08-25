// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MCP tools — MIDI clips and their notes (the piano roll / clip editor). Notes are beat-relative
// to the clip start. pitch is a MIDI note number (60 = C4/middle C), velocity 0..1.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class MidiTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record Note(
        [property: Description("MIDI note number, 60 = middle C")] int Pitch,
        [property: Description("Start in beats, relative to the clip")] double Start,
        [property: Description("Length in beats")] double Length,
        [property: Description("Velocity 0..1")] float Velocity);

    private static NotaNote ToEngine(Note n) => new(n.Pitch, n.Start, n.Length, Math.Clamp(n.Velocity, 0f, 1f));
    private static Note FromEngine(NotaNote n) => new(n.Pitch, n.StartBeat, n.LengthBeats, n.Velocity);

    [McpServerTool(Name = "add_midi_clip"), Description("Add an empty MIDI clip to an instrument track at startBeat for lengthBeats. Returns the clip index.")]
    public Task<int> AddMidiClip(int trackId, double startBeat, double lengthBeats)
        => Mutate(() => E.AddMidiClip(trackId, Math.Max(0, startBeat), Math.Max(0.25, lengthBeats)));

    [McpServerTool(Name = "get_clip_notes"), Description("Get the notes of a MIDI clip (pitch, start, length in beats, velocity).")]
    public Task<Note[]> GetClipNotes(int trackId, int clipIndex)
        => Read(() => Array.ConvertAll(E.GetClipNotes(trackId, clipIndex), FromEngine));

    [McpServerTool(Name = "set_clip_notes"), Description("Replace all notes in a MIDI clip with the given note list.")]
    public Task SetClipNotes(int trackId, int clipIndex, Note[] notes)
        => Mutate(() => E.SetClipNotes(trackId, clipIndex, Array.ConvertAll(notes, ToEngine)));

    [McpServerTool(Name = "add_notes"), Description("Append notes to a MIDI clip, keeping the existing ones.")]
    public Task AddNotes(int trackId, int clipIndex, Note[] notes) => Mutate(() =>
    {
        var existing = E.GetClipNotes(trackId, clipIndex);
        var merged = new NotaNote[existing.Length + notes.Length];
        existing.CopyTo(merged, 0);
        for (int i = 0; i < notes.Length; i++) merged[existing.Length + i] = ToEngine(notes[i]);
        E.SetClipNotes(trackId, clipIndex, merged);
    });

    [McpServerTool(Name = "clear_notes"), Description("Remove all notes from a MIDI clip.")]
    public Task ClearNotes(int trackId, int clipIndex) => Mutate(() => E.SetClipNotes(trackId, clipIndex, Array.Empty<NotaNote>()));

    [McpServerTool(Name = "move_clip"), Description("Move a clip to a new start beat.")]
    public Task MoveClip(int trackId, int clipIndex, double newStartBeat) => Mutate(() => E.MoveClip(trackId, clipIndex, Math.Max(0, newStartBeat)));

    [McpServerTool(Name = "trim_clip"), Description("Resize a clip to a new start + length in beats.")]
    public Task TrimClip(int trackId, int clipIndex, double newStartBeat, double newLengthBeats)
        => Mutate(() => E.TrimClip(trackId, clipIndex, Math.Max(0, newStartBeat), Math.Max(0.25, newLengthBeats)));
}
