// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Custom-drawn views for the Nota Chamber card, one graph of one thing per tab (#531):
//   · ChamberIrView — the room's recording (the impulse response) as a clip: Start / End
//     brackets trim it, and the fade-in (Attack) and the fade-out the engine applies shape the
//     waveform where they act. Also the drop target for a user IR.
//   · ChamberDecayGraph — how long each frequency rings: one curve over Lows · Mids · Highs,
//     a handle on each plateau (drag up / down) and the Damping boundary (drag sideways).
//   · ChamberEqCurve — the tail EQ on the shared filter graph (FilterGraphBase): low cut +
//     low shelf on one handle, high cut + high shelf on the other.
//   · ChamberFlowView — the signal flow: the input through Convolution and Algorithm (side by
//     side or one into the other), the EQ where it sits, the stereo stage, ducking, the dry
//     path and the output. A click on an engine turns it on or off; a click on the EQ moves it.
// Values are pushed in by the card's refresh tick; the interactive views raise events so the
// card writes the params and records automation gestures.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application.Localization;

namespace Nota.App;

internal static class ChamberInk
{
    public static readonly IBrush Veil = NotaPalette.Wash(NotaPalette.SurfaceAbyss, 0xB8);
    public static readonly IBrush GridLine = NotaPalette.SurfaceCard;
    private static readonly Typeface NumFace = NotaFonts.Sans;
    private static readonly Typeface Sans = NotaFonts.SansSemiBold;

    public static FormattedText Text(string s, double size, IBrush b) => new NotaFormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NumFace, size, b, NotaTextSize.InCard);
    public static FormattedText Caps(string s, double size, IBrush b) => new NotaFormattedText(NotaText.Label(s), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, size, b, NotaTextSize.InCard);
    public static IBrush Slate => NotaPalette.TextSecondary;   // convolution (was slate blue)
    public static IBrush Mauve => NotaPalette.TextTertiary;    // shimmer / pitch (was mauve)
    public static IBrush Alpha(Color c, byte a) => new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));

    // A time axis that stays put while the content moves inside it (so turning a knob visibly
    // moves the curve instead of rescaling it away), and steps to the next range only when the
    // content no longer fits or shrinks well below it.
    private static readonly double[] Ranges = { 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 30, 45, 60, 90 };
    public static double Axis(double current, double content)
    {
        double need = Math.Max(0.1, content) * 1.08;
        if (current > 0 && need <= current && need >= current * 0.4) return current;
        foreach (double r in Ranges) if (r >= need) return r;
        return Ranges[^1];
    }
    public static string AxisLabel(double s) => s >= 1 ? NotaNum.F($"{s:0.##} s") : NotaNum.F($"{s * 1000:0} ms");
    // Normalised amplitude → 0..1 height on a −60 dB floor.
    public static double DbHeight(double v, double floorDb = 60) => Math.Clamp((20 * Math.Log10(Math.Max(v, 1e-9)) + floorDb) / floorDb, 0, 1);

    public static string Sec(double s) => s >= 10 ? NotaNum.F($"{s:0.0} s") : s >= 1 ? NotaNum.F($"{s:0.00} s") : NotaNum.F($"{s * 1000:0} ms");

    /// <summary>A graph handle: hollow in the accent, filled with a halo while hot, its label
    /// beside it (flipped left at the right edge) above or below.</summary>
    public static void Handle(DrawingContext ctx, Rect plot, Point at, bool hot, string? label, bool above = true)
    {
        if (hot) ctx.DrawEllipse(NotaPalette.Wash(NotaPalette.Accent, 0x40), null, at, 8, 8);
        ctx.DrawEllipse(hot ? NotaPalette.AccentBright : NotaGraph.Ground, new Pen(NotaPalette.AccentBright, 1.6), at, 3.8, 3.8);
        if (label is null) return;
        var ft = NotaGraph.AxisText(label, hot ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
        double x = at.X + 7;
        if (x + ft.Width > plot.Right) x = at.X - 7 - ft.Width;
        double y = above ? at.Y - ft.Height - 2 : at.Y + 3;
        y = Math.Clamp(y, plot.Y, plot.Bottom - ft.Height);
        NotaGraph.PlateText(ctx, ft, new Point(Math.Max(plot.X, x), y));
    }
}

// THE IMPULSE RESPONSE as a clip: the room's recording (both channels' peak, mirrored about the
// centre line) on its own time axis. Start / End brackets keep a stretch of it — the rest is
// veiled — and inside it the waveform is drawn as the engine shapes it: the fade-in (Attack,
// squared), the fade-out that ends a trimmed IR, and reversed when Reverse IR is on. A faint
// copy behind shows what the fades take away. Drag a bracket (a press takes the nearer one),
// double-click resets both. Drop an audio file (Finder or the browser) to load it.
internal sealed class ChamberIrView : Control, IValueHandles
{
    private const double Pad = 8, AxisRow = 13;
    private float[] _l = Array.Empty<float>(), _r = Array.Empty<float>();
    private double _sec, _start, _end = 1, _attackSec, _size = 1;
    private bool _reverse, _building, _hover;
    private string? _empty;
    private int _drag, _hot;   // 0 none, 1 start, 2 end

    public event Action<double, double>? TrimChanged;    // (start, end) fractions
    public event Action<int>? DragStarted;               // 1 start / 2 end
    public event Action<int>? DragEnded;
    public event Action<string>? FileDropped;
    public event Action? DropHover;                      // a file drag is over the view
    public int DraggingHandle => _drag;

