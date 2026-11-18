// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Tempo reads. The tempo itself is the master track's Tempo automation lane (specs/099);
// these convert through the map the engine derives from it. Also the master pan (PR 2).

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    public bool IsTempoAutomated { get { ThrowIfDisposed(); return NativeMethods.TempoAutomated(_handle) != 0; } }
    public double BeatToSeconds(double beat) { ThrowIfDisposed(); return NativeMethods.TempoBeatToSeconds(_handle, beat); }
    public double SecondsToBeat(double seconds) { ThrowIfDisposed(); return NativeMethods.TempoSecondsToBeat(_handle, seconds); }
    public double TempoAt(double beat) { ThrowIfDisposed(); return NativeMethods.TempoBpmAt(_handle, beat); }
    /// <summary>Device-free engine self-test of the master Tempo lane → tempo map; 0 = pass.</summary>
    public int TempoLaneSelfTest() { ThrowIfDisposed(); return NativeMethods.TempoLaneSelfTest(_handle); }

    // Master pan (specs/099 PR 2).
    public void SetMasterPan(float pan) { ThrowIfDisposed(); Check(NativeMethods.SetMasterPan(_handle, pan)); }
    public float MasterPan { get { ThrowIfDisposed(); return NativeMethods.MasterPan(_handle); } }
    public int MasterAutomationSelfTest() { ThrowIfDisposed(); return NativeMethods.MasterAutomationSelfTest(_handle); }
}

internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_engine_tempo_automated")]
    internal static partial int TempoAutomated(IntPtr e);
    [LibraryImport(Lib, EntryPoint = "nota_engine_tempo_lane_selftest")]
    internal static partial int TempoLaneSelfTest(IntPtr e);
    [LibraryImport(Lib, EntryPoint = "nota_engine_set_master_pan")]
    internal static partial NotaResult SetMasterPan(IntPtr e, float pan);
    [LibraryImport(Lib, EntryPoint = "nota_engine_master_pan")]
    internal static partial float MasterPan(IntPtr e);
    [LibraryImport(Lib, EntryPoint = "nota_engine_master_automation_selftest")]
    internal static partial int MasterAutomationSelfTest(IntPtr e);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_beat_to_seconds")]
    internal static partial double TempoBeatToSeconds(IntPtr e, double beat);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_seconds_to_beat")]
    internal static partial double TempoSecondsToBeat(IntPtr e, double seconds);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_bpm_at")]
    internal static partial double TempoBpmAt(IntPtr e, double beat);
}
