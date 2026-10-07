// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// An arrangement audio clip as a sample file, so it can be dragged onto anything that takes a
// dropped sample (a Grain, a Drum Rack pad, a Sampler…). Clip samples live in engine memory —
// a recording has no file at all, and an imported one is usually trimmed — so the clip's
// played region is written to a WAV of its own. Warped clips take the region under their
// play window (proportionally across the warp); a reversed clip is written reversed. Gain,
// ADSR and envelopes are mix-time shaping and are left out: the sample is the raw material.

using System;
using System.IO;
using System.Linq;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.App;

internal static class ClipAudioExport
{
    /// <summary>The clip's played region as a Sample browser item backed by a fresh WAV, or
    /// null when the clip has no audio.</summary>
    public static BrowserItem? AsSample(IAudioEngine engine, int trackId, int clipIndex, string name)
    {
        if (!engine.TryGetAudioClipInfo(trackId, clipIndex, out var ci) || ci.SampleId == 0) return null;
        if (!engine.TryGetSampleInfo(ci.SampleId, out var si) || si.Channels <= 0 || si.Frames <= 0) return null;

        long off = Math.Clamp((long)ci.SourceOffsetFrames, 0, si.Frames);
        long len = si.Frames - off;
        if (ci.LengthFrames > 0) len = Math.Min(len, ci.LengthFrames);
        if (ci.WarpEnabled != 0 && ci.WarpBeats > 0)
        {
            double a = Math.Clamp(ci.WarpPlayStart / ci.WarpBeats, 0, 1);
            double b = ci.WarpPlayEnd > 0 ? Math.Clamp(ci.WarpPlayEnd / ci.WarpBeats, a, 1) : 1;
            long from = (long)Math.Round(a * len);
            off += from;
            len = Math.Max(0, (long)Math.Round(b * len) - from);
        }
        if (len <= 0) return null;

        var data = engine.ReadSample(ci.SampleId);
        int ch = si.Channels;
        if (data.Length < (off + len) * ch) return null;
        var region = new float[len * ch];
        Array.Copy(data, off * ch, region, 0, region.Length);
        if (ci.Reversed != 0)
            for (long i = 0, j = len - 1; i < j; i++, j--)
                for (int k = 0; k < ch; k++)
                    (region[i * ch + k], region[j * ch + k]) = (region[j * ch + k], region[i * ch + k]);

        // A folder per drag: the file is named after the clip (instruments show it), and two
        // clips with the same name must never overwrite a file an instrument already loaded.
        string clean = new string((name.Length > 0 ? name : "Clip")
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        if (clean.Length == 0) clean = "Clip";
        string dir = Path.Combine(Path.GetTempPath(), "Nota", "Clip Samples", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, clean + ".wav");
        using (var w = new WavWriter(path, si.SampleRate > 0 ? (int)Math.Round(si.SampleRate) : 44100, ch, WavBitDepth.Float32))
            w.WriteFrames(region, (int)len);
        return new BrowserItem { Name = clean, Kind = BrowserItemKind.Sample, Path = path };
    }
}
