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
