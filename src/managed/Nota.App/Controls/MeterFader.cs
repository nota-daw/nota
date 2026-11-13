// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The compact Mixer strip's level control (specs/070, #386): a wide stereo meter that is also
// the volume fader. Two 18 px channels — wider than the 11 px meter rule, an accepted
// exception (DESIGN.md) because the wide meter is what makes a 72 px strip readable — with a
// small triangle on the right edge at the volume's dB *on the meter's own scale*, so 0 dB on
// the handle lines up with 0 dB on the meter, and a hairline across the channels at that
// height. The whole control is the drag target (relative, like VFader: a click never jumps the
// value); a click without a drag clears the peak hold, as on MeterBar. Value is VFader's
// 0..1.5 linear, so the strip wires it exactly like the regular fader.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class MeterFader : Control
{
    public const double Max = 1.5;
    private const double ScaleW = 14, ChannelW = 18, ChannelGap = 3, GutterW = 11, HandleW = 9, HandleH = 10;
    /// <summary>The control's natural width: scale · two channels · handle gutter.</summary>
    public const double NaturalWidth = ScaleW + 1 + ChannelW * 2 + ChannelGap + GutterW;
    private static readonly double[] Marks = { 6, 0, -12, -48 };
    private static readonly string[] MarkText = { "+6", "0", "−12", "−48" };

    private double _value;
    private double _levelL, _levelR, _holdL, _holdR;
    private bool _drag, _moved, _hover;
    private double _startY, _startDb;

    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;   // automation write + undo step
    public event Action? GestureEnd;
    public bool Dragging => _drag;
    public double Value => _value;

    public MeterFader(double value)
    {
        _value = Math.Clamp(value, 0, Max);
        Width = NaturalWidth;
        MinHeight = 40;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        ToolTip.SetTip(this, Loc.T("control.meter_fader.drag_to_set_the_volume"));
    }

    /// <summary>Sets the value without raising ValueChanged (external sync, the dB field).</summary>
    public void SetValueExternal(double v) { _value = Math.Clamp(v, 0, Max); InvalidateVisual(); }

    /// <summary>The fader as a typeable value, as <see cref="VFader.EntryField"/>.</summary>
    internal ValueField EntryField(string name, Func<double, string> fmt, double? def)
        => new(name, () => _value / Max, n => fmt(n * Max), n => Set(n * Max),
            () => GestureBegin?.Invoke(), () => GestureEnd?.Invoke(), def is { } d ? Math.Clamp(d, 0, Max) / Max : null);

    /// <summary>Feed a fresh reading; repaints only when something moved (#55).</summary>
    public void Push(NotaMeter m)
    {
        double l = MeterScale.Norm(m.PeakL), r = MeterScale.Norm(m.PeakR);
        double hl = Math.Max(_holdL, l), hr = Math.Max(_holdR, r);
        if (l == _levelL && r == _levelR && hl == _holdL && hr == _holdR) return;
        _levelL = l; _levelR = r; _holdL = hl; _holdR = hr;
        InvalidateVisual();
    }

    public void Reset() { _levelL = _levelR = _holdL = _holdR = 0; InvalidateVisual(); }

    private void Set(double v)
    {
        v = Math.Clamp(v, 0, Max);
        if (Math.Abs(v - _value) < 1e-9) return;
        _value = v; InvalidateVisual(); ValueChanged?.Invoke(v);
    }

    // The volume in dB on the meter's scale (−60…+6); silence sits at the floor.
    private static double ToDb(double v) => v <= 1e-4 ? MeterScale.FloorDb : Math.Max(MeterScale.FloorDb, AudioMath.LinToDb(v));
    private static double FromDb(double db) => db <= MeterScale.FloorDb + 1e-6 ? 0 : Math.Min(Max, AudioMath.DbToLin(db));

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click bubbles to MIDI Learn
        _drag = true; _moved = false;
        _startY = e.GetPosition(this).Y; _startDb = ToDb(_value);
        e.Pointer.Capture(this); e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double h = Bounds.Height; if (h <= 0) return;
        double dy = _startY - e.GetPosition(this).Y;
        if (!_moved)
        {
            if (Math.Abs(dy) < 2) return;   // below this it is still a click
            _moved = true; GestureBegin?.Invoke();
        }
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double span = MeterScale.CeilDb - MeterScale.FloorDb;
        Set(FromDb(Math.Clamp(_startDb + dy / h * span / (fine ? 10 : 1), MeterScale.FloorDb, MeterScale.CeilDb)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null);
        if (_moved) GestureEnd?.Invoke();
        else { _holdL = _levelL; _holdR = _levelR; }   // a click clears the peak hold
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; if (_moved) GestureEnd?.Invoke(); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        // Centre the natural layout if the host is wider.
        double x = Math.Max(0, (w - NaturalWidth) / 2);

        for (int i = 0; i < Marks.Length; i++)
        {
            var t = new NotaFormattedText(MarkText[i], System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                NotaFonts.Measure, 7, NotaPalette.TextDisabled);
            double y = (1 - MeterScale.NormDb(Marks[i])) * h - t.Height / 2;
            ctx.DrawText(t, new Point(x + ScaleW - 1 - t.Width, Math.Clamp(y, 0, h - t.Height)));
        }

        double cx = x + ScaleW + 1;
        MeterScale.DrawChannel(ctx, new Rect(cx, 0, ChannelW, h), _levelL, _holdL, false);
        MeterScale.DrawChannel(ctx, new Rect(cx + ChannelW + ChannelGap, 0, ChannelW, h), _levelR, _holdR, false);

        // The volume: a hairline across the channels and the triangle in the gutter — neutral
        // at rest, the accent only while hovered or dragged.
        bool live = _drag || _hover;
        var ink = live ? NotaPalette.Accent : NotaPalette.VizInk;
        double hy = Math.Round((1 - MeterScale.NormDb(ToDb(_value))) * h);
        double chW = ChannelW * 2 + ChannelGap;
        ctx.FillRectangle(NotaPalette.Wash(ink, 0x8C), new Rect(cx, Math.Clamp(hy, 0, h - 1), chW, 1));
        double gx = cx + chW + 2;
        double ty = Math.Clamp(hy, HandleH / 2, h - HandleH / 2);
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(gx, ty), true);
            g.LineTo(new Point(gx + HandleW, ty - HandleH / 2));
            g.LineTo(new Point(gx + HandleW, ty + HandleH / 2));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(ink, null, tri);
    }
}
