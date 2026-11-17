// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// specs/099: the #568 tempo track stored its own points (step / linear / cubic-Bezier
// segments). Tempo is now the master track's Tempo automation lane, whose segments follow
// the ordinary automation curve. This converts the old points into lane points that keep
// every beat at the same time: step and linear exactly, a Bezier segment as automation-curve
// pieces fitted so each piece lasts exactly as long as the original, split until no beat
// inside a piece is off by more than 0.1 ms.

using System;
using System.Collections.Generic;
using Nota.Application.Editing;

namespace Nota.Infrastructure;

public static class TempoMigration
{
    /// <summary>A #568 tempo point: shape 0 step, 1 linear, 2 cubic Bezier with handles
    /// (x1, y1, x2, y2); incomingBpm (0 = same as bpm) is the tempo arriving at this point.</summary>
    public readonly record struct LegacyPoint(double Beat, double Bpm, int Shape = 0,
        double X1 = 1.0 / 3, double Y1 = 0, double X2 = 2.0 / 3, double Y2 = 1, double IncomingBpm = 0);

    public readonly record struct LanePoint(double Beat, float Value, float Curve);

    public const double MinBpm = 20, MaxBpm = 300;
    private const double MaxPieceErrorSeconds = 1e-4;
    private const int MaxSplitDepth = 6; // ≤ 64 pieces per segment
    /// <summary>The most distinct beats a tempo lane may hold: the engine's TempoMap::kMaxPoints.
    /// A lane past it is rejected, and the project would not open.</summary>
    public const int MaxMapPoints = 4096;

    /// <summary>Lane points for <paramref name="legacy"/>; <paramref name="clamped"/> is true when a
    /// tempo had to be limited to 20–300 BPM. Empty for an invalid map (the project then keeps
    /// its single tempo).</summary>
    public static List<LanePoint> Convert(IReadOnlyList<LegacyPoint> legacy, out bool clamped)
    {
        clamped = false;
        if (legacy.Count == 0 || !Valid(legacy)) return new List<LanePoint>();
        // Many curved segments, each split up to 64 ways, can pass the engine's point limit:
        // split less finely until the lane fits. Unsplit, a lane has one beat per old point.
        var fits = new Dictionary<(double, double), (float Curve, bool Split)>();
        for (int depth = MaxSplitDepth; depth >= 0; depth--)
        {
            var lane = Convert(legacy, depth, fits, out clamped);
            if (DistinctBeats(lane) <= MaxMapPoints) return lane;
        }
        clamped = false;
        return new List<LanePoint>();
    }

    /// <summary>Distinct beats in <paramref name="lane"/>, counted as the engine counts its map
    /// points: a same-beat pair is one point, and a lane starting after beat 0 gains one there.</summary>
    public static int DistinctBeats(IReadOnlyList<LanePoint> lane)
    {
        int n = 0;
        for (int i = 0; i < lane.Count; i++)
            if (i == 0 || lane[i].Beat - lane[i - 1].Beat >= 1e-6) n++;
        if (lane.Count > 0 && lane[0].Beat > 0) n++;
        return n;
    }

    private static List<LanePoint> Convert(IReadOnlyList<LegacyPoint> legacy, int maxDepth,
        Dictionary<(double, double), (float Curve, bool Split)> fits, out bool clamped)
    {
        var lane = new List<LanePoint>();
        bool anyClamped = false;
        float C(double bpm)
        {
            double v = Math.Clamp(bpm, MinBpm, MaxBpm);
            if (v != bpm) anyClamped = true;
            return (float)v;
        }

        double arriving = legacy[0].Bpm;
        for (int i = 0; i < legacy.Count; i++)
        {
            var a = legacy[i];
            if (i > 0 && Math.Abs(arriving - a.Bpm) > 1e-9)
                lane.Add(new LanePoint(a.Beat, C(arriving), 0));   // the tempo arriving, then the jump
            if (i + 1 == legacy.Count) { lane.Add(new LanePoint(a.Beat, C(a.Bpm), 0)); break; }
            var b = legacy[i + 1];
            double end = a.Shape == 0 ? a.Bpm : b.IncomingBpm > 0 ? b.IncomingBpm : b.Bpm;
            arriving = end;
            if (a.Shape != 2)
            {
                lane.Add(new LanePoint(a.Beat, C(a.Bpm), 0));      // step holds, linear ramps (curve 0)
                continue;
            }
            Func<double, double> f = beat => BezierBpm(a, end, (beat - a.Beat) / (b.Beat - a.Beat));
            Fit(f, a.Beat, b.Beat, 0, maxDepth, fits, lane, C);
        }
        clamped = anyClamped;
        return lane;
    }

