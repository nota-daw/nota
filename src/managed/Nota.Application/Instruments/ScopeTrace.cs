// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Modular Scope node's view (specs/047): its saved settings (modulator fields 13..18),
// their labels, and how the engine's (value, time step) history is laid into the time
// window, free-running or held still on a rising edge. Pure, so the smoke test checks the
// same maths the node and its pop-out window draw.

using System;
using System.Globalization;

namespace Nota.Application;

public static class ScopeTrace
{
    public const int Kind = 6;   // ModulatorKind::Scope
    public const int FieldWindowMs = 13, FieldSync = 14, FieldWindowBeats = 15, FieldZoom = 16,
        FieldTrigger = 17, FieldHeight = 18;
    public const int HistoryLength = 8192;              // engine kScopeLen
    public const double MinWindowMs = 50, MaxWindowMs = 8000, DefaultWindowMs = 1000;
    public const double DefaultWindowBeats = 4;
    public const double MinHeight = 76, MaxHeight = 320;
    /// <summary>Vertical scale at 1×: ±1 CV with a little headroom.</summary>
    public const double Range = 1.15;

    /// <summary>The synced window steps, in beats (1/16 to 8 bars in 4/4).</summary>
    public static readonly float[] BeatSteps = { 0.25f, 0.5f, 1f, 2f, 4f, 8f, 16f, 32f };
    public static readonly int[] Zooms = { 1, 2, 4, 8 };

    /// <summary>The window in seconds: beats at the tempo when synced, else milliseconds.</summary>
    public static double WindowSeconds(bool sync, double ms, double beats, double bpm) =>
        sync ? beats * 60.0 / Math.Max(1.0, bpm) : Math.Clamp(ms, MinWindowMs, MaxWindowMs) / 1000.0;

    /// <summary>The window knob's position for a value (log in ms, stepped in beats).</summary>
    public static double MsToKnob(double ms) =>
        Math.Log(Math.Clamp(ms, MinWindowMs, MaxWindowMs) / MinWindowMs) / Math.Log(MaxWindowMs / MinWindowMs);
    public static double KnobToMs(double frac) => MinWindowMs * Math.Pow(MaxWindowMs / MinWindowMs, Math.Clamp(frac, 0, 1));
    public static int BeatIndex(double beats)
    {
        int best = 0;
        for (int i = 1; i < BeatSteps.Length; i++)
            if (Math.Abs(BeatSteps[i] - beats) < Math.Abs(BeatSteps[best] - beats)) best = i;
        return best;
    }
    public static int ZoomIndex(double zoom) => zoom >= 6 ? 3 : zoom >= 3 ? 2 : zoom >= 1.5 ? 1 : 0;

    public static string MsLabel(double ms) => ms >= 1000
        ? (ms / 1000).ToString(ms >= 10000 ? "0" : "0.0#", CultureInfo.InvariantCulture) + " s"
        : ms.ToString("0", CultureInfo.InvariantCulture) + " ms";

    public static string BeatsLabel(double beats)
    {
        if (beats >= 4) { int bars = (int)Math.Round(beats / 4); return bars == 1 ? "1 bar" : $"{bars} bars"; }
        return "1/" + (int)Math.Round(4 / Math.Max(0.25, beats));
    }

    public static string ZoomLabel(int zoom) => zoom.ToString(CultureInfo.InvariantCulture) + "×";

    /// <summary>
    /// Lay the history (oldest → newest; <paramref name="dt"/>[i] = seconds from entry i-1 to i)
    /// into a window of <paramref name="window"/> seconds. Writes each point's x (0 = left edge,
    /// 1 = right; the first point may sit just left of 0 so the line reaches the edge) and value,
    /// and returns the count. Free running, the newest value is at the right. With
    /// <paramref name="trigger"/>, the trace starts at the latest upward crossing of 0 that has
    /// a full window after it (interpolated between blocks), so a periodic signal holds still;
    /// <paramref name="triggered"/> says whether one was found (if not, it runs free).
    /// </summary>
    public static int Layout(ReadOnlySpan<float> v, ReadOnlySpan<float> dt, double window, bool trigger,
        Span<double> xs, Span<float> ys, out bool triggered)
    {
        triggered = false;
        int n = Math.Min(v.Length, dt.Length);
        if (n < 2 || window <= 0) return 0;
        int cap = Math.Min(xs.Length, ys.Length);

        // age[i] = seconds from entry i to the newest entry, walked back on the fly.
        if (trigger)
        {
            double ageI = 0;   // age of entry i
            for (int i = n - 1; i >= 1; i--)
            {
                double agePrev = ageI + dt[i];
                if (v[i - 1] < 0 && v[i] >= 0)
                {
                    double frac = -v[i - 1] / (double)(v[i] - v[i - 1]);
                    double ageCross = agePrev - frac * dt[i];
                    if (ageCross >= window)
                    {
                        triggered = true;
                        int count = 0;
                        double age = agePrev;
                        for (int j = i - 1; j < n && count < cap; j++)
                        {
                            if (j > i - 1) age -= dt[j];
                            double x = (ageCross - age) / window;
                            xs[count] = x; ys[count] = v[j]; count++;
                            if (x >= 1) break;
                        }
                        return count;
                    }
                }
                ageI = agePrev;
            }
        }

        // Free running: walk back to the window's left edge (one point past it).
        int start = n - 1;
        double a = 0;
        while (start > 0 && a < window) { a += dt[start]; start--; }
        int written = 0;
        double ageJ = a;
        for (int j = start; j < n && written < cap; j++)
        {
            if (j > start) ageJ -= dt[j];
            xs[written] = 1 - ageJ / window; ys[written] = v[j]; written++;
        }
        return written;
    }
}
