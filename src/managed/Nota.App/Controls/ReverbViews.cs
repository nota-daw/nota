// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The custom-drawn view of the Nota Reverb card: the decay in dB. On a dB axis the tail is a
// straight line, so how long the reverb rings reads at a glance: it starts at the pre-delay
// (0 dB) and reaches −60 dB at the decay time (RT60). Two labelled handles set them — Pre
// (drag sideways) and Decay (drag the −60 dB point sideways); a press elsewhere takes the
// nearer one, double-click or ⌥-click resets it. The early reflections are ticks right after
// the pre-delay, and a fainter line shows the highs (8 kHz) dying sooner under HF damp. Under
// the set line runs your audio after its latest hit (#377): the input as a grey wash and the
// measured tail as a grey line, each against its own peak, so a tail that rings longer or
// shorter than the setting shows at once. Frozen, the tail is a held line at 0 dB under an
// accent frame. It draws what the card hands it and owns no engine state.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class ReverbTailView : Control, IValueHandles
{
    private const double DbFloor = -72, PreMaxMs = 200, RtLo = 0.2, RtHi = 12;
    private static readonly double[] Windows = { 0.5, 1, 2, 3, 4, 6, 8, 12, 16 };
    private const double Left = 30, AxisRow = 14, TopPad = 26;

    private double _rt60 = 2, _rtHigh = 1.5, _preMs = 20, _win = 4;
    private double[] _erMs = Array.Empty<double>();
    private float[] _erGain = Array.Empty<float>();
    private bool _freeze;
    // The latest hit of your audio: (seconds after the hit, dB against its own peak); empty = none.
    private readonly List<(double Sec, double Db)> _inLive = new(), _tailLive = new();
    private int _drag, _hover;        // 0 none, 1 decay, 2 pre-delay

    public event Action<int, double>? Changed;   // target (0 decay / 1 pre-delay), new normalised value
    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;
    public event Action<int>? Reset;

    public bool Dragging => _drag != 0;

    private static readonly IPen LivePen = new Pen(NotaPalette.TextSecondary, 1.2, lineJoin: PenLineJoin.Round);

    public ReverbTailView()
    {
        ClipToBounds = true;
        MinHeight = 60;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    /// <summary>Feed the window: RT60 and the highs' RT60 in seconds, freeze, the pre-delay in
    /// ms, and the early reflections (ms after the pre-delay, level 0..1; empty = off).</summary>
    public void Set(double rt60, double rtHigh, bool freeze, double preMs, double[] erMs, float[] erGain)
    {
        _rt60 = Math.Max(0.05, rt60); _rtHigh = Math.Max(0.02, rtHigh); _freeze = freeze; _preMs = preMs;
        _erMs = erMs; _erGain = erGain;
        // The time axis fits the tail with some room, and holds still while a handle is dragged.
        if (_drag == 0)
        {
            double need = (_preMs / 1000 + (_freeze ? 2 : _rt60)) * 1.15;
            _win = Windows[^1];
            foreach (var wv in Windows) if (wv >= need) { _win = wv; break; }
        }
        InvalidateVisual();
    }

    /// <summary>Feed the level history (at <paramref name="off"/> in the scope; <paramref name="has"/>
    /// false = none): the input and the tail from the latest hit of the input on.</summary>
    public void SetLive(float[] scope, int off, bool has)
    {
        bool had = _tailLive.Count > 0;
        _inLive.Clear(); _tailLive.Clear();
        int hit = has && scope.Length >= off + SpaceDraw.Floats ? SpaceDraw.LatestHit(scope, off) : -1;
        if (hit >= 0)
        {
            var input = SpaceDraw.Since(scope, off, hit);
            var tail = SpaceDraw.Since(scope, off + SpaceDraw.Points, hit);
            double inPk = -120, tailPk = -120;
            foreach (var (_, db) in input) inPk = Math.Max(inPk, db);
            foreach (var (_, db) in tail) tailPk = Math.Max(tailPk, db);
            if (tailPk > -70)
            {
                foreach (var (t, db) in input) _inLive.Add((t, db - inPk));
                foreach (var (t, db) in tail) _tailLive.Add((t, db - tailPk));
            }
        }
        if (had || _tailLive.Count > 0) InvalidateVisual();
    }

    // ---- geometry ------------------------------------------------------------------
    private Rect Plot => new(Left, TopPad, Math.Max(1, Bounds.Width - Left - 10), Math.Max(1, Bounds.Height - TopPad - AxisRow));
    private double XOfSec(double sec) { var r = Plot; return r.X + Math.Clamp(sec / _win, 0, 1) * r.Width; }
    private double SecAt(double x) { var r = Plot; return Math.Clamp((x - r.X) / r.Width, 0, 1) * _win; }
    private double YOf(double db) { var r = Plot; return r.Y + Math.Clamp(db / DbFloor, 0, 1) * r.Height; }
    private Point PrePt => new(XOfSec(_preMs / 1000), YOf(0));
    private Point DecayPt => new(XOfSec(_preMs / 1000 + _rt60), YOf(-60));

    private int Nearest(Point p)
        => Math.Abs(p.X - PrePt.X) + Math.Abs(p.Y - PrePt.Y) * 0.5 <= Math.Abs(p.X - DecayPt.X) + Math.Abs(p.Y - DecayPt.Y) * 0.5 ? 2 : 1;

    // Typed entry (specs/035): handle 0 = the decay, 1 = the pre-delay.
    public int HandleAt(Point p) => Nearest(p) == 2 ? 1 : 0;
    public Rect HandleRect(int handle) => ValueEntry.Near(handle == 1 ? PrePt : DecayPt, Bounds.Size);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _freeze) return;
        var p = e.GetPosition(this);
        int target = Nearest(p);
        if (e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0) { Reset?.Invoke(target - 1); e.Handled = true; return; }
        _drag = target;
        GestureBegin?.Invoke(target - 1);
        e.Pointer.Capture(this);
        Apply(p);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag != 0) { Apply(p); return; }
        int h = Nearest(p);
        if (h != _hover) { _hover = h; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hover != 0) { _hover = 0; InvalidateVisual(); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == 0) return;
        GestureEnd?.Invoke(_drag - 1); _drag = 0; e.Pointer.Capture(null); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_drag != 0) { GestureEnd?.Invoke(_drag - 1); _drag = 0; InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    private void Apply(Point p)
    {
        double sec = SecAt(p.X);
        if (_drag == 2)
            Changed?.Invoke(1, Math.Clamp(sec * 1000 / PreMaxMs, 0, 1));
        else
        {
            double rt = Math.Clamp(sec - _preMs / 1000, RtLo, RtHi);
            Changed?.Invoke(0, Math.Log(rt / RtLo) / Math.Log(RtHi / RtLo));
        }
    }

    private static FormattedText Text(string t, IBrush ink, double size = NotaGraph.AxisSize)
        => new NotaFormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans, size, ink, NotaTextSize.InCard);
    private static string SecF(double s) => s >= 1 ? NotaNum.F($"{s:0.##} s") : NotaNum.F($"{s * 1000:0} ms");

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

        // dB lines (0 · −20 · −40 · −60, the last dashed: where RT60 is measured) and the time grid.
        foreach (var db in new[] { 0.0, -20, -40, -60 })
        {
            double y = YOf(db);
            var pen = db == -60 ? new Pen(NotaPalette.TextTertiary, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) } : NotaGraph.GridPen;
            ctx.DrawLine(pen, new Point(r.X, y), new Point(r.Right, y));
            var ft = Text(db == 0 ? "0 dB" : NotaNum.F($"{db:0}"), NotaGraph.AxisInk);
            ctx.DrawText(ft, new Point(Left - 4 - ft.Width, y - ft.Height / 2));
        }
        double step = _win <= 1 ? 0.25 : _win <= 3 ? 0.5 : _win <= 8 ? 1 : 2;
        for (double t = step; t < _win - 1e-6; t += step)
        {
            double x = XOfSec(t);
            ctx.DrawLine(NotaGraph.GridPen, new Point(x, r.Y), new Point(x, r.Bottom));
            var ft = Text(SecF(t), NotaGraph.AxisInk);
            ctx.DrawText(ft, new Point(x - ft.Width / 2, r.Bottom + 1));
        }
        ctx.DrawText(Text("0", NotaGraph.AxisInk), new Point(r.X - 2, r.Bottom + 1));

        double pre = _preMs / 1000;
        double preX = XOfSec(pre);

        if (_freeze)
        {
            ctx.DrawLine(new Pen(NotaPalette.AccentBright, 1.8), new Point(preX, YOf(0)), new Point(r.Right, YOf(0)));
            ctx.DrawText(new NotaFormattedText(Loc.T("device.reverb.tail_held_no_decay"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                NotaFonts.SansMedium, NotaGraph.TitleSize, NotaPalette.AccentBright, this), new Point(6, 3));
            return;
        }

        // Your audio after its latest hit, under the set line: the input as a wash, the tail as a line.
        if (_tailLive.Count > 1)
        {
            var wash = new StreamGeometry();
            using (var g = wash.Open())
            {
                g.BeginFigure(new Point(XOfSec(0), r.Bottom), true);
                foreach (var (t, db) in _inLive) { if (t > _win) break; g.LineTo(new Point(XOfSec(t), YOf(db))); }
                g.LineTo(new Point(XOfSec(Math.Min(_inLive[^1].Sec, _win)), r.Bottom));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.TextTertiary, 0x40), null, wash);
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                bool first = true;
                foreach (var (t, db) in _tailLive)
                {
                    if (t > _win) break;
                    var at = new Point(XOfSec(t), YOf(Math.Max(db, DbFloor)));
                    if (first) { g.BeginFigure(at, false); first = false; } else g.LineTo(at);
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, LivePen, line);
        }

        // The highs: a fainter line that falls sooner (HF damp).
        var hiPen = new Pen(NotaPalette.Wash(NotaPalette.VizInk, 0x66), 1.2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        var hiEnd = new Point(XOfSec(pre + _rtHigh * -DbFloor / 60), YOf(DbFloor));
        ctx.DrawLine(hiPen, new Point(preX, YOf(0)), hiEnd.X > r.Right ? new Point(r.Right, YOf(-60 * (_win - pre) / _rtHigh)) : hiEnd);

        // The tail: 0 dB at the pre-delay, −60 dB at the decay, on to the floor.
        double endSec = pre + _rt60 * -DbFloor / 60;
        var end = endSec <= _win ? new Point(XOfSec(endSec), YOf(DbFloor)) : new Point(r.Right, YOf(-60 * (_win - pre) / _rt60));
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(preX, r.Bottom), true);
            g.LineTo(new Point(preX, YOf(0))); g.LineTo(end); g.LineTo(new Point(end.X, r.Bottom));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x14), null, fill);
        ctx.DrawLine(new Pen(NotaPalette.VizInk, 1.8, lineCap: PenLineCap.Round), new Point(preX, YOf(0)), end);
        // The early reflections: short ticks hanging under the start of the tail, longer the
        // louder they are (they come within ~60 ms, so on a seconds axis they sit close).
        int n = Math.Min(_erMs.Length, _erGain.Length);
        var erPen = new Pen(NotaPalette.Wash(NotaPalette.TextSecondary, 0xB0), 1);
        double tickMax = Math.Max(6, YOf(-20) - YOf(0));
        for (int k = 0; k < n; k++)
        {
            double x = XOfSec(pre + _erMs[k] / 1000);
            if (x > r.Right) break;
            double y0 = YOf(0) + 5;
            ctx.DrawLine(erPen, new Point(x, y0), new Point(x, y0 + tickMax * Math.Clamp(_erGain[k], 0, 1)));
        }

        // The handles and their labels.
        void Handle(Point at, bool hot, string label, bool above)
        {
            if (hot) ctx.DrawEllipse(NotaPalette.Wash(NotaPalette.Accent, 0x40), null, at, 8, 8);
            ctx.DrawEllipse(hot ? NotaPalette.AccentBright : NotaGraph.Ground, new Pen(NotaPalette.AccentBright, 1.6), at, 3.8, 3.8);
            var ft = Text(label, hot ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
            double x = Math.Clamp(at.X + 7, r.X, r.Right - ft.Width);
            if (at.X + 7 + ft.Width > r.Right) x = at.X - 7 - ft.Width;
            ctx.DrawText(ft, new Point(x, above ? at.Y - ft.Height - 2 : at.Y + 2));
        }
        Handle(PrePt, _drag == 2 || (_drag == 0 && _hover == 2), NotaNum.F($"Pre {_preMs:0} ms"), true);
        var dp = DecayPt;
        if (dp.X <= r.Right + 0.5)
            Handle(dp, _drag == 1 || (_drag == 0 && _hover == 1), "Decay " + SecF(_rt60), true);

        NotaGraph.Title(ctx, rect, "Decay");
        var hi = Text(Loc.T("device.reverb.highs_8_khz"), NotaGraph.AxisInk);
        double lx = w - 10 - hi.Width;
        ctx.DrawText(hi, new Point(lx, 4));
        ctx.DrawLine(hiPen, new Point(lx - 18, 4 + hi.Height / 2), new Point(lx - 4, 4 + hi.Height / 2));
        if (_tailLive.Count > 1)
        {
            var yt = Text(Loc.T("device.reverb.your_tail"), NotaGraph.AxisInk);
            double yx = lx - 26 - yt.Width;
            ctx.DrawText(yt, new Point(yx, 4));
            ctx.DrawLine(LivePen, new Point(yx - 18, 4 + yt.Height / 2), new Point(yx - 4, 4 + yt.Height / 2));
        }
    }
}
