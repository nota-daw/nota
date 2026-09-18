// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A performance wheel — the pitch-bend / modulation pair every hardware synth puts left of
// the keys, drawn flat to the almanac: a sunken pill, a hairline border, tick marks and a
// Brass Light bar at the value. Nota Operator's card keeps both on screen on every tab.
//
// Interaction matches the knob contract: vertical drag (up increases) over the wheel's own
// height, Shift/Ctrl/⌘ for fine, a click never jumps the value, double-click restores the
// default, and a spring wheel (pitch bend) returns to its default when released.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class PerformWheel : Control
{
    private const double Travel = 140;   // px of drag for the full range — the knob's feel

    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush Face = NotaPalette.SurfaceRaised;
    private static readonly IBrush Edge = NotaPalette.BorderDefault;
    private static readonly IBrush Tick = NotaPalette.BorderStrong;
    private static readonly IBrush Bar = NotaPalette.AccentBright;
    private static readonly IBrush BarOff = NotaPalette.TextTertiary;

    private double _norm;
    private bool _drag;
    private double _startY, _startV;

    /// <summary>Value restored on double-click, and sprung back to when <see cref="Spring"/>.</summary>
    public double Default { get; init; } = 0;

    /// <summary>True for a pitch wheel: it snaps back to <see cref="Default"/> on release.</summary>
    public bool Spring { get; init; }

    public bool Dragging => _drag;

    public event Action<double>? ValueChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public PerformWheel()
    {
        Width = 16;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        Focusable = false;
    }

    /// <summary>Follow an external write (automation, a preset) without raising ValueChanged.</summary>
    public double Norm
    {
        get => _norm;
        set { double v = Math.Clamp(value, 0, 1); if (Math.Abs(v - _norm) < 1e-6) return; _norm = v; InvalidateVisual(); }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;   // right-click bubbles to the CV / learn menu
        if (e.ClickCount >= 2) { Write(Default); e.Handled = true; return; }
        _drag = true; _startY = pt.Position.Y; _startV = _norm;
        GestureBegin?.Invoke();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control)
                   || e.KeyModifiers.HasFlag(KeyModifiers.Meta) ? 0.25 : 1.0;
        Write(_startV + (_startY - e.GetPosition(this).Y) / Travel * fine);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        e.Pointer.Capture(null);
        if (Spring) Write(Default);
        GestureEnd?.Invoke();
    }

    private void Write(double v)
    {
        v = Math.Clamp(v, 0, 1);
        if (Math.Abs(v - _norm) < 1e-6) return;
        _norm = v; InvalidateVisual(); ValueChanged?.Invoke(v);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 6) return;
        double r = w / 2;
        var body = new Rect(0.5, 0.5, w - 1, h - 1);
        ctx.DrawRectangle(Well, new Pen(Edge, 1), body, r, r);

        // A short raised face in the middle third — the part the thumb would touch.
        double faceH = Math.Max(10, h * 0.34);
        ctx.DrawRectangle(Face, null, new Rect(1.5, (h - faceH) / 2, w - 3, faceH), r - 1, r - 1);

        // Grip ticks over the face, evenly spaced.
        for (int i = 1; i <= 3; i++)
        {
            double ty = (h - faceH) / 2 + faceH * i / 4.0;
            ctx.DrawLine(new Pen(Tick, 1), new Point(3, ty), new Point(w - 3, ty));
        }

        // The value bar. Pitch reads from the centre, so at rest it sits on the centreline.
        double pad = 3;
        double y = pad + (1 - _norm) * (h - pad * 2);
        bool live = Math.Abs(_norm - Default) > 1e-3;
        ctx.DrawRectangle(live ? Bar : BarOff, null, new Rect(1.5, y - 1, w - 3, 2), 1, 1);
    }
}
