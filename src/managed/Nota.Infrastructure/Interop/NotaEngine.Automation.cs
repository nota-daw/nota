// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    /// <summary>Device-free self-test (M9): automation interpolation + block-rate apply.</summary>
    public bool AutomationSelfTest()
    { ThrowIfDisposed(); return NativeMethods.AutomationSelfTest(_handle) != 0; }

    /// <summary>Device-free self-test (M9-D): per-segment curvature shaping.</summary>
    public bool AutomationCurveSelfTest()
    { ThrowIfDisposed(); return NativeMethods.AutomationCurveSelfTest(_handle) != 0; }

    /// <summary>Device-free self-test (M9-B): plugin-param automation lane storage + dispatch.</summary>
    public bool PluginAutomationSelfTest()
    { ThrowIfDisposed(); return NativeMethods.PluginAutomationSelfTest(_handle) != 0; }

    /// <summary>Device-free self-test (Instrument Rack): chains sum, mute silences, a macro
    /// mapping drives a child param, and the serialized blob round-trips.</summary>
    public bool RackSelfTest()
    { ThrowIfDisposed(); return NativeMethods.RackSelfTest(_handle) != 0; }

    /// <summary>Device-free self-test (M9-C): automation write path (touch, latch, override).</summary>
    public bool AutomationWriteSelfTest()
    { ThrowIfDisposed(); return NativeMethods.AutomationWriteSelfTest(_handle) != 0; }

    // --- automation write / record (M9-C). deviceIndex < 0 = instrument. ---
    /// <summary>While on, a control gesture records into that parameter's lane. The
    /// transport record button drives this — <see cref="SetRecording"/> sets it too.</summary>
    public void SetAutomationRecord(bool on)
    { ThrowIfDisposed(); NativeMethods.SetAutomationRecord(_handle, on ? 1 : 0); }
    public bool AutomationRecording
    { get { ThrowIfDisposed(); return NativeMethods.AutomationRecord(_handle) != 0; } }
    /// <summary>Fires on every control gesture-begin so the arrangement can follow a touched knob.</summary>
    public event System.Action<int, AutomationTarget, int, int, string>? AutomationTouched;
    /// <summary><paramref name="latch"/> marks a hardware/MIDI control: it keeps writing
    /// past the release until the transport stops. Mouse gestures pass false.</summary>
    public void BeginAutomationWrite(int trackId, AutomationTarget target, int deviceIndex, int paramIndex, string paramId, bool latch = false)
    {
        ThrowIfDisposed();
        AutomationTouched?.Invoke(trackId, target, deviceIndex, paramIndex, paramId ?? "");
        NativeMethods.BeginAutomationWrite(_handle, trackId, (int)target, deviceIndex, paramIndex, paramId ?? "", latch ? 1 : 0);
    }
    public void EndAutomationWrite(int trackId, AutomationTarget target, int deviceIndex, int paramIndex, string paramId)
    { ThrowIfDisposed(); NativeMethods.EndAutomationWrite(_handle, trackId, (int)target, deviceIndex, paramIndex, paramId ?? ""); }
    /// <summary>Hand every lane the user took over by hand back to playback.</summary>
    public void ReenableAutomation()
    { ThrowIfDisposed(); NativeMethods.ReenableAutomation(_handle); }
    /// <summary>True while at least one lane is overridden by a hand-moved control.</summary>
    public bool AutomationOverridden
    { get { ThrowIfDisposed(); return NativeMethods.AutomationOverridden(_handle) != 0; } }

    public int AddAutomationLane(int trackId, AutomationTarget target, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.AddAutomationLane(_handle, trackId, (int)target, deviceIndex, paramIndex); }

    public int AutomationLaneCount(int trackId)
    { ThrowIfDisposed(); return NativeMethods.AutomationLaneCount(_handle, trackId); }

    public AutomationLaneInfo AutomationLaneInfo(int trackId, int laneIndex)
    {
        ThrowIfDisposed();
        Check(NativeMethods.AutomationLaneInfo(_handle, trackId, laneIndex,
            out int target, out int deviceIndex, out int paramIndex, out int pointCount));
        return new AutomationLaneInfo((AutomationTarget)target, deviceIndex, paramIndex, pointCount);
    }

    public AutomationPoint[] GetAutomationPoints(int trackId, int laneIndex)
    {
        ThrowIfDisposed();
        int count = NativeMethods.AutomationGetPoints(_handle, trackId, laneIndex, null, 0);
        if (count == 0) return Array.Empty<AutomationPoint>();
        var buf = new AutomationPoint[count];
        int written = NativeMethods.AutomationGetPoints(_handle, trackId, laneIndex, buf, count);
        return written == count ? buf : buf[..written];
    }

    public void SetAutomationPoints(int trackId, int laneIndex, AutomationPoint[] points)
    { ThrowIfDisposed(); Check(NativeMethods.AutomationSetPoints(_handle, trackId, laneIndex, points, points.Length)); }

    public void SetAutomationPointsLive(int trackId, int laneIndex, AutomationPoint[] points)
    { ThrowIfDisposed(); Check(NativeMethods.AutomationSetPointsLive(_handle, trackId, laneIndex, points, points.Length)); }

    public void RemoveAutomationLane(int trackId, int laneIndex)
    { ThrowIfDisposed(); Check(NativeMethods.RemoveAutomationLane(_handle, trackId, laneIndex)); }

    public AutomationPoint[] GetMasterVolumeAutomation()
    {
        ThrowIfDisposed();
        int count = NativeMethods.MasterVolumeAutomationCount(_handle);
        if (count == 0) return Array.Empty<AutomationPoint>();
        var buf = new AutomationPoint[count];
        int written = NativeMethods.MasterVolumeAutomationGet(_handle, buf, count);
        return written == count ? buf : buf[..written];
    }

    public void SetMasterVolumeAutomation(AutomationPoint[] points)
    { ThrowIfDisposed(); Check(NativeMethods.MasterVolumeAutomationSet(_handle, points, points.Length)); }
}
