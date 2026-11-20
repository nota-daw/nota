// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Controller auto-mapping (specs/051), driven from the UI tick. It finds out what is connected
// — shipped profiles (embedded) and the user's (<DataDir>/controllers), the engine's MIDI port
// generation for hot-plug, each new port's MIDI Identity Reply — and, for a recognised and
// enabled controller, makes it Active: sends the profile's init, claims its role messages in the
// engine (so they never play an instrument), and turns what arrives into actions applied through
// the same ControlApplier MIDI Learn uses (latching automation, one undo step per movement).
//
// An explicit MIDI Learn mapping beats a role (FR-012): learned controls are left out of the
// claims, and on a port a profile owns whole they are handed to MIDI Learn. While Learn waits for
// a message the engine copies claimed messages to it too, and roles hold still.
//
// A controller's session state (bank, page, knob mode) is kept by port uid across an unplug, so
// a replug resumes where it was; opening a project resets it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nota.Application;
using Nota.Application.Controllers;
using Nota.Application.Controllers.Mcu;
using Nota.Application.Controllers.Hui;
using Nota.Application.Localization;

namespace Nota.App.Controllers;

public enum ControllerStage { Identifying, Matched, Unmatched, Active, Gone }

public sealed class ConnectedController
{
    public required string Uid { get; init; }
    public string Name { get; set; } = "";
    public int Source { get; set; }
    public string OutputUid { get; set; } = "";
    public ControllerStage Stage { get; set; }
    public IdentityReply? Identity { get; set; }
    public ControllerProfile? Profile { get; set; }
    public ControllerState State { get; } = new();
    internal RoleRouter Router { get; } = new();
    internal long IdentifyUntilMs { get; set; }
    internal int UnknownLogged { get; set; }
    // Last LED state sent per feedback control (null = unknown: send on the next tick).
    internal Dictionary<ControlDecl, bool> Leds { get; } = new(ReferenceEqualityComparer.Instance);
    // Soft takeover per absolute control (FR-019); all let go when State.Epoch moves.
    internal Dictionary<ControlDecl, PickupState> Pickups { get; } = new(ReferenceEqualityComparer.Instance);
    internal int PickupEpoch { get; set; } = -1;
    // A Mackie Control / HUI surface: its codec, feedback and touch state (US4 / US6).
    internal ProtocolSurface? Surface { get; set; }
    // Last value-feedback sent per control (a ring, a motor fader), 14-bit.
    internal Dictionary<ControlDecl, int> Values { get; } = new(ReferenceEqualityComparer.Instance);
    // Pinned buttons held down (a press fires once).
    internal Dictionary<ControlDecl, bool> PinDown { get; } = new(ReferenceEqualityComparer.Instance);

    public bool HasKnobs => Profile?.Controls.Any(c => c.Role.Kind == ControlRoleKind.Knob) == true
                            || Profile?.Protocol is ControllerProtocol.Mcu or ControllerProtocol.Hui;

    public bool HasFaders => Profile?.Controls.Any(c => c.Role.Kind == ControlRoleKind.Fader) == true
                             || Profile?.Protocol is ControllerProtocol.Mcu or ControllerProtocol.Hui;
}

public sealed class ControllerService
{
    private const long IdentityTimeoutMs = 500;
    private const long ScanEveryMs = 2000;   // also catches inputs the engine opened late (after the audio device)

    private readonly IAudioEngine _engine;
    private readonly ControlApplier _applier;
    private readonly ISettingsService _settings;
    private readonly ILogSink? _log;
    private readonly string _userDir;
    private readonly Dictionary<string, ConnectedController> _controllers = new();   // by port uid, Gone ones kept
    private readonly ControlDecoder _decoder = new();
    private List<ControllerProfile> _profiles = new();
    private readonly List<string> _profileWarnings = new();
    private MidiLearnService? _learn;
    private uint _generation;
    private bool _started;
    private bool _claimsDirty = true;
    private long _lastScanMs;
    private readonly byte[] _sysex = new byte[512];
    private readonly int[] _events = new int[256 * 5];

    public ControllerService(IAudioEngine engine, ControlApplier applier, ISettingsService settings, string userProfileDir, ILogSink? log = null)
    {
        _engine = engine;
        _applier = applier;
        _settings = settings;
        _userDir = userProfileDir;
        _log = log;
        FocusChanged += InvalidateSummary;
        BankChanged += InvalidateSummary;
        ControllersChanged += InvalidateSummary;
    }

    /// <summary>Connected (and recently unplugged) controllers, by port uid.</summary>
    public IReadOnlyCollection<ConnectedController> Controllers => _controllers.Values;
    public IReadOnlyList<ControllerProfile> Profiles => _profiles;
    /// <summary>Why profile files or controls were skipped, one line each, file name first.</summary>
    public IReadOnlyList<string> ProfileWarnings => _profileWarnings;
    public string UserProfileFolder => _userDir;

    public event Action? ControllersChanged;
    /// <summary>A controller became Active (once per connect): for the "connected" notice.</summary>
    public event Action<ConnectedController>? Connected;
    /// <summary>The tracks under some controller's bank changed.</summary>
    public event Action? BankChanged;
    /// <summary>The focused device, its page or a knob mode changed (card markers, indicator).</summary>
    public event Action? FocusChanged;
    /// <summary>Which controls wait to pick up changed (the pickup hint, FR-020).</summary>
    public event Action? PickupChanged;

    // ---- device focus (FR-016) ------------------------------------------------------------------

    private int _selectedTrack = -1;
    private int _panelTrack = -1;
    private DeviceAddress? _panelSelection;
    private FocusedDevice? _focused;
    private string _focusSignature = "";
    private long _focusCheckMs;
    /// <summary>Edited pages (project copy, then the user's); null until US5 wires the store.</summary>
    public IDevicePageSource? EditedPages { get; set; }

    /// <summary>Runs a command by its palette id (<c>edit.undo</c> …) and says whether it ran;
    /// the main window supplies it. Null (headless, tests) = command buttons do nothing.</summary>
    public Func<string, bool>? RunCommand { get; set; }

    /// <summary>Whether the Session view is showing: the jog wheel and the track buttons then walk
    /// its clip selection too (specs/051 Story 8). Null = never.</summary>
    public Func<bool>? SessionShown { get; set; }

    // ---- the user's corrections from a control's menu (Story 7) ------------------------------

    private ControllerOverrides? _overrides;
    /// <summary>Unbinds and pins (FR-032 / FR-033); null = none (tests).</summary>
    public ControllerOverrides? Overrides
    {
        get => _overrides;
        set
        {
            if (_overrides is { } old) old.Changed -= OverridesChanged;
            _overrides = value;
            if (value is not null) value.Changed += OverridesChanged;
            OverridesChanged();
        }
    }

    private void OverridesChanged()
    {
        _claimsDirty = true;
        foreach (var c in _controllers.Values) { c.Pickups.Clear(); c.PinDown.Clear(); }
        UpdateGhosts();
        ControllersChanged?.Invoke();
    }

    /// <summary>A MIDI Learn target as the overrides keep it, its whole address (a rack, a chain, a
    /// chain device) so a pin comes back on the same control.</summary>
    public static StoredTarget Store(MidiTarget t)
        => new((int)t.Kind, t.TrackId > 0 ? t.TrackId : -1, t.ParamIndex, t.DeviceIndex, t.Sub);