    public ChamberIrView()
    {
        ClipToBounds = true;
        MinHeight = 60;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) => { if (BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.Copy; _hover = true; DropHover?.Invoke(); InvalidateVisual(); e.Handled = true; } });
        DragDrop.AddDragLeaveHandler(this, (_, _) => { _hover = false; InvalidateVisual(); });
        DragDrop.AddDropHandler(this, (_, e) =>
        {
            _hover = false; InvalidateVisual();
            foreach (var it in BrowserView.DroppedItems(e))
                if (it.Kind != Nota.Presentation.BrowserItemKind.MidiFile && it.Path is { Length: > 0 } p) { FileDropped?.Invoke(p); break; }
            e.Handled = true;
        });
    }

    public void SetWave(float[] l, float[] r) { _l = l; _r = r; InvalidateVisual(); }
    /// <summary>The IR's length (s), the trims (0..1), the fade-in (s, as heard), the Size stretch,
    /// reverse, a rebuild in progress, and the text for an empty user slot (null when loaded).</summary>
    public void Set(double seconds, double start, double end, double attackSec, double size, bool reverse, bool building, string? emptyText)
    {
        _sec = seconds; _attackSec = attackSec; _size = Math.Max(0.25, size); _reverse = reverse; _building = building; _empty = emptyText;
        if (_drag == 0) { _start = start; _end = end; }
        InvalidateVisual();
    }

    private Rect Plot => new(Pad, NotaGraph.PlotTop, Math.Max(1, Bounds.Width - 2 * Pad), Math.Max(1, Bounds.Height - NotaGraph.PlotTop - AxisRow - 2));
    private double X(double f) { var r = Plot; return r.X + Math.Clamp(f, 0, 1) * r.Width; }
    private int Nearest(double x) => Math.Abs(x - X(_start)) <= Math.Abs(x - X(_end)) ? 1 : 2;

    private void Apply(PointerEventArgs e)
    {
        var r = Plot;
        double f = Math.Clamp((e.GetPosition(this).X - r.X) / r.Width, 0, 1);
        if (_drag == 1) _start = Math.Min(f, Math.Min(0.9, _end - 0.02));
        else _end = Math.Max(f, Math.Max(0.05, _start + 0.02));
        TrimChanged?.Invoke(_start, _end); InvalidateVisual();
    }
    // Typed entry (specs/035): handle 0 = the start bracket, 1 = the end bracket.
    public int HandleAt(Point p) => _empty != null ? -1 : Nearest(p.X) - 1;
    public Rect HandleRect(int handle) => ValueEntry.Near(new Point(X(handle == 0 ? _start : _end), Plot.Y), Bounds.Size);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _empty != null) return;
        if (e.ClickCount == 2)
        {
            DragStarted?.Invoke(1); DragStarted?.Invoke(2);
            _start = 0; _end = 1; TrimChanged?.Invoke(_start, _end);
            DragEnded?.Invoke(1); DragEnded?.Invoke(2);
            InvalidateVisual(); e.Handled = true; return;
        }
        _drag = Nearest(e.GetPosition(this).X);
        DragStarted?.Invoke(_drag); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag != 0) { Apply(e); return; }
        int h = _empty != null ? 0 : Nearest(e.GetPosition(this).X);
        if (h != _hot) { _hot = h; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hot != 0) { _hot = 0; InvalidateVisual(); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag != 0) { int d = _drag; _drag = 0; e.Pointer.Capture(null); InvalidateVisual(); DragEnded?.Invoke(d); } }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { if (_drag != 0) { int d = _drag; _drag = 0; InvalidateVisual(); DragEnded?.Invoke(d); } base.OnPointerCaptureLost(e); }

    // The peak of both channels at a source position (0..1).
    private double PeakAt(double f)
    {
        int n = Math.Max(_l.Length, _r.Length);
        if (n < 2) return 0;
        int i = Math.Clamp((int)Math.Round(f * (n - 1)), 0, n - 1);
        float a = i < _l.Length ? _l[i] : 0, b = i < _r.Length ? _r[i] : a;
        return Math.Sqrt(Math.Clamp(Math.Max(a, b), 0f, 1f));   // √ lifts the tail into view
    }

    // The engine's gain at a point u (0..1) of the kept stretch, in the order it is heard
    // (ChamberIrSynth::shapeIr): a raised-cosine fade-out on the forward window's end (it moves
    // to the head when reversed), then a squared fade-in on what is heard first.
    private double GainAt(double u, double keptSec)
    {
        if (keptSec <= 0) return 1;
        double heardSec = keptSec / _size;
        double fo = _end < 0.999 ? Math.Min(0.3 * heardSec, 0.35) : Math.Min(heardSec / 4, 0.01);
        double fi = Math.Max(_attackSec, _start > 0.0005 || _reverse ? 0.002 : 0);
        double t = u * heardSec;                          // heard time into the kept stretch
        double tf = _reverse ? t : heardSec - t;          // time from the forward window's end
        double g = tf < fo ? 0.5 - 0.5 * Math.Cos(Math.PI * tf / fo) : 1;
        if (t < fi) { double k = t / fi; g *= k * k; }
        return g;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 40) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        if (_empty != null)
        {
            var t = NotaGraph.AxisText(_empty, NotaPalette.TextTertiary);
            ctx.DrawText(t, new Point((w - t.Width) / 2, (h - t.Height) / 2));
            if (_hover) ctx.DrawRectangle(null, new Pen(NotaPalette.AccentBright, 1.5), new RoundedRect(rect.Deflate(1), NotaGraph.Radius));
            return;
        }
        var r = Plot;
        double mid = r.Y + r.Height / 2, amp = r.Height / 2 - 2;
        ctx.DrawLine(NotaGraph.GridPen, new Point(r.X, mid), new Point(r.Right, mid));

        // The time grid and its labels (the source's own time).
        if (_sec > 0)
        {
            double step = _sec <= 0.6 ? 0.1 : _sec <= 1.5 ? 0.25 : _sec <= 3 ? 0.5 : _sec <= 8 ? 1 : 2;
            for (double s = step; s < _sec - step * 0.3; s += step)
            {
                double x = X(s / _sec);
                ctx.DrawLine(NotaGraph.GridPen, new Point(x, r.Y), new Point(x, r.Bottom));
                var ft = NotaGraph.AxisText(ChamberInk.AxisLabel(s));
                ctx.DrawText(ft, new Point(x - ft.Width / 2, r.Bottom + 2));
            }
            ctx.DrawText(NotaGraph.AxisText("0"), new Point(r.X - 2, r.Bottom + 2));
        }

        // The whole recording, faint: what the brackets leave out and the fades take away.
        int cols = (int)Math.Clamp(r.Width / 1.5, 40, 480);
        Geometry Wave(Func<double, double> heightAt)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(r.X, mid), true);
                for (int i = 0; i <= cols; i++) { double f = (double)i / cols; c.LineTo(new Point(X(f), mid - heightAt(f) * amp)); }
                for (int i = cols; i >= 0; i--) { double f = (double)i / cols; c.LineTo(new Point(X(f), mid + heightAt(f) * amp)); }
                c.EndFigure(true);
            }
            return g;
        }
        ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.VizInk, 0x22), null, Wave(PeakAt));

        // The kept stretch as heard: reversed inside the brackets when Reverse IR is on, faded.
        double s0 = _start, s1 = _end, keptSec = (s1 - s0) * _sec;
        double Heard(double f)
        {
            if (f < s0 || f > s1 || s1 <= s0) return 0;
            double u = (f - s0) / (s1 - s0);
            return PeakAt(_reverse ? s1 - u * (s1 - s0) : f) * GainAt(u, keptSec);
        }
        using (ctx.PushClip(new Rect(X(s0), r.Y, Math.Max(0, X(s1) - X(s0)), r.Height)))
        {
            var heard = Wave(Heard);
            ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x26), new Pen(NotaPalette.VizInk, 1), heard);
        }

        // The gain line over the kept stretch: where the fades act.
        var gl = new StreamGeometry();
        using (var c = gl.Open())
        {
            for (int i = 0; i <= 120; i++)
            {
                double u = i / 120.0;
                var p = new Point(X(s0 + u * (s1 - s0)), r.Y + 1 + (1 - GainAt(u, keptSec)) * (mid - r.Y - 1));
                if (i == 0) c.BeginFigure(p, false); else c.LineTo(p);
            }
        }
        ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xA0), 1) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) }, gl);

        // Veil outside the brackets, then the brackets: a line with a grab tab at the top.
        double xs = X(s0), xe = X(s1);
        ctx.FillRectangle(ChamberInk.Veil, new Rect(r.X, r.Y, xs - r.X, r.Height));
        ctx.FillRectangle(ChamberInk.Veil, new Rect(xe, r.Y, r.Right - xe, r.Height));
        void Bracket(double x, int id, bool left, string label)
        {
            bool hot = _drag == id || (_drag == 0 && _hot == id);
            var ink = hot ? NotaPalette.AccentBright : NotaPalette.VizInk;
            ctx.DrawLine(new Pen(ink, hot ? 1.6 : 1.2), new Point(x, r.Y), new Point(x, r.Bottom));
            var tab = left ? new Rect(x, r.Y, 5, 12) : new Rect(x - 5, r.Y, 5, 12);
            ctx.DrawRectangle(ink, null, tab, 1.5, 1.5);
            ctx.DrawLine(new Pen(ink, 2), new Point(left ? x : x - 5, r.Bottom - 0.5), new Point(left ? x + 5 : x, r.Bottom - 0.5));
            var ft = NotaGraph.AxisText(label, hot ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
            double tx = left ? x + 8 : x - 8 - ft.Width;
            if (left && tx + ft.Width > xe - 10) tx = Math.Max(r.X, x - 8 - ft.Width);
            NotaGraph.PlateText(ctx, ft, new Point(Math.Clamp(tx, r.X, r.Right - ft.Width), r.Y + 1));
        }
        Bracket(xs, 1, true, "Start " + ChamberInk.Sec(s0 * _sec));
        Bracket(xe, 2, false, "End " + ChamberInk.Sec(s1 * _sec));

        // Title, and the state on the right: reversed, rebuilding.
        NotaGraph.Title(ctx, rect, Loc.T("device.chamber.impulse_response"));
        var tags = new List<string>();
        if (_reverse) tags.Add(Loc.T("device.chamber.reversed"));
        if (_building) tags.Add(Loc.T("device.chamber.building"));
        if (tags.Count > 0)
        {
            var ft = NotaGraph.AxisText(string.Join(" · ", tags), NotaPalette.TextSecondary);
            ctx.DrawText(ft, new Point(w - 8 - ft.Width, 5));
        }
        if (_hover) ctx.DrawRectangle(null, new Pen(NotaPalette.AccentBright, 1.5), new RoundedRect(rect.Deflate(1), NotaGraph.Radius));
    }
}

