// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota AutoGain (device kind 18) loudness history — the signature viz. It samples the
// device's instantaneous meters each UI tick and scrolls them right→left: input loudness
// as an olive filled area, the gain-matched output as a brass line, and the target as a
// teal dashed line (which itself moves when a sidechain reference is driving the target).
// An annotation shows the live correction ("correcting +6.0 → +4.3 dB"). Read-only.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class AutoGainHistory : Control
{
    // scope layout — must match AutoGain.h enum.
    private const int S_InLufs = 0, S_OutLufs = 1, S_Target = 3, S_Applied = 4, S_Desired = 8, kScope = 9;
    private const int N = 480;                 // ~8 s of history at 60 Hz
    private const double LTop = -4.0, LBot = -44.0;

    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush BorderB = NotaPalette.BorderDefault;
    private static readonly IPen Grid = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x1E, 0x1C, 0x18)), 1);
    private static readonly IBrush InFill = new SolidColorBrush(Color.FromArgb(0x8C, 0x3E, 0x4A, 0x3C));
    private static readonly IPen InPen = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x52, 0x60, 0x50)), 1);
    private static readonly IPen OutPen = new Pen(NotaPalette.Accent, 1.8);
    private static readonly IPen TargetPen = new Pen(NotaPalette.Teal, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush Correcting = new SolidColorBrush(Color.Parse("#C48A6A"));
    private static readonly IBrush TealBright = new SolidColorBrush(Color.Parse("#7FC9C6"));
    private static readonly IBrush AxisB = NotaPalette.TextTertiary;
    private static readonly Typeface Face = new(FontFamily.Default);

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private readonly float[] _in = new float[N], _out = new float[N], _tar = new float[N];
    private readonly float[] _scope = new float[kScope];
    private int _w; private bool _primed;
    private float _desired, _applied;

    public AutoGainHistory(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        MinHeight = 60; ClipToBounds = true;
        for (int i = 0; i < N; i++) { _in[i] = -120; _out[i] = -120; _tar[i] = -14; }
    }

    public void Tick()
    {
        int n = _engine.DeviceScope(_track, _device, _scope, kScope);
        if (n >= kScope)
        {
            _in[_w] = _scope[S_InLufs]; _out[_w] = _scope[S_OutLufs]; _tar[_w] = _scope[S_Target];
            _desired = _scope[S_Desired]; _applied = _scope[S_Applied];
            _w = (_w + 1) % N; _primed = true;
        }
        InvalidateVisual();
    }

    private static double Y(double lufs, double h) => Math.Clamp((LTop - lufs) / (LTop - LBot) * h, 0, h);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Bg, new Pen(BorderB, 1), new Rect(0, 0, w, h), 6, 6);
        double gx = 6, gy = 14, gw = w - 12, gh = h - gy - 12;
        if (gw <= 0 || gh <= 0) return;

        foreach (double lu in new[] { -10.0, -20, -30 }) ctx.DrawLine(Grid, new Point(gx, gy + Y(lu, gh)), new Point(gx + gw, gy + Y(lu, gh)));

        double X(int i) => gx + (double)i / (N - 1) * gw;
        int Idx(int i) => (_w + i) % N;   // oldest → newest

        if (_primed)
        {
            // input area (olive)
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(gx, gy + gh), true);
                for (int i = 0; i < N; i++) g.LineTo(new Point(X(i), gy + Y(_in[Idx(i)], gh)));
                g.LineTo(new Point(gx + gw, gy + gh)); g.EndFigure(true);
            }
            ctx.DrawGeometry(InFill, InPen, area);

            // target dashed teal (follows the reference when sidechained)
            Point tp = default; bool has = false;
            for (int i = 0; i < N; i += 3) { var p = new Point(X(i), gy + Y(_tar[Idx(i)], gh)); if (has) ctx.DrawLine(TargetPen, tp, p); tp = p; has = true; }

            // output brass line
            Point op = default; has = false;
            for (int i = 0; i < N; i++) { var p = new Point(X(i), gy + Y(_out[Idx(i)], gh)); if (has && _out[Idx(i)] > -119) ctx.DrawLine(OutPen, op, p); op = p; has = true; }
        }

        void Lbl(string s, double x, double y, IBrush b) => ctx.DrawText(new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));
        Lbl("LOUDNESS", gx, 2, Muted);
        Lbl("▩ input", gx + gw - 150, 2, new SolidColorBrush(Color.Parse("#52604F")));
        Lbl("─ output", gx + gw - 96, 2, NotaPalette.Accent);
        Lbl("┄ target", gx + gw - 44, 2, NotaPalette.Teal);
        if (_primed && Math.Abs(_desired - _applied) > 0.15f)
            Lbl($"correcting {_desired:+0.0;-0.0;0.0} → {_applied:+0.0;-0.0;0.0} dB", gx + 2, gy + 1, Correcting);
        else if (_primed)
            Lbl(Math.Abs(_applied) < 0.05f ? "matched" : "locked", gx + gw - 44, gy + 1, TealBright);
        Lbl("−8 s", gx, gy + gh + 1, AxisB);
        Lbl("now", gx + gw - 18, gy + gh + 1, AxisB);
    }
}
