// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The one place a hardware control's value lands on a Nota target, shared by MIDI Learn and
// controller profiles (specs/051) so the two cannot drift: a move is a latching automation
// gesture and one undo step per movement ended by a 1 s pause (specs/009, specs/016), exactly
// as MIDI Learn has always applied it; a button triggers. Values are normalized 0..1 over the
// target's on-screen travel.

using System;
using System.Collections.Generic;
using Nota.Application;
using Nota.Application.Editing;

namespace Nota.App.Controllers;

public sealed class ControlApplier(IAudioEngine engine)
{
    public const float VolumeMax = 1.5f;   // track/master fader travel (matches VFader.Max)
    public const float RackGainMax = 2.0f; // rack out / chain gain travel (matches the rack faders)

    private static readonly TimeSpan MixPause = TimeSpan.FromSeconds(1);
    private readonly Dictionary<MixTarget, MixMovement> _mixUndo = new();

    /// <summary>Bar length for Rewind / Forward (the arrangement's, set by the main window).</summary>
    public Func<double> BeatsPerBar { get; set; } = () => 4.0;

    /// <summary>Loop on / off the way the transport bar's button does it (its region, or the
    /// selection, or the first four bars). Set by the main window; the fallback toggles the
    /// engine's region with the same four-bar default.</summary>
    public Action? ToggleLoop { get; set; }

    /// <summary>A Select button asked for this track (the main window selects it like a header click).</summary>
    public event Action<int>? TrackSelectRequested;

    /// <summary>Ends controller movements after their pause; call on every UI tick.</summary>
    public void Poll(long nowMs)
    {
        // A fader held by its touch sensor is one movement however long it rests: its release ends it.
        foreach (var (key, m) in _mixUndo)
            if (!_touchedMix.Contains(key)) m.Poll(nowMs);
    }

    private void MixTouch(MixTarget target, Action set)
    {
        if (!_mixUndo.TryGetValue(target, out var m)) _mixUndo[target] = m = new MixMovement(engine, target, MixPause);
        m.Touch(set, Environment.TickCount64);
    }

    // Motor faders held by their touch sensor (FR-024): the touch is the movement — automation
    // writes while held (not latching) and the undo step ends on release.
    private readonly HashSet<MidiTarget> _touched = new();
    private readonly HashSet<MixTarget> _touchedMix = new();

    /// <summary>A touch-sensitive fader was touched: its movement starts now.</summary>
    public void TouchBegin(MidiTarget t)
    {
        if (!_touched.Add(t)) return;
        if (MixKey(t) is { } k) _touchedMix.Add(k);
        if (Lane(t) is { } l) engine.BeginAutomationWrite(t.TrackId, l.Target, l.Device, l.Param, l.Id, latch: false);
    }

    /// <summary>The fader was let go: the automation write and the undo step end now.</summary>
    public void TouchEnd(MidiTarget t)
    {
        if (!_touched.Remove(t)) return;
        if (Lane(t) is { } l) engine.EndAutomationWrite(t.TrackId, l.Target, l.Device, l.Param, l.Id);
        if (MixKey(t) is { } k)
        {
            _touchedMix.Remove(k);
            if (_mixUndo.TryGetValue(k, out var m)) m.End();
        }
    }

    private static MixTarget? MixKey(MidiTarget t) => t.Kind switch
    {
        MidiTargetKind.TrackVolume => MixTarget.Volume(t.TrackId),
        MidiTargetKind.TrackPan => MixTarget.Pan(t.TrackId),
        MidiTargetKind.MasterVolume => MixTarget.Master,
        _ => null,
    };

    private (AutomationTarget Target, int Device, int Param, string Id)? Lane(MidiTarget t) => t.Kind switch
    {
        MidiTargetKind.TrackVolume => (AutomationTarget.Volume, -1, -1, ""),
        MidiTargetKind.TrackPan => (AutomationTarget.Pan, -1, -1, ""),
        _ => null,
    };

