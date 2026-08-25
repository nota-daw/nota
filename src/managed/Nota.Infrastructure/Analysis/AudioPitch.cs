// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Monophonic pitch detection for "Convert Melody to New MIDI Track". A compact YIN
// estimator (de Cheveigné & Kawahara) — pure, permissive, no deps. MVP quality:
// meant for clean single-note lines / vocals.

using System;

namespace Nota.Infrastructure;

public static class AudioPitch
{
    /// <summary>Estimated fundamental (Hz) of <paramref name="x"/>[start .. start+win), or 0 when
    /// unvoiced. Needs <c>start + win + sr/minHz</c> samples available.</summary>
    public static double YinHz(float[] x, int start, int win, double sr,
                               double threshold = 0.15, double minHz = 65, double maxHz = 1100)
    {
        int maxTau = (int)(sr / minHz);
        int minTau = Math.Max(2, (int)(sr / maxHz));
        if (x is null || start < 0 || win < 4 || start + win + maxTau > x.Length || maxTau <= minTau + 1) return 0;

        // Difference function d(tau), then cumulative-mean-normalised d'(tau).
        var dn = new double[maxTau];
        dn[0] = 1.0;
        double running = 0.0;
        for (int tau = 1; tau < maxTau; tau++)
        {
            double sum = 0.0;
            for (int j = 0; j < win; j++)
            {
                double diff = x[start + j] - x[start + j + tau];
                sum += diff * diff;
            }
            running += sum;
            dn[tau] = running > 1e-12 ? sum * tau / running : 1.0;
        }

        // First tau below the absolute threshold that is a local minimum; else the global min.
        int best = -1;
        for (int tau = minTau; tau < maxTau - 1; tau++)
        {
            if (dn[tau] < threshold)
            {
                while (tau + 1 < maxTau && dn[tau + 1] < dn[tau]) tau++;
                best = tau;
                break;
            }
        }
        if (best < 0)
        {
            double lo = double.MaxValue;
            for (int tau = minTau; tau < maxTau; tau++) if (dn[tau] < lo) { lo = dn[tau]; best = tau; }
            if (best < 0 || lo > 0.6) return 0;   // no confident pitch
        }

        // Parabolic interpolation around the chosen lag for sub-sample precision.
        double betterTau = best;
        if (best > 0 && best < maxTau - 1)
        {
            double s0 = dn[best - 1], s1 = dn[best], s2 = dn[best + 1];
            double denom = 2.0 * (2.0 * s1 - s2 - s0);
            if (Math.Abs(denom) > 1e-12) betterTau = best + (s2 - s0) / denom;
        }
        return betterTau > 0 ? sr / betterTau : 0;
    }
}
