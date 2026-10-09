// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic's windows:
//
//   MosaicZoneMap    the zone map — keys on X, velocity on Y, the keyboard under it with a
//                    brass tick under every root. Zones are tiles; the selected one is
//                    outlined in Brass Light with its root written in; zones sounding now
//                    fill brass (muted brass while the pedal holds them, a deeper wash for
//                    release samples and fades); an exclusive group is outlined. Click a
//                    tile to select it (again: the next round-robin tile under it), drag it
//                    to move, drag an edge to resize. The keyboard plays (press / release).
//                    The mini (S) map is the same picture without axes or input.
//   MosaicCurveView  a small curve window: the release level against hold time, or the
//                    velocity curve. Drag up / down to change it; double-click resets.
//
// All in the NotaGraph window (well, hairline, radius 4, labels in mono 7).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Mosaic;

namespace Nota.App;

internal sealed class MosaicZoneMap : Control
{
    private IReadOnlyList<MosaicZone> _zones = Array.Empty<MosaicZone>();
    private bool _release;               // show the release zones (else the attack ones)
    private int _sel = -1;
    private readonly Dictionary<int, int> _sounding = new();   // zone → 0 held · 1 pedal · 2 release
    private readonly HashSet<int> _keysDown = new();
    private int _lo = 21, _hi = 108;     // keys shown (inclusive)
    private string _empty = "";

    public bool Mini { get; init; }
    public bool Interactive { get; init; } = true;
    /// <summary>Notes to flag on the keyboard (a gap, say) in Danger.</summary>
    public HashSet<int> Flagged { get; } = new();

    /// <summary>A tile was picked (index into the zone list).</summary>
    public event Action<int>? Selected;
    /// <summary>A drag moved / resized a zone: index, key lo / hi, vel lo / hi, done (pointer up).</summary>
    public event Action<int, int, int, int, int, bool>? Edited;
    public event Action<int>? KeyDown, KeyUp;

    private const double AxisW = 14, KeyH = 12, OctH = 9;

    public MosaicZoneMap() { ClipToBounds = true; }

    public void Set(IReadOnlyList<MosaicZone> zones, bool release, int selected, string empty = "")
    {
        _zones = zones; _release = release; _sel = selected; _empty = empty;
        // Whole octaves around what the map holds (C to C), at least four.
        int lo = 127, hi = 0;
        foreach (var z in zones) { lo = Math.Min(lo, z.KeyLo); hi = Math.Max(hi, z.KeyHi); }
        if (lo > hi) { lo = 36; hi = 84; }
        lo -= lo % 12;
        hi = Math.Min(127, hi + (12 - hi % 12) % 12);
        while (hi - lo < 48 && (lo > 0 || hi < 127)) { if (lo >= 12) lo -= 12; if (hi - lo < 48) hi = Math.Min(127, hi + 12); }
        _lo = lo; _hi = hi;
        InvalidateVisual();
    }

    public void SetSounding(IEnumerable<MosaicModel.Sounding> s, IEnumerable<int> keys)
    {
        _sounding.Clear();
        foreach (var x in s)
            if (!_sounding.TryGetValue(x.Zone, out var cur) || x.State < cur) _sounding[x.Zone] = x.State;
        _playing.Clear();
        foreach (var k in keys) _playing.Add(k);
        InvalidateVisual();
    }
    private readonly HashSet<int> _playing = new();

    // ---- geometry ----------------------------------------------------------------------------
    private Rect MapRect()
    {
        double ax = Mini ? 0 : AxisW;
        double bottom = Mini ? KeyH * 0.75 : KeyH + OctH + 2;
        return new Rect(ax, 0, Math.Max(1, Bounds.Width - ax), Math.Max(1, Bounds.Height - bottom));
    }
    private double KeyW(Rect m) => m.Width / Math.Max(1, _hi - _lo + 1);
    private Rect ZoneRect(Rect m, MosaicZone z)
    {
        double kw = KeyW(m);
        double x0 = m.X + (Math.Max(z.KeyLo, _lo) - _lo) * kw, x1 = m.X + (Math.Min(z.KeyHi, _hi) - _lo + 1) * kw;
        double y0 = m.Y + (127 - z.VelHi) / 128.0 * m.Height, y1 = m.Y + (128 - z.VelLo) / 128.0 * m.Height;
        return new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }
    public int KeyAt(Point p)
    {
        var m = MapRect();
        double kw = KeyW(m);
        if (kw <= 0 || p.X < m.X) return -1;
        return Math.Clamp(_lo + (int)Math.Floor((p.X - m.X) / kw), 0, 127);
    }
    private int VelAt(Point p, Rect m) => Math.Clamp(127 - (int)Math.Floor((p.Y - m.Y) / m.Height * 128), 0, 127);
    private bool Shows(MosaicZone z) => z.Release == _release;

