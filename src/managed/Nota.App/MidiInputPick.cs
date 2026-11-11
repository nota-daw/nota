// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// An instrument track's MIDI input pickers (track menu, inspector) — specs/086. All inputs (the
// computer keyboard, gamepad and Nota Remote too) or one connected MIDI device, and a channel:
// all or 1–16. A device the track names that isn't connected stays listed, greyed, so the pick
// isn't lost (its label says "not connected"); an MPE setup with a single channel gets a warning (MPE spreads notes over
// channels).

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

internal static class MidiInputPick
{
    /// <summary>The device entries: All inputs, then every enabled connected input, then the
    /// track's own device when it isn't connected.</summary>
    public static List<(string? Uid, string Label, bool Connected)> Devices(IAudioEngine engine, int trackId)
    {
        var cur = engine.GetTrackMidiInput(trackId);
        var list = new List<(string?, string, bool)> { (null, Loc.T("midi_input.all_inputs"), true) };
        IReadOnlyList<MidiDevice> devices;
        try { devices = App.Services.GetService<IMidiDeviceService>()?.InputDevices() ?? Array.Empty<MidiDevice>(); }
        catch (InvalidOperationException) { devices = Array.Empty<MidiDevice>(); }
        bool listed = cur.Uid is null;
        foreach (var d in devices)
        {
            if (!engine.IsMidiInputEnabled(d.Uid) && d.Uid != cur.Uid) continue;
            list.Add((d.Uid, d.Name.Length > 0 ? d.Name : d.Uid, cur.Uid != d.Uid || cur.Connected));
            if (d.Uid == cur.Uid) listed = true;
        }
        if (!listed) list.Add((cur.Uid, Loc.F("midi_input.not_connected", cur.Uid!), false));
        return list;
    }

    /// <summary>What the track listens to, short: "All inputs", a device, "· Ch 3".</summary>
    public static string ShortLabel(IAudioEngine engine, int trackId)
    {
        var cur = engine.GetTrackMidiInput(trackId);
        string dev = Loc.T("midi_input.all_inputs");
        foreach (var d in Devices(engine, trackId))
            if (d.Uid == cur.Uid) { dev = d.Label; break; }
        return cur.Channel > 0 ? Loc.F("midi_input.device_channel", dev, cur.Channel) : dev;
    }

    /// <summary>The channel entries: All, then 1–16.</summary>
    public static List<(int Channel, string Label)> Channels()
    {
        var list = new List<(int, string)> { (0, Loc.T("midi_input.all_channels")) };
        for (int c = 1; c <= 16; c++) list.Add((c, Loc.F("midi_input.channel_n", c)));
        return list;
    }

    /// <summary>One entry of the compact MIDI selectors (header, mixer, Session I/O): a live
    /// input — All inputs or a device, keeping the track's channel — or another instrument
    /// track to take MIDI from (<see cref="FromTrack"/> &gt; 0).</summary>
    public readonly record struct Entry(string? Uid, int FromTrack, string Label);

    /// <summary>The compact selectors' list: the live inputs (the current one with its channel),
    /// then "From" each other instrument track; and which entry is current. A MIDI In track that
    /// was removed stays listed, as "From a removed track" (#504), so the selector never shows
    /// another source as the one in use.</summary>
    public static (List<Entry> Items, int Current) Compact(IAudioEngine engine, int trackId, IEnumerable<(int Id, string Name)> instruments)
    {
        var cur = engine.GetTrackMidiInput(trackId);
        int from = engine.GetTrackMidiSource(trackId);
        var items = new List<Entry>();
        int current = 0;
        foreach (var d in Devices(engine, trackId))
        {
            bool isCur = d.Uid == cur.Uid;
            if (isCur && from <= 0) current = items.Count;
            items.Add(new Entry(d.Uid, -1, isCur && cur.Channel > 0 ? Loc.F("midi_input.device_channel", d.Label, cur.Channel) : d.Label));
        }
        bool listed = from <= 0;
        foreach (var (id, name) in instruments)
        {
            if (id == trackId) continue;
            if (id == from) { current = items.Count; listed = true; }
            items.Add(new Entry(cur.Uid, id, Loc.F("midi_input.from_track", name)));
        }
        if (!listed)
        {
            current = items.Count;
            items.Add(new Entry(cur.Uid, from, Loc.T("midi_input.from_removed_track")));
        }
        return (items, current);
    }

    /// <summary>Applies a compact entry: a live input clears MIDI from; a track sets it.</summary>
    public static void Apply(IAudioEngine engine, int trackId, Entry e)
    {
        if (e.FromTrack > 0)
        {
            engine.SetTrackMidiSource(trackId, e.FromTrack);
            return;
        }
        engine.SetTrackMidiInput(trackId, e.Uid, engine.GetTrackMidiInput(trackId).Channel);
        engine.SetTrackMidiSource(trackId, -1);
    }

    /// <summary>MPE input on and the track listening to one channel: an MPE controller plays
    /// each note on its own channel, so most of them would not arrive.</summary>
    public static bool MpeWarning(IAudioEngine engine, int trackId)
        => engine.GetTrackMidiInput(trackId).Channel > 0 && engine.GetMpe().Enabled;

    /// <summary>MIDI input ▸ for the track menu: the devices, Channel ▸, the MPE warning.</summary>
    public static MenuItem Menu(IAudioEngine engine, int trackId, Action? edited = null)
    {
        var cur = engine.GetTrackMidiInput(trackId);
        var menu = MenuKit.ValueItem(Loc.T("midi_input.menu"), GlyphKind.Input, ShortLabel(engine, trackId));
        foreach (var d in Devices(engine, trackId))
        {
            var mi = new MenuItem { Header = d.Label, ToggleType = MenuItemToggleType.Radio, IsChecked = d.Uid == cur.Uid };
            string? uid = d.Uid;
            mi.Click += (_, _) => { engine.SetTrackMidiInput(trackId, uid, cur.Channel); edited?.Invoke(); };
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var ch = MenuKit.ValueItem(Loc.T("midi_input.channel"), null,
            cur.Channel > 0 ? cur.Channel.ToString(System.Globalization.CultureInfo.CurrentCulture) : Loc.T("midi_input.all_channels"));
        foreach (var (c, label) in Channels())
        {
            var mi = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = c == cur.Channel };
            int channel = c;
            mi.Click += (_, _) => { engine.SetTrackMidiInput(trackId, cur.Uid, channel); edited?.Invoke(); };
            ch.Items.Add(mi);
        }
        menu.Items.Add(ch);
        if (MpeWarning(engine, trackId))
            menu.Items.Add(new MenuItem { Header = Loc.T("midi_input.mpe_warning"), IsEnabled = false });
        return menu;
    }
}
