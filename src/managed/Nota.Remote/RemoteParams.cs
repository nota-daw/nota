// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What the phone's XY pad and macro knobs drive when nothing is mapped to them, and how a
// parameter is read, written and printed. Engine-only (no Avalonia), so it runs inside the hub's
// UI-thread tick. A phone move is a hardware-style gesture: it latches automation while Record is
// on and overrides an automated lane otherwise — the same rule MIDI Learn applies to a knob.

using System.Globalization;
using Nota.Application;

namespace Nota.Remote;

/// <summary>One parameter a phone control drives.</summary>
public sealed record RemoteParam(RemoteParamKind Kind, int TrackId, int DeviceIndex, int ParamIndex, string Name, string Device);

public enum RemoteParamKind
{
    /// <summary>A built-in effect's parameter, in its own min..max units.</summary>
    DeviceParam,
    /// <summary>A plug-in or built-in instrument parameter, normalized 0..1 (device −1 = the instrument).</summary>
    PluginParam,
    /// <summary>An Instrument / Drum Rack macro (device −1) or an Effect Rack's (device ≥ 0).</summary>
    RackMacro,
}

/// <summary>A page of up to eight knobs from one device in the track's chain.</summary>
public sealed record RemoteMacroDevice(string Name, int DeviceIndex, IReadOnlyList<RemoteParam> Params);

public static class RemoteParams
{
    public const int AutoFilterKind = 7;
    public const int EffectRackKind = 5;
    public const int InstrumentRackKind = 3, DrumRackKind = 4, FluxKind = 11, RhythmKind = 12, SamplerKind = 1;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Built-in instrument names, by the engine's instrument kind.</summary>
    public static string InstrumentName(IAudioEngine e, int trackId)
    {
        int kind = e.TrackInstrumentKind(trackId);
        string name = kind switch
        {
            0 => "Nota Synth", 1 => "Sampler", 2 => "Nota Physical", 3 => "Instrument Rack", 4 => "Drum Rack",
            5 => "Nota Aurora", 6 => "Nota Volt", 7 => "Nota Bass", 8 => "Nota Pendulum", 9 => "Nota Operator",
            10 => "Nota Grain", 11 => "Nota Flux", 12 => "Nota Rhythm", 13 => "Nota Monolith", 14 => "Nota Pentad",
            15 => "Nota Consort", _ => "",
        };
        if (name.Length > 0) return name;
        string id = e.TrackInstrumentPluginId(trackId);
        return PluginDisplayName(id) ?? "Instrument";
    }

    // A JUCE identifier reads "<format>-<name>-<hash>-<uid>": the name is the useful part.
    private static string? PluginDisplayName(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var parts = id.Split('-');
        return parts.Length >= 2 && parts[1].Length > 0 ? parts[1] : id;
    }

    public static string DeviceName(IAudioEngine e, int trackId, int deviceIndex)
    {
        string n = e.DeviceName(trackId, deviceIndex);
        return n.Length > 0 ? n : PluginDisplayName(e.TrackDevicePluginId(trackId, deviceIndex)) ?? $"Device {deviceIndex + 1}";
    }

    /// <summary>The XY pad's default pair: Nota Flux's vector, else Cutoff × Resonance of the first
    /// Auto Filter in the chain. Null when the track has neither.</summary>
    public static (RemoteParam X, RemoteParam Y)? DefaultXy(IAudioEngine e, int trackId)
    {
        if (e.TrackInstrumentKind(trackId) == FluxKind)
        {
            int n = e.PluginParamCount(trackId, -1);
            int xi = -1, yi = -1;
            for (int i = 0; i < n; i++)
            {
                string id = e.PluginParamId(trackId, -1, i);
                if (id == "vecx") xi = i; else if (id == "vecy") yi = i;
            }
            if (xi >= 0 && yi >= 0)
                return (new RemoteParam(RemoteParamKind.PluginParam, trackId, -1, xi, "Vector X", "Nota Flux"),
                        new RemoteParam(RemoteParamKind.PluginParam, trackId, -1, yi, "Vector Y", "Nota Flux"));
        }
        int devs = e.TrackDeviceCount(trackId);
        for (int d = 0; d < devs; d++)
        {
            if (e.TrackDeviceBuiltinKind(trackId, d) != AutoFilterKind) continue;
            int n = e.DeviceParamCount(trackId, d);
            int cut = -1, res = -1;
            for (int i = 0; i < n; i++)
            {
                string pn = e.DeviceParamName(trackId, d, i).ToLowerInvariant();
                if (cut < 0 && (pn.Contains("cutoff") || pn == "freq" || pn.Contains("frequency"))) cut = i;
                else if (res < 0 && (pn.Contains("reso") || pn == "q")) res = i;
            }
            if (cut < 0) cut = 0;
            if (res < 0) res = Math.Min(1, n - 1);
            string dn = DeviceName(e, trackId, d);
            return (new RemoteParam(RemoteParamKind.DeviceParam, trackId, d, cut, e.DeviceParamName(trackId, d, cut), dn),
                    new RemoteParam(RemoteParamKind.DeviceParam, trackId, d, res, e.DeviceParamName(trackId, d, res), dn));
        }
        return null;
    }

