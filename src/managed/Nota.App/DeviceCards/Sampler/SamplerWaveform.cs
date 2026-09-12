// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Sampler's waveform view: draggable Start/End brackets +
// Loop Start/End markers, a time grid, and a live playback cursor.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class SamplerWaveform : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush Wave = NotaPalette.Accent;
    private static readonly IBrush WaveDim = NotaPalette.Wash(NotaPalette.Accent, 0x40);
    private static readonly IBrush Outside = NotaPalette.Wash(NotaPalette.BgSunken, 0x80);
    private static readonly IBrush LoopFill = NotaPalette.Wash(NotaPalette.Accent, 0x14);   // brass wash
    private static readonly IPen StartPen = new Pen(NotaPalette.Success, 2);          // start = green
    private static readonly IPen EndPen = new Pen(NotaPalette.Danger, 2);            // end = red
    private static readonly IPen LoopPen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xC8), 1.2);
    private static readonly IPen MidLine = new Pen(NotaPalette.GridBar, 1);
    private static readonly IPen GridPen = new Pen(NotaPalette.Wash(NotaPalette.BorderStrong, 0x55), 1);
    private static readonly IPen PlayPen = new Pen(NotaPalette.AccentBright, 1.4) { };
    private static readonly IBrush GridText = NotaPalette.TextTertiary;
    private static readonly Typeface Mono = new("monospace");

    private float[] _peaks = Array.Empty<float>();
    private double _start, _end = 1, _ls, _le = 1;
    private int _loopMode;
    private int _drag = -1;
    private double _dur;         // sample duration (seconds), for the time grid
    private double _play = -1;   // live playback position (0..1), -1 = hidden

    public event Action<double>? StartChanged, EndChanged, LoopStartChanged, LoopEndChanged;
    public Action? GestureBegin;

    public SamplerWaveform() { MinHeight = 70; ClipToBounds = true; }
    public void SetPeaks(float[] p) { _peaks = p; InvalidateVisual(); }
    public void SetDuration(double seconds) { _dur = seconds; InvalidateVisual(); }
    public void SetPlayhead(double frac) { if (Math.Abs(frac - _play) > 1e-4) { _play = frac; InvalidateVisual(); } }
    public void SetMarkers(double s, double e, double ls, double le, int mode)
    { _start = s; _end = e; _ls = ls; _le = le; _loopMode = mode; InvalidateVisual(); }

    private double[] Handles() => _loopMode > 0 ? new[] { _start, _end, _ls, _le } : new[] { _start, _end };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        double w = Bounds.Width; if (w <= 0) return;
        double x = e.GetPosition(this).X / w;
        var hs = Handles();
        int best = -1; double bestD = 0.04;   // ~4% of width grab radius
        for (int i = 0; i < hs.Length; i++) { double d = Math.Abs(x - hs[i]); if (d < bestD) { bestD = d; best = i; } }
        _drag = best;
        if (_drag >= 0) { GestureBegin?.Invoke(); e.Pointer.Capture(this); Apply(x); e.Handled = true; }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    { if (_drag >= 0 && Bounds.Width > 0) Apply(Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag = -1; e.Pointer.Capture(null); }

    private void Apply(double f)
    {
        f = Math.Clamp(f, 0, 1);
        switch (_drag)
        {
            case 0: StartChanged?.Invoke(Math.Min(f, _end - 0.001)); break;
            case 1: EndChanged?.Invoke(Math.Max(f, _start + 0.001)); break;
            case 2: LoopStartChanged?.Invoke(Math.Min(f, _le - 0.001)); break;
            case 3: LoopEndChanged?.Invoke(Math.Max(f, _ls + 0.001)); break;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Bg, null, new Rect(0, 0, w, h), 4, 4);
        double cy = h / 2;

        // Time grid (behind the waveform): vertical lines + labels at a nice step.
        if (_dur > 0)
        {
            double step = _dur <= 0.5 ? 0.05 : _dur <= 1.5 ? 0.1 : _dur <= 5 ? 0.5 : _dur <= 15 ? 1.0 : 5.0;
            for (double t = step; t < _dur; t += step)
            {
                double x = t / _dur * w;
                ctx.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
                string lbl = _dur >= 1 ? $"{t:0.##}s" : $"{(int)Math.Round(t * 1000)}";
                ctx.DrawText(new FormattedText(lbl, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, GridText), new Point(x + 2, h - 11));
            }
        }

        int n = _peaks.Length / 2;
        if (n > 0)
        {
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n * w;
                double frac = (double)i / n;
                bool inWin = frac >= _start && frac <= _end;
                double y0 = cy - _peaks[i * 2 + 1] * (cy - 2);
                double y1 = cy - _peaks[i * 2] * (cy - 2);
                ctx.DrawLine(new Pen(inWin ? Wave : WaveDim, 1), new Point(x, y0), new Point(x, y1));
            }
        }
        ctx.DrawLine(MidLine, new Point(0, cy), new Point(w, cy));

        // Dim the region outside [start,end].
        if (_start > 0) ctx.FillRectangle(Outside, new Rect(0, 0, _start * w, h));
        if (_end < 1) ctx.FillRectangle(Outside, new Rect(_end * w, 0, (1 - _end) * w, h));
        // Loop region + markers.
        if (_loopMode > 0)
        {
            ctx.FillRectangle(LoopFill, new Rect(_ls * w, 0, Math.Max(1, (_le - _ls) * w), h));
            ctx.DrawLine(LoopPen, new Point(_ls * w, 0), new Point(_ls * w, h));
            ctx.DrawLine(LoopPen, new Point(_le * w, 0), new Point(_le * w, h));
        }
        // Start (green) / End (red) brackets.
        ctx.DrawLine(StartPen, new Point(_start * w, 0), new Point(_start * w, h));
        ctx.DrawLine(EndPen, new Point(_end * w, 0), new Point(_end * w, h));
        // Live playback cursor.
        if (_play >= 0 && _play <= 1) ctx.DrawLine(PlayPen, new Point(_play * w, 0), new Point(_play * w, h));
    }
}
