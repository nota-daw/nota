// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The value bar, the slider inside devices (style guide, spec 038): 9 high, corner 4, in a
// groove; the soft orange tint fills up to the value and a 3px orange line marks it. While
// dragged the fill deepens toward orange. Bipolar bars fill from the centre, with a 1px
// centre mark. An inactive bar (IsDim) keeps its position in ink 4, so the number next to
// it stays meaningful. A role chroma (Ink) recolours the line. An upright bar (Vertical, for
// a device's faders) is 16 wide and fills from the bottom; it drags up and down only.
//
// It works in normalised 0..1; the caller maps to the parameter (linear, log, stepped).
// Interaction follows the almanac (§ Interaction contract), with the one exception it allows:
// a slider drags along its own axis (#115) — right increases and the handle follows the
// pointer — and a vertical drag still works as on a knob (up increases, full range over
// ~140px). Shift (or Ctrl/⌘) is fine on both; ew-resize; a click never jumps the value.
// Double-click resets when a reset is supplied; ⌥-click types a value (ValueEntry, attached
// by the row); right-click opens the parameter menu.
// GestureBegin / GestureEnd bracket the drag so automation writes are recorded, and the
// row's live-follow refresher should skip while Dragging.
//
// A Shell bar is the same control outside a device (the clip editors' rows): drawn like
// MiniFader — flat on the Sunken fill, a quiet grey fill, a 2px ink-2 line and its value
// written inside — taking the soft tint and an orange line only under the pointer or in the hand.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class SliderTrack : Control
{
    private static readonly IBrush Fill = NotaPalette.AccentSoft;
    private static readonly IBrush FillDrag = NotaPalette.Derived(() => Mix(NotaPalette.AccentSoft.Color, NotaPalette.Accent.Color, 0.45));
    private static readonly IBrush Line = NotaPalette.Accent;
    private static readonly IBrush FillDim = NotaPalette.Well;
    private static readonly IBrush LineDim = NotaPalette.TextDisabled;
    private static readonly IBrush Mark = NotaPalette.TextSecondary;   // a live level against the value
    private static readonly IBrush Centre = NotaPalette.BorderStrong;

    private static readonly IBrush ShellGround = NotaPalette.BgSunken;
    private static readonly IBrush ShellFill = NotaPalette.Derived(() => Mix(NotaPalette.BgSunken.Color, NotaPalette.TextTertiary.Color, 0.4));
    private static readonly IBrush ShellLine = NotaPalette.TextSecondary;
    private static readonly Typeface Face = NotaFonts.Sans;

    // A device bar is 9 high (DeviceH); a Shell bar and an upright fader are 16 (Height0).
    public const double LineW = 3, Height0 = 16, DeviceH = 9, ShellLineW = 2;

    internal static Color Mix(Color a, Color b, double t)
        => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private double _norm;
    private bool _dim, _drag;
    private double _lastX, _lastY;

    public bool Bipolar { get; init; }

    /// <summary>Outside a device: flat and grey at rest, the value written inside (see above).</summary>
    public bool Shell { get => _shell; init { _shell = value; if (value) Height = Height0; } }
    private readonly bool _shell;

    /// <summary>The value written inside a Shell bar; null writes nothing.</summary>
    public Func<string>? Text { get; set; }

    private bool _hover;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; if (Shell) InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; if (Shell) InvalidateVisual(); }

    /// <summary>An upright value bar (a device fader): 16 wide, filling from the bottom. Set it
    /// in the initializer; it swaps the default height for a width.</summary>
    public bool Vertical
    {
        get => _vertical;
        init
        {
            _vertical = value;
            if (!value) return;
            Height = double.NaN; Width = Height0; MinWidth = 0; MinHeight = 24;
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        }
    }
    private bool _vertical;

    /// <summary>A modulation depth. Drawn like any value bar: the guide keeps one orange for
    /// every value inside a device.</summary>
    public bool Modulation { get; init; }

    /// <summary>A role chroma for the value line (rose for a destructive amount), overriding
    /// orange. May change live.</summary>
    public IBrush? Ink { get => _ink; set { if (ReferenceEquals(_ink, value)) return; _ink = value; InvalidateVisual(); } }
    private IBrush? _ink;

    /// <summary>A 1px mark at this 0..1 position (a live level against the value); NaN hides it.</summary>
    public double Marker { get => _marker; set { if (value.Equals(_marker)) return; _marker = value; InvalidateVisual(); } }
    private double _marker = double.NaN;

    /// <summary>Double-click handler (restore the default). Null disables reset. ⌥-click types a
    /// value once the row attaches ValueEntry (DeviceCardKit.SliderRow does).</summary>
    public Action? Reset { get; init; }

    public event Action<double>? Changed;   // new normalised value, from the pointer
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public bool Dragging => _drag;

    public double Norm { get => _norm; set { var v = Math.Clamp(value, 0, 1); if (v == _norm) return; _norm = v; InvalidateVisual(); } }
    public bool IsDim { get => _dim; set { if (_dim == value) return; _dim = value; InvalidateVisual(); } }

    public SliderTrack()
    {
        Height = DeviceH;
        MinWidth = 24;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) && Reset is not null)
        {
            GestureBegin?.Invoke();
            Reset();
            GestureEnd?.Invoke();
            e.Handled = true;
            return;
        }
        _drag = true;
        var p = e.GetPosition(this);
        _lastX = p.X;
        _lastY = p.Y;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        var p = e.GetPosition(this);
        double dx = p.X - _lastX, dy = _lastY - p.Y;   // right = increase, up = increase
        _lastX = p.X;
        _lastY = p.Y;
        if (dx == 0 && dy == 0) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        // Along the bar the handle tracks the pointer; across a level bar a vertical drag still
        // works as on a knob.
        double delta = _vertical ? dy / Math.Max(Bounds.Height, 24) : dx / Math.Max(Bounds.Width, 24) + dy / 140.0;
        Nudge(delta / (fine ? 10.0 : 1.0));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        e.Pointer.Capture(null);
        GestureEnd?.Invoke();
        InvalidateVisual();   // a drag that ended off the control leaves no hover to repaint it
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Never leave an automation session dangling.
        if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    private void Nudge(double delta)
    {
        double v = Math.Clamp(_norm + delta, 0, 1);
        if (Math.Abs(v - _norm) < 1e-6) return;
        _norm = v;
        InvalidateVisual();
        Changed?.Invoke(v);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));   // whole row height hit-tests
        if (_vertical) { RenderUpright(ctx, w, h); return; }
        if (Shell) { RenderShell(ctx, w, h); return; }
        double bh = Math.Min(Height0, h), ty = (h - bh) / 2;
        var bar = new RoundedRect(new Rect(0, ty, w, bh), NotaRadius.ValueBarValue);
        NotaDepth.Groove(ctx, this, bar);

        double x = _norm * w;
        double a = Bipolar ? Math.Min(w / 2, x) : 0, b = Bipolar ? Math.Max(w / 2, x) : x;
        using (ctx.PushClip(bar))
        {
            if (b - a > 0.5) ctx.FillRectangle(_dim ? FillDim : _drag ? FillDrag : Fill, new Rect(a, ty, b - a, bh));
            if (Bipolar) ctx.FillRectangle(Centre, new Rect(Math.Round(w / 2) - 0.5, ty + 3, 1, bh - 6));
            if (!double.IsNaN(_marker) && !_dim)
                ctx.FillRectangle(Mark, new Rect(Math.Round(Math.Clamp(_marker, 0, 1) * (w - 1)), ty, 1, bh));
            double lx = Math.Clamp(x, LineW / 2, w - LineW / 2) - LineW / 2;
            ctx.FillRectangle(_dim ? LineDim : _ink ?? Line, new Rect(lx, ty, LineW, bh));
        }
    }

    private void RenderShell(DrawingContext ctx, double w, double h)
    {
        double bh = Math.Min(Height0, h), ty = (h - bh) / 2;
        var bar = new RoundedRect(new Rect(0, ty, w, bh), NotaRadius.ValueBarValue);
        ctx.DrawRectangle(ShellGround, null, bar);
        bool live = !_dim && (_drag || _hover);
        double x = _norm * w;
        double a = Bipolar ? Math.Min(w / 2, x) : 0, b = Bipolar ? Math.Max(w / 2, x) : x;
        using (ctx.PushClip(bar))
        {
            if (b - a > 0.5 && !_dim) ctx.FillRectangle(_drag ? FillDrag : live ? Fill : ShellFill, new Rect(a, ty, b - a, bh));
            if (Bipolar) ctx.FillRectangle(Centre, new Rect(Math.Round(w / 2) - 0.5, ty + 3, 1, bh - 6));
            double lx = Math.Clamp(x, ShellLineW / 2, w - ShellLineW / 2) - ShellLineW / 2;
            ctx.FillRectangle(_dim ? LineDim : live ? Line : ShellLine, new Rect(lx, ty, ShellLineW, bh));
            if (Text?.Invoke() is { Length: > 0 } str && bh >= 14)
            {
                var t = new NotaFormattedText(str, NotaNum.Culture, FlowDirection.LeftToRight, Face, 10.5,
                    _dim ? NotaPalette.TextDisabled : NotaPalette.TextPrimary);
                double fx = Bipolar ? 0 : x;
                double tx = Bipolar || t.Width + 12 <= fx ? 6 : Math.Min(fx + ShellLineW + 4, w - t.Width - 4);
                ctx.DrawText(t, new Point(tx, ty + (bh - t.Height) / 2));
            }
        }
    }

    private void RenderUpright(DrawingContext ctx, double w, double h)
    {
        double bw = Math.Min(Height0, w), tx = (w - bw) / 2;
        var bar = new RoundedRect(new Rect(tx, 0, bw, h), NotaRadius.ValueBarValue);
        NotaDepth.Groove(ctx, this, bar);

        double y = (1 - _norm) * h;
        double a = Bipolar ? Math.Min(h / 2, y) : y, b = Bipolar ? Math.Max(h / 2, y) : h;
        using (ctx.PushClip(bar))
        {
            if (b - a > 0.5) ctx.FillRectangle(_dim ? FillDim : _drag ? FillDrag : Fill, new Rect(tx, a, bw, b - a));
            if (Bipolar) ctx.FillRectangle(Centre, new Rect(tx + 3, Math.Round(h / 2) - 0.5, bw - 6, 1));
            if (!double.IsNaN(_marker) && !_dim)
                ctx.FillRectangle(Mark, new Rect(tx, Math.Round((1 - Math.Clamp(_marker, 0, 1)) * (h - 1)), bw, 1));
            double ly = Math.Clamp(y, LineW / 2, h - LineW / 2) - LineW / 2;
            ctx.FillRectangle(_dim ? LineDim : _ink ?? Line, new Rect(tx, ly, bw, LineW));
        }
    }
}