    /// <summary>Drive a continuous target to <paramref name="norm"/> (0..1 of its travel).</summary>
    public void SetNormalized(MidiTarget t, double norm)
    {
        double outv = Math.Clamp(norm, 0, 1);
        if (!_touched.Contains(t)) BeginLatchWrite(t);   // a hardware move is a latching automation gesture

        switch (t.Kind)
        {
            // Device parameters, volume and pan: one undo step per movement, ended by a 1 s
            // pause (specs/009, specs/016).
            case MidiTargetKind.DeviceParam:
            {
                float min = engine.DeviceParamMin(t.TrackId, t.DeviceIndex, t.ParamIndex);
                float max = engine.DeviceParamMax(t.TrackId, t.DeviceIndex, t.ParamIndex);
                MixTouch(MixTarget.DeviceParam(t.TrackId, t.DeviceIndex, t.ParamIndex),
                    () => engine.DeviceSetParam(t.TrackId, t.DeviceIndex, t.ParamIndex, (float)(min + outv * (max - min))));
                break;
            }
            case MidiTargetKind.PluginParam:
                MixTouch(MixTarget.PluginParam(t.TrackId, t.DeviceIndex, t.ParamIndex),
                    () => engine.PluginParamSet(t.TrackId, t.DeviceIndex, t.ParamIndex, (float)outv));
                break;
            case MidiTargetKind.MidiDeviceParam:
            {
                float min = engine.MidiEffectParamMin(t.TrackId, t.DeviceIndex, t.ParamIndex);
                float max = engine.MidiEffectParamMax(t.TrackId, t.DeviceIndex, t.ParamIndex);
                MixTouch(MixTarget.MidiDeviceParam(t.TrackId, t.DeviceIndex, t.ParamIndex),
                    () => engine.MidiEffectSetParam(t.TrackId, t.DeviceIndex, t.ParamIndex, (float)(min + outv * (max - min))));
                break;
            }
            case MidiTargetKind.TrackVolume:
                MixTouch(MixTarget.Volume(t.TrackId), () => engine.SetTrackVolume(t.TrackId, (float)(outv * VolumeMax)));
                break;
            case MidiTargetKind.MasterVolume:
                MixTouch(MixTarget.Master, () => engine.SetMasterVolume((float)(outv * VolumeMax)));
                break;
            case MidiTargetKind.CueVolume: // a listening level: no undo step (specs/089)
                engine.CueVolume = (float)(outv * VolumeMax); // the Cue fader's travel
                break;
            case MidiTargetKind.TrackPan:
                MixTouch(MixTarget.Pan(t.TrackId), () => engine.SetTrackPan(t.TrackId, (float)(outv * 2.0 - 1.0)));
                break;
            // A send level: one undo step per movement and its own lane (specs/072).
            case MidiTargetKind.TrackSend:
                MixTouch(MixTarget.Send(t.TrackId, t.ParamIndex), () => engine.SetTrackSend(t.TrackId, t.ParamIndex, (float)outv));
                break;
            // Inside a rack (specs/051 R10): set as the rack card's own knobs do — they keep no
            // value undo of their own and have no automation lane.
            case MidiTargetKind.RackChainDeviceParam:
            {
                var (c, d) = t.ChainSlot;
                var (min, max) = ChainParamRange(t, c, d);
                float v = (float)(min + outv * (max - min));
                if (t.DeviceIndex <= MidiTarget.MidiRackBase) engine.MidiRackChainDeviceParamSet(t.TrackId, MidiTarget.MidiRackBase - t.DeviceIndex, c, d, t.ParamIndex, v);
                else if (t.DeviceIndex < 0) engine.RackChainDeviceParamSet(t.TrackId, c, d, t.ParamIndex, v);
                else engine.RackDevChainDeviceParamSet(t.TrackId, t.DeviceIndex, c, d, t.ParamIndex, v);
                break;
            }
            case MidiTargetKind.RackChainInstrumentParam:
                if (t.DeviceIndex < 0) engine.RackChainInstrumentParamSet(t.TrackId, t.Sub, t.ParamIndex, (float)outv);
                else engine.RackDevChainInstrumentParamSet(t.TrackId, t.DeviceIndex, t.Sub, t.ParamIndex, (float)outv);
                break;
            case MidiTargetKind.RackVolume or MidiTargetKind.RackChainGain when t.DeviceIndex <= MidiTarget.MidiRackBase:
                break; // a MIDI Effect Rack has no audio level
            case MidiTargetKind.RackVolume:
                if (t.DeviceIndex < 0) engine.RackSetVolume(t.TrackId, (float)(outv * RackGainMax));
                else engine.RackDevSetVolume(t.TrackId, t.DeviceIndex, (float)(outv * RackGainMax));
                break;
            case MidiTargetKind.RackChainGain:
                if (t.DeviceIndex < 0) engine.RackSetChainGain(t.TrackId, t.ParamIndex, (float)(outv * RackGainMax));
                else engine.RackDevSetChainGain(t.TrackId, t.DeviceIndex, t.ParamIndex, (float)(outv * RackGainMax));
                break;
        }
    }

