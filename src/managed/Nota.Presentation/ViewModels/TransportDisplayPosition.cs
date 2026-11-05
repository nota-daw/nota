// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application;

namespace Nota.Presentation;

/// <summary>Places a display cursor within the last rendered block (AR-9). It never
/// runs past that block if audio stalls, and its result is never an editing position.</summary>
public static class TransportDisplayPosition
{
    public static double At(in NotaTransportSnapshot snapshot)
    {
        if (!snapshot.IsPlaying || snapshot.DurationSeconds <= 0 || snapshot.Bpm <= 0
            || !double.IsFinite(snapshot.ElapsedSeconds)) return snapshot.PositionBeats;

        // Use the exact endpoint once the block has elapsed, including rounding at a seam.
        double beat = snapshot.ElapsedSeconds >= snapshot.DurationSeconds
            ? snapshot.PositionBeats
            : snapshot.StartBeat + Math.Max(0, snapshot.ElapsedSeconds) * snapshot.Bpm / 60;
        if (snapshot.Looping != 0 && snapshot.LoopEnd > snapshot.LoopStart && beat >= snapshot.LoopEnd)
            beat = snapshot.LoopStart + (beat - snapshot.LoopStart) % (snapshot.LoopEnd - snapshot.LoopStart);
        return beat;
    }
}
