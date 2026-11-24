// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The device a controller's knobs follow, resolved against the engine (specs/051, FR-016): its
// type key, its pages (edited → catalog → plug-in default) and, for a page, the eight targets
// the knobs drive — addressed as MIDI Learn addresses them, so a knob lands through the same
// ControlApplier and the card's controls are found by their MIDI Learn binding (FR-017). Parameter
// references are looked up by name / id when the focus lands; one that no longer resolves
// leaves its knob empty.
//
// Also the focus rule for a track whose chain the Devices panel is not showing: its instrument,
// else its first device (MIDI effects come first in the chain, then the audio effects).

using System;
using System.Collections.Generic;
using System.Linq;
using Nota.Application;

namespace Nota.Application.Controllers;

public sealed class FocusedDevice
{
    public required DeviceAddress Address { get; init; }
    public required string TypeKey { get; init; }
    public required IReadOnlyList<DevicePage> Pages { get; init; }
    public required string DeviceName { get; init; }
    // Per page: the knobs' targets and parameter names (null = empty knob).
    private readonly List<(ControllerTarget? Target, string Name)[]> _knobs = new();

    public int PageCount => Pages.Count;

    /// <summary>Knob <paramref name="knob"/> (0..7) on <paramref name="page"/>: its target, or null.</summary>
    public ControllerTarget? Target(int page, int knob)
        => page >= 0 && page < _knobs.Count && knob >= 0 && knob < DevicePage.Knobs ? _knobs[page][knob].Target : null;

    public (ControllerTarget? Target, string Name)[] Knobs(int page)
        => page >= 0 && page < _knobs.Count ? _knobs[page] : new (ControllerTarget?, string)[DevicePage.Knobs];

    public string PageName(int page) => page >= 0 && page < Pages.Count ? Pages[page].Name ?? "" : "";

    // ---- the focus rule ------------------------------------------------------------------------

    /// <summary>A track's device when nothing on it is selected: the instrument, else the first device.</summary>
    public static DeviceAddress? DefaultFor(IAudioEngine e, int trackId)
    {
        if (trackId <= 0) return null;
        if (e.TrackInstrumentKind(trackId) != -2) return DeviceAddress.Instrument(trackId);
        if (e.TrackMidiEffectCount(trackId) > 0) return new DeviceAddress(trackId, FocusSection.Midi, 0);
        if (e.TrackDeviceCount(trackId) > 0) return new DeviceAddress(trackId, FocusSection.Audio, 0);
        return null;
    }

    // ---- resolving --------------------------------------------------------------------------

    // How to read one device's parameters: count, name, stable id (plug-ins), target.
    private sealed record Access(int Count, Func<int, string> Name, Func<int, string> Id, Func<int, ControllerTarget> Target, Func<int, ControllerTarget>? Macro = null);

    /// <summary>Resolve <paramref name="a"/>; null when it no longer exists.</summary>
    public static FocusedDevice? Build(IAudioEngine e, DeviceAddress a, IDevicePageSource? edited)
    {
        if (Describe(e, a) is not { } d) return null;
        var (key, name, acc) = d;
        var pages = DevicePageCatalog.Resolve(key, edited, () =>
        {
            var ids = new List<string>(acc.Count);
            for (int i = 0; i < acc.Count; i++) ids.Add(acc.Id(i));
            return ids;
        });
        // A device with no catalog entry and no plug-in identity (an unknown kind): its
        // parameters in order, by name.
        if (pages.Count == 0 && acc.Count > 0)
        {
            var list = new List<DevicePage>();
            for (int i = 0; i < acc.Count; i += DevicePage.Knobs)
            {
                var refs = new string?[Math.Min(DevicePage.Knobs, acc.Count - i)];
                for (int k = 0; k < refs.Length; k++) refs[k] = acc.Name(i + k);
                list.Add(new DevicePage(null, refs));
            }
            pages = list;
        }

        var f = new FocusedDevice { Address = a, TypeKey = key, Pages = pages, DeviceName = name };
        Dictionary<string, int>? byName = null, byId = null;
        foreach (var page in pages)
        {
            var knobs = new (ControllerTarget?, string)[DevicePage.Knobs];
            for (int k = 0; k < DevicePage.Knobs && k < page.Params.Count; k++)
            {
                if (page.Params[k] is not { Length: > 0 } r) continue;
                if (r.StartsWith(DevicePageCatalog.MacroPrefix, StringComparison.Ordinal))
                {
                    if (acc.Macro is { } macro && int.TryParse(r.AsSpan(DevicePageCatalog.MacroPrefix.Length), out int n) && n >= 1 && n <= acc.Count)
                        knobs[k] = (macro(n - 1), acc.Name(n - 1));
                    continue;
                }
                int idx;
                if (r.StartsWith(DevicePageCatalog.IdPrefix, StringComparison.Ordinal))
                {
                    byId ??= Index(acc.Count, acc.Id);
                    if (!byId.TryGetValue(r[DevicePageCatalog.IdPrefix.Length..], out idx)) continue;
                }
                else
                {
                    byName ??= Index(acc.Count, acc.Name);
                    if (!byName.TryGetValue(r, out idx)) continue;
                }
                knobs[k] = (acc.Target(idx), acc.Name(idx));
            }
            f._knobs.Add(knobs);
        }
        return f;
    }

