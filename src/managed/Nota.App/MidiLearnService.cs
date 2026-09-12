// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The MIDI-learn brain: owns the mapping table + the learn state machine, and on
// every UI tick drains native CC/note events and applies them. When armed and a
// control is selected (pending), the next incoming event binds it; otherwise each
// event drives every mapping whose source matches. Apply goes straight to the
// engine via the same setters the on-screen controls use, so mapped controls work
// even when their card is closed. Buttons (mute/solo/transport) trigger/toggle;
// everything else is an absolute value scaled through the mapping's range.
//
// Gamepad buttons are a third source kind, pushed in by MainWindow.Gamepad rather
// than drained here (the pad queue is polled on its own tick). They bind, dispatch
// and persist exactly like a MIDI note.

using System;
using System.Collections.Generic;
using System.Linq;
using Nota.Application;

namespace Nota.App;

public sealed class MidiLearnService
{
    private const float VolumeMax = 1.5f;   // track/master fader travel (matches VFader.Max)
    private const float RackGainMax = 2.0f; // rack out / chain gain travel (matches the rack faders)

    private readonly IAudioEngine _engine;
    private readonly ILogSink? _log;
    private readonly List<MidiMapping> _mappings = new();
    private readonly int[] _buf = new int[256 * 4];   // reused each tick — up to 256 events
    // Distinct control sources seen recently, keyed by (isNote, channel, number) → last value
    // + hit count. Lets a headless client (MCP) discover what a connected controller emits.
    private readonly Dictionary<(bool isNote, int channel, int number), (int value, int count)> _seen = new();

    private bool _armed;
    private MidiBinding? _pending;
    // The gamepad button whose press just bound a control. Its release must not then
    // dispatch through the fresh mapping — that would immediately drive the control to
    // its floor the moment you let go of the button you learned with.
    private int? _padBoundOnPress;
    // Also echo MIDI to the console for live watching under `dotnet run` (opt-in, to
    // avoid flooding the terminal with a knob's CC stream during normal use).
    private readonly bool _echo = Environment.GetEnvironmentVariable("NOTA_MIDI_LOG") is not null;

    public MidiLearnService(IAudioEngine engine, ILogSink? log = null) { _engine = engine; _log = log; }

    private void LogMidi(string msg) { _log?.Info(msg); if (_echo) Console.WriteLine(msg); }

    public event Action? ArmedChanged;
    public event Action? PendingChanged;
    public event Action? MappingsChanged;

    /// <summary>Learn mode: controls highlight and a click selects a target to bind.</summary>
    public bool Armed
    {
        get => _armed;
        set
        {
            if (_armed == value) return;
            _armed = value;
            if (!_armed) ClearPending();
            ArmedChanged?.Invoke();
        }
    }

    /// <summary>The control awaiting a MIDI message (null = none selected yet).</summary>
    public MidiBinding? Pending => _pending;

    public void SelectForLearn(MidiBinding binding)
    {
        _pending = binding;
        PendingChanged?.Invoke();
    }

    public void ClearPending()
    {
        if (_pending is null) return;
        _pending = null;
        PendingChanged?.Invoke();
    }

    public IReadOnlyList<MidiMapping> Mappings => _mappings;

    /// <summary>The mapping bound to <paramref name="target"/>, if any (for the "mapped" badge).</summary>
    public MidiMapping? MappingFor(MidiTarget target) => _mappings.FirstOrDefault(m => m.Target.Equals(target));

    /// <summary>The mapping a gamepad control drives, if any. Matched on the control alone,
    /// not on which pad sent it: pad slots reshuffle when a controller is unplugged, so
    /// binding to a slot would silently break the mapping on the next replug.</summary>
    public MidiMapping? GamepadMappingFor(int controlId)
        => _mappings.FirstOrDefault(m => m.SourceKind == MidiSourceKind.Gamepad && m.Number == controlId);

