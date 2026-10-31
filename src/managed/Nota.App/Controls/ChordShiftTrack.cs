// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Nota Chord shift (almanac mockups 1a / 1b): a bipolar −12..+12 semitone track centred
// on zero. Horizontal (L): a 3px well groove with a centre tick, a neutral fill from the
// centre and a 6×9 handle (accent while dragged). Vertical (S): a framed well with a centre rule and a bar
// growing up (positive) or down (negative). Unlike the generic SliderTrack the value is
// positional — press or drag anywhere sets the semitone under the pointer — because the
// track is a pitch ruler. A switched-off slot draws its fill in Border strong.
// GestureBegin / GestureEnd bracket the drag for automation writes; double-click resets
// to 0, ⌥-click types a shift; right-click opens the parameter menu.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ChordShiftTrack : Control
{
    // A value bar (style guide): a groove, soft orange from the centre to the value, a 2px orange
    // line at the value. An off shift shows only a grey line at the centre.
    private static readonly IBrush Wash = NotaPalette.AccentSoft;
    private static readonly IBrush Line = NotaPalette.Accent;
    private static readonly IBrush LineHot = NotaPalette.AccentBright;
    private static readonly IBrush Centre = NotaPalette.TextTertiary;

    public const int Range = 12;

    private int _value;
    private bool _on = true, _drag;

    public bool Vertical { get; init; }
    public bool Dragging => _drag;

    public int Value { get => _value; set { var v = Math.Clamp(value, -Range, Range); if (v == _value) return; _value = v; InvalidateVisual(); } }
    public bool On { get => _on; set { if (_on == value) return; _on = value; InvalidateVisual(); } }

    public event Action<int>? Changed;

    /// <summary>The track as a typeable value: semitones −12…+12 ("+3", "−5"), double-click 0.</summary>
    internal ValueField EntryField(string name)
        => new(name, () => (_value + Range) / (2.0 * Range), n => Semis(Steps(n)), n => { int v = Steps(n); _value = v; InvalidateVisual(); Changed?.Invoke(v); },
            () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke(), 0.5);

    private static int Steps(double n) => (int)Math.Round(Math.Clamp(n, 0, 1) * 2 * Range) - Range;
    private static string Semis(int v) => v == 0 ? "0" : v.ToString("+0;−0", NotaNum.Culture);
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Cursor = new Cursor(Vertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
    }

    private int At(Point p)
    {
        double f = Vertical ? 1 - p.Y / Math.Max(1, Bounds.Height) : p.X / Math.Max(1, Bounds.Width);
        return (int)Math.Round(Math.Clamp(f, 0, 1) * 2 * Range - Range);
    }

    private void Set(Point p)
    {
        int v = At(p);
        if (v == _value && _on) return;
        _value = v; InvalidateVisual();
        Changed?.Invoke(v);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { _value = 0; InvalidateVisual(); GestureBegin?.Invoke(); Changed?.Invoke(0); GestureEnd?.Invoke(); return; }
        _drag = true;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        Set(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag) Set(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false; InvalidateVisual();
        e.Pointer.Capture(null);
        GestureEnd?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double f = (_value + Range) / (2.0 * Range);
        var line = _drag ? LineHot : Line;
        var r = new RoundedRect(new Rect(0, 0, w, h), NotaRadius.ValueBarValue);
        NotaDepth.Groove(ctx, this, r);
        using (ctx.PushClip(r))
        {
            if (Vertical)
            {
                double mid = Math.Round(h / 2), y = (1 - f) * h;
                if (!_on || _value == 0) { ctx.FillRectangle(_on ? line : Centre, new Rect(0, mid - 1, w, 2)); return; }
                ctx.FillRectangle(Wash, new Rect(0, Math.Min(mid, y), w, Math.Abs(y - mid)));
                ctx.FillRectangle(line, new Rect(0, Math.Clamp(y - 1, 0, h - 2), w, 2));
            }
            else
            {
                double c = Math.Round(w / 2), x = f * w;
                if (!_on || _value == 0) { ctx.FillRectangle(_on ? line : Centre, new Rect(c - 1, 0, 2, h)); return; }
                ctx.FillRectangle(Wash, new Rect(Math.Min(c, x), 0, Math.Abs(x - c), h));
                ctx.FillRectangle(line, new Rect(Math.Clamp(x - 1.5, 0, w - 3), 0, 3, h));
            }
        }
    }
}