    // ---- input -------------------------------------------------------------------------------
    private enum Drag { None, Move, Left, Right, Top, Bottom }
    private Drag _drag;
    private int _dragZone = -1, _keyHeld = -1;
    private Point _press;
    private bool _moved;
    private (int Klo, int Khi, int Vlo, int Vhi) _orig, _live;

    private (int Zone, Drag Part) HitTest(Point p)
    {
        var m = MapRect();
        int found = -1; Drag part = Drag.None;
        // Prefer the selected zone (its edges), then the topmost tile under the pointer.
        for (int pass = 0; pass < 2 && found < 0; pass++)
            for (int i = _zones.Count - 1; i >= 0; i--)
            {
                if (!Shows(_zones[i]) || (pass == 0 && i != _sel)) continue;
                var r = ZoneRect(m, _zones[i]);
                if (!r.Inflate(2).Contains(p)) continue;
                found = i;
                const double e = 4;
                if (Math.Abs(p.X - r.Left) <= e && r.Width > 10) part = Drag.Left;
                else if (Math.Abs(p.X - r.Right) <= e && r.Width > 10) part = Drag.Right;
                else if (Math.Abs(p.Y - r.Top) <= 3 && r.Height > 10) part = Drag.Top;
                else if (Math.Abs(p.Y - r.Bottom) <= 3 && r.Height > 10) part = Drag.Bottom;
                else part = Drag.Move;
                break;
            }
        return (found, part);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!Interactive) return;
        var p = e.GetPosition(this);
        if (_drag == Drag.None)
        {
            var (z, part) = HitTest(p);
            Cursor = z >= 0 && z == _sel ? part switch
            {
                Drag.Left or Drag.Right => new Cursor(StandardCursorType.SizeWestEast),
                Drag.Top or Drag.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
                _ => new Cursor(StandardCursorType.SizeAll),
            } : z >= 0 || p.Y > MapRect().Bottom ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            return;
        }
        if (_dragZone < 0) return;
        if (!_moved && Math.Abs(p.X - _press.X) + Math.Abs(p.Y - _press.Y) < 3) return;
        _moved = true;
        var m = MapRect();
        int dk = (int)Math.Round((p.X - _press.X) / KeyW(m));
        int dv = (int)Math.Round(-(p.Y - _press.Y) / m.Height * 128);
        var (klo, khi, vlo, vhi) = _orig;
        switch (_drag)
        {
            case Drag.Move:
                dk = Math.Clamp(dk, -klo, 127 - khi); dv = Math.Clamp(dv, -vlo, 127 - vhi);
                klo += dk; khi += dk; vlo += dv; vhi += dv; break;
            case Drag.Left: klo = Math.Clamp(klo + dk, 0, khi); break;
            case Drag.Right: khi = Math.Clamp(khi + dk, klo, 127); break;
            case Drag.Top: vhi = Math.Clamp(vhi + dv, vlo, 127); break;
            case Drag.Bottom: vlo = Math.Clamp(vlo + dv, 0, vhi); break;
        }
        if ((klo, khi, vlo, vhi) != _live)
        {
            _live = (klo, khi, vlo, vhi);
            Edited?.Invoke(_dragZone, klo, khi, vlo, vhi, false);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Interactive || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var m = MapRect();
        if (p.Y > m.Bottom)   // the keyboard plays
        {
            int k = KeyAt(p);
            if (k >= 0) { _keyHeld = k; _keysDown.Add(k); KeyDown?.Invoke(k); InvalidateVisual(); e.Pointer.Capture(this); e.Handled = true; }
            return;
        }
        var (z, part) = HitTest(p);
        if (z < 0) return;
        // A second click on the selected tile picks the next one stacked under it (round-robin).
        if (z == _sel && part == Drag.Move && e.ClickCount == 1)
        {
            var r = ZoneRect(m, _zones[z]);
            var stack = new List<int>();
            for (int i = 0; i < _zones.Count; i++) if (Shows(_zones[i]) && ZoneRect(m, _zones[i]) == r) stack.Add(i);
            if (stack.Count > 1) { _cycleNext = stack[(stack.IndexOf(z) + 1) % stack.Count]; }
        }
        if (z != _sel) { _sel = z; Selected?.Invoke(z); }
        _drag = part; _dragZone = z; _press = p; _moved = false;
        var zz = _zones[z];
        _orig = _live = (zz.KeyLo, zz.KeyHi, zz.VelLo, zz.VelHi);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }
    private int _cycleNext = -1;

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_keyHeld >= 0) { KeyUp?.Invoke(_keyHeld); _keysDown.Remove(_keyHeld); _keyHeld = -1; InvalidateVisual(); }
        if (_drag != Drag.None && _dragZone >= 0)
        {
            if (_moved) Edited?.Invoke(_dragZone, _live.Klo, _live.Khi, _live.Vlo, _live.Vhi, true);
            else if (_cycleNext >= 0) { _sel = _cycleNext; Selected?.Invoke(_sel); }
        }
        _cycleNext = -1;
        _drag = Drag.None; _dragZone = -1;
        e.Pointer.Capture(null);
    }

    // ---- drawing -----------------------------------------------------------------------------
    private static readonly IPen Hair = new Pen(NotaPalette.GraphBorder, 1);
    private static readonly IPen SelPen = new Pen(NotaPalette.AccentBright, 1.5);
    private static readonly IPen ExclPen = new Pen(NotaPalette.TextTertiary, 1);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 2) return;
        var m = MapRect();
        NotaGraph.Window(ctx, m);
        var inner = m.Deflate(1);
        double kw = KeyW(m);

        // Octave rules (a C every 12 keys).
        for (int k = _lo; k <= _hi; k++)
            if (k % 12 == 0 && k > _lo)
            {
                double x = Math.Round(m.X + (k - _lo) * kw) + 0.5;
                ctx.DrawLine(NotaGraph.GridPen, new Point(x, inner.Top), new Point(x, inner.Bottom));
            }

        // Tiles: plain ones first, the sounding over them, the selected last.
        var drawn = new HashSet<Rect>();
        var roots = new HashSet<int>();
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i];
            if (!Shows(z)) continue;
            roots.Add(z.Root);
            var r = ZoneRect(m, z).Deflate(0.5);
            if (_sounding.ContainsKey(i) || i == _sel || !drawn.Add(r)) continue;
            ctx.DrawRectangle(NotaPalette.SurfaceCard, Hair, new RoundedRect(r, 1));
            if (z.Excl != 0 || z.OffBy != 0) ctx.DrawRectangle(null, ExclPen, new RoundedRect(r.Deflate(1.5), 1));
        }
        foreach (var (i, st) in _sounding)
        {
            if (i < 0 || i >= _zones.Count || !Shows(_zones[i])) continue;
            var r = ZoneRect(m, _zones[i]).Deflate(0.5);
            IBrush fill = st == 0 ? NotaPalette.Accent : st == 1 ? NotaPalette.AccentDim : NotaPalette.AccentMute;
            ctx.DrawRectangle(fill, null, new RoundedRect(r, 1));
        }
        if (_sel >= 0 && _sel < _zones.Count && Shows(_zones[_sel]))
        {
            var z = _zones[_sel];
            var r = ZoneRect(m, z).Deflate(0.75);
            if (!_sounding.ContainsKey(_sel)) ctx.DrawRectangle(NotaPalette.AccentSubtle, null, new RoundedRect(r, 1));
            ctx.DrawRectangle(null, SelPen, new RoundedRect(r, 1));
            if (!Mini)
            {
                var t = SamplerInk.Mono(MosaicNames.NoteName(z.Root), _sounding.ContainsKey(_sel) ? NotaPalette.TextOnAccent : NotaPalette.AccentBright, 6.5);
                if (t.Width + 2 < r.Width && t.Height + 2 < r.Height)
                    ctx.DrawText(t, new Point(r.X + (r.Width - t.Width) / 2, r.Y + (r.Height - t.Height) / 2));
            }
        }
        if (_zones.Count == 0 && _empty.Length > 0)
        {
            var t = SamplerInk.Mono(_empty, NotaPalette.TextTertiary, 8);
            ctx.DrawText(t, new Point(m.X + (m.Width - t.Width) / 2, m.Y + (m.Height - t.Height) / 2));
        }

        // Velocity axis.
        if (!Mini)
            foreach (var (v, s) in new[] { (127, "127"), (64, "64"), (1, "1") })
            {
                var t = SamplerInk.Mono(s, NotaPalette.TextAxis, 6);
                double y = m.Y + (127 - v) / 128.0 * m.Height;
                y = Math.Clamp(y, m.Y, m.Bottom - t.Height);
                ctx.DrawText(t, new Point(AxisW - 2 - t.Width, y));
            }

        // The keyboard: white / black keys, sounding keys brass, a tick under every root.
        double ky = m.Bottom + 1, kh = Mini ? KeyH * 0.75 - 2 : KeyH;
        var held = new HashSet<int>(_keysDown);
        held.UnionWith(_playing);
        for (int k = _lo; k <= _hi; k++)
        {
            int pc = k % 12;
            bool black = pc is 1 or 3 or 6 or 8 or 10;
            var r = new Rect(m.X + (k - _lo) * kw + 0.5, ky, Math.Max(1, kw - 1), black ? kh * 0.62 : kh);
            IBrush fill = held.Contains(k) ? NotaPalette.Accent : Flagged.Contains(k) ? NotaPalette.Danger
                : black ? NotaPalette.KeyBlack : NotaPalette.MiniKeyWhite;
            ctx.DrawRectangle(fill, null, r);
            if (roots.Contains(k) && !Mini)
                ctx.DrawRectangle(NotaPalette.Accent, null, new Rect(r.X + r.Width * 0.2, ky + kh + 1, Math.Max(1, r.Width * 0.6), 1.5));
        }
        if (!Mini)
            for (int k = _lo; k <= _hi; k++)
                if (k % 12 == 0)
                {
                    var t = SamplerInk.Mono(MosaicNames.NoteName(k), NotaPalette.TextAxis, 6);
                    double x = m.X + (k - _lo) * kw;
                    if (x + t.Width <= m.Right + 1) ctx.DrawText(t, new Point(x, ky + kh + 2));
                }
    }
}

