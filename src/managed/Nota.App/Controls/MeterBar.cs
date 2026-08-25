// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
using Nota.Application;
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// M6-2: stereo peak/RMS level meter. Fed a NotaMeter each UI tick (~30 Hz);
// the bar rises instantly and falls with a fixed decay, with a slower peak-hold
// tick. dB-mapped (-60..0) with green/amber/red zones. Works vertical (track
// headers) or horizontal (master bus in the transport bar).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

public sealed class MeterBar : Control
{
    // Ember Graphite meter colours (NotaTheme: Success / Warning / Danger, BgSunken).
    private static readonly IBrush GreenBrush = NotaPalette.Success;
    private static readonly IBrush AmberBrush = NotaPalette.Warning;
    private static readonly IBrush RedBrush   = NotaPalette.Danger;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush HoldBrush = NotaPalette.TextPrimary;

    private const double DbFloor = -60.0;
    private const double AmberDb = (-6.0 - DbFloor) / (0.0 - DbFloor);   // −6 dB, normalised
    private const double FallPerTick = 0.05;      // ~ full fall in ~0.7 s at 30 Hz
    private const double HoldFallPerTick = 0.012; // peak-hold drifts down slowly

    private readonly bool _horizontal;
    private double _levelL, _levelR;       // displayed (decayed) 0..1
    private double _holdL, _holdR;         // peak-hold 0..1

    public MeterBar(bool horizontal = false)
    {
        _horizontal = horizontal;
        if (horizontal) { Height = 12; MinWidth = 80; }
        else { Width = 10; MinHeight = 40; }
    }

    /// <summary>Feed a fresh reading; the bar attacks up instantly and decays down.</summary>
    public void Push(NotaMeter m)
    {
        _levelL = Step(_levelL, Norm(m.PeakL), FallPerTick);
        _levelR = Step(_levelR, Norm(m.PeakR), FallPerTick);
        _holdL = Math.Max(_levelL, _holdL - HoldFallPerTick);
        _holdR = Math.Max(_levelR, _holdR - HoldFallPerTick);
        InvalidateVisual();
    }

    /// <summary>Clears the meter to silence (e.g. on stop).</summary>
    public void Reset()
    {
        _levelL = _levelR = _holdL = _holdR = 0;
        InvalidateVisual();
    }

    private static double Step(double cur, double target, double fall)
        => target >= cur ? target : Math.Max(target, cur - fall);

    private static double Norm(float amp)
    {
        if (amp <= 1e-5f) return 0;
        double db = AudioMath.LinToDb(amp);
        return Math.Clamp((db - DbFloor) / (0.0 - DbFloor), 0.0, 1.0);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        if (_horizontal)
        {
            double gap = 2, chH = (h - gap) / 2;
            DrawChannel(ctx, new Rect(0, 0, w, chH), _levelL, _holdL);
            DrawChannel(ctx, new Rect(0, chH + gap, w, chH), _levelR, _holdR);
        }
        else
        {
            double gap = 2, chW = (w - gap) / 2;
            DrawChannel(ctx, new Rect(0, 0, chW, h), _levelL, _holdL);
            DrawChannel(ctx, new Rect(chW + gap, 0, chW, h), _levelR, _holdR);
        }
    }

    // Discrete LED segments (HANDOFF §4: "segments, not gradients"). Lit cells
    // are green; the peak-hold tick turns amber ≥ −6 dB and red at clip (0 dB).
    private void DrawChannel(DrawingContext ctx, Rect area, double level, double hold)
    {
        ctx.DrawRectangle(Sunken, null, area, 2, 2);
        double longLen = _horizontal ? area.Width : area.Height;
        if (longLen <= 0) return;
        const double gap = 1;
        int cells = Math.Max(6, (int)(longLen / 4));
        double cell = (longLen - (cells - 1) * gap) / cells;
        if (cell <= 0) return;

        for (int i = 0; i < cells; i++)
        {
            if ((i + 0.5) / cells > level) break;   // lit up to the level
            double off = i * (cell + gap);
            Rect r = _horizontal
                ? new Rect(area.X + off, area.Y, cell, area.Height)
                : new Rect(area.X, area.Bottom - off - cell, area.Width, cell);
            ctx.FillRectangle(GreenBrush, r);
        }

        if (hold > 0.02)
        {
            var tick = hold >= AmberDb ? (hold >= 0.995 ? RedBrush : AmberBrush) : HoldBrush;
            const double t = 2;
            Rect tr = _horizontal
                ? new Rect(area.X + area.Width * hold - t, area.Y, t, area.Height)
                : new Rect(area.X, area.Bottom - area.Height * hold - t, area.Width, t);
            ctx.FillRectangle(tick, tr);
        }
    }
}
