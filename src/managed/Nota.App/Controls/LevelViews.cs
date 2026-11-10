// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Level views (device kind 18) — the pictures on the Level card:
//   LvHistoryView — the last 8 s of loudness: the input (dim ink), the output (neutral ink) and the target
//                   (dashed grey, with a ±1 LU band) that moves with a reference track. A dot
//                   marks the output now. Drag up / down for Target (not while a reference
//                   drives it); double-click resets it.
//   LvMeter       — a 3px channel with an optional grey tick (the target, the ceiling, 0 dB).
//   LvLegend      — the graph legend drawn with NotaGraph's samples, for the panel bar.
// LevelMath holds the telemetry layout (AutoGain.h) and the unit mappings.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class LevelMath
{
    // Params (AutoGain.h).
    public const int Target = 0, Scale = 1, Auto = 2, Trim = 3, Response = 4, WindowP = 5, MaxGain = 6, Safe = 7, Ceiling = 8, Gain = 9;
    // Scope (AutoGain.h).
    public const int S_InLufs = 0, S_OutLufs = 1, S_InMom = 2, S_Target = 3, S_Applied = 4, S_TruePeak = 5, S_Corr = 6,
        S_ScLufs = 7, S_Desired = 8, S_Measured = 9, S_OutMeasured = 10, S_OutMom = 11, S_Level = 12, S_LimGr = 13,
        S_Clamp = 14, S_Silent = 15, S_Latency = 16, S_SampleRate = 17, S_Primed = 18, S_Delta = 19, S_LimHold = 20, kScope = 21;

    public static readonly string[] ScaleNames = { "Mom", "Short", "Integ" };
    public static readonly string[] ScaleLong = { "momentary · 400\u2009ms", "short-term · 3\u2009s", "integrated · gated" };

    public const double LTop = 0, LBot = -40;   // the graph and meter range, LUFS

    public static double TargetLufs(double v) => -36 + v * 36;
    public static double TargetNorm(double lufs) => Math.Clamp((lufs + 36) / 36, 0, 1);
    public static double TrimDb(double v) => (v - 0.5) * 24;
    public static double WindowS(double v) => 0.4 * Math.Pow(25, Math.Clamp(v, 0, 1));
    public static double WindowNorm(double s) => Math.Log(Math.Clamp(s, 0.4, 10) / 0.4) / Math.Log(25);
    public static double MaxGainDb(double v) => v * 24;
    public static double CeilingDb(double v) => -6 + v * 6;
    public static double GainDb(double v) => (v - 0.5) * 48;
    public static double GainNorm(double db) => Math.Clamp(0.5 + db / 48, 0, 1);
    public static int ScaleIndex(double v) => Math.Clamp((int)Math.Round(v * 2), 0, 2);

    public static string Lufs(double v) => v <= -119 ? "—" : NotaNum.F($"{v:0.0;−0.0;0.0}");
    public static string Sgn(double v) => NotaNum.F($"{v:+0.0;−0.0;0.0}");
}

internal sealed class LvHistoryView : Control
{
    private const int N = 480;   // ~8 s at the 60 Hz UI tick
    private readonly float[] _in = new float[N], _out = new float[N], _tar = new float[N];
    private int _w, _count;
    private bool _on = true, _dragging, _canDrag = true;
    private string _lock = "", _targetLabel = "";
    private bool _locked;
    private double _y0, _v0;

    /// <summary>Target (0..1) at the start of a drag.</summary>
    public Func<double>? Value { get; set; }
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double>? Changed;
    public event Action? ResetRequested;
    public bool Dragging => _dragging;

    public LvHistoryView()
    {
        ClipToBounds = true; MinHeight = 60;
        for (int i = 0; i < N; i++) { _in[i] = -120; _out[i] = -120; _tar[i] = -14; }
    }

    /// <summary>Push one tick of momentary loudness (in, out) and the target in effect.</summary>
    public void Push(float inLufs, float outLufs, float target)
    {
        _in[_w] = inLufs; _out[_w] = outLufs; _tar[_w] = target;
        _w = (_w + 1) % N; if (_count < N) _count++;
    }

