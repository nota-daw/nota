// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The custom-drawn views of the Nota Delay card: the repeat window (the echo train per
// channel, L in brass above the axis and R in rose below, the sounding repeat lit), the
// LOOP column's vertical faders, and the feedback tone graph (low cut / high cut, dragged
// by its handles). They draw what the card hands them and own no engine state.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// The repeat window: the echo train per channel — L above the centre axis, R below — with
// height = level (feedback decay) and spacing = actual timing. Ping-pong and dotted offsets
// read at a glance. Frozen, every repeat stands at full height and the window is brass-lined.
// The repeat that is sounding now is lit (colour only — nothing moves).
internal sealed class DelayTaps : Control
{
    private const int MaxTaps = 24;

    private double _fracL = 0.25, _fracR = 0.25;
    private float _fb = 0.4f;
    private bool _ping, _freeze;
    private string _title = "", _sub = "", _ruler = "", _window = "";
    private int _litL = -1, _litR = -1;

    public DelayTaps() => ClipToBounds = true;

    /// <summary>Feed the window: tap spacing as a fraction of its width per channel, the
    /// feedback, the ping-pong and freeze states, and the four corner labels.</summary>
    public void Set(double fracL, double fracR, float feedback, bool ping, bool freeze,
                    string title, string sub, string ruler, string window)
    {
        _fracL = Math.Clamp(fracL, 0.02, 1.0);
        _fracR = Math.Clamp(fracR, 0.02, 1.0);
        _fb = feedback; _ping = ping; _freeze = freeze;
        _title = title; _sub = sub; _ruler = ruler; _window = window;
        InvalidateVisual();
    }

    /// <summary>Which repeat is sounding now per channel (−1 = none): the card counts them
    /// off the transport clock, this only lights them.</summary>
    public void SetLit(int litL, int litR)
    {
        if (litL == _litL && litR == _litR) return;
        _litL = litL; _litR = litR;
        InvalidateVisual();
    }

    private static FormattedText Text(string t, IBrush ink, double size = 8)
        => new(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 8 || h < 8) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        if (_freeze)
            ctx.DrawRectangle(null, new Pen(NotaPalette.Accent, 1), new RoundedRect(rect.Deflate(0.5), NotaGraph.Radius));

        double x0 = 8, x1 = w - 8, mid = h * 0.5, half = (h - 34) * 0.5;
        if (x1 <= x0 || half <= 2) return;

        // Quarter grid + the centre axis the two channels hang from.
        for (int i = 1; i < 4; i++)
        {
            double x = x0 + (x1 - x0) * i / 4.0;
            ctx.DrawLine(NotaGraph.GridPen, new Point(x, 5), new Point(x, h - 5));
        }
        ctx.DrawLine(new Pen(NotaPalette.GridBeat, 1.2), new Point(x0, mid), new Point(x1, mid));

        void Train(double frac, bool up, int lit)
        {
            var ink = up ? NotaPalette.Accent : NotaPalette.Rose;
            var litInk = up ? NotaPalette.AccentBright : NotaPalette.RoseBright;
            for (int n = 1; n <= MaxTaps; n++)
            {
                double x = x0 + n * frac * (x1 - x0);
                if (x > x1) break;
                // Height is the repeat's level on a −60 dB scale, the way the ear hears it
                // fade — a linear scale buries everything past the third repeat.
                double lvl = _freeze ? 1.0 : Math.Pow(Math.Clamp(_fb, 0.02f, 0.999f), n - 1);
                double len = Math.Max(2, Math.Clamp((20 * Math.Log10(Math.Max(lvl, 1e-9)) + 60) / 60, 0, 1) * half);
                var head = new Point(x, up ? mid - len : mid + len);
                // Ping-pong bounces the repeat across the channels, so every other one leads.
                bool lead = _ping && ((n % 2 == 1) == up);
                bool sounding = n - 1 == lit;
                var pen = sounding ? litInk : lead ? (up ? NotaPalette.AccentBright : NotaPalette.RoseBright) : ink;
                ctx.DrawLine(new Pen(pen, sounding ? 2.4 : 2), new Point(x, mid), head);
                ctx.DrawEllipse(pen, null, head, sounding ? 3 : 2.4, sounding ? 3 : 2.4);
            }
        }
        Train(_fracL, true, _litL);
        Train(_fracR, false, _litR);

