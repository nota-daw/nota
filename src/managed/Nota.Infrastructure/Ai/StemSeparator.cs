// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Stem separation with htdemucs (Demucs v4, Meta, MIT). The ONNX file is the network alone
// (nota-daw/nota-models: export_htdemucs.py); everything around it is here, as Demucs does it:
// the whole signal is normalised by its mono mean / std (demucs.separate), cut into 7.8 s
// segments overlapping by a quarter (demucs.apply.apply_model, one model, no random shifts),
// each segment's spectrogram goes in beside the waveform, and the two branches that come out —
// a complex spectrogram per stem (back through the iSTFT) and a waveform per stem — are summed.
// Overlapping segments are cross-faded with Demucs's triangular weights.

using Microsoft.ML.OnnxRuntime;

namespace Nota.Infrastructure;

public sealed class StemSeparator : IDisposable
{
    /// <summary>The model's stems, in output order.</summary>
    public static readonly string[] Stems = ["Drums", "Bass", "Other", "Vocals"];
    public const int SampleRate = 44100;
    private const int Segment = 343980;           // htdemucs's training length: 39/5 s at 44.1 kHz
    private const double Overlap = 0.25;

    private readonly InferenceSession _session;

    /// <param name="runtimePath">The ONNX Runtime native library (ModelStore).</param>
    /// <param name="modelPath">htdemucs.onnx.</param>
    public StemSeparator(string runtimePath, string modelPath)
    {
        OnnxRuntimeLoader.Use(runtimePath);
        _session = OnnxRuntimeLoader.Session(modelPath);
    }

    public void Dispose() => _session.Dispose();

    /// <summary>Separates a stereo signal at <see cref="SampleRate"/>. Returns
    /// [stem][channel][sample], same length as the input, stems in <see cref="Stems"/> order.</summary>
    public float[][][] Separate(float[] left, float[] right, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int len = left.Length;
        var outp = new float[Stems.Length][][];
        for (int s = 0; s < Stems.Length; s++) outp[s] = [new float[len], new float[len]];
        if (len == 0) return outp;

        // demucs.separate: normalise by the mono mix's mean and (unbiased) std.
        double mean = 0;
        for (int i = 0; i < len; i++) mean += (left[i] + right[i]) * 0.5;
        mean /= len;
        double var = 0;
        for (int i = 0; i < len; i++) { double d = (left[i] + right[i]) * 0.5 - mean; var += d * d; }
        double std = Math.Sqrt(var / Math.Max(1, len - 1));
        if (std < 1e-8) return outp;               // silence separates into silence
        float fm = (float)mean, inv = (float)(1 / std);

        int stride = (int)((1 - Overlap) * Segment);
        var weight = new float[Segment];
        int halfSeg = Segment / 2;
        for (int i = 0; i < Segment; i++)
            weight[i] = (i < halfSeg ? i + 1 : Segment - i) / (float)halfSeg;
        var sumWeight = new float[len];

        var mix = new float[2 * Segment];
        int frames = DemucsSpec.Frames(Segment);
        int plane = DemucsSpec.Bins * frames;
        var mag = new float[4 * plane];
        var re = new float[plane];
        var im = new float[plane];
        var chunk = new float[Stems.Length * 2 * Segment];
        int segments = (len + stride - 1) / stride;
        int done = 0;
        progress?.Report(0);
        for (int offset = 0; offset < len; offset += stride)
        {
            ct.ThrowIfCancellationRequested();
            // TensorChunk.padded: the chunk centred in a full segment, real neighbouring audio
            // where there is some, zeros past either end.
            int chunkLen = Math.Min(Segment, len - offset);
            int delta = Segment - chunkLen;
            int start = offset - delta / 2;
            for (int i = 0; i < Segment; i++)
            {
                int k = start + i;
                bool inside = k >= 0 && k < len;
                mix[i] = inside ? (left[k] - fm) * inv : 0f;
                mix[Segment + i] = inside ? (right[k] - fm) * inv : 0f;
            }
            for (int c = 0; c < 2; c++)
            {
                DemucsSpec.Forward(mix.AsSpan(c * Segment, Segment), re, im);
                Array.Copy(re, 0, mag, (2 * c) * plane, plane);       // "cac": real and imaginary
                Array.Copy(im, 0, mag, (2 * c + 1) * plane, plane);   // parts as channels
            }

            RunSegment(mix, mag, frames, plane, chunk);

            // center_trim back to the chunk, then the weighted overlap-add.
            int trim = delta / 2;
            for (int s = 0; s < Stems.Length; s++)
                for (int c = 0; c < 2; c++)
                {
                    var dst = outp[s][c];
                    int src = (s * 2 + c) * Segment + trim;
                    for (int i = 0; i < chunkLen; i++) dst[offset + i] += weight[i] * chunk[src + i];
                }
            for (int i = 0; i < chunkLen; i++) sumWeight[offset + i] += weight[i];
            progress?.Report(++done / (double)segments);
        }

        float fstd = (float)std;
        for (int s = 0; s < Stems.Length; s++)
            for (int c = 0; c < 2; c++)
            {
                var d = outp[s][c];
                for (int i = 0; i < len; i++) d[i] = d[i] / sumWeight[i] * fstd + fm;
            }
        return outp;
    }

    // One segment through the network: chunk[stem][channel][sample] = iSTFT(spec branch) + wave branch.
    private void RunSegment(float[] mix, float[] mag, int frames, int plane, float[] chunk)
    {
        using var mixValue = OrtValue.CreateTensorValueFromMemory(mix, [1, 2, Segment]);
        using var magValue = OrtValue.CreateTensorValueFromMemory(mag, [1, 4, DemucsSpec.Bins, frames]);
        using var run = new RunOptions();
        using var results = _session.Run(run, ["mix", "mag"], [mixValue, magValue], ["spec", "wave"]);
        var spec = results[0].GetTensorDataAsSpan<float>().ToArray();   // [1, stems, 4, bins, frames]
        var wave = results[1].GetTensorDataAsSpan<float>().ToArray();   // [1, stems, 2, Segment]
        Parallel.For(0, Stems.Length * 2, sc =>
        {
            int reAt = (2 * sc) * plane, imAt = reAt + plane;            // stem s, channel c: planes 2c, 2c+1
            int at = sc * Segment;
            var dst = chunk.AsSpan(at, Segment);
            DemucsSpec.Inverse(spec, spec, reAt, imAt, Segment, dst);
            for (int i = 0; i < Segment; i++) dst[i] += wave[at + i];
        });
    }
}
