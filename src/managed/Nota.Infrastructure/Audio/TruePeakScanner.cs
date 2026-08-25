// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Infrastructure;

/// <summary>Streaming true-peak estimator for export normalize. Tracks the largest
/// absolute inter-sample value across an interleaved-stereo render by 4×-oversampling
/// each channel with Catmull-Rom interpolation — this catches the reconstruction
/// overshoots between samples that a plain sample-peak scan misses, so normalizing to
/// the result keeps the DAC's true peak under target. Fed chunk-by-chunk; keeps a
/// 3-sample history per channel across chunk boundaries.</summary>
internal sealed class TruePeakScanner
{
    // Interpolate the segment [p1,p2] at these fractions (0 = the sample itself,
    // already covered by the raw-sample max below, so start at 1/4).
    private static readonly float[] Frac = { 0.25f, 0.5f, 0.75f };

    private float _l0, _l1, _l2;   // last three left samples (l2 = newest)
    private float _r0, _r1, _r2;
    private int _seen;             // samples fed so far (per channel)
    private float _peak;

    public float Peak => _peak;

    public void Feed(float[] interleaved, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            float l = interleaved[i * 2];
            float r = interleaved[i * 2 + 1];

            float al = l < 0 ? -l : l;
            if (al > _peak) _peak = al;
            float ar = r < 0 ? -r : r;
            if (ar > _peak) _peak = ar;

            // With a full 4-sample window (p0,p1,p2,p3) = (_l0,_l1,_l2,l), evaluate the
            // inter-sample peak of the interior segment [_l1,_l2].
            if (_seen >= 3)
            {
                Interp(_l0, _l1, _l2, l);
                Interp(_r0, _r1, _r2, r);
            }

            _l0 = _l1; _l1 = _l2; _l2 = l;
            _r0 = _r1; _r1 = _r2; _r2 = r;
            _seen++;
        }
    }

    private void Interp(float p0, float p1, float p2, float p3)
    {
        // Catmull-Rom coefficients (once per segment).
        float c0 = p1;
        float c1 = 0.5f * (p2 - p0);
        float c2 = p0 - 2.5f * p1 + 2f * p2 - 0.5f * p3;
        float c3 = 0.5f * (p3 - p0) + 1.5f * (p1 - p2);
        foreach (float t in Frac)
        {
            float y = ((c3 * t + c2) * t + c1) * t + c0;
            float a = y < 0 ? -y : y;
            if (a > _peak) _peak = a;
        }
    }
}
