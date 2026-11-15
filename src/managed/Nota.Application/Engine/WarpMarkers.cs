// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Every warp marker of a clip. The engine reports how many a clip has whatever the buffer
// size, and "To transients" alone pins up to 258, so a fixed buffer lost the ones past it:
// the clip editor then measured the warp by the last marker it saw (its playhead ran ahead
// of the waveform, #537) and a save dropped the rest of the markers.

using System;

namespace Nota.Application;

public static class WarpMarkers
{
    /// <summary>The clip's markers in source-frame order (empty when it has none to report).</summary>
    public static (double[] Src, double[] Beat) Read(IAudioEngine engine, int trackId, int clipIndex)
    {
        int cap = 128;
        while (true)
        {
            var src = new double[cap];
            var beat = new double[cap];
            int n = engine.GetClipWarpMarkers(trackId, clipIndex, src, beat);
            if (n <= 0) return (Array.Empty<double>(), Array.Empty<double>());
            if (n <= cap) return (src[..n], beat[..n]);
            cap = n;   // more than the buffer held: read again with room for all of them
        }
    }
}
