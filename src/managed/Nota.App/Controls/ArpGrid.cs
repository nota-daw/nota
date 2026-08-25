// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Arp step sequencer (mockup 2k): 16 columns for the selected lane — bar height =
// value with fill opacity tracking it (velocity), bar-line shading every 4 steps, step
// numbers under each column (click to mute via the On lane), the playing step lit
// accent-bright, muted steps as empty wells, and ratchet counts «×2/×3» in teal above.
// Drag to draw; ⌥ ramps a line across the dragged steps. Steps beyond the loop dim.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class ArpGrid : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush Group = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00));
    private static readonly IBrush Play = new SolidColorBrush(Color.FromArgb(0x2A, 0xF0, 0xC0, 0x60));
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush WellPen = new SolidColorBrush(Color.FromArgb(0x60, 0x3A, 0x36, 0x2D));
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush Txt = NotaPalette.TextSecondary;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly Typeface Face = new(FontFamily.Default);
    private const int Vel = 13, Ratchet = 61, On = 93, Loop = 11, kSteps = 16, NumH = 15;

    private readonly IAudioEngine _e; private readonly int _t, _mi;
    private int _base = Vel; private double _min, _max = 1; private bool _vel = true, _int;
    private int _playStep = -1, _dragStep = -1; private double _dragNorm;

    public ArpGrid(IAudioEngine e, int t, int mi) { _e = e; _t = t; _mi = mi; MinHeight = 90; }
    public void SetLane(int b, double min, double max, bool vel, bool isInt) { _base = b; _min = min; _max = max; _vel = vel; _int = isInt; InvalidateVisual(); }
    public void SetPlayhead(int s) { if (s != _playStep) { _playStep = s; InvalidateVisual(); } }

    private double Get(int p) => _e.MidiEffectGetParam(_t, _mi, p);
    private int LoopLen() => Math.Clamp((int)Math.Round(Get(Loop)), 1, kSteps);
    private int StepAt(double x) => Math.Clamp((int)(x / Bounds.Width * kSteps), 0, kSteps - 1);
    private double BarH() => Math.Max(1, Bounds.Height - NumH);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.Y >= Bounds.Height - NumH) { int s = StepAt(p.X); _e.MidiEffectSetParam(_t, _mi, On + s, Get(On + s) > 0.5 ? 0f : 1f); InvalidateVisual(); e.Pointer.Capture(this); e.Handled = true; _dragStep = -1; return; }
        _dragStep = StepAt(p.X); _dragNorm = NormAt(p.Y); Apply(_dragStep, _dragNorm);
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragStep < 0) return;
        var p = e.GetPosition(this); int s = StepAt(p.X); double n = NormAt(p.Y);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && s != _dragStep)
        {
            int a = _dragStep, b = s; double na = _dragNorm, nb = n;
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            for (int i = lo; i <= hi; i++) { double t = a == b ? 0 : (double)(i - a) / (b - a); Apply(i, na + (nb - na) * t); }
        }
        else Apply(s, n);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _dragStep = -1; e.Pointer.Capture(null); }

    private double NormAt(double y) => Math.Clamp(1 - y / BarH(), 0, 1);
    private void Apply(int s, double norm)
    {
        double v = _min + norm * (_max - _min); if (_int) v = Math.Round(v);
        _e.MidiEffectSetParam(_t, _mi, _base + s, (float)Math.Clamp(v, _min, _max));
        InvalidateVisual();
    }

    private void DrawText(DrawingContext ctx, string t, double cx, double y, IBrush b, double size = 8)
    { var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, b); ctx.DrawText(ft, new Point(cx - ft.Width / 2, y)); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Bg, null, new Rect(0, 0, w, h), 4, 4);
        double cw = w / kSteps, barH = BarH(), numY = h - NumH + 2;
        int loop = LoopLen();
        double zero = _min < 0 ? barH * (_max / (_max - _min)) : barH;   // baseline for signed lanes

        for (int s = 0; s < kSteps; s++)
        {
            double x = s * cw;
            if ((s / 4) % 2 == 1) ctx.FillRectangle(Group, new Rect(x, 0, cw, barH));
            if (s == _playStep) ctx.FillRectangle(Play, new Rect(x, 0, cw, h));
            bool active = s < loop;
            bool muted = Get(On + s) < 0.5;
            var cell = new Rect(x + 2, 2, cw - 4, barH - 4);

            if (muted)
            {
                ctx.DrawRectangle(null, new Pen(WellPen, 1), cell, 2, 2);
            }
            else
            {
                double v = Get(_base + s), norm = (v - _min) / (_max - _min);
                byte a = (byte)(active ? (_vel ? 70 + norm * 175 : 210) : 70);
                var brush = new SolidColorBrush(Color.FromArgb(a, 0xD8, 0xA0, 0x3D));
                if (_min < 0)
                {
                    double top = barH - norm * barH, y0 = Math.Min(top, zero), y1 = Math.Max(top, zero);
                    ctx.FillRectangle(brush, new Rect(x + 2, y0, cw - 4, Math.Max(1, y1 - y0)));
                }
                else
                {
                    double bh = norm * (barH - 4);
                    ctx.FillRectangle(brush, new Rect(x + 2, barH - bh - 2, cw - 4, Math.Max(1, bh)));
                }
                // Ratchet marker above the bar.
                int r = (int)Math.Round(Get(Ratchet + s));
                if (r >= 2) DrawText(ctx, "×" + r, x + cw / 2, 1, active ? Teal : Muted, 8);
            }

            // Step number strip (click to mute).
            var numBrush = s == _playStep ? AccentBright : muted ? new SolidColorBrush(Color.FromArgb(0x66, 0x6E, 0x6A, 0x5E)) : Txt;
            DrawText(ctx, (s + 1).ToString(), x + cw / 2, numY, numBrush, 8);
        }
        if (_min < 0) ctx.DrawLine(new Pen(WellPen, 1), new Point(0, zero), new Point(w, zero));
    }
}
