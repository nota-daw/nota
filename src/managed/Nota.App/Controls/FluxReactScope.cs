// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Flux "React" scope: a rolling view of what the synth hears on
// its sidechain, in a graph window (NotaGraph). Teal above the centre line = the envelope
// follower; the neutral line below = the spectral tilt (brighter material swings further
// down); teal verticals = detected transients. A narrow well to the right is the reaction
// itself — how hard the target is being driven right now. With no source the window is
// neutral: a flat line and one Ink 6 sentence. Fed one sample per UI tick via Push().

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class FluxReactScope : Control
{
    private const int N = 150;
    private const double BarW = 8, Gap = 4;
    private readonly float[] _env = new float[N];
    private readonly float[] _tilt = new float[N];
    private readonly float[] _tr = new float[N];
    private int _head;
    private float _react;
    private bool _live;

    public FluxReactScope() { ClipToBounds = true; }

    /// <summary>Whether a source is assigned. Off clears the history.</summary>
    public bool Live
    {
        get => _live;
        set
        {
            if (_live == value) return;
            _live = value;
            if (!value) { Array.Clear(_env); Array.Clear(_tilt); Array.Clear(_tr); _react = 0; }
            InvalidateVisual();
        }
    }

    public void Push(float env, float transient, float tilt, float react)
    {
        _env[_head] = Math.Clamp(env * 3.5f, 0, 1);   // the engine's own env → 0..1 scaling
        _tilt[_head] = Math.Clamp(tilt, 0, 1);
        _tr[_head] = Math.Clamp(transient, 0, 1);
        _head = (_head + 1) % N;
        _react = Math.Clamp(react, 0, 1);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < BarW + Gap + 8 || h < 12) return;
        var plot = new Rect(0, 0, w - BarW - Gap, h);
        NotaGraph.Window(ctx, plot);
        double mid = Math.Round(h / 2) + 0.5, half = h * 0.40;

        // centre line and the two quarter lines, dashed.
        var quarter = new Pen(NotaPalette.GridBeat, 1, new DashStyle(new double[] { 2, 3 }, 0));
        ctx.DrawLine(NotaGraph.GridPen, new Point(1, mid), new Point(plot.Width - 1, mid));
        ctx.DrawLine(quarter, new Point(1, Math.Round(h / 4) + 0.5), new Point(plot.Width - 1, Math.Round(h / 4) + 0.5));
        ctx.DrawLine(quarter, new Point(1, Math.Round(h * 3 / 4) + 0.5), new Point(plot.Width - 1, Math.Round(h * 3 / 4) + 0.5));

        using (ctx.PushClip(plot.Deflate(1)))
        {
            if (!_live)
                ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1.4), new Point(1, mid), new Point(plot.Width - 1, mid));
            else
            {
                double X(int i) => 1 + (plot.Width - 2) * i / (N - 1);

                // transients: a teal vertical on each rising edge past the threshold.
                var tick = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x90), 1);
                for (int i = 1; i < N; i++)
                {
                    int idx = (_head + i) % N, prev = (_head + i - 1) % N;
                    if (_tr[idx] > 0.35f && _tr[prev] <= 0.35f)
                    {
                        double x = Math.Round(X(i)) + 0.5;
                        ctx.DrawLine(tick, new Point(x, 1), new Point(x, h - 1));
                    }
                }

                // tilt below the centre (neutral), envelope above it (teal).
                ctx.DrawGeometry(null, NotaGraph.SecondaryPen(NotaPalette.BorderStrong, 1.2), Curve(i => mid + 2 + _tilt[i] * (half - 2), X));
                ctx.DrawGeometry(null, NotaGraph.SecondaryPen(NotaPalette.Teal, 1.6), Curve(i => mid - 2 - _env[i] * (half - 2), X));
            }
        }

        var lbl = new FormattedText("SIDECHAIN", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.SansBold,
            NotaType.KnobLabel, _live ? NotaPalette.TextTertiary : NotaPalette.TextDisabled);
        ctx.DrawText(lbl, new Point(6, 4));
        NotaGraph.Axis(ctx, plot, NotaGraph.Corner.TopRight, "env · transients · tilt", _live ? NotaPalette.TealBright : NotaPalette.TextAxis);
        if (!_live)
        {
            var none = new FormattedText("no source assigned", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans,
                NotaType.KnobLabel, NotaPalette.TextDisabled);
            ctx.DrawText(none, new Point((plot.Width - none.Width) / 2, h - 8 - none.Height));
        }

        // the reaction, in its own narrow well.
        var bar = new Rect(w - BarW, 0, BarW, h);
        ctx.DrawRectangle(NotaPalette.BgSunken, NotaGraph.FramePen, new RoundedRect(bar.Deflate(0.5), NotaRadius.ClipValue));
        if (_live && _react > 0.005f)
        {
            double fill = (h - 4) * _react;
            ctx.FillRectangle(NotaPalette.Teal, new Rect(bar.X + 2, h - 2 - fill, BarW - 4, fill), (float)NotaRadius.ClipValue);
        }
    }

    private StreamGeometry Curve(Func<int, double> y, Func<int, double> x)
    {
        var g = new StreamGeometry();
        using var gc = g.Open();
        for (int i = 0; i < N; i++)
        {
            int idx = (_head + i) % N;
            var p = new Point(x(i), y(idx));
            if (i == 0) gc.BeginFigure(p, false); else gc.LineTo(p);
        }
        gc.EndFigure(false);
        return g;
    }
}
