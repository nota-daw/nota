// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MCP tools — MIDI controllers & MIDI Learn. Inspect the connected MIDI input devices and the
// controls they emit, then view/manage the learned mappings that drive Nota's on-screen params
// from a hardware controller. (This is hardware MIDI Learn — distinct from MidiTools, which edits
// note data inside clips.)

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class MidiControlTools(
    IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh,
    IMidiDeviceService midiDevices, IMidiLearnAccess learn)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record MidiInputInfo(
        [property: Description("Stable device id (uid) to pass to enable/disable")] string Uid,
        [property: Description("Human-readable device name")] string Name,
        [property: Description("Whether Nota is currently listening to this input")] bool Enabled);

    [McpServerTool(Name = "list_midi_devices"), Description("List the connected MIDI input devices and whether Nota is listening to each.")]
    public Task<MidiInputInfo[]> ListMidiDevices() => Read(() =>
    {
        var devices = midiDevices.InputDevices();
        var result = new MidiInputInfo[devices.Count];
        for (int i = 0; i < devices.Count; i++)
            result[i] = new MidiInputInfo(devices[i].Uid, devices[i].Name, E.IsMidiInputEnabled(devices[i].Uid));
        return result;
    });

    [McpServerTool(Name = "set_midi_device_enabled"), Description("Start or stop listening to a MIDI input device (by uid from list_midi_devices).")]
    public Task SetMidiDeviceEnabled(string uid, bool enabled) => Mutate(() =>
    {
        E.SetMidiInputEnabled(uid, enabled);
        E.ApplyMidi();
    });

    [McpServerTool(Name = "get_midi_activity"), Description("List the distinct MIDI controls (CC / notes) seen recently across the enabled inputs — move a knob/pad on the controller, then call this to discover what it sends. Use clear_midi_activity to reset the window.")]
    public Task<MidiControlSeen[]> GetMidiActivity() => Read(() =>
    {
        var seen = learn.RecentControls();
        var arr = new MidiControlSeen[seen.Count];
        for (int i = 0; i < seen.Count; i++) arr[i] = seen[i];
        return arr;
    });

    [McpServerTool(Name = "clear_midi_activity"), Description("Forget the recorded MIDI controller activity so get_midi_activity starts a fresh discovery window.")]
    public Task ClearMidiActivity() => Mutate(() => learn.ClearRecentControls());

    [McpServerTool(Name = "get_midi_mappings"), Description("List the current MIDI-Learn mappings: which on-screen control each hardware CC/note drives, plus its output range and inversion.")]
    public Task<MidiMappingInfo[]> GetMidiMappings() => Read(() =>
    {
        var m = learn.Mappings();
        var arr = new MidiMappingInfo[m.Count];
        for (int i = 0; i < m.Count; i++) arr[i] = m[i];
        return arr;
    });

    [McpServerTool(Name = "set_midi_learn"), Description("Arm or disarm MIDI-Learn mode. While armed, mappable controls highlight in the app; the user clicks one and moves a hardware control to bind it (binding a target needs the on-screen click, so this pairs with the UI).")]
    public Task SetMidiLearn(bool armed) => Mutate(() => learn.Armed = armed);

    [McpServerTool(Name = "remove_midi_mapping"), Description("Remove the MIDI-Learn mapping at the given index (from get_midi_mappings). Returns false if the index is out of range.")]
    public Task<bool> RemoveMidiMapping(int index) => Mutate(() => learn.RemoveMapping(index));

    [McpServerTool(Name = "clear_midi_mappings"), Description("Remove all MIDI-Learn mappings.")]
    public Task ClearMidiMappings() => Mutate(() => learn.ClearMappings());

    [McpServerTool(Name = "set_midi_mapping_range"), Description("Edit a mapping's output window and inversion (index from get_midi_mappings). rangeMin/rangeMax are normalized 0..1 within the control's own range; invert flips the direction. Returns false if the index is out of range.")]
    public Task<bool> SetMidiMappingRange(int index, double rangeMin, double rangeMax, bool invert)
        => Mutate(() => learn.SetMappingRange(index, rangeMin, rangeMax, invert));
}
