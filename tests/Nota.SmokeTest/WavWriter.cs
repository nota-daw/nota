// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Minimal 16-bit PCM mono WAV writer, used only to generate deterministic test
// input for the smoke test.

namespace Nota.SmokeTest;

internal static class WavWriter
{
    public static void WriteSine(string path, double seconds, double freq, int sampleRate)
    {
        int frames = (int)(seconds * sampleRate);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        int dataBytes = frames * 2;           // 16-bit mono
        int byteRate = sampleRate * 2;

        // RIFF header
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        // fmt chunk
        w.Write("fmt "u8.ToArray());
        w.Write(16);                          // chunk size
        w.Write((short)1);                    // PCM
        w.Write((short)1);                    // channels
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)2);                    // block align
        w.Write((short)16);                   // bits per sample
        // data chunk
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);

        for (int i = 0; i < frames; i++)
        {
            double s = Math.Sin(2.0 * Math.PI * freq * i / sampleRate) * 0.8;
            w.Write((short)(s * short.MaxValue));
        }
    }

    // First half silence, second half a steady tone. Used to prove source-region
    // editing: sliding the Start marker to the midpoint should reveal the tone.
    public static void WriteSilenceThenTone(string path, double seconds, double freq, int sampleRate)
    {
        int frames = (int)(seconds * sampleRate);
        int half = frames / 2;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        int dataBytes = frames * 2;
        int byteRate = sampleRate * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(byteRate); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);

        for (int i = 0; i < frames; i++)
        {
            double s = i < half ? 0.0 : Math.Sin(2.0 * Math.PI * freq * i / sampleRate) * 0.8;
            w.Write((short)(s * short.MaxValue));
        }
    }

    // A percussive click track at a fixed tempo: a short decaying tone burst on
    // each beat. Deterministic (no RNG) so tempo detection is reproducible.
    public static void WriteClicks(string path, double bpm, double seconds, int sampleRate)
    {
        int frames = (int)(seconds * sampleRate);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        int dataBytes = frames * 2;
        int byteRate = sampleRate * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(byteRate); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);

        double beatFrames = 60.0 / bpm * sampleRate;
        for (int i = 0; i < frames; i++)
        {
            double phase = i % beatFrames;                        // frames since the last beat
            double env = Math.Exp(-phase / (0.03 * sampleRate));  // ~30ms decay
            double s = Math.Sin(2.0 * Math.PI * 1200.0 * i / sampleRate) * env * 0.9;
            w.Write((short)(s * short.MaxValue));
        }
    }

    // A steady sum of sines (each at `amp`), mono — one tone per band for the multiband tests.
    public static void WriteTones(string path, double seconds, double[] freqs, double amp, int sampleRate)
    {
        int frames = (int)(seconds * sampleRate);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        int dataBytes = frames * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);
        for (int i = 0; i < frames; i++)
        {
            double s = 0;
            foreach (double f in freqs) s += Math.Sin(2.0 * Math.PI * f * i / sampleRate) * amp;
            w.Write((short)(Math.Clamp(s, -1, 1) * short.MaxValue));
        }
    }

    // A stereo impulse response: `lead` seconds of silence, then decaying noise (independent
    // L / R, deterministic LCG) — a stand-in for a user IR file.
    public static void WriteNoiseIr(string path, double seconds, double lead, int sampleRate)
    {
        int frames = (int)(seconds * sampleRate), leadF = (int)(lead * sampleRate);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        int dataBytes = frames * 4;           // 16-bit stereo
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(sampleRate); w.Write(sampleRate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);
        uint seed = 12345;
        double Next() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 8388608.0 - 1.0; }
        for (int i = 0; i < frames; i++)
        {
            double env = i < leadF ? 0.0 : Math.Exp(-(i - leadF) / (0.25 * sampleRate)) * 0.7;
            w.Write((short)(Next() * env * short.MaxValue));
            w.Write((short)(Next() * env * short.MaxValue));
        }
    }
}
