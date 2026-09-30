// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Polyphonic pitch estimation for "Convert Harmony to New MIDI Track". Pure, permissive DSP
// (in-house FFT), in four stages:
//   1. Tuning — the recording's offset from A=440 (weighted circular mean of peak cents), so a
//      slightly sharp/flat source doesn't flip between neighbouring semitones.
//   2. Per frame, iterative estimate-and-cancel (after Klapuri): score every pitch by a weighted
//      sum of its harmonics, take the best, subtract its (smoothed) partials, repeat. A harmonic
//      of a chosen note no longer reads as its own note, and missing-fundamental ghosts are
//      rejected.
//   3. Per pitch, hysteresis segmentation: a note starts on a strong frame and sustains through
//      weak ones; a median filter + gap bridging + minimum length kill flicker fragments. A note
//      must also sit in tune on average: glides, and the smeared partial clouds of chorused /
//      detuned-unison / shimmer sounds, wander 25–50 cents off-centre, real notes stay within ~15.
//   4. Starts snap to detected onsets (the long STFT window smears attacks), and a clear level
//      jump at an onset inside a held note splits it into a re-strike. Only onsets with a real
//      energy rise count — the drum-tuned detector also fires on chorus/tremolo pulsing.
// Still approximate — best on sustained chords / pads.

using System;
using System.Collections.Generic;

namespace Nota.Infrastructure;

/// <summary>A detected note: pitch + [start, end) in source samples + 0..1 velocity.</summary>
public readonly record struct MidiNoteSpan(int Pitch, int StartSample, int EndSample, float Velocity);

public static class AudioHarmony
{
    const int LowPitch = 28, HighPitch = 96;   // ~E1 … C7
    const int Pitches = HighPitch - LowPitch + 1;
    const int MaxVoices = 6, Harmonics = 10;
    const double MaxPartialHz = 5000;
    const double StableCents = 20;   // max mean |cents off| over a note's frames

    /// <param name="minNoteSec">Shortest note kept (shorter runs are flicker).</param>
    public static List<MidiNoteSpan> Detect(float[] mono, double sr, double minHz = 55, double maxHz = 2000,
                                            double minNoteSec = 0.1)
    {
        var notes = new List<MidiNoteSpan>();
        int n = sr > 60000 ? 16384 : 8192, hop = n / 8;
        if (mono is null || mono.Length < n || sr <= 0) return notes;

        int bins = n / 2;
        int frames = (mono.Length - n) / hop + 1;
        var win = new double[n];
        for (int i = 0; i < n; i++) win[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (n - 1)));
        var re = new double[n];
        var im = new double[n];
        var m = new double[bins];

        void Spectrum(int f)
        {
            int off = f * hop;
            for (int i = 0; i < n; i++) { re[i] = mono[off + i] * win[i]; im[i] = 0; }
            AudioFft.Forward(re, im);
            for (int b = 0; b < bins; b++) m[b] = Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
        }

        // ---- 1. Tuning --------------------------------------------------------------------
        double tuneCents = EstimateTuning(frames, bins, n, sr, m, Spectrum);

        // Per pitch / harmonic: the bin window searched for that partial (±~⅓ semitone, ≥ ±1 bin),
        // and Klapuri's harmonic weight (f0 + 27) / (h·f0 + 320).
        var lo = new int[Pitches, Harmonics];
        var hi = new int[Pitches, Harmonics];
        var w = new double[Pitches, Harmonics];
        // Expected partial position (fractional bin) where the window is clamped to ±1 bin and so
        // overlaps the semitone neighbours' (low partials); 0 elsewhere.
        var centre = new double[Pitches, Harmonics];
        var inRange = new bool[Pitches];
        for (int p = 0; p < Pitches; p++)
        {
            double f0 = 440.0 * Math.Pow(2, (LowPitch + p - 69) / 12.0 + tuneCents / 1200.0);
            inRange[p] = f0 >= minHz && f0 <= maxHz;
            for (int h = 0; h < Harmonics; h++)
            {
                double fh = f0 * (h + 1);
                double c = fh * n / sr, half = Math.Max(1.0, c * 0.02);
                centre[p, h] = c * 0.02 < 1.0 ? c : 0;
                lo[p, h] = (int)Math.Floor(c - half);
                hi[p, h] = (int)Math.Ceiling(c + half);
                if (fh > MaxPartialHz || hi[p, h] >= bins - 1 || lo[p, h] < 1) { lo[p, h] = -1; continue; }
                w[p, h] = (f0 + 27) / ((h + 1) * f0 + 320);
            }
        }