    private static bool Valid(IReadOnlyList<LegacyPoint> p)
    {
        if (p[0].Beat != 0) return false;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            if (!double.IsFinite(a.Beat) || !double.IsFinite(a.Bpm) || a.Bpm <= 0 || a.Shape is < 0 or > 2) return false;
            if (!double.IsFinite(a.IncomingBpm) || a.IncomingBpm < 0) return false;
            if (i > 0 && a.Beat - p[i - 1].Beat < 1e-6) return false;
            foreach (double h in new[] { a.X1, a.Y1, a.X2, a.Y2 })
                if (!double.IsFinite(h) || h < 0 || h > 1) return false;
        }
        return true;
    }

    // The #568 curve: x is the segment fraction; a cubic Bezier through (0,0), (x1,y1), (x2,y2), (1,1).
    private static double BezierBpm(LegacyPoint a, double end, double x)
    {
        x = Math.Clamp(x, 0, 1);
        double lo = 0, hi = 1;
        for (int i = 0; i < 48; i++)
        {
            double t = (lo + hi) * 0.5;
            if (Bez(t, a.X1, a.X2) < x) lo = t; else hi = t;
        }
        return a.Bpm + (end - a.Bpm) * Bez((lo + hi) * 0.5, a.Y1, a.Y2);
    }

    private static double Bez(double t, double c1, double c2)
    {
        double u = 1 - t;
        return 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t;
    }

    // Seconds from l to r under a tempo function (Simpson, n even).
    private static double Seconds(Func<double, double> bpm, double l, double r, int n = 256)
    {
        double h = (r - l) / n, sum = 60 / bpm(l) + 60 / bpm(r);
        for (int i = 1; i < n; i++) sum += (i % 2 == 1 ? 4 : 2) * 60 / bpm(l + i * h);
        return sum * h / 3;
    }

    private static void Fit(Func<double, double> f, double l, double r, int depth, int maxDepth,
        Dictionary<(double, double), (float Curve, bool Split)> fits, List<LanePoint> lane, Func<double, float> clampBpm)
    {
        float v0 = clampBpm(f(l)), v1 = clampBpm(f(r));
        // A piece's fit doesn't depend on how deep the split may go: a coarser retry reuses it.
        if (!fits.TryGetValue((l, r), out var known))
        {
            known = FitPiece(f, l, r, v0, v1);
            fits[(l, r)] = known;
        }
        if (known.Split && depth < maxDepth)
        {
            double m = (l + r) * 0.5;
            Fit(f, l, m, depth + 1, maxDepth, fits, lane, clampBpm);
            Fit(f, m, r, depth + 1, maxDepth, fits, lane, clampBpm);
            return;
        }
        lane.Add(new LanePoint(l, v0, known.Curve));
    }

    // The automation curve that lasts exactly as long as f over l..r, and whether some beat
    // inside is still off by more than MaxPieceErrorSeconds (so a finer split would help).
    private static (float Curve, bool Split) FitPiece(Func<double, double> f, double l, double r, float v0, float v1)
    {
        Func<double, double> target = x => Math.Clamp(f(x), MinBpm, MaxBpm);
        double want = Seconds(target, l, r);
        Func<float, Func<double, double>> piece = c => x => v0 + (v1 - v0) * AutomationCurve.Shape((x - l) / (r - l), c);
        // Time over the piece falls monotonically with the curve when the tempo rises (and the
        // other way round when it falls): bisect the curve that lasts exactly as long.
        float curve = 0;
        if (Math.Abs(v1 - v0) > 1e-9)
        {
            double lo = -1, hi = 1;
            bool rising = v1 > v0;
            for (int i = 0; i < 40; i++)
            {
                double mid = (lo + hi) * 0.5;
                double got = Seconds(piece((float)mid), l, r, 64);
                if ((got > want) == rising) lo = mid; else hi = mid;
            }
            curve = (float)((lo + hi) * 0.5);
        }
        var fit = piece(curve);
        double worst = 0;
        for (int i = 1; i < 16; i++)
        {
            double x = l + (r - l) * i / 16;
            worst = Math.Max(worst, Math.Abs(Seconds(target, l, x, 64) - Seconds(fit, l, x, 64)));
        }
        return (curve, worst > MaxPieceErrorSeconds);
    }
}
