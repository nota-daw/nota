// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Delay body (device kind 3), a build of the "Nota Delay"
// mockup (700 × 260) on the Chamber frame: an always-visible LOOP column (Feedback ·
// Spread), a centre tabbed panel (Time / Loop · Wow) over the live repeat window, a right
// tabbed panel (Levels / Output) and a status strip. The repeat window and the feedback
// filter are controls, not decoration. Every control is a device param, so automation /
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
    // Scope telemetry layout (Delay::S_*).
    private const int S_WetPk = 0, S_OutL = 1, S_OutR = 2, S_Cpu = 3, S_SampleRate = 4, S_Bpm = 5,
        S_DiffuseSmp = 6, S_Frozen = 7, S_MsL = 8, S_MsR = 9, S_Latency = 10, kScope = 11;
    private const int A_ClearLoop = 0;

    private static readonly string[] DivNames = { "1/16", "1/8T", "1/8", "1/8.", "1/4T", "1/4", "1/4.", "1/2" };
    private static readonly double[] DivBeats = { 0.25, 1.0 / 3, 0.5, 0.75, 2.0 / 3, 1.0, 1.5, 2.0 };
    private const double SyncWindowBeats = 8.0;   // the repeat window spans two 4/4 bars
    private static readonly double[] WindowRanges = { 0.25, 0.4, 0.6, 0.8, 1.2, 1.6, 2.4, 3.2, 4.8, 6.4, 9.6 };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "DELAY";

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
        var scope = new float[kScope];
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

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 5) => new()
        { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 50, IBrush? arc = null, Action? onChange = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); onChange?.Invoke(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(() => { if (knob.Dragging) return; float c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(c); });
            return KnobCell(name, knob, val, cellW);
        }

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
            var seg = DeviceCardKit.Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); onPick?.Invoke(); }, out var sync, fill: fill, padX: fill ? 2 : 6);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // A latching chip over a toggle param (Link · Ping · Fade · Freeze).
        Border Latch(int p, string text, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 18, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSubtle : Sunken;
                b.BorderBrush = on ? NotaPalette.BorderBrass : BorderDef;
                tb.Foreground = on ? NotaPalette.AccentHover : TextTertiary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); Hi(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(Hi); Hi();
            return b;
        }

        // A small text button (an action, not a parameter).
        Border Btn(string text, Action click, string? tip = null)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Badge, Background = Raised, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); e.Handled = true; };
            if (tip != null) ToolTip.SetTip(b, tip);
            return b;
        }

        // Horizontal bar slider over a param.
        Control Bar(int p, Func<double, string> fmt, Func<bool>? dim = null, double valW = 30)
        {
            var row = DeviceCardKit.SliderRow("", () => P(p), v => Raw(p, (float)v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), dim: dim, valueWidth: valW);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return row;
        }
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 52, Func<bool>? dim = null, double valW = 30)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(Bar(p, fmt, dim, valW), 1));
            return g;
        }

        // ======================================================================
        // The repeat window — shared by both centre tabs
        // ======================================================================
        // One view per centre tab — a control has one parent, and both tabs show the window.
        var tapViews = new List<DelayTaps>();
        double windowSec = 0;
        var start = DateTime.UtcNow;
        DelayTaps NewTaps()
        {
            var t = new DelayTaps();
            ToolTip.SetTip(t, "The repeat train — L above the axis, R below. Height is the level left after each pass; the repeat sounding now is lit.");
            tapViews.Add(t);
            return t;
        }
        void SyncTaps()
        {
            if (tapViews.Count == 0) return;
            bool sync = On(SyncMode), link = On(LinkLR), freeze = On(Freeze);
            double msl = MsL(), msr = link ? msl : MsR();
            double fracL, fracR; string title, sub, ruler, win;
            if (sync)
            {
                int dl = Sel(DivL, 8), dr = link ? dl : Sel(DivR, 8);
                windowSec = SyncWindowBeats * BeatMs() / 1000.0;
                fracL = DivBeats[dl] / SyncWindowBeats;
                fracR = (DivBeats[dr] * BeatMs() + SpreadMs()) / (windowSec * 1000.0);
                title = NotaNum.F($"L {DivNames[dl]}");
                sub = NotaNum.F($"R {DivNames[dr]}");
                ruler = "0 · 1 bar · 2 bars";
                win = "";
            }
            else
            {
                windowSec = NiceWindow(Math.Max(msl, msr + SpreadMs()) * 3.2 / 1000.0);
                double winMs = windowSec * 1000.0;
                fracL = msl / winMs; fracR = (msr + SpreadMs()) / winMs;
                title = NotaNum.F($"L {Ms(msl)}");
                sub = NotaNum.F($"R {Ms(msr)}");
                ruler = "";
                win = Window(windowSec);
            }
            if (freeze) { title = "LOOP HELD · no decay"; sub = NotaNum.F($"R {Ms(msr)}"); }
            foreach (var t in tapViews) t.Set(fracL, fracR, P(Feedback), On(PingPong), freeze, title, sub, ruler, win);

            // Light the repeat that is sounding: the card counts them off the wall clock at the
            // repeat period, and only while the wet path is actually making sound.
            int litL = -1, litR = -1;
            if (Sc(S_WetPk) > 5e-4 && msl > 1 && msr > 1)
            {
                double ms = (DateTime.UtcNow - start).TotalMilliseconds;
                int nL = Math.Max(1, (int)(1.0 / Math.Max(fracL, 1e-3)));
                int nR = Math.Max(1, (int)(1.0 / Math.Max(fracR, 1e-3)));
                litL = (int)(ms / msl) % nL; litR = (int)(ms / msr) % nR;
            }
            foreach (var t in tapViews) t.SetLit(litL, litR);
        }
        readouts.Add(SyncTaps);

        // ======================================================================
        // LEFT — LOOP column (Feedback · Spread)
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
                val.Foreground = hot || P(p) > 0.9f ? AccentBright : TextPrimary;
                lb.Foreground = hot ? AccentBright : TextTertiary;
            });
            return new DockPanel { Width = 44, Children = { Docked(val, Dock.Bottom), Docked(lb, Dock.Bottom), f } };
        }
        var loopTitle = Caps("LOOP"); loopTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 2) };
        faders.Children.Add(LoopFader(Feedback, "FEEDBACK", NotaPalette.Accent, NotaPalette.AccentBright,
            () => On(Freeze) ? "HOLD" : Pct(P(Feedback)),
            "Feedback — how much of each repeat goes round again; Freeze pins it at 100 %. Double-click resets."));
        faders.Children.Add(Col(LoopFader(Spread, "SPREAD", NotaPalette.Rose, NotaPalette.RoseBright,
            () => NotaNum.F($"{P(Spread) * 50:0} ms"),
            "Spread — pushes the right channel later, up to 50 ms. Double-click resets."), 1));
        var loopCol = new Border
        {
            Width = 96, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(loopTitle, Dock.Top), faders } },
        };
        DockPanel.SetDock(loopCol, Dock.Left);

        // ======================================================================
        // CENTRE — Time
        // ======================================================================
        Control TimeTab()
        {
            var host = new ContentControl();
            // The bottom half changes with the mode: divisions in Sync, free times in ms.
            Control SyncRows()
            {
                Control LR(string ch, int p, Func<bool>? dim)
                {
                    var g = new Grid { ColumnDefinitions = new ColumnDefinitions("12,*"), ColumnSpacing = 5, Height = 20 };
                    var lbl = Caps(ch); g.Children.Add(lbl);
                    // Eight divisions: past the almanac's four-choice limit for segments — an
                    // accepted exception, see DESIGN.md § Accepted departures.
                    g.Children.Add(Col(Seg(p, DivNames, fill: true, onPick: SyncTaps), 1));
                    if (dim is not null) readouts.Add(() => lbl.Foreground = dim() ? TextDisabled : TextTertiary);
                    return g;
                }
                return new StackPanel { Spacing = 4, Children = { LR("L", DivL, null), LR("R", DivR, () => On(LinkLR)) } };
            }
            Control MsRow()
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,Auto"), Height = 56 };
                g.Children.Add(K(TimeL, "L TIME", v => Ms(v * 2000), 34, 50, null, SyncTaps));
                g.Children.Add(Col(K(TimeR, "R TIME", v => On(LinkLR) ? Ms(P(TimeL) * 2000) : Ms(v * 2000), 34, 50, null, SyncTaps), 1));
                g.Children.Add(Col(K(Feedback, "FEEDBACK", Pct, 34, 50, null, SyncTaps), 2));
                g.Children.Add(Col(K(Spread, "SPREAD", v => NotaNum.F($"{v * 50:0} ms"), 34, 50, null, SyncTaps), 3));
                var toggles = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Children = {
                    Toggle(LinkLR, "Link L/R"), Toggle(FadeChange, "Fade on change") } };
                g.Children.Add(Col(toggles, 4));
                return g;
            }
            Control? syncRows = null, msRow = null;
            void Refill() => host.Content = On(SyncMode) ? syncRows ??= SyncRows() : msRow ??= MsRow();
            Refill();

            var modeSeg = Seg(SyncMode, new[] { "ms", "Sync" }, onPick: () => { Refill(); SyncTaps(); });
            var hint = Mono("", 7, TextTertiary); hint.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => hint.Text = On(SyncMode)
                ? NotaNum.F($"L {DivNames[Sel(DivL, 8)]} · R {DivNames[On(LinkLR) ? Sel(DivL, 8) : Sel(DivR, 8)]}")
                : On(LinkLR) ? "channels linked" : "channels independent");

            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,*"), ColumnSpacing = 5, Height = 20 };
            head.Children.Add(Caps("MODE"));
            head.Children.Add(Col(modeSeg, 1));
            head.Children.Add(Col(Latch(LinkLR, "Link", "Link L/R — the right channel follows the left's time"), 2));
            head.Children.Add(Col(Latch(PingPong, "Ping", "Ping-pong — each repeat crosses to the other channel"), 3));
            head.Children.Add(Col(Latch(FadeChange, "Fade", "Fade on change — crossfade to a new delay time instead of gliding to it (which bends the pitch, tape style)"), 4));
            head.Children.Add(Col(hint, 5));

            var bottom = new Border { Child = host };
            // Automation or a preset can flip Sync under us — follow it.
            readouts.Add(() => { if (On(SyncMode) != ReferenceEquals(host.Content, syncRows)) Refill(); });
            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 4), Children = {
                Docked(head, Dock.Top),
                Docked(bottom, Dock.Bottom),
                new Border { Margin = new Thickness(0, 4, 0, 4), Child = NewTaps() } } };
        }

        // ======================================================================
        // CENTRE — Loop · Wow
        // ======================================================================
        Control LoopTab()
        {
            var filter = new DelayFilterCurve { MinWidth = 78 };
            filter.Changed += (h, v) => Raw(h == 0 ? LowCut : HighCut, (float)v);
            filter.GestureBegin += h => Begin(h == 0 ? LowCut : HighCut);
            filter.GestureEnd += h => End(h == 0 ? LowCut : HighCut);
            filter.Reset += h => Reset(h == 0 ? LowCut : HighCut);
            ToolTip.SetTip(filter, "The band the repeats live in — drag the left handle for the low cut, the right one for the high cut. Double-click a handle to open it.");
            MidiLearn.Bind(filter, MidiTarget.DeviceParam(track, di, LowCut), "Low Cut");
            readouts.Add(() => { if (!filter.Dragging) filter.Set(P(LowCut), P(HighCut), LowHz(P(LowCut)), HighHz(P(HighCut))); });

            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("48,48,*,60,60,Auto"), ColumnSpacing = 4, Height = 58 };
            g.Children.Add(K(Feedback, "FEEDBACK", v => On(Freeze) ? "HOLD" : Pct(v), 34, 48, null, SyncTaps));
            g.Children.Add(Col(K(Diffuse, "DIFFUSE", Pct, 34, 48), 1));
            g.Children.Add(Col(new Border { Margin = new Thickness(2, 2, 2, 6), Child = filter }, 2));
            g.Children.Add(Col(K(WowRate, "WOW RATE", v => { double hz = Exp(v, 0.05, 8); return hz >= 1 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz"); }, 34, 60, Teal), 3));
            g.Children.Add(Col(K(WowDepth, "WOW DEPTH", Pct, 34, 60, Teal), 4));
            var toggles = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Children = {
                Toggle(Freeze, "Freeze"), Toggle(TapeMode, "Tape mode") } };
            g.Children.Add(Col(toggles, 5));

            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 4), Children = {
                Docked(g, Dock.Bottom),
                new Border { Margin = new Thickness(0, 0, 0, 4), Child = NewTaps() } } };
        }

        // ======================================================================
        // RIGHT — Levels / Output
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

        Control LevelsTab()
        {
            var wetBar = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            var wetDb = Mono("", 8, TextPrimary); wetDb.Width = 30; wetDb.TextAlignment = TextAlignment.Right;
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

            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var freezeBtn = Btn("Freeze", () => { SetP(Freeze, On(Freeze) ? 0f : 1f); SyncTaps(); }, "Freeze — hold the loop forever; the input is muted and the repeats stop decaying");
            var freezeTxt = (TextBlock)freezeBtn.Child!;
            freezeBtn.BorderThickness = new Thickness(1);
            readouts.Add(() =>
            {
                bool on = On(Freeze);
                freezeBtn.Background = on ? NotaPalette.AccentSubtle : Raised;
                freezeBtn.BorderBrush = on ? NotaPalette.BorderBrass : Brushes.Transparent;
                freezeTxt.Foreground = on ? NotaPalette.AccentHover : TextPrimary;
                freezeTxt.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            });
            btns.Children.Add(freezeBtn);
            btns.Children.Add(Col(Btn("Tap tempo", TapTempo, "Tap four times to set the delay time by ear (switches to free ms)"), 1));

            var clear = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            clear.Children.Add(Btn("Clear loop", () => engine.DeviceAction(track, di, A_ClearLoop, 0, 0), "Empty the delay buffer — silences what is still circulating"));
            clear.Children.Add(Col(Toggle(WetOnly, "Wet only"), 1));

            var rows = new Control[]
            {
                SliderRow("MIX", DryWet, Pct, 52, () => On(WetOnly)),
                SliderRow("DRY", DryLevel, v => ShortDb(DryDb(v)), 52, () => On(WetOnly)),
                SliderRow("OUT", Output, v => ShortDb(OutDb(v))),
                new Border { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 2) },
                SliderRow("FEEDBACK", Feedback, v => On(Freeze) ? "HOLD" : Pct(v), 52),
                SliderRow("SPREAD", Spread, v => NotaNum.F($"{v * 50:0}"), 52),
                new Border { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 2) },
                btns, clear, wet,
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,*,*,Auto,*,*,Auto,*,*,*") };
            for (int r = 0; r < rows.Length; r++) grid.Children.Add(GRow(rows[r], r));
            return new Border { Padding = new Thickness(8, 4), Child = grid };
        }

        Control OutputTab()
        {
            var meterL = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
            var meterR = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
            readouts.Add(() => { meterL.SetLinear(Sc(S_OutL)); meterR.SetLinear(Sc(S_OutR)); });
            static Control Centre(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }
            var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), Height = 56, Children = { Mono("0", 7, TextDisabled), GRow(Mono("−12", 7, TextDisabled), 1), GRow(Mono("−48", 7, TextDisabled), 2) } };
            var meters = Row(4,
                new StackPanel { Spacing = 2, Children = { meterL, Centre(Caps("L")) } },
                new StackPanel { Spacing = 2, Children = { meterR, Centre(Caps("R")) } },
                scale);
            meters.VerticalAlignment = VerticalAlignment.Top;
            var top = Row(8, K(Output, "OUT", v => Db(OutDb(v)), 40, 54), meters);

            var mid = new StackPanel { Spacing = 6, Children = {
                SliderRow("MIX", DryWet, Pct, 52, () => On(WetOnly)),
                SliderRow("WIDTH", WidthP, v => NotaNum.F($"{v * 200:0}"), 52),
                SliderRow("MONO f", BassMono, v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0}"), 52) } };

            var wetOnly = Toggle(WetOnly, "Wet only (send)");
            ToolTip.SetTip(wetOnly, "Wet only — repeats with no dry, for a return / send track");
            var comp = Toggle(LatencyComp, "Latency comp.");
            ToolTip.SetTip(comp, "The diffuser lengthens the loop by a fixed group delay; on, the read pointer is pulled back by exactly that so each repeat lands on the set time.");
            var bottom = new StackPanel { Spacing = 5, Children = { wetOnly, comp } };

            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(top, Dock.Top), Docked(Divider(mid), Dock.Top), Docked(Divider(bottom), Dock.Bottom) } };
            ((Control)body.Children[1]).Margin = new Thickness(0, 5, 0, 0);
            return new Border { Padding = new Thickness(8, 5), Child = body };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[2];
        var rightBodies = new Control?[2];
        int centreTab = 0;
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
            else
            {
                extras.Text = On(Freeze)
                    ? NotaNum.F($"input muted · loop {Secs(MsL() / 1000)}")
                    : NotaNum.F($"filter {LowCutF(P(LowCut))} — {HighCutF(P(HighCut))}");
                extras.Foreground = On(Freeze) ? AccentBright : TextTertiary;
            }
        });
        Control CentreBody(int t) => centreBodies[t] ??= t == 1 ? LoopTab() : TimeTab();
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : LevelsTab();

        var centre = TabFrame(new[] { "Time", "Loop · Wow" }, centreHost, CentreBody, false, t => { centreTab = t; foreach (var a in readouts) a(); }, extras);
        var rightFrame = TabFrame(new[] { "Levels", "Output" }, rightHost, RightBody, true, _ => { foreach (var a in readouts) a(); }, null);
        var right = new Border { Width = 186, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Margin = new Thickness(5, 0), ClipToBounds = true, Child = centre };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        string StatusText()
        {
            if (On(Freeze))
                return NotaNum.F($"Freeze: loop held, input muted · filter {LowCutF(P(LowCut))} — {HighCutF(P(HighCut))}");
            string time = On(SyncMode)
                ? NotaNum.F($"Sync {DivNames[Sel(DivL, 8)]}{(On(LinkLR) ? "" : NotaNum.F($" / {DivNames[Sel(DivR, 8)]}"))}")
                : NotaNum.F($"ms · L {MsL():0} / R {(On(LinkLR) ? MsL() : MsR()):0}");
            string mode = On(PingPong) ? " · ping-pong" : "";
            string tape = On(TapeMode) ? " · tape" : "";
            // How many repeats stay above −60 dB — the honest answer to "how long does it ring".
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
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl)
        {
            int sel = 0;
            var btns = new Border[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? Card2 : Brushes.Transparent;
                    btns[i].BorderBrush = on ? Brass : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!;
                    tb.Foreground = on ? AccentBright : TextTertiary;
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
