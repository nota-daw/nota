// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Strata (mockup 3n) per-layer waveform strip. A peak envelope of one loop
// layer, drawn as vertical bars in the layer's palette colour — greyed when the
// layer is muted, and (while that layer is the one recording) drawn only up to the
// playhead, with the rest dark because it hasn't happened yet. The device body
// pushes a fresh envelope each 60 Hz tick from the engine's layerWave query.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class StrataWave : Control
{
    private float[] _env = Array.Empty<float>();
    private Color _color = NotaPalette.InkColor("#C99C55");
    private bool _muted, _recording;
    private float _progress = 1f;   // 0..1 of the loop that has been recorded (rec layer)

    public void Set(float[] env, Color color, bool muted, bool recording, float progress)
    {
        _env = env; _color = color; _muted = muted; _recording = recording; _progress = progress;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0 || _env.Length == 0) return;
        int n = _env.Length; double bw = w / n, mid = h / 2;
        var full = _muted ? NotaPalette.BorderStrong
                          : new SolidColorBrush(_recording ? NotaPalette.Record.Color : Color.FromArgb(0xCC, _color.R, _color.G, _color.B));
        var future = NotaPalette.GraphBorder;
        double cut = _recording ? _progress * n : n;
        for (int i = 0; i < n; i++)
        {
            double bx = i * bw, bwi = Math.Max(0.7, bw - 0.6);
            double bh = Math.Max(1.0, Math.Clamp(_env[i], 0f, 1f) * (h - 2));
            var brush = i < cut ? full : future;
            if (i >= cut) bh = Math.Max(1.0, h * 0.04);
            ctx.FillRectangle(brush, new Rect(bx, mid - bh / 2, bwi, bh));
        }
    }
}
