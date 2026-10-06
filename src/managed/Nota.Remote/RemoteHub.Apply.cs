// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Applying phone commands, on the UI thread. The phone is one more controller: transport goes
// through Nota's transport view-model (so the bar mirrors it), faders and knobs are
// hardware-style gestures (latching automation while recording, an override otherwise), and a
// mapped control goes through MIDI Learn first, exactly like a gamepad stick.

using System.Text.Json;
using Nota.Application;

namespace Nota.Remote;

public sealed partial class RemoteHub
{
    private const float VolumeMax = 1.5f;   // track / master fader travel (VFader.Max)
    private static readonly TimeSpan FaderHold = TimeSpan.FromMilliseconds(700);

    // Faders a phone is moving right now: another phone sees "Anna has it" on that fader.
    private readonly Dictionary<int, (int Conn, string Name, long Until)> _heldFaders = new();

    private static bool NeedsControl(string t) => t is not ("track" or "follow" or "sub" or "name");

    private void Apply(IRemoteHost host, RemoteClient c, RemoteCommand cmd)
    {
        if (NeedsControl(cmd.T) && Access() != RemoteAccess.Control)
        {
            Toast(c, "Nota lets this phone play notes only. Change it in Settings → Remote.");
            return;
        }

        switch (cmd.T)
        {
            case "track":
                if (TrackExists(cmd.Id) && cmd.Id != c.TrackId)
                {
                    c.TrackId = cmd.Id;
                    c.Follow = false;
                    _devicesDirty = true;
                }
                break;
            case "follow":
                c.Follow = cmd.On;
                if (cmd.On && host.SelectedTrackId > 0) c.TrackId = host.SelectedTrackId;
                _devicesDirty = true;
                break;
            case "sub":
                c.Screen = cmd.S;
                c.MacroDevice = Math.Max(0, cmd.I);
                c.SentScreenState = null;
                _devicesDirty = true;
                break;
            case "name":
                if (c.Device is { } d)
                {
                    Pairing.Rename(d.Id, cmd.S);
                    c.Name = RemotePairing.CleanName(cmd.S);
                    _devicesDirty = true;
                }
                break;

            // ---- transport ------------------------------------------------------
            case "play": host.Play(); break;
            case "stop": host.Stop(); break;
            case "rec": host.SetRecord(cmd.On); break;
            case "loop": host.SetLoop(cmd.On); break;
            case "met": host.SetMetronome(cmd.On); break;
            case "bpm": if (cmd.V is >= 20 and <= 300) host.SetBpm(Math.Round(cmd.V, 2)); break;
            case "seek": _engine.Seek(Math.Max(0, cmd.V)); break;
            case "loopRange":
                if (cmd.V2 > cmd.V)
                {
                    host.SetLoopRange(Math.Max(0, cmd.V), cmd.V2);
                    host.SetLoop(cmd.On);
                }
                break;
            case "undo":
                Toast(c, host.Undo() ? "Undone" : "Nothing to undo");
                break;

            // ---- mixer ----------------------------------------------------------
            case "vol":
                if (!TrackExists(cmd.Id)) break;
                _engine.BeginAutomationWrite(cmd.Id, AutomationTarget.Volume, -1, -1, "", latch: true);
                _engine.SetTrackVolume(cmd.Id, (float)Math.Clamp(cmd.V, 0, VolumeMax));
                HoldFader(c, cmd.Id, cmd.Phase);
                if (cmd.Phase == 2) host.Refresh();
                break;
            case "pan":
                if (!TrackExists(cmd.Id)) break;
                _engine.BeginAutomationWrite(cmd.Id, AutomationTarget.Pan, -1, -1, "", latch: true);
                _engine.SetTrackPan(cmd.Id, (float)Math.Clamp(cmd.V, -1, 1));
                if (cmd.Phase == 2) host.Refresh();
                break;
            case "mute": if (TrackExists(cmd.Id)) { _engine.SetTrackMute(cmd.Id, cmd.On); host.Refresh(); } break;
            case "solo": if (TrackExists(cmd.Id)) { _engine.SetTrackSolo(cmd.Id, cmd.On); host.Refresh(); } break;
            case "arm": if (TrackExists(cmd.Id)) { _engine.SetTrackArmed(cmd.Id, cmd.On); host.Refresh(); } break;
            case "mvol":
                host.MasterVolume = Math.Clamp(cmd.V, 0, VolumeMax);
                HoldFader(c, MasterFaderId, cmd.Phase);
                break;

            // ---- XY / tilt --------------------------------------------------------
            case "xy": ApplyXy(host, c, cmd); break;

            // ---- macros -----------------------------------------------------------
            case "mac":
            {
                int knob = cmd.I;
                if (knob is < 0 or > 7) break;
                var r = host.PhoneControl(PhoneControls.Macro1 + knob, c.TrackId, cmd.V);
                if (r == PhoneControlResult.Bound) Learned(host, c, PhoneControls.Macro1 + knob);
                if (r != PhoneControlResult.None) break;
                var devs = MacroDevicesFor(c.TrackId);
                if (c.MacroDevice < devs.Count && knob < devs[c.MacroDevice].Params.Count)
                    RemoteParams.Set(_engine, devs[c.MacroDevice].Params[knob], cmd.V);
                if (cmd.Phase == 2) host.Refresh();
                break;
            }

            // ---- session ------------------------------------------------------------
            case "launch": if (TrackExists(cmd.Id) && cmd.I >= 0 && cmd.I < _engine.SceneCount) _engine.LaunchSlot(cmd.Id, cmd.I); break;
            case "scene": if (cmd.I >= 0 && cmd.I < _engine.SceneCount) _engine.LaunchScene(cmd.I); break;
            case "stopSlot": if (TrackExists(cmd.Id)) _engine.StopSlot(cmd.Id); break;
            case "stopAll": _engine.StopAllSession(); break;
        }
    }

