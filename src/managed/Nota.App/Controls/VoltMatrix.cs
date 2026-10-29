// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Volt modulation matrix: a source×destination grid of bipolar cells. Drag a cell
// vertically to set its amount (up = positive, down = negative); ⌥-click types it
// (−100…+100 %), double-click clears it, right-click opens Type value… / Clear — every
// clear one gesture, so it undoes (specs/035). A route reads as a neutral-washed cell carrying its signed
// amount — the deeper the wash, the more of it — and an empty one stays a dark well, so
// the routes a patch actually uses are the only thing on screen. Modulator rows are
// named in teal, performance sources in plain ink. Reads/writes the mtx{s}_{d} plugin
// params and records automation gestures, like the other Volt drag controls.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class VoltMatrix : Control, IValueEntryHost
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush GridB = NotaGraph.Grid;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush Lit = NotaPalette.TextPrimary;
    private static readonly IBrush LitEdge = NotaPalette.BorderStrong;
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
    {
        _e = e; _t = t; _idx = idx; _src = src; _dst = dst;
        _fields = new ValueField?[src.Length, dst.Length];
        ValueEntry.AttachHost(this);
    }

    // ---- typed entry ------------------------------------------------------------
    private readonly ValueField?[,] _fields;

    private ValueField? FieldOf(int s, int d)
    {
        int i = _idx[s, d];
        if (i < 0) return null;
        return _fields[s, d] ??= new ValueField($"{_src[s]} → {_dst[d]}", () => _e.PluginParamGet(_t, -1, i),
            v => NotaNum.F($"{(v - 0.5) * 200:+0;−0;0}\u2009%"), v => _e.PluginParamSet(_t, -1, i, (float)Math.Clamp(v, 0, 1)),
            () => _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(s, d)),
            () => _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(s, d)), 0.5)
        {
            Clear = () => Clear(s, d),
            After = () => { InvalidateVisual(); Changed?.Invoke(); },
        };
    }

    // One gesture, so the clear is an undo step and records like a drag.
    private void Clear(int s, int d)
    {
        int i = _idx[s, d];
        if (i < 0 || Math.Abs(_e.PluginParamGet(_t, -1, i) - 0.5f) < 1e-6) return;
        _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(s, d));
        _e.PluginParamSet(_t, -1, i, 0.5f);
        _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(s, d));
    }

    private (int s, int d) CellAt(Point p)
    {
        var (cw, ch) = Cell();
        if (p.X < LabelW || p.Y < HeaderH || cw <= 0 || ch <= 0) return (-1, -1);
        int d = (int)((p.X - LabelW) / cw), s = (int)((p.Y - HeaderH) / ch);
        return s < _src.Length && d < _dst.Length ? (s, d) : (-1, -1);
    }

    public IReadOnlyList<ValueField>? FieldsAt(Point p)
        => CellAt(p) is var (s, d) && s >= 0 && FieldOf(s, d) is { } f ? [f] : null;

    public Rect AnchorOf(ValueField field)
    {
        var (cw, ch) = Cell();
        for (int s = 0; s < _src.Length; s++)
            for (int d = 0; d < _dst.Length; d++)
                if (ReferenceEquals(_fields[s, d], field)) return new Rect(LabelW + d * cw + 1, HeaderH + s * ch + 1, cw - 2, ch - 2);
        return default;
    }

    // Tab walks the routes the patch uses, row by row.
    public IReadOnlyList<ValueField> AllFields
    {
        get
        {
            var list = new List<ValueField>();
            for (int s = 0; s < _src.Length; s++)
                for (int d = 0; d < _dst.Length; d++)
                    if (_idx[s, d] is var i and >= 0 && Math.Abs(_e.PluginParamGet(_t, -1, i) - 0.5f) > 0.005f && FieldOf(s, d) is { } f) list.Add(f);
            return list;
        }
    }

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
        var p = e.GetPosition(this);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click: the parameter menu
        var (s, d) = CellAt(p);
        if (s < 0) return;
        int i = _idx[s, d]; if (i < 0) return;
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

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_dragS >= 0)
        {
            _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, Id(_dragS, _dragD));
            _dragS = _dragD = -1; InvalidateVisual();
            Changed?.Invoke();
        }
        base.OnPointerCaptureLost(e);
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
                // A route lights the whole cell: a neutral wash that deepens with the amount,
                // a hairline, and the signed number in primary ink.
                var wash = NotaPalette.Wash(NotaPalette.VizInk, (byte)(0x18 + 0x40 * Math.Min(1.0, Math.Abs(amt))));
                ctx.DrawRectangle(wash, new Pen(LitEdge, 1), r, 2, 2);
                if (r.Width >= 18)
                    Txt(ctx, (amt > 0 ? "+" : "−") + (int)Math.Round(Math.Abs(amt) * 100), r.X + r.Width / 2, r.Y + r.Height / 2 - 5, Lit, 8, true);
            }
        }
    }
}
