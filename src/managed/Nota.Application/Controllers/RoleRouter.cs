// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// From "this physical control moved" to "this is what Nota should do" (specs/051), without
// touching the engine: the app executes the actions. One router per connected controller — it
// remembers button edges, so a CC button that sends 127 then 0 fires once. Faders drive the
// tracks of the controller's bank of eight; knobs are handed on (they follow the focused device,
// a later slice) together with page, knob-mode and jog roles.
//
// Claims say which messages are roles: a generic profile claims its declared controls except
// pads and buttons (they keep playing / stay learnable) and except anything the user mapped with
// MIDI Learn — an explicit mapping wins (FR-012). An MCU / HUI profile, or one that marks its
// port as control-only, claims the whole port.

using Nota.Application;
using System.Linq;

namespace Nota.Application.Controllers;

public enum ControllerTargetKind
{
    TransportPlay, TransportStop, TransportRecord, TransportLoop, TransportRewind, TransportForward,
    TrackVolume, MasterVolume,
    // Knobs (specs/051 US2): the bank's pan / send, and the focused device's parameters.
    TrackPan, TrackSend, DeviceParam, PluginParam, MidiDeviceParam, RackChainDeviceParam, RackChainInstrumentParam,
}

/// <summary>A control's target, independent of the app's MIDI Learn types. Device / Param / Sub
/// follow MIDI Learn's addressing: Device = the device index (-1 the instrument; for rack
/// children the rack: −1 Instrument Rack, ≥ 0 Audio Effect Rack, ≤ <see cref="MidiRackBase"/> − mi
/// MIDI Effect Rack), Param = the parameter (TrackSend: the send), Sub = the chain (a chain
/// device: chain × 1024 + slot).</summary>
public readonly record struct ControllerTarget(ControllerTargetKind Kind, int TrackId = -1, int Device = -1, int Param = -1, int Sub = -1)
{
    public const int MidiRackBase = -1000;
    public static ControllerTarget DeviceParam(int t, int d, int p) => new(ControllerTargetKind.DeviceParam, t, d, p);
    public static ControllerTarget PluginParam(int t, int d, int p) => new(ControllerTargetKind.PluginParam, t, d, p);
    public static ControllerTarget MidiDeviceParam(int t, int d, int p) => new(ControllerTargetKind.MidiDeviceParam, t, d, p);
    public static ControllerTarget RackChainDeviceParam(int t, int rack, int chain, int slot, int p)
        => new(ControllerTargetKind.RackChainDeviceParam, t, rack, p, chain * 1024 + slot);
    public static ControllerTarget RackChainInstrumentParam(int t, int rack, int chain, int p)
        => new(ControllerTargetKind.RackChainInstrumentParam, t, rack, p, chain);
}

public enum ControllerActionKind { Trigger, SetNormalized, Nudge, BankStep, Knob, PageStep, KnobMode, Jog, Command, TrackStep }

/// <summary>Something to do. Trigger / SetNormalized / Nudge carry a target; BankStep, TrackStep and
/// PageStep a signed amount in <see cref="Index"/>; Knob the knob index and its value; KnobMode
/// the mode; Jog its steps in <see cref="Index"/>; Command the command id. Nudge values are
/// fractions of the travel.</summary>
public readonly record struct ControllerAction(
    ControllerActionKind Kind, ControllerTarget Target = default, double Value = 0, int Index = 0,
    ControlValue Control = default, KnobModeKind Mode = KnobModeKind.Device, string? Command = null);

public sealed class RoleRouter
{
    /// <summary>One relative step moves this much of a target's travel.</summary>
    public const double StepFraction = 1.0 / 128;

    private readonly Dictionary<ControlDecl, bool> _down = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ControlDecl, int> _last = new(ReferenceEqualityComparer.Instance);   // a two-way knob's last value

    /// <summary>The claim table entries for <paramref name="profile"/> on <paramref name="source"/>.
    /// <paramref name="learned"/> holds MIDI Learn sources as (isNote, channel, number).</summary>
    public static List<MidiClaim> BuildClaims(ControllerProfile profile, int source, ISet<(bool isNote, int channel, int number)> learned)
    {
        var claims = new List<MidiClaim>();
        if (profile.Protocol != ControllerProtocol.Generic || profile.ClaimAll)
        {
            claims.Add(MidiClaim.Whole(source));
            return claims;
        }
        foreach (var d in profile.Controls)
        {
            if (!d.Role.IsClaimed) continue;
            foreach (var (status, number) in ControlDecoder.Messages(d))
            {
                if (status == 0xB0 && learned.Contains((false, d.Channel, number))) continue;
                if (status == 0x80 && learned.Contains((true, d.Channel, number))) continue;
                claims.Add(new MidiClaim(source, 0, status, d.Channel, number));
            }
        }
        return claims;
    }