    private const int MasterFaderId = -1;

    private void HoldFader(RemoteClient c, int faderId, int phase)
    {
        if (phase == 2) { _heldFaders.Remove(faderId); return; }
        _heldFaders[faderId] = (c.ConnectionId, ShortName(c.Name), DateTime.UtcNow.Add(FaderHold).Ticks);
    }

    private void ExpireHeldFaders()
    {
        if (_heldFaders.Count == 0) return;
        long now = DateTime.UtcNow.Ticks;
        foreach (var k in _heldFaders.Where(kv => kv.Value.Until < now || !_clients.ContainsKey(kv.Value.Conn)).Select(kv => kv.Key).ToList())
            _heldFaders.Remove(k);
    }

    /// <summary>The XY pad or the tilt moved. A mapped axis drives its mapping; an unmapped one
    /// drives the pad's default pair. Tilt that isn't mapped of its own moves the pad's axes.
    /// <c>I</c>: 0 both axes, 1 X only, 2 Y only (in learn mode the phone sends the axis the
    /// finger actually moved, so one gesture binds one axis).</summary>
    private void ApplyXy(IRemoteHost host, RemoteClient c, RemoteCommand cmd)
    {
        bool tilt = cmd.S == "tilt";
        bool doX = cmd.I != 2, doY = cmd.I != 1;
        (RemoteParam X, RemoteParam Y)? def = null;
        bool defResolved = false;

        void Axis(bool isX, double v)
        {
            int ctl = tilt ? (isX ? PhoneControls.TiltX : PhoneControls.TiltY) : (isX ? PhoneControls.XyX : PhoneControls.XyY);
            var r = host.PhoneControl(ctl, c.TrackId, v);
            if (r == PhoneControlResult.Bound) { Learned(host, c, ctl); return; }
            if (r == PhoneControlResult.Mapped) return;
            if (tilt)
            {
                // Unmapped tilt stands in for the finger on the pad.
                int xy = isX ? PhoneControls.XyX : PhoneControls.XyY;
                var r2 = host.PhoneControl(xy, c.TrackId, v);
                if (r2 == PhoneControlResult.Bound) { Learned(host, c, xy); return; }
                if (r2 == PhoneControlResult.Mapped) return;
            }
            if (!defResolved) { def = RemoteParams.DefaultXy(_engine, c.TrackId); defResolved = true; }
            if (def is { } d) RemoteParams.Set(_engine, isX ? d.X : d.Y, v);
        }

        if (doX) Axis(true, Math.Clamp(cmd.V, 0, 1));
        if (doY) Axis(false, Math.Clamp(cmd.V2, 0, 1));
        if (cmd.Phase == 2) host.Refresh();
    }

    private void Learned(IRemoteHost host, RemoteClient c, int controlId)
    {
        string target = host.PhoneMappingName(controlId, c.TrackId) ?? "";
        c.Send(Json(w =>
        {
            w.WriteString("t", "learned");
            w.WriteString("src", PhoneControls.Name(controlId));
            w.WriteString("dst", target);
        }));
        c.SentScreenState = null;
    }

    internal static void Toast(RemoteClient c, string msg)
        => c.Send(Json(w => { w.WriteString("t", "toast"); w.WriteString("msg", msg); }));

    private bool TrackExists(int trackId)
    {
        if (trackId <= 0) return false;
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return true;
        return false;
    }
}