    /// <summary>Feed a gamepad button edge through the learn/dispatch machine. Returns
    /// true when the edge was consumed — it bound the pending control, or it drove a
    /// mapping — so the caller knows to skip whatever the button does by default. An
    /// unmapped button returns false and goes on playing its note.</summary>
    public bool HandleGamepadButton(int buttonId, bool pressed)
    {
        if (pressed && _pending is { } p)
        {
            Bind(p, MidiSourceKind.Gamepad, 0, buttonId);
            LogMidi($"MIDI learn: gamepad {GamepadControls.Name(buttonId)} → bound to '{p.Name}'");
            _padBoundOnPress = buttonId;
            return true;
        }

        if (!pressed && _padBoundOnPress == buttonId)
        {
            _padBoundOnPress = null;
            return true;
        }

        // A button edge is momentary: held reads full-scale, released reads zero. On a
        // toggle target that fires once, on the press; on a continuous one it is a
        // hold-to-open, which is the same thing a mapped MIDI note already does.
        return Dispatch(MidiSourceKind.Gamepad, 0, buttonId, pressed ? 127 : 0) > 0;
    }

    // How far a stick or trigger must leave its rest position to count as a deliberate
    // move rather than a spring settling back. Only gates *learning*: once bound, the
    // whole travel drives the target.
    private const int AxisLearnThreshold = 24;

    /// <summary>Feed a gamepad stick or trigger position (0..127) through the same machine.
    /// Sampled on the UI tick, so call it only when the value actually moved. Returns true
    /// when it bound the pending control or drove a mapping.</summary>
    public bool HandleGamepadAxis(int axisId, int value127)
    {
        if (_pending is { } p && Math.Abs(value127 - GamepadControls.Rest(axisId)) >= AxisLearnThreshold)
        {
            Bind(p, MidiSourceKind.Gamepad, 0, axisId);
            LogMidi($"MIDI learn: gamepad {GamepadControls.Name(axisId)} → bound to '{p.Name}'");
            return true;
        }
        return Dispatch(MidiSourceKind.Gamepad, 0, axisId, value127) > 0;
    }

    public void RemoveMapping(MidiMapping m)
    {
        if (_mappings.Remove(m)) MappingsChanged?.Invoke();
    }

    public void Clear()
    {
        if (_mappings.Count == 0) return;
        _mappings.Clear();
        MappingsChanged?.Invoke();
    }

    /// <summary>Remove the mapping at <paramref name="index"/> (into <see cref="Mappings"/>).</summary>
    public bool RemoveMappingAt(int index)
    {
        if (index < 0 || index >= _mappings.Count) return false;
        _mappings.RemoveAt(index);
        MappingsChanged?.Invoke();
        return true;
    }

    /// <summary>Edit a mapping's output window / inversion by index.</summary>
    public bool SetMappingRange(int index, double min, double max, bool invert)
    {
        if (index < 0 || index >= _mappings.Count) return false;
        var m = _mappings[index];
        m.RangeMin = min; m.RangeMax = max; m.Invert = invert;
        MappingsChanged?.Invoke();
        return true;
    }

    // ---- recent controller activity (for headless discovery) --------------

    private void RecordSeen(bool isNote, int channel, int number, int value)
    {
        var key = (isNote, channel, number);
        int count = _seen.TryGetValue(key, out var e) ? e.count + 1 : 1;
        _seen[key] = (value, count);
        if (_seen.Count > 256) _seen.Clear();   // safety cap; distinct controls rarely near this
    }

    /// <summary>Distinct MIDI controls seen since the last clear: (isNote, channel, number,
    /// last value, hit count). Lets a client see what a connected controller is sending.</summary>
    public IReadOnlyList<(bool isNote, int channel, int number, int lastValue, int count)> RecentControls()
    {
        var list = new List<(bool, int, int, int, int)>(_seen.Count);
        foreach (var kv in _seen) list.Add((kv.Key.isNote, kv.Key.channel, kv.Key.number, kv.Value.value, kv.Value.count));
        return list;
    }

    /// <summary>Forget the recorded controller activity (start a fresh discovery window).</summary>
    public void ClearRecentControls() => _seen.Clear();

