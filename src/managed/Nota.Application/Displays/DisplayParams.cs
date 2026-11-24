// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A device's parameters by name, for its LED display (specs/113 research R3). Two kinds of
// parameter reach a display: a built-in audio or MIDI effect's, read raw in its own min..max
// units, and an instrument's (plug-in style), read normalized with the engine's value text
// ("1.20 kHz", "250 ms", "−6.0 dB", "45 %"). The unit helpers give a picture Hz, seconds, dB or a
// fraction from either kind, and fall back to a sensible range when there is no unit — so 49
// device pictures need no copy of each card's value mapping. A name that does not resolve
// returns the fallback: a renamed parameter dims a picture, it never breaks one.

using System.Globalization;
using Nota.Application.Controllers;

namespace Nota.Application.Displays;

public sealed class DisplayParams
{
    private enum Kind { None, Plugin, Device, Midi }

    private readonly IAudioEngine _e;
    private readonly Kind _kind;
    private readonly int _t, _d;
    private readonly Dictionary<string, int> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _names = new();

    public DeviceAddress Address { get; }
    public int Count => _names.Count;
    public IReadOnlyList<string> Names => _names;

    public DisplayParams(IAudioEngine e, DeviceAddress a)
    {
        _e = e;
        Address = a;
        _t = a.TrackId;
        if (a.Rack is not null) return;
        int n;
        Func<int, string> name;
        switch (a.Section)
        {
            case FocusSection.Instrument when e.TrackInstrumentKind(_t) != -2:
                _kind = Kind.Plugin; _d = -1;
                n = e.PluginParamCount(_t, -1);
                name = i => e.PluginParamName(_t, -1, i);
                break;
            case FocusSection.Audio when a.Index >= 0 && a.Index < e.TrackDeviceCount(_t):
                _d = a.Index;
                bool plugin = e.TrackDevicePluginId(_t, _d).Length > 0 || e.TrackDeviceBuiltinKind(_t, _d) < 0
                              || DevicePageCatalog.RackKey(FocusSection.Audio, e.TrackDeviceBuiltinKind(_t, _d)) is not null;
                _kind = plugin ? Kind.Plugin : Kind.Device;
                n = plugin ? e.PluginParamCount(_t, _d) : e.DeviceParamCount(_t, _d);
                name = plugin ? i => e.PluginParamName(_t, _d, i) : i => e.DeviceParamName(_t, _d, i);
                break;
            case FocusSection.Midi when a.Index >= 0 && a.Index < e.TrackMidiEffectCount(_t):
                _kind = Kind.Midi; _d = a.Index;
                n = e.MidiEffectParamCount(_t, _d);
                name = i => e.MidiEffectParamName(_t, _d, i);
                break;
            default:
                return;
        }
        for (int i = 0; i < n; i++)
        {
            string s = name(i);
            _names.Add(s);
            _byName.TryAdd(s, i);
        }
    }

    public bool Has(string name) => _byName.ContainsKey(name);
    public int IndexOf(string name) => _byName.TryGetValue(name, out int i) ? i : -1;

    // ---- by index --------------------------------------------------------------------------

    public double NormAt(int i)
    {
        if (i < 0 || i >= _names.Count) return 0;
        switch (_kind)
        {
            case Kind.Plugin: return Math.Clamp(_e.PluginParamGet(_t, _d, i), 0, 1);
            case Kind.Device:
            {
                float lo = _e.DeviceParamMin(_t, _d, i), hi = _e.DeviceParamMax(_t, _d, i);
                return hi > lo ? Math.Clamp((_e.DeviceGetParam(_t, _d, i) - lo) / (hi - lo), 0, 1) : 0;
            }
            case Kind.Midi:
            {
                float lo = _e.MidiEffectParamMin(_t, _d, i), hi = _e.MidiEffectParamMax(_t, _d, i);
                return hi > lo ? Math.Clamp((_e.MidiEffectGetParam(_t, _d, i) - lo) / (hi - lo), 0, 1) : 0;
            }
            default: return 0;
        }
    }

    /// <summary>The raw value: a built-in effect's in its own units; an instrument's normalized.</summary>
    public double RawAt(int i) => i < 0 || i >= _names.Count ? 0 : _kind switch
    {
        Kind.Device => _e.DeviceGetParam(_t, _d, i),
        Kind.Midi => _e.MidiEffectGetParam(_t, _d, i),
        Kind.Plugin => _e.PluginParamGet(_t, _d, i),
        _ => 0,
    };

    private (double Lo, double Hi) RangeAt(int i) => _kind switch
    {
        Kind.Device => (_e.DeviceParamMin(_t, _d, i), _e.DeviceParamMax(_t, _d, i)),
        Kind.Midi => (_e.MidiEffectParamMin(_t, _d, i), _e.MidiEffectParamMax(_t, _d, i)),
        _ => (0, 1),
    };

    /// <summary>The engine's text for an instrument parameter; "" for the others.</summary>
    public string TextAt(int i) => _kind == Kind.Plugin && i >= 0 && i < _names.Count ? _e.PluginParamText(_t, _d, i).Trim() : "";

    // ---- by name ---------------------------------------------------------------------------

    /// <summary>Normalized 0..1.</summary>
    public double N(string name, double fallback = 0) => _byName.TryGetValue(name, out int i) ? NormAt(i) : fallback;

    /// <summary>Raw (an effect's own units; an instrument's normalized).</summary>
    public double Raw(string name, double fallback = 0) => _byName.TryGetValue(name, out int i) ? RawAt(i) : fallback;

    public bool On(string name) => N(name) >= 0.5;

    /// <summary>The engine's value text of an instrument parameter ("" for effects and unknown names).</summary>
    public string Txt(string name) => _byName.TryGetValue(name, out int i) ? TextAt(i) : "";

