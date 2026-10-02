// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The AI models' C# ports against golden output from the Python originals: htdemucs's STFT /
// iSTFT and the whole separation (demucs.apply.apply_model), basic-pitch's inference and note
// decoding. Needs the ONNX Runtime library, the models and fixtures written by
// nota-models/scripts/fixtures.py, so it is opt-in:
//   dotnet run --project tests/Nota.SmokeTest -- --ai-golden <libonnxruntime> <dir>
// where <dir> holds htdemucs.onnx, nmp.onnx and fx/ (raw little-endian float32 files).

using System.Text.Json;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class AiGoldenTests
{
    public static IEnumerable<(bool Ok, string Label)> Run(string runtime, string dir)
    {
        string fx = Path.Combine(dir, "fx");
        var mix = Floats(Path.Combine(fx, "mix44.f32"));
        int n = mix.Length / 2;
        var left = mix[..n];
        var right = mix[n..];

        // ---- STFT ----------------------------------------------------------------------------
        const int seg = 343980;
        int frames = DemucsSpec.Frames(seg);
        int plane = DemucsSpec.Bins * frames;
        var re = new float[plane];
        var im = new float[plane];
        var refMag = Floats(Path.Combine(fx, "mag.f32"));
        double magErr = 0, magPeak = 0;
        for (int c = 0; c < 2; c++)
        {
            DemucsSpec.Forward((c == 0 ? left : right).AsSpan(0, seg), re, im);
            for (int j = 0; j < plane; j++)
            {
                magErr = Math.Max(magErr, Math.Abs(re[j] - refMag[(2 * c) * plane + j]));
                magErr = Math.Max(magErr, Math.Abs(im[j] - refMag[(2 * c + 1) * plane + j]));
                magPeak = Math.Max(magPeak, Math.Abs(refMag[(2 * c) * plane + j]));
            }
        }
        yield return (magErr < 1e-4 * Math.Max(1, magPeak), $"htdemucs spectrogram matches _spec (max err {magErr:g3}, peak {magPeak:g3})");

        var refIspec = Floats(Path.Combine(fx, "ispec.f32"));
        var back = new float[seg];
        double ispecErr = 0;
        for (int c = 0; c < 2; c++)
        {
            DemucsSpec.Forward((c == 0 ? left : right).AsSpan(0, seg), re, im);
            DemucsSpec.Inverse(re, im, 0, 0, seg, back);
            for (int i = 0; i < seg; i++) ispecErr = Math.Max(ispecErr, Math.Abs(back[i] - refIspec[c * seg + i]));
        }
        yield return (ispecErr < 1e-4, $"…and the iSTFT matches _ispec (max err {ispecErr:g3})");

        // ---- separation ----------------------------------------------------------------------
        var refStems = Floats(Path.Combine(fx, "stems.f32"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        float[][][] stems;
        using (var sep = new StemSeparator(runtime, Path.Combine(dir, "htdemucs.onnx")))
            stems = sep.Separate(left, right);
        sw.Stop();
        for (int s = 0; s < StemSeparator.Stems.Length; s++)
        {
            double err = 0, sig = 0;
            for (int c = 0; c < 2; c++)
                for (int i = 0; i < n; i++)
                {
                    double r = refStems[(s * 2 + c) * n + i], d = stems[s][c][i] - r;
                    err += d * d; sig += r * r;
                }
            double snr = 10 * Math.Log10(sig / Math.Max(1e-20, err));
            yield return (snr > 50, $"{StemSeparator.Stems[s]} stem matches apply_model ({snr:0} dB SNR)");
        }
        Console.WriteLine($"    separation of {n / 44100.0:0.0} s took {sw.Elapsed.TotalSeconds:0.0} s");

        // ---- basic-pitch ---------------------------------------------------------------------
        var mono = Floats(Path.Combine(fx, "mono22.f32"));
        var refNote = Floats(Path.Combine(fx, "bp_note.f32"));
        var refOnset = Floats(Path.Combine(fx, "bp_onset.f32"));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fx, "bp_notes.json")));
        int bpFrames = doc.RootElement.GetProperty("frames").GetInt32();
        var refNotes = doc.RootElement.GetProperty("notes").EnumerateArray()
            .Select(e => (Start: e[0].GetDouble(), End: e[1].GetDouble(), Pitch: e[2].GetInt32())).ToList();

        // Decoding alone, fed the reference activations: must reproduce the notes exactly.
        var decoded = PitchTranscriber.Decode(refNote, refOnset, bpFrames);
        yield return (decoded.Count == refNotes.Count, $"basic-pitch decoding finds the reference's {refNotes.Count} notes (got {decoded.Count})");

        List<TranscribedNote> notes;
        using (var tr = new PitchTranscriber(runtime, Path.Combine(dir, "nmp.onnx")))
            notes = tr.Transcribe(mono);
        int matched = refNotes.Count(r => notes.Any(nn => nn.Pitch == r.Pitch && Math.Abs(nn.Start - r.Start) < 0.012 && Math.Abs(nn.End - r.End) < 0.012));
        yield return (matched >= refNotes.Count * 0.98 && Math.Abs(notes.Count - refNotes.Count) <= Math.Max(2, refNotes.Count / 50),
            $"basic-pitch end to end: {matched}/{refNotes.Count} reference notes matched to a frame ({notes.Count} found)");

        // ---- resampler -----------------------------------------------------------------------
        var tone = new float[48000];
        for (int i = 0; i < tone.Length; i++) tone[i] = (float)Math.Sin(2 * Math.PI * 1000 * i / 48000.0);
        var down = Resampler.Convert(tone, 48000, 44100);
        double rErr = 0;
        for (int i = 200; i < down.Length - 200; i++) rErr = Math.Max(rErr, Math.Abs(down[i] - Math.Sin(2 * Math.PI * 1000 * i / 44100.0)));
        yield return (down.Length == 44100 && rErr < 1e-3, $"resampler 48 → 44.1 kHz keeps a 1 kHz tone (max err {rErr:g3})");
    }

    private static float[] Floats(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var f = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, f, 0, f.Length * 4);
        return f;
    }
}