// DECAY TIME BY FREQUENCY: how long each part of the spectrum rings. The algorithm splits its
// tail in three bands (Chamber::Algo — below 250 Hz, up to the Damping frequency, above it), so
// the curve is three plateaus (named along the floor) with soft steps between them. A handle on each plateau sets that
// band's decay (drag up / down: the mids are Decay, lows and highs are stored relative to it, so
// they ride along); the Damping boundary is a dashed line with its own handle (drag sideways).
// Double-click a handle to reset it. Frozen, the curve is a held line under an accent frame.
internal sealed class ChamberDecayGraph : Control, IValueHandles
{
    private const double Left = 34, AxisRow = 13, FLo = 20, FHi = 20000, LowEdge = 250;
    private double _rtL = 3, _rtM = 3, _rtH = 1.5, _damp = 4500, _ymax = 4;
    private bool _freeze;
    private int _drag = -1, _hot = -1;   // 0 low · 1 mid · 2 high · 3 damping

    public event Action<int, double>? Changed;    // band (0 low / 1 mid / 2 high), RT60 seconds
    public event Action<double>? DampChanged;     // Damping, normalised
    public event Action<int>? DragStarted;        // handle 0..3
    public event Action<int>? DragEnded;
    public event Action<int>? Reset;
    public bool Dragging => _drag >= 0;