        // ---- 2. Per-frame estimate-and-cancel ---------------------------------------------
        var level = new float[frames][];   // salience of each chosen pitch (0 = not chosen)
        var frameTop = new float[frames];  // salience of the frame's first (strongest) pick
        var cents = new float[frames][];   // chosen pitch's measured offset from its centre
        var amp = new double[Harmonics];
        var taken = new bool[Pitches];
        for (int f = 0; f < frames; f++)
        {
            level[f] = new float[Pitches];
            cents[f] = new float[Pitches];
            Spectrum(f);
            double mmax = 0;
            for (int b = 1; b < bins; b++) if (m[b] > mmax) mmax = m[b];
            if (mmax < 1e-6) continue;
            Array.Clear(taken);

            double first = 0;
            for (int voice = 0; voice < MaxVoices; voice++)
            {
                int best = -1; double bestSal = 0;
                for (int p = 0; p < Pitches; p++)
                {
                    if (!inRange[p] || taken[p] || lo[p, 0] < 0) continue;
                    double s = 0;
                    for (int h = 0; h < Harmonics && lo[p, h] >= 0; h++) s += w[p, h] * Partial(m, lo[p, h], hi[p, h], centre[p, h]);
                    if (s > bestSal) { bestSal = s; best = p; }
                }
                if (best < 0 || bestSal < 0.08 * first) break;

                // A real note has energy at its fundamental; otherwise it's a missing-fundamental
                // ghost assembled from other notes' partials — drop the candidate, keep looking.
                taken[best] = true;
                if (Peak(m, lo[best, 0], hi[best, 0]) < 0.05 * mmax) { voice--; continue; }

                if (first == 0) first = bestSal;
                level[f][best] = (float)bestSal;
                cents[f][best] = (float)CentsOff(m, best, lo, hi, n, sr, tuneCents);

                // Cancel its partials. Spectral smoothing (min with the local mean) leaves most of a
                // partial that coincides with another note's, so that note is still found.
                int hc = 0;
                for (; hc < Harmonics && lo[best, hc] >= 0; hc++) amp[hc] = Peak(m, lo[best, hc], hi[best, hc]);
                for (int h = 0; h < hc; h++)
                {
                    double a = amp[h];
                    if (a <= 0) continue;
                    double take = a;
                    if (h > 0) { double mean = (amp[h - 1] + a + (h + 1 < hc ? amp[h + 1] : a)) / 3; take = Math.Min(a, mean); }
                    double keep = 1 - take / a;
                    for (int b = lo[best, h]; b <= hi[best, h]; b++) m[b] *= keep;
                }
            }
            frameTop[f] = (float)first;
        }

        // ---- 3. Hysteresis segmentation ---------------------------------------------------
        float gmax = 0;
        foreach (var t in frameTop) gmax = Math.Max(gmax, t);
        if (gmax <= 0) return notes;

        double frameSec = hop / sr;
        int minFrames = Math.Max(2, (int)Math.Round(minNoteSec / frameSec));
        int gapFrames = Math.Max(1, (int)Math.Round(0.06 / frameSec));
        var onsets = StrongOnsets(mono, sr);
        var weak = new bool[frames];
        var strong = new bool[frames];
        var on = new bool[frames];
        var runs = new List<(int s, int e)>();