    public void SetState(bool on, bool locked, string lockText, string targetLabel, bool canDrag)
    {
        _on = on; _locked = locked; _lock = lockText; _targetLabel = targetLabel; _canDrag = canDrag;
        Cursor = canDrag ? new Cursor(StandardCursorType.SizeNorthSouth) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_canDrag || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { ResetRequested?.Invoke(); e.Handled = true; return; }
        _v0 = Value?.Invoke() ?? 0.611; _y0 = e.GetPosition(this).Y; _dragging = true;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        // The line follows the pointer: the graph spans 40 LU, the target 36 LU of 0..1.
        double h = Math.Max(40, Bounds.Height - 20);
        double dLu = (_y0 - e.GetPosition(this).Y) / h * (LevelMath.LTop - LevelMath.LBot) * (fine ? 0.2 : 1);
        Changed?.Invoke(Math.Clamp(_v0 + dLu / 36, 0, 1));
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false; InvalidateVisual(); e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_dragging) { _dragging = false; InvalidateVisual(); GestureEnd?.Invoke(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double top = 1, bot = h - 1;
        double Y(double lufs) => top + Math.Clamp((LevelMath.LTop - lufs) / (LevelMath.LTop - LevelMath.LBot), 0, 1) * (bot - top);
        double X(int i) => 1 + (double)i / (N - 1) * (w - 2);
        int Idx(int i) => (_w + i) % N;   // oldest → newest

        foreach (double lu in new[] { -10.0, -20, -30 }) ctx.DrawLine(NotaGraph.GridPen, new Point(0, Y(lu)), new Point(w, Y(lu)));
        for (int k = 1; k < 4; k++) ctx.DrawLine(NotaGraph.GridPen, new Point(w * k / 4, 0), new Point(w * k / 4, h));

        float tNow = _tar[(_w + N - 1) % N];
        // The ±1 LU band around the target now.
        ctx.FillRectangle(NotaPalette.Wash(NotaPalette.VizInk, 0x10), new Rect(0, Y(tNow + 1), w, Math.Max(1, Y(tNow - 1) - Y(tNow + 1))));

        int first = N - _count;
        // Input (ink), under the target and the output.
        Trace(ctx, _in, first, new Pen(NotaPalette.TextTertiary, 1));
        // Target, dashed grey (steps with a reference).
        var tp = new Pen(NotaPalette.TextSecondary, 1, new DashStyle(new double[] { 4, 3 }, 0));
        Trace(ctx, _tar, Math.Max(0, first), tp, allowSilent: true);
        // Output, neutral ink.
        Trace(ctx, _out, first, NotaGraph.SecondaryPen(_on ? NotaPalette.VizInk : NotaPalette.TextTertiary, NotaGraph.PrimaryWidth));

        NotaGraph.Title(ctx, frame, "Loudness");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, _lock, _locked ? NotaPalette.TextSecondary : NotaPalette.TextTertiary);
        foreach (double lu in new[] { -10.0, -20, -30 })
        {
            var ft = NotaGraph.AxisText(NotaNum.F($"{lu:0;−0}"));
            double y = Y(lu) + 1;
            if (Math.Abs(Y(tNow) - Y(lu)) > 9) ctx.DrawText(ft, new Point(4, y));
        }
        if (_targetLabel.Length > 0)
        {
            var ft = NotaGraph.AxisText(_targetLabel, NotaPalette.TextSecondary);
            double y = Y(tNow) - ft.Height - 1;
            if (y < 12) y = Y(tNow) + 2;
            ctx.DrawText(ft, new Point(w - 14 - ft.Width, y));
        }
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "−8 s");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "now");

        float oNow = _out[(_w + N - 1) % N];
        if (_on && _count > 0 && oNow > -119) NotaGraph.Node(ctx, new Point(w - 4, Y(oNow)), false, NotaPalette.VizInk);

        void Trace(DrawingContext dc, float[] src, int from, IPen pen, bool allowSilent = false)
        {
            var geo = new StreamGeometry();
            bool any = false;
            using (var g = geo.Open())
            {
                bool open = false;
                for (int i = Math.Max(0, from); i < N; i++)
                {
                    float v = src[Idx(i)];
                    if (!allowSilent && v <= -119) { if (open) { g.EndFigure(false); open = false; } continue; }
                    var p = new Point(X(i), Y(v));
                    if (!open) { g.BeginFigure(p, false); open = true; any = true; } else g.LineTo(p);
                }
                if (open) g.EndFigure(false);
            }
            if (any) dc.DrawGeometry(null, pen, geo);
        }
    }
}

internal sealed class LvMeter : Control
{
    private double _l, _r, _mark = double.NaN;
    private IBrush _ink = NotaPalette.VizInk;

    public LvMeter() { Height = 10; }

    /// <summary>Fill from <paramref name="from"/> to <paramref name="to"/> (0..1), a grey marker at
    /// <paramref name="marker"/> (NaN hides it).</summary>
    public void Set(double from, double to, IBrush ink, double marker)
    {
        _l = Math.Clamp(Math.Min(from, to), 0, 1); _r = Math.Clamp(Math.Max(from, to), 0, 1); _ink = ink; _mark = marker;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        // A 3px channel on the well, the level in its ink, and the target (or ceiling) as a
        // 2px grey tick standing over it.
        double ty = Math.Round((h - 3) / 2);
        using (ctx.PushClip(new RoundedRect(new Rect(0, ty, w, 3), NotaRadius.FineValue)))
        {
            ctx.FillRectangle(NotaPalette.Well, new Rect(0, ty, w, 3));
            if (_r > _l) ctx.FillRectangle(_ink, new Rect(_l * w, ty, (_r - _l) * w, 3));
        }
        if (!double.IsNaN(_mark))
            ctx.FillRectangle(NotaPalette.TextSecondary, new Rect(Math.Round(Math.Clamp(_mark, 0, 1) * (w - 2)), 0, 2, h), 1);
    }
}

internal sealed class LvLegend : Control
{
    public LvLegend() { Height = 14; Width = 150; }

    public override void Render(DrawingContext ctx)
        => NotaGraph.Legend(ctx, Bounds.Width, 0, ("input", NotaPalette.TextTertiary, NotaGraph.Mark.Line),
            ("output", NotaPalette.VizInk, NotaGraph.Mark.Line), ("target", NotaPalette.TextSecondary, NotaGraph.Mark.Dashed));
}
