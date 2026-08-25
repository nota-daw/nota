// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M6-4: streaming WAV writer for master export. Takes interleaved stereo float
// blocks (as produced by the engine's offline render) and writes 16/24-bit PCM
// or 32-bit IEEE float. Sizes are back-patched on Close so the render can stream
// straight to disk without buffering the whole file in RAM.

using System;
using System.IO;

namespace Nota.Infrastructure;

public sealed class WavWriter : IDisposable
{
    private const ushort FormatPcm = 1;
    private const ushort FormatIeeeFloat = 3;

    private readonly FileStream _fs;
    private readonly BinaryWriter _w;
    private readonly int _channels;
    private readonly WavBitDepth _depth;
    private readonly float _gain;
    private readonly bool _dither;
    private readonly Random _rng = new();
    private float _tpdfPrev;         // previous TPDF sample, for a triangular PDF
    private long _dataBytes;
    private bool _closed;

    /// <param name="gain">Output gain applied to every sample (normalize; 1 = unity).</param>
    /// <param name="dither">Add ±1-LSB TPDF dither before 16-bit quantization. Ignored
    /// for 24-bit / float output, which don't benefit from it here.</param>
    public WavWriter(string path, int sampleRate, int channels, WavBitDepth depth,
                     float gain = 1f, bool dither = false)
    {
        _channels = channels;
        _depth = depth;
        _gain = gain;
        _dither = dither && depth == WavBitDepth.Pcm16;
        _fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        _w = new BinaryWriter(_fs);
        WriteHeader(sampleRate);
    }

    private int BytesPerSample => _depth switch
    {
        WavBitDepth.Pcm16 => 2,
        WavBitDepth.Pcm24 => 3,
        WavBitDepth.Float32 => 4,
        _ => 2,
    };

    private void WriteHeader(int sampleRate)
    {
        ushort format = _depth == WavBitDepth.Float32 ? FormatIeeeFloat : FormatPcm;
        int bytesPerSample = BytesPerSample;
        int blockAlign = _channels * bytesPerSample;
        int byteRate = sampleRate * blockAlign;

        _w.Write("RIFF"u8.ToArray());
        _w.Write(0);                       // RIFF chunk size — patched on Close
        _w.Write("WAVE"u8.ToArray());

        _w.Write("fmt "u8.ToArray());
        _w.Write(16);                      // fmt chunk size
        _w.Write(format);
        _w.Write((ushort)_channels);
        _w.Write(sampleRate);
        _w.Write(byteRate);
        _w.Write((ushort)blockAlign);
        _w.Write((ushort)(bytesPerSample * 8));

        _w.Write("data"u8.ToArray());
        _w.Write(0);                       // data chunk size — patched on Close
    }

    /// <summary>Appends <paramref name="frames"/> interleaved frames from the buffer.</summary>
    public void WriteFrames(float[] interleaved, int frames)
    {
        int samples = frames * _channels;
        float g = _gain;
        switch (_depth)
        {
            case WavBitDepth.Pcm16:
                for (int i = 0; i < samples; i++)
                {
                    float x = interleaved[i] * g;
                    // TPDF dither: two independent uniform draws → a triangular ±1-LSB
                    // PDF added before rounding, decorrelating the quantization error.
                    if (_dither)
                    {
                        float cur = (float)_rng.NextDouble();
                        x += (cur - _tpdfPrev) / short.MaxValue;
                        _tpdfPrev = cur;
                    }
                    _w.Write(QuantizeShort(x));
                }
                break;

            case WavBitDepth.Pcm24:
                for (int i = 0; i < samples; i++)
                {
                    int v = (int)MathF.Round(Clamp(interleaved[i] * g) * 8388607.0f); // 2^23 - 1
                    _w.Write((byte)(v & 0xFF));
                    _w.Write((byte)((v >> 8) & 0xFF));
                    _w.Write((byte)((v >> 16) & 0xFF));
                }
                break;

            case WavBitDepth.Float32:
                for (int i = 0; i < samples; i++)
                    _w.Write(interleaved[i] * g);
                break;
        }
        _dataBytes += (long)samples * BytesPerSample;
    }

    private static float Clamp(float x) => x < -1f ? -1f : (x > 1f ? 1f : x);

    // Round to the nearest 16-bit code, clamped to the asymmetric [-32768, 32767] range.
    private static short QuantizeShort(float x)
    {
        int v = (int)MathF.Round(x * short.MaxValue);
        if (v > short.MaxValue) v = short.MaxValue;
        else if (v < short.MinValue) v = short.MinValue;
        return (short)v;
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;

        _w.Flush();
        // Back-patch the two size fields now that the payload length is known.
        _fs.Seek(4, SeekOrigin.Begin);
        _w.Write((uint)(36 + _dataBytes));   // RIFF size = 36 + data
        _fs.Seek(40, SeekOrigin.Begin);
        _w.Write((uint)_dataBytes);          // data size
        _w.Flush();
    }

    public void Dispose()
    {
        Close();
        _w.Dispose();
        _fs.Dispose();
    }
}
