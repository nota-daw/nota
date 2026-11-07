// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the upright fader the device cards draw themselves (Nota Level's
// correction fader). It keeps its own gesture (drag, wheel, double-click reset) and is drawn
// as the style guide's upright value bar (UprightBar): 16 wide, corner 4, in a groove; the
// soft orange tint fills up to the value and a 3px orange line marks it, deepening while
// dragged.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

/// <summary>Draws an upright value bar the way SliderTrack does (Vertical), for the device
/// faders that keep their own gestures.</summary>
internal static class UprightBar
{
    private static readonly IBrush Fill = NotaPalette.AccentSoft;
    private static readonly IBrush FillDrag = NotaPalette.Derived(() => SliderTrack.Mix(NotaPalette.AccentSoft.Color, NotaPalette.Accent.Color, 0.45));
    private static readonly IBrush Centre = NotaPalette.BorderStrong;

    /// <param name="norm">The value, 0..1 from the bottom (from the top with <paramref name="fromTop"/>).</param>
    /// <param name="live">Where modulation has the value now (a dashed line), or NaN.</param>
    /// <param name="marker">A live level the value works against (a 1px tick), or NaN.</param>
    /// <param name="tick">A threshold (a dashed amber line), or NaN.</param>
    /// <param name="ink">Recolours the fill and line (a level, not a value).</param>
    public static void Draw(DrawingContext ctx, Control owner, double norm, bool drag, double live = double.NaN,
        double marker = double.NaN, double tick = double.NaN, bool fromTop = false, bool bipolar = false, bool dim = false, IBrush? ink = null)
    {
        double w = owner.Bounds.Width, h = owner.Bounds.Height;
        if (w < 2 || h < 4) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));   // the whole box hit-tests
        double bw = Math.Min(SliderTrack.Height0, w), tx = (w - bw) / 2;
        var bar = new RoundedRect(new Rect(tx, 0, bw, h), NotaRadius.ValueBarValue);
        NotaDepth.Groove(ctx, owner, bar);
        norm = Math.Clamp(norm, 0, 1);
        double y = fromTop ? norm * h : (1 - norm) * h;
        double a = bipolar ? Math.Min(h / 2, y) : fromTop ? 0 : y, b = bipolar ? Math.Max(h / 2, y) : fromTop ? y : h;
        IBrush fill = dim ? NotaPalette.Well : ink is SolidColorBrush s ? NotaPalette.Wash(s, 0x48) : drag ? FillDrag : Fill;
        IBrush line = dim ? NotaPalette.TextDisabled : drag ? NotaPalette.Accent : ink ?? NotaPalette.Accent;
        using (ctx.PushClip(bar))
        {
            if (b - a > 0.5) ctx.FillRectangle(fill, new Rect(tx, a, bw, b - a));
            if (bipolar) ctx.FillRectangle(Centre, new Rect(tx + 3, Math.Round(h / 2) - 0.5, bw - 6, 1));
            if (!double.IsNaN(marker) && !dim)
                ctx.FillRectangle(NotaPalette.TextSecondary, new Rect(tx, Math.Round((1 - Math.Clamp(marker, 0, 1)) * (h - 1)), bw, 1));
            if (!double.IsNaN(live) && Math.Abs(live - norm) > 0.005)
            {
                double ly = Math.Clamp((1 - Math.Clamp(live, 0, 1)) * h, 1, h - 1);
                ctx.DrawLine(new Pen(NotaPalette.TextSecondary, 1, new DashStyle(new double[] { 2, 1 }, 0)), new Point(tx + 2, ly), new Point(tx + bw - 2, ly));
            }
            if (!double.IsNaN(tick))
            {
                double ty = (1 - Math.Clamp(tick, 0, 1)) * h;
                ctx.DrawLine(new Pen(NotaPalette.Threshold, 1, new DashStyle(new double[] { 2, 2 }, 0)), new Point(tx, ty), new Point(tx + bw, ty));
            }
            if (ink is null || norm > 0.004)
            {
                double ly = Math.Clamp(y, SliderTrack.LineW / 2, h - SliderTrack.LineW / 2) - SliderTrack.LineW / 2;
                ctx.FillRectangle(line, new Rect(tx, ly, bw, SliderTrack.LineW));
            }
        }
    }
}

internal sealed class LvGainFader : Control
{
    private double _db, _max = 12;
    private bool _manual, _on = true, _drag;
    private double _y0, _v0;

    /// <summary>The manual gain (dB) at the start of a drag.</summary>
    public Func<double>? Value { get; set; }
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double>? Changed;   // new gain in dB
    public event Action? ResetRequested;
    public bool Dragging => _drag;

    public LvGainFader() { Width = 14; }

    public void Set(double db, double maxDb, bool manual, bool on)
    {
        _db = db; _max = Math.Max(0.01, maxDb); _manual = manual; _on = on;
        Cursor = manual ? new Cursor(StandardCursorType.SizeNorthSouth) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_manual || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { ResetRequested?.Invoke(); e.Handled = true; return; }
        _v0 = Value?.Invoke() ?? _db; _y0 = e.GetPosition(this).Y; _drag = true;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        // Full range (−max … +max) over ~140 px, fine steps with a modifier.
        double d = (_y0 - e.GetPosition(this).Y) / 140.0 * 2 * _max * (fine ? 0.1 : 1);
        Changed?.Invoke(Math.Round(Math.Clamp(_v0 + d, -_max, _max) * 10) / 10);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; InvalidateVisual(); e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        // A bipolar bar: it fills from 0 dB toward the gain, in ink 4 while off and in the
        // tertiary ink when the gain is automatic rather than set by hand.
        double n = 0.5 + Math.Clamp(_db / _max, -1, 1) / 2;
        UprightBar.Draw(ctx, this, n, _drag, bipolar: true, dim: !_on,
            ink: _manual ? null : NotaPalette.TextTertiary);
    }
}
