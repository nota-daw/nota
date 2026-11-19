// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Mackie Control or HUI surface driven by Nota (specs/051 US4 / US6). The port is claimed
// whole; what it sends is decoded by the protocol's codec into strip / transport events, and
// what Nota shows goes back as feedback each UI tick, by difference (FR-022, FR-023):
//
//   strips    the bank's eight tracks — fader = volume, V-pot = what the knobs do (Device:
//             the focused device's page, Volume, Pan or Send), Rec / Solo / Mute / Select,
//             the track name on the scribble strip, its peak meter
//   master    the master fader = master volume
//   transport play, stop, record, cycle (loop), rewind / forward a bar, jog = the playhead
//   bank      bank ← → (eight tracks), channel ← → (one)
//
// A touched motor fader is a movement of its own (FR-024): automation writes and the undo step
// last exactly as long as the touch, and feedback leaves that fader alone. Faders that are not
// motorised (a keyboard's MCU port) go through pickup like any absolute control (FR-019).

using System;
using System.Collections.Generic;
using Nota.Application;
using Nota.Application.Controllers;
using Nota.Application.Controllers.Mcu;

namespace Nota.App.Controllers;

internal sealed class ProtocolSurface
{
    private readonly ConnectedController _c;
    private readonly ISurfaceCodec _codec;
    private readonly IAudioEngine _engine;
    private readonly ControlApplier _applier;
    private readonly FeedbackDiffer _differ;
    private readonly bool[] _touched = new bool[9];
    private readonly MidiTarget[] _touchTarget = new MidiTarget[9];
    private readonly PickupState[] _pickup = new PickupState[9];
    private int _pickupEpoch = -1;
    private readonly SurfaceState _want = new();

    /// <summary>A target the user unbound (specs/051 Story 7): the surface leaves it alone.</summary>
    public Func<MidiTarget, bool>? Unbound { get; set; }

    /// <summary>The Session view shows (Story 8): the jog wheel walks its clips and the channel
    /// buttons its tracks, as on a generic controller. Null = never.</summary>
    public Func<bool>? SessionShown { get; set; }
    /// <summary>Runs a palette command (the Session walk); null = none.</summary>
    public Func<string, bool>? RunCommand { get; set; }

    public ProtocolSurface(ConnectedController c, ISurfaceCodec codec, IAudioEngine engine, ControlApplier applier)
    {
        _c = c;
        _codec = codec;
        _engine = engine;
        _applier = applier;
        _differ = new FeedbackDiffer(codec);
        for (int i = 0; i < _pickup.Length; i++) _pickup[i] = new PickupState();
    }

    public ISurfaceCodec Codec => _codec;

    // HUI keeps a connection by ping (US6): once a second; no answer for 3 s = gone, and the next
    // answer gets a full refresh (the surface may have been switched off and on).
    private long _lastPingMs, _lastAliveMs;
    private bool _alive = true;

    /// <summary>Send everything on the next tick (connect, bank change, project load).</summary>
    public void Refresh() => _differ.Invalidate();

    /// <summary>Hello: the protocol's device query, then a full refresh.</summary>
    public void Connect()
    {
        if (_codec is McuCodec mcu) Send(mcu.DeviceQuery);
        Refresh();
    }

    /// <summary>Goodbye: everything dark (FR-004), touches ended.</summary>
    public void Release()
    {
        foreach (var m in _codec.Blank()) Send(m);
        EndTouches();
        Refresh();
    }

    /// <summary>Every held fader let go — also when the surface was unplugged mid-touch, so no
    /// automation write stays open.</summary>
    public void EndTouches()
    {
        for (int s = 0; s < _touched.Length; s++)
            if (_touched[s]) { _touched[s] = false; _applier.TouchEnd(_touchTarget[s]); }
    }

    // ---- input ------------------------------------------------------------------------------

