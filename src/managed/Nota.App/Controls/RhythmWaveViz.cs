// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Rhythm — the sample-mode waveform preview shown in the voice panel when a voice loads
// a one-shot. A simple filled peak envelope computed from the decoded sample (interleaved).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class RhythmWaveViz : Control
{
    private float[] _peaks = System.Array.Empty<float>();   // per-column max |amp|, 0..1
    private static readonly IBrush Fill = NotaPalette.Wash(NotaPalette.Teal, 0xD9);
    private static readonly IPen Mid = new Pen(NotaPalette.BorderDefault, 1);

    public void SetSamples(float[] interleaved, int channels)
    {
        int cols = 140;
        var p = new float[cols];
        if (interleaved.Length > 0 && channels > 0)
        {
            long frames = interleaved.Length / channels;
            for (int c = 0; c < cols; c++)
            {
                long a = frames * c / cols, b = System.Math.Max(a + 1, frames * (c + 1) / cols);
                float mx = 0;
                for (long f = a; f < b; f++)
                    for (int ch = 0; ch < channels; ch++)
                    { float v = System.Math.Abs(interleaved[f * channels + ch]); if (v > mx) mx = v; }
                p[c] = System.Math.Min(1f, mx);
            }
        }
        _peaks = p;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, mid = h * 0.5;
        if (w < 4 || h < 4) return;
        ctx.DrawLine(Mid, new Point(0, mid), new Point(w, mid));
        if (_peaks.Length == 0) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(0, mid), true);
            for (int i = 0; i < _peaks.Length; i++) { double x = w * i / (_peaks.Length - 1); g.LineTo(new Point(x, mid - _peaks[i] * (mid - 1))); }
            for (int i = _peaks.Length - 1; i >= 0; i--) { double x = w * i / (_peaks.Length - 1); g.LineTo(new Point(x, mid + _peaks[i] * (mid - 1))); }
            g.EndFigure(true);
        }
        ctx.DrawGeometry(Fill, null, geo);
    }
}
