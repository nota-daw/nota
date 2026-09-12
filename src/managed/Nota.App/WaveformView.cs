// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

/// <summary>
/// Draws a clip's min/max waveform peaks. Peaks are fetched once from the engine
/// (message thread) and cached; rendering uses Avalonia's Skia-backed
/// DrawingContext (NFR-4).
/// </summary>
public sealed class WaveformView : Control
{
    private float[]? _peaks;
    private int _buckets;

    // Design tokens (see .claude/skills/nota-design — Data colors).
    private static readonly IBrush Background = NotaPalette.BgSunken;
    private static readonly IPen WavePen = new Pen(NotaPalette.Accent, 1);
    private static readonly IPen MidPen = new Pen(NotaPalette.BorderDefault, 1);

    public void SetPeaks(float[] minMax, int buckets)
    {
        _peaks = minMax;
        _buckets = buckets;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);

        double w = bounds.Width, h = bounds.Height, mid = h / 2.0;
        context.DrawLine(MidPen, new Point(0, mid), new Point(w, mid));

        if (_peaks is null || _buckets <= 0) return;

        for (int i = 0; i < _buckets; i++)
        {
            double x = i * w / _buckets;
            float mn = _peaks[i * 2];
            float mx = _peaks[i * 2 + 1];
            double y1 = mid - mx * mid;
            double y2 = mid - mn * mid;
            context.DrawLine(WavePen, new Point(x, y1), new Point(x, y2));
        }
    }
}