    // ---- per-tick drain + apply -------------------------------------------

    public void Tick()
    {
        int n = _engine.PollMidiControlEvents(_buf);
        for (int i = 0; i < n; i++)
        {
            var kind = _buf[i * 4] == 0 ? MidiSourceKind.Cc : MidiSourceKind.Note;
            int channel = _buf[i * 4 + 1];
            int number = _buf[i * 4 + 2];
            int value = _buf[i * 4 + 3];
            string src = $"{(kind == MidiSourceKind.Cc ? "CC" : "Note")} {number} ch{channel + 1} v{value}";
            RecordSeen(kind == MidiSourceKind.Note, channel, number, value);

            if (_pending is { } p)
            {
                Bind(p, kind, channel, number);
                LogMidi($"MIDI learn: {src} → bound to '{p.Name}'");
                continue;
            }
            int matched = Dispatch(kind, channel, number, value);
            LogMidi(matched > 0 ? $"MIDI in: {src} → {matched} mapping(s)" : $"MIDI in: {src} (unmapped)");
        }
    }

    private void Bind(MidiBinding binding, MidiSourceKind kind, int channel, int number)
    {
        // Rebinding a control replaces its old mapping; a source may still drive
        // several targets (fan-out), so we only dedupe on the target.
        _mappings.RemoveAll(m => m.Target.Equals(binding.Target));
        _mappings.Add(new MidiMapping
        {
            Target = binding.Target,
            DisplayName = binding.Name,
            SourceKind = kind,
            Channel = channel,
            Number = number,
        });
        ClearPending();
        MappingsChanged?.Invoke();
    }

    private int Dispatch(MidiSourceKind kind, int channel, int number, int value)
    {
        int matched = 0;
        foreach (var m in _mappings)
            if (m.SourceKind == kind && m.Channel == channel && m.Number == number)
            {
                Apply(m, value);
                matched++;
            }
        return matched;
    }

    private void Apply(MidiMapping m, int value127)
    {
        var t = m.Target;
        if (t.IsButton)
        {
            // A note-on, or anything else crossing the half-way point. Latch on the edge:
            // a swept CC or a squeezed trigger sends a run of values past the threshold,
            // and toggling on each one would make the target flutter.
            bool on = m.SourceKind == MidiSourceKind.Note ? value127 > 0 : value127 >= 64;
            if (m.Invert) on = !on;
            if (on == m.TriggerLatch) return;
            m.TriggerLatch = on;
            if (on) Trigger(t);
            return;
        }

        double norm = Math.Clamp(value127 / 127.0, 0, 1);
        if (m.Invert) norm = 1.0 - norm;
        double outv = m.RangeMin + norm * (m.RangeMax - m.RangeMin);   // 0..1 within the mapped window

        BeginLatchWrite(t);   // a hardware move is a latching automation gesture

        switch (t.Kind)
        {
            case MidiTargetKind.DeviceParam:
            {
                float min = _engine.DeviceParamMin(t.TrackId, t.DeviceIndex, t.ParamIndex);
                float max = _engine.DeviceParamMax(t.TrackId, t.DeviceIndex, t.ParamIndex);
                _engine.DeviceSetParam(t.TrackId, t.DeviceIndex, t.ParamIndex, (float)(min + outv * (max - min)));
                break;
            }
            case MidiTargetKind.PluginParam:
                _engine.PluginParamSet(t.TrackId, t.DeviceIndex, t.ParamIndex, (float)outv);
                break;
            case MidiTargetKind.MidiDeviceParam:
            {
                float min = _engine.MidiEffectParamMin(t.TrackId, t.DeviceIndex, t.ParamIndex);
                float max = _engine.MidiEffectParamMax(t.TrackId, t.DeviceIndex, t.ParamIndex);
                _engine.MidiEffectSetParam(t.TrackId, t.DeviceIndex, t.ParamIndex, (float)(min + outv * (max - min)));
                break;
            }
            case MidiTargetKind.TrackVolume: _engine.SetTrackVolume(t.TrackId, (float)(outv * VolumeMax)); break;
            case MidiTargetKind.MasterVolume: _engine.SetMasterVolume((float)(outv * VolumeMax)); break;
            case MidiTargetKind.TrackPan: _engine.SetTrackPan(t.TrackId, (float)(outv * 2.0 - 1.0)); break;
            case MidiTargetKind.RackVolume:
                if (t.DeviceIndex < 0) _engine.RackSetVolume(t.TrackId, (float)(outv * RackGainMax));
                else _engine.RackDevSetVolume(t.TrackId, t.DeviceIndex, (float)(outv * RackGainMax));
                break;
            case MidiTargetKind.RackChainGain:
                if (t.DeviceIndex < 0) _engine.RackSetChainGain(t.TrackId, t.ParamIndex, (float)(outv * RackGainMax));
                else _engine.RackDevSetChainGain(t.TrackId, t.DeviceIndex, t.ParamIndex, (float)(outv * RackGainMax));
                break;
        }
    }

