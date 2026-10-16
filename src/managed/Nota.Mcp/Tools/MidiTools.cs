// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
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

    public sealed record ControllerPoint(
        [property: Description("Beat, relative to the clip start")] double Beat,
        [property: Description("Value: a CC 0..1 (data / 127; sustain and other pedals are down at >= 0.5), the pitch wheel -1..1 (0 = centre), channel pressure 0..1")] float Value);

    public sealed record ClipController(
        [property: Description("Controller id: 0..127 a CC, 128 the pitch wheel, 129 channel pressure")] int Ctl,
        [property: Description("Its name: Sustain, Mod wheel, Pitch wheel, Expression, CC 3, ...")] string Name,
        ControllerPoint[] Points);

    // A controller by number or name: "sustain", "mod_wheel", "pitch_wheel", "channel_pressure", "expression", "64", "cc74".
    private static int ResolveCtl(string ctl)
    {
        var k = ctl.Trim().ToLowerInvariant().Replace(" ", "_");
        if (k.StartsWith("cc")) k = k[2..].TrimStart('_', ' ');
        int id = k switch
        {
            "sustain" or "sustain_pedal" or "pedal" => MidiController.Sustain,
            "mod" or "mod_wheel" or "modwheel" or "modulation" => MidiController.ModWheel,
            "pitch_wheel" or "pitchwheel" or "pitch_bend" or "bend" or "wheel" => MidiController.PitchWheel,
            "channel_pressure" or "pressure" or "aftertouch" => MidiController.ChannelPressure,
            "breath" => 2,
            "expression" => 11,
            _ => int.TryParse(k, out var n) ? n : -1,
        };
        if (MidiController.IsReserved(id))
            throw new ArgumentException($"'{ctl}' is not a controller Nota records (0..127 except 6, 38, 96-101, 120-127; pitch_wheel; channel_pressure).");
        return id;
    }

    [McpServerTool(Name = "get_clip_controllers"), Description(
        "Get a MIDI clip's controller curves — the sustain pedal, mod wheel, pitch wheel, channel pressure and any CC recorded " +
        "with the notes or drawn in the piano roll. Each curve: its controller id and name, and its points (beat in the clip, " +
        "value). Pedals step from point to point; the others glide linearly. Before a curve's first point the controller " +
        "keeps whatever value it had.")]
    public Task<ClipController[]> GetClipControllers(int trackId, int clipIndex) => Read(() =>
        Array.ConvertAll(E.GetMidiClipControllers(trackId, clipIndex), ctl => new ClipController(ctl, MidiController.Name(ctl),
            Array.ConvertAll(E.GetMidiClipController(trackId, clipIndex, ctl), p => new ControllerPoint(p.Beat, p.Value)))));

    [McpServerTool(Name = "set_clip_controller"), Description(
        "Replace one controller curve of a MIDI clip (an empty list removes it); one undo step, heard on the next pass. ctl: a " +
        "CC number (\"64\", \"cc74\") or a name (\"sustain\", \"mod_wheel\", \"pitch_wheel\", \"channel_pressure\", " +
        "\"expression\", \"breath\"). Example — the pedal down on beat 0, up just before the chord change on beat 4: " +
        "[{beat:0,value:1},{beat:3.9,value:0}].")]
    public Task SetClipController(int trackId, int clipIndex, string ctl, ControllerPoint[] points) => Mutate(() =>
    {
        int id = ResolveCtl(ctl);
        var (lo, hi) = MidiController.Range(id);
        E.SetMidiClipController(trackId, clipIndex, id,
            Array.ConvertAll(points, p => new AutomationPoint(Math.Max(-64, p.Beat), Math.Clamp(p.Value, lo, hi))));
    });

    public sealed record ClipRef(int TrackId, int ClipIndex);

    [McpServerTool(Name = "consolidate_clips"), Description(
        "Consolidate (like Ableton's Cmd+J): on each given MIDI or audio track, replace everything in " +
        "[startBeat, endBeat) with ONE clip spanning the range. MIDI notes are merged; audio clips are " +
        "rendered (gain, pitch, warp, fades, clip envelopes baked in) into a new sample. Parts of clips " +
        "outside the range are kept. Returns the new clips (empty when no track had content there).")]
    public Task<ClipRef[]> ConsolidateClips(int[] trackIds, double startBeat, double endBeat) => Mutate(() =>
        E.ConsolidateRange(trackIds, Math.Max(0, startBeat), endBeat)
            ? Array.ConvertAll(E.LastPlacedClips(), p => new ClipRef(p.trackId, p.clipIndex))
            : Array.Empty<ClipRef>());
}
