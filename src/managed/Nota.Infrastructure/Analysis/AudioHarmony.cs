// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Polyphonic pitch estimation for "Convert Harmony to New MIDI Track". STFT peak-
// picking with crude harmonic suppression, then per-pitch temporal segmentation.
// Pure, permissive DSP (in-house FFT). Approximate by design (MVP) — polyphonic
// transcription is hard; this catches sustained vertical chords reasonably.

using System;
using System.Collections.Generic;

namespace Nota.Infrastructure;

/// <summary>A detected note: pitch + [start, end) in source samples + 0..1 velocity.</summary>
public readonly record struct MidiNoteSpan(int Pitch, int StartSample, int EndSample, float Velocity);

public static class AudioHarmony
{
    public static List<MidiNoteSpan> Detect(float[] mono, double sr, double minHz = 55, double maxHz = 2000)
    {
        var notes = new List<MidiNoteSpan>();
        const int n = 8192, hop = 2048;
        if (mono is null || mono.Length < n || sr <= 0) return notes;

        int bins = n / 2;
        var win = new double[n];
        for (int i = 0; i < n; i++) win[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (n - 1)));

        const int lowPitch = 28, highPitch = 96;   // ~E1 … C7
        const int maxVoices = 6;
        int frames = (mono.Length - n) / hop + 1;
        var act = new bool[frames][];
        var lvl = new float[frames][];
        int loBin = Math.Max(1, (int)(minHz * n / sr));
        int hiBin = Math.Min(bins - 2, (int)(maxHz * n / sr));

        var re = new double[n];
        var im = new double[n];
        var m = new double[bins];
        for (int f = 0; f < frames; f++)
        {
            act[f] = new bool[highPitch - lowPitch + 1];
            lvl[f] = new float[highPitch - lowPitch + 1];
            int off = f * hop;
            for (int i = 0; i < n; i++) { re[i] = mono[off + i] * win[i]; im[i] = 0; }
            AudioFft.Forward(re, im);

            double mmax = 0;
            for (int b = 1; b < bins; b++) { m[b] = Math.Sqrt(re[b] * re[b] + im[b] * im[b]); if (m[b] > mmax) mmax = m[b]; }
            if (mmax < 1e-6) continue;
            double thr = mmax * 0.12;

            // Spectral peaks (parabolic-interpolated frequency), strongest first.
            var peaks = new List<(double hz, double amp)>();
            for (int b = loBin; b <= hiBin; b++)
            {
                if (m[b] > thr && m[b] >= m[b - 1] && m[b] > m[b + 1])
                {
                    double a0 = m[b - 1], a1 = m[b], a2 = m[b + 1];
                    double denom = a0 - 2 * a1 + a2;
                    double delta = Math.Abs(denom) > 1e-12 ? 0.5 * (a0 - a2) / denom : 0;
                    peaks.Add(((b + delta) * sr / n, a1));
                }
            }
            peaks.Sort((x, y) => y.amp.CompareTo(x.amp));

            // Map peaks to pitches, skipping ones that look like a harmonic of an already-chosen
            // (stronger, lower) pitch, and dedupe by pitch. Keep up to maxVoices.
            var chosen = new List<(int pitch, double hz, double amp)>();
            foreach (var pk in peaks)
            {
                bool harmonic = false;
                foreach (var c in chosen)
                    for (int h = 2; h <= 6; h++)
                        if (Math.Abs(pk.hz - c.hz * h) < c.hz * h * 0.03) { harmonic = true; break; }
                if (harmonic) continue;
                int pitch = (int)Math.Round(69 + 12 * Math.Log2(pk.hz / 440.0));
                if (pitch < lowPitch || pitch > highPitch) continue;
                bool dup = false;
                foreach (var c in chosen) if (c.pitch == pitch) { dup = true; break; }
                if (dup) continue;
                chosen.Add((pitch, pk.hz, pk.amp));
                if (chosen.Count >= maxVoices) break;
            }
            foreach (var c in chosen) { act[f][c.pitch - lowPitch] = true; lvl[f][c.pitch - lowPitch] = (float)(c.amp / mmax); }
        }

        // Per-pitch temporal segmentation: runs of active frames (bridging 1-frame gaps), min 2 frames.
        const int minFrames = 2, gapMerge = 1;
        for (int p = 0; p <= highPitch - lowPitch; p++)
        {
            int f = 0;
            while (f < frames)
            {
                if (!act[f][p]) { f++; continue; }
                int start = f, end = f, gap = 0; float peak = 0;
                while (f < frames)
                {
                    if (act[f][p]) { end = f; peak = Math.Max(peak, lvl[f][p]); gap = 0; f++; }
                    else { gap++; if (gap > gapMerge) break; f++; }
                }
                if (end - start + 1 >= minFrames)
                    notes.Add(new MidiNoteSpan(lowPitch + p, start * hop, (end + 1) * hop, Math.Clamp(0.3f + peak * 0.7f, 0.3f, 1f)));
            }
        }
        return notes;
    }
}