    public ChamberDecayGraph() { ClipToBounds = true; MinHeight = 60; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    public void Set(double rtLow, double rtMid, double rtHigh, double dampHz, bool freeze)
    {
        _freeze = freeze;
        if (_drag < 0)
        {
            _rtL = rtLow; _rtM = rtMid; _rtH = rtHigh; _damp = dampHz;
            _ymax = ChamberInk.Axis(_ymax, Math.Max(_rtL, Math.Max(_rtM, _rtH)));   // steady axis
        }
        InvalidateVisual();
    }

    // ---- geometry ------------------------------------------------------------------
    private Rect Plot => new(Left, NotaGraph.PlotTop, Math.Max(1, Bounds.Width - Left - 8), Math.Max(1, Bounds.Height - NotaGraph.PlotTop - AxisRow - 2));
    private double XOf(double hz) { var r = Plot; return r.X + Math.Log(Math.Clamp(hz, FLo, FHi) / FLo) / Math.Log(FHi / FLo) * r.Width; }
    private double HzAt(double x) { var r = Plot; return FLo * Math.Pow(FHi / FLo, Math.Clamp((x - r.X) / r.Width, 0, 1)); }
    private double YOf(double s) { var r = Plot; return r.Bottom - Math.Clamp(s / _ymax, 0, 1) * (r.Height - 4); }
    private double SecAt(double y) { var r = Plot; return Math.Max(0, (r.Bottom - y) / (r.Height - 4) * _ymax); }

    // The decay at a frequency: the three bands crossed over smoothly (one-pole-like splits).
    private double RtAt(double f)
    {
        double wl = 1 / (1 + Math.Pow(f / LowEdge, 4)), rh = Math.Pow(f / _damp, 4), wh = rh / (1 + rh);
        double wm = Math.Max(0, 1 - wl - wh);
        double sum = wl + wm + wh;
        return Math.Exp((wl * Math.Log(_rtL) + wm * Math.Log(_rtM) + wh * Math.Log(_rtH)) / sum);
    }
    private double BandHz(int b) => b switch { 0 => 70, 1 => Math.Sqrt(LowEdge * _damp), _ => Math.Min(14000, Math.Sqrt(_damp * FHi) * 1.2) };
    private double BandRt(int b) => b switch { 0 => _rtL, 1 => _rtM, _ => _rtH };
    private Point HandlePt(int h) => h == 3 ? new Point(XOf(_damp), Plot.Bottom - 6) : new Point(XOf(BandHz(h)), YOf(BandRt(h)));

    private int Nearest(Point p)
    {
        int best = 0; double bd = double.MaxValue;
        for (int h = 0; h < 4; h++)
        {
            var q = HandlePt(h);
            double d = h == 3 ? Math.Abs(p.X - q.X) * 1.6 + 6 : Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y) * 0.6;
            if (d < bd) { bd = d; best = h; }
        }
        return best;
    }

    // Typed entry (specs/035): handles 0 low · 1 mid (Decay) · 2 high · 3 Damping.
    public int HandleAt(Point p) => _freeze ? -1 : Nearest(p);
    public Rect HandleRect(int handle) => ValueEntry.Near(HandlePt(handle), Bounds.Size);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _freeze) return;
        var p = e.GetPosition(this);
        int h = Nearest(p);
        if (e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) { Reset?.Invoke(h); e.Handled = true; return; }
        _drag = h; DragStarted?.Invoke(h); e.Pointer.Capture(this); Apply(p); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag >= 0) { Apply(p); return; }
        int h = _freeze ? -1 : Nearest(p);
        if (h != _hot)
        {
            _hot = h;
            Cursor = new Cursor(h == 3 ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
            InvalidateVisual();
        }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hot >= 0) { _hot = -1; InvalidateVisual(); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag >= 0) { int b = _drag; _drag = -1; e.Pointer.Capture(null); InvalidateVisual(); DragEnded?.Invoke(b); } }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { if (_drag >= 0) { int b = _drag; _drag = -1; InvalidateVisual(); DragEnded?.Invoke(b); } base.OnPointerCaptureLost(e); }

    private void Apply(Point p)
    {
        if (_drag == 3)
        {
            _damp = Math.Clamp(HzAt(p.X), 500, 18000);
            DampChanged?.Invoke(Math.Log(_damp / 500) / Math.Log(18000.0 / 500));
        }
        else
        {
            double rt = Math.Max(0.05, SecAt(p.Y));
            if (_drag == 1)
            {
                // Lows and highs are ratios of the mids: they ride along.
                double k = Math.Clamp(rt, 0.2, 20) / _rtM;
                _rtM *= k; _rtL *= k; _rtH *= k;
                Changed?.Invoke(1, _rtM);
            }
            else
            {
                rt = Math.Clamp(rt, _rtM * 0.25, _rtM * 4);
                if (_drag == 0) _rtL = rt; else _rtH = rt;
                Changed?.Invoke(_drag, rt);
            }
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 60 || h < 50) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        if (_freeze)
        {
            ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x0D), rect);
            ctx.DrawRectangle(null, new Pen(NotaPalette.BorderBrass, 1), new RoundedRect(rect.Deflate(0.5), NotaGraph.Radius));
        }
        var r = Plot;

        // Seconds up the left, the decades along the bottom.
        double step = _ymax <= 1 ? 0.25 : _ymax <= 3 ? 0.5 : _ymax <= 6 ? 1 : _ymax <= 12 ? 2 : 5;
        for (double s = step; s <= _ymax + 1e-6; s += step)
        {
            double y = YOf(s);
            ctx.DrawLine(NotaGraph.GridPen, new Point(r.X, y), new Point(r.Right, y));
            var ft = NotaGraph.AxisText(ChamberInk.AxisLabel(s));
            if (y - ft.Height / 2 > r.Y - 2) ctx.DrawText(ft, new Point(Left - 4 - ft.Width, y - ft.Height / 2));
        }
        foreach (var f in new[] { 100.0, 1000, 10000 })
        {
            double x = XOf(f);
            ctx.DrawLine(NotaGraph.GridPen, new Point(x, r.Y), new Point(x, r.Bottom));
        }
        foreach (var f in new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000 })
        {
            var ft = NotaGraph.AxisText(NotaNum.Hz(f));
            ctx.DrawText(ft, new Point(XOf(f) - ft.Width / 2, r.Bottom + 2));
        }

        // The three bands, named along the floor.
        double xl = XOf(LowEdge), xd = XOf(_damp);
        ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.TextTertiary, 0x80), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }, new Point(xl, r.Y), new Point(xl, r.Bottom));
        bool dampHot = _drag == 3 || (_drag < 0 && _hot == 3);
        ctx.DrawLine(new Pen(dampHot ? NotaPalette.AccentBright : NotaPalette.Wash(NotaPalette.Accent, 0x90), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }, new Point(xd, r.Y), new Point(xd, r.Bottom));
        void BandName(string t, double x0, double x1)
        {
            var ft = NotaGraph.AxisText(t, NotaPalette.TextTertiary);
            if (x1 - x0 < ft.Width + 6) return;
            ctx.DrawText(ft, new Point((x0 + x1 - ft.Width) / 2, r.Bottom - ft.Height - 2));
        }
        BandName(Loc.T("device.chamber.lows"), r.X, xl);
        BandName(Loc.T("device.chamber.mids"), xl, xd);
        BandName(Loc.T("device.chamber.highs"), xd, r.Right);

        if (_freeze)
        {
            double y = r.Y + 8;
            ctx.DrawLine(new Pen(NotaPalette.AccentBright, 1.8), new Point(r.X, y), new Point(r.Right, y));
            NotaGraph.Title(ctx, rect, Loc.T("device.chamber.decay_time_by_frequency"));
            var ft = NotaGraph.AxisText(Loc.T("device.chamber.frozen_the_tail_holds"), NotaPalette.AccentBright);
            ctx.DrawText(ft, new Point(w - 8 - ft.Width, 5));
            return;
        }

        // The curve, with a light fill down to the floor.
        int n = (int)Math.Clamp(r.Width / 1.5, 40, 400);
        var line = new StreamGeometry(); var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            ac.BeginFigure(new Point(r.X, r.Bottom), true);
            for (int i = 0; i < n; i++)
            {
                double x = r.X + r.Width * i / (n - 1);
                var p = new Point(x, YOf(RtAt(HzAt(x))));
                if (i == 0) lc.BeginFigure(p, false); else lc.LineTo(p);
                ac.LineTo(p);
            }
            ac.LineTo(new Point(r.Right, r.Bottom)); ac.EndFigure(true);
        }
        ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x1E), null, area);
        ctx.DrawGeometry(null, new Pen(NotaPalette.AccentBright, NotaGraph.PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);

        // The handles: one per band (its decay beside it) and the Damping grip on its line.
        for (int b = 0; b < 3; b++)
        {
            bool hot = _drag == b || (_drag < 0 && _hot == b);
            ChamberInk.Handle(ctx, r, HandlePt(b), hot, ChamberInk.Sec(BandRt(b)), HandlePt(b).Y > r.Y + 16);
        }
        var dp = HandlePt(3);
        if (dampHot) ctx.DrawEllipse(NotaPalette.Wash(NotaPalette.Accent, 0x40), null, dp, 8, 8);
        var grip = new Rect(dp.X - 3.5, dp.Y - 3.5, 7, 7);
        ctx.DrawRectangle(dampHot ? NotaPalette.AccentBright : NotaGraph.Ground, new Pen(NotaPalette.AccentBright, 1.4), grip, 1.5, 1.5);
        var dl = NotaGraph.AxisText(Loc.T("device.chamber.damping") + " " + NotaNum.Hz(_damp), dampHot ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
        double dx = dp.X + 8 + dl.Width > r.Right ? dp.X - 8 - dl.Width : dp.X + 8;
        NotaGraph.PlateText(ctx, dl, new Point(dx, dp.Y - dl.Height - 6));

        NotaGraph.Title(ctx, rect, Loc.T("device.chamber.decay_time_by_frequency"));
    }
}

