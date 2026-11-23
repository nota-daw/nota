// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// #974: a sample under a folder or file name with non-ASCII characters loads. On Windows the
// decoders opened the UTF-8 path in the ANSI code page, so "Música/Canción ñ.wav" failed.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class UnicodeSamplePathTests
{
    private const int Rate = 48000;

    public static IEnumerable<(bool, string)> Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"nota-unicode-{Guid.NewGuid():N}", "Música ñ Ж 音");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Canción é.wav");
        var x = new float[Rate / 4 * 2];
        for (int i = 0; i < x.Length / 2; i++) x[2 * i] = x[2 * i + 1] = 0.3f * (float)Math.Sin(2 * Math.PI * 440 * i / Rate);
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, x.Length / 2);

        using var e = new NotaEngine();
        int t = e.AddAudioTrack();
        int clip = e.AddAudioClip(t, path, 0.0);
        yield return (clip >= 0, $"#974: a WAV under a non-ASCII path loads (clip {clip})");

        try { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); } catch (IOException) { }
    }
}
