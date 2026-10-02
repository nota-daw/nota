// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Offline sample-rate conversion for the AI models (htdemucs wants 44.1 kHz, basic-pitch
// 22.05 kHz): a Kaiser-windowed sinc, 32 zero crossings each side, its cutoff lowered to the
// target's Nyquist when downsampling. Not real-time — it runs once over a clip.

namespace Nota.Infrastructure;

internal static class Resampler
{
    private const int Zeros = 32;
    private const double Beta = 8.6;               // ~ -90 dB stop band
    private const int TableRes = 512;              // kernel samples per zero crossing
    private static readonly double[] Kernel = MakeKernel();   // Kaiser × sinc over [0, Zeros)

    /// <summary><paramref name="src"/> (one channel at <paramref name="fromRate"/>) at
    /// <paramref name="toRate"/>. Returns the input itself when the rates match.</summary>
    public static float[] Convert(float[] src, double fromRate, double toRate, CancellationToken ct = default)
    {
        if (Math.Abs(fromRate - toRate) < 1e-6 || src.Length == 0) return src;
        double ratio = toRate / fromRate;
        double cutoff = Math.Min(1.0, ratio) * 0.97;   // a little below Nyquist, inside the transition band
        int half = (int)Math.Ceiling(Zeros / cutoff);
        int outLen = (int)Math.Round(src.Length * ratio);
        var dst = new float[outLen];
        for (int j = 0; j < outLen; j++)
        {
            if ((j & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            double pos = j / ratio;
            int centre = (int)Math.Floor(pos);
            double acc = 0, wsum = 0;
            for (int k = centre - half + 1; k <= centre + half; k++)
            {
                double d = (pos - k) * cutoff;         // distance in zero crossings of the filter
                double w = Tap(Math.Abs(d));
                if (w == 0) continue;
                wsum += w;
                if (k >= 0 && k < src.Length) acc += src[k] * w;
            }
            dst[j] = wsum != 0 ? (float)(acc / wsum) : 0f;   // unity DC gain at every phase
        }
        return dst;
    }

    private static double Tap(double ad)
    {
        double x = ad * TableRes;
        int i = (int)x;
        if (i >= Kernel.Length - 1) return 0;
        double f = x - i;
        return Kernel[i] + (Kernel[i + 1] - Kernel[i]) * f;
    }

    private static double[] MakeKernel()
    {
        int n = Zeros * TableRes + 2;
        var t = new double[n];
        double i0b = BesselI0(Beta);
        for (int i = 0; i < n; i++)
        {
            double d = (double)i / TableRes;
            double r = Math.Min(1.0, d / Zeros);
            double sinc = i == 0 ? 1.0 : Math.Sin(Math.PI * d) / (Math.PI * d);
            t[i] = BesselI0(Beta * Math.Sqrt(1 - r * r)) / i0b * sinc;
        }
        return t;
    }

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1, q = x * x / 4;
        for (int k = 1; k < 50; k++)
        {
            term *= q / (k * k);
            sum += term;
            if (term < sum * 1e-12) break;
        }
        return sum;
    }
}
