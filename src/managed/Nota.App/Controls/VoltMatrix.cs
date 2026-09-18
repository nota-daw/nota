// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Volt modulation matrix: a source×destination grid of bipolar cells. Drag a cell
// vertically to set its amount (up = positive, down = negative); right-click or
// double-click clears it. A route reads as a brass-washed cell carrying its signed
// amount — the deeper the wash, the more of it — and an empty one stays a dark well, so
// the routes a patch actually uses are the only thing on screen. Modulator rows are
// named in teal, performance sources in plain ink. Reads/writes the mtx{s}_{d} plugin
// params and records automation gestures, like the other Volt drag controls.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class VoltMatrix : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush GridB = NotaGraph.Grid;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush Lit = NotaPalette.AccentBright;
    private static readonly IBrush LitEdge = NotaPalette.BorderBrass;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush CellBg = NotaPalette.BgSunken;
    private static readonly Typeface Face = NotaFonts.Mono;
    // How far the pointer travels for the full ±range — the knob's own feel.
    private const double Travel = 140;

    private readonly IAudioEngine _e;
    private readonly int _t;
    private readonly int[,] _idx;      // [src, dst] → plugin-param index (-1 if absent)
    private readonly string[] _src, _dst;
    private const double LabelW = 56, HeaderH = 15;
    private int _dragS = -1, _dragD = -1;
    private double _startY, _startVal;

    /// <summary>How many leading source rows are modulators (named in teal); the rest are
    /// performance sources.</summary>
    public int Modulators { get; init; } = 4;

    /// <summary>Raised after a cell changes, so the card can refresh what reads the matrix
    /// (the route count, an LFO's "lands on" line, the status strip).</summary>
    public event Action? Changed;

    public VoltMatrix(IAudioEngine e, int t, int[,] idx, string[] src, string[] dst)
    { _e = e; _t = t; _idx = idx; _src = src; _dst = dst; }

    public void Refresh() => InvalidateVisual();
    public void ClearAll()
    {
        foreach (var i in _idx) if (i >= 0) _e.PluginParamSet(_t, -1, i, 0.5f);
        InvalidateVisual();
        Changed?.Invoke();
    }

    private (double cw, double ch) Cell()
        => ((Bounds.Width - LabelW) / _dst.Length, (Bounds.Height - HeaderH) / _src.Length);
    private string Id(int s, int d) => $"mtx{s}_{d}";

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this); var (cw, ch) = Cell();
        int d = (int)((p.X - LabelW) / cw), s = (int)((p.Y - HeaderH) / ch);
        if (s < 0 || s >= _src.Length || d < 0 || d >= _dst.Length) return;
        int i = _idx[s, d]; if (i < 0) return;
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsRightButtonPressed || e.ClickCount == 2)
        { _e.PluginParamSet(_t, -1, i, 0.5f); InvalidateVisual(); Changed?.Invoke(); e.Handled = true; return; }
        _dragS = s; _dragD = d; _startY = p.Y; _startVal = _e.PluginParamGet(_t, -1, i);
        _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(s, d));
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragS < 0) return;
        int i = _idx[_dragS, _dragD]; if (i < 0) return;
        // Vertical drag, like a knob: up adds, down subtracts, a modifier goes fine.
        double fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control)
                   || e.KeyModifiers.HasFlag(KeyModifiers.Meta) ? 0.25 : 1.0;
        double v = Math.Clamp(_startVal + (_startY - e.GetPosition(this).Y) / Travel * fine, 0, 1);
        _e.PluginParamSet(_t, -1, i, (float)v); InvalidateVisual(); Changed?.Invoke();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragS < 0) return;
        _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(_dragS, _dragD));
        _dragS = _dragD = -1; e.Pointer.Capture(null);
        Changed?.Invoke();
    }

    private void Txt(DrawingContext ctx, string t, double x, double y, IBrush b, double size = 8, bool center = false)
    {
        var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, b);
        ctx.DrawText(ft, new Point(center ? x - ft.Width / 2 : x, y));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
        var (cw, ch) = Cell();
        // Destination headers.
        for (int d = 0; d < _dst.Length; d++)
            Txt(ctx, _dst[d], LabelW + cw * (d + 0.5), 1, MutedC, 8, true);
        for (int s = 0; s < _src.Length; s++)
        {
            double cy = HeaderH + s * ch;
            Txt(ctx, _src[s], 2, cy + ch / 2 - 5, s < Modulators ? Teal : TxtC, 8);
            for (int d = 0; d < _dst.Length; d++)
            {
                double cx = LabelW + d * cw;
                var r = new Rect(cx + 1, cy + 1, cw - 2, ch - 2);
                int i = _idx[s, d];
                if (i < 0) { ctx.DrawRectangle(Sunken, new Pen(GridB, 1), r, 2, 2); continue; }
                double amt = (_e.PluginParamGet(_t, -1, i) - 0.5) * 2.0;
                bool on = Math.Abs(amt) > 0.01;
                if (!on) { ctx.DrawRectangle(CellBg, new Pen(GridB, 1), r, 2, 2); continue; }
                // A route lights the whole cell: a brass wash that deepens with the amount,
                // a brass hairline, and the signed number in Brass Light.
                var wash = NotaPalette.Wash(NotaPalette.Accent, (byte)(0x18 + 0x40 * Math.Min(1.0, Math.Abs(amt))));
                ctx.DrawRectangle(wash, new Pen(LitEdge, 1), r, 2, 2);
                if (r.Width >= 18)
                    Txt(ctx, (amt > 0 ? "+" : "−") + (int)Math.Round(Math.Abs(amt) * 100), r.X + r.Width / 2, r.Y + r.Height / 2 - 5, Lit, 8, true);
            }
        }
    }
}
