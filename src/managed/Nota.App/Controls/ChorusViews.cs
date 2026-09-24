// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chorus views (device kind 25) — the pictures on the Chorus card:
//   ChorusVoicesView — each voice over two LFO cycles: its delay τ (0 … 2 × the mode's centre)
//                      or its instantaneous detune in cents, with a running head and the dot
//                      where each voice is now. Drag up / down for Amount; double-click resets it.
//   ChorusFieldView  — the stereo field of the voices: where each one sits (Width) and how far
//                      it is detuned right now.
// ChorusMath mirrors Chorus.h so the picture matches the sound.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class ChorusMath
{
    public static readonly string[] Modes = { "Classic", "Ensemble", "Vibrato" };
    // Per mode: the centre delay and the swing at Amount 100 %, ms (Chorus::kBaseMs / kDepMs).
    public static readonly double[] BaseMs = { 8, 12, 3 };
    public static readonly double[] DepMs = { 5, 6, 2.5 };
    // Sync divisions, slowest → fastest (Chorus::kDivBeats / kDivNames).
    public static readonly double[] DivBeats = { 16, 8, 4, 2, 1, 2.0 / 3, 0.5, 1.0 / 3, 0.25 };
    public static readonly string[] DivNames = { "4/1", "2/1", "1/1", "1/2", "1/4", "1/4T", "1/8", "1/8T", "1/16" };
    public const double FbMax = 0.9;

    public static int ModeIndex(double v) => Math.Clamp((int)Math.Round(v * 2), 0, 2);
    public static int DivIndex(double v) => Math.Clamp((int)Math.Round(v * (DivBeats.Length - 1)), 0, DivBeats.Length - 1);
    public static double DivNorm(int i) => (double)Math.Clamp(i, 0, DivBeats.Length - 1) / (DivBeats.Length - 1);
    public static double FreeHz(double v) => 0.02 * Math.Pow(400, Math.Clamp(v, 0, 1));
    public static double HpHz(double v) => v < 0.01 ? 0 : 20 * Math.Pow(100, Math.Clamp(v, 0, 1));
    public static double Fb(double v) => (Math.Clamp(v, 0, 1) - 0.5) * 2 * FbMax;
    public static double FbNorm(double fb) => Math.Clamp(0.5 + fb / (2 * FbMax), 0, 1);
    public static double WidthPct(double v) => Math.Clamp(v, 0, 1) * 200;
    public static double Dep(int mode, double amount) => DepMs[mode] * Math.Clamp(amount, 0, 1);

    /// <summary>The voices of a mode: their LFO phase (cycles) and side (−1 left … +1 right).</summary>
    public static (string Name, double Phase, double Side)[] Voices(int mode, double offsetCycles) => mode == 1
        ? new[] { ("V1", 0.0, -1.0), ("V2", 1 / 3.0, 0.0), ("V3", 2 / 3.0, 1.0) }
        : new[] { ("L", 0.0, -1.0), ("R", offsetCycles, 1.0) };

    public static double Tau(int mode, double dep, double p) => BaseMs[mode] + dep * Math.Sin(2 * Math.PI * p);

    /// <summary>The detune at phase <paramref name="p"/>: the read head's speed is 1 − dτ/dt.</summary>
    public static double Cents(double dep, double rateHz, double p)
    {
        double dt = dep / 1000 * 2 * Math.PI * rateHz * Math.Cos(2 * Math.PI * p);
        return 1200 * Math.Log2(Math.Max(1 - dt, 0.01));
    }
    public static double MaxCents(double dep, double rateHz) => 1200 * Math.Log2(1 + dep / 1000 * 2 * Math.PI * rateHz);
    /// <summary>The cents axis' half-range: a bit beyond the sweep, never under 4 ct.</summary>
    public static double CentsRange(double maxCents) => Math.Max(maxCents * 1.3, 4);

    public static IBrush VoiceInk(int i) => i switch { 0 => NotaPalette.Accent, 1 => NotaPalette.TealBright, _ => NotaPalette.RoseBright };

    public static string Ms(double t) => t >= 10 ? NotaNum.F($"{t:0.0}") : NotaNum.F($"{t:0.00}");
    public static string HzF(double f) => f >= 1000 ? NotaNum.F($"{f / 1000:0.00}k") : NotaNum.F($"{f:0}");
    public static string Ct(double c) => NotaNum.F($"{c:0.0}");
}

internal sealed class ChorusVoicesView : Control
{
    private int _mode;
    private double _dep = 2, _off = 0.5, _rate = 0.8, _head, _periodMs = 1250;
    private bool _on = true, _cents, _drag;
    private double _y0, _a0;

    /// <summary>Amount (normalized 0..1) at the start of a drag.</summary>
    public Func<double>? Value { get; set; }
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double>? Changed;
    public event Action? ResetRequested;

