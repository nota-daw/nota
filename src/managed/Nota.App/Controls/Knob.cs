// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A rotary knob for device / instrument parameters (the Studio dial): a 270° groove in the
// well colour, the value arc over it — neutral ink at rest, the accent while live — a
// raised cap on a seam and a primary-ink pointer —
// all laid out on a 52-unit grid (groove r 21 · stroke 5, cap r 14, pointer 2.4 wide
// reaching the cap's edge) and scaled to the knob's size. Drag vertically to change
// (up = increase; Ctrl/⌘/Shift = fine). Double-click resets and ⌥-click types a value
// (ValueEntry, attached by KnobCell once `Format` is set). Same public API as
// MiniFader (Value / max / Accent / ValueChanged / GestureBegin / GestureEnd) so it
// drops into the plugin editors.
//
// A knob exists in exactly three sizes — 34 secondary · 36 regular · 44 main. Width and
// Height are coerced onto that scale, so a call site asking for 30 or 40 gets 34 or 44.
// The one exception is an `Inline` knob (24) that sits inside a table row, where a
// section knob cannot fit — the Consort / Pentad / Monolith oscillator rows and ADSR strips.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class Knob : Control
{
    // The dial: a raised face on a seam, a well-coloured track, the value arc in neutral
    // ink at rest and in the accent only while the knob is live (under the pointer or in
    // the hand) — a resting value is never orange.
    private static readonly IBrush Groove = NotaPalette.BgSunken;         // the track: well
    private static readonly IBrush NeutralArc = NotaPalette.Rule;         // a knob off the audio path, or disabled
    private static readonly IBrush RestArc = NotaPalette.VizInk;          // a value at rest
    private static readonly IBrush LiveArc = NotaPalette.Accent;          // the value while it is being set
    private static readonly IBrush Cap = NotaPalette.SurfaceRaised;
    private static readonly IBrush CapEdge = NotaPalette.Seam;
    private static readonly IBrush CapEdgeOff = NotaPalette.Seam;
    private static readonly IBrush PointerNeutral = NotaPalette.TextPrimary;
    private static readonly IBrush PointerOff = NotaPalette.TextDisabled;

    private const double StartDeg = 135.0;   // down-left
    private const double SweepDeg = 270.0;   // clockwise to down-right

    // The knob is drawn in a 52-unit box; everything scales from it.
    private const double Grid = 52, GrooveR = 21, GrooveStroke = 5, CapR = 14, PointerW = 2.4, PointerLen = 12;

    public const double SizeSecondary = NotaSize.KnobSecondary, SizeRegular = NotaSize.KnobRegular, SizeMain = NotaSize.KnobMain, SizeInline = NotaSize.KnobInline;

    static Knob()
    {
        WidthProperty.OverrideMetadata<Knob>(new StyledPropertyMetadata<double>(double.NaN, coerce: (o, v) => o is Knob { _inline: true } ? SizeInline : Snap(v)));
        HeightProperty.OverrideMetadata<Knob>(new StyledPropertyMetadata<double>(double.NaN, coerce: (o, v) => o is Knob { _inline: true } ? SizeInline : Snap(v)));
    }

    /// <summary>Onto the three-size scale: up to 34 is secondary, under 40 regular, else main.</summary>
    public static double Snap(double v) => double.IsNaN(v) ? v : v <= SizeSecondary ? SizeSecondary : v < 40 ? SizeRegular : SizeMain;

    /// <summary>The paired light tone of a role chroma, for the pointer on a chroma arc.</summary>
    private static IBrush PointerFor(IBrush arc)
        => ReferenceEquals(arc, NotaPalette.Teal) ? NotaPalette.TealBright
         : ReferenceEquals(arc, NotaPalette.Rose) ? NotaPalette.RoseBright
         : arc;

    /// <summary>The 24 px table-row knob, outside the three-size scale (see the header).</summary>
    public bool Inline { get => _inline; init { _inline = value; CoerceValue(WidthProperty); CoerceValue(HeightProperty); } }
    private readonly bool _inline;

    /// <summary>On the audio path: its value arc is drawn in neutral ink (and the accent while
    /// live) rather than the quieter rule grey of an off-path knob.</summary>
    public bool Accent { get; init; }

    /// <summary>Overrides the value-arc colour (e.g. teal for modulation knobs); may change live
    /// (a knob that turns into a modulation depth when its mode switches).</summary>
    public IBrush? ArcColor { get => _arc; set { if (ReferenceEquals(_arc, value)) return; _arc = value; InvalidateVisual(); } }
    private IBrush? _arc;

    /// <summary>Drawn as inactive (no brass) while still taking input — see Inactive.</summary>
    public bool IsDim { get => _dim; set { if (_dim == value) return; _dim = value; InvalidateVisual(); } }
    private bool _dim;

    /// <summary>Value restored on double-click (NaN = no reset).</summary>
    public double Default { get; set; } = double.NaN;

    /// <summary>The read-out text at a value (0..max) — what the knob's value line shows. Pure:
    /// it formats, it never writes. Set it and the knob takes typed values (ValueEntry).</summary>
    public Func<double, string>? Format { get; set; }

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

    /// <summary>The knob as a typeable value: a typed value or a reset runs the same
    /// GestureBegin / ValueChanged / GestureEnd a drag does. Null without a Format.</summary>
    internal ValueField? EntryField(string name)
        => Format is not { } fmt ? null : new ValueField(name, () => _value / _max, n => fmt(n * _max), n => SetByHand(n * _max),
            () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke(), double.IsNaN(Default) ? null : Math.Clamp(Default, 0, _max) / _max);

    /// <summary>A probing field for a knob without a Format: each probe sets the knob (running its
    /// ValueChanged handlers) and reads <paramref name="text"/> back.</summary>
    internal ValueField ProbingField(string name, Func<string> text)
        => ValueEntry.Probing(name, () => _value / _max, n => SetByHand(n * _max), text,
            () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke()) with
        { Default = double.IsNaN(Default) ? null : Math.Clamp(Default, 0, _max) / _max };

    // A value set by the user other than by dragging (typed, reset): repaint and notify.
    private void SetByHand(double v)
    {
        v = Math.Clamp(v, 0, _max);
        if (Math.Abs(v - _value) < 1e-9) return;
        _value = v;
        InvalidateVisual();
        ValueChanged?.Invoke(_value);
        ValueSet?.Invoke();
    }

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
        // Double-click (or ⌥-click) restores the default — ValueEntry handles it first on an
        // attached knob (⌥ types there); this keeps reset on a knob built without one.
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && !double.IsNaN(Default))
        {
            GestureBegin?.Invoke();
            SetByHand(Default);
            GestureEnd?.Invoke();
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
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    // Under the pointer the knob is live: its arc takes the accent (120ms is the button
    // fade; a dial repaints on the spot, which reads the same at this size).
    private bool _hover;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }

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

        bool live = on && (_drag || _hover);
        var arcBrush = !on ? NeutralArc : ArcColor ?? (live ? LiveArc : Accent ? RestArc : NeutralArc);
        var pointer = !on ? PointerOff : ArcColor is { } c ? PointerFor(c) : PointerNeutral;

        // Groove, then the value arc over it — round caps.
        double r = GrooveR * k, sw = GrooveStroke * k;
        ctx.DrawGeometry(null, new Pen(Groove, sw, lineCap: PenLineCap.Round), Arc(cx, cy, r, 0, 1));
        if (frac > 0.001)
            ctx.DrawGeometry(null, new Pen(arcBrush, sw, lineCap: PenLineCap.Round), Arc(cx, cy, r, 0, frac));

        // Raised face with a 1px seam edge.
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