        if (_title.Length > 0) ctx.DrawText(Text(_title, _freeze ? NotaPalette.AccentBright : NotaGraph.AxisInk), new Point(x0 - 2, 4));
        if (_sub.Length > 0) { var ft = Text(_sub, NotaGraph.AxisInk); ctx.DrawText(ft, new Point(x0 - 2, h - 4 - ft.Height)); }
        if (_ruler.Length > 0) { var ft = Text(_ruler, NotaGraph.AxisInk); ctx.DrawText(ft, new Point(x1 + 2 - ft.Width, 4)); }
        if (_window.Length > 0) { var ft = Text(_window, NotaGraph.AxisInk); ctx.DrawText(ft, new Point(x1 + 2 - ft.Width, h - 4 - ft.Height)); }
    }
}

// A vertical fader in the LOOP column: a groove, a wash fill in the parameter's own chroma
// and a bright handle. Drag up to raise, double-click restores the default.
internal sealed class DelayFader : Control
{
    private double _v;
    private bool _drag;

    /// <summary>The fill chroma — brass for feedback, rose for spread.</summary>
    public IBrush Ink { get; init; } = NotaPalette.Accent;
    public IBrush Handle { get; init; } = NotaPalette.AccentBright;
    public double Default { get; set; } = double.NaN;
    public bool Dragging => _drag;

    public event Action<double>? Changed;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public DelayFader() { Width = 16; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    public void Set(double v)
    {
        if (_drag) return;
        double n = Math.Clamp(v, 0, 1);
        if (Math.Abs(n - _v) < 1e-4) return;
        _v = n; InvalidateVisual();
    }

    private void Apply(PointerEventArgs e)
    {
        double h = Bounds.Height; if (h <= 2) return;
        _v = Math.Clamp(1 - e.GetPosition(this).Y / h, 0, 1);
        Changed?.Invoke(_v); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && !double.IsNaN(Default))
        {
            GestureBegin?.Invoke(); _v = Default; Changed?.Invoke(_v); GestureEnd?.Invoke();
            InvalidateVisual(); e.Handled = true; return;
        }
        _drag = true; GestureBegin?.Invoke(); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 4) return;
        var rect = new Rect(0.5, 0.5, w - 1, h - 1);
        double r = Math.Min(8, w / 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, rect, r, r);
        double y = (1 - _v) * h;
        using (ctx.PushClip(new RoundedRect(rect, r)))
            ctx.FillRectangle(NotaPalette.Wash((SolidColorBrush)Ink, 0x48), new Rect(0, y, w, h - y));
        ctx.DrawRectangle(null, new Pen(NotaPalette.BorderDefault, 1), rect, r, r);
        double hy = Math.Clamp(y, 1.5, h - 2.5);
        ctx.DrawRectangle(Handle, null, new Rect(1.5, hy - 1, w - 3, 2), 1, 1);
    }
}

// The feedback tone: the band the repeats live in, between the low cut and the high cut.
// Drag either handle horizontally to move its corner; double-click opens the band wide again.
internal sealed class DelayFilterCurve : Control
{
    private const double FMin = 20, FMax = 20000;

    private double _low, _high = 1;
    private double _lowHz = 20, _highHz = 20000;
    private int _drag;            // 0 none, 1 low cut, 2 high cut
    private int _hover;

    public event Action<int, double>? Changed;   // handle (0 low / 1 high), new normalised value
    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;
    public event Action<int>? Reset;

    public bool Dragging => _drag != 0;