// TAIL EQ on the shared filter graph (FilterGraphBase, as Beat Repeat / Auto Filter draw theirs):
// low cut + low shelf, high shelf + high cut (the same RBJ biquads the engine runs). Handle 0 =
// low cut (X) + low shelf gain (Y); handle 1 = high cut (X) + high shelf gain (Y). A cut dragged
// to its end of the axis is off. Double-click a handle to reset it.
internal sealed class ChamberEqCurve : FilterGraphBase, IValueHandles
{
    private double _lc, _lg = 0.5, _hs = 0.5, _hc = 1;
    private int _drag = -1, _hot = -1;
    private const double Sr = 48000;

    public event Action<int, double, double>? Changed;   // handle, cut (norm), gain (norm)
    public event Action<int>? DragStarted;
    public event Action<int>? DragEnded;
    public event Action<int>? Reset;
    public int DraggingHandle => _drag;

    public ChamberEqCurve() { MinHeight = 60; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }

    protected override (double Lo, double Hi) Span => (20.0, 20000.0);
    protected override (double Top, double Bottom) DbSpan => (18.0, -30.0);
    protected override double ResponseDb(double hz) => Db(hz);
    protected override double CutoffHz
    {
        get
        {
            int h = _drag >= 0 ? _drag : _hot;
            if (h == 0) return _lc > 0.001 ? LowCutHz(_lc) : double.NaN;
            if (h == 1) return _hc < 0.999 ? HighCutHz(_hc) : double.NaN;
            return double.NaN;
        }
    }

    public void Set(double lowCut, double lowGain, double highShelf, double highCut)
    { if (_drag >= 0) return; _lc = lowCut; _lg = lowGain; _hs = highShelf; _hc = highCut; InvalidateVisual(); }

