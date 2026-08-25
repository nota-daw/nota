// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Delay taps visualization (mockup 2h): the echo train drawn per channel — L above the
// centre axis, R below — with height = level (feedback decay) and spacing = actual
// timing. Ping-pong and dotted offsets read at a glance. The body feeds it tap fractions
// (0..1 of the graph), feedback and a ruler; this control just draws.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class DelayTaps : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Accent = NotaPalette.Accent;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush GridB = new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x36, 0x2D));
    private static readonly IBrush Axis = NotaPalette.TextDisabled;
    private static readonly Typeface Face = new(FontFamily.Default);

    private double _fracL = 0.25, _fracR = 0.25;
    private float _fb = 0.4f;
    private bool _ping;
    private string _label = "";
    private string[] _ruler = { "0", "1 bar", "2 bars" };

    public void Set(double fracL, double fracR, float feedback, bool ping, string label, string[] ruler)
    { _fracL = Math.Max(0.02, fracL); _fracR = Math.Max(0.02, fracR); _fb = feedback; _ping = ping; _label = label; _ruler = ruler; InvalidateVisual(); }

    private void Txt(DrawingContext c, string t, double x, double y, IBrush b, bool center = false)
    {
        var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b);
        c.DrawText(ft, new Point(center ? x - ft.Width / 2 : x, y));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 5, 5);
        double x0 = 8, x1 = w - 8, mid = h * 0.5, half = (h - 30) * 0.5;

        // Ruler ticks (0 / mid / end) + centre axis.
        for (int i = 0; i <= 2; i++) { double x = x0 + (x1 - x0) * i / 2.0; ctx.DrawLine(new Pen(GridB, 1), new Point(x, 14), new Point(x, h - 12)); Txt(ctx, _ruler[Math.Min(i, _ruler.Length - 1)], x, h - 10, Axis, i > 0); }
        ctx.DrawLine(new Pen(GridB, 1.2), new Point(x0, mid), new Point(x1, mid));
        Txt(ctx, _label, x0 + 2, 3, Axis);
        Txt(ctx, "L", x0 + 2, mid - half - 2, Accent);
        Txt(ctx, "R", x0 + 2, mid + half - 6, Accent);

        void Train(double frac, bool up)
        {
            for (int n = 1; n <= 16; n++)
            {
                double x = x0 + n * frac * (x1 - x0);
                if (x > x1) break;
                double lvl = Math.Pow(Math.Max(0.05f, _fb), n - 1);
                double len = Math.Max(2, lvl * half);
                var top = up ? new Point(x, mid - len) : new Point(x, mid + len);
                bool bright = _ping && ((n % 2 == 1) == up);   // ping-pong: alternate emphasis
                ctx.DrawLine(new Pen(bright ? AccentBright : Accent, 2), new Point(x, mid), top);
                ctx.DrawEllipse(bright ? AccentBright : Accent, null, top, 2, 2);
            }
        }
        Train(_fracL, true);
        Train(_fracR, false);
    }
}
