// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The slider outside devices (style guide, spec 038) — track volume, Master, sends: a flat
// 16px bar, corner 4, on the Sunken fill with no groove. A quiet grey fills up to the value
// and a 2px Text-secondary line marks it; the value itself is written inside the bar at the
// left (10.5 / 400), or just past the line when the fill is too short to hold it. Under the
// pointer or in the hand it takes the value bar's colours: the soft orange tint and an
// orange line. A double-click on the written value types one; elsewhere it resets.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed class MiniFader : Control, IInlineReadout
{
    private static readonly IBrush Ground = NotaPalette.BgSunken;
    private static readonly IBrush Fill = NotaPalette.Derived(() => Mix(NotaPalette.BgSunken.Color, NotaPalette.TextTertiary.Color, 0.4));
    private static readonly IBrush Line = NotaPalette.TextSecondary;
    private static readonly IBrush FillLive = NotaPalette.AccentSoft;
    private static readonly IBrush FillDrag = NotaPalette.Derived(() => Mix(NotaPalette.AccentSoft.Color, NotaPalette.Accent.Color, 0.45));
    private static readonly IBrush LineLive = NotaPalette.Accent;
    private static readonly IBrush Ink = NotaPalette.TextPrimary;
    private static readonly Typeface Face = NotaFonts.Sans;

    public const double BarH = 16, LineW = 2;

    private static Color Mix(Color a, Color b, double t)
        => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>The value written inside the bar (from the raw 0..max value); null writes nothing.</summary>
    public Func<double, string>? Text { get; init; }
    /// <summary>Double-click target value, or negative to disable reset.</summary>
    public double Default { get; init; } = -1;

    private readonly double _max;
    private double _value;
    private bool _drag;

    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;   // pointer down (M9-C automation write)
    public event Action? GestureEnd;     // pointer up

    /// <summary>True while the user is dragging — callers skip external value writes then.</summary>
    public bool Dragging => _drag;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, _max); InvalidateVisual(); }
    }

    public MiniFader(double value = 1.0, double max = 1.5)
    {
        _max = max;
        _value = Math.Clamp(value, 0, max);
        Height = BarH;
        MinWidth = 40;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);   // almanac: vertical drag for every value control
    }

    private double _lastY;
    private Rect _textRect;

    // The written value is the read-out: a double-click on it types (ValueEntry), with a few
    // pixels' grace either side; a double-click elsewhere on the bar resets.
    bool IInlineReadout.ReadoutAt(Point p) => Text is not null && _textRect.Width > 0 && _textRect.Inflate(new Thickness(4, 0)).Contains(p);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Left button only — right-click bubbles to the CV-modulate / MIDI Learn menu.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Double-click resets to the default value (e.g. 0 dB for a track volume) — ValueEntry
        // handles it first once attached; ⌥-click types a value there.
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && Default >= 0)
        {
            GestureBegin?.Invoke();
            SetValue(Default);
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
        double y = e.GetPosition(this).Y, dy = _lastY - y;   // up = increase, ~140px full range
        _lastY = y;
        if (dy == 0) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        SetValue(_value + dy / (fine ? 1400.0 : 140.0) * _max);
    }

    // Under the pointer (or in the hand) the control is live and takes the accent; at rest
    // its value is neutral ink — the accent marks a knob or fader only while focused or dragged.
    private bool _hover;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) GestureEnd?.Invoke();
        _drag = false;
        e.Pointer.Capture(null);
        InvalidateVisual();   // a drag that ended off the control leaves no hover to repaint it
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    /// <summary>The fader as a typeable value; <paramref name="fmt"/> is its read-out at a value (0..max).</summary>
    internal ValueField EntryField(string name, Func<double, string> fmt)
        => new(name, () => _value / _max, n => fmt(n * _max), n => SetValue(n * _max),
            () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke(), Default >= 0 ? Math.Clamp(Default, 0, _max) / _max : null);

    private void SetValue(double raw)
    {
        double v = Math.Clamp(raw, 0, _max);
        if (Math.Abs(v - _value) < 1e-4) return;
        _value = v;
        InvalidateVisual();
        ValueChanged?.Invoke(_value);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double bh = Math.Min(BarH, h), ty = (h - bh) / 2;
        double frac = _max > 0 ? _value / _max : 0;
        double fx = frac * w;
        bool live = _drag || _hover;
        var bar = new RoundedRect(new Rect(0, ty, w, bh), NotaRadius.ValueBarValue);
        ctx.DrawRectangle(Ground, null, bar);
        using (ctx.PushClip(bar))
        {
            if (fx > 0.5) ctx.FillRectangle(_drag ? FillDrag : live ? FillLive : Fill, new Rect(0, ty, fx, bh));
            double lx = Math.Clamp(fx, LineW / 2, w - LineW / 2) - LineW / 2;
            ctx.FillRectangle(live ? LineLive : Line, new Rect(lx, ty, LineW, bh));
            if (Text is { } fmt && bh >= 14)
            {
                var t = new NotaFormattedText(fmt(_value), NotaNum.Culture, FlowDirection.LeftToRight, Face, 10.5, Ink);
                // Inside the fill at the left when it fits; otherwise just past the line.
                double tx = t.Width + 12 <= fx ? 6 : Math.Min(fx + LineW + 4, w - t.Width - 4);
                ctx.DrawText(t, new Point(tx, ty + (bh - t.Height) / 2));
                _textRect = new Rect(tx, ty, t.Width, bh);
            }
        }
    }
}