    /// <summary>The devices the Macros screen steps through: the rack's macros or the instrument's
    /// parameters first, then every effect. Plug-ins and long built-ins page by eight.</summary>
    public static List<RemoteMacroDevice> MacroDevices(IAudioEngine e, int trackId, bool isInstrumentTrack)
    {
        var list = new List<RemoteMacroDevice>();
        if (isInstrumentTrack)
        {
            int kind = e.TrackInstrumentKind(trackId);
            string inst = InstrumentName(e, trackId);
            if (kind is InstrumentRackKind or DrumRackKind)
            {
                var ps = new List<RemoteParam>();
                for (int m = 0; m < 8; m++)
                {
                    string mn = e.RackMacroName(trackId, m);
                    ps.Add(new RemoteParam(RemoteParamKind.RackMacro, trackId, -1, m, mn.Length > 0 ? mn : $"Macro {m + 1}", inst));
                }
                list.Add(new RemoteMacroDevice(inst + " · Macros", -1, ps));
            }
            else
            {
                int n = e.PluginParamCount(trackId, -1);
                AddPaged(list, inst, -1, n, i => new RemoteParam(RemoteParamKind.PluginParam, trackId, -1, i, e.PluginParamName(trackId, -1, i), inst));
            }
        }
        int devs = e.TrackDeviceCount(trackId);
        for (int d = 0; d < devs; d++)
        {
            string dn = DeviceName(e, trackId, d);
            int bk = e.TrackDeviceBuiltinKind(trackId, d);
            int di = d;
            if (bk == EffectRackKind)
            {
                var ps = new List<RemoteParam>();
                for (int m = 0; m < 8; m++)
                {
                    string mn = e.RackDevMacroName(trackId, d, m);
                    ps.Add(new RemoteParam(RemoteParamKind.RackMacro, trackId, d, m, mn.Length > 0 ? mn : $"Macro {m + 1}", dn));
                }
                list.Add(new RemoteMacroDevice(dn + " · Macros", d, ps));
            }
            else if (bk >= 0)
                AddPaged(list, dn, d, e.DeviceParamCount(trackId, d), i => new RemoteParam(RemoteParamKind.DeviceParam, trackId, di, i, e.DeviceParamName(trackId, di, i), dn));
            else
                AddPaged(list, dn, d, e.PluginParamCount(trackId, d), i => new RemoteParam(RemoteParamKind.PluginParam, trackId, di, i, e.PluginParamName(trackId, di, i), dn));
        }
        return list;
    }

    private static void AddPaged(List<RemoteMacroDevice> list, string name, int deviceIndex, int count, Func<int, RemoteParam> make)
    {
        if (count <= 0) return;
        int pages = (count + 7) / 8;
        for (int p = 0; p < pages; p++)
        {
            var ps = new List<RemoteParam>();
            for (int i = p * 8; i < Math.Min(count, p * 8 + 8); i++) ps.Add(make(i));
            list.Add(new RemoteMacroDevice(pages > 1 ? $"{name} · {p + 1}/{pages}" : name, deviceIndex, ps));
        }
    }

    public static double Get(IAudioEngine e, RemoteParam p)
    {
        switch (p.Kind)
        {
            case RemoteParamKind.DeviceParam:
            {
                float min = e.DeviceParamMin(p.TrackId, p.DeviceIndex, p.ParamIndex), max = e.DeviceParamMax(p.TrackId, p.DeviceIndex, p.ParamIndex);
                float v = e.DeviceGetParam(p.TrackId, p.DeviceIndex, p.ParamIndex);
                return max > min ? Math.Clamp((v - min) / (max - min), 0, 1) : 0;
            }
            case RemoteParamKind.PluginParam:
                return Math.Clamp(e.PluginParamGet(p.TrackId, p.DeviceIndex, p.ParamIndex), 0, 1);
            default:
                return Math.Clamp(p.DeviceIndex < 0 ? e.RackMacroGet(p.TrackId, p.ParamIndex) : e.RackDevMacroGet(p.TrackId, p.DeviceIndex, p.ParamIndex), 0, 1);
        }
    }

