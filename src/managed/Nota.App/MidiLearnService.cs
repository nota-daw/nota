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
using Nota.Application.Editing;
using Nota.Application.Localization;
using Nota.App.Controllers;

namespace Nota.App;

public sealed class MidiLearnService
{
    private readonly IAudioEngine _engine;
    // Values land through the applier shared with controller profiles (specs/051): latching
    // automation and one undo step per movement (specs/009, specs/016).
    private readonly ControlApplier _applier;
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

    public MidiLearnService(IAudioEngine engine, ControlApplier applier, ILogSink? log = null)
    { _engine = engine; _applier = applier; _log = log; }

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
            LogMidi(string.Format("MIDI learn: gamepad {0} → bound to '{1}'", GamepadControls.Name(buttonId), p.Name));
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
            LogMidi(string.Format("MIDI learn: gamepad {0} → bound to '{1}'", GamepadControls.Name(axisId), p.Name));
            return true;
        }
        return Dispatch(MidiSourceKind.Gamepad, 0, axisId, value127) > 0;
    }

    /// <summary>The mappings a phone control drives for a phone on <paramref name="trackId"/>. Tied
    /// to the control, not the phone, so every phone on that track drives the same targets. The
    /// XY pad and tilt are per track; a track without its own falls back to an unscoped mapping.</summary>
    private List<MidiMapping> PhoneMappings(int controlId, int trackId)
    {
        int scope = PhoneScope(controlId, trackId);
        var own = _mappings.Where(m => m.SourceKind == MidiSourceKind.Phone && m.Number == controlId && m.ScopeTrackId == scope).ToList();
        if (own.Count == 0 && scope != 0)
            own = _mappings.Where(m => m.SourceKind == MidiSourceKind.Phone && m.Number == controlId && m.ScopeTrackId == 0).ToList();
        return own;
    }

    private static int PhoneScope(int controlId, int trackId)
        => Nota.Remote.PhoneControls.PerTrack(controlId) ? Math.Max(0, trackId) : 0;

    /// <summary>The mapping a phone control drives on that track, if any.</summary>
    public MidiMapping? PhoneMappingFor(int controlId, int trackId) => PhoneMappings(controlId, trackId).FirstOrDefault();

    /// <summary>Feed a Nota Remote control (normalized 0..1) from a phone on <paramref name="trackId"/>.
    /// Binds the pending control, or drives every mapping on it — at full resolution, not
    /// quantized to 0..127.</summary>
    public Nota.Remote.PhoneControlResult HandlePhoneControl(int controlId, int trackId, double norm)
    {
        if (_pending is { } p)
        {
            Bind(p, MidiSourceKind.Phone, 0, controlId, PhoneScope(controlId, trackId));
            LogMidi(string.Format("MIDI learn: phone {0} → bound to '{1}'", Nota.Remote.PhoneControls.Name(controlId), p.Name));
            return Nota.Remote.PhoneControlResult.Bound;
        }
        var list = PhoneMappings(controlId, trackId);
        foreach (var m in list) ApplyNormalized(m, Math.Clamp(norm, 0, 1));
        if (list.Count > 0) EventCount++;
        return list.Count > 0 ? Nota.Remote.PhoneControlResult.Mapped : Nota.Remote.PhoneControlResult.None;
    }

    /// <summary>The source as the MIDI map lists it; a per-track phone control names its track.</summary>
    public string SourceLabel(MidiMapping m)
    {
        if (m.SourceKind != MidiSourceKind.Phone || m.ScopeTrackId <= 0) return m.SourceLabel;
        int i = IndexOfTrackId(m.ScopeTrackId);
        string name = _engine.GetTrackName(m.ScopeTrackId);
        return $"{m.SourceLabel} · {(name.Length > 0 ? name : $"Track {i + 1}")}";
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

    /// <summary>Monotonic count of control events drained since start. The transport
    /// bar's activity dot lights while this keeps moving, so a connected-but-silent
    /// controller is diagnosable without opening the mappings tab.</summary>
    public long EventCount { get; private set; }

    /// <summary>Forget the recorded controller activity (start a fresh discovery window).</summary>
    public void ClearRecentControls() => _seen.Clear();

    // ---- per-tick drain + apply -------------------------------------------

    public void Tick()
    {
        long nowMs = Environment.TickCount64;
        _applier.Poll(nowMs);   // end controller movements after their pause
        int n = _engine.PollMidiControlEvents(_buf);
        EventCount += n;
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
                LogMidi(string.Format("MIDI learn: {0} → bound to '{1}'", src, p.Name));
                continue;
            }
            int matched = Dispatch(kind, channel, number, value);
            LogMidi(matched > 0 ? string.Format("MIDI in: {0} → {1} mapping(s)", src, matched) : string.Format("MIDI in: {0} (unmapped)", src));
        }
    }

    private void Bind(MidiBinding binding, MidiSourceKind kind, int channel, int number, int scopeTrackId = 0)
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
            ScopeTrackId = scopeTrackId,
        });
        ClearPending();
        MappingsChanged?.Invoke();
    }

    /// <summary>A MIDI Learn mapping on a port a controller profile owns whole (specs/051): the
    /// controller hands the message over, because an explicit mapping beats a role (FR-012).
    /// Returns whether a mapping took it.</summary>
    public bool DispatchFromController(bool isNote, int channel, int number, int value)
    {
        int matched = Dispatch(isNote ? MidiSourceKind.Note : MidiSourceKind.Cc, channel, number, value);
        if (matched > 0) EventCount++;
        return matched > 0;
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
        if (m.Target.IsButton && m.SourceKind == MidiSourceKind.Note)
        {
            // A note-on fires; anything else crosses the half-way point (ApplyNormalized).
            ApplyNormalized(m, value127 > 0 ? 1.0 : 0.0);
            return;
        }
        ApplyNormalized(m, Math.Clamp(value127 / 127.0, 0, 1));
    }

    private void ApplyNormalized(MidiMapping m, double norm)
    {
        var t = m.Target;
        if (t.IsButton)
        {
            // Crossing the half-way point. Latch on the edge: a swept CC or a squeezed
            // trigger sends a run of values past the threshold, and toggling on each one
            // would make the target flutter.
            bool on = norm >= 0.5;
            if (m.Invert) on = !on;
            if (on == m.TriggerLatch) return;
            m.TriggerLatch = on;
            if (on) _applier.Trigger(t);
            return;
        }

        if (m.Invert) norm = 1.0 - norm;
        _applier.SetNormalized(t, m.RangeMin + norm * (m.RangeMax - m.RangeMin));   // 0..1 within the mapped window
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
        /// <summary>A plug-in / built-in param's stable id (null in files from 0.59 and older):
        /// the index is looked up by it on load, so a device whose layout grows keeps its mappings.</summary>
        public string? ParamId { get; set; }
        public int SourceKind { get; set; }
        public int Channel { get; set; }
        public int Number { get; set; }
        public double RangeMin { get; set; }
        public double RangeMax { get; set; } = 1;
        public bool Invert { get; set; }
        public string DisplayName { get; set; } = "";
        /// <summary>A per-track phone control's track (−1 = none).</summary>
        public int ScopeTrackIndex { get; set; } = -1;
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
                ParamId = t.Kind == MidiTargetKind.PluginParam ? _engine.PluginParamId(t.TrackId, t.DeviceIndex, t.ParamIndex) : null,
                SourceKind = (int)m.SourceKind,
                Channel = m.Channel,
                Number = m.Number,
                RangeMin = m.RangeMin,
                RangeMax = m.RangeMax,
                Invert = m.Invert,
                DisplayName = m.DisplayName,
                ScopeTrackIndex = m.ScopeTrackId > 0 ? IndexOfTrackId(m.ScopeTrackId) : -1,
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
                    int scope = d.ScopeTrackIndex >= 0 ? TrackIdAtIndex(d.ScopeTrackIndex) : 0;
                    if (scope < 0) continue;                          // its phone's track since deleted
                    _mappings.Add(new MidiMapping
                    {
                        Target = new MidiTarget(kind, trackId, d.DeviceIndex,
                            kind == MidiTargetKind.PluginParam ? PluginParamIndex(trackId, d) : d.ParamIndex),
                        DisplayName = d.DisplayName,
                        SourceKind = (MidiSourceKind)d.SourceKind,
                        Channel = d.Channel,
                        Number = d.Number,
                        RangeMin = d.RangeMin,
                        RangeMax = d.RangeMax,
                        Invert = d.Invert,
                        ScopeTrackId = scope,
                    });
                }
            }
            catch { /* a corrupt sidecar just means no mappings — never block the load */ }
        }
        MappingsChanged?.Invoke();
    }

    // A plug-in / built-in param mapping's index today: found by its saved id, or for a file
    // from 0.59 and older (no id) moved as Nota Twin's grown layout moved it (#531).
    private int PluginParamIndex(int trackId, Dto d)
    {
        if (d.ParamId is not { Length: > 0 } id) return _engine.PluginParamIndexFromV1(trackId, d.DeviceIndex, d.ParamIndex);
        if (_engine.PluginParamId(trackId, d.DeviceIndex, d.ParamIndex) == id) return d.ParamIndex;
        int n = _engine.PluginParamCount(trackId, d.DeviceIndex);
        for (int i = 0; i < n; i++)
            if (_engine.PluginParamId(trackId, d.DeviceIndex, i) == id) return i;
        return d.ParamIndex;
    }

    private static bool TrackScoped(MidiTargetKind k) => k
        is MidiTargetKind.DeviceParam or MidiTargetKind.PluginParam or MidiTargetKind.MidiDeviceParam
        or MidiTargetKind.TrackVolume or MidiTargetKind.TrackPan or MidiTargetKind.TrackMute or MidiTargetKind.TrackSolo
        or MidiTargetKind.TrackCue
        or MidiTargetKind.RackVolume or MidiTargetKind.RackChainGain
        or MidiTargetKind.RackChainMute or MidiTargetKind.RackChainSolo
        or MidiTargetKind.TrackArm or MidiTargetKind.TrackSelect or MidiTargetKind.TrackSend;

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
