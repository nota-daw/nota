// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Ceiling (mockup 3o) visualizers. A shared ring of the last ~4 seconds of
// per-tick meters (input peak / limited output / gain reduction, all dB) feeds two
// controls: the LEVEL plot (input behind, output in brass, the excess above the
// ceiling in red, the ceiling drawn as a bright line) and the GAIN REDUCTION lane
// (bars hanging from the top in teal, amber past −3 dB). The device body samples
// the engine scope each 60 Hz tick and pushes one column; both controls redraw.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

/// <summary>Rolling 4-second history of the limiter's meters, newest at the head.</summary>
internal sealed class CeilingMeters
{
    public const int N = 160;                 // columns (~4 s at 60 Hz, decimated)
    public readonly float[] In = Fill(-120), Out = Fill(-120), Gr = new float[N];
    public float Ceiling = -1f;               // current ceiling dB, for the plot line

    private static float[] Fill(float v) { var a = new float[N]; for (int i = 0; i < N; i++) a[i] = v; return a; }

    public void Push(float inDb, float outDb, float grDb)
    {
        Array.Copy(In, 1, In, 0, N - 1); In[N - 1] = inDb;
        Array.Copy(Out, 1, Out, 0, N - 1); Out[N - 1] = outDb;
        Array.Copy(Gr, 1, Gr, 0, N - 1); Gr[N - 1] = grDb;
    }
}

internal sealed class CeilingLevelPlot : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush Border = NotaPalette.GraphBorder;
    private static readonly IBrush InC = NotaPalette.SignalInFill;
    private static readonly IBrush OutC = NotaPalette.Wash(NotaPalette.Accent, 0xD9);
    private static readonly IBrush OverC = NotaPalette.Wash(NotaPalette.Danger, 0x99);
    private static readonly IBrush CeilC = NotaPalette.AccentBright;
    private static readonly IBrush Axis = NotaPalette.TextDisabled;
    private static readonly Typeface Face = new(FontFamily.Default);
    private const double Lo = -30, Hi = 3;    // dBFS display range

    private readonly CeilingMeters _m;
    public CeilingLevelPlot(CeilingMeters m) { _m = m; }
    public void Tick() => InvalidateVisual();
    private static double Frac(double db) => Math.Clamp((db - Lo) / (Hi - Lo), 0, 1);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(Border, 1), new Rect(0, 0, w, h), 6, 6);
        double padT = 14, padB = 12, padX = 6;
        double x0 = padX, x1 = w - padX, top = padT, bot = h - padB;
        double plotH = Math.Max(1, bot - top), plotW = Math.Max(1, x1 - x0);
        int n = CeilingMeters.N; double bw = plotW / n;

        double ceilY = bot - Frac(_m.Ceiling) * plotH;
        for (int i = 0; i < n; i++)
        {
            double bx = x0 + i * bw, bwi = Math.Max(0.6, bw - 0.6);
            double inF = Frac(_m.In[i]), outF = Frac(_m.Out[i]);
            double inY = bot - inF * plotH, outY = bot - outF * plotH;
            if (inF > 0.001) ctx.FillRectangle(InC, new Rect(bx, inY, bwi, bot - inY));
            if (inY < ceilY) ctx.FillRectangle(OverC, new Rect(bx, inY, bwi, ceilY - inY)); // excess above ceiling
            if (outF > 0.001) ctx.FillRectangle(OutC, new Rect(bx, outY, bwi, bot - outY));
        }
        // Ceiling line + label.
        ctx.DrawLine(new Pen(CeilC, 1), new Point(x0, ceilY), new Point(x1, ceilY));

        void Txt(string t, double x, double y, IBrush b, double fs = 8) =>
            ctx.DrawText(new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, fs, b), new Point(x, y));
        Txt("LEVEL — 4 s", x0 + 1, 2, NotaPalette.TextTertiary);
        Txt("input", x1 - 118, 2, InC); Txt("output", x1 - 78, 2, OutC); Txt("over", x1 - 30, 2, OverC);
        Txt($"ceiling {_m.Ceiling:0.0}", x1 - 54, ceilY - 9, CeilC, 7);
        Txt("−4 s", 1, bot + 1, Axis); Txt("now", x1 - 18, bot + 1, Axis);
    }
}

internal sealed class CeilingGrLane : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush Border = NotaPalette.GraphBorder;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush Amber = NotaPalette.Warning;
    private static readonly Typeface Face = new(FontFamily.Default);
    private const double MaxGr = 6.0;

    private readonly CeilingMeters _m;
    public CeilingGrLane(CeilingMeters m) { _m = m; }
    public void Tick() => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(Border, 1), new Rect(0, 0, w, h), 6, 6);
        double top = 13, bot = h - 3, padX = 6;
        double x0 = padX, x1 = w - padX, laneH = Math.Max(1, bot - top), plotW = Math.Max(1, x1 - x0);
        int n = CeilingMeters.N; double bw = plotW / n;
        for (int i = 0; i < n; i++)
        {
            double frac = Math.Clamp(_m.Gr[i] / MaxGr, 0, 1);
            if (frac < 0.002) continue;
            double bx = x0 + i * bw, bwi = Math.Max(0.6, bw - 0.6);
            ctx.FillRectangle(_m.Gr[i] > 3.0 ? Amber : Teal, new Rect(bx, top, bwi, frac * laneH));
        }
        void Txt(string t, double x, double y, IBrush b) =>
            ctx.DrawText(new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));
        Txt("GAIN REDUCTION", x0 + 1, 2, Teal);
        Txt("0 … −6 dB", x1 - 48, 2, NotaPalette.TextDisabled);
    }
}