    public static MidiTarget FromStored(StoredTarget s)
        => new((MidiTargetKind)s.Kind, s.TrackId, s.Device, s.Param, s.Sub);

    private bool Unbound(MidiTarget t) => _overrides?.IsUnbound(Store(t)) == true;

    /// <summary>A hardware control that drives a target now: the controller and the control's name.</summary>
    public readonly record struct HardwareBinding(ConnectedController Controller, string Label, bool Pinned, int Knob = -1);

    /// <summary>Which hardware controls drive <paramref name="t"/> right now (FR-031): a pin, the
    /// focused device's knob, the bank fader / knob over its track, a transport or master role.</summary>
    public List<HardwareBinding> BindingsFor(MidiTarget t)
    {
        var list = new List<HardwareBinding>();
        var active = _controllers.Values.Where(c => c.Stage == ControllerStage.Active && c.Profile is not null).ToList();
        if (_overrides is { } o)
            foreach (var pin in o.PinsFor(Store(t)))
                foreach (var c in active.Where(c => c.Profile!.Id == pin.ProfileId))
                    list.Add(new HardwareBinding(c, pin.Label, true));
        if (Unbound(t)) return list;
        if (KnobFocus() is { } kf)
            for (int k = 0; k < kf.Knobs.Count; k++)
                if (kf.Knobs[k].Target is { } kt && kt.Equals(t)) list.Add(new HardwareBinding(kf.Controller, KnobLabel(k), false, k));
        if (list.Count > 0 && list[^1].Knob >= 0) return list;
        List<int>? ids = null;
        foreach (var c in active)
        {
            var p = c.Profile!;
            bool generic = p.Protocol == ControllerProtocol.Generic;
            if (t.Kind == MidiTargetKind.TrackVolume)
            {
                ids ??= TrackIds();
                int i = ids.IndexOf(t.TrackId) - c.State.BankOrigin;
                if (i < 0 || i >= ControllerState.BankSize) continue;
                bool has = generic ? p.Controls.Any(d => d.Role is { Kind: ControlRoleKind.Fader } r && r.Index == i && Pin(c, d) is null) : true;
                if (has) list.Add(new HardwareBinding(c, Loc.F("controllers.control.fader", i + 1), false));
            }
            else if (t.Kind is MidiTargetKind.TrackPan or MidiTargetKind.TrackSend && c.HasKnobs && c.State.KnobMode.Kind != KnobModeKind.Device)
            {
                ids ??= TrackIds();
                for (int k = 0; k < ControllerState.BankSize; k++)
                    if (RoleRouter.KnobTarget(c.State, k, ids, _focused, IsReturn) is { } kt && ToMidiTarget(kt) is { } mt && mt.Equals(t))
                        list.Add(new HardwareBinding(c, KnobLabel(k), false, k));
            }
            else if (generic && RoleFor(t) is { } role)
                foreach (var d in p.Controls.Where(d => d.Role.Kind == role && Pin(c, d) is null))
                    list.Add(new HardwareBinding(c, ControlLabel(d), false));
            else if (!generic && RoleFor(t) is { } surfaceRole)
                list.Add(new HardwareBinding(c, RoleLabel(new ControlRole(surfaceRole)), false));   // a Mackie / HUI button
        }
        return list;
    }

    /// <summary>The controls a target can be pinned to (FR-033): every connected generic profile's
    /// absolute CC / note controls but pads — relative encoders and Mackie / HUI strips aren't offered.</summary>
    public List<(ConnectedController Controller, ControlDecl Control, string Label)> PinnableControls()
    {
        var list = new List<(ConnectedController, ControlDecl, string)>();
        foreach (var c in _controllers.Values.Where(c => c.Stage == ControllerStage.Active && c.Profile is { Protocol: ControllerProtocol.Generic }))
            foreach (var d in c.Profile!.Controls)
                if (d.Role.Kind != ControlRoleKind.Pad && d.Encoder == EncoderMode.Absolute && d.Message is ControlMessage.Cc or ControlMessage.Note)
                    list.Add((c, d, ControlLabel(d)));
        return list;
    }

    /// <summary>Pin <paramref name="control"/> of <paramref name="c"/> to <paramref name="t"/>.</summary>
    public void PinControl(ConnectedController c, ControlDecl control, MidiTarget t)
        => _overrides?.Pin(c.Profile!.Id, ControllerOverrides.ControlKey(control), ControlLabel(control), Store(t));

    private ControlPin? Pin(ConnectedController c, ControlDecl d)
        => c.Profile is { } p ? _overrides?.PinOf(p.Id, ControllerOverrides.ControlKey(d)) : null;

    private static MidiTarget? RoleTarget(ControlRoleKind k) => k switch
    {
        ControlRoleKind.Play => MidiTarget.TransportPlay,
        ControlRoleKind.Stop => MidiTarget.TransportStop,
        ControlRoleKind.Record => MidiTarget.TransportRecord,
        ControlRoleKind.Loop => MidiTarget.TransportLoop,
        ControlRoleKind.Rewind => MidiTarget.TransportRewind,
        ControlRoleKind.Forward => MidiTarget.TransportForward,
        ControlRoleKind.MasterFader => MidiTarget.MasterVolume,
        _ => null,
    };

    private static ControlRoleKind? RoleFor(MidiTarget t) => t.Kind switch
    {
        MidiTargetKind.TransportPlay => ControlRoleKind.Play,
        MidiTargetKind.TransportStop => ControlRoleKind.Stop,
        MidiTargetKind.TransportRecord => ControlRoleKind.Record,
        MidiTargetKind.TransportLoop => ControlRoleKind.Loop,
        MidiTargetKind.TransportRewind => ControlRoleKind.Rewind,
        MidiTargetKind.TransportForward => ControlRoleKind.Forward,
        MidiTargetKind.MasterVolume => ControlRoleKind.MasterFader,
        _ => null,
    };

    private static Dictionary<string, string>? _commandTitles;
    private static string CommandTitle(string id)
    {
        _commandTitles ??= Nota.Application.Palette.CommandCatalog.Create().ToDictionary(c => c.Id, c => c.Title, StringComparer.Ordinal);
        return _commandTitles.TryGetValue(id, out var t) ? t : Loc.F("controllers.control.command", id);
    }

    public static string KnobLabel(int k) => Loc.F("controllers.control.knob", k + 1);

    /// <summary>A declared control's name in menus: "Knob 4", "Fader 2", "Loop", "Undo".</summary>
    public static string ControlLabel(ControlDecl d) => RoleLabel(d.Role);

