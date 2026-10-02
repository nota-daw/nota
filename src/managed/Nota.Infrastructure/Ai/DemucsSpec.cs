// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The STFT / iSTFT that htdemucs runs around its network. The ONNX export leaves them out
// (ONNX has no complex STFT), so they are reproduced here exactly as HTDemucs._spec / _ispec
// and demucs/spec.py compute them: a 4096-point periodic Hann window, hop 1024, torch's
// "normalized" scaling (1/√N each way), centre padding by reflection, plus htdemucs's own
// reflect pad of 1.5 hops so the frame count is exactly ceil(length / hop). Spectra are
// bin-major: [bin * frames + frame], 2048 bins (the Nyquist bin is dropped, as htdemucs does).

namespace Nota.Infrastructure;

internal static class DemucsSpec
{
    public const int NFft = 4096, Hop = 1024, Bins = NFft / 2;
    private const int Pad = Hop / 2 * 3;           // htdemucs's reflect pad around the signal
    private static readonly double Norm = 1.0 / Math.Sqrt(NFft);
    private static readonly double[] Window = MakeWindow();

    private static double[] MakeWindow()
    {
        var w = new double[NFft];
        for (int n = 0; n < NFft; n++) w[n] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / NFft);   // periodic, as torch.hann_window
        return w;
    }

    /// <summary>Frames the spectrum of a <paramref name="length"/>-sample signal has.</summary>
    public static int Frames(int length) => (length + Hop - 1) / Hop;

    /// <summary>Spectrum of one channel into <paramref name="re"/> / <paramref name="im"/>
    /// (each <see cref="Bins"/> × <see cref="Frames"/>).</summary>
    public static void Forward(ReadOnlySpan<float> x, float[] re, float[] im)
    {
        int len = x.Length, le = Frames(len);
        int p1 = le * Hop + 2 * Pad;               // after htdemucs's pad (pad, pad + le*hop - len)
        var fr = new double[NFft];
        var fi = new double[NFft];
        for (int t = 0; t < le; t++)
        {
            // Kept frame t is STFT frame t + 2; STFT frames start at hop multiples of the
            // centre-padded signal (NFft/2 of reflection either side of the p1-long one).
            int start = (t + 2) * Hop - NFft / 2;
            for (int n = 0; n < NFft; n++)
            {
                int i = Reflect(start + n, p1) - Pad;
                fr[n] = x[Reflect(i, len)] * Window[n];
                fi[n] = 0;
            }
            AudioFft.Forward(fr, fi);
            for (int k = 0; k < Bins; k++)
            {
                re[k * le + t] = (float)(fr[k] * Norm);
                im[k * le + t] = (float)(fi[k] * Norm);
            }
        }
    }

    /// <summary>Signal of <paramref name="length"/> samples back from a spectrum of
    /// <see cref="Frames"/>(<paramref name="length"/>) frames (its planes start at the given
    /// offsets), into <paramref name="dst"/>.</summary>
    public static void Inverse(float[] re, float[] im, int reOffset, int imOffset, int length, Span<float> dst)
    {
        int le = Frames(length);
        int frames = le + 4;                       // two silent frames either side, as _ispec pads
        int olaLen = NFft + Hop * (frames - 1);
        var ola = new double[olaLen];
        var env = new double[olaLen];
        var fr = new double[NFft];
        var fi = new double[NFft];
        double scale = Math.Sqrt(NFft);            // undoes the forward 1/√N ("normalized")
        for (int f = 0; f < frames; f++)
        {
            int at = f * Hop;
            for (int n = 0; n < NFft; n++) env[at + n] += Window[n] * Window[n];
            int t = f - 2;
            if (t < 0 || t >= le) continue;        // a silent frame adds only to the envelope
            fr[0] = re[reOffset + t]; fi[0] = 0;   // irfft ignores the DC bin's imaginary part
            for (int k = 1; k < Bins; k++)
            {
                double a = re[reOffset + k * le + t], b = im[imOffset + k * le + t];
                fr[k] = a; fi[k] = b;
                fr[NFft - k] = a; fi[NFft - k] = -b;
            }
            fr[Bins] = 0; fi[Bins] = 0;            // the dropped Nyquist bin
            AudioFft.Inverse(fr, fi);
            for (int n = 0; n < NFft; n++) ola[at + n] += fr[n] * scale * Window[n];
        }
        // istft drops NFft/2 of centre padding; _ispec then drops its own 1.5-hop pad.
        int off = NFft / 2 + Pad;
        for (int i = 0; i < length; i++)
        {
            double e = env[off + i];
            dst[i] = e > 1e-11 ? (float)(ola[off + i] / e) : 0f;
        }
    }

    // numpy/torch "reflect" (edge sample not repeated) into [0, n).
    private static int Reflect(int i, int n)
    {
        if (n == 1) return 0;
        int period = 2 * (n - 1);
        i %= period;
        if (i < 0) i += period;
        return i < n ? i : period - i;
    }
}
