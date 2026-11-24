// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What a device page's picture reads (specs/113): the device's parameters, the page and its
// knobs, the time for anything that moves, and the live measurements Nota already takes for the
// desktop cards (research R4) — an instrument's output spectrum, a device's scope telemetry, a
// dynamics device's gain reduction, the track's level, a MIDI effect's last note. Every tap is
// read lazily, at most once per frame, on the UI thread that renders; none touches the audio
// thread beyond the lock-free reads the cards already make.

using Nota.Application.Controllers;

namespace Nota.Application.Displays;

public sealed class DisplayContext
{
    private readonly IAudioEngine _e;
    private readonly DisplayState _state;
    private float[]? _scope;
    private int _scopeN = -1;
    private double? _level, _gr, _lvl01;

    public DisplayContext(IAudioEngine engine, DeviceAddress address, DisplayParams p, DisplayState state, double time,
        string typeKey, string deviceName, int page, string pageName, IReadOnlyList<string?> knobs, int turning)
    {
        _e = engine;
        _state = state;
        Address = address;
        P = p;
        Time = time;
        TypeKey = typeKey;
        DeviceName = deviceName;
        Page = page;
        PageName = pageName;
        Knobs = knobs;
        Turning = turning;
    }

    public DeviceAddress Address { get; }
    public int TrackId => Address.TrackId;
    public IAudioEngine Engine => _e;
    public DisplayParams P { get; }
    /// <summary>Seconds, for anything that moves (an LFO, a playhead).</summary>
    public double Time { get; }
    public string TypeKey { get; }
    public string DeviceName { get; }
    public int Page { get; }
    public string PageName { get; }
    /// <summary>The page's eight parameter names (null = an empty knob).</summary>
    public IReadOnlyList<string?> Knobs { get; }
    /// <summary>The knob being turned (0..7), −1 for none.</summary>
    public int Turning { get; }
    public bool Playing => _e.IsPlaying;

    /// <summary>The name of the knob being turned, or null.</summary>
    public string? TurningName => Turning >= 0 && Turning < Knobs.Count ? Knobs[Turning] : null;

    /// <summary>Whether <paramref name="name"/> is the parameter being turned.</summary>
    public bool IsTurning(string name) => TurningName is { } n && string.Equals(n, name, StringComparison.OrdinalIgnoreCase);

    // ---- live taps ----------------------------------------------------------------------------

    /// <summary>The instrument's output spectrum: 64 log bands 20 Hz–20 kHz in dB (−120 = silent).
    /// Smoothed across frames — quick to rise, slow to fall. Empty when the device is not the
    /// track's instrument.</summary>
    public double[] Spectrum()
    {
        if (Address.Section != FocusSection.Instrument) return _state.Silent;
        _state.Spectrum.Update(_e, TrackId);
        return _state.Spectrum.Bands;
    }

    /// <summary>The device's scope telemetry (its own layout, see each card); count read.</summary>
    public int Scope(out float[] data, int max = 8192)
    {
        if (_scopeN < 0)
        {
            _scope = _state.ScopeBuffer(max);
            _scopeN = Address.Section switch
            {
                FocusSection.Audio when Address.Rack is null => _e.DeviceScope(TrackId, Address.Index, _scope, _scope.Length),
                FocusSection.Instrument => _e.InstrumentScope(TrackId, _scope),
                FocusSection.Midi => _e.MidiEffectScope(TrackId, Address.Index, _scope),
                _ => 0,
            };
        }
        data = _scope!;
        return Math.Max(0, _scopeN);
    }

    /// <summary>The device's gain reduction in dB (≥ 0); 0 for devices without one.</summary>
    public double GainReduction() => _gr ??= Address.Section == FocusSection.Audio && Address.Rack is null
        ? Math.Max(0, _e.DeviceGainReduction(TrackId, Address.Index)) : 0;

    /// <summary>The track's output peak in dB (−90 when silent).</summary>
    public double TrackLevelDb()
    {
        if (_level is { } l) return l;
        double peak = _e.TryGetTrackMeter(TrackId, out var m) ? Math.Max(m.PeakL, m.PeakR) : 0;
        return (_level = peak > 1e-5 ? 20 * Math.Log10(peak) : -90).Value;
    }

    /// <summary>The track's level as 0..1 on a −60..0 dB scale, smoothed (falls slowly).</summary>
    public double TrackLevel() => _lvl01 ??= _state.SmoothLevel(Math.Clamp((TrackLevelDb() + 60) / 60, 0, 1));

    /// <summary>The track level over the last ~4 s (0..1 per frame, oldest first), one entry added per frame.</summary>
    public IReadOnlyList<double> LevelHistory() => _state.History(TrackLevel());

    /// <summary>A MIDI effect's last note in / out (−1 none).</summary>
    public (int In, int Out) MidiLast() => Address.Section == FocusSection.Midi
        ? (_e.MidiEffectLastIn(TrackId, Address.Index), _e.MidiEffectLastOut(TrackId, Address.Index)) : (-1, -1);

    /// <summary>The playhead in beats and the tempo, for patterns that run with the song.</summary>
    public double Beats => _e.PositionBeats;
}

/// <summary>What a watched device page keeps between frames: the spectrum's smoothing, the
/// level's fall, a scope buffer. One per watched (device, page) in the hub.</summary>
public sealed class DisplayState
{
    internal readonly DisplaySpectrum Spectrum = new();
    internal readonly double[] Silent = Enumerable.Repeat(-120.0, DisplaySpectrum.BandCount).ToArray();
    private float[] _scope = Array.Empty<float>();
    private double _level;
    private readonly List<double> _history = new();
    private bool _pushed;

    /// <summary>Called once per rendered frame.</summary>
    public void NextFrame() => _pushed = false;

    internal IReadOnlyList<double> History(double v)
    {
        if (!_pushed) { _history.Add(v); if (_history.Count > 128) _history.RemoveAt(0); _pushed = true; }
        return _history;
    }

    internal float[] ScopeBuffer(int n) => _scope.Length >= n ? _scope : _scope = new float[n];

    internal double SmoothLevel(double v)
    {
        _level = v > _level ? v : _level * 0.85 + v * 0.15;
        return _level;
    }
}