    /// <summary>The device a parameter target belongs to (not inside a rack), or null.</summary>
    public static DeviceAddress? AddressOf(ControllerTarget t) => t.Kind switch
    {
        ControllerTargetKind.PluginParam => t.Device < 0 ? DeviceAddress.Instrument(t.TrackId) : new DeviceAddress(t.TrackId, FocusSection.Audio, t.Device),
        ControllerTargetKind.DeviceParam => new DeviceAddress(t.TrackId, FocusSection.Audio, t.Device),
        ControllerTargetKind.MidiDeviceParam => new DeviceAddress(t.TrackId, FocusSection.Midi, t.Device),
        _ => null,
    };

    /// <summary>The page reference (name, <c>id:</c> or <c>macro:</c>) of <paramref name="t"/> on the
    /// device at <paramref name="a"/>, or null when it isn't one of its parameters.</summary>
    public static string? RefOf(IAudioEngine e, DeviceAddress a, ControllerTarget t)
    {
        if (Describe(e, a) is not { } d) return null;
        bool plugin = DeviceTypeKey.IsPlugin(d.Key), rack = DeviceTypeKey.IsRack(d.Key);
        for (int i = 0; i < d.Acc.Count; i++)
        {
            var ti = rack && d.Acc.Macro is { } m ? m(i) : d.Acc.Target(i);
            if (ti != t) continue;
            return rack ? DevicePageCatalog.MacroPrefix + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                 : plugin ? DevicePageCatalog.IdPrefix + d.Acc.Id(i) : d.Acc.Name(i);
        }
        return null;
    }

    /// <summary>Pages with <paramref name="paramRef"/> taken off every page (Unbind, FR-032).</summary>
    public static List<DevicePage> WithoutParam(IReadOnlyList<DevicePage> pages, string paramRef)
        => pages.Select(p => new DevicePage(p.Name, p.Params.Select(r => r == paramRef ? null : r).ToList())).ToList();

    /// <summary>Pages with <paramref name="paramRef"/> in knob <paramref name="knob"/>'s slot of
    /// <paramref name="page"/> and off every other slot (Bind to ▸ Knob N, FR-033); the parameter that
    /// held the slot leaves it. A page past the end is added.</summary>
    public static List<DevicePage> WithParamAt(IReadOnlyList<DevicePage> pages, string paramRef, int page, int knob)
    {
        var list = WithoutParam(pages, paramRef);
        while (list.Count <= page) list.Add(new DevicePage(null, new string?[DevicePage.Knobs]));
        var slots = list[page].Params.ToList();
        while (slots.Count < DevicePage.Knobs) slots.Add(null);
        slots[Math.Clamp(knob, 0, DevicePage.Knobs - 1)] = paramRef;
        list[page] = new DevicePage(list[page].Name, slots);
        return list;
    }

    /// <summary>The type key of the device at <paramref name="a"/>, or null when it is gone.</summary>
    public static string? TypeKeyOf(IAudioEngine e, DeviceAddress a) => Describe(e, a)?.Key;

