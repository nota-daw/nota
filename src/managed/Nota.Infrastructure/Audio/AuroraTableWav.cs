// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Nota Aurora wavetable as a file (specs/053): float32 mono WAV, with the `clm ` chunk
// wavetable editors use to name the frame size (2048), so the file opens as a wavetable
// elsewhere too. Float32 makes the reload exact: the frames come back bit for bit.

using System.IO;

namespace Nota.Infrastructure;

public static class AuroraTableWav
{
    public const int FrameSize = 2048;
    private const int SampleRate = 44100;

    public static void Write(string path, float[] frames)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        byte[] clm = System.Text.Encoding.ASCII.GetBytes($"<!>{FrameSize} 00000000 wavetable (Nota)");
        int clmPad = clm.Length & 1;
        int dataBytes = frames.Length * sizeof(float);
        int riff = 4 + (8 + 16) + (8 + clm.Length + clmPad) + (8 + dataBytes);

        w.Write("RIFF"u8.ToArray()); w.Write(riff); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16);
        w.Write((ushort)3);                 // IEEE float
        w.Write((ushort)1);                 // mono
        w.Write(SampleRate); w.Write(SampleRate * 4);
        w.Write((ushort)4); w.Write((ushort)32);
        w.Write("clm "u8.ToArray()); w.Write(clm.Length); w.Write(clm);
        if (clmPad != 0) w.Write((byte)0);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);
        var bytes = new byte[dataBytes];
        System.Buffer.BlockCopy(frames, 0, bytes, 0, dataBytes);
        w.Write(bytes);
    }
}
