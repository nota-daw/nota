// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A rotary knob for device / instrument parameters (style guide, spec 038): a 270° arc,
// 2.4 wide with round ends, on the knob track; the value in orange inside a device (grey
// for a knob off the audio path); a small centre cap; a thin ink-1 pointer from the centre
// to the arc. A bipolar knob fills from 12 o'clock. A Flat knob is the one used outside
// device cards (sends): no cap, the value in ink 3, orange only while live. Drag vertically to change
// (up = increase; Ctrl/⌘/Shift = fine). Double-click resets and ⌥-click types a value
// (ValueEntry, attached by KnobCell once `Format` is set). Same public API as
// MiniFader (Value / max / Accent / ValueChanged / GestureBegin / GestureEnd) so it
// drops into the plugin editors.
//
// A device knob is 26; an `Inline` knob, whose label sits beside it (Glide, an EQ band, the
// oscillator rows and ADSR strips), is 22; a Flat knob outside a device is 28. Width and
// Height are coerced onto that scale, whatever a call site asks for.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class Knob : Control
{
    private static readonly IBrush Track = NotaPalette.KnobTrack;
    private static readonly IBrush DeviceArc = NotaPalette.Accent;        // a device value, at rest and live
    private static readonly IBrush GreyArc = NotaPalette.TextTertiary;    // off the audio path, or a flat knob at rest
    private static readonly IBrush OffArc = NotaPalette.TextDisabled;     // disabled or inactive
    private static readonly IBrush LiveArc = NotaPalette.Accent;          // a flat knob while it is being set
    private static readonly IBrush Cap = NotaPalette.KnobCap;
    private static readonly IBrush CapEdge = NotaPalette.Seam;   // the guide's cap edge: Sunken in dark, Seam in light — within a step
    private static readonly IBrush PointerOn = NotaPalette.TextPrimary;
    private static readonly IBrush PointerOff = NotaPalette.TextDisabled;

    private const double StartDeg = 135.0;   // down-left
    private const double SweepDeg = 270.0;   // clockwise to down-right
    private const double Stroke = 2.4, PointerW = 1.6;

    public const double Size = NotaSize.Knob, SizeInline = NotaSize.KnobInline, SizeFlat = NotaSize.KnobFlat;

    static Knob()
    {
        WidthProperty.OverrideMetadata<Knob>(new StyledPropertyMetadata<double>(double.NaN, coerce: (o, v) => double.IsNaN(v) ? v : ((Knob)o).Fixed));
        HeightProperty.OverrideMetadata<Knob>(new StyledPropertyMetadata<double>(double.NaN, coerce: (o, v) => double.IsNaN(v) ? v : ((Knob)o).Fixed));
    }

    private double Fixed => _inline ? SizeInline : _flat ? SizeFlat : Size;

    /// <summary>The 22 px knob with its label beside it (see the header).</summary>
    public bool Inline { get => _inline; init { _inline = value; CoerceValue(WidthProperty); CoerceValue(HeightProperty); } }
    private readonly bool _inline;

    /// <summary>The 28 px knob outside a device card: no cap, a grey value, orange while live.</summary>
    public bool Flat { get => _flat; init { _flat = value; CoerceValue(WidthProperty); CoerceValue(HeightProperty); } }
    private readonly bool _flat;

    /// <summary>On the audio path: inside a device its value is orange; a knob off the audio
    /// path draws its value in ink 3.</summary>
    public bool Accent { get; init; }

    /// <summary>The value fills from 12 o'clock (pan, detune, a ± amount).</summary>
    public bool Bipolar { get; init; }

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
        Width = Size;
        Height = Size;
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
        double d = Math.Min(w, h), cx = w / 2, cy = h / 2;
        double frac = _max > 0 ? _value / _max : 0;
        bool on = IsEffectivelyEnabled && !_dim;
        bool live = on && (_drag || _hover);

        var arcBrush = !on ? OffArc
            : ArcColor ?? (_flat ? (live ? LiveArc : GreyArc) : Accent || live ? DeviceArc : GreyArc);
        var pointer = !on ? PointerOff : PointerOn;

        // The track, then the value arc over it — round ends. A bipolar value grows from the top.
        double r = d / 2 - 3;
        ctx.DrawGeometry(null, new Pen(Track, Stroke, lineCap: PenLineCap.Round), Arc(cx, cy, r, 0, 1));
        double from = Bipolar ? 0.5 : 0;
        if (Math.Abs(frac - from) > 0.001)
            ctx.DrawGeometry(null, new Pen(arcBrush, Stroke, lineCap: PenLineCap.Round), Arc(cx, cy, r, Math.Min(from, frac), Math.Max(from, frac)));

        // The centre cap, devices only.
        if (!_flat) ctx.DrawEllipse(Cap, new Pen(CapEdge, 1), new Point(cx, cy), d / 2 - 5, d / 2 - 5);

        // Pointer from the centre to the arc.
        double a = (StartDeg + frac * SweepDeg) * Math.PI / 180.0;
        ctx.DrawLine(new Pen(pointer, PointerW, lineCap: PenLineCap.Round),
            new Point(cx, cy), new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
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