    public DelayFilterCurve()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    public void Set(double lowNorm, double highNorm, double lowHz, double highHz)
    {
        _low = Math.Clamp(lowNorm, 0, 1); _high = Math.Clamp(highNorm, 0, 1);
        _lowHz = lowHz; _highHz = highHz;
        InvalidateVisual();
    }

    private static double XOf(double hz, double w) => Math.Log(Math.Clamp(hz, FMin, FMax) / FMin) / Math.Log(FMax / FMin) * w;

    private int NearestHandle(double x, double w)
        => Math.Abs(x - XOf(_lowHz, w)) <= Math.Abs(x - XOf(_highHz, w)) ? 1 : 2;

    private void Apply(PointerEventArgs e)
    {
        double w = Bounds.Width; if (w <= 4 || _drag == 0) return;
        double x = Math.Clamp(e.GetPosition(this).X, 0, w);
        double hz = FMin * Math.Pow(FMax / FMin, x / w);
        // Each handle spans its own parameter range: 20 Hz … 2 kHz, and 200 Hz … 20 kHz.
        double norm = _drag == 1
            ? Math.Log(Math.Clamp(hz, 20, 2000) / 20) / Math.Log(2000.0 / 20)
            : Math.Log(Math.Clamp(hz, 200, 20000) / 200) / Math.Log(20000.0 / 200);
        Changed?.Invoke(_drag - 1, Math.Clamp(norm, 0, 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int h = NearestHandle(e.GetPosition(this).X, Bounds.Width);
        if (e.ClickCount == 2) { Reset?.Invoke(h - 1); e.Handled = true; return; }
        _drag = h; GestureBegin?.Invoke(h - 1); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag != 0) { Apply(e); return; }
        int h = NearestHandle(e.GetPosition(this).X, Bounds.Width);
        if (h != _hover) { _hover = h; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e)
    { base.OnPointerExited(e); if (_hover != 0) { _hover = 0; InvalidateVisual(); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == 0) return;
        GestureEnd?.Invoke(_drag - 1); _drag = 0; e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 12 || h < 12) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);

        double top = 12, bottom = h - 5, span = bottom - top;
        if (span < 4) return;

        // One-pole high-pass × one-pole low-pass magnitude, −24 dB floor.
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            bool first = true;
            for (int i = 0; i <= 56; i++)
            {
                double x = w * i / 56.0;
                double f = FMin * Math.Pow(FMax / FMin, i / 56.0);
                double rl = f / _lowHz, rh = f / _highHz;
                double mag = rl / Math.Sqrt(1 + rl * rl) * (1 / Math.Sqrt(1 + rh * rh));
                double db = 20 * Math.Log10(Math.Max(mag, 1e-4));
                double y = top + span * Math.Clamp(-db / 24.0, 0, 1);
                if (first) { g.BeginFigure(new Point(x, y), false); first = false; }
                else g.LineTo(new Point(x, y));
            }
        }
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, geo);

        // Corner handles on the two cut frequencies.
        foreach (var (hz, idx) in new[] { (_lowHz, 1), (_highHz, 2) })
        {
            double x = Math.Clamp(XOf(hz, w), 2, w - 2);
            bool hot = _drag == idx || (_drag == 0 && _hover == idx);
            // Stop the stem above the corner readout so the two never cross.
            ctx.DrawLine(new Pen(hot ? NotaPalette.AccentBright : NotaPalette.BorderStrong, 1),
                new Point(x, top - 4), new Point(x, bottom - 9));
            ctx.DrawEllipse(hot ? NotaPalette.AccentBright : NotaPalette.TextSecondary, null, new Point(x, top + span * 0.5), 2.8, 2.8);
        }

        NotaGraph.Title(ctx, rect, "Filter");
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomRight, NotaNum.F($"{Hz(_lowHz)} – {Hz(_highHz)}"));
    }

    private static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.#}k") : NotaNum.F($"{hz:0}");
}