    /// <summary>The declared control a message belongs to, or null. <paramref name="nrpn"/> is the
    /// NRPN parameter selected on that channel (CC 99 / 98): every NRPN control shares the same CC
    /// numbers, so the selected parameter tells them apart.</summary>
    public static ControlDecl? Find(ControllerProfile profile, int status, int channel, int d1, int? nrpn = null)
    {
        foreach (var d in profile.Controls)
        {
            if (d.Channel != channel) continue;
            if (d.Message == ControlMessage.Nrpn && nrpn is { } p && p != d.Number) continue;
            foreach (var (st, number) in ControlDecoder.Messages(d))
            {
                bool statusOk = st == 0x80 ? status is 0x80 or 0x90 : st == status;
                if (statusOk && (st is 0xE0 or 0xD0 || number == d1)) return d;
            }
        }
        return null;
    }

    /// <summary>What a decoded movement of <paramref name="decl"/> asks for. <paramref name="trackIds"/>
    /// is the track order the bank walks.</summary>
    public IReadOnlyList<ControllerAction> Route(ControlDecl decl, ControlValue v, ControllerState state, IReadOnlyList<int> trackIds)
    {
        var role = decl.Role;
        // A two-way role (an encoder for next / previous track, forward / rewind …): one press of
        // the right half per step right, of the left half per step left.
        if (role.LeftKind is { } leftKind && role.Kind != ControlRoleKind.Command)
        {
            int steps = Steps(decl, v);
            if (steps == 0 || PressAction(steps > 0 ? role.Kind : leftKind) is not { } act) return Array.Empty<ControllerAction>();
            return Enumerable.Repeat(act, Math.Min(Math.Abs(steps), 8)).ToArray();
        }
        switch (role.Kind)
        {
            case ControlRoleKind.Play: return Press(decl, v, Trigger(ControllerTargetKind.TransportPlay));
            case ControlRoleKind.Stop: return Press(decl, v, Trigger(ControllerTargetKind.TransportStop));
            case ControlRoleKind.Record: return Press(decl, v, Trigger(ControllerTargetKind.TransportRecord));
            case ControlRoleKind.Loop: return Press(decl, v, Trigger(ControllerTargetKind.TransportLoop));
            case ControlRoleKind.Rewind: return Press(decl, v, Trigger(ControllerTargetKind.TransportRewind));
            case ControlRoleKind.Forward: return Press(decl, v, Trigger(ControllerTargetKind.TransportForward));
            case ControlRoleKind.BankNext: return Press(decl, v, new ControllerAction(ControllerActionKind.BankStep, Index: ControllerState.BankSize));
            case ControlRoleKind.BankPrev: return Press(decl, v, new ControllerAction(ControllerActionKind.BankStep, Index: -ControllerState.BankSize));
            case ControlRoleKind.TrackNext: return Press(decl, v, new ControllerAction(ControllerActionKind.TrackStep, Index: 1));   // select the track (specs/107)
            case ControlRoleKind.TrackPrev: return Press(decl, v, new ControllerAction(ControllerActionKind.TrackStep, Index: -1));
            case ControlRoleKind.PageNext: return Press(decl, v, new ControllerAction(ControllerActionKind.PageStep, Index: 1));
            case ControlRoleKind.PagePrev: return Press(decl, v, new ControllerAction(ControllerActionKind.PageStep, Index: -1));
            case ControlRoleKind.ModeDevice: return Press(decl, v, new ControllerAction(ControllerActionKind.KnobMode, Mode: KnobModeKind.Device));
            case ControlRoleKind.ModeVolume: return Press(decl, v, new ControllerAction(ControllerActionKind.KnobMode, Mode: KnobModeKind.Volume));
            case ControlRoleKind.ModePan: return Press(decl, v, new ControllerAction(ControllerActionKind.KnobMode, Mode: KnobModeKind.Pan));
            case ControlRoleKind.ModeSend: return Press(decl, v, new ControllerAction(ControllerActionKind.KnobMode, Mode: KnobModeKind.Send));
            case ControlRoleKind.Command when role.Command is { Length: > 0 } id && role.CommandLeft is { Length: > 0 } left:
            {
                // A two-way encoder: right runs one command, left the other, once per step. An
                // absolute knob steps by its value moving up or down.
                int steps = Steps(decl, v);
                if (steps == 0) return Array.Empty<ControllerAction>();
                return new[] { new ControllerAction(ControllerActionKind.Command, Index: Math.Min(Math.Abs(steps), 8), Command: steps > 0 ? id : left) };
            }
            case ControlRoleKind.Command when role.Command is { Length: > 0 } id:
                return Press(decl, v, new ControllerAction(ControllerActionKind.Command, Index: 1, Command: id));

            case ControlRoleKind.Fader:
            {
                int i = state.BankOrigin + role.Index;
                if (role.Index >= ControllerState.BankSize || i < 0 || i >= trackIds.Count) return Array.Empty<ControllerAction>();
                return new[] { Move(new ControllerTarget(ControllerTargetKind.TrackVolume, trackIds[i]), v) };
            }
            case ControlRoleKind.MasterFader:
                return new[] { Move(new ControllerTarget(ControllerTargetKind.MasterVolume), v) };
            case ControlRoleKind.Knob:
                return new[] { new ControllerAction(ControllerActionKind.Knob, Index: role.Index, Control: v) };
            case ControlRoleKind.Jog:
                return v.IsRelative && v.Steps != 0
                    ? new[] { new ControllerAction(ControllerActionKind.Jog, Index: v.Steps) }
                    : Array.Empty<ControllerAction>();
        }
        return Array.Empty<ControllerAction>();   // pads, buttons: not routed (format 1)
    }