    /// <summary>Move a continuous target by <paramref name="delta"/> of its travel (relative encoders).</summary>
    public void Nudge(MidiTarget t, double delta)
    {
        if (CurrentNormalized(t) is { } v) SetNormalized(t, v + delta);
    }

    /// <summary>The target's value as 0..1 of the travel <see cref="SetNormalized"/> uses; null
    /// for buttons and targets with no readable value.</summary>
    public double? CurrentNormalized(MidiTarget t)
    {
        static double Span(float v, float min, float max) => max > min ? Math.Clamp((v - min) / (max - min), 0, 1) : 0;
        switch (t.Kind)
        {
            case MidiTargetKind.DeviceParam:
                return Span(engine.DeviceGetParam(t.TrackId, t.DeviceIndex, t.ParamIndex),
                    engine.DeviceParamMin(t.TrackId, t.DeviceIndex, t.ParamIndex), engine.DeviceParamMax(t.TrackId, t.DeviceIndex, t.ParamIndex));
            case MidiTargetKind.PluginParam:
                return Math.Clamp(engine.PluginParamGet(t.TrackId, t.DeviceIndex, t.ParamIndex), 0, 1);
            case MidiTargetKind.MidiDeviceParam:
                return Span(engine.MidiEffectGetParam(t.TrackId, t.DeviceIndex, t.ParamIndex),
                    engine.MidiEffectParamMin(t.TrackId, t.DeviceIndex, t.ParamIndex), engine.MidiEffectParamMax(t.TrackId, t.DeviceIndex, t.ParamIndex));
            case MidiTargetKind.TrackVolume:
                return TrackInfo(t.TrackId) is { } vi ? Math.Clamp(vi.Volume / VolumeMax, 0, 1) : null;
            case MidiTargetKind.TrackPan:
                return TrackInfo(t.TrackId) is { } pi ? Math.Clamp((pi.Pan + 1) / 2, 0, 1) : null;
            case MidiTargetKind.MasterVolume:
                return Math.Clamp(engine.MasterVolume / VolumeMax, 0, 1);
            case MidiTargetKind.CueVolume:
                return Math.Clamp(engine.CueVolume / VolumeMax, 0, 1);
            case MidiTargetKind.TrackSend:
                return Math.Clamp(engine.GetTrackSend(t.TrackId, t.ParamIndex), 0, 1);
            case MidiTargetKind.RackChainDeviceParam:
            {
                var (c, d) = t.ChainSlot;
                var (min, max) = ChainParamRange(t, c, d);
                float v = t.DeviceIndex <= MidiTarget.MidiRackBase
                    ? engine.MidiRackChainDeviceParamGet(t.TrackId, MidiTarget.MidiRackBase - t.DeviceIndex, c, d, t.ParamIndex)
                    : t.DeviceIndex < 0 ? engine.RackChainDeviceParamGet(t.TrackId, c, d, t.ParamIndex)
                    : engine.RackDevChainDeviceParamGet(t.TrackId, t.DeviceIndex, c, d, t.ParamIndex);
                return Span(v, min, max);
            }
            case MidiTargetKind.RackChainInstrumentParam:
                return Math.Clamp(t.DeviceIndex < 0 ? engine.RackChainInstrumentParamGet(t.TrackId, t.Sub, t.ParamIndex)
                    : engine.RackDevChainInstrumentParamGet(t.TrackId, t.DeviceIndex, t.Sub, t.ParamIndex), 0, 1);
            case MidiTargetKind.RackVolume when t.DeviceIndex > MidiTarget.MidiRackBase:
                return Math.Clamp((t.DeviceIndex < 0 ? engine.RackVolume(t.TrackId) : engine.RackDevVolume(t.TrackId, t.DeviceIndex)) / RackGainMax, 0, 1);
            case MidiTargetKind.RackChainGain when t.DeviceIndex > MidiTarget.MidiRackBase:
                return Math.Clamp((t.DeviceIndex < 0 ? engine.RackChainGain(t.TrackId, t.ParamIndex)
                    : engine.RackDevChainGain(t.TrackId, t.DeviceIndex, t.ParamIndex)) / RackGainMax, 0, 1);
            default:
                return null;
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
                engine.BeginAutomationWrite(t.TrackId, AutomationTarget.DeviceParam, t.DeviceIndex, t.ParamIndex, "", latch: true);
                break;
            case MidiTargetKind.MidiDeviceParam:
                engine.BeginAutomationWrite(t.TrackId, AutomationTarget.MidiDeviceParam, t.DeviceIndex, t.ParamIndex, "", latch: true);
                break;
            case MidiTargetKind.PluginParam:
                engine.BeginAutomationWrite(t.TrackId, AutomationTarget.PluginParam, t.DeviceIndex, -1,
                    engine.PluginParamId(t.TrackId, t.DeviceIndex, t.ParamIndex), latch: true);
                break;
            case MidiTargetKind.TrackVolume:
                engine.BeginAutomationWrite(t.TrackId, AutomationTarget.Volume, -1, -1, "", latch: true);
                break;
            case MidiTargetKind.TrackPan:
                engine.BeginAutomationWrite(t.TrackId, AutomationTarget.Pan, -1, -1, "", latch: true);
                break;
            case MidiTargetKind.TrackSend:
                engine.BeginAutomationWrite(t.TrackId, AutomationTarget.Send, t.ParamIndex, -1, "", latch: true);
                break;
        }
    }

