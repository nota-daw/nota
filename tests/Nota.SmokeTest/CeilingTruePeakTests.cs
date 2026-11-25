// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class CeilingTruePeakTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nota-ceiling-burst-{Guid.NewGuid():N}.wav");
        WavWriter.WriteStereo(path, 0.2, 44100, i =>
        {
            double x = i >= 2048 && i < 2064 ? ((i - 2048) % 4 < 2 ? 0.85 : -0.85) : 0;
            return (x, x);
        });
        try
        {
            foreach (int block in new[] { 32, 257 })
            foreach (float lookahead in new[] { 0f, 3f, 10f })
            foreach (float character in new[] { 0f, 1f, 2f })
            {
                using var e = new NotaEngine();
                int t = e.AddAudioTrack();
                e.AddAudioClip(t, path, 0);
                int d = e.AddBuiltinDevice(t, 14);
                e.DeviceSetParam(t, d, 0, -1);
                e.DeviceSetParam(t, d, 4, character);
                e.DeviceSetParam(t, d, 5, lookahead);
                e.DeviceSetParam(t, d, 6, 100);
                e.DeviceSetParam(t, d, 7, 1);
                e.Play();
                var buf = new float[block * 2];
                for (int n = 0; n < 8192; n += block) e.RenderOffline(buf, Math.Min(block, 8192 - n));
                var scope = new float[40];
                e.DeviceScope(t, d, scope, scope.Length);
                yield return (scope[10] <= -0.95f && scope[10] > -2.5f,
                    $"#1088 Ceiling burst, block {block}, lookahead {lookahead}, character {character}: {scope[10]:F2} dBTP");
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