    // Moving a hardware control (knob, fader, stick, trigger) is a latching automation
    // gesture: while the transport is recording it writes this parameter's lane and keeps
    // writing past the release until the transport stops — a physical control has no
    // "release", so Live's Latch is the behaviour that fits. With record off, the engine
    // turns this into an override of an already-automated lane, and ignores it otherwise.
    // Targets with no automation lane of their own (master volume, rack chains) are skipped.
    private void BeginLatchWrite(MidiTarget t)
    {
        switch (t.Kind)
        {
            case MidiTargetKind.DeviceParam:
                _engine.BeginAutomationWrite(t.TrackId, AutomationTarget.DeviceParam, t.DeviceIndex, t.ParamIndex, "", latch: true);
                break;
            case MidiTargetKind.MidiDeviceParam:
                _engine.BeginAutomationWrite(t.TrackId, AutomationTarget.MidiDeviceParam, t.DeviceIndex, t.ParamIndex, "", latch: true);
                break;
            case MidiTargetKind.PluginParam:
                _engine.BeginAutomationWrite(t.TrackId, AutomationTarget.PluginParam, t.DeviceIndex, -1,
                    _engine.PluginParamId(t.TrackId, t.DeviceIndex, t.ParamIndex), latch: true);
                break;
            case MidiTargetKind.TrackVolume:
                _engine.BeginAutomationWrite(t.TrackId, AutomationTarget.Volume, -1, -1, "", latch: true);
                break;
            case MidiTargetKind.TrackPan:
                _engine.BeginAutomationWrite(t.TrackId, AutomationTarget.Pan, -1, -1, "", latch: true);
                break;
        }
    }

    private void Trigger(MidiTarget t)
    {
        switch (t.Kind)
        {
            case MidiTargetKind.TransportPlay: _engine.Play(); break;
            case MidiTargetKind.TransportStop: _engine.StopTransport(); break;
            case MidiTargetKind.TransportRecord: _engine.SetRecording(!_engine.IsRecording); break;
            case MidiTargetKind.TrackMute: _engine.SetTrackMute(t.TrackId, !TrackFlag(t.TrackId, mute: true)); break;
            case MidiTargetKind.TrackSolo: _engine.SetTrackSolo(t.TrackId, !TrackFlag(t.TrackId, mute: false)); break;
            case MidiTargetKind.RackChainMute:
                if (t.DeviceIndex < 0) _engine.RackSetChainMute(t.TrackId, t.ParamIndex, !_engine.RackChainMute(t.TrackId, t.ParamIndex));
                else _engine.RackDevSetChainMute(t.TrackId, t.DeviceIndex, t.ParamIndex, !_engine.RackDevChainMute(t.TrackId, t.DeviceIndex, t.ParamIndex));
                break;
            case MidiTargetKind.RackChainSolo:
                if (t.DeviceIndex < 0) _engine.RackSetChainSolo(t.TrackId, t.ParamIndex, !_engine.RackChainSolo(t.TrackId, t.ParamIndex));
                else _engine.RackDevSetChainSolo(t.TrackId, t.DeviceIndex, t.ParamIndex, !_engine.RackDevChainSolo(t.TrackId, t.DeviceIndex, t.ParamIndex));
                break;
        }
    }

