// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What the phone's Macros screen walks (specs/114): the track's devices in chain order — MIDI
// effects, the instrument (or rack), the audio effects — one entry per device, each with the
// controller's pages for it (edited → catalog → plug-in default, specs/051), so the phone and the
// hardware's knobs always show the same eight parameters on the same page. A page slot resolves
// to a RemoteParam the hub reads and writes; devices inside racks are reached through their
// rack's macros, as on the controller.

using Nota.Application;
using Nota.Application.Controllers;

namespace Nota.Remote;

/// <summary>One page of a device on the phone: its name and eight knobs (null = empty).</summary>
public sealed record RemoteMacroPage(string Name, IReadOnlyList<RemoteParam?> Knobs);

/// <summary>One device of the track's chain as the phone's Macros screen shows it.</summary>
public sealed record RemoteMacroDevice(string Name, DeviceAddress Address, string TypeKey, IReadOnlyList<RemoteMacroPage> Pages)
{
    /// <summary>The device index as the older `md` field numbers it (−1 instrument, ≥ 0 audio effect), else null.</summary>
    public int? LegacyIndex => Address.Section switch
    {
        FocusSection.Instrument => -1,
        FocusSection.Audio => Address.Index,
        _ => null,
    };
}

public static class RemoteMacros
{
    /// <summary>The devices of <paramref name="trackId"/> in chain order with their pages.</summary>
    public static List<RemoteMacroDevice> For(IAudioEngine e, int trackId, IDevicePageSource? edited)
    {
        var list = new List<RemoteMacroDevice>();
        if (trackId <= 0) return list;
        for (int m = 0, mc = e.TrackMidiEffectCount(trackId); m < mc; m++)
            Add(list, e, new DeviceAddress(trackId, FocusSection.Midi, m), edited);
        if (e.TrackInstrumentKind(trackId) != -2) Add(list, e, DeviceAddress.Instrument(trackId), edited);
        for (int d = 0, dc = e.TrackDeviceCount(trackId); d < dc; d++)
            Add(list, e, new DeviceAddress(trackId, FocusSection.Audio, d), edited);
        return list;
    }

    private static void Add(List<RemoteMacroDevice> list, IAudioEngine e, DeviceAddress a, IDevicePageSource? edited)
    {
        var f = FocusedDevice.Build(e, a, edited);
        if (f is null) return;
        var pages = new List<RemoteMacroPage>(Math.Max(1, f.PageCount));
        for (int p = 0; p < f.PageCount; p++)
        {
            var knobs = f.Knobs(p);
            var ps = new RemoteParam?[DevicePage.Knobs];
            for (int k = 0; k < DevicePage.Knobs && k < knobs.Length; k++)
                if (knobs[k].Target is { } t) ps[k] = ToParam(t, knobs[k].Name, f.DeviceName);
            pages.Add(new RemoteMacroPage(f.PageName(p) is { Length: > 0 } n ? n : (p + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), ps));
        }
        if (pages.Count == 0) pages.Add(new RemoteMacroPage("1", new RemoteParam?[DevicePage.Knobs]));
        string name = a.Section == FocusSection.Instrument ? RemoteParams.InstrumentName(e, a.TrackId) : f.DeviceName;
        list.Add(new RemoteMacroDevice(name, a, f.TypeKey, pages));
    }

    /// <summary>A controller target as the phone drives it; null for targets the phone doesn't
    /// reach (inside a rack — the rack's macros stand for them).</summary>
    public static RemoteParam? ToParam(ControllerTarget t, string name, string device) => t.Kind switch
    {
        ControllerTargetKind.DeviceParam => new RemoteParam(RemoteParamKind.DeviceParam, t.TrackId, t.Device, t.Param, name, device),
        ControllerTargetKind.PluginParam => new RemoteParam(RemoteParamKind.PluginParam, t.TrackId, t.Device, t.Param, name, device),
        ControllerTargetKind.MidiDeviceParam => new RemoteParam(RemoteParamKind.MidiParam, t.TrackId, t.Device, t.Param, name, device),
        _ => null,
    };

    /// <summary>The index of <paramref name="a"/> in <paramref name="devs"/>, or −1.</summary>
    public static int IndexOf(IReadOnlyList<RemoteMacroDevice> devs, DeviceAddress a)
    {
        for (int i = 0; i < devs.Count; i++)
            if (devs[i].Address.Section == a.Section && devs[i].Address.Index == a.Index && devs[i].Address.TrackId == a.TrackId) return i;
        return -1;
    }
}