    /// <summary>Fire a button target once (toggle, transport step, select).</summary>
    public void Trigger(MidiTarget t)
    {
        switch (t.Kind)
        {
            case MidiTargetKind.TransportPlay: engine.Play(); break;
            case MidiTargetKind.TransportStop: engine.StopTransport(); break;
            case MidiTargetKind.TransportRecord: engine.SetRecording(!engine.IsRecording); break;
            case MidiTargetKind.TransportLoop:
                if (ToggleLoop is { } toggle) toggle();
                else
                {
                    double s = engine.LoopStart, e = engine.LoopEnd;
                    if (e <= s) { s = 0; e = 4 * BeatsPerBar(); }
                    engine.SetLoop(!engine.LoopEnabled, s, e);
                }
                break;
            case MidiTargetKind.TransportRewind or MidiTargetKind.TransportForward:
            {
                // One bar: back to the start of this bar (or the previous one when already there),
                // forward to the next bar line.
                double bar = Math.Max(0.25, BeatsPerBar());
                double pos = engine.PositionBeats;
                double barIndex = Math.Floor(pos / bar + 1e-9);
                double to = t.Kind == MidiTargetKind.TransportForward
                    ? (barIndex + 1) * bar
                    : (pos - barIndex * bar > 1e-6 ? barIndex * bar : (barIndex - 1) * bar);
                engine.Seek(Math.Max(0, to));
                break;
            }
            case MidiTargetKind.TrackMute: engine.SetTrackMute(t.TrackId, !(TrackInfo(t.TrackId)?.Muted > 0)); break;
            case MidiTargetKind.TrackSolo: engine.SetTrackSolo(t.TrackId, !(TrackInfo(t.TrackId)?.Soloed > 0)); break;
            case MidiTargetKind.TrackCue:
                engine.SetTrackCue(t.TrackId, !engine.GetTrackCue(t.TrackId));
                CueKey.NotifyChanged();
                break;
            case MidiTargetKind.TrackArm: engine.SetTrackArmed(t.TrackId, !(TrackInfo(t.TrackId)?.Armed > 0)); break;
            case MidiTargetKind.TrackSelect: TrackSelectRequested?.Invoke(t.TrackId); break;
            case MidiTargetKind.RackChainMute when t.DeviceIndex <= MidiTarget.MidiRackBase:
            {
                int mi = MidiTarget.MidiRackBase - t.DeviceIndex;
                engine.MidiRackSetChainMute(t.TrackId, mi, t.ParamIndex, !engine.MidiRackChainMute(t.TrackId, mi, t.ParamIndex));
                break;
            }
            case MidiTargetKind.RackChainSolo when t.DeviceIndex <= MidiTarget.MidiRackBase:
            {
                int mi = MidiTarget.MidiRackBase - t.DeviceIndex;
                engine.MidiRackSetChainSolo(t.TrackId, mi, t.ParamIndex, !engine.MidiRackChainSolo(t.TrackId, mi, t.ParamIndex));
                break;
            }
            case MidiTargetKind.RackChainMute:
                if (t.DeviceIndex < 0) engine.RackSetChainMute(t.TrackId, t.ParamIndex, !engine.RackChainMute(t.TrackId, t.ParamIndex));
                else engine.RackDevSetChainMute(t.TrackId, t.DeviceIndex, t.ParamIndex, !engine.RackDevChainMute(t.TrackId, t.DeviceIndex, t.ParamIndex));
                break;
            case MidiTargetKind.RackChainSolo:
                if (t.DeviceIndex < 0) engine.RackSetChainSolo(t.TrackId, t.ParamIndex, !engine.RackChainSolo(t.TrackId, t.ParamIndex));
                else engine.RackDevSetChainSolo(t.TrackId, t.DeviceIndex, t.ParamIndex, !engine.RackDevChainSolo(t.TrackId, t.DeviceIndex, t.ParamIndex));
                break;
        }
    }

    private (float Min, float Max) ChainParamRange(MidiTarget t, int chain, int slot)
    {
        if (t.DeviceIndex <= MidiTarget.MidiRackBase)
        {
            int mi = MidiTarget.MidiRackBase - t.DeviceIndex;
            return (engine.MidiRackChainDeviceParamMin(t.TrackId, mi, chain, slot, t.ParamIndex),
                    engine.MidiRackChainDeviceParamMax(t.TrackId, mi, chain, slot, t.ParamIndex));
        }
        return t.DeviceIndex < 0
            ? (engine.RackChainDeviceParamMin(t.TrackId, chain, slot, t.ParamIndex), engine.RackChainDeviceParamMax(t.TrackId, chain, slot, t.ParamIndex))
            : (engine.RackDevChainDeviceParamMin(t.TrackId, t.DeviceIndex, chain, slot, t.ParamIndex),
               engine.RackDevChainDeviceParamMax(t.TrackId, t.DeviceIndex, chain, slot, t.ParamIndex));
    }

    private NotaTrackInfo? TrackInfo(int trackId)
    {
        int n = engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return ti;
        return null;
    }
}