        for (int p = 0; p < Pitches; p++)
        {
            bool any = false;
            for (int f = 0; f < frames; f++)
            {
                float l = level[f][p];
                weak[f] = l > 0 && l >= 0.01f * gmax;
                strong[f] = l >= 0.2f * frameTop[f] && l >= 0.04f * gmax;
                any |= strong[f];
            }
            if (!any) continue;
            MedianBool(weak, on, 2);

            runs.Clear();
            int fi = 0;
            while (fi < frames)
            {
                if (!on[fi]) { fi++; continue; }
                int s = fi, e = fi, gap = 0;
                for (fi++; fi < frames; fi++)
                {
                    if (on[fi]) { e = fi; gap = 0; }
                    else if (++gap > gapFrames) break;
                }
                bool hasStrong = false;
                for (int k = s; k <= e && !hasStrong; k++) hasStrong = strong[k];
                if (hasStrong && e - s + 1 >= minFrames && InTune(level, cents, p, s, e)) runs.Add((s, e));
            }

            foreach (var (s, e) in runs) EmitRun(notes, level, onsets, LowPitch + p, p, s, e, n, hop, minFrames, mono.Length, gmax);
        }
        notes.Sort((a, b) => a.StartSample != b.StartSample ? a.StartSample.CompareTo(b.StartSample) : a.Pitch.CompareTo(b.Pitch));
        return notes;
    }

    // ---- 4. Onset split + snap, then emit -------------------------------------------------
    static void EmitRun(List<MidiNoteSpan> notes, float[][] level, List<int> onsets, int pitch, int p,
                        int s, int e, int n, int hop, int minFrames, int length, float gmax)
    {
        // A frame's centre is f·hop + n/2; an onset is felt ~half a window either side of it.
        int reach = n / (2 * hop);
        var cuts = new List<int>();   // sample positions where a re-strike begins
        foreach (int o in onsets)
        {
            int fo = (int)Math.Round((o - n / 2.0) / hop);
            if (fo - reach < s || fo + reach > e) continue;
            if (fo - (cuts.Count > 0 ? FrameOf(cuts[^1], n, hop) : s) < minFrames || e - fo + 1 < minFrames) continue;
            float before = level[fo - reach][p], after = level[fo + reach][p];
            if (after > 1.5f * before) cuts.Add(o);
        }

        int start = SnapStart(s, onsets, n, hop);
        int end = Math.Min(length, e * hop + n / 2 + hop / 2);
        foreach (int c in cuts)
        {
            if (c > start) notes.Add(Span(pitch, start, c, level, p, FrameOf(start, n, hop), FrameOf(c, n, hop), gmax));
            start = c;
        }
        if (end > start) notes.Add(Span(pitch, start, end, level, p, FrameOf(start, n, hop), e, gmax));
    }

    static MidiNoteSpan Span(int pitch, int start, int end, float[][] level, int p, int fs, int fe, float gmax)
    {
        float peak = 0;
        for (int f = Math.Max(0, fs); f <= Math.Min(level.Length - 1, fe); f++) peak = Math.Max(peak, level[f][p]);
        float v = 0.25f + 0.75f * MathF.Sqrt(peak / gmax);
        return new MidiNoteSpan(pitch, start, end, Math.Clamp(v, 0.25f, 1f));
    }

    // Onsets whose energy over the next 60 ms is ≥ 2× (+3 dB) that of the previous 60 ms.
    static List<int> StrongOnsets(float[] mono, double sr)
    {
        var all = AudioOnsets.Detect(mono, sr);
        var keep = new List<int>(all.Count);
        int w = Math.Max(1, (int)(sr * 0.060));
        foreach (int o in all)
        {
            if (o < w || o + w > mono.Length) continue;
            double before = 0, after = 0;
            for (int i = o - w; i < o; i++) before += (double)mono[i] * mono[i];
            for (int i = o; i < o + w; i++) after += (double)mono[i] * mono[i];
            if (after >= 2 * before) keep.Add(o);
        }
        return keep;
    }

    static int FrameOf(int sample, int n, int hop) => Math.Max(0, (int)Math.Round((sample - n / 2.0) / hop));

    // Estimated start = a little before the first detected frame's centre; snapped to the nearest
    // onset within half a window, since the window smears the attack by that much.
    static int SnapStart(int s, List<int> onsets, int n, int hop)
    {
        if (s == 0) return 0;
        int est = s * hop + n / 2 - hop / 2;
        int best = est, bestD = n / 2 + 1;
        foreach (int o in onsets)
        {
            int d = Math.Abs(o - est);
            if (d < bestD) { bestD = d; best = o; }
        }
        return Math.Max(0, best);
    }

    // Weighted circular mean of spectral peaks' deviation from the 12-TET grid, in cents (−50..50).
    static double EstimateTuning(int frames, int bins, int n, double sr, double[] m, Action<int> spectrum)
    {
        int loBin = Math.Max(2, (int)(100.0 * n / sr)), hiBin = Math.Min(bins - 2, (int)(4000.0 * n / sr));
        int step = Math.Max(1, frames / 200);
        double sx = 0, sy = 0;
        for (int f = 0; f < frames; f += step)
        {
            spectrum(f);
            double mmax = 0;
            for (int b = loBin; b <= hiBin; b++) if (m[b] > mmax) mmax = m[b];
            if (mmax < 1e-6) continue;
            double thr = mmax * 0.1;
            for (int b = loBin; b <= hiBin; b++)
            {
                if (m[b] <= thr || m[b] < m[b - 1] || m[b] <= m[b + 1]) continue;
                double a0 = m[b - 1], a1 = m[b], a2 = m[b + 1];
                double denom = a0 - 2 * a1 + a2;
                double delta = Math.Abs(denom) > 1e-12 ? 0.5 * (a0 - a2) / denom : 0;
                double cents = 1200 * Math.Log2((b + delta) * sr / n / 440.0);
                double ang = 2 * Math.PI * cents / 100.0;
                sx += a1 * Math.Cos(ang); sy += a1 * Math.Sin(ang);
            }
        }
        if (sx == 0 && sy == 0) return 0;
        return Math.Atan2(sy, sx) / (2 * Math.PI) * 100.0;
    }

    static bool InTune(float[][] level, float[][] cents, int p, int s, int e)
    {
        double sum = 0; int k = 0;
        for (int f = s; f <= e; f++) if (level[f][p] > 0) { sum += Math.Abs(cents[f][p]); k++; }
        return k > 0 && sum / k <= StableCents;
    }

    // How far (cents) the chosen pitch's strongest low partial sits from the pitch's tuned centre.
    // Uses the loudest of harmonics 1–4 (parabolic-interpolated) so low notes, whose fundamental
    // spans few bins, are measured on a better-resolved partial.
    static double CentsOff(double[] m, int p, int[,] lo, int[,] hi, int n, double sr, double tuneCents)
    {
        int bh = -1, bb = 0; double ba = 0;
        for (int h = 0; h < 4 && lo[p, h] >= 0; h++)
            for (int b = lo[p, h]; b <= hi[p, h]; b++)
                if (m[b] > ba) { ba = m[b]; bb = b; bh = h; }
        if (bh < 0) return 0;
        double a0 = m[bb - 1], a1 = m[bb], a2 = m[bb + 1];
        double denom = a0 - 2 * a1 + a2;
        double delta = Math.Abs(denom) > 1e-12 ? 0.5 * (a0 - a2) / denom : 0;
        double hz = (bb + delta) * sr / n / (bh + 1);
        double centre = 440.0 * Math.Pow(2, (LowPitch + p - 69) / 12.0 + tuneCents / 1200.0);
        return 1200 * Math.Log2(hz / centre);
    }

    // A partial's amplitude, discounted by how far its interpolated peak sits from where it should
    // be (full at 0 cents, none at ≥ 50). Low notes' semitone neighbours share bins; this keeps the
    // one whose partials actually line up ahead.
    static double Partial(double[] m, int lo, int hi, double expected)
    {
        if (expected <= 0) return Peak(m, lo, hi);
        int bb = lo;
        for (int b = lo + 1; b <= hi; b++) if (m[b] > m[bb]) bb = b;
        double a1 = m[bb];
        if (a1 <= 0) return 0;
        double a0 = m[bb - 1], a2 = m[bb + 1];
        double denom = a0 - 2 * a1 + a2;
        double delta = Math.Abs(denom) > 1e-12 ? 0.5 * (a0 - a2) / denom : 0;
        double off = Math.Abs(1200 * Math.Log2((bb + delta) / expected));
        return a1 * Math.Max(0, 1 - off / 50);
    }

    static double Peak(double[] m, int lo, int hi)
    {
        double v = 0;
        for (int b = lo; b <= hi; b++) if (m[b] > v) v = m[b];
        return v;
    }

    static void MedianBool(bool[] src, bool[] dst, int r)
    {
        for (int i = 0; i < src.Length; i++)
        {
            int cnt = 0, tot = 0;
            for (int k = Math.Max(0, i - r); k <= Math.Min(src.Length - 1, i + r); k++) { tot++; if (src[k]) cnt++; }
            dst[i] = cnt * 2 > tot;
        }
    }
}