    private bool TrackFlag(int trackId, bool mute)
    {
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId)
                return (mute ? ti.Muted : ti.Soloed) != 0;
        return false;
    }

    // ---- persistence seam (see project DTO mapping) -----------------------

    // ---- project persistence (sidecar in the .nota bundle) ----------------
    //
    // Mappings travel with the project as midimap.json. Runtime TrackIds aren't
    // stable across sessions, so we store each track-scoped mapping by the track's
    // position (its TryGetTrackInfo index) and re-resolve on load; globals use -1.

    private const string SidecarName = "midimap.json";

    private sealed class Dto
    {
        public int TrackIndex { get; set; } = -1;
        public int Kind { get; set; }
        public int DeviceIndex { get; set; } = -1;
        public int ParamIndex { get; set; } = -1;
        public int SourceKind { get; set; }
        public int Channel { get; set; }
        public int Number { get; set; }
        public double RangeMin { get; set; }
        public double RangeMax { get; set; } = 1;
        public bool Invert { get; set; }
        public string DisplayName { get; set; } = "";
    }

    public void SaveMappings(string bundleDir)
    {
        var list = new List<Dto>(_mappings.Count);
        foreach (var m in _mappings)
        {
            var t = m.Target;
            list.Add(new Dto
            {
                TrackIndex = IndexOfTrackId(t.TrackId),
                Kind = (int)t.Kind,
                DeviceIndex = t.DeviceIndex,
                ParamIndex = t.ParamIndex,
                SourceKind = (int)m.SourceKind,
                Channel = m.Channel,
                Number = m.Number,
                RangeMin = m.RangeMin,
                RangeMax = m.RangeMax,
                Invert = m.Invert,
                DisplayName = m.DisplayName,
            });
        }
        var json = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(System.IO.Path.Combine(bundleDir, SidecarName), json);
    }

    public void LoadMappings(string bundleDir)
    {
        _mappings.Clear();
        var path = System.IO.Path.Combine(bundleDir, SidecarName);
        if (System.IO.File.Exists(path))
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<Dto>>(System.IO.File.ReadAllText(path)) ?? new();
                foreach (var d in list)
                {
                    var kind = (MidiTargetKind)d.Kind;
                    int trackId = TrackScoped(kind) ? TrackIdAtIndex(d.TrackIndex) : -1;
                    if (TrackScoped(kind) && trackId < 0) continue;   // track since deleted
                    _mappings.Add(new MidiMapping
                    {
                        Target = new MidiTarget(kind, trackId, d.DeviceIndex, d.ParamIndex),
                        DisplayName = d.DisplayName,
                        SourceKind = (MidiSourceKind)d.SourceKind,
                        Channel = d.Channel,
                        Number = d.Number,
                        RangeMin = d.RangeMin,
                        RangeMax = d.RangeMax,
                        Invert = d.Invert,
                    });
                }
            }
            catch { /* a corrupt sidecar just means no mappings — never block the load */ }
        }
        MappingsChanged?.Invoke();
    }

    private static bool TrackScoped(MidiTargetKind k) => k
        is MidiTargetKind.DeviceParam or MidiTargetKind.PluginParam or MidiTargetKind.MidiDeviceParam
        or MidiTargetKind.TrackVolume or MidiTargetKind.TrackPan or MidiTargetKind.TrackMute or MidiTargetKind.TrackSolo
        or MidiTargetKind.RackVolume or MidiTargetKind.RackChainGain
        or MidiTargetKind.RackChainMute or MidiTargetKind.RackChainSolo;

    private int IndexOfTrackId(int trackId)
    {
        if (trackId < 0) return -1;
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return i;
        return -1;
    }

    private int TrackIdAtIndex(int index)
        => index >= 0 && _engine.TryGetTrackInfo(index, out var ti) ? ti.Id : -1;
}
