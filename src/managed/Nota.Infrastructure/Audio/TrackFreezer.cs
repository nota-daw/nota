// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System;
using Nota.Application;

namespace Nota.Infrastructure;

/// <summary>Drives an offline track freeze (M7): stops the audio backend for exclusive
/// engine access, bounces the track's pre-fader chain into its freeze buffer, then
/// restarts the backend. Mirrors <see cref="WavAudioExporter"/>'s stop/render/restore
/// shape; the caller suspends the UI clock first.</summary>
public sealed class TrackFreezer
{
    private const int Chunk = 8192; // must stay <= engine kMaxBlock

    /// <summary>Freezes <paramref name="trackId"/> over [0, <paramref name="lengthBeats"/>].
    /// Returns false when the engine can't freeze the track (empty/unsupported).</summary>
    public bool Freeze(IAudioEngine engine, int trackId, double lengthBeats,
                       bool restoreLoop, bool restoreMetronome, IProgress<double>? progress = null)
    {
        double restoreBeat = engine.PositionBeats;
        double loopStart = engine.LoopStart, loopEnd = engine.LoopEnd; // preserve the loop region

        engine.Stop();               // halt the audio backend: no audio thread during capture
        engine.SetLoop(false, 0, 0); // capture the whole range once, no wrap
        engine.SetMetronome(false);  // keep clicks out of the freeze
        engine.StopTransport();

        long total = engine.BeginFreeze(trackId, lengthBeats);
        if (total <= 0) { Restore(engine, restoreLoop, restoreMetronome, loopStart, loopEnd, restoreBeat); return false; }

        try
        {
            engine.StopTransport();
            engine.Seek(0);
            engine.Play();            // audio/instrument content only renders while playing

            var buf = new float[Chunk * 2]; // discarded: we only want the per-track capture
            long remaining = total, done = 0;
            int lastPct = -1;
            while (remaining > 0)
            {
                int m = (int)Math.Min(Chunk, remaining);
                engine.RenderOffline(buf, m); // capture happens inside mixGraph at the device rate
                remaining -= m; done += m;
                if (progress is not null && total > 0)
                {
                    int pct = (int)(100.0 * done / total);
                    if (pct != lastPct) { lastPct = pct; progress.Report((double)done / total); }
                }
            }
            engine.StopTransport();
            engine.EndFreeze(trackId);
        }
        catch
        {
            engine.CancelFreeze();
            Restore(engine, restoreLoop, restoreMetronome, loopStart, loopEnd, restoreBeat);
            throw;
        }

        Restore(engine, restoreLoop, restoreMetronome, loopStart, loopEnd, restoreBeat);
        progress?.Report(1.0);
        return true;
    }

    private static void Restore(IAudioEngine engine, bool loop, bool metronome,
                                double loopStart, double loopEnd, double beat)
    {
        engine.StopTransport();
        engine.Seek(beat);
        engine.SetLoop(loop, loopStart, loopEnd);
        engine.SetMetronome(metronome);
        engine.Start();              // resume live audio at the device sample rate
    }
}
