// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Minimal in-house radix-2 FFT (iterative Cooley–Tukey) for offline spectral
// analysis (Convert Harmony, the stem separator's STFT). Pure, permissive — no external DSP
// dependency.

using System;

namespace Nota.Infrastructure;

public static class AudioFft
{
    /// <summary>In-place forward FFT. <paramref name="re"/> / <paramref name="im"/> must be the
    /// same length and a power of two.</summary>
    public static void Forward(double[] re, double[] im)
    {
        int n = re.Length;
        if (n < 2 || (n & (n - 1)) != 0 || im.Length != n) return;

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cwr = 1.0, cwi = 0.0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = cwr * re[b] - cwi * im[b];
                    double ti = cwr * im[b] + cwi * re[b];
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncwr = cwr * wr - cwi * wi;
                    cwi = cwr * wi + cwi * wr; cwr = ncwr;
                }
            }
        }
    }

    /// <summary>In-place inverse FFT, scaled by 1/n (so Inverse(Forward(x)) == x).</summary>
    public static void Inverse(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 0; i < n; i++) im[i] = -im[i];
        Forward(re, im);
        double s = 1.0 / n;
        for (int i = 0; i < n; i++) { re[i] *= s; im[i] = -im[i] * s; }
    }
}
