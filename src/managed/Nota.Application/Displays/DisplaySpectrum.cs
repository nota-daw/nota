// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// An instrument's output as 64 log bands for its LED display (specs/114): the same tap the
// desktop filter graph reads (IAudioEngine.InstrumentOutput, which keeps itself on for about a
// second after each read), Hann-windowed, FFT'd and smoothed so it rises quickly and falls slowly.
// Coarser than the card's (64 bands for 128 columns) — a dot matrix shows no more.

namespace Nota.Application.Displays;

internal sealed class DisplaySpectrum
{
    public const int N = 2048, BandCount = 64;
    private const double FMin = 20, FMax = 20000;
    private readonly float[] _buf = new float[4096];
    private readonly double[] _re = new double[N], _im = new double[N];
    private static readonly double[] Hann = BuildHann();
    public double[] Bands { get; } = Enumerable.Repeat(-120.0, BandCount).ToArray();

    private static double[] BuildHann()
    {
        var w = new double[N];
        for (int i = 0; i < N; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1));
        return w;
    }

    /// <summary>Read the tap and analyse; false (bands falling) when there is nothing to read.</summary>
    public bool Update(IAudioEngine e, int trackId)
    {
        int n = e.InstrumentOutput(trackId, _buf);
        if (n < N)
        {
            for (int b = 0; b < BandCount; b++) Bands[b] = Math.Max(-120, Bands[b] - 3);
            return false;
        }
        int off = n - N;
        for (int i = 0; i < N; i++) { _re[i] = _buf[off + i] * Hann[i]; _im[i] = 0; }
        Fft(_re, _im);
        double sr = e.SampleRate > 0 ? e.SampleRate : 48000;
        double binHz = sr / N;
        for (int b = 0; b < BandCount; b++)
        {
            double f0 = FMin * Math.Pow(FMax / FMin, (double)b / BandCount), f1 = FMin * Math.Pow(FMax / FMin, (double)(b + 1) / BandCount);
            int k0 = Math.Clamp((int)(f0 / binHz), 1, N / 2 - 1), k1 = Math.Clamp((int)Math.Ceiling(f1 / binHz), k0 + 1, N / 2);
            double peak = 0;
            for (int k = k0; k < k1; k++) peak = Math.Max(peak, _re[k] * _re[k] + _im[k] * _im[k]);
            double db = peak > 1e-20 ? 10 * Math.Log10(peak) - 20 * Math.Log10(N / 4.0) : -120;
            Bands[b] = db > Bands[b] ? db : Math.Max(db, Bands[b] - 2.5);
        }
        return true;
    }

    // In-place radix-2 FFT.
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }
}
