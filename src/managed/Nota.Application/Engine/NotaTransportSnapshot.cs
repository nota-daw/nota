// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Application;

/// <summary>A coherent rendered block for display interpolation. Mirrors NotaTransportSnapshot
/// in the C ABI; elapsed time is measured by the engine's monotonic clock.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaTransportSnapshot
{
    public double StartBeat;
    public double PositionBeats;
    public double Bpm;
    public double LoopStart;
    public double LoopEnd;
    public double DurationSeconds;
    public double ElapsedSeconds;
    public int Playing;
    public int Looping;

    public readonly bool IsPlaying => Playing != 0;
}