    /// <summary>What knob <paramref name="knob"/> drives (FR-016, FR-016a): in Device mode the
    /// focused device's current page; in Volume / Pan / Send the bank's track — a return track
    /// has no sends of its own, so Send mode skips it. Null = nothing (an empty knob).</summary>
    public static ControllerTarget? KnobTarget(ControllerState state, int knob, IReadOnlyList<int> trackIds,
        FocusedDevice? focused, Func<int, bool> isReturn)
    {
        var mode = state.KnobMode;
        if (mode.Kind == KnobModeKind.Device)
            return focused?.Target(Math.Clamp(state.Page, 0, Math.Max(0, focused.PageCount - 1)), knob);
        int i = state.BankOrigin + knob;
        if (knob < 0 || knob >= ControllerState.BankSize || i < 0 || i >= trackIds.Count) return null;
        int track = trackIds[i];
        return mode.Kind switch
        {
            KnobModeKind.Volume => new ControllerTarget(ControllerTargetKind.TrackVolume, track),
            KnobModeKind.Pan => new ControllerTarget(ControllerTargetKind.TrackPan, track),
            _ => isReturn(track) ? null : new ControllerTarget(ControllerTargetKind.TrackSend, track, -1, mode.Send),
        };
    }

    // A two-way control's steps: a relative encoder's own, an absolute knob's value moving.
    private int Steps(ControlDecl decl, ControlValue v)
    {
        if (v.IsRelative) return v.Steps;
        int now = (int)Math.Round(v.Position * 127);
        int steps = _last.TryGetValue(decl, out int was) ? now - was : 0;
        _last[decl] = now;
        return steps;
    }

    // What one press of a button role does (the half of a two-way control that ran).
    private static ControllerAction? PressAction(ControlRoleKind k) => k switch
    {
        ControlRoleKind.Rewind => Trigger(ControllerTargetKind.TransportRewind),
        ControlRoleKind.Forward => Trigger(ControllerTargetKind.TransportForward),
        ControlRoleKind.BankNext => new ControllerAction(ControllerActionKind.BankStep, Index: ControllerState.BankSize),
        ControlRoleKind.BankPrev => new ControllerAction(ControllerActionKind.BankStep, Index: -ControllerState.BankSize),
        ControlRoleKind.TrackNext => new ControllerAction(ControllerActionKind.TrackStep, Index: 1),
        ControlRoleKind.TrackPrev => new ControllerAction(ControllerActionKind.TrackStep, Index: -1),
        ControlRoleKind.PageNext => new ControllerAction(ControllerActionKind.PageStep, Index: 1),
        ControlRoleKind.PagePrev => new ControllerAction(ControllerActionKind.PageStep, Index: -1),
        _ => null,
    };

    private static ControllerAction Trigger(ControllerTargetKind k) => new(ControllerActionKind.Trigger, new ControllerTarget(k));

    private static ControllerAction Move(ControllerTarget t, ControlValue v) => v.IsRelative
        ? new ControllerAction(ControllerActionKind.Nudge, t, v.Steps * StepFraction)
        : new ControllerAction(ControllerActionKind.SetNormalized, t, v.Position);

    // A button fires once on the press: a note-on / a CC crossing up through half-way. A relative
    // control (an encoder clicked as a button) fires on any step.
    private IReadOnlyList<ControllerAction> Press(ControlDecl decl, ControlValue v, ControllerAction action)
    {
        // A note is pressed at any velocity (a soft pad hit is still a press); a CC past half-way.
        bool down = v.IsRelative ? v.Steps != 0 : decl.Message == ControlMessage.Note ? v.Position > 0 : v.Position >= 0.5;
        bool was = _down.TryGetValue(decl, out var w) && w;
        _down[decl] = !v.IsRelative && down;
        return down && (v.IsRelative || !was) ? new[] { action } : Array.Empty<ControllerAction>();
    }
}