    /// <summary>A decoded event. <paramref name="trackIds"/> is the bank's track order;
    /// <paramref name="focused"/> what Device-mode V-pots follow. Returns true when the bank moved.</summary>
    public bool Input(SurfaceInput e, List<int> trackIds, FocusedDevice? focused, int selectedTrack, Func<int, bool> isReturn)
    {
        var st = _c.State;
        int Track(int strip) => strip >= 0 && st.BankOrigin + strip < trackIds.Count ? trackIds[st.BankOrigin + strip] : -1;
        void Press(MidiTarget t) { if (e.Pressed && Unbound?.Invoke(t) != true) _applier.Trigger(t); }

        switch (e.Kind)
        {
            case SurfaceInputKind.Alive:
                _lastAliveMs = Environment.TickCount64;
                if (!_alive) { _alive = true; Refresh(); }
                break;
            case SurfaceInputKind.Fader when Track(e.Strip) is var t and > 0:
                Fader(e.Strip, MidiTarget.TrackVolume(t), e.Value / 16383.0);
                break;
            case SurfaceInputKind.MasterFader:
                Fader(8, MidiTarget.MasterVolume, e.Value / 16383.0);
                break;
            case SurfaceInputKind.Touch when Track(e.Strip) is var t and > 0:
                Touch(e.Strip, MidiTarget.TrackVolume(t), e.Pressed);
                break;
            case SurfaceInputKind.MasterTouch:
                Touch(8, MidiTarget.MasterVolume, e.Pressed);
                break;
            case SurfaceInputKind.VPot when RoleRouter.KnobTarget(st, e.Strip, trackIds, focused, isReturn) is { } kt
                                            && ControllerService.ToMidiTarget(kt) is { } mt && Unbound?.Invoke(mt) != true:
                _applier.Nudge(mt, e.Value * RoleRouter.StepFraction);
                break;
            case SurfaceInputKind.VPotPush when e.Pressed && st.KnobMode.Kind == KnobModeKind.Pan && Track(e.Strip) is var t and > 0:
                _applier.SetNormalized(MidiTarget.TrackPan(t), 0.5);   // push: back to the centre
                break;
            case SurfaceInputKind.Rec when Track(e.Strip) is var t and > 0: Press(MidiTarget.TrackArm(t)); break;
            case SurfaceInputKind.Solo when Track(e.Strip) is var t and > 0: Press(MidiTarget.TrackSolo(t)); break;
            case SurfaceInputKind.Mute when Track(e.Strip) is var t and > 0: Press(MidiTarget.TrackMute(t)); break;
            case SurfaceInputKind.Select when Track(e.Strip) is var t and > 0: Press(MidiTarget.TrackSelect(t)); break;
            case SurfaceInputKind.Play: Press(MidiTarget.TransportPlay); break;
            case SurfaceInputKind.Stop: Press(MidiTarget.TransportStop); break;
            case SurfaceInputKind.Record: Press(MidiTarget.TransportRecord); break;
            case SurfaceInputKind.Loop: Press(MidiTarget.TransportLoop); break;
            case SurfaceInputKind.Rewind: Press(MidiTarget.TransportRewind); break;
            case SurfaceInputKind.Forward: Press(MidiTarget.TransportForward); break;
            case SurfaceInputKind.Jog when e.Value != 0 && SessionShown?.Invoke() == true:
                for (int i = 0, n = Math.Min(Math.Abs(e.Value), 8); i < n; i++)
                    RunCommand?.Invoke(e.Value > 0 ? "session.selectDown" : "session.selectUp");
                break;
            case SurfaceInputKind.Jog when !_engine.IsPlaying && e.Value != 0:
                _engine.Seek(Math.Max(0, _engine.PositionBeats + e.Value * 0.25));   // one 16th per step, stopped
                break;
            case SurfaceInputKind.BankLeft or SurfaceInputKind.BankRight or SurfaceInputKind.ChannelLeft or SurfaceInputKind.ChannelRight when e.Pressed:
            {
                int step = e.Kind switch
                {
                    SurfaceInputKind.BankLeft => -ControllerState.BankSize, SurfaceInputKind.BankRight => ControllerState.BankSize,
                    SurfaceInputKind.ChannelLeft => -1, _ => 1,
                };
                // In the Session the channel buttons also walk the selected slot across.
                if (Math.Abs(step) == 1 && SessionShown?.Invoke() == true)
                    RunCommand?.Invoke(step > 0 ? "session.selectRight" : "session.selectLeft");
                if (!st.BankStep(step, trackIds.Count)) return false;
                Refresh();   // eight new strips: everything again
                return true;
            }
        }
        return false;
    }

    private void Fader(int strip, MidiTarget t, double pos)
    {
        // A fader held through a bank change keeps writing the track it was touched on.
        if (_touched[strip]) t = _touchTarget[strip];
        if (Unbound?.Invoke(t) == true) return;   // the user unbound it from the control's menu (FR-032)
        // A motor fader (or pickup off) jumps; one without a motor picks up first.
        bool pickup = _c.State.PickupOn && _c.Profile?.Pickup == true && !_touched[strip];
        if (pickup)
        {
            if (_pickupEpoch != _c.State.Epoch) { _pickupEpoch = _c.State.Epoch; foreach (var p in _pickup) p.Release(); }
            if (_applier.CurrentNormalized(t) is { } cur && !_pickup[strip].Accept(t, pos, cur)) return;
        }
        _applier.SetNormalized(t, pos);
        if (pickup) _pickup[strip].ReadBack(_applier.CurrentNormalized(t));
    }