    public ChorusVoicesView() { ClipToBounds = true; MinHeight = 40; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    public void Set(int mode, double dep, double offCycles, double rateHz, double head, double periodMs, bool cents, bool on)
    {
        _mode = mode; _dep = dep; _off = offCycles; _rate = rateHz; _head = head; _periodMs = periodMs; _cents = cents; _on = on;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(); e.Handled = true; return; }
        _a0 = Value?.Invoke() ?? 0.4; _y0 = e.GetPosition(this).Y; _drag = true;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double k = fine ? 0.2 : 1;
        Changed?.Invoke(Math.Clamp(_a0 - (e.GetPosition(this).Y - _y0) * k / Math.Max(60, Bounds.Height), 0, 1));
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        double baseMs = ChorusMath.BaseMs[_mode];
        double cR = ChorusMath.CentsRange(ChorusMath.MaxCents(_dep, _rate));
        double X(double p) => p / 2 * w;
        double YTau(double t) => h * (186 - t / (baseMs * 2) * 172) / 200;
        double YCt(double c) => h * (100 - c / cR * 80) / 200;
        var voices = ChorusMath.Voices(_mode, _off);
        double Val(int v, double p)
        {
            if (_cents) return YCt(_on ? ChorusMath.Cents(_dep, _rate, p + voices[v].Phase) : 0);
            return YTau(_on ? ChorusMath.Tau(_mode, _dep, p + voices[v].Phase) : baseMs);
        }

        double gTop = _cents ? YCt(cR * 0.75) : YTau(baseMs * 2), gBot = _cents ? YCt(-cR * 0.75) : YTau(0), gMid = _cents ? YCt(0) : YTau(baseMs);
        var dash = new Pen(NotaPalette.GridBeat, 1, new DashStyle(new double[] { 2, 3 }, 0));
        ctx.DrawLine(dash, new Point(X(1), 0), new Point(X(1), h));
        ctx.DrawLine(NotaGraph.GridPen, new Point(0, gTop), new Point(w, gTop));
        ctx.DrawLine(NotaGraph.GridPen, new Point(0, gBot), new Point(w, gBot));
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(0, gMid), new Point(w, gMid));

        // The last voice first, so V1 / L reads on top where they cross.
        for (int v = voices.Length - 1; v >= 0; v--)
        {
            var ink = _on ? ChorusMath.VoiceInk(v) : NotaPalette.TextAxis;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                int n = Math.Max(64, (int)w);
                for (int i = 0; i <= n; i++)
                {
                    double p = 2.0 * i / n;
                    var pt = new Point(X(p), Val(v, p));
                    if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(false);
            }
            using (ctx.PushOpacity(v == 0 ? 1 : 0.8)) ctx.DrawGeometry(null, NotaGraph.SecondaryPen(ink, v == 0 ? 1.6 : 1.2), geo);
        }

        void Label(string s, double y)
        {
            var ft = NotaGraph.AxisText(s, size: 6);
            ctx.DrawText(ft, new Point(5, Math.Clamp(y - ft.Height - 1, 1, h - ft.Height - 1)));
        }
        Label(_cents ? "+" + ChorusMath.Ct(cR * 0.75) + " ct" : ChorusMath.Ms(baseMs * 2) + " ms", gTop + 10);
        Label(_cents ? "0 ct" : ChorusMath.Ms(baseMs) + " ms", gMid);
        Label(_cents ? "−" + ChorusMath.Ct(cR * 0.75) + " ct" : "0 ms", gBot);
        string span = "2 × " + (_periodMs >= 1000 ? NotaNum.F($"{_periodMs / 1000:0.0} s") : NotaNum.F($"{_periodMs:0} ms"));
        var br = NotaGraph.AxisText(span, size: 6);
        ctx.DrawText(br, new Point(w - 5 - br.Width, h - 3 - br.Height));

        if (_on)
        {
            double ph = Math.Clamp(_head, 0, 2), hx = X(ph);
            ctx.DrawLine(new Pen(NotaPalette.TextAxis, 1), new Point(hx, 0), new Point(hx, h));
            for (int v = voices.Length - 1; v >= 0; v--)
                ctx.DrawEllipse(ChorusMath.VoiceInk(v), new Pen(NotaGraph.Ground, 2), new Point(hx, Val(v, ph)), 3, 3);
        }
    }
}

internal sealed class ChorusFieldView : Control
{
    private int _mode;
    private double _width = 100, _cR = 4;
    private double[] _cents = new double[3];
    private double _off = 0.5;
    private bool _on = true;

    public ChorusFieldView() { ClipToBounds = true; Height = 34; }

    public void Set(int mode, double offCycles, double widthPct, double[] centsNow, double centsRange, bool on)
    {
        _mode = mode; _off = offCycles; _width = widthPct; _cR = centsRange; _on = on;
        for (int i = 0; i < 3; i++) _cents[i] = i < centsNow.Length ? centsNow[i] : 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        var line = new Pen(NotaPalette.GraphBorder, 1);
        ctx.DrawLine(line, new Point(w * 0.05, h / 2), new Point(w * 0.95, h / 2));
        ctx.DrawLine(line, new Point(w / 2, 4), new Point(w / 2, h - 4));
        double spread = Math.Min(_width / 200, 1);
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1), new Point(w * (0.5 - spread * 0.45), h / 2 - 1), new Point(w * (0.5 + spread * 0.45), h / 2 - 1));

        var l = NotaGraph.AxisText("L", size: 7);
        ctx.DrawText(l, new Point(5, h / 2 - l.Height / 2 + 5));
        var r = NotaGraph.AxisText("R", size: 7);
        ctx.DrawText(r, new Point(w - 5 - r.Width, h / 2 - r.Height / 2 + 5));
        ctx.DrawText(NotaGraph.AxisText("STEREO · DETUNE", size: 6), new Point(14, 2));
        string wt = NotaNum.F($"width {_width:0} %") + (_width > 100.5 ? NotaNum.F($" · side +{20 * Math.Log10(_width / 100):0.0} dB") : "");
        var wtt = NotaGraph.AxisText(wt, size: 6);
        ctx.DrawText(wtt, new Point(w - 14 - wtt.Width, 2));

        var voices = ChorusMath.Voices(_mode, _off);
        for (int v = voices.Length - 1; v >= 0; v--)
        {
            double x = w * (0.5 + voices[v].Side * spread * 0.45);
            double y = h * (0.5 - (_on ? _cents[v] : 0) / _cR * 0.38);
            ctx.DrawEllipse(_on ? ChorusMath.VoiceInk(v) : NotaPalette.TextAxis, null, new Point(x, y), 3, 3);
        }
    }
}
