// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Delay body (device kind 3), 700 × 260: an always-visible LOOP
// column (Feedback · Spread), a centre tabbed panel whose graph fills it (Time: the live
// echoes of your audio, on the beat grid, with where the repeats will land; Loop · Wow: each
// repeat's response through the loop filter, and the echo spectrum), a right panel with the
// controls of the open centre tab (or Output) and a status strip. The repeats graph is a
// control too (drag its handles for the cuts). Every control is a device param, so automation /
// MIDI learn / presets / A-B / persistence come for free; Tap tempo and Clear loop are the
// two actions that are not (the first writes Time L/R, the second wipes the loop).
// FullBleed — the shared shell draws the header (name · preset · badge · bypass).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class DelayDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Delay.h) ───────────────────────────────
    private const int SyncMode = 0, TimeL = 1, TimeR = 2, DivL = 3, DivR = 4, LinkLR = 5, Feedback = 6,
        Spread = 7, PingPong = 8, WowRate = 9, WowDepth = 10, Freeze = 11, DryWet = 12, Output = 13,
        DryLevel = 14, Diffuse = 15, LowCut = 16, HighCut = 17, TapeMode = 18, FadeChange = 19,
        WidthP = 20, BassMono = 21, WetOnly = 22, LatencyComp = 23;
    // Scope telemetry layout (Delay::S_*), then the level history.
    private const int S_WetPk = 0, S_OutL = 1, S_OutR = 2, S_Cpu = 3, S_SampleRate = 4, S_Bpm = 5,
        S_DiffuseSmp = 6, S_Frozen = 7, S_MsL = 8, S_MsR = 9, S_Latency = 10, S_Beat = 11, S_Playing = 12, kScope = 13;
    private const int ScopeLen = kScope + SpaceDraw.Floats;   // then the level history (dsp::LevelHistory)
    private const int A_ClearLoop = 0;

    private static readonly string[] DivNames = { "1/16", "1/8T", "1/8", "1/8.", "1/4T", "1/4", "1/4.", "1/2" };
    // The same divisions on the panel's segments, which have room for the denominator only (the row reads "L 1/").
    private static readonly string[] DivShort = { "16", "8T", "8", "8.", "4T", "4", "4.", "2" };
    private static readonly double[] DivBeats = { 0.25, 1.0 / 3, 0.5, 0.75, 2.0 / 3, 1.0, 1.5, 2.0 };
    private const double SyncWindowBeats = 8.0;   // the repeat window spans two 4/4 bars
    private static readonly double[] WindowRanges = { 0.25, 0.4, 0.6, 0.8, 1.2, 1.6, 2.4, 3.2, 4.8, 6.4, 9.6 };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "Delay";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, 0f, 1f));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        var scope = new float[ScopeLen];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        // ---- formatters -------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Pct(double v) => NotaNum.F($"{v * 100:0} %");
        static string Ms(double ms) => ms >= 100 ? NotaNum.F($"{ms:0} ms") : NotaNum.F($"{ms:0.0} ms");
        static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0} k") : NotaNum.F($"{hz:0} Hz");
        static string ShortDb(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:0.0;−0.0;0.0}");
        static string Db(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:+0.0;−0.0;0.0} dB");
        static string Secs(double s) => s >= 1 ? NotaNum.F($"{s:0.0} s") : NotaNum.F($"{s * 1000:0} ms");
        static double OutDb(double v) => v <= 0.0005 ? -100 : 20 * Math.Log10(v * 2);
        static double DryDb(double v) { double g = 2 * v * v; return g <= 1e-5 ? -100 : 20 * Math.Log10(g); }
        static double LowHz(double v) => Exp(v, 20, 2000);
        static double HighHz(double v) => Exp(v, 200, 20000);
        string LowCutF(double v) => v <= 0.001 ? "off" : Hz(LowHz(v));
        string HighCutF(double v) => v >= 0.999 ? "off" : Hz(HighHz(v));
        // "filter 80 Hz — 6 kHz", or "filter off" when neither cut is on.
        string FilterF() => P(LowCut) <= 0.001 && P(HighCut) >= 0.999 ? "filter off" : NotaNum.F($"filter {LowCutF(P(LowCut))} — {HighCutF(P(HighCut))}");
        static string Window(double s) => s >= 1 ? NotaNum.F($"{s:0.#} s") : NotaNum.F($"{s * 1000:0} ms");
        static double NiceWindow(double seconds)
        {
            foreach (double r in WindowRanges) if (r >= seconds) return r;
            return WindowRanges[^1];
        }

        // The engine's tempo only reaches the scope once audio has rolled, so fall back to 120.
        double Bpm() => Sc(S_Bpm) > 1 ? Sc(S_Bpm) : 120;
        double BeatMs() => 60000.0 / Bpm();
        // The set times, as the knobs read them. The engine's own figures (smoothed, and
        // shortened by the diffuser compensation) reach the card through S_MsL / S_MsR.
        double MsL() => On(SyncMode) ? DivBeats[Sel(DivL, 8)] * BeatMs() : P(TimeL) * 2000;
        double MsR() => On(LinkLR) ? MsL() : On(SyncMode) ? DivBeats[Sel(DivR, 8)] * BeatMs() : P(TimeR) * 2000;
        double SpreadMs() => P(Spread) * 50;

        // ======================================================================
        // The live graphs — one per centre tab
        // ======================================================================
        var start = DateTime.UtcNow;
        int centreTab = 0;
        double windowSec = 0;
        var echo = new DelayEchoView();
        ToolTip.SetTip(echo, "Your echoes — the input (grey) and the repeats before the mix, now at the right; on the beat grid in Sync while the transport rolls. The dots mark where the repeats of the latest hit land and how loud the feedback leaves them.");
        var repeats = new DelayRepeatsView();
        repeats.Changed += (hd, v) => Raw(hd == 0 ? LowCut : HighCut, (float)v);
        repeats.GestureBegin += hd => Begin(hd == 0 ? LowCut : HighCut);
        repeats.GestureEnd += hd => End(hd == 0 ? LowCut : HighCut);
        repeats.Reset += hd => { Begin(hd == 0 ? LowCut : HighCut); Raw(hd == 0 ? LowCut : HighCut, hd == 0 ? 0f : 1f); End(hd == 0 ? LowCut : HighCut); };
        ToolTip.SetTip(repeats, "The repeats — each pass round the loop goes through the low cut, the high cut (and the tape's head loss) again and loses the feedback, so each curve sits lower and narrower. Drag the handles for the cuts; double-click one to open it. Grey: the echoes' live spectrum.");
        MidiLearn.Bind(repeats, MidiTarget.DeviceParam(track, di, LowCut), "Low Cut");
        void SyncTaps()
        {
            double msl = MsL(), msr = (On(LinkLR) ? msl : MsR()) + SpreadMs();
            windowSec = Math.Min(SpaceDraw.HistorySec, On(SyncMode)
                ? SyncWindowBeats * BeatMs() / 1000.0
                : NiceWindow(Math.Max(msl, msr) * 3.2 / 1000.0));
            bool has = scN >= ScopeLen;
            if (centreTab == 0)
                echo.Set(scope, kScope, has, windowSec, msl, msr, P(Feedback), On(Freeze), On(SyncMode) && Sc(S_Playing) > 0.5, Sc(S_Beat), Bpm());
            else
                repeats.Set(On(Freeze) ? 1 : Math.Min(0.98, P(Feedback)), LowHz(P(LowCut)), HighHz(P(HighCut)), P(LowCut) > 0.001f, P(HighCut) < 0.999f,
                    On(TapeMode), On(Freeze), scope, kScope + 2 * SpaceDraw.Points, has, Sc(S_SampleRate));
        }
        readouts.Add(SyncTaps);

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 5) => new()
        { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };

        // On/off switch bound to a param (> 0.5 = on).
        Control Toggle(int p, string label, Func<bool>? dim = null)
        {
            var wrap = Switch(label, () => On(p), () => SetP(p, On(p) ? 0f : 1f), out var sync, dim);
            readouts.Add(sync);
            MidiLearn.Bind(wrap, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return wrap;
        }

        // Segmented chips over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names, bool fill = false, Action? onPick = null)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); onPick?.Invoke(); }, out var sync, fill: fill, padX: fill ? 2 : 6, fontSize: 8);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // A latching chip over a toggle param (Link · Ping · Fade · Freeze).
        Border Latch(int p, string text, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 18, Padding = new Thickness(5, 0), CornerRadius = NotaRadius.Field, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSubtle : Sunken;
                b.BorderBrush = on ? NotaPalette.BorderBrass : BorderDef;
                tb.Foreground = on ? NotaPalette.AccentHover : TextTertiary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); Hi(); SyncTaps(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(Hi); Hi();
            return b;
        }

        // A small text button (an action, not a parameter).
        Border Btn(string text, Action click, string? tip = null)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, Background = Raised, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); e.Handled = true; };
            if (tip != null) ToolTip.SetTip(b, tip);
            return b;
        }

        // Horizontal bar slider over a param.
        Control Bar(int p, Func<double, string> fmt, Func<bool>? dim = null, double valW = 30, bool teal = false)
        {
            var row = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, (float)v); SyncTaps(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), dim: dim, valueWidth: valW, modulation: teal);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return row;
        }
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 52, Func<bool>? dim = null, double valW = 34, bool teal = false)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(Bar(p, fmt, dim, valW, teal), 1));
            return g;
        }

        // ======================================================================
        // LEFT — Loop column (Feedback · Spread)
        // ======================================================================
        Control LoopFader(int p, string label, IBrush ink, IBrush handle, Func<string> value, string tip)
        {
            var f = new DelayFader { Ink = ink, Handle = handle, Width = 18, VerticalAlignment = VerticalAlignment.Stretch, Default = engine.DeviceParamDefault(track, di, p) };
            f.Changed += v => Raw(p, (float)v);
            f.GestureBegin += () => Begin(p);
            f.GestureEnd += () => End(p);
            MidiLearn.Bind(f, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            ToolTip.SetTip(f, tip);
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center;
            var val = Mono("", 7, TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Center;
            readouts.Add(() =>
            {
                f.Set(P(p));
                val.Text = value();
                bool hot = On(Freeze) && p == Feedback;
                val.Foreground = hot ? AccentBright : P(p) > 0.9f ? NotaPalette.Warning : TextPrimary;   // Freeze is live; > 90 % runs away
                lb.Foreground = hot ? AccentBright : TextTertiary;
            });
            return new DockPanel { Width = 44, Children = { Docked(val, Dock.Bottom), Docked(lb, Dock.Bottom), f } };
        }
        var loopTitle = Caps("LOOP"); loopTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 2) };
        faders.Children.Add(LoopFader(Feedback, "FEEDBACK", NotaPalette.VizInk, NotaPalette.VizInk,
            () => On(Freeze) ? "Hold" : Pct(P(Feedback)),
            "Feedback — how much of each repeat goes round again; Freeze pins it at 100 %. Double-click resets."));
        faders.Children.Add(Col(LoopFader(Spread, "SPREAD", DeviceCardKit.ModInk, DeviceCardKit.ModInkBright,
            () => NotaNum.F($"{P(Spread) * 50:0} ms"),
            "Spread — pushes the right channel later, up to 50 ms. Double-click resets."), 1));
        var loopCol = new RuleSection
        {
            Width = 96, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(loopTitle, Dock.Top), faders } },
        };
        DockPanel.SetDock(loopCol, Dock.Left);

        // ======================================================================
        // RIGHT — the controls of the centre tab (Time · Loop), and Output
        // ======================================================================
        double lastTap = 0; int tapCount = 0; double tapSum = 0;
        void TapTempo()
        {
            double now = (DateTime.UtcNow - start).TotalMilliseconds;
            double gap = now - lastTap;
            lastTap = now;
            if (gap < 80 || gap > 2000) { tapCount = 0; tapSum = 0; return; }   // a new series
            tapSum += gap; tapCount++;
            double ms = tapSum / tapCount;
            SetP(SyncMode, 0f);                       // tapping a time means free time
            SetP(TimeL, (float)(ms / 2000.0));
            if (!On(LinkLR)) SetP(TimeR, (float)(ms / 2000.0));
            SyncTaps();
        }

        // Rows spread evenly down the panel; Auto rows (rules) keep their height.
        Control Rows(params Control[] rows)
        {
            var grid = new Grid { RowSpacing = 2 };
            for (int r = 0; r < rows.Length; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(rows[r] is Border { Height: 1 } ? GridLength.Auto : GridLength.Star));
                grid.Children.Add(GRow(rows[r], r));
            }
            return new Border { Padding = new Thickness(8, 4, 8, 5), Child = grid };
        }
        static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 1) };
        static Grid Pair(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(a); g.Children.Add(Col(b, 1));
            return g;
        }

        Control TimePanel()
        {
            // L and R change with the mode: divisions in Sync, free times in ms.
            Control Channel(string ch, int divP, int timeP, Func<bool>? dim)
            {
                var host = new ContentControl();
                Control? div = null, ms = null;
                Control Div()
                {
                    var g = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*"), ColumnSpacing = 3, VerticalAlignment = VerticalAlignment.Center };
                    var lbl = Caps(ch + " 1/"); g.Children.Add(lbl);
                    // Eight divisions: past the almanac's four-choice limit for segments — an
                    // accepted exception, see DESIGN.md § Accepted exceptions.
                    var seg = DeviceCardKit.Segments(DivShort, () => Sel(divP, 8), iv => { SetP(divP, iv / 7f); SyncTaps(); }, out var sync, fill: true, padX: 1, fontSize: 8, dim: dim);
                    readouts.Add(sync);
                    MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, divP), engine.DeviceParamName(track, di, divP));
                    g.Children.Add(Col(seg, 1));
                    if (dim is not null) readouts.Add(() => lbl.Foreground = dim() ? TextDisabled : TextTertiary);
                    return g;
                }
                Control MsRow() => SliderRow(ch + " TIME", timeP, v => dim?.Invoke() == true ? Ms(P(TimeL) * 2000) : Ms(v * 2000), 52, dim, 40);
                // Both rows are built up front: building one inside a readout would add readouts
                // while they run, which stops the pass (and every readout after it).
                div = Div(); ms = MsRow();
                host.Content = On(SyncMode) ? div : ms;
                readouts.Add(() => { var want = On(SyncMode) ? div : ms; if (!ReferenceEquals(host.Content, want)) host.Content = want; });
                return host;
            }
            var modeSeg = Seg(SyncMode, new[] { "ms", "Sync" }, onPick: SyncTaps);
            var latches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = {
                Latch(LinkLR, "Link", "Link L/R — the right channel follows the left's time"),
                Latch(PingPong, "Ping", "Ping-pong — each repeat crosses to the other channel"),
                Latch(FadeChange, "Fade", "Fade on change — crossfade to a new delay time instead of gliding to it (which bends the pitch, tape style)") } };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(modeSeg); head.Children.Add(Col(latches, 1));
            var hint = Mono("", 7, TextTertiary); hint.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => hint.Text = On(SyncMode)
                ? NotaNum.F($"L {DivNames[Sel(DivL, 8)]} · R {DivNames[On(LinkLR) ? Sel(DivL, 8) : Sel(DivR, 8)]}")
                : On(LinkLR) ? "channels linked" : "channels independent");
            var btns = Pair(Btn("Tap tempo", TapTempo, "Tap four times to set the delay time by ear (switches to free ms)"),
                Btn("Clear loop", () => engine.DeviceAction(track, di, A_ClearLoop, 0, 0), "Empty the delay buffer — silences what is still circulating"));
            return Rows(
                head,
                hint,
                Channel("L", DivL, TimeL, null),
                Channel("R", DivR, TimeR, () => On(LinkLR)),
                SliderRow("FEEDBACK", Feedback, v => On(Freeze) ? "HOLD" : Pct(v)),
                SliderRow("SPREAD", Spread, v => NotaNum.F($"{v * 50:0} ms"), 52, null, 40),
                Rule(),
                btns);
        }

        Control LoopPanel()
        {
            var fr = Toggle(Freeze, "Freeze");
            ToolTip.SetTip(fr, "Freeze — hold the loop forever; the input is muted and the repeats stop decaying");
            var tp = Toggle(TapeMode, "Tape");
            ToolTip.SetTip(tp, "Tape mode — head loss and saturation in the loop, and a deeper wow");
            return Rows(
                SliderRow("FEEDBACK", Feedback, v => On(Freeze) ? "HOLD" : Pct(v)),
                SliderRow("DIFFUSE", Diffuse, Pct),
                SliderRow("LOW CUT", LowCut, LowCutF, 52, null, 40),
                SliderRow("HIGH CUT", HighCut, HighCutF, 52, null, 40),
                Rule(),
                SliderRow("WOW RATE", WowRate, v => { double hz = Exp(v, 0.05, 8); return hz >= 1 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz"); }, 52, null, 40, teal: true),
                SliderRow("WOW", WowDepth, Pct, teal: true),
                Pair(fr, tp));
        }

        // The L / R output level meters (after Out) on the 0 / −12 / −48 dB scale of the level bars.
        Control OutMeters(Func<double> l, Func<double> r)
        {
            var barL = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            var barR = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            readouts.Add(() => { barL.SetLinear(l()); barR.SetLinear(r()); });
            // The bars run −48 … +3 dBFS, so the marks sit at 0, 36/51 and 48/51 of the width.
            var scale = new Canvas { Height = 9 };
            var marks = new (double At, TextBlock Tb)[] { (0, Mono("−48", 7, TextDisabled)), (36 / 51.0, Mono("−12", 7, TextDisabled)), (48 / 51.0, Mono("0", 7, TextDisabled)) };
            foreach (var (_, tb) in marks) scale.Children.Add(tb);
            void Place()
            {
                double w = scale.Bounds.Width;
                foreach (var (at, tb) in marks)
                {
                    double tw = tb.DesiredSize.Width;
                    Canvas.SetLeft(tb, Math.Clamp(at * w - (at > 0 ? tw / 2 : 0), 0, Math.Max(0, w - tw)));
                }
            }
            scale.PropertyChanged += (_, e) => { if (e.Property == Visual.BoundsProperty) Place(); };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 6, RowSpacing = 2, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps("L")); g.Children.Add(Col(barL, 1));
            g.Children.Add(GRow(Caps("R"), 1)); g.Children.Add(GRow(Col(barR, 1), 1));
            g.Children.Add(GRow(Col(scale, 1), 2));
            ToolTip.SetTip(g, "Output level, left and right, after Out");
            return g;
        }

        Control OutputPanel()
        {
            var wetBar = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            var wetDb = Mono("", 8, TextPrimary); wetDb.Width = 26; wetDb.TextAlignment = TextAlignment.Right;
            readouts.Add(() =>
            {
                double pk = Sc(S_WetPk);
                wetBar.SetLinear(pk);
                double db = pk > 1e-5 ? 20 * Math.Log10(pk) : -100;
                wetDb.Text = ShortDb(db);
                wetDb.Foreground = db > -6 ? NotaPalette.Warning : TextPrimary;
            });
            var wet = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            wet.Children.Add(Caps("WET")); wet.Children.Add(Col(wetBar, 1)); wet.Children.Add(Col(wetDb, 2));
            var wetOnly = Toggle(WetOnly, "Wet only");
            ToolTip.SetTip(wetOnly, "Wet only — repeats with no dry, for a return / send track");
            var comp = Toggle(LatencyComp, "Lat. comp");
            ToolTip.SetTip(comp, "Latency comp — the diffuser lengthens the loop by a fixed group delay; on, the read pointer is pulled back by exactly that so each repeat lands on the set time.");
            return Rows(
                SliderRow("MIX", DryWet, Pct, 52, () => On(WetOnly)),
                SliderRow("DRY", DryLevel, v => Db(DryDb(v)), 52, () => On(WetOnly), 40),
                SliderRow("OUT", Output, v => Db(OutDb(v)), 52, null, 40),
                SliderRow("WIDTH", WidthP, v => NotaNum.F($"{v * 200:0} %"), 52, null, 40),
                SliderRow("MONO", BassMono, v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0} Hz"), 52, null, 40),
                Rule(),
                Pair(wetOnly, comp),
                wet,
                OutMeters(() => Sc(S_OutL), () => Sc(S_OutR)));
        }

        // ======================================================================
        // Tab frames: the centre graph, and the panel that follows it
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[2];
        var panelBodies = new Control?[2];
        Control? outputBody = null;
        int rightTab = 0;
        string[] panelTitles = { "Time", "Loop · Wow" };
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            if (centreTab == 0)
            {
                extras.Text = On(SyncMode)
                    ? NotaNum.F($"{DivNames[Sel(DivL, 8)]} = {Ms(MsL())} · {(On(PingPong) ? "ping-pong" : "parallel")}")
                    : NotaNum.F($"L {Ms(MsL())} · R {Ms(On(LinkLR) ? MsL() : MsR())} · window {Window(windowSec)}");
                extras.Foreground = TextTertiary;
            }
            else if (On(Freeze))
            {
                extras.Text = NotaNum.F($"input muted · loop {Secs(MsL() / 1000)}");
                extras.Foreground = AccentBright;
            }
            else
            {
                // The wow's pitch swing: a delay wobbling by D samples at r Hz bends the pitch by up to 2πrD / sr.
                double sr = Sc(S_SampleRate) > 1000 ? Sc(S_SampleRate) : 48000;
                double depth = P(WowDepth) * (On(TapeMode) ? 12 : 6) * sr / 44100, rate = Exp(P(WowRate), 0.05, 8);
                double cents = 1200 * Math.Log2(1 + 2 * Math.PI * rate * depth / sr);
                extras.Text = NotaNum.F($"{FilterF()}{(cents >= 0.5 ? NotaNum.F($" · wow ±{cents:0} cents") : "")}");
                extras.Foreground = TextTertiary;
            }
        });
        Control CentreBody(int t) => centreBodies[t] ??= new Border { Margin = new Thickness(8, 6), Child = t == 1 ? repeats : echo };
        Control PanelBody(int t) => panelBodies[t] ??= t == 1 ? LoopPanel() : TimePanel();
        Control RightBody(int t) => t == 1 ? outputBody ??= OutputPanel() : PanelBody(centreTab);

        TextBlock[]? rightLabels = null;
        var centre = TabFrame(new[] { "Time", "Loop · Wow" }, centreHost, CentreBody, false, t =>
        {
            centreTab = t;
            rightLabels![0].Text = panelTitles[t];
            if (rightTab == 0) rightHost.Content = PanelBody(t);
            Refresh();
        }, extras, out _);
        var rightFrame = TabFrame(new[] { panelTitles[0], "Output" }, rightHost, RightBody, true, t => { rightTab = t; Refresh(); }, null, out rightLabels);
        var right = new RuleSection { Width = 186, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new RuleSection { Margin = new Thickness(5, 0), Child = centre };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        string StatusText()
        {
            if (On(Freeze))
                return NotaNum.F($"Freeze: loop held, input muted · {FilterF()}");
            string time = On(SyncMode)
                ? NotaNum.F($"Sync {DivNames[Sel(DivL, 8)]}{(On(LinkLR) ? "" : NotaNum.F($" / {DivNames[Sel(DivR, 8)]}"))}")
                : NotaNum.F($"ms · L {MsL():0} / R {(On(LinkLR) ? MsL() : MsR()):0}");
            string mode = On(PingPong) ? " · ping-pong" : "";
            string tape = On(TapeMode) ? " · tape" : "";
            // How many repeats stay above −60 dB — the honest answer to "how long does it ring".
            float fb = Math.Clamp(P(Feedback), 0.001f, 0.98f);
            int audible = (int)Math.Floor(Math.Log(0.001) / Math.Log(fb));
            return NotaNum.F($"{time}{mode}{tape} · feedback {Pct(P(Feedback))} · {audible} audible repeats");
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            statusLeft.Foreground = On(Freeze) ? AccentBright : TextSecondary;
            double sr = Sc(S_SampleRate), diff = Sc(S_DiffuseSmp);
            string comp = P(Diffuse) <= 0.001f || diff < 1 ? "" : On(LatencyComp)
                ? NotaNum.F($" · diffuser −{diff:0}\u2009smp compensated")
                : NotaNum.F($" · diffuser +{diff:0}\u2009smp");
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · latency {Sc(S_Latency):0} smp{comp} · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { loopCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceCard, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, ScopeLen);
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl, out TextBlock[] labels)
        {
            int sel = 0;
            var btns = new Border[tabs.Length];
            labels = new TextBlock[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? Card2 : Brushes.Transparent;
                    btns[i].BorderBrush = on ? Brass : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!;
                    tb.Foreground = on ? NotaPalette.TextPrimary : TextTertiary;
                    tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var bar = centered ? (Avalonia.Controls.Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border
                {
                    Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                };
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
                btns[i] = b;
                labels[i] = (TextBlock)b.Child;
                bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi();
            host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }
    }
}