// ---- a small curve window (release level vs hold · velocity curve) ------------------------------
internal sealed class MosaicCurveView : Control
{
    /// <summary>y (0..1, 1 = top) at x (0..1) for the current value.</summary>
    public Func<double, double> Curve { get; set; } = x => x;
    /// <summary>Normalized value the curve stands for; a vertical drag changes it.</summary>
    public Func<float> Get { get; set; } = () => 0.5f;
    public Action<float>? SetValue { get; set; }
    public Action? Begin { get; set; }
    public Action? End { get; set; }
    public float Default { get; set; } = 0.5f;
    /// <summary>A node to mark on the curve (x 0..1), or null.</summary>
    public double? Node { get; set; }
    /// <summary>Drag up raises the value (else lowers it).</summary>
    public bool UpIsMore { get; set; } = true;

    private bool _drag;
    private double _y0;
    private float _v0;

    public MosaicCurveView() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || SetValue is null) return;
        if (e.ClickCount == 2) { Begin?.Invoke(); SetValue(Default); End?.Invoke(); InvalidateVisual(); e.Handled = true; return; }
        _drag = true; _y0 = e.GetPosition(this).Y; _v0 = Get();
        Begin?.Invoke();
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag || SetValue is null) return;
        double dy = (_y0 - e.GetPosition(this).Y) / Math.Max(20, Bounds.Height * 2);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) dy *= 0.2;
        SetValue((float)Math.Clamp(_v0 + (UpIsMore ? dy : -dy), 0, 1));
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; End?.Invoke();
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 4 || h <= 4) return;
        var r = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, r);
        var inner = r.Deflate(5);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            const int n = 40;
            for (int i = 0; i <= n; i++)
            {
                double x = i / (double)n, y = Math.Clamp(Curve(x), 0, 1);
                var p = new Point(inner.X + x * inner.Width, inner.Bottom - y * inner.Height);
                if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, geo);
        if (Node is { } nx)
        {
            double y = Math.Clamp(Curve(nx), 0, 1);
            NotaGraph.Node(ctx, new Point(inner.X + nx * inner.Width, inner.Bottom - y * inner.Height), true);
        }
    }
}
