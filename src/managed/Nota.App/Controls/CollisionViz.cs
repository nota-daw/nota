// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Modal partial spectrum for Nota Physical's resonator: draws the tuned mode series
// (frequency on a log axis, bar height = struck amplitude) exactly as the DSP freezes
// it from Type / Decay / Material / Bright / Inharm / Ratio / Hit — so the picture the
// user sees is the resonator they hear. Non-interactive readout, app-styled.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class CollisionViz : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush Grid = NotaPalette.Wash(NotaPalette.BorderStrong, 0x50);
    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly string[] TypeNames = { "BEAM", "MARIMBA", "STRING", "MEMBRANE", "PLATE", "PIPE" };

    private const int Modes = 16;
    private int _type;
    private double _decay, _material, _bright, _inharm, _ratio, _hit;

    public CollisionViz() { MinWidth = 150; MinHeight = 60; }

    public void Set(int type, double decay, double material, double bright, double inharm, double ratio, double hit)
    {
        _type = Math.Clamp(type, 0, 5); _decay = decay; _material = material;
        _bright = bright; _inharm = inharm; _ratio = ratio; _hit = hit;
        InvalidateVisual();
    }

    private static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0.0, 1.0));

    // Partial ratios per material Type (mode 0 = fundamental = 1.0). Mirrors PhysicalSynth::ratioOf.
    private static double RatioOf(int type, int m)
    {
        double[] beam = { 1.0, 2.7565, 5.4039, 8.9330, 13.3443, 18.6379, 24.8137, 31.8718, 39.8122, 48.6349, 58.3399, 68.9272, 80.3968, 92.7487, 105.983, 120.099 };
        double[] marimba = { 1.0, 3.984, 9.531, 17.65, 28.10, 41.0, 56.4, 74.3, 94.7, 117.6, 143.0, 170.9, 201.3, 234.2, 269.6, 307.5 };
        double[] membrane = { 1.0, 1.5933, 2.1355, 2.2954, 2.6531, 2.9173, 3.1555, 3.5001, 3.5985, 3.6521, 3.8452, 4.0602, 4.1057, 4.2323, 4.6018, 4.8321 };
        return type switch
        {
            0 => beam[m],
            1 => marimba[m],
            2 => m + 1,
            3 => membrane[m],
            4 => Math.Sqrt(m + 1),
            _ => 2 * m + 1,
        };
    }

    private void Label(DrawingContext ctx, string text, double x, double y, IBrush brush, double size = 8)
        => ctx.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush), new Point(x, y));

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 4, 4);
        Label(ctx, "PARTIALS · " + TypeNames[_type], 6, 3, TextTertiary);

        double pad = 6, x0 = pad, x1 = w - pad, top = pad + 12, bot = h - pad - 8;

        // Compute mode frequencies (as octave offsets from the fundamental) and amplitudes,
        // exactly as buildBank does: inharmonic stretch, ratio exponent, bright tilt, hit comb.
        double ratioExp = 0.5 + _ratio;
        double stretch = _inharm * 0.04;
        double brightRoll = ExpMap(_bright, 0.55, 1.0);
        var oct = new double[Modes];
        var amp = new double[Modes];
        double maxOct = 0.001, maxAmp = 1e-6;
        for (int m = 0; m < Modes; m++)
        {
            double r = RatioOf(_type, m) * (1.0 + stretch * m);
            r = Math.Pow(r, ratioExp);
            oct[m] = Math.Log2(Math.Max(r, 1e-6));
            amp[m] = Math.Pow(brightRoll, m) * (0.3 + 0.7 * Math.Abs(Math.Sin((m + 1) * Math.PI * (0.02 + 0.96 * _hit))));
            if (oct[m] > maxOct) maxOct = oct[m];
            if (amp[m] > maxAmp) maxAmp = amp[m];
        }
        double span = Math.Max(maxOct, 3.0);

        // Faint horizontal grid.
        var gridPen = new Pen(Grid, 1);
        for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy)); }
        ctx.DrawLine(new Pen(BorderDef, 1), new Point(x0, bot), new Point(x1, bot));

        // Higher modes ring out faster (material), so fade the bars toward the top of the series.
        double matRoll = ExpMap(_material, 1.0, 0.45);
        for (int m = 0; m < Modes; m++)
        {
            if (amp[m] < maxAmp * 0.02) continue;
            double x = x0 + (oct[m] / span) * (x1 - x0);
            if (x < x0 || x > x1) continue;
            double barH = (amp[m] / maxAmp) * (bot - top);
            byte a = (byte)Math.Clamp(90 + 165 * Math.Pow(matRoll, m), 40, 255);
            var brush = new SolidColorBrush(Color.FromArgb(a, 0xF0, 0xC0, 0x60));
            ctx.DrawRectangle(brush, null, new Rect(x - 1.4, bot - barH, 2.8, barH), 1, 1);
            if (m == 0) ctx.DrawEllipse(AccentBright, null, new Point(x, bot - barH - 2.5), 1.6, 1.6);
        }
    }
}
