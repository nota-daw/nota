// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Physical's resonator window — the modal partials of the selected bank exactly as
// the engine tunes them (its scope telemetry: ratio to the note, struck amplitude, ring
// time). Frequency runs on a log axis from the fundamental, bar height is the struck
// amplitude and a bar's ink fades with how much sooner that partial dies than the
// fundamental. Partials above Nyquist for the reference note are left out. The other
// bank, when it sounds, is drawn as faint hairlines behind. A drag is a control: sideways
// spreads or squeezes the series (Ratio), up / down tilts the highs (Bright).

using System;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

internal sealed class PhysPartialsView : VtWindowBase
{
    private (double Ratio, double Amp, double Tau)[] _modes = Array.Empty<(double, double, double)>();
    private (double Ratio, double Amp, double Tau)[] _ghost = Array.Empty<(double, double, double)>();
    private string _title = "PARTIALS", _footer = "";
    private double _maxRatio = double.PositiveInfinity;
    private bool _dim;

    /// <summary>Show a bank. <paramref name="maxRatio"/> = the highest ratio still under
    /// Nyquist for the reference note; <paramref name="ghost"/> = the other bank, or empty.</summary>
    public void Set(string title, (double, double, double)[] modes, (double, double, double)[] ghost,
        double maxRatio, string footer, bool dim)
    {
        if (title == _title && footer == _footer && dim == _dim && maxRatio == _maxRatio
            && Same(modes, _modes) && Same(ghost, _ghost)) return;
        _title = title; _modes = modes; _ghost = ghost; _maxRatio = maxRatio; _footer = footer; _dim = dim;
        InvalidateVisual();
    }

    private static bool Same((double, double, double)[] a, (double, double, double)[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        double x0 = 8, x1 = w - 8, top = 16, bot = h - 12;
        for (int i = 1; i <= 3; i++)
        {
            double gy = top + (bot - top) * i / 4.0;
            ctx.DrawLine(NotaGraph.GridPen, new Point(0, gy), new Point(w, gy));
        }

        // The axis spans both banks' audible series (at least three octaves), from the
        // lower of the two fundamentals, so the ghost lines up with what it'd sound against.
        double lo = double.MaxValue, hi = 0, maxAmp = 1e-9;
        void Scan((double Ratio, double Amp, double Tau)[] ms, bool amp)
        {
            foreach (var m in ms)
            {
                if (m.Ratio <= 0 || m.Ratio > _maxRatio) continue;
                lo = Math.Min(lo, m.Ratio); hi = Math.Max(hi, m.Ratio);
                if (amp) maxAmp = Math.Max(maxAmp, m.Amp);
            }
        }
        Scan(_modes, true); Scan(_ghost, false);
        if (hi > 0)
        {
            double oLo = Math.Log2(lo), span = Math.Max(3.0, Math.Log2(hi) - oLo);
            double X(double r) => x0 + (Math.Log2(r) - oLo) / span * (x1 - x0);

            double ghostMax = 1e-9;
            foreach (var m in _ghost) ghostMax = Math.Max(ghostMax, m.Amp);
            var ghostPen = new Pen(NotaPalette.Wash(NotaPalette.TextTertiary, 0x66), 1);
            foreach (var m in _ghost)
            {
                if (m.Ratio <= 0 || m.Ratio > _maxRatio || m.Amp < ghostMax * 0.02) continue;
                double x = X(m.Ratio);
                ctx.DrawLine(ghostPen, new Point(x, bot), new Point(x, bot - m.Amp / ghostMax * (bot - top)));
            }

            var ink = _dim ? NotaPalette.TextDisabled : NotaPalette.Accent;
            double tau0 = _modes.Length > 0 ? Math.Max(1e-3, _modes[0].Tau) : 1;
            for (int i = 0; i < _modes.Length; i++)
            {
                var m = _modes[i];
                if (m.Ratio <= 0 || m.Ratio > _maxRatio || m.Amp < maxAmp * 0.02) continue;
                double x = X(m.Ratio), bh = m.Amp / maxAmp * (bot - top);
                // A partial that dies at once is drawn at ~35 % ink; one that rings as long
                // as the fundamental at full.
                byte a = (byte)Math.Clamp(90 + 165 * Math.Min(1, m.Tau / tau0), 60, 255);
                var c = ((ISolidColorBrush)ink).Color;
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)), null, new Rect(x - 1.5, bot - bh, 3, bh), 1, 1);
                if (i == 0) ctx.DrawEllipse(_dim ? NotaPalette.TextDisabled : NotaPalette.AccentBright, null, new Point(x, bot - bh - 3), 1.8, 1.8);
            }
        }

        ctx.DrawText(NotaGraph.AxisText(_title, NotaGraph.TitleInk), new Point(6, 3));
        if (_footer.Length > 0)
        {
            var ft = NotaGraph.AxisText(_footer, NotaGraph.AxisInk);
            ctx.DrawText(ft, new Point(w - ft.Width - 6, h - ft.Height - 1));
        }
    }
}
