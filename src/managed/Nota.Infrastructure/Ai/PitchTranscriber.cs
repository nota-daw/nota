// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Polyphonic audio → notes with Spotify's basic-pitch (Apache-2.0). The model takes 2 s
// windows of 22.05 kHz mono and returns, per 256-sample frame, note and onset activations for
// the 88 piano keys. Windowing, unwrapping and note decoding are ported from
// basic_pitch.inference / note_creation (output_to_notes_polyphonic with inferred onsets and the
// "melodia trick"), with the library's default thresholds. Pitch bends are left out.

using Microsoft.ML.OnnxRuntime;

namespace Nota.Infrastructure;

/// <summary>A transcribed note: times in seconds from the start of the audio, MIDI pitch, and
/// the model's mean frame activation (0..1, basic-pitch's "amplitude").</summary>
public readonly record struct TranscribedNote(double Start, double End, int Pitch, float Amplitude)
{
    public int Velocity => Math.Clamp((int)Math.Round(127 * Amplitude), 1, 127);
}

public sealed class PitchTranscriber : IDisposable
{
    public const int SampleRate = 22050;
    private const int FftHop = 256;
    private const int WindowSamples = SampleRate * 2 - FftHop;        // AUDIO_N_SAMPLES = 43844
    private const int FramesPerWindow = 172;                         // ANNOT_N_FRAMES
    private const int Keys = 88, MidiOffset = 21;
    private const int OverlapFrames = 30;
    private const int OverlapLen = OverlapFrames * FftHop;
    private const int HopSize = WindowSamples - OverlapLen;
    private const int Batch = 8;
    private const int Fps = SampleRate / FftHop;                     // ANNOTATIONS_FPS = 86

    private const float OnsetThresh = 0.5f, FrameThresh = 0.3f;
    private const int MinNoteLen = 11;                               // round(127.70 ms × 86.13 fps)
    private const int EnergyTol = 11;

    private readonly InferenceSession _session;
    private readonly string _input;

    public PitchTranscriber(string runtimePath, string modelPath)
    {
        OnnxRuntimeLoader.Use(runtimePath);
        _session = OnnxRuntimeLoader.Session(modelPath);
        _input = _session.InputMetadata.Keys.First();
    }

    public void Dispose() => _session.Dispose();