    public static double LowCutHz(double v) => 20 * Math.Pow(100, Math.Clamp(v, 0, 1));
    public static double HighCutHz(double v) => 1000 * Math.Pow(20, Math.Clamp(v, 0, 1));
    private Point Pt(int h) => h == 0 ? new Point(XOf(LowCutHz(_lc)), YOf((_lg - 0.5) * 36)) : new Point(XOf(HighCutHz(_hc)), YOf((_hs - 0.5) * 36));
    private int Nearest(Point p) => Dist(p, Pt(0)) <= Dist(p, Pt(1)) ? 0 : 1;
    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // Typed entry (specs/035): handle 0 = low cut + low shelf, 1 = high cut + high shelf (the nearer).
    public int HandleAt(Point p) => Nearest(p);
    public Rect HandleRect(int handle) => ValueEntry.Near(Pt(handle), Bounds.Size);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        int h = Nearest(p);
        if (e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) { Reset?.Invoke(h); e.Handled = true; return; }
        _drag = h; Dragging = true;
        DragStarted?.Invoke(h); e.Pointer.Capture(this); Apply(p); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag >= 0) { Apply(p); return; }
        int h = Nearest(p);
        if (h != _hot) { _hot = h; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hot = -1; InvalidateVisual(); }
    private void Apply(Point p)
    {
        var r = PlotRect;
        double hz = HzAt(p.X);
        double db = Math.Clamp(-(p.Y - YOf(0)) / Math.Max(1, YOf(-18) - YOf(0)) * 18, -18, 18);
        double gn = Math.Clamp(0.5 + db / 36, 0, 1);
        if (_drag == 0) { _lc = p.X <= r.X + 3 ? 0 : Math.Clamp(Math.Log(hz / 20) / Math.Log(100), 0, 1); _lg = gn; Changed?.Invoke(0, _lc, _lg); }
        else { _hc = p.X >= r.Right - 3 ? 1 : Math.Clamp(Math.Log(hz / 1000) / Math.Log(20), 0, 1); _hs = gn; Changed?.Invoke(1, _hc, _hs); }
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag >= 0) { int d = _drag; _drag = -1; Dragging = false; InvalidateVisual(); e.Pointer.Capture(null); DragEnded?.Invoke(d); } }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { if (_drag >= 0) { int d = _drag; _drag = -1; Dragging = false; InvalidateVisual(); DragEnded?.Invoke(d); } base.OnPointerCaptureLost(e); }

    // |H| (dB) of the four stages at f.
    private double Db(double f)
    {
        double lc = LowCutHz(_lc), hc = HighCutHz(_hc), lg = (_lg - 0.5) * 36, hg = (_hs - 0.5) * 36;
        double db = 0;
        if (_lc > 0.001) db += Mag(Biquad.Hp(lc), f);
        if (Math.Abs(lg) > 0.05) db += Mag(Biquad.LowShelf(Math.Max(250, lc * 2), lg), f);
        if (Math.Abs(hg) > 0.05) db += Mag(Biquad.HighShelf(Math.Min(4000, hc * 0.5), hg), f);
        if (_hc < 0.999) db += Mag(Biquad.Lp(hc), f);
        return db;
    }
    private static double Mag((double b0, double b1, double b2, double a0, double a1, double a2) c, double f)
    {
        double w = 2 * Math.PI * Math.Min(f, Sr * 0.49) / Sr, cw = Math.Cos(w), sw = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = c.b0 + c.b1 * cw + c.b2 * c2, ni = -(c.b1 * sw + c.b2 * s2);
        double dr = c.a0 + c.a1 * cw + c.a2 * c2, di = -(c.a1 * sw + c.a2 * s2);
        return 10 * Math.Log10(Math.Max(1e-30, nr * nr + ni * ni) / Math.Max(1e-30, dr * dr + di * di));
    }
    private static class Biquad
    {
        public static (double, double, double, double, double, double) Hp(double f) { double w = 2 * Math.PI * f / Sr, c = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071); return ((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al); }
        public static (double, double, double, double, double, double) Lp(double f) { double w = 2 * Math.PI * f / Sr, c = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071); return ((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al); }
        public static (double, double, double, double, double, double) LowShelf(double f, double db)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / Sr, c = Math.Cos(w), al = Math.Sin(w) / 2 * Math.Sqrt(2), sa = 2 * Math.Sqrt(A) * al;
            return (A * ((A + 1) - (A - 1) * c + sa), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sa), (A + 1) + (A - 1) * c + sa, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sa);
        }
        public static (double, double, double, double, double, double) HighShelf(double f, double db)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / Sr, c = Math.Cos(w), al = Math.Sin(w) / 2 * Math.Sqrt(2), sa = 2 * Math.Sqrt(A) * al;
            return (A * ((A + 1) + (A - 1) * c + sa), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sa), (A + 1) - (A - 1) * c + sa, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sa);
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var r = PlotRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        DrawBack(ctx);
        DrawCurve(ctx, Db);
        double fc = CutoffHz;
        DrawFront(ctx, double.IsFinite(fc) ? YOf(Db(fc)) : double.NaN);
        bool saved = Dragging;
        string[] names = { Loc.T("device.chamber.low"), Loc.T("device.chamber.high") };
        for (int h = 0; h < 2; h++)
        {
            Dragging = _drag == h;
            var at = Pt(h);
            at = new Point(Math.Clamp(at.X, r.X + 5, r.Right - 5), at.Y);
            DrawHandle(ctx, at);
            bool hot = _drag == h || (_drag < 0 && _hot == h);
            var ft = NotaGraph.AxisText(names[h], hot ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
            double tx = h == 0 ? at.X + 7 : at.X - 7 - ft.Width;
            ctx.DrawText(ft, new Point(Math.Clamp(tx, r.X + 2, r.Right - ft.Width - 2), at.Y - ft.Height - 3));
        }
        Dragging = saved;
    }
}

// THE SIGNAL FLOW: what Chamber does to the sound, left to right. The input splits into the dry
// path (along the bottom) and the two engines — side by side (Parallel) or the convolution
// feeding the algorithm (Serial) — whose blend runs through the tail EQ (where EQ position puts
// it), the stereo stage (Width, bass mono) and ducking, meets the dry at the mix and leaves at
// the output. Each block names its live value; an engine that is off is dashed. A click on an
// engine turns it on or off; a click on the EQ moves it to the next position.
internal sealed class ChamberFlowView : Control
{
    public readonly record struct State(bool ConvOn, bool AlgoOn, bool Serial, double Blend, int EqPos, bool EqFlat,
        bool WetOnly, string Dry, double Mix, string Width, string Mono, double DuckAmountDb, double DuckNowDb, string Out, bool Freeze);

    private State _s = new(true, true, false, 0.5, 1, true, false, "0.0", 0.35, "100 %", "off", 0, 0, "0.0 dB", false);
    private Rect _conv, _algo, _eq;
    private int _hot;   // 0 none · 1 conv · 2 algo · 3 EQ

    public event Action? ConvClicked;
    public event Action? AlgoClicked;
    public event Action? EqClicked;

    public ChamberFlowView() { ClipToBounds = true; MinHeight = 60; }

    public void Set(State s) { if (s == _s) return; _s = s; InvalidateVisual(); }

