// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// #974: a sample under a folder or file name with non-ASCII characters loads. On Windows the
// decoders opened the UTF-8 path in the ANSI code page, so "Música/Canción ñ.wav" failed.
// WAV, FLAC and MP3 each go through both ways in: the whole-file decode (a clip added by
// path) and the background import (double-click, drop, File → Import). Each file is longer
// than the 16 384-frame block the Windows decoders read in, so a lost tail or a wrong block
// shows: WAV and FLAC must decode to the source frames, MP3 the same through both ways.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class UnicodeSamplePathTests
{
    private const int Rate = 48000;
    private const int Frames = 50_000;   // > 3 × 16 384

    public static IEnumerable<(bool, string)> Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nota-unicode-{Guid.NewGuid():N}");
        var dir = Path.Combine(root, "Música ñ Ж 音");
        Directory.CreateDirectory(dir);

        var src = new float[Frames * 2];
        for (int i = 0; i < Frames; i++)
        {
            src[2 * i] = 0.3f * (float)Math.Sin(2 * Math.PI * 440 * i / Rate);
            src[2 * i + 1] = 0.2f * (float)Math.Sin(2 * Math.PI * 660 * i / Rate);
        }

        // Written under ASCII names (the encoders aren't what this pins), then moved in.
        string Place(string ascii, string unicode)
        {
            var to = Path.Combine(dir, unicode);
            File.Move(Path.Combine(root, ascii), to);
            return to;
        }
        using (var w = new Nota.Infrastructure.WavWriter(Path.Combine(root, "a.wav"), Rate, 2, WavBitDepth.Float32))
            w.WriteFrames(src, Frames);
        using (var enc = new NativeEncoder(Path.Combine(root, "a.flac"), Rate, 2, WavBitDepth.Pcm16))
        { enc.WriteFrames(src, Frames); enc.Close(); }
        using (var enc = new NativeEncoder(Path.Combine(root, "a.mp3"), Rate, 2, 192))
        { enc.WriteFrames(src, Frames); enc.Close(); }
        var wav = Place("a.wav", "Canción é.wav");
        var flac = Place("a.flac", "Canción é.flac");
        var mp3 = Place("a.mp3", "Canción é.mp3");

        using var e = new NotaEngine();
        int t = e.AddAudioTrack();

        foreach (var (path, fmt, tol) in new[] { (wav, "WAV", 1e-6f), (flac, "FLAC", 2.0f / 32768f), (mp3, "MP3", -1f) })
        {
            float[] decoded = Decode(e, t, path);
            float[] imported = Import(e, t, path);
            yield return (decoded.Length > 0, $"#974: a {fmt} under a non-ASCII path decodes ({decoded.Length / 2} frames)");
            yield return (imported.Length > 0, $"#974: …and imports in the background ({imported.Length / 2} frames)");
            if (tol > 0)
            {
                yield return (decoded.Length == src.Length && MaxDiff(decoded, src) <= tol,
                    $"#974: {fmt} decode is the source, tail included (max diff {MaxDiff(decoded, src):G3})");
                yield return (imported.Length == src.Length && MaxDiff(imported, src) <= tol,
                    $"#974: {fmt} import is the source, tail included (max diff {MaxDiff(imported, src):G3})");
            }
            else
            {
                // A lossy codec: both ways in must agree sample for sample, run the full length
                // (the encoder may pad, never cut) and carry the source's level.
                double rms = Rms(decoded), want = Rms(src);
                yield return (decoded.Length == imported.Length && MaxDiff(decoded, imported) <= 1e-6f,
                    $"#974: {fmt} decode and import agree ({decoded.Length / 2} vs {imported.Length / 2} frames)");
                yield return (decoded.Length >= src.Length && Math.Abs(rms - want) < 0.1 * want,
                    $"#974: {fmt} decodes the whole file at the source level (rms {rms:F3} vs {want:F3})");
            }
        }

        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static float[] Decode(NotaEngine e, int track, string path)
    {
        int c = e.AddAudioClip(track, path, 0.0);
        return c >= 0 && e.TryGetAudioClipInfo(track, c, out var info) ? e.ReadSample(info.SampleId) : Array.Empty<float>();
    }

    private static float[] Import(NotaEngine e, int track, string path)
    {
        using var job = e.OpenAudioImport(path);
        if (job is null) return Array.Empty<float>();
        while (job.Step(1 << 14) > 0) { }
        int c = e.AddImportedAudioClip(track, job, 0.0);
        return c >= 0 && e.TryGetAudioClipInfo(track, c, out var info) ? e.ReadSample(info.SampleId) : Array.Empty<float>();
    }

    private static float MaxDiff(float[] a, float[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        float m = a.Length == b.Length ? 0f : float.PositiveInfinity;
        for (int i = 0; i < n; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
        return m;
    }

    private static double Rms(float[] x)
    {
        double s = 0;
        foreach (var v in x) s += v * (double)v;
        return x.Length == 0 ? 0 : Math.Sqrt(s / x.Length);
    }
}
