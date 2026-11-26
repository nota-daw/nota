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
        [property: Description("Velocity 0..1")] float Velocity,
        [property: Description("Articulation id in the track's articulation map, 0 = none. Keep the value get_clip_notes returned when you write a note back.")] int Articulation = 0);

    // The articulation travels both ways, so a get → edit → set round trip keeps it (specs/085).
    private static NotaNote ToEngine(Note n) => new(n.Pitch, n.Start, n.Length, Math.Clamp(n.Velocity, 0f, 1f)) { ArticulationId = Math.Max(0, n.Articulation) };
    private static Note FromEngine(NotaNote n) => new(n.Pitch, n.StartBeat, n.LengthBeats, n.Velocity, n.ArticulationId);

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

    // ---- External MIDI (specs/115) ------------------------------------------------------------

    public sealed record MidiOutput(
        [property: Description("Stable output id, for set_external_midi")] string Uid,
        [property: Description("Human-readable output name")] string Name);

    public sealed record ExternalMidi(
        [property: Description("The output's uid, or null when the track follows a role or has none")] string? OutputUid,
        [property: Description("The output's name (its saved name while it is not connected)")] string? OutputName,
        [property: Description("The project output role it follows, or null")] string? Role,
        [property: Description("1-16, or \"keep\" for Keep channels (each note on its own MPE member channel)")] string Channel,
        [property: Description("available, missing (not connected or listed twice) or none (no output chosen)")] string Availability,
        [property: Description("Messages dropped because the output ring was full")] uint Dropped);

    [McpServerTool(Name = "list_midi_outputs"), Description("List the MIDI outputs an External MIDI track can play (uid + name).")]
    public Task<MidiOutput[]> ListMidiOutputs() => Read(() => E.MidiOutputs().Select(o => new MidiOutput(o.Uid, o.Name)).ToArray());

    [McpServerTool(Name = "set_external_midi"), Description("Point an External MIDI track (add one with add_instrument_track kind 19) at a MIDI output by uid (from list_midi_outputs) and a channel: 1-16 sends every message on that channel, \"keep\" gives each note its own MPE member channel (for MPE synths). A port that isn't connected is kept and receives nothing until it is — a similarly named one never stands in. One undo step; notes the track held end at its old output.")]
    public Task SetExternalMidi(int trackId, string outputUid, string channel = "1") => Mutate(() =>
    {
        int ch = ParseChannel(channel);
        string name = E.MidiOutputs().FirstOrDefault(o => o.Uid == outputUid).Name ?? "";
        if (!E.SetExternalMidi(trackId, outputUid ?? "", ch, name))
            throw new ArgumentException($"Track {trackId} isn't an External MIDI track (add one with add_instrument_track kind 19).");
    });

    [McpServerTool(Name = "get_external_midi"), Description("Read an External MIDI track's output, channel and whether its output is connected.")]
    public Task<ExternalMidi> GetExternalMidi(int trackId) => Read(() =>
    {
        var d = E.GetExternalMidi(trackId) ?? throw new ArgumentException($"Track {trackId} isn't an External MIDI track.");
        string? roleName = d.RoleId.Length > 0 ? E.MidiRoles().FirstOrDefault(r => r.Id == d.RoleId)?.Name ?? d.RoleId : null;
        int status = E.ExternalMidiStatus(trackId);
        string avail = status == 1 ? "available" : status == 2 ? "missing" : "none";
        bool byRole = d.RoleId.Length > 0;
        return new ExternalMidi(byRole ? null : NullIfEmpty(d.OutputUid), NullIfEmpty(d.OutputName), roleName,
            d.Channel == ExternalMidiDestination.KeepChannels ? "keep" : d.Channel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            avail, E.ExternalMidiDropped(trackId));
    });

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static int ParseChannel(string channel)
    {
        if (string.Equals(channel?.Trim(), "keep", StringComparison.OrdinalIgnoreCase)) return ExternalMidiDestination.KeepChannels;
        if (int.TryParse(channel, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int c) && c is >= 1 and <= 16) return c;
        throw new ArgumentException($"channel {channel}: 1-16 or \"keep\"");
    }

    // ---- MIDI routes overview and output roles (specs/115 US3, US4) -----------------------------

    public sealed record RouteEnd(string Key, string Name, string Detail,
        [property: Description("available, missing, ambiguous, unbound or over-limit")] string Availability);

    public sealed record MidiRoute(int TrackId, string TrackName,
        [property: Description("True when the track listens to every input")] bool AllInputs,
        [property: Description("Channels of every input, e.g. \"3, 7, 11\"; empty = all")] string AllInputsChannels,
        [property: Description("The ports it listens to, Detail = their channels (empty = all)")] RouteEnd[] Inputs,
        [property: Description("The track it takes MIDI from (Key track:id), or null")] RouteEnd? MidiFrom,
        string Instrument,
        [property: Description("External MIDI's port or role (Key role:id), Detail = channel or keep; null for a local instrument")] RouteEnd? Destination);

    public sealed record OutputRole(string Id, string Name, string? OutputUid, string? OutputName, string Availability, string[] UsedBy);

    private static string Avail(Nota.Application.Midi.RouteAvailability a) => a switch
    {
        Nota.Application.Midi.RouteAvailability.Available => "available",
        Nota.Application.Midi.RouteAvailability.Ambiguous => "ambiguous",
        Nota.Application.Midi.RouteAvailability.Unbound => "unbound",
        Nota.Application.Midi.RouteAvailability.OverLimit => "over-limit",
        _ => "missing",
    };

    private static RouteEnd? End(Nota.Application.Midi.MidiRouteEnd? e) => e is null ? null : new RouteEnd(e.Key, e.Name, e.Detail, Avail(e.Availability));

    [McpServerTool(Name = "get_midi_routes"), Description("The MIDI routes overview: one row per instrument track with what it listens to (every input, or ports with their channels), the track it takes MIDI from, and where an External MIDI track sends (port or role, channel), each marked available or missing. Optional filters: source = a port uid or \"track:<id>\"; destination = an output uid or \"role:<id>\" (a port also matches tracks following a role bound to it). Read-only.")]
    public Task<MidiRoute[]> GetMidiRoutes(string? source = null, string? destination = null) => Read(() =>
        Nota.Application.Midi.MidiRoutesQuery.Rows(E, source, destination).Select(r => new MidiRoute(r.TrackId, r.TrackName,
            r.AllInputs, r.AllInputsChannels, r.Inputs.Select(i => End(i)!).ToArray(), End(r.MidiFrom), r.InstrumentName, End(r.Destination))).ToArray());

    [McpServerTool(Name = "list_midi_output_roles"), Description("List the project's MIDI output roles (named destinations like \"Stage Synth\" that External MIDI tracks follow): id, name, bound output, whether it is connected, and the tracks that follow it.")]
    public Task<OutputRole[]> ListMidiOutputRoles() => Read(() =>
    {
        var outputs = E.MidiOutputs();
        return E.MidiRoles().Select(r => new OutputRole(r.Id, r.Name, r.OutputUid.Length > 0 ? r.OutputUid : null,
            r.OutputName.Length > 0 ? r.OutputName : null,
            r.OutputUid.Length == 0 ? "unbound" : Avail(Nota.Application.Midi.MidiRouteResolver.ResolveOutput(outputs, r.OutputUid)),
            Nota.Application.Midi.MidiOutputRoles.UsedBy(E, r.Id).Select(E.GetTrackName).ToArray())).ToArray();
    });

    [McpServerTool(Name = "create_midi_output_role"), Description("Create a project MIDI output role, optionally bound to an output (uid from list_midi_outputs). Names are unique (case-insensitive), 1-40 characters. Returns its id.")]
    public Task<string> CreateMidiOutputRole(string name, string? outputUid = null) => Mutate(() =>
        Nota.Application.Midi.MidiOutputRoles.Create(E, name, outputUid, outputUid is null ? null : E.MidiOutputs().FirstOrDefault(o => o.Uid == outputUid).Name));

    [McpServerTool(Name = "rename_midi_output_role"), Description("Rename a MIDI output role (by id or name).")]
    public Task RenameMidiOutputRole(string role, string name) => Mutate(() =>
        Nota.Application.Midi.MidiOutputRoles.Rename(E, RoleId(role), name));

    [McpServerTool(Name = "rebind_midi_output_role"), Description("Bind a MIDI output role (by id or name) to an output: every External MIDI track that follows it moves there, keeping its channel — one undo step; notes they hold end at the old output. With preview = true, only returns the track names that would move. Refused while the transport plays.")]
    public Task<string[]> RebindMidiOutputRole(string role, string outputUid, bool preview = false) => Mutate(() =>
    {
        string id = RoleId(role);
        var moved = Nota.Application.Midi.MidiOutputRoles.UsedBy(E, id).Select(E.GetTrackName).ToArray();
        if (!preview)
            Nota.Application.Midi.MidiOutputRoles.Rebind(E, id, outputUid, E.MidiOutputs().FirstOrDefault(o => o.Uid == outputUid).Name ?? "");
        return moved;
    });

    [McpServerTool(Name = "delete_midi_output_role"), Description("Delete a MIDI output role (by id or name). Refused while any track follows it — the message names them.")]
    public Task DeleteMidiOutputRole(string role) => Mutate(() => Nota.Application.Midi.MidiOutputRoles.Delete(E, RoleId(role)));

    [McpServerTool(Name = "set_external_midi_role"), Description("Point an External MIDI track at a project MIDI output role (by id or name) instead of a port; its channel stays.")]
    public Task SetExternalMidiRole(int trackId, string role) => Mutate(() =>
    {
        if (!Nota.Application.Midi.MidiOutputRoles.Assign(E, trackId, RoleId(role)))
            throw new ArgumentException($"Track {trackId} isn't an External MIDI track.");
    });

    private string RoleId(string role) =>
        Nota.Application.Midi.MidiOutputRoles.Find(E, role)?.Id ?? throw new ArgumentException($"No MIDI output role \"{role}\".");
}