    /// <summary>Move a parameter to <paramref name="norm"/> as a hardware control would: a
    /// latching automation write (an override when Record is off).</summary>
    public static void Set(IAudioEngine e, RemoteParam p, double norm)
    {
        norm = Math.Clamp(norm, 0, 1);
        switch (p.Kind)
        {
            case RemoteParamKind.DeviceParam:
            {
                e.BeginAutomationWrite(p.TrackId, AutomationTarget.DeviceParam, p.DeviceIndex, p.ParamIndex, "", latch: true);
                float min = e.DeviceParamMin(p.TrackId, p.DeviceIndex, p.ParamIndex), max = e.DeviceParamMax(p.TrackId, p.DeviceIndex, p.ParamIndex);
                e.DeviceSetParam(p.TrackId, p.DeviceIndex, p.ParamIndex, (float)(min + norm * (max - min)));
                break;
            }
            case RemoteParamKind.PluginParam:
                e.BeginAutomationWrite(p.TrackId, AutomationTarget.PluginParam, p.DeviceIndex, -1,
                    e.PluginParamId(p.TrackId, p.DeviceIndex, p.ParamIndex), latch: true);
                e.PluginParamSet(p.TrackId, p.DeviceIndex, p.ParamIndex, (float)norm);
                break;
            default:
                if (p.DeviceIndex < 0) e.RackMacroSet(p.TrackId, p.ParamIndex, (float)norm);
                else e.RackDevMacroSet(p.TrackId, p.DeviceIndex, p.ParamIndex, (float)norm);
                break;
        }
    }

    /// <summary>Whether the parameter has an automation lane with points (the brass dot).</summary>
    public static bool Automated(IAudioEngine e, RemoteParam p)
    {
        if (p.Kind == RemoteParamKind.RackMacro) return false;
        int n = e.AutomationLaneCount(p.TrackId);
        string pid = p.Kind == RemoteParamKind.PluginParam ? e.PluginParamId(p.TrackId, p.DeviceIndex, p.ParamIndex) : "";
        for (int i = 0; i < n; i++)
        {
            var li = e.AutomationLaneInfo(p.TrackId, i);
            if (li.PointCount == 0 || li.DeviceIndex != p.DeviceIndex) continue;
            if (p.Kind == RemoteParamKind.DeviceParam && li.Target == AutomationTarget.DeviceParam && li.ParamIndex == p.ParamIndex) return true;
            if (p.Kind == RemoteParamKind.PluginParam && li.Target == AutomationTarget.PluginParam && e.AutomationLaneParamId(p.TrackId, i) == pid) return true;
        }
        return false;
    }

    /// <summary>The value in the parameter's own units, as Nota prints it ("1.2 kHz", "42 %").</summary>
    public static string Text(IAudioEngine e, RemoteParam p, double norm)
    {
        if (p.Kind == RemoteParamKind.PluginParam)
        {
            string t = e.PluginParamText(p.TrackId, p.DeviceIndex, p.ParamIndex);
            if (!string.IsNullOrWhiteSpace(t)) return t.Trim();
            return Pct(norm);
        }
        if (p.Kind == RemoteParamKind.RackMacro) return Pct(norm);

        string name = p.Name.ToLowerInvariant();
        float min = e.DeviceParamMin(p.TrackId, p.DeviceIndex, p.ParamIndex), max = e.DeviceParamMax(p.TrackId, p.DeviceIndex, p.ParamIndex);
        double v = min + norm * (max - min);
        bool unit = min >= 0 && max <= 1.0001;
        bool isFreq = name.Contains("cutoff") || name.Contains("freq") || name.EndsWith(" hz") || name.Contains("hp") || name.Contains("lp");
        if (isFreq)
        {
            // A 0..1 frequency is the engine's log sweep (Auto Filter: 30 Hz · 600^v).
            double hz = unit ? 30 * Math.Pow(600, norm) : v;
            return hz >= 1000 ? (hz / 1000).ToString("0.0", Inv) + " kHz" : Math.Round(hz).ToString(Inv) + " Hz";
        }
        if (name.Contains("db") || name.Contains("gain") && !unit) return v.ToString("0.0", Inv) + " dB";
        if (unit) return Pct(norm);
        if (max - min >= 20 && Math.Abs(v - Math.Round(v)) < 1e-3) return Math.Round(v).ToString(Inv);
        return v.ToString("0.00", Inv);
    }

    private static string Pct(double norm) => Math.Round(norm * 100).ToString(Inv) + " %";
}
