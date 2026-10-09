// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Tap tempo: the tempo from a series of taps. The estimate is the mean interval over the
// last eight gaps (first-to-last span / gap count, so one late tap shifts it once, not
// twice). A pause longer than a 20 BPM beat starts a new series; a gap far off the running
// mean means the player changed tempo, so the series restarts from the previous tap; a
// gap shorter than a 300 BPM sixteenth is a switch bounce and is ignored.

using System;
using System.Collections.Generic;

namespace Nota.Application;

public sealed class TapTempo
{
    public const double MinBpm = 20, MaxBpm = 300;
    private const int MaxGaps = 8;
    private const double ResetMs = 60000.0 / MinBpm;   // 3 s: a longer pause is a new series
    private const double BounceMs = 50;
    private const double Deviation = 0.25;              // ±25 % off the mean = a new tempo

    private readonly List<double> _taps = new();

    /// <summary>Taps in the current series (1 after the first tap, 0 when idle).</summary>
    public int Count => _taps.Count;

    public void Reset() => _taps.Clear();

    /// <summary>Registers a tap at <paramref name="nowMs"/> (any monotonic clock, in ms).
    /// Returns the tempo once there are two taps, else null. Whole BPM: a tapped series
    /// isn't accurate past that, and a round number is what the player meant.</summary>
    public double? Tap(double nowMs)
    {
        if (_taps.Count > 0)
        {
            double gap = nowMs - _taps[^1];
            if (gap < 0 || gap > ResetMs) _taps.Clear();
            else if (gap < BounceMs) return Estimate();
            else if (_taps.Count >= 3)
            {
                double mean = (_taps[^1] - _taps[0]) / (_taps.Count - 1);
                if (Math.Abs(gap - mean) > mean * Deviation) _taps.RemoveRange(0, _taps.Count - 1);
            }
        }
        _taps.Add(nowMs);
        if (_taps.Count > MaxGaps + 1) _taps.RemoveAt(0);
        return Estimate();
    }

    private double? Estimate()
    {
        if (_taps.Count < 2) return null;
        double mean = (_taps[^1] - _taps[0]) / (_taps.Count - 1);
        return Math.Clamp(Math.Round(60000.0 / mean), MinBpm, MaxBpm);
    }
}
