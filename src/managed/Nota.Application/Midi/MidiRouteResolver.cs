// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which saved MIDI port a route can use right now (specs/115 R8). A route names a port by the uid
// the system gave it; this decides, against the ports connected now, whether that port is there
// (Available), gone (Missing), listed twice so Nota can't tell which (Ambiguous) or connected but
// past the 16-input limit (OverLimit). A port with a similar or even the same *name* never stands
// in for a missing uid (FR-006): a route keeps its device or sends nothing. The engine is only ever
// handed a uid this says is Available, or "" (send nothing).

namespace Nota.Application.Midi;

/// <summary>Where a saved port or role stands against the ports connected now.</summary>
public enum RouteAvailability
{
    Available,
    /// <summary>No connected port has the saved uid.</summary>
    Missing,
    /// <summary>Two or more connected ports report the saved uid.</summary>
    Ambiguous,
    /// <summary>An output role that is not bound to a port.</summary>
    Unbound,
    /// <summary>An input that is connected and enabled but past the 16-input limit (#1011).</summary>
    OverLimit,
}

public sealed class MidiRouteResolver
{
    private readonly IAudioEngine _engine;
    private uint _generation;
    private bool _loaded;
    private IReadOnlyList<MidiPortInfo> _inputs = Array.Empty<MidiPortInfo>();
    private IReadOnlyList<MidiOutputInfo> _outputs = Array.Empty<MidiOutputInfo>();

    public MidiRouteResolver(IAudioEngine engine) => _engine = engine;

    /// <summary>Raised after the port lists changed (a device came or went). Message thread.</summary>
    public event Action? Changed;

    /// <summary>The inputs and outputs as last read.</summary>
    public IReadOnlyList<MidiPortInfo> Inputs { get { EnsureLoaded(); return _inputs; } }
    public IReadOnlyList<MidiOutputInfo> Outputs { get { EnsureLoaded(); return _outputs; } }

    /// <summary>Re-read the port lists when the engine says they changed (or always, with
    /// <paramref name="force"/>), and raise <see cref="Changed"/> if they did. Call it from the UI
    /// tick, after the controllers' tick has applied the new port set.</summary>
    public bool Refresh(bool force = false)
    {
        uint gen = _engine.MidiPortsGeneration;
        if (_loaded && !force && gen == _generation) return false;
        _generation = gen;
        _loaded = true;
        _inputs = _engine.MidiPorts();
        _outputs = _engine.MidiOutputs();
        Changed?.Invoke();
        return true;
    }

    private void EnsureLoaded()
    {
        if (!_loaded) Refresh(force: true);
    }

    /// <summary>A saved input port, by uid.</summary>
    public RouteAvailability Input(string? uid)
    {
        EnsureLoaded();
        return ResolveInput(_inputs, uid);
    }

    /// <summary>A saved output port, by uid.</summary>
    public RouteAvailability Output(string? uid)
    {
        EnsureLoaded();
        return ResolveOutput(_outputs, uid);
    }

    /// <summary>The uid to hand the engine for a saved output: itself when it is available, else ""
    /// so the engine sends nothing.</summary>
    public string EngineOutputUid(string? uid) => Output(uid) == RouteAvailability.Available ? uid! : "";

    /// <summary>The connected port's current name, or null when it is not connected.</summary>
    public string? InputName(string? uid) { EnsureLoaded(); return _inputs.FirstOrDefault(p => p.Uid == uid).Name; }
    public string? OutputName(string? uid) { EnsureLoaded(); return _outputs.FirstOrDefault(p => p.Uid == uid).Name; }

    // ---- the rules, as pure functions (the smoke test drives them directly) --------------------

    public static RouteAvailability ResolveInput(IReadOnlyList<MidiPortInfo> inputs, string? uid)
    {
        if (string.IsNullOrEmpty(uid)) return RouteAvailability.Missing;
        int n = 0;
        bool over = false;
        foreach (var p in inputs)
            if (p.Uid == uid) { n++; over |= p.OverLimit; }
        return n switch
        {
            0 => RouteAvailability.Missing,
            1 => over ? RouteAvailability.OverLimit : RouteAvailability.Available,
            _ => RouteAvailability.Ambiguous,
        };
    }

    public static RouteAvailability ResolveOutput(IReadOnlyList<MidiOutputInfo> outputs, string? uid)
    {
        if (string.IsNullOrEmpty(uid)) return RouteAvailability.Missing;
        int n = outputs.Count(p => p.Uid == uid);
        return n switch
        {
            0 => RouteAvailability.Missing,
            1 => RouteAvailability.Available,
            _ => RouteAvailability.Ambiguous,
        };
    }
}
