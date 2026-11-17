// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// specs/099: an independent reading of the #568 tempo map (step / linear / cubic-Bezier
// segments, incoming tempo at an anchor), integrated finely, so the migration tests compare
// against what the old build played rather than against the converter itself.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LegacyTempoReference
{
    public static double Bpm(IReadOnlyList<TempoMigration.LegacyPoint> p, double beat)
    {
        int i = p.Count - 1;
        while (i > 0 && p[i].Beat > beat) i--;
        var a = p[i];
        if (i + 1 >= p.Count || a.Shape == 0) return a.Bpm;
        var b = p[i + 1];
        double end = b.IncomingBpm > 0 ? b.IncomingBpm : b.Bpm;
        double x = Math.Clamp((beat - a.Beat) / (b.Beat - a.Beat), 0, 1);
        if (a.Shape == 2)
        {
            double lo = 0, hi = 1;
            for (int k = 0; k < 60; k++)
            {
                double t = (lo + hi) / 2;
                if (Bez(t, a.X1, a.X2) < x) lo = t; else hi = t;
            }
            x = Bez((lo + hi) / 2, a.Y1, a.Y2);
        }
        return a.Bpm + (end - a.Bpm) * x;
    }

    private static double Bez(double t, double c1, double c2)
    {
        double u = 1 - t;
        return 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t;
    }

    /// <summary>Seconds at <paramref name="beat"/>, integrated per segment (Simpson, 4096 steps).</summary>
    public static double Seconds(IReadOnlyList<TempoMigration.LegacyPoint> p, double beat)
    {
        double total = 0;
        for (int i = 0; i < p.Count; i++)
        {
            double l = p[i].Beat;
            if (l >= beat) break;
            double r = i + 1 < p.Count ? Math.Min(p[i + 1].Beat, beat) : beat;
            if (i + 1 >= p.Count || p[i].Shape == 0) { total += (r - l) * 60 / p[i].Bpm; continue; }
            const int n = 4096;
            double h = (r - l) / n, sum = 60 / Bpm(p, l) + 60 / Bpm(p, r - 1e-12);
            for (int k = 1; k < n; k++) sum += (k % 2 == 1 ? 4 : 2) * 60 / Bpm(p, l + k * h);
            total += sum * h / 3;
        }
        return total;
    }
}