    /// <summary>Notes in <paramref name="mono"/> (at <see cref="SampleRate"/>), by start time.</summary>
    public List<TranscribedNote> Transcribe(float[] mono, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var (note, onset, n) = Infer(mono, progress, ct);
        var events = Decode(note, onset, n);
        var notes = new List<TranscribedNote>(events.Count);
        foreach (var (s, e, p, a) in events) notes.Add(new TranscribedNote(FrameTime(s), FrameTime(e), p, a));
        notes.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Pitch.CompareTo(b.Pitch));
        return notes;
    }

    /// <summary>One line out of a transcription: wherever notes overlap, the strongest one
    /// wins and the others are dropped (Convert Melody).</summary>
    public static List<TranscribedNote> Monophonic(IReadOnlyList<TranscribedNote> notes)
    {
        var kept = new List<TranscribedNote>();
        foreach (var n in notes.OrderByDescending(n => n.Amplitude * (n.End - n.Start)))
            if (!kept.Any(k => k.Start < n.End && n.Start < k.End)) kept.Add(n);
        kept.Sort((a, b) => a.Start.CompareTo(b.Start));
        return kept;
    }

    // ---- inference (inference.run_inference) -----------------------------------------------

    private (float[] Note, float[] Onset, int Frames) Infer(float[] mono, IProgress<double>? progress, CancellationToken ct)
    {
        int original = mono.Length;
        int padded = OverlapLen / 2 + original;       // half an overlap of silence in front
        int windows = Math.Max(1, (padded + HopSize - 1) / HopSize);
        int keep = FramesPerWindow - OverlapFrames;   // unwrap drops half the overlap at each end
        int total = (int)Math.Floor(original * ((double)Fps / SampleRate));
        var note = new float[Math.Max(0, total) * Keys];
        var onset = new float[note.Length];

        var input = new float[Batch * WindowSamples];
        for (int w0 = 0; w0 < windows; w0 += Batch)
        {
            ct.ThrowIfCancellationRequested();
            int b = Math.Min(Batch, windows - w0);
            Array.Clear(input);
            for (int w = 0; w < b; w++)
            {
                int from = (w0 + w) * HopSize - OverlapLen / 2;   // in the unpadded signal
                for (int i = 0; i < WindowSamples; i++)
                {
                    int k = from + i;
                    if (k >= 0 && k < original) input[w * WindowSamples + i] = mono[k];
                }
            }
            using var x = OrtValue.CreateTensorValueFromMemory(input, [b, WindowSamples, 1]);
            using var run = new RunOptions();
            using var r = _session.Run(run, [_input], [x], ["StatefulPartitionedCall:1", "StatefulPartitionedCall:2"]);
            var nt = r[0].GetTensorDataAsSpan<float>();
            var on = r[1].GetTensorDataAsSpan<float>();
            for (int w = 0; w < b; w++)
                for (int f = 0; f < keep; f++)
                {
                    int dst = (w0 + w) * keep + f;
                    if (dst >= total) break;
                    int src = (w * FramesPerWindow + OverlapFrames / 2 + f) * Keys;
                    nt.Slice(src, Keys).CopyTo(note.AsSpan(dst * Keys, Keys));
                    on.Slice(src, Keys).CopyTo(onset.AsSpan(dst * Keys, Keys));
                }
            progress?.Report(Math.Min(1, (w0 + b) / (double)windows));
        }
        return (note, onset, Math.Max(0, total));
    }

    // note_creation.model_frames_to_time: frame index → seconds, with basic-pitch's per-window
    // drift correction.
    private static double FrameTime(int frame)
    {
        double t = frame * (double)FftHop / SampleRate;
        double windowOffset = (double)FftHop / SampleRate * (FramesPerWindow - (double)WindowSamples / FftHop) + 0.0018;
        return t - windowOffset * Math.Floor((double)frame / FramesPerWindow);
    }

    // ---- decoding (note_creation.output_to_notes_polyphonic) --------------------------------

    internal static List<(int Start, int End, int Pitch, float Amplitude)> Decode(float[] frames, float[] onsetsIn, int n)
    {
        var events = new List<(int, int, int, float)>();
        if (n == 0) return events;
        var onsets = InferOnsets(onsetsIn, frames, n);

        // Onset peaks (strict local maxima in time) above the threshold, latest first.
        var peaks = new List<(int T, int F)>();
        for (int t = 1; t < n - 1; t++)
            for (int f = 0; f < Keys; f++)
            {
                float v = onsets[t * Keys + f];
                if (v >= OnsetThresh && v > onsets[(t - 1) * Keys + f] && v > onsets[(t + 1) * Keys + f]) peaks.Add((t, f));
            }
        peaks.Reverse();

        var energy = (float[])frames.Clone();
        foreach (var (start, f) in peaks)
        {
            if (start >= n - 1) continue;
            int i = start + 1, k = 0;
            while (i < n - 1 && k < EnergyTol)
            {
                k = energy[i * Keys + f] < FrameThresh ? k + 1 : 0;
                i++;
            }
            i -= k;
            if (i - start <= MinNoteLen) continue;
            for (int t = start; t < i; t++)
            {
                energy[t * Keys + f] = 0;
                if (f < Keys - 1) energy[t * Keys + f + 1] = 0;
                if (f > 0) energy[t * Keys + f - 1] = 0;
            }
            events.Add((start, i, f + MidiOffset, Mean(frames, start, i, f)));
        }

        // Melodia trick: follow the strongest remaining energy both ways into further notes.
        while (true)
        {
            int best = -1; float max = FrameThresh;
            for (int j = 0; j < energy.Length; j++)
                if (energy[j] > max) { max = energy[j]; best = j; }
            if (best < 0) break;
            int mid = best / Keys, f = best % Keys;
            energy[best] = 0;

            int i = mid + 1, k = 0;
            while (i < n - 1 && k < EnergyTol)
            {
                k = energy[i * Keys + f] < FrameThresh ? k + 1 : 0;
                Clear(energy, i, f);
                i++;
            }
            int end = i - 1 - k;

            i = mid - 1; k = 0;
            while (i > 0 && k < EnergyTol)
            {
                k = energy[i * Keys + f] < FrameThresh ? k + 1 : 0;
                Clear(energy, i, f);
                i--;
            }
            int start = i + 1 + k;

            if (end - start <= MinNoteLen) continue;
            events.Add((start, end, f + MidiOffset, Mean(frames, start, end, f)));
        }
        return events;
    }

    private static void Clear(float[] energy, int t, int f)
    {
        energy[t * Keys + f] = 0;
        if (f < Keys - 1) energy[t * Keys + f + 1] = 0;
        if (f > 0) energy[t * Keys + f - 1] = 0;
    }

    private static float Mean(float[] frames, int start, int end, int f)
    {
        double s = 0;
        for (int t = start; t < end; t++) s += frames[t * Keys + f];
        return end > start ? (float)(s / (end - start)) : 0f;
    }

    // get_infered_onsets: onsets also where frame activations jump (the smaller of the 1- and
    // 2-frame rises), rescaled to the predicted onsets' peak.
    private static float[] InferOnsets(float[] onsets, float[] frames, int n)
    {
        const int nDiff = 2;
        var diff = new float[onsets.Length];
        float maxDiff = 0, maxOnset = 0;
        for (int t = 0; t < n; t++)
            for (int f = 0; f < Keys; f++)
            {
                int j = t * Keys + f;
                maxOnset = Math.Max(maxOnset, onsets[j]);
                if (t < nDiff) continue;
                float d = float.MaxValue;
                for (int m = 1; m <= nDiff; m++) d = Math.Min(d, frames[j] - frames[(t - m) * Keys + f]);
                d = Math.Max(0, d);
                diff[j] = d;
                maxDiff = Math.Max(maxDiff, d);
            }
        var outp = new float[onsets.Length];
        float scale = maxDiff > 0 ? maxOnset / maxDiff : 0;
        for (int j = 0; j < outp.Length; j++) outp[j] = Math.Max(onsets[j], diff[j] * scale);
        return outp;
    }
}