    private int HitAt(Point p) => _conv.Contains(p) ? 1 : _algo.Contains(p) ? 2 : _eq.Contains(p) ? 3 : 0;
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int h = HitAt(e.GetPosition(this));
        if (h == _hot) return;
        _hot = h;
        Cursor = h != 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hot != 0) { _hot = 0; InvalidateVisual(); } }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        switch (HitAt(e.GetPosition(this)))
        {
            case 1: ConvClicked?.Invoke(); e.Handled = true; break;
            case 2: AlgoClicked?.Invoke(); e.Handled = true; break;
            case 3: EqClicked?.Invoke(); e.Handled = true; break;
        }
    }

    private static readonly IPen Wire = new Pen(NotaPalette.TextTertiary, 1.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen WireOff = new Pen(NotaPalette.Wash(NotaPalette.TextTertiary, 0x60), 1, lineCap: PenLineCap.Round) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 200 || h < 70) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        NotaGraph.Title(ctx, rect, Loc.T("device.chamber.signal_flow"));
        var mode = NotaGraph.AxisText(_s.Serial ? Loc.T("device.chamber.serial_convolution_feeds_the_algorithm") : Loc.T("device.chamber.parallel_both_engines_hear_the_input"), NotaPalette.TextSecondary);
        ctx.DrawText(mode, new Point(w - 8 - mode.Width, 5));

        double top = NotaGraph.PlotTop + 4, bot = h - 8;
        double yDry = bot - 6;
        double yConv = top + (yDry - top) * 0.16, yAlgo = top + (yDry - top) * 0.66;
        double yMain = (yConv + yAlgo) / 2;
        const double bh = 28, engW = 96, blkW = 60, eqW = 40, sumR = 7;

        // Lay the main line out: fixed widths, the spare width shared by the gaps.
        bool eqIn = _s.EqPos == 0, eqTail = _s.EqPos == 1, eqOut = _s.EqPos == 2;
        var inLbl = NotaGraph.AxisText(Loc.T("device.chamber.in"), NotaPalette.TextSecondary);
        var outLbl = NotaGraph.AxisText(Loc.T("device.chamber.out"), NotaPalette.TextSecondary);
        var parts = new List<double> { inLbl.Width + 4 };
        if (eqIn) parts.Add(eqW);
        parts.Add(engW); parts.Add(0);              // engines, the blend node
        if (eqTail) parts.Add(eqW);
        parts.Add(blkW); parts.Add(blkW);            // stereo, ducking
        parts.Add(2 * sumR);                         // the mix node
        if (eqOut) parts.Add(eqW);
        parts.Add(outLbl.Width + 4);
        double fixedW = 0; foreach (var p in parts) fixedW += p;
        double gap = Math.Max(8, (w - 16 - fixedW) / (parts.Count - 1));
        double x = 8;
        int k = 0;
        double Next() { double at = x; x += parts[k++] + gap; return at; }

        // In, and the dry split right after it.
        double xIn = Next();
        ctx.DrawText(inLbl, new Point(xIn, yMain - inLbl.Height / 2));
        double xSplitDry = xIn + inLbl.Width + 4 + Math.Min(6, gap / 2);
        double cur = xIn + inLbl.Width + 4;

        Rect Block(double bx, double cy, double bw, string name, string value, bool on, bool hot, bool click)
        {
            var b = new Rect(bx, cy - bh / 2, bw, bh);
            if (on) ctx.DrawRectangle(NotaPalette.SurfaceCard, new Pen(hot ? NotaPalette.AccentBright : NotaPalette.BorderDefault, 1), b, 4, 4);
            else ctx.DrawRectangle(null, new Pen(hot ? NotaPalette.AccentBright : NotaPalette.TextDisabled, 1) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) }, b, 4, 4);
            var n = new NotaFormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.SansMedium, NotaGraph.TitleSize,
                on ? NotaPalette.TextPrimary : NotaPalette.TextDisabled, NotaTextSize.InCard) { MaxTextWidth = bw - 6, Trimming = TextTrimming.CharacterEllipsis };
            var v = NotaGraph.AxisText(value, on ? (click && hot ? NotaPalette.AccentBright : NotaPalette.TextSecondary) : NotaPalette.TextDisabled);
            double th = n.Height + v.Height;
            ctx.DrawText(n, new Point(bx + (bw - n.Width) / 2, cy - th / 2));
            ctx.DrawText(v, new Point(bx + (bw - v.Width) / 2, cy - th / 2 + n.Height));
            return b;
        }
        string eqVal = _s.EqFlat ? Loc.T("device.chamber.flat") : Loc.T("device.chamber.shaping");
        _eq = default;

        if (eqIn)
        {
            double ex = Next();
            ctx.DrawLine(Wire, new Point(cur, yMain), new Point(ex, yMain));
            _eq = Block(ex, yMain, eqW, "EQ", eqVal, true, _hot == 3, true);
            cur = ex + eqW;
        }

        // The engines.
        double xe = Next();
        double xSplit = xe - Math.Min(12, gap * 0.6);
        ctx.DrawLine(Wire, new Point(cur, yMain), new Point(xSplit, yMain));
        string convShare = NotaNum.Pct(1 - _s.Blend), algoShare = NotaNum.Pct(_s.Blend);
        // Input → Convolution; Input → Algorithm (Parallel) or Convolution → Algorithm (Serial).
        ctx.DrawGeometry(null, _s.ConvOn ? Wire : WireOff, Elbow(new Point(xSplit, yMain), new Point(xe, yConv)));
        if (!_s.Serial) ctx.DrawGeometry(null, _s.AlgoOn ? Wire : WireOff, Elbow(new Point(xSplit, yMain), new Point(xe, yAlgo)));
        _conv = Block(xe, yConv, engW, Loc.T("device.chamber.convolution"), _s.ConvOn ? convShare : Loc.T("device.chamber.off"), _s.ConvOn, _hot == 1, true);
        _algo = Block(xe, yAlgo, engW, Loc.T("device.chamber.algorithm"), !_s.AlgoOn ? Loc.T("device.chamber.off") : _s.Freeze ? algoShare + " · " + Loc.T("device.chamber.frozen") : algoShare, _s.AlgoOn, _hot == 2, true);
        if (_s.Serial)
        {
            double cx = xe + engW / 2;
            var pen = _s.ConvOn && _s.AlgoOn ? Wire : WireOff;
            ctx.DrawLine(pen, new Point(cx, _conv.Bottom), new Point(cx, _algo.Top - 1));
            Arrow(ctx, new Point(cx, _algo.Top - 1), pen.Brush ?? NotaPalette.TextTertiary, down: true);
        }

        // The blend node.
        double xBlend = Next();
        ctx.DrawGeometry(null, _s.ConvOn ? Wire : WireOff, Elbow(new Point(xe + engW, yConv), new Point(xBlend, yMain), toRight: false));
        ctx.DrawGeometry(null, _s.AlgoOn ? Wire : WireOff, Elbow(new Point(xe + engW, yAlgo), new Point(xBlend, yMain), toRight: false));
        ctx.DrawEllipse(NotaPalette.TextTertiary, null, new Point(xBlend, yMain), 2.2, 2.2);
        cur = xBlend;

        if (eqTail)
        {
            double ex = Next();
            ctx.DrawLine(Wire, new Point(cur, yMain), new Point(ex, yMain));
            _eq = Block(ex, yMain, eqW, "EQ", eqVal, true, _hot == 3, true);
            cur = ex + eqW;
        }
        double xs = Next();
        ctx.DrawLine(Wire, new Point(cur, yMain), new Point(xs, yMain));
        Block(xs, yMain, blkW, Loc.T("device.chamber.width"), _s.Width, true, false, false);
        if (_s.Mono != "off")
        {
            var mf = NotaGraph.AxisText(Loc.T("device.chamber.mono_below") + " " + _s.Mono, NotaPalette.TextTertiary);
            ctx.DrawText(mf, new Point(xs + (blkW - mf.Width) / 2, yMain + bh / 2 + 2));
        }
        cur = xs + blkW;
        double xd = Next();
        ctx.DrawLine(Wire, new Point(cur, yMain), new Point(xd, yMain));
        bool ducking = _s.DuckAmountDb > 0.05;
        string duckVal = !ducking ? Loc.T("device.chamber.off") : _s.DuckNowDb > 0.05 ? NotaNum.Unit(-_s.DuckNowDb, "0.0", "dB") : NotaNum.Unit(-_s.DuckAmountDb, "0", "dB") + " " + Loc.T("device.chamber.max");
        Block(xd, yMain, blkW, Loc.T("device.chamber.ducking"), duckVal, ducking, false, false);
        if (ducking && _s.DuckNowDb > 0.05)
            ctx.DrawRectangle(NotaPalette.AccentBright, null, new Rect(xd + 4, yMain + bh / 2 - 3, (blkW - 8) * Math.Clamp(_s.DuckNowDb / Math.Max(1, _s.DuckAmountDb), 0, 1), 2), 1, 1);
        cur = xd + blkW;

        // The mix: the wet in from the left, the dry up from below.
        double xm = Next() + sumR;
        ctx.DrawLine(Wire, new Point(cur, yMain), new Point(xm - sumR, yMain));
        ctx.DrawEllipse(NotaPalette.SurfaceCard, new Pen(NotaPalette.BorderDefault, 1), new Point(xm, yMain), sumR, sumR);
        ctx.DrawLine(new Pen(NotaPalette.TextSecondary, 1.2), new Point(xm - 3.5, yMain), new Point(xm + 3.5, yMain));
        ctx.DrawLine(new Pen(NotaPalette.TextSecondary, 1.2), new Point(xm, yMain - 3.5), new Point(xm, yMain + 3.5));
        var mixT = NotaGraph.AxisText(Loc.T("device.chamber.wet") + " " + (_s.WetOnly ? "100 %" : NotaNum.Pct(_s.Mix)), NotaPalette.TextSecondary);
        NotaGraph.PlateText(ctx, mixT, new Point(Math.Min(xm - mixT.Width / 2, w - 8 - mixT.Width), yMain - sumR - mixT.Height - 2));

        // The dry path along the bottom.
        var dryPen = _s.WetOnly ? WireOff : Wire;
        var dry = new StreamGeometry();
        using (var g = dry.Open())
        {
            g.BeginFigure(new Point(xSplitDry, yMain), false);
            g.LineTo(new Point(xSplitDry, yDry)); g.LineTo(new Point(xm, yDry)); g.LineTo(new Point(xm, yMain + sumR));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, dryPen, dry);
        ctx.DrawEllipse(NotaPalette.TextTertiary, null, new Point(xSplitDry, yMain), 2.2, 2.2);
        var dryT = NotaGraph.AxisText(_s.WetOnly ? Loc.T("device.chamber.dry_off_wet_only") : Loc.T("device.chamber.dry") + " " + _s.Dry,
            _s.WetOnly ? NotaPalette.TextDisabled : NotaPalette.TextSecondary);
        NotaGraph.PlateText(ctx, dryT, new Point((xSplitDry + xm - dryT.Width) / 2, yDry - dryT.Height / 2));
        cur = xm + sumR;

        if (eqOut)
        {
            double ex = Next();
            ctx.DrawLine(Wire, new Point(cur, yMain), new Point(ex, yMain));
            _eq = Block(ex, yMain, eqW, "EQ", eqVal, true, _hot == 3, true);
            cur = ex + eqW;
        }
        double xo = Next();
        ctx.DrawLine(Wire, new Point(cur, yMain), new Point(xo - 3, yMain));
        Arrow(ctx, new Point(xo - 2, yMain), NotaPalette.TextTertiary, down: false);
        ctx.DrawText(outLbl, new Point(xo + 2, yMain - outLbl.Height / 2));
        var outV = NotaGraph.AxisText(_s.Out, NotaPalette.TextTertiary);
        ctx.DrawText(outV, new Point(Math.Min(xo + 2, w - 6 - outV.Width), yMain + outLbl.Height / 2 + 1));
    }

    // A wire from a to b that runs across, then up / down, then across (right-angled, rounded).
    private static Geometry Elbow(Point a, Point b, bool toRight = true)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(a, false);
            double xm = toRight ? a.X + Math.Min(6, (b.X - a.X) / 2) : b.X - Math.Min(6, (b.X - a.X) / 2);
            if (Math.Abs(b.Y - a.Y) < 0.5) c.LineTo(b);
            else
            {
                double rr = Math.Min(Math.Min(4, Math.Abs(b.Y - a.Y) / 2), Math.Min(Math.Abs(xm - a.X), Math.Abs(b.X - xm))), sy = Math.Sign(b.Y - a.Y);
                c.LineTo(new Point(xm - rr, a.Y));
                c.QuadraticBezierTo(new Point(xm, a.Y), new Point(xm, a.Y + sy * rr));
                c.LineTo(new Point(xm, b.Y - sy * rr));
                c.QuadraticBezierTo(new Point(xm, b.Y), new Point(xm + rr, b.Y));
                c.LineTo(b);
            }
            c.EndFigure(false);
        }
        return g;
    }
    private static void Arrow(DrawingContext ctx, Point tip, IBrush ink, bool down)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            if (down) { c.BeginFigure(tip, true); c.LineTo(new Point(tip.X - 3, tip.Y - 4)); c.LineTo(new Point(tip.X + 3, tip.Y - 4)); }
            else { c.BeginFigure(tip, true); c.LineTo(new Point(tip.X - 4, tip.Y - 3)); c.LineTo(new Point(tip.X - 4, tip.Y + 3)); }
            c.EndFigure(true);
        }
        ctx.DrawGeometry(ink, null, g);
    }
}
