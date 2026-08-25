// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Offline audio analysis for the "Convert Drums / Slice to New MIDI Track" commands.
// Pure, dependency-free DSP (the dual license forbids GPL libraries): a time-domain
// energy-flux onset detector plus a coarse kick/snare/hat classifier. MVP quality —
// tuned for percussive / breakbeat material.

using System;
using System.Collections.Generic;

namespace Nota.Infrastructure;

public enum DrumClass { Kick, Snare, Hat }

public static class AudioOnsets
{
    /// <summary>Onset frame positions in <paramref name="mono"/> (source frames). Time-domain:
    /// a short-window log-energy envelope, positive first difference (onset strength), then an
    /// adaptive-threshold peak-pick with a refractory gap.</summary>
    public static List<int> Detect(float[] mono, double sr)
    {
        var onsets = new List<int>();
        if (mono is null || mono.Length == 0 || sr <= 0) return onsets;

        int hop = Math.Max(1, (int)(sr * 0.005));    // 5 ms
        int win = Math.Max(hop, (int)(sr * 0.020));  // 20 ms
        int frames = (mono.Length - win) / hop;
        if (frames <= 2) { if (mono.Length > 0) onsets.Add(0); return onsets; }

        var env = new double[frames];
        for (int i = 0; i < frames; i++)
        {
            int start = i * hop;
            double e = 0;
            for (int j = 0; j < win; j++) { float s = mono[start + j]; e += (double)s * s; }
            env[i] = Math.Log(1e-9 + e / win);
        }

        var flux = new double[frames];
        for (int i = 1; i < frames; i++) flux[i] = Math.Max(0.0, env[i] - env[i - 1]);

        int refr = Math.Max(1, (int)(0.050 * sr / hop));   // 50 ms between onsets
        int mw = Math.Max(3, (int)(0.120 * sr / hop));     // ~120 ms local-mean window
        int last = -refr;
        for (int i = 1; i < frames - 1; i++)
        {
            double mean = 0; int c = 0;
            for (int k = Math.Max(0, i - mw); k <= Math.Min(frames - 1, i + mw); k++) { mean += flux[k]; c++; }
            mean /= Math.Max(1, c);
            double thr = mean + 0.20;   // margin in log-energy units
            if (flux[i] > thr && flux[i] >= flux[i - 1] && flux[i] > flux[i + 1] && (i - last) >= refr)
            {
                onsets.Add(i * hop);
                last = i;
            }
        }
        return onsets;
    }

    /// <summary>Peak level (0..1) just after an onset — used for note velocity.</summary>
    public static float Strength(float[] mono, double sr, int onsetFrame)
    {
        int n = Math.Max(1, (int)(sr * 0.030));
        float peak = 0f;
        for (int i = onsetFrame; i < Math.Min(mono.Length, onsetFrame + n); i++)
            peak = Math.Max(peak, Math.Abs(mono[i]));
        return Math.Clamp(peak, 0f, 1f);
    }

    /// <summary>Coarse kick/snare/hat guess from a short window after the onset, using a low-band
    /// energy ratio (one-pole ~150 Hz low-pass) and the zero-crossing rate. Heuristic (MVP).</summary>
    public static DrumClass Classify(float[] mono, double sr, int onsetFrame)
    {
        int n = Math.Max(64, (int)(sr * 0.040));   // 40 ms
        int end = Math.Min(mono.Length, onsetFrame + n);
        if (end - onsetFrame < 8) return DrumClass.Snare;

        double a = 1.0 - Math.Exp(-2.0 * Math.PI * 150.0 / sr);   // one-pole LP coefficient @150 Hz
        double lp = 0, lowE = 0, totalE = 0;
        int zc = 0; float prev = 0;
        for (int i = onsetFrame; i < end; i++)
        {
            float x = mono[i];
            lp += a * (x - lp);
            lowE += lp * lp;
            totalE += (double)x * x;
            if ((x >= 0f) != (prev >= 0f)) zc++;
            prev = x;
        }
        int len = end - onsetFrame;
        double lowRatio = totalE > 1e-12 ? lowE / totalE : 0;
        double zcr = (double)zc / len;   // sign changes per sample

        if (zcr > 0.18) return DrumClass.Hat;                 // bright / noisy
        if (lowRatio > 0.55 && zcr < 0.06) return DrumClass.Kick;   // low-dominant, few crossings
        return DrumClass.Snare;
    }
}