    private static string RoleLabel(ControlRole r) => r.LeftKind is { } leftKind && r.Kind != ControlRoleKind.Command
        ? Loc.F("controllers.control.two_way", RoleLabel(new ControlRole(leftKind)), RoleLabel(new ControlRole(r.Kind)))
        : r.Kind switch
    {
        ControlRoleKind.Knob => KnobLabel(r.Index),
        ControlRoleKind.Fader => Loc.F("controllers.control.fader", r.Index + 1),
        ControlRoleKind.Button => Loc.F("controllers.control.button", r.Index + 1),
        ControlRoleKind.Pad => Loc.F("controllers.control.pad", r.Index + 1),
        ControlRoleKind.Command when r.Command is { } id && r.CommandLeft is { } left => Loc.F("controllers.control.two_way", CommandTitle(left), CommandTitle(id)),
        ControlRoleKind.Command when r.Command is { } id => CommandTitle(id),
        ControlRoleKind.MasterFader => Loc.T("controllers.control.master_fader"),
        ControlRoleKind.Play => Loc.T("controllers.control.play"),
        ControlRoleKind.Stop => Loc.T("controllers.control.stop"),
        ControlRoleKind.Record => Loc.T("controllers.control.record"),
        ControlRoleKind.Loop => Loc.T("controllers.control.loop"),
        ControlRoleKind.Rewind => Loc.T("controllers.control.rewind"),
        ControlRoleKind.Forward => Loc.T("controllers.control.forward"),
        ControlRoleKind.PageNext => Loc.T("controllers.control.page_next"),
        ControlRoleKind.PagePrev => Loc.T("controllers.control.page_prev"),
        ControlRoleKind.BankNext => Loc.T("controllers.control.bank_next"),
        ControlRoleKind.BankPrev => Loc.T("controllers.control.bank_prev"),
        ControlRoleKind.TrackNext => Loc.T("controllers.control.track_next"),
        ControlRoleKind.TrackPrev => Loc.T("controllers.control.track_prev"),
        ControlRoleKind.ModeDevice => Loc.T("controllers.control.mode_device"),
        ControlRoleKind.ModeVolume => Loc.T("controllers.control.mode_volume"),
        ControlRoleKind.ModePan => Loc.T("controllers.control.mode_pan"),
        ControlRoleKind.ModeSend => Loc.T("controllers.control.mode_send"),
        _ => Loc.T("controllers.control.jog"),
    };

    /// <summary>The device the knobs follow, or null.</summary>
    public FocusedDevice? Focused => _focused;

    /// <summary>A track was selected (Arrangement, Mixer, a Select button).</summary>
    public void SetSelectedTrack(int trackId)
    {
        if (trackId == _selectedTrack) return;
        _selectedTrack = trackId;
        RefreshFocus();
    }

    /// <summary>The Devices panel shows <paramref name="trackId"/> with <paramref name="selection"/>
    /// selected (null: nothing). It wins for its track; called after every panel rebuild.</summary>
    public void SetPanelFocus(int trackId, DeviceAddress? selection)
    {
        _panelTrack = trackId;
        _panelSelection = selection;
        if (trackId > 0) _selectedTrack = trackId;
        RefreshFocus();
    }

    /// <summary>Resolve the focus again (pages edited, devices changed).</summary>
    public void RefreshFocus(bool force = false)
    {
        int track = _selectedTrack;
        var address = track == _panelTrack && _panelSelection is { } sel ? sel : FocusedDevice.DefaultFor(_engine, track);
        var f = address is null ? null : FocusedDevice.Build(_engine, address, EditedPages);
        if (f is null && address is not null && !ReferenceEquals(address, _panelSelection)) address = null;
        if (f is null && _panelSelection is not null && track == _panelTrack)
        {
            // The panel's selection went (deleted device): the rule picks again.
            address = FocusedDevice.DefaultFor(_engine, track);
            f = address is null ? null : FocusedDevice.Build(_engine, address, EditedPages);
        }
        string sig = Signature(f);
        bool moved = sig != _focusSignature;
        _focused = f;
        _focusSignature = sig;
        if (moved)
            foreach (var c in _controllers.Values) c.State.FocusMoved();
        if (moved || force) FocusChanged?.Invoke();
    }

    private string Signature(FocusedDevice? f) => f is null ? "" : $"{f.Address}|{f.TypeKey}|{f.PageCount}|{FocusIdentity(f.Address)}";

    // Which device sits at the address: a replaced instrument or effect keeps the address but is another device.
    private long FocusIdentity(DeviceAddress a) => a.Rack is not null ? 0 : a.Section switch
    {
        FocusSection.Instrument => _engine.TrackInstrumentId(a.TrackId),
        FocusSection.Audio => _engine.TrackDeviceId(a.TrackId, a.Index),
        _ => _engine.MidiEffectKind(a.TrackId, a.Index),
    };

    /// <summary>The knobs' state for the card markers: the first active controller with knobs in
    /// Device mode — its page and the eight targets (null when no controller follows a device).</summary>
    public (ConnectedController Controller, int Page, IReadOnlyList<(MidiTarget? Target, string Name)> Knobs)? KnobFocus()
    {
        if (_focused is null) return null;
        foreach (var c in _controllers.Values)
        {
            if (c.Stage != ControllerStage.Active || !c.HasKnobs || c.State.KnobMode.Kind != KnobModeKind.Device) continue;
            int page = Math.Clamp(c.State.Page, 0, Math.Max(0, _focused.PageCount - 1));
            var knobs = _focused.Knobs(page).Select(k => (k.Target is { } t ? ToMidiTarget(t) : null, k.Name)).ToList();
            return (c, page, knobs);
        }
        return null;
    }

    /// <summary>The lead controller (the first active one with knobs, as the indicator shows it):
    /// what its knobs are on (specs/107). Null when no active controller has knobs.</summary>
    public ControllerFocusSummary? FocusSummary()
    {
        var active = _controllers.Values.Where(c => c.Stage == ControllerStage.Active).ToList();
        var lead = active.FirstOrDefault(c => c.HasKnobs);
        if (lead is null) return null;
        string name = active.Count == 1 ? lead.Profile?.Name ?? lead.Name : Loc.Plural("controllers.count", active.Count);
        var mode = lead.State.KnobMode;
        if (mode.Kind != KnobModeKind.Device)
        {
            int count = TrackIds().Count;
            int first = Math.Min(lead.State.BankOrigin, Math.Max(0, count - 1));
            return new ControllerFocusSummary(name, mode, 0, "", null, null, 0, 0, "",
                count == 0 ? 0 : first + 1, Math.Min(first + ControllerState.BankSize, count));
        }
        var f = _focused;
        int track = f?.Address.TrackId ?? _selectedTrack;
        string trackName = track > 0 ? _engine.GetTrackName(track) : "";
        if (f is null) return new ControllerFocusSummary(name, mode, Math.Max(0, track), trackName, null, null, 0, 0, "", 0, 0);
        int page = Math.Clamp(lead.State.Page, 0, Math.Max(0, f.PageCount - 1));
        return new ControllerFocusSummary(name, mode, track, trackName, f.DeviceName, f.Address,
            page, Math.Max(1, f.PageCount), f.PageName(page), 0, 0);
    }

    /// <summary>Controls that have not picked up yet: their target and physical position (0..1).</summary>
    public IReadOnlyDictionary<MidiTarget, double> PickupGhosts => _ghosts;
    private readonly Dictionary<MidiTarget, double> _ghosts = new();

    /// <summary>Knob mode from Nota (the controller indicator): every active controller with knobs.</summary>
    public void SetKnobMode(KnobMode mode)
    {
        bool changed = false;
        foreach (var c in _controllers.Values.Where(c => c.Stage == ControllerStage.Active && c.HasKnobs))
            changed |= c.State.SetMode(mode);
        if (changed) { FocusChanged?.Invoke(); ControllersChanged?.Invoke(); }
    }