    /// <summary>The type keys of every device in the project — track level and inside racks —
    /// which is what its pages copy keeps.</summary>
    public static IEnumerable<string> TypeKeysIn(IAudioEngine e)
    {
        for (int i = 0, n = e.TrackCount; i < n; i++)
        {
            if (!e.TryGetTrackInfo(i, out var ti)) continue;
            int t = ti.Id;
            if (TypeKeyOf(e, DeviceAddress.Instrument(t)) is { } ik)
            {
                yield return ik;
                if (ik == DeviceTypeKey.RackInstrument)
                    for (int c = 0, cc = e.RackChainCount(t); c < cc; c++)
                        for (int s = -1, sc = e.RackChainDeviceCount(t, c); s < sc; s++)   // −1: the chain's instrument
                            if (TypeKeyOf(e, new DeviceAddress(t, FocusSection.Instrument, -1, new RackPath(c, s))) is { } rk) yield return rk;
            }
            for (int m = 0, mc = e.TrackMidiEffectCount(t); m < mc; m++)
                if (TypeKeyOf(e, new DeviceAddress(t, FocusSection.Midi, m)) is { } mk)
                {
                    yield return mk;
                    if (mk == DeviceTypeKey.RackMidi)
                        for (int c = 0, cc = e.MidiRackChainCount(t, m); c < cc; c++)
                            for (int s = 0, sc = e.MidiRackChainDeviceCount(t, m, c); s < sc; s++)
                                if (TypeKeyOf(e, new DeviceAddress(t, FocusSection.Midi, m, new RackPath(c, s))) is { } rk) yield return rk;
                }
            for (int d = 0, dc = e.TrackDeviceCount(t); d < dc; d++)
                if (TypeKeyOf(e, new DeviceAddress(t, FocusSection.Audio, d)) is { } dk)
                {
                    yield return dk;
                    if (dk == DeviceTypeKey.RackAudio)
                        for (int c = 0, cc = e.RackDevChainCount(t, d); c < cc; c++)
                            for (int s = 0, sc = e.RackDevChainDeviceCount(t, d, c); s < sc; s++)
                                if (TypeKeyOf(e, new DeviceAddress(t, FocusSection.Audio, d, new RackPath(c, s))) is { } rk) yield return rk;
                }
        }
    }

    /// <summary>Every parameter of the device at <paramref name="a"/>: its page reference (name or
    /// <c>id:</c>) and display name — what the page editor offers.</summary>
    public static IReadOnlyList<(string Ref, string Name)> Parameters(IAudioEngine e, DeviceAddress a)
    {
        if (Describe(e, a) is not { } d) return Array.Empty<(string, string)>();
        var list = new List<(string, string)>(d.Acc.Count);
        bool plugin = DeviceTypeKey.IsPlugin(d.Key);
        bool rack = DeviceTypeKey.IsRack(d.Key);
        int n = rack ? Math.Min(8, d.Acc.Count) : d.Acc.Count;
        for (int i = 0; i < n; i++)
        {
            string name = d.Acc.Name(i);
            string r = rack ? DevicePageCatalog.MacroPrefix + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                     : plugin ? DevicePageCatalog.IdPrefix + d.Acc.Id(i) : name;
            list.Add((r, name));
        }
        return list;
    }

