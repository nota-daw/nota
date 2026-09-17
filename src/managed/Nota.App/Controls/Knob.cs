// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A rotary knob for device / instrument parameters, drawn to the almanac: a 270° groove
// in the well colour, the value arc over it, a raised cap and a Brass Light pointer —
// all laid out on a 52-unit grid (groove r 21 · stroke 5, cap r 14, pointer 2.4 wide
// reaching the cap's edge) and scaled to the knob's size. Drag vertically to change
// (up = increase; Ctrl/⌘/Shift = fine), double-click resets. Same public API as
// MiniFader (Value / max / Accent / ValueChanged / GestureBegin / GestureEnd) so it
// drops into the plugin editors.
//
// A knob exists in exactly three sizes — 34 secondary · 36 regular · 44 main. Width and
// Height are coerced onto that scale, so a call site asking for 30 or 40 gets 34 or 44.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class Knob : Control
{
    private static readonly IBrush Groove = NotaPalette.BgSunken;         // Well
    private static readonly IBrush NeutralArc = NotaPalette.BorderStrong; // a knob off the audio path, or disabled
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush Cap = NotaPalette.Panel;
    private static readonly IBrush CapEdge = NotaPalette.BorderStrong;
    private static readonly IBrush CapEdgeOff = NotaPalette.BorderDefault;
    private static readonly IBrush PointerBrass = NotaPalette.AccentBright;  // Brass Light
    private static readonly IBrush PointerNeutral = NotaPalette.TextStrong;
    private static readonly IBrush PointerOff = NotaPalette.TextTertiary;

    private const double StartDeg = 135.0;   // down-left
    private const double SweepDeg = 270.0;   // clockwise to down-right

    // The almanac draws the knob in a 52-unit box; everything scales from it.
    private const double Grid = 52, GrooveR = 21, GrooveStroke = 5, CapR = 14, PointerW = 2.4, PointerLen = 12;

    public const double SizeSecondary = NotaSize.KnobSecondary, SizeRegular = NotaSize.KnobRegular, SizeMain = NotaSize.KnobMain;

    static Knob()
    {
        WidthProperty.OverrideMetadata<Knob>(new StyledPropertyMetadata<double>(double.NaN, coerce: (_, v) => Snap(v)));
        HeightProperty.OverrideMetadata<Knob>(new StyledPropertyMetadata<double>(double.NaN, coerce: (_, v) => Snap(v)));
    }

    /// <summary>Onto the three-size scale: up to 34 is secondary, under 40 regular, else main.</summary>
    public static double Snap(double v) => double.IsNaN(v) ? v : v <= SizeSecondary ? SizeSecondary : v < 40 ? SizeRegular : SizeMain;

    /// <summary>The paired light tone of a role chroma, for the pointer on a chroma arc.</summary>
    private static IBrush PointerFor(IBrush arc)
        => ReferenceEquals(arc, NotaPalette.Teal) ? NotaPalette.TealBright
         : ReferenceEquals(arc, NotaPalette.Rose) ? NotaPalette.RoseBright
         : arc;

    /// <summary>Brass value arc instead of neutral grey.</summary>
    public bool Accent { get; init; }

    /// <summary>Overrides the value-arc colour (e.g. teal for modulation knobs).</summary>
    public IBrush? ArcColor { get; init; }

    /// <summary>Drawn as inactive (no brass) while still taking input — see Inactive.</summary>
    public bool IsDim { get => _dim; set { if (_dim == value) return; _dim = value; InvalidateVisual(); } }
    private bool _dim;

    /// <summary>Value restored on double-click (NaN = no reset).</summary>
    public double Default { get; set; } = double.NaN;

    private readonly double _max;
    private double _value;
    private bool _drag;
    private double _lastY;

    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    /// <summary>True while the user is dragging — live-follow refreshers skip it then.</summary>
    public bool Dragging => _drag;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, _max); InvalidateVisual(); ValueSet?.Invoke(); }
    }

    /// <summary>Raised whenever the value moves — by hand, by reset or by live follow — so
    /// a cell can re-evaluate <see cref="IsModified"/>.</summary>
    public event Action? ValueSet;

    /// <summary>The value differs from the parameter's default.</summary>
    public bool IsModified => !double.IsNaN(Default) && Math.Abs(_value - Math.Clamp(Default, 0, _max)) > 1e-4 * _max;

    public Knob(double value = 1.0, double max = 1.0)
    {
        _max = max <= 0 ? 1.0 : max;
        _value = Math.Clamp(value, 0, _max);
        Width = SizeRegular;
        Height = SizeRegular;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Left button only — let right-click bubble to a context menu (e.g. CV modulate).
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Double-click restores the parameter's default.
        if (e.ClickCount == 2 && !double.IsNaN(Default))
        {
            double d = Math.Clamp(Default, 0, _max);
            if (Math.Abs(d - _value) > 1e-6)
            {
                GestureBegin?.Invoke();
                _value = d;
                InvalidateVisual();
                ValueChanged?.Invoke(_value);
                ValueSet?.Invoke();
                GestureEnd?.Invoke();
            }
            e.Handled = true;
            return;
        }
        _drag = true;
        _lastY = e.GetPosition(this).Y;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y;
        double dy = _lastY - y;                       // up = increase
        _lastY = y;
        if (dy == 0) return;
        // Fine adjust: hold Ctrl (Windows/Linux), ⌘ or Shift — 10× finer (macOS turns
        // Ctrl+click into a right-click, so Cmd/Shift cover it there).
        bool fine = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) != 0;
        double v = Math.Clamp(_value + dy / (fine ? 1400.0 : 140.0) * _max, 0, _max);
        if (Math.Abs(v - _value) < 1e-6) return;
        _value = v;
        InvalidateVisual();
        ValueChanged?.Invoke(_value);
        ValueSet?.Invoke();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) GestureEnd?.Invoke();
        _drag = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        // Transparent fill makes the whole knob area hit-testable (not just the arc).
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        double k = Math.Min(w, h) / Grid;          // units → px
        double cx = w / 2, cy = h / 2;
        double frac = _max > 0 ? _value / _max : 0;
        bool on = IsEffectivelyEnabled && !_dim;

        var arcBrush = !on ? NeutralArc : ArcColor ?? (Accent ? Brass : NeutralArc);
        var pointer = !on ? PointerOff : ArcColor is { } c ? PointerFor(c) : Accent ? PointerBrass : PointerNeutral;

        // Groove, then the value arc over it — round caps, as in the almanac.
        double r = GrooveR * k, sw = GrooveStroke * k;
        ctx.DrawGeometry(null, new Pen(Groove, sw, lineCap: PenLineCap.Round), Arc(cx, cy, r, 0, 1));
        if (frac > 0.001)
            ctx.DrawGeometry(null, new Pen(arcBrush, sw, lineCap: PenLineCap.Round), Arc(cx, cy, r, 0, frac));

        // Raised cap with a 1px edge.
        ctx.DrawEllipse(Cap, new Pen(on ? CapEdge : CapEdgeOff, 1), new Point(cx, cy), CapR * k, CapR * k);

        // Pointer from the centre to the cap's edge.
        double a = (StartDeg + frac * SweepDeg) * Math.PI / 180.0;
        double len = PointerLen * k;
        ctx.DrawLine(new Pen(pointer, PointerW * k, lineCap: PenLineCap.Round),
            new Point(cx, cy), new Point(cx + Math.Cos(a) * len, cy + Math.Sin(a) * len));
    }

    // The sweep from fraction t0..t1 as a true arc.
    private static StreamGeometry Arc(double cx, double cy, double r, double t0, double t1)
    {
        double a0 = (StartDeg + t0 * SweepDeg) * Math.PI / 180.0;
        double a1 = (StartDeg + t1 * SweepDeg) * Math.PI / 180.0;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx + Math.Cos(a0) * r, cy + Math.Sin(a0) * r), false);
            c.ArcTo(new Point(cx + Math.Cos(a1) * r, cy + Math.Sin(a1) * r), new Size(r, r), 0,
                (t1 - t0) * SweepDeg > 180, SweepDirection.Clockwise);
            c.EndFigure(false);
        }
        return g;
    }
}