    /// <summary>A stepped parameter's step (0-based) out of <paramref name="steps"/>.</summary>
    public int Step(string name, int steps, int fallback = 0)
    {
        if (!_byName.TryGetValue(name, out int i)) return fallback;
        if (_kind is Kind.Device or Kind.Midi)
        {
            var (lo, hi) = RangeAt(i);
            double raw = RawAt(i);
            if (hi - lo >= steps - 1 - 1e-6 && hi - lo <= steps + 1e-6) return Math.Clamp((int)Math.Round(raw - lo), 0, steps - 1);
        }
        return Math.Clamp((int)Math.Floor(NormAt(i) * steps - 1e-9), 0, steps - 1);
    }

    /// <summary>A frequency in Hz. Units from the effect's range (≥ 100 Hz top means Hz) or the
    /// instrument's text; else the normalized value on a log 20 Hz–20 kHz sweep.</summary>
    public double Hz(string name, double fallback = 1000)
    {
        if (!_byName.TryGetValue(name, out int i)) return fallback;
        if (_kind is Kind.Device or Kind.Midi)
        {
            var (lo, hi) = RangeAt(i);
            if (hi >= 100) return Math.Max(1, RawAt(i));
            return 20 * Math.Pow(1000, NormAt(i));
        }
        if (ParseUnit(TextAt(i), out double v, out string u))
        {
            if (u.StartsWith("khz", StringComparison.Ordinal)) return v * 1000;
            if (u.StartsWith("hz", StringComparison.Ordinal)) return v;
        }
        return 20 * Math.Pow(1000, NormAt(i));
    }

    /// <summary>A time in seconds. Effects: the range says ms (top above 20) or s; instruments: the
    /// text; else the normalized value on a log 1 ms–10 s sweep.</summary>
    public double Sec(string name, double fallback = 0.1)
    {
        if (!_byName.TryGetValue(name, out int i)) return fallback;
        if (_kind is Kind.Device or Kind.Midi)
        {
            var (lo, hi) = RangeAt(i);
            double raw = RawAt(i);
            if (hi > 1.0001) return hi > 20 ? raw / 1000.0 : raw;
            return 0.001 * Math.Pow(10000, NormAt(i));
        }
        if (ParseUnit(TextAt(i), out double v, out string u))
        {
            if (u.StartsWith("ms", StringComparison.Ordinal)) return v / 1000.0;
            if (u is "s" or "sec" or "secs") return v;
            if (u.StartsWith("min", StringComparison.Ordinal)) return v * 60;
        }
        return 0.001 * Math.Pow(10000, NormAt(i));
    }

    /// <summary>A level in dB. Effects: raw when the range is in dB (it goes below 0 or above 1),
    /// else the normalized value as gain on −60..0 dB; instruments: the text, else as gain.</summary>
    public double Db(string name, double fallback = 0)
    {
        if (!_byName.TryGetValue(name, out int i)) return fallback;
        if (_kind is Kind.Device or Kind.Midi)
        {
            var (lo, hi) = RangeAt(i);
            if (lo < 0 || hi > 1.0001) return RawAt(i);
            return -60 + 60 * NormAt(i);
        }
        if (ParseUnit(TextAt(i), out double v, out string u) && u.StartsWith("db", StringComparison.Ordinal)) return v;
        if (TextAt(i).Contains("-inf", StringComparison.OrdinalIgnoreCase) || TextAt(i).Contains("−∞", StringComparison.Ordinal)) return -90;
        return -60 + 60 * NormAt(i);
    }

    /// <summary>A bipolar amount −1..+1 (an env amount, a pan): raw when the range is symmetric
    /// around zero, else the normalized value around the middle.</summary>
    public double Bi(string name, double fallback = 0)
    {
        if (!_byName.TryGetValue(name, out int i)) return fallback;
        if (_kind is Kind.Device or Kind.Midi)
        {
            var (lo, hi) = RangeAt(i);
            if (lo < 0 && hi > 0 && Math.Abs(lo + hi) < 1e-3 * Math.Max(1, hi)) return Math.Clamp(RawAt(i) / hi, -1, 1);
        }
        return NormAt(i) * 2 - 1;
    }

    /// <summary>A plain number in the parameter's own units (semitones, steps, a count); for an
    /// instrument the number in its text, else the normalized value.</summary>
    public double Num(string name, double fallback = 0)
    {
        if (!_byName.TryGetValue(name, out int i)) return fallback;
        if (_kind is Kind.Device or Kind.Midi) return RawAt(i);
        return ParseUnit(TextAt(i), out double v, out _) ? v : NormAt(i);
    }

    /// <summary>The first number in <paramref name="text"/> and the unit after it, lower case
    /// ("1.20 kHz" → 1.2, "khz"; "−6 dB" → −6, "db"; "45 %" → 45, "%").</summary>
    public static bool ParseUnit(string text, out double value, out string unit)
    {
        value = 0; unit = "";
        if (string.IsNullOrEmpty(text)) return false;
        string s = text.Replace('−', '-').Replace(',', '.');
        int start = -1;
        for (int k = 0; k < s.Length; k++)
            if (char.IsDigit(s[k]) || ((s[k] == '-' || s[k] == '+' || s[k] == '.') && k + 1 < s.Length && (char.IsDigit(s[k + 1]) || s[k + 1] == '.')))
            { start = k; break; }
        if (start < 0) return false;
        int end = start + 1;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;
        if (!double.TryParse(s.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        unit = s[end..].Trim().ToLowerInvariant();
        if (unit.StartsWith('k') && !unit.StartsWith("khz", StringComparison.Ordinal)) { value *= 1000; unit = unit[1..].TrimStart(); }
        return true;
    }
}
