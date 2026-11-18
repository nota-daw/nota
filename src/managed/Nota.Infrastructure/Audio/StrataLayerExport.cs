// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One Nota Strata layer as its own WAV (#482, specs/102). Reads the device state blob
// (Strata::getState, v1): version, sample rate, loop length, bars, layer count, per layer
// (gain dB, muted, state), then each layer's interleaved stereo PCM for one cycle. A layer's
// buffer is the input after Input gain at its place in the loop — index 0 is the loop's top —
// so it is written as stored: no Speed, Reverse, Feedback, level or mute, and no other layer.
// Never touches the device; anything but a finished layer of a closed loop writes nothing.

using System;
using Nota.Application;

namespace Nota.Infrastructure;

public enum StrataLayerExportResult
{
    Written,    // the layer is there and finished (Check: it would be written)
    NoLoop,     // the first loop hasn't closed yet
    Recording,  // this layer is still being recorded
    NoLayer,    // no such layer (undone, cleared, never recorded)
    Invalid,    // the blob is shorter than its header says
}

public static class StrataLayerExport
{
    private const int HeaderBytes = 20, LayerInfoBytes = 12, MaxLayers = 8, LayerRecording = 2;

    /// <summary>What exporting <paramref name="layer"/> (0-based) from this blob would do.</summary>
    public static StrataLayerExportResult Check(byte[] blob, int layer) => Locate(blob, layer, out _, out _, out _);

    /// <summary>A copy of the layer's interleaved stereo PCM, one loop cycle long.</summary>
    public static StrataLayerExportResult TryReadLayer(byte[] blob, int layer, out int sampleRate, out float[] interleaved, out int frames)
    {
        interleaved = Array.Empty<float>();
        var r = Locate(blob, layer, out sampleRate, out frames, out int pcm);
        if (r != StrataLayerExportResult.Written) { frames = 0; return r; }
        interleaved = new float[frames * 2];
        Buffer.BlockCopy(blob, pcm, interleaved, 0, frames * 8);
        return r;
    }

    /// <summary>Writes the layer as a 32-bit float stereo WAV at the loop's sample rate. Any
    /// result but <see cref="StrataLayerExportResult.Written"/> leaves <paramref name="path"/> alone.</summary>
    public static StrataLayerExportResult WriteLayerWav(byte[] blob, int layer, string path)
    {
        var r = TryReadLayer(blob, layer, out int sr, out var pcm, out int frames);
        if (r != StrataLayerExportResult.Written) return r;
        using var w = new WavWriter(path, sr, 2, WavBitDepth.Float32);
        w.WriteFrames(pcm, frames);
        return r;
    }

    private static StrataLayerExportResult Locate(byte[] blob, int layer, out int sampleRate, out int frames, out int pcmOffset)
    {
        sampleRate = 0; frames = 0; pcmOffset = 0;
        if (blob is null || blob.Length < HeaderBytes) return StrataLayerExportResult.Invalid;
        int I(int at) => BitConverter.ToInt32(blob, at);
        int sr = I(4), len = I(8), lc = I(16);
        if (lc < 0 || lc > MaxLayers || len < 0) return StrataLayerExportResult.Invalid;
        if (layer < 0 || layer >= lc) return StrataLayerExportResult.NoLayer;
        if (len <= 0) return StrataLayerExportResult.NoLoop;
        if (HeaderBytes + (long)lc * LayerInfoBytes > blob.Length) return StrataLayerExportResult.Invalid;
        if (I(HeaderBytes + layer * LayerInfoBytes + 8) == LayerRecording) return StrataLayerExportResult.Recording;
        long at = HeaderBytes + (long)lc * LayerInfoBytes + (long)layer * len * 8;
        if (at + (long)len * 8 > blob.Length || at > int.MaxValue) return StrataLayerExportResult.Invalid;
        sampleRate = sr > 0 ? sr : 44100;
        frames = len;
        pcmOffset = (int)at;
        return StrataLayerExportResult.Written;
    }
}