    /// <summary>MIDI Learn shares the controls: its mappings win, and roles hold still while it listens.</summary>
    public void AttachLearn(MidiLearnService learn)
    {
        _learn = learn;
        learn.MappingsChanged += () => _claimsDirty = true;
        learn.PendingChanged += () => _engine.SetMidiLearnListening(learn.Pending is not null);
    }

    // ---- profiles ---------------------------------------------------------------------------

    /// <summary>Re-read shipped and user profiles (startup, "Reload profiles") and re-match.</summary>
    public void ReloadProfiles()
    {
        _profileWarnings.Clear();
        var list = new List<ControllerProfile>();
        var asm = typeof(ControllerService).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("Nota.Controllers.", StringComparison.Ordinal)))
        {
            using var s = asm.GetManifestResourceStream(res);
            if (s is null) continue;
            using var r = new StreamReader(s);
            Add(r.ReadToEnd(), res["Nota.Controllers.".Length..], ProfileOrigin.Shipped);
        }
        try
        {
            if (Directory.Exists(_userDir))
                foreach (var f in Directory.GetFiles(_userDir, "*.json").Order(StringComparer.Ordinal))
                {
                    if (Path.GetFileName(f) is var fn && (fn.Equals("pages.json", StringComparison.OrdinalIgnoreCase)
                        || fn.Equals("overrides.json", StringComparison.OrdinalIgnoreCase))) continue;   // device pages / the user's rebinds, not profiles
                    try { Add(File.ReadAllText(f), Path.GetFileName(f), ProfileOrigin.User); }
                    catch (IOException ex) { _profileWarnings.Add(Loc.F("controllers.file_cant_be_read", Path.GetFileName(f), ex.Message)); }
                }
        }
        catch (IOException ex) { _profileWarnings.Add(Loc.F("controllers.folder_cant_be_read", ex.Message)); }

        // A user profile with a shipped profile's id replaces it.
        var userIds = list.Where(p => p.Origin == ProfileOrigin.User).Select(p => p.Id).ToHashSet();
        _profiles = list.Where(p => p.Origin == ProfileOrigin.User || !userIds.Contains(p.Id)).ToList();
        foreach (var w in _profileWarnings) _log?.Info($"Controller profiles: {w}");

        foreach (var c in _controllers.Values.Where(c => c.Stage is ControllerStage.Matched or ControllerStage.Unmatched or ControllerStage.Active))
            Match(c);
        ControllersChanged?.Invoke();

        void Add(string json, string file, ProfileOrigin origin)
        {
            var (p, warnings) = ProfileLoader.Load(json, file, origin);
            _profileWarnings.AddRange(warnings);
            if (p is not null) list.Add(p);
        }
    }

    // ---- user choices (Preferences, MCP) -------------------------------------------------------

    public ControllerChoice ChoiceFor(string uid)
        => _settings.Current.Controllers.TryGetValue(uid, out var c) ? c : new ControllerChoice();

    /// <summary>Profile override (null = automatic, "none" = plain MIDI) and on / off for a port.</summary>
    public void SetChoice(string uid, string? profileId, bool enabled, bool? pickup = null)
    {
        var choice = ChoiceFor(uid);
        choice.ProfileId = profileId;
        choice.Enabled = enabled;
        if (pickup is { } pk) choice.PickupOn = pk;
        _settings.Current.Controllers[uid] = choice;
        _settings.Save();
        if (_controllers.TryGetValue(uid, out var c) && c.Stage != ControllerStage.Gone && c.Stage != ControllerStage.Identifying)
        {
            Match(c);
            if (c.Profile is { } p) c.State.PickupOn = choice.PickupOn && p.Pickup;   // live, for an active controller too
            ControllersChanged?.Invoke();
        }
    }

    /// <summary>A project opened: every controller back to its first bank and page, knobs on the device.</summary>
    public void ResetSessionState()
    {
        foreach (var c in _controllers.Values)
        {
            c.State.Reset();
            c.Leds.Clear();
            c.Values.Clear();
            c.Pickups.Clear();
            c.Surface?.Refresh();
        }
        _selectedTrack = _panelTrack = -1;
        _panelSelection = null;
        RefreshFocus(force: true);
        BankChanged?.Invoke();
    }

    /// <summary>Nota is closing: hand every active controller back (its release messages).</summary>
    public void ReleaseAll()
    {
        foreach (var c in _controllers.Values.Where(c => c.Stage == ControllerStage.Active)) Release(c);
        _engine.SetMidiClaims(ReadOnlySpan<MidiClaim>.Empty);
    }

    /// <summary>Look at the port list now rather than on the next periodic scan.</summary>
    public void RescanNow()
    {
        if (!_started) return;   // the first tick scans anyway
        Rescan(Environment.TickCount64);
    }

    // ---- tick -----------------------------------------------------------------------------

    /// <summary>UI tick: hot-plug, identity replies, then the controllers' messages. Call it
    /// before MIDI Learn's tick, so a message that binds a learn target never also fires its role.</summary>
    public void Tick()
    {
        long now = Environment.TickCount64;
        if (!_started)
        {
            _started = true;
            ReloadProfiles();
            _generation = _engine.MidiPortsGeneration;
            Rescan(now);
        }
        else
        {
            uint gen = _engine.MidiPortsGeneration;
            if (gen != _generation)
            {
                _generation = gen;
                _engine.ApplyMidi();   // connect what was plugged in (and let go of what left)
                Rescan(now);
            }
            else if (now - _lastScanMs >= ScanEveryMs) Rescan(now);
        }

        bool changed = false;
        int n;
        while ((n = _engine.PollMidiSysex(out int source, _sysex)) != 0)
        {
            if (n < 0) continue;
            if (IdentityReply.TryParse(_sysex.AsSpan(0, n)) is not { } reply) continue;
            var c = _controllers.Values.FirstOrDefault(x => x.Source == source && x.Stage == ControllerStage.Identifying);
            if (c is null) continue;
            c.Identity = reply;
            Match(c);
            changed = true;
        }
        foreach (var c in _controllers.Values.Where(c => c.Stage == ControllerStage.Identifying && now >= c.IdentifyUntilMs))
        {
            Match(c);   // no reply: the port name decides
            changed = true;
        }
        if (changed) ControllersChanged?.Invoke();

        if (_claimsDirty) PublishClaims();
        DrainSurface();
        SendLeds();
        TickSurfaces();
        // A device removed or replaced without the panel noticing (MCP, undo with the panel hidden).
        if (now - _focusCheckMs >= 500)
        {
            _focusCheckMs = now;
            if (_selectedTrack > 0 || _focused is not null) RefreshFocus();
        }
        // The readout and Remote (specs/107): a rename, a bank or page move, a mode — compared as a
        // key a few times a second, and at once after this tick's controller messages.
        if (_summaryDirty || now - _summaryCheckMs >= 150)
        {
            _summaryDirty = false;
            _summaryCheckMs = now;
            var sum = FocusSummary();
            string key = sum?.Key ?? "";
            if (key != _summaryKey) { _summaryKey = key; _summary = sum; FocusSummaryChanged?.Invoke(); }
        }
    }

    private long _summaryCheckMs;
    private bool _summaryDirty = true;
    private string? _summaryKey;
    private ControllerFocusSummary? _summary;

    /// <summary>The last <see cref="FocusSummary"/> the readout was told about.</summary>
    public ControllerFocusSummary? CurrentSummary => _summary;

    /// <summary>What <see cref="FocusSummary"/> returns changed (track, device, page, mode, bank, a name).</summary>
    public event Action? FocusSummaryChanged;

    /// <summary>Re-read the summary on the next tick (after a change Nota made itself).</summary>
    public void InvalidateSummary() => _summaryDirty = true;

    private void Rescan(long now)
    {
        _lastScanMs = now;
        var ports = _engine.MidiPorts();
        var present = new HashSet<string>();
        bool changed = false;
        foreach (var p in ports)
        {
            present.Add(p.Uid);
            if (_controllers.TryGetValue(p.Uid, out var c) && c.Stage != ControllerStage.Gone)
            {
                if (c.Source != p.Source) { c.Source = p.Source; _claimsDirty = true; }   // reconnecting renumbers sources
                continue;
            }
            changed = true;
            c ??= new ConnectedController { Uid = p.Uid };
            c.Name = p.Name;
            c.Source = p.Source;
            c.OutputUid = p.OutputUid;
            c.Identity = null;
            c.Profile = null;
            c.Leds.Clear();
            c.Values.Clear();
            _controllers[p.Uid] = c;
            if (p.OutputUid.Length > 0 && _engine.MidiSend(p.OutputUid, IdentityReply.Request))
            {
                c.Stage = ControllerStage.Identifying;
                c.IdentifyUntilMs = now + IdentityTimeoutMs;
            }
            else Match(c);
        }
        bool bankGone = false;
        foreach (var c in _controllers.Values.Where(c => !present.Contains(c.Uid) && c.Stage != ControllerStage.Gone))
        {
            bankGone |= c.Stage == ControllerStage.Active;
            c.Surface?.EndTouches();   // unplugged mid-touch: no write stays open
            c.Surface = null;
            c.Stage = ControllerStage.Gone;
            _claimsDirty = true;
            _log?.Info($"Controller gone: {c.Name}");
            changed = true;
        }
        if (changed) ControllersChanged?.Invoke();
        if (bankGone) BankChanged?.Invoke();   // its bank's track marks go
    }

    // Matched → Active when enabled; the choice in Preferences can force a profile or none.
    private void Match(ConnectedController c)
    {
        bool wasActive = c.Stage == ControllerStage.Active;
        var choice = ChoiceFor(c.Uid);
        var profile = choice.ProfileId switch
        {
            null => ProfileMatcher.Match(_profiles, c.Name, c.Identity),
            "none" => null,
            // Any port can be driven as a Mackie Control or HUI surface (FR-025).
            ForcedMcu => _profiles.FirstOrDefault(p => p.Id == GenericMcuId) ?? Forced(ControllerProtocol.Mcu),
            ForcedHui => Forced(ControllerProtocol.Hui),
            var id => _profiles.FirstOrDefault(p => p.Id == id),
        };
        if (wasActive && (profile != c.Profile || !choice.Enabled)) Release(c);
        c.Profile = profile;
        if (profile is null)
        {
            c.Stage = ControllerStage.Unmatched;
            _log?.Info($"Controller {c.Name}: no profile (plain MIDI)");
        }
        else if (!choice.Enabled)
        {
            c.Stage = ControllerStage.Matched;
            _log?.Info($"Controller {c.Name}: profile {profile.Id}, switched off");
        }
        else if (!wasActive || c.Stage != ControllerStage.Active)
        {
            c.Stage = ControllerStage.Active;
            c.State.PickupOn = choice.PickupOn && profile.Pickup;
            c.Leds.Clear();
            c.Values.Clear();
            foreach (var m in profile.Init) _engine.MidiSend(c.OutputUid, m);
            StartSurface(c);
            _log?.Info($"Controller {c.Name}: profile {profile.Id}{(c.Identity is null ? " (port name)" : " (identity)")}");
            Connected?.Invoke(c);
        }
        _claimsDirty = true;
        BankChanged?.Invoke();
    }

    /// <summary>Profile choices that force a protocol on any port (Preferences, MCP).</summary>
    public const string ForcedMcu = "mcu", ForcedHui = "hui";
    public const string GenericMcuId = "generic.mackie-control";

    private static readonly Dictionary<ControllerProtocol, ControllerProfile> _forced = new();
    private static ControllerProfile Forced(ControllerProtocol protocol)
    {
        if (!_forced.TryGetValue(protocol, out var p))
            _forced[protocol] = p = new ControllerProfile(protocol == ControllerProtocol.Hui ? ForcedHui : ForcedMcu,
                protocol == ControllerProtocol.Hui ? Loc.T("controllers.protocol.hui") : Loc.T("controllers.protocol.mcu"), protocol, null,
                Array.Empty<System.Text.RegularExpressions.Regex>(), null, Array.Empty<byte[]>(), Array.Empty<byte[]>(),
                false, true, Array.Empty<ControlDecl>(), ProfileOrigin.Shipped, "");
        return p;
    }

    private void StartSurface(ConnectedController c)
    {
        c.Surface = c.Profile?.Protocol switch
        {
            ControllerProtocol.Mcu => new ProtocolSurface(c, new McuCodec(), _engine, _applier) { Unbound = Unbound, SessionShown = () => SessionShown?.Invoke() == true, RunCommand = id => RunCommand?.Invoke(id) == true },
            ControllerProtocol.Hui => new ProtocolSurface(c, new HuiCodec(), _engine, _applier) { Unbound = Unbound, SessionShown = () => SessionShown?.Invoke() == true, RunCommand = id => RunCommand?.Invoke(id) == true },
            _ => null,
        };
        c.Surface?.Connect();
    }

    private void Release(ConnectedController c)
    {
        c.Surface?.Release();   // strips blank, faders down, lights off (FR-004)
        c.Surface = null;
        if (c.Profile is { } p && c.OutputUid.Length > 0)
            foreach (var m in p.Release) _engine.MidiSend(c.OutputUid, m);
        c.Stage = ControllerStage.Matched;
        _claimsDirty = true;
    }

    // ---- the setup wizard's capture (FR-030) ------------------------------------------------

    private string? _captureUid;
    private readonly List<(int Status, int Channel, int D1, int D2)> _captured = new();

    /// <summary>Claim a port whole for the setup wizard: what it sends is collected, not played
    /// and not routed. Null ends the capture.</summary>
    public void Capture(string? uid)
    {
        _captureUid = uid;
        _captured.Clear();
        _claimsDirty = true;
    }

    /// <summary>Take what the captured port sent since the last call.</summary>
    public List<(int Status, int Channel, int D1, int D2)> TakeCaptured()
    {
        var list = new List<(int, int, int, int)>(_captured);
        _captured.Clear();
        return list;
    }

    /// <summary>Write a profile the wizard made into the user folder, and use it.</summary>
    public string SaveUserProfile(string fileName, string json)
    {
        Directory.CreateDirectory(_userDir);
        string path = Path.Combine(_userDir, fileName);
        File.WriteAllText(path, json);
        ReloadProfiles();
        return path;
    }

    // ---- the mapping editor (specs/110) ---------------------------------------------------------

    private const string ShippedPrefix = "Nota.Controllers.";

    /// <summary>A profile's file as written — the embedded resource for a shipped one, the file for
    /// the user's — so the editor can change its controls and keep everything else; null when gone.</summary>
    public string? ProfileJson(ControllerProfile p)
    {
        try
        {
            if (p.Origin == ProfileOrigin.User)
            {
                string path = Path.Combine(_userDir, p.FileName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            using var s = typeof(ControllerService).Assembly.GetManifestResourceStream(ShippedPrefix + p.FileName);
            if (s is null) return null;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch (IOException) { return null; }
    }

    /// <summary>The file name of the shipped profile with this id, or null — a user profile with
    /// that id is the user's copy of a shipped one.</summary>
    public string? ShippedFileName(string id)
    {
        var asm = typeof(ControllerService).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith(ShippedPrefix, StringComparison.Ordinal)))
        {
            using var s = asm.GetManifestResourceStream(res);
            if (s is null) continue;
            using var r = new StreamReader(s);
            var (p, _) = ProfileLoader.Load(r.ReadToEnd(), res[ShippedPrefix.Length..], ProfileOrigin.Shipped);
            if (p?.Id == id) return res[ShippedPrefix.Length..];
        }
        return null;
    }

    /// <summary>The profile in use for this id (after a save or a reload).</summary>
    public ControllerProfile? ProfileById(string id) => _profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>Remove a user profile (Back to shipped profile) and re-match.</summary>
    public void RemoveUserProfile(string fileName)
    {
        try { File.Delete(Path.Combine(_userDir, fileName)); }
        catch (IOException ex) { _log?.Info($"Controller profiles: {fileName} can't be removed: {ex.Message}"); }
        ReloadProfiles();
    }

    // One table for every active controller, without the controls MIDI Learn owns.
    private void PublishClaims()
    {
        _claimsDirty = false;
        var learned = new HashSet<(bool, int, int)>();
        if (_learn is not null)
            foreach (var m in _learn.Mappings)
                if (m.SourceKind is MidiSourceKind.Cc or MidiSourceKind.Note)
                    learned.Add((m.SourceKind == MidiSourceKind.Note, m.Channel, m.Number));
        var claims = new List<MidiClaim>();
        foreach (var c in _controllers.Values.Where(c => c.Stage == ControllerStage.Active && c.Profile is not null && c.Uid != _captureUid))
        {
            var own = RoleRouter.BuildClaims(c.Profile!, c.Source, learned);
            if (_overrides is { } o && c.Profile!.Protocol == ControllerProtocol.Generic && !c.Profile.ClaimAll)
                foreach (var d in c.Profile.Controls)
                {
                    var msgs = ControlDecoder.Messages(d).ToList();
                    bool learnedHere = msgs.Any(m => learned.Contains((m.status == 0x80, d.Channel, m.number)));
                    if (learnedHere) continue;   // MIDI Learn owns it
                    if (Pin(c, d) is not null)
                    {
                        // A pinned button or pad is claimed now (its role left it free).
                        foreach (var (st, num) in msgs)
                            if (!own.Any(x => x.Status == st && x.Channel == d.Channel && x.Data1 == num))
                                own.Add(new MidiClaim(c.Source, 0, st, d.Channel, num));
                    }
                    else if (RoleTarget(d.Role.Kind) is { } rt && o.IsUnbound(Store(rt)))
                        // A transport / master role the user unbound lets go of its control (FR-032).
                        own.RemoveAll(x => msgs.Any(m => m.status == x.Status && m.number == x.Data1) && x.Channel == d.Channel);
                }
            claims.AddRange(own);
        }
        if (_captureUid is not null && _controllers.TryGetValue(_captureUid, out var cap) && cap.Stage != ControllerStage.Gone)
            claims.Add(MidiClaim.Whole(cap.Source));
        if (!_engine.SetMidiClaims(claims.ToArray())) _log?.Warn("Controller claims refused by the engine");
    }

    private void DrainSurface()
    {
        int n = _engine.PollMidiSurfaceEvents(_events);
        if (n == 0) return;
        bool learning = _learn?.Pending is not null;
        bool surfaceMoved = false;
        List<int>? trackIds = null;
        for (int i = 0; i < n; i++)
        {
            int source = _events[i * 5], status = _events[i * 5 + 1], channel = _events[i * 5 + 2], d1 = _events[i * 5 + 3], d2 = _events[i * 5 + 4];
            if (_captureUid is not null && _controllers.TryGetValue(_captureUid, out var cap) && cap.Source == source)
            {
                if (_captured.Count < 4096) _captured.Add((status, channel, d1, d2));
                continue;
            }
            if (learning) continue;   // MIDI Learn got its copy; the role holds still
            var c = _controllers.Values.FirstOrDefault(x => x.Source == source && x.Stage == ControllerStage.Active);
            if (c?.Profile is not { } profile) continue;

            // A learned control on a port the profile owns whole: MIDI Learn takes it.
            if (_learn is not null && (status == 0xB0 || status == 0x90)
                && _learn.DispatchFromController(status == 0x90, channel, d1, d2)) continue;

            if (c.Surface is { } surface)
            {
                trackIds ??= TrackIds();
                if (surface.Codec.Decode(status, channel, d1, d2) is { } input
                    && surface.Input(input, trackIds, _focused, _selectedTrack, IsReturn))
                    BankChanged?.Invoke();
                surfaceMoved = true;
                continue;
            }

            _decoder.Observe(source, status, channel, d1, d2);
            if (RoleRouter.Find(profile, status, channel, d1, _decoder.NrpnParam(source, channel)) is not { } decl)
            {
                // A claimed message no control declares (a whole-port profile in another mode, a
                // control the profile misses): log the first few, so a silent controller is diagnosable.
                if (c.UnknownLogged < 32)
                {
                    c.UnknownLogged++;
                    _log?.Info($"Controller {c.Name}: no role for {status:X2} ch{channel + 1} {d1} {d2}");
                }
                continue;
            }
            if (_decoder.Decode(decl, source, status, channel, d1, d2) is not { } value) continue;
            if (Pin(c, decl) is { } pin)
            {
                // A pinned control drives its target, whatever the bank, instead of its role.
                var pt = FromStored(pin.Target);
                if (pt.IsButton)
                {
                    bool down = value.IsRelative ? value.Steps != 0 : decl.Message == ControlMessage.Note ? value.Position > 0 : value.Position >= 0.5;
                    bool was = c.PinDown.TryGetValue(decl, out var w) && w;
                    c.PinDown[decl] = !value.IsRelative && down;
                    if (down && (value.IsRelative || !was)) _applier.Trigger(pt);
                }
                else if (value.IsRelative) _applier.Nudge(pt, value.Steps * RoleRouter.StepFraction);
                else Apply(c, decl, pt, value.Position);
                continue;
            }
            trackIds ??= TrackIds();
            foreach (var a in c.Router.Route(decl, value, c.State, trackIds)) Execute(c, decl, a, trackIds);
        }
        if (surfaceMoved) UpdateGhosts();
    }

    private void Execute(ConnectedController c, ControlDecl decl, ControllerAction a, List<int> trackIds)
    {
        switch (a.Kind)
        {
            case ControllerActionKind.Trigger when ToMidiTarget(a.Target) is { } t && !Unbound(t):
                _applier.Trigger(t);
                break;
            case ControllerActionKind.SetNormalized when ToMidiTarget(a.Target) is { } t && !Unbound(t):
                Apply(c, decl, t, a.Value);
                break;
            case ControllerActionKind.Nudge when ToMidiTarget(a.Target) is { } t && !Unbound(t):
                _applier.Nudge(t, a.Value);
                break;
            case ControllerActionKind.BankStep:
                if (c.State.BankStep(a.Index, trackIds.Count)) BankChanged?.Invoke();
                break;
            case ControllerActionKind.TrackStep:
                StepTrack(c, a.Index, trackIds);
                break;
            case ControllerActionKind.Knob when RoleRouter.KnobTarget(c.State, a.Index, trackIds, _focused, IsReturn) is { } kt
                                                 && ToMidiTarget(kt) is { } t && !Unbound(t):
                if (a.Control.IsRelative) _applier.Nudge(t, a.Control.Steps * RoleRouter.StepFraction);
                else Apply(c, decl, t, a.Control.Position);
                break;
            case ControllerActionKind.PageStep:
                if (c.State.PageStep(a.Index, _focused?.PageCount ?? 0)) FocusChanged?.Invoke();
                break;
            case ControllerActionKind.KnobMode:
                if (c.State.StepMode(a.Mode, _engine.ReturnTrackCount)) { FocusChanged?.Invoke(); ControllersChanged?.Invoke(); }
                break;
            case ControllerActionKind.Command when a.Command is { } id:
                // A command button (undo, save, metronome …): the same command the menu and the
                // shortcut run; one that isn't available right now (nothing to undo) does nothing.
                for (int i = 0, n = Math.Max(1, a.Index); i < n; i++)
                    if (RunCommand?.Invoke(id) != true)
                    {
                        if (c.UnknownLogged < 32) { c.UnknownLogged++; _log?.Info($"Controller {c.Name}: command {id} not run (unknown or unavailable)"); }
                        break;
                    }
                break;
            case ControllerActionKind.Jog when SessionShown?.Invoke() == true:
                // In the Session the wheel walks the clips of the selected track, a scene per step.
                for (int i = 0, n = Math.Min(Math.Abs(a.Index), 8); i < n; i++)
                    RunCommand?.Invoke(a.Index > 0 ? "session.selectDown" : "session.selectUp");
                break;
            case ControllerActionKind.Jog when !_engine.IsPlaying:
                _engine.Seek(Math.Max(0, _engine.PositionBeats + a.Index * 0.25));   // one 16th per step
                break;
        }
    }

    // Track ◀ / ▶ (specs/107 FR-006–FR-009): select the neighbour in the order the user sees, as
    // a header click does (in the Session its slot in the same row), then move the bank the
    // least amount that keeps it inside.
    private void StepTrack(ConnectedController c, int delta, List<int> trackIds)
    {
        // The order on screen: the Session's own columns while it shows (it folds groups on its
        // own), else the track list with the Arrangement's folds.
        var order = VisibleTrackOrder?.Invoke()
                    ?? ControllerTrackStep.Order(ArrangementView.HierarchyOrder(_engine).Select(r => (r.Id, r.ParentGroupId)),
                                                 id => GroupCollapsed?.Invoke(id) == true);
        if (ControllerTrackStep.Step(order, _selectedTrack, delta) is not { } to) return;
        SelectTrack?.Invoke(to);
        if (c.State.BankFollow(trackIds.IndexOf(to), trackIds.Count)) BankChanged?.Invoke();
    }

    /// <summary>Select a track as a header click would (wired by the main window; specs/107).</summary>
    public Action<int>? SelectTrack { get; set; }
    /// <summary>The tracks Track ◀ / ▶ walk when a view has its own order (the Session's columns),
    /// else null for the track list.</summary>
    public Func<List<int>?>? VisibleTrackOrder { get; set; }
    /// <summary>Whether a group is collapsed in the track list (its members aren't stepped to).</summary>
    public Func<int, bool>? GroupCollapsed { get; set; }
    /// <summary>Next / Previous device (wired by the main window, specs/107): MCP steps through it.</summary>
    public Func<int, bool>? StepDevice { get; set; }
    /// <summary>The Session view's selection — "none" / "slot" / "scene", track, scene (for MCP).</summary>
    public Func<(string Kind, int Track, int Scene)>? SessionSelection { get; set; }

    // An absolute move: through pickup unless the control is motorised or pickup is off (FR-019, FR-021).
    private void Apply(ConnectedController c, ControlDecl decl, MidiTarget t, double position)
    {
        if (!c.State.PickupOn || decl.Motorised)
        {
            _applier.SetNormalized(t, position);
            return;
        }
        if (c.PickupEpoch != c.State.Epoch)
        {
            c.PickupEpoch = c.State.Epoch;
            foreach (var p in c.Pickups.Values) p.Release();
        }
        if (!c.Pickups.TryGetValue(decl, out var pk)) c.Pickups[decl] = pk = new PickupState();
        var current = _applier.CurrentNormalized(t);
        if (current is not { } cur || pk.Accept(t, position, cur))
        {
            _applier.SetNormalized(t, position);
            pk.ReadBack(_applier.CurrentNormalized(t));
        }
        UpdateGhosts();
    }

    private void UpdateGhosts()
    {
        var ghosts = new Dictionary<MidiTarget, double>();
        foreach (var c in _controllers.Values)
        {
            if (c.Stage != ControllerStage.Active) continue;
            bool stale = c.PickupEpoch != c.State.Epoch;
            foreach (var p in c.Pickups.Values)
                if (!stale && !p.Caught && p.Target is MidiTarget t && p.LastPhysical is { } phys) ghosts[t] = phys;
            if (c.Surface is { } surface)
                foreach (var (t, phys) in surface.Ghosts()) ghosts[t] = phys;
        }
        if (ghosts.Count == _ghosts.Count && ghosts.All(g => _ghosts.TryGetValue(g.Key, out var v) && v == g.Value)) return;
        _ghosts.Clear();
        foreach (var g in ghosts) _ghosts[g.Key] = g.Value;
        PickupChanged?.Invoke();
    }

    private bool IsReturn(int trackId) => _engine.TrackReturnIndex(trackId) >= 0;

    /// <summary>A track's position in the project (−1 = none) and back: what the overrides save.</summary>
    public int TrackIndexOf(int trackId)
    {
        if (trackId < 0) return -1;
        for (int i = 0, n = _engine.TrackCount; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return i;
        return -1;
    }

    public int TrackIdAt(int index) => index >= 0 && _engine.TryGetTrackInfo(index, out var ti) ? ti.Id : -1;

    /// <summary>The MIDI Learn target a controller target lands on (same applier, same binding).</summary>
    public static MidiTarget? ToMidiTarget(ControllerTarget t) => t.Kind switch
    {
        ControllerTargetKind.TrackPan => MidiTarget.TrackPan(t.TrackId),
        ControllerTargetKind.TrackSend => MidiTarget.TrackSend(t.TrackId, t.Param),
        ControllerTargetKind.DeviceParam => MidiTarget.DeviceParam(t.TrackId, t.Device, t.Param),
        ControllerTargetKind.PluginParam => MidiTarget.PluginParam(t.TrackId, t.Device, t.Param),
        ControllerTargetKind.MidiDeviceParam => MidiTarget.MidiDeviceParam(t.TrackId, t.Device, t.Param),
        ControllerTargetKind.RackChainDeviceParam => new MidiTarget(MidiTargetKind.RackChainDeviceParam, t.TrackId, t.Device, t.Param, t.Sub),
        ControllerTargetKind.RackChainInstrumentParam => new MidiTarget(MidiTargetKind.RackChainInstrumentParam, t.TrackId, t.Device, t.Param, t.Sub),
        ControllerTargetKind.TransportPlay => MidiTarget.TransportPlay,
        ControllerTargetKind.TransportStop => MidiTarget.TransportStop,
        ControllerTargetKind.TransportRecord => MidiTarget.TransportRecord,
        ControllerTargetKind.TransportLoop => MidiTarget.TransportLoop,
        ControllerTargetKind.TransportRewind => MidiTarget.TransportRewind,
        ControllerTargetKind.TransportForward => MidiTarget.TransportForward,
        ControllerTargetKind.TrackVolume => MidiTarget.TrackVolume(t.TrackId),
        ControllerTargetKind.MasterVolume => MidiTarget.MasterVolume,
        _ => null,
    };

    /// <summary>The track order banks walk: the Mixer's strips — the arrangement's rows (each
    /// group followed by its members, specs/070), then the returns; never the master.</summary>
    public List<int> TrackIds()
    {
        var ids = new List<int>(_engine.TrackCount);
        foreach (var r in ArrangementView.HierarchyOrder(_engine)) ids.Add(r.Id);
        foreach (int i in ArrangementView.DisplayOrder(_engine))
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.IsReturn) ids.Add(ti.Id);
        return ids;
    }

    /// <summary>Track ids under any active controller with faders (for the header marker).</summary>
    public HashSet<int> TracksUnderBanks()
    {
        var set = new HashSet<int>();
        var ids = TrackIds();
        foreach (var c in _controllers.Values.Where(c => c.Stage == ControllerStage.Active && c.HasFaders))
        {
            c.State.Clamp(ids.Count);
            for (int i = c.State.BankOrigin; i < Math.Min(ids.Count, c.State.BankOrigin + ControllerState.BankSize); i++) set.Add(ids[i]);
        }
        return set;
    }

    // MCU / HUI feedback: the bank's strips, master, transport and time, by difference.
    private void TickSurfaces()
    {
        List<int>? ids = null;
        Dictionary<int, NotaTrackInfo>? infos = null;
        foreach (var c in _controllers.Values)
        {
            if (c.Stage != ControllerStage.Active || c.Surface is not { } surface) continue;
            if (ids is null)
            {
                ids = TrackIds();
                infos = new Dictionary<int, NotaTrackInfo>();
                for (int i = 0, n = _engine.TrackCount; i < n; i++)
                    if (_engine.TryGetTrackInfo(i, out var ti)) infos[ti.Id] = ti;
            }
            surface.Tick(ids, infos!, _focused, _selectedTrack, IsReturn, ModeLabel(c.State.KnobMode));
        }
    }

    /// <summary>A knob mode as Nota shows it ("Device", "Pan", "Send A").</summary>
    public static string ModeLabel(KnobMode m) => m.Kind switch
    {
        KnobModeKind.Volume => Loc.T("controllers.mode.volume"),
        KnobModeKind.Pan => Loc.T("controllers.mode.pan"),
        KnobModeKind.Send => Loc.F("controllers.mode.send", (char)('A' + m.Send)),
        _ => Loc.T("controllers.mode.device"),
    };

    // Transport LEDs a profile declares: sent when the state they show changes (and on connect).
    private void SendLeds()
    {
        List<int>? trackIds = null;
        foreach (var c in _controllers.Values)
        {
            if (c.Stage != ControllerStage.Active || c.Profile is null || c.OutputUid.Length == 0) continue;
            foreach (var d in c.Profile.Controls)
            {
                if (d.Feedback is not { } fb) continue;
                if (fb.ValueTemplate is { Length: > 0 } template)
                {
                    // A ring or a motor fader: the value of what it drives now, through the template.
                    trackIds ??= TrackIds();
                    if (ValueTarget(c, d, trackIds) is not { } vt || _applier.CurrentNormalized(vt) is not { } v) continue;
                    int v14 = (int)Math.Round(Math.Clamp(v, 0, 1) * 16383);
                    if (c.Values.TryGetValue(d, out int was) && was == v14) continue;
                    if (ControlFeedback.Fill(template, v14) is { } msg && _engine.MidiSend(c.OutputUid, msg)) c.Values[d] = v14;
                    continue;
                }
                bool? on = d.Role.Kind switch
                {
                    ControlRoleKind.Play => _engine.IsPlaying,
                    ControlRoleKind.Stop => !_engine.IsPlaying,
                    ControlRoleKind.Record => _engine.IsRecording,
                    ControlRoleKind.Loop => _engine.LoopEnabled,
                    _ => null,
                };
                if (on is not { } lit) continue;
                if (c.Leds.TryGetValue(d, out bool sent) && sent == lit) continue;
                // Remembered only once sent: a failed send is tried again on the next tick.
                if ((lit ? fb.On : fb.Off) is not { } bytes || _engine.MidiSend(c.OutputUid, bytes)) c.Leds[d] = lit;
            }
        }
    }

    // What a value-feedback control shows: its pin, the focused device's knob, the bank fader's
    // track, the master.
    private MidiTarget? ValueTarget(ConnectedController c, ControlDecl d, List<int> trackIds)
    {
        // Mapped with MIDI Learn: the control drives (and shows) that mapping, not its role.
        if (_learn is not null && d.Message is ControlMessage.Cc or ControlMessage.Note)
        {
            var kind = d.Message == ControlMessage.Note ? MidiSourceKind.Note : MidiSourceKind.Cc;
            foreach (var m in _learn.Mappings)
                if (m.SourceKind == kind && m.Channel == d.Channel && m.Number == d.Number) return m.Target;
        }
        if (Pin(c, d) is { } pin) return FromStored(pin.Target);
        switch (d.Role.Kind)
        {
            case ControlRoleKind.Knob:
                return RoleRouter.KnobTarget(c.State, d.Role.Index, trackIds, _focused, IsReturn) is { } kt ? ToMidiTarget(kt) : null;
            case ControlRoleKind.Fader:
            {
                int i = c.State.BankOrigin + d.Role.Index;
                return d.Role.Index < ControllerState.BankSize && i >= 0 && i < trackIds.Count ? MidiTarget.TrackVolume(trackIds[i]) : null;
            }
            case ControlRoleKind.MasterFader: return MidiTarget.MasterVolume;
        }
        return null;
    }
}