    private static Dictionary<string, int> Index(int count, Func<int, string> key)
    {
        var map = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++) map.TryAdd(key(i), i);   // the first of a repeated name wins
        return map;
    }

    /// <summary>A device's type key, display name and parameter access; null when it is gone.</summary>
    private static (string Key, string Name, Access Acc)? Describe(IAudioEngine e, DeviceAddress a)
    {
        int t = a.TrackId;
        if (a.Rack is { } rp) return DescribeRackChild(e, a, rp);
        switch (a.Section)
        {
            case FocusSection.Instrument:
            {
                int kind = e.TrackInstrumentKind(t);
                if (kind == -2) return null;
                string pid = e.TrackInstrumentPluginId(t);
                var acc = new Access(e.PluginParamCount(t, -1), i => e.PluginParamName(t, -1, i), i => e.PluginParamId(t, -1, i),
                    i => ControllerTarget.PluginParam(t, -1, i), i => ControllerTarget.PluginParam(t, -1, i));
                return (pid.Length > 0 ? DeviceTypeKey.Plugin(pid) : DeviceTypeKey.Builtin(FocusSection.Instrument, kind),
                    BuiltinName(FocusSection.Instrument, kind), acc);
            }
            case FocusSection.Audio:
            {
                int di = a.Index;
                if (di < 0 || di >= e.TrackDeviceCount(t)) return null;
                string pid = e.TrackDevicePluginId(t, di);
                int kind = e.TrackDeviceBuiltinKind(t, di);
                if (pid.Length > 0 || DevicePageCatalog.RackKey(FocusSection.Audio, kind) is not null)
                {
                    // A hosted plug-in, or an Audio Effect Rack (its macros are its params).
                    var acc = new Access(e.PluginParamCount(t, di), i => e.PluginParamName(t, di, i), i => e.PluginParamId(t, di, i),
                        i => ControllerTarget.PluginParam(t, di, i), i => ControllerTarget.PluginParam(t, di, i));
                    return (pid.Length > 0 ? DeviceTypeKey.Plugin(pid) : DeviceTypeKey.RackAudio, e.DeviceName(t, di), acc);
                }
                var bacc = new Access(e.DeviceParamCount(t, di), i => e.DeviceParamName(t, di, i), i => e.DeviceParamName(t, di, i),
                    i => ControllerTarget.DeviceParam(t, di, i));
                return (DeviceTypeKey.Builtin(FocusSection.Audio, kind), e.DeviceName(t, di), bacc);
            }
            default:
            {
                int mi = a.Index;
                if (mi < 0 || mi >= e.TrackMidiEffectCount(t)) return null;
                int kind = e.MidiEffectKind(t, mi);
                var acc = new Access(e.MidiEffectParamCount(t, mi), i => e.MidiEffectParamName(t, mi, i), i => e.MidiEffectParamName(t, mi, i),
                    i => ControllerTarget.MidiDeviceParam(t, mi, i), i => ControllerTarget.MidiDeviceParam(t, mi, i));
                return (DeviceTypeKey.Builtin(FocusSection.Midi, kind), e.MidiEffectName(t, mi), acc);
            }
        }
    }

    // A device inside a rack: Instrument Rack (the instrument, index -1), Audio Effect Rack (an
    // audio effect at its index), MIDI Effect Rack (a MIDI effect at its index).
    private static (string Key, string Name, Access Acc)? DescribeRackChild(IAudioEngine e, DeviceAddress a, RackPath rp)
    {
        int t = a.TrackId, c = rp.Chain, s = rp.Slot;
        switch (a.Section)
        {
            case FocusSection.Midi:
            {
                int mi = a.Index, rack = ControllerTarget.MidiRackBase - mi;
                if (c < 0 || c >= e.MidiRackChainCount(t, mi) || s < 0 || s >= e.MidiRackChainDeviceCount(t, mi, c)) return null;
                int kind = e.MidiRackChainDeviceKind(t, mi, c, s);
                var acc = new Access(e.MidiRackChainDeviceParamCount(t, mi, c, s), i => e.MidiRackChainDeviceParamName(t, mi, c, s, i),
                    i => e.MidiRackChainDeviceParamName(t, mi, c, s, i), i => ControllerTarget.RackChainDeviceParam(t, rack, c, s, i));
                return (DeviceTypeKey.Builtin(FocusSection.Midi, kind), e.MidiRackChainDeviceName(t, mi, c, s), acc);
            }
            case FocusSection.Instrument when !rp.Midi:
            {
                if (c < 0 || c >= e.RackChainCount(t)) return null;
                if (s < 0)
                {
                    int kind = e.RackChainInstrumentKind(t, c);
                    string pid = e.RackChainInstrumentPluginId(t, c);
                    var acc = new Access(e.RackChainInstrumentParamCount(t, c), i => e.RackChainInstrumentParamName(t, c, i),
                        i => e.RackChainInstrumentParamId(t, c, i), i => ControllerTarget.RackChainInstrumentParam(t, -1, c, i));
                    return (pid.Length > 0 ? DeviceTypeKey.Plugin(pid) : DeviceTypeKey.Builtin(FocusSection.Instrument, kind),
                        e.RackChainInstrumentName(t, c), acc);
                }
                if (s >= e.RackChainDeviceCount(t, c)) return null;
                var dacc = new Access(e.RackChainDeviceParamCount(t, c, s), i => e.RackChainDeviceParamName(t, c, s, i),
                    i => e.RackChainDeviceParamName(t, c, s, i), i => ControllerTarget.RackChainDeviceParam(t, -1, c, s, i));
                return (DeviceTypeKey.Builtin(FocusSection.Audio, e.RackChainDeviceBuiltinKind(t, c, s)), e.RackChainDeviceName(t, c, s), dacc);
            }
            case FocusSection.Audio when !rp.Midi:
            {
                int di = a.Index;
                if (di < 0 || di >= e.TrackDeviceCount(t) || c < 0 || c >= e.RackDevChainCount(t, di)) return null;
                if (s < 0 || s >= e.RackDevChainDeviceCount(t, di, c)) return null;
                var acc = new Access(e.RackDevChainDeviceParamCount(t, di, c, s), i => e.RackDevChainDeviceParamName(t, di, c, s, i),
                    i => e.RackDevChainDeviceParamName(t, di, c, s, i), i => ControllerTarget.RackChainDeviceParam(t, di, c, s, i));
                return (DeviceTypeKey.Builtin(FocusSection.Audio, e.RackDevChainDeviceBuiltinKind(t, di, c, s)), e.RackDevChainDeviceName(t, di, c, s), acc);
            }
        }
        // A MIDI effect inside an Instrument Rack's chain has no parameter access from here:
        // the knobs follow the rack's macros instead.
        return Describe(e, a with { Rack = null });
    }

    private static string BuiltinName(FocusSection section, int kind)
        => BuiltinDeviceCatalog.Find(section switch
        {
            FocusSection.Instrument => BuiltinDeviceType.Instrument,
            FocusSection.Audio => BuiltinDeviceType.AudioEffect,
            _ => BuiltinDeviceType.MidiEffect,
        }, kind)?.Name ?? "";
}
