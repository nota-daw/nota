// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Volt modulation matrix (mockup 2a/2e): a source×destination grid of bipolar
// cells. Drag a cell vertically to set its amount (up = positive, down = negative);
// right-click or double-click clears it. Source rows 0..3 are modulators (teal bars),
// 4..6 are performance sources (amber). Reads/writes the mtx{s}_{d} plugin params and
// records automation gestures, like the other Volt drag controls.

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
    private static readonly IBrush GridB = new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x36, 0x2D));
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush TealFill = new SolidColorBrush(Color.FromArgb(0x80, 0x5B, 0x9E, 0x9C));
    private static readonly IBrush AmberFill = new SolidColorBrush(Color.FromArgb(0x80, 0xD8, 0xA0, 0x3D));
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush CellBg = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly Typeface Face = new(FontFamily.Default);

    private readonly IAudioEngine _e;
    private readonly int _t;
    private readonly int[,] _idx;      // [src, dst] → plugin-param index (-1 if absent)
    private readonly string[] _src, _dst;
    private const double LabelW = 56, HeaderH = 15;
    private int _dragS = -1, _dragD = -1;
    private double _startX, _startVal;


    public VoltMatrix(IAudioEngine e, int t, int[,] idx, string[] src, string[] dst)
    { _e = e; _t = t; _idx = idx; _src = src; _dst = dst; }

    public void Refresh() => InvalidateVisual();
    public void ClearAll()
    {
        foreach (var i in _idx) if (i >= 0) _e.PluginParamSet(_t, -1, i, 0.5f);
        InvalidateVisual();
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
        { _e.PluginParamSet(_t, -1, i, 0.5f); InvalidateVisual(); e.Handled = true; return; }
        _dragS = s; _dragD = d; _startX = p.X; _startVal = _e.PluginParamGet(_t, -1, i);
        _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(s, d));
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragS < 0) return;
        int i = _idx[_dragS, _dragD]; if (i < 0) return;
        // Horizontal drag: dragging one cell-width right/left sweeps the full ±range.
        var (cw, _) = Cell();
        double v = Math.Clamp(_startVal + (e.GetPosition(this).X - _startX) / Math.Max(24, cw), 0, 1);
        _e.PluginParamSet(_t, -1, i, (float)v); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragS < 0) return;
        _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(_dragS, _dragD));
        _dragS = _dragD = -1; e.Pointer.Capture(null);
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
            Txt(ctx, _src[s], 2, cy + ch / 2 - 5, s < 4 ? Teal : TxtC, 8);
            for (int d = 0; d < _dst.Length; d++)
            {
                double cx = LabelW + d * cw;
                var r = new Rect(cx + 1, cy + 1, cw - 2, ch - 2);
                int i = _idx[s, d];
                bool has = i >= 0;
                ctx.DrawRectangle(has ? CellBg : Sunken, new Pen(GridB, 1), r, 2, 2);
                if (!has) continue;
                double amt = (_e.PluginParamGet(_t, -1, i) - 0.5) * 2.0;
                double midX = r.X + r.Width / 2;
                // Bipolar horizontal bar: grows right for +, left for −, from the centre line.
                if (Math.Abs(amt) > 0.01)
                {
                    double bw = Math.Abs(amt) * (r.Width / 2 - 1);
                    var bar = amt > 0 ? new Rect(midX, r.Y + 1, bw, r.Height - 2)
                                      : new Rect(midX - bw, r.Y + 1, bw, r.Height - 2);
                    ctx.FillRectangle(s < 4 ? TealFill : AmberFill, bar);
                }
                ctx.DrawLine(new Pen(GridB, 1), new Point(midX, r.Y + 2), new Point(midX, r.Bottom - 2));
                if (Math.Abs(amt) > 0.01 && r.Width >= 20)
                    Txt(ctx, ((int)Math.Round(amt * 100)).ToString(), midX, r.Y + r.Height / 2 - 5, TxtC, 8, true);
            }
        }
    }
}