    private void Touch(int strip, MidiTarget t, bool down)
    {
        if (down == _touched[strip]) return;
        _touched[strip] = down;
        if (down) { _touchTarget[strip] = t; _applier.TouchBegin(t); }
        else _applier.TouchEnd(_touchTarget[strip]);
    }

    // ---- feedback ---------------------------------------------------------------------------

    /// <summary>Send what changed on the surface since the last tick.</summary>
    public void Tick(List<int> trackIds, Dictionary<int, NotaTrackInfo> infos, FocusedDevice? focused, int selectedTrack,
        Func<int, bool> isReturn, string modeText)
    {
        var st = _c.State;
        st.Clamp(trackIds.Count);
        var labels = new string[SurfaceState.Strips];
        for (int s = 0; s < SurfaceState.Strips; s++)
        {
            int i = st.BankOrigin + s;
            if (i >= trackIds.Count || !infos.TryGetValue(trackIds[i], out var ti))
            {
                _want.Strip[s] = SurfaceStripState.Empty;
                labels[s] = "";
                continue;
            }
            int id = ti.Id;
            double peak = _engine.TryGetTrackMeter(id, out var m) ? Math.Max(m.PeakL, m.PeakR) : 0;
            int ring = 0;
            if (RoleRouter.KnobTarget(st, s, trackIds, focused, isReturn) is { } kt && ControllerService.ToMidiTarget(kt) is { } mt
                && _applier.CurrentNormalized(mt) is { } v)
                ring = Math.Clamp((int)Math.Round(1 + v * 10), 1, 11);
            labels[s] = st.KnobMode.Kind == KnobModeKind.Device
                ? (focused?.Knobs(Math.Clamp(st.Page, 0, Math.Max(0, focused.PageCount - 1)))[s] is { Target: not null } k ? k.Name : "")
                : modeText;
            _want.Strip[s] = new SurfaceStripState(id, Pos(ti.Volume / ControlApplier.VolumeMax), ring,
                ti.Muted != 0, ti.Soloed != 0, ti.Armed != 0, id == selectedTrack,
                TrackNames.Of(_engine, ti), SurfaceState.MeterLevel(peak));
        }
        _want.MasterFader = Pos(_engine.MasterVolume / ControlApplier.VolumeMax);
        bool playing = _engine.IsPlaying;
        _want.Play = playing;
        _want.Stop = !playing;
        _want.Record = _engine.IsRecording;
        _want.Loop = _engine.LoopEnabled;
        _want.Time = SurfaceState.TimeText(_engine.PositionBeats, _applier.BeatsPerBar());
        var line = new System.Text.StringBuilder();
        foreach (var l in labels) line.Append(McuCodec.Fit(l, 6)).Append(' ');
        _want.Assignment = line.ToString();

        if (_codec is Nota.Application.Controllers.Hui.HuiCodec)
        {
            long now = Environment.TickCount64;
            if (now - _lastPingMs >= 1000) { _lastPingMs = now; Send(Nota.Application.Controllers.Hui.HuiCodec.Ping); }
            if (_lastAliveMs == 0) _lastAliveMs = now;
            if (_alive && now - _lastAliveMs > 3000) _alive = false;
            if (!_alive) return;   // nobody there: feedback waits for the next answer
        }
        long nowMs = Environment.TickCount64;
        if (_retryAt > 0 && nowMs >= _retryAt) { _retryAt = 0; _differ.Invalidate(); }   // a send failed: everything again
        bool ok = true;
        foreach (var msg in _differ.Diff(_want, _touched)) ok &= Send(msg);
        if (!ok && _retryAt == 0) _retryAt = nowMs + 1000;   // the output went away for a moment: retry once a second
    }

    private long _retryAt;

    private static int Pos(double norm) => (int)Math.Round(Math.Clamp(norm, 0, 1) * 16383);

    private bool Send(byte[] m) => _c.OutputUid.Length > 0 && _engine.MidiSend(_c.OutputUid, m);

    /// <summary>Faders waiting to pick up: their target and hardware position (the hint, FR-020).</summary>
    public IEnumerable<(MidiTarget Target, double Physical)> Ghosts()
    {
        if (_pickupEpoch != _c.State.Epoch) yield break;
        foreach (var p in _pickup)
            if (!p.Caught && p.Target is MidiTarget t && p.LastPhysical is { } phys) yield return (t, phys);
    }

    /// <summary>Hardware fader touches still held (for the closing release).</summary>
    public IReadOnlyList<bool> Touched => _touched;
}
