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
    /// <summary>A built-in MIDI effect's parameter, in its own min..max units (specs/114).</summary>
    MidiParam,
}

public static class RemoteParams
{
    public const int AutoFilterKind = 7;
    public const int EffectRackKind = 5;
    public const int InstrumentRackKind = 3, DrumRackKind = 4, FluxKind = 11, RhythmKind = 12, SamplerKind = 1;
    public const int TwinKind = 18;
    // Nota Twin's phone pages, in order: the morph and the pad, then the Lower part's side-A sound.
    private static readonly string[] TwinRemoteIds =
    {
        "lo.morph", "padx", "pady", "padz", "lo.a.ldcut", "lo.a.ldres", "lo.a.mmcut", "lo.a.mmres",
        "lo.a.o1wave", "lo.a.o2wave", "lo.a.o2level", "lo.a.noiselevel", "lo.a.route", "lo.a.brute", "lo.a.drive", "lo.a.mmmode",
        "lo.a.aenva", "lo.a.aenvd", "lo.a.aenvs", "lo.a.aenvr", "lo.a.fenva", "lo.a.fenvd", "lo.a.ldenv", "lo.a.mmenv",
        "lo.a.vibamt", "lo.a.spread", "lo.a.drift", "lo.glide",
    };

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
            15 => "Nota Consort", 16 => "Nota Keys", 17 => "Nota Mosaic", 18 => "Nota Twin", _ => "",
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
            case RemoteParamKind.MidiParam:
            {
                float min = e.MidiEffectParamMin(p.TrackId, p.DeviceIndex, p.ParamIndex), max = e.MidiEffectParamMax(p.TrackId, p.DeviceIndex, p.ParamIndex);
                float v = e.MidiEffectGetParam(p.TrackId, p.DeviceIndex, p.ParamIndex);
                return max > min ? Math.Clamp((v - min) / (max - min), 0, 1) : 0;
            }
            default:
                return Math.Clamp(p.DeviceIndex < 0 ? e.RackMacroGet(p.TrackId, p.ParamIndex) : e.RackDevMacroGet(p.TrackId, p.DeviceIndex, p.ParamIndex), 0, 1);
        }
    }

    /// <summary>The undo target for a parameter (specs/016); none for a rack macro, which
    /// value undo does not cover.</summary>
    public static MixTarget? UndoTarget(RemoteParam p) => p.Kind switch
    {
        RemoteParamKind.DeviceParam => MixTarget.DeviceParam(p.TrackId, p.DeviceIndex, p.ParamIndex),
        RemoteParamKind.PluginParam => MixTarget.PluginParam(p.TrackId, p.DeviceIndex, p.ParamIndex),
        RemoteParamKind.MidiParam => MixTarget.MidiDeviceParam(p.TrackId, p.DeviceIndex, p.ParamIndex),
        _ => null,
    };

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
            case RemoteParamKind.MidiParam:
            {
                e.BeginAutomationWrite(p.TrackId, AutomationTarget.MidiDeviceParam, p.DeviceIndex, p.ParamIndex, "", latch: true);
                float min = e.MidiEffectParamMin(p.TrackId, p.DeviceIndex, p.ParamIndex), max = e.MidiEffectParamMax(p.TrackId, p.DeviceIndex, p.ParamIndex);
                e.MidiEffectSetParam(p.TrackId, p.DeviceIndex, p.ParamIndex, (float)(min + norm * (max - min)));
                break;
            }
            default:
                if (p.DeviceIndex < 0) e.RackMacroSet(p.TrackId, p.ParamIndex, (float)norm);
                else e.RackDevMacroSet(p.TrackId, p.DeviceIndex, p.ParamIndex, (float)norm);
                break;
        }
    }

    /// <summary>Whether the parameter has an automation lane with points (the brass dot).</summary>
    public static bool Automated(IAudioEngine e, RemoteParam p)
    {
        if (p.Kind is RemoteParamKind.RackMacro or RemoteParamKind.MidiParam) return false;
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
        bool midi = p.Kind == RemoteParamKind.MidiParam;
        float min = midi ? e.MidiEffectParamMin(p.TrackId, p.DeviceIndex, p.ParamIndex) : e.DeviceParamMin(p.TrackId, p.DeviceIndex, p.ParamIndex);
        float max = midi ? e.MidiEffectParamMax(p.TrackId, p.DeviceIndex, p.ParamIndex) : e.DeviceParamMax(p.TrackId, p.DeviceIndex, p.ParamIndex);
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
