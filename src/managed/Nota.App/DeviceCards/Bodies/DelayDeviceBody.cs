// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Delay body (device kind 3), a build of the "Nota Delay"
// mockup (700 × 260) on the Chamber frame: an always-visible LOOP column (Feedback ·
// Spread), a centre tabbed panel (Time / Loop · Wow) over the live repeat window, a right
// tabbed panel (Levels / Output); no status line. The repeat window and the feedback
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
        static TextBlock Caps(string t, IBrush? c = null, double fs = 0) => FxParts.Label(t, c);
        static TextBlock Mono(string t, double fs, IBrush c) { var tb = FxParts.Info(t, c); tb.TextTrimming = TextTrimming.None; return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 5) => new()
        { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 50, IBrush? arc = null, Action? onChange = null, bool inline = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Inline = inline, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
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
            // Raised at rest; on, the soft tint with orange text.
            var tb = new TextBlock { Text = text, FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = NotaSize.Dropdown, Padding = new Thickness(9, 0), CornerRadius = NotaRadius.Field, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? AccentBright : TextPrimary;
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
            // A raised button inside a device.
            var tb = new TextBlock { Text = text, FontSize = NotaType.KnobValue, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = NotaSize.Dropdown, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Field, Background = NotaPalette.SurfaceHover, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); e.Handled = true; };
            if (tip != null) ToolTip.SetTip(b, tip);
            return b;
        }

        // Horizontal bar slider over a param.
        Control Bar(int p, Func<double, string> fmt, Func<bool>? dim = null, double valW = 34)
        {
            var row = DeviceCardKit.SliderRow("", () => P(p), v => Raw(p, (float)v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), dim: dim, valueWidth: valW);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return row;
        }
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 57, Func<bool>? dim = null, double valW = 34)
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
            if (freeze) { title = "Loop held · no decay"; sub = NotaNum.F($"R {Ms(msr)}"); }
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
        // LEFT — Loop column (Feedback · Spread)
        // ======================================================================
        Control LoopFader(int p, string label, Func<string> value, string tip)
        {
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center; lb.Margin = new Thickness(0, 7, 0, 0);
            var val = Mono("", 7, TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Center;
            var f = FxParts.Fader(engine.DeviceParamName(track, di, p), () => P(p), v => Raw(p, (float)v), value,
                () => Begin(p), () => End(p), () => Reset(p), val, out var fSync, after: SyncTaps);
            MidiLearn.Bind(f, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            ToolTip.SetTip(f, tip);
            readouts.Add(() =>
            {
                fSync();
                bool hot = On(Freeze) && p == Feedback;
                val.Foreground = hot ? AccentBright : P(p) > 0.9f ? NotaPalette.Warning : TextPrimary;   // Freeze is live; > 90 % runs away
                lb.Foreground = hot ? AccentBright : TextTertiary;
            });
            return new DockPanel { Width = 54, Children = { Docked(val, Dock.Bottom), Docked(lb, Dock.Bottom), f } };
        }
        var loopTitle = new TextBlock { Text = "Loop", FontSize = FxParts.TabSize, FontWeight = FontWeight.SemiBold, Foreground = TextSecondary, Margin = new Thickness(12, 0, 0, 0) };
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
        faders.Children.Add(LoopFader(Feedback, "Feedback",
            () => On(Freeze) ? "Hold" : Pct(P(Feedback)),
            "Feedback — how much of each repeat goes round again; Freeze pins it at 100 %. Double-click resets."));
        faders.Children.Add(Col(LoopFader(Spread, "Spread",
            () => NotaNum.F($"{P(Spread) * 50:0} ms"),
            "Spread — pushes the right channel later, up to 50 ms. Double-click resets."), 1));
        var loopCol = new RuleSection
        {
            Width = 118, Padding = new Thickness(0, 12, 0, 12),
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
                    var g = new Grid { ColumnDefinitions = new ColumnDefinitions("12,*"), ColumnSpacing = 8, Height = 24 };
                    var lbl = Caps(ch); g.Children.Add(lbl);
                    // Eight divisions: past the almanac's four-choice limit for segments — an
                    // accepted exception, see DESIGN.md § Accepted exceptions.
                    g.Children.Add(Col(Seg(p, DivNames, fill: true, onPick: SyncTaps), 1));
                    if (dim is not null) readouts.Add(() => lbl.Foreground = dim() ? TextDisabled : TextTertiary);
                    return g;
                }
                return new StackPanel { Spacing = 6, Children = { LR("L", DivL, null), LR("R", DivR, () => On(LinkLR)) } };
            }
            Control MsRow()
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,Auto"), Height = 56 };
                g.Children.Add(K(TimeL, "L time", v => Ms(v * 2000), 34, 50, null, SyncTaps));
                g.Children.Add(Col(K(TimeR, "R time", v => On(LinkLR) ? Ms(P(TimeL) * 2000) : Ms(v * 2000), 34, 50, null, SyncTaps), 1));
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
            // The L/R division rows and the Link latch already say what a readout here would.

            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,*"), ColumnSpacing = 8, Height = 24 };
            head.Children.Add(Caps("Mode"));
            head.Children.Add(Col(modeSeg, 1));
            head.Children.Add(Col(Latch(LinkLR, "Link", "Link L/R — the right channel follows the left's time"), 2));
            head.Children.Add(Col(Latch(PingPong, "Ping", "Ping-pong — each repeat crosses to the other channel"), 3));
            head.Children.Add(Col(Latch(FadeChange, "Fade", "Fade on change — crossfade to a new delay time instead of gliding to it (which bends the pitch, tape style)"), 4));

            var bottom = new Border { Child = host };
            // Automation or a preset can flip Sync under us — follow it.
            readouts.Add(() => { if (On(SyncMode) != ReferenceEquals(host.Content, syncRows)) Refill(); });
            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0), Children = {
                Docked(head, Dock.Top),
                Docked(bottom, Dock.Bottom),
                new Border { Margin = new Thickness(0, 6, 0, 6), Child = NewTaps() } } };
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

            // The filter band sits beside the taps (it shapes every repeat), the knobs and switches under both.
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("52,46,56,60,*,Auto"), ColumnSpacing = 3, Height = 58 };
            g.Children.Add(K(Feedback, "FEEDBACK", v => On(Freeze) ? "Hold" : Pct(v), 34, 52, null, SyncTaps));
            g.Children.Add(Col(K(Diffuse, "DIFFUSE", Pct, 34, 46), 1));
            g.Children.Add(Col(K(WowRate, "WOW RATE", v => { double hz = Exp(v, 0.05, 8); return hz >= 1 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz"); }, 34, 56), 2));
            g.Children.Add(Col(K(WowDepth, "WOW DEPTH", Pct, 34, 60), 3));
            var toggles = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), Children = {
                Toggle(Freeze, "Freeze"), Toggle(TapeMode, "Tape mode") } };
            g.Children.Add(Col(toggles, 5));

            filter.Width = 96; filter.Margin = new Thickness(6, 0, 0, 0);
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
            top.Children.Add(NewTaps()); top.Children.Add(Col(filter, 1));
            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0), Children = {
                Docked(g, Dock.Bottom), top } };
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
            var wetDb = Mono("", 8, TextPrimary); wetDb.Width = 34; wetDb.TextAlignment = TextAlignment.Right;
            readouts.Add(() =>
            {
                double pk = Sc(S_WetPk);
                wetBar.SetLinear(pk);
                double db = pk > 1e-5 ? 20 * Math.Log10(pk) : -100;
                wetDb.Text = ShortDb(db);
                wetDb.Foreground = db > -6 ? NotaPalette.Warning : TextPrimary;
            });
            var wet = new Grid { ColumnDefinitions = new ColumnDefinitions("49,*,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
            wet.Children.Add(Caps("Wet")); wet.Children.Add(Col(wetBar, 1)); wet.Children.Add(Col(wetDb, 2));

            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            static Border Wide(Border b) { b.HorizontalAlignment = HorizontalAlignment.Stretch; return b; }
            var freezeBtn = Btn("Freeze", () => { SetP(Freeze, On(Freeze) ? 0f : 1f); SyncTaps(); }, "Freeze — hold the loop forever; the input is muted and the repeats stop decaying");
            var freezeTxt = (TextBlock)freezeBtn.Child!;
            readouts.Add(() =>
            {
                bool on = On(Freeze);
                freezeBtn.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                freezeTxt.Foreground = on ? AccentBright : TextPrimary;
            });
            btns.Children.Add(Wide(freezeBtn));
            btns.Children.Add(Col(Wide(Btn("Tap tempo", TapTempo, "Tap four times to set the delay time by ear (switches to free ms)")), 1));

            var clear = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            clear.Children.Add(Wide(Btn("Clear loop", () => engine.DeviceAction(track, di, A_ClearLoop, 0, 0), "Empty the delay buffer — silences what is still circulating")));
            clear.Children.Add(Col(Toggle(WetOnly, "Wet only"), 1));

            var rows = new Control[]
            {
                SliderRow("Mix", DryWet, Pct, dim: () => On(WetOnly)),
                SliderRow("Dry", DryLevel, v => ShortDb(DryDb(v)), dim: () => On(WetOnly)),
                SliderRow("Out", Output, v => ShortDb(OutDb(v))),
                new Border { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center },
                btns, clear, wet,
            };
            // Feedback and Spread live on the Loop rail beside this panel, so they are not repeated here.
            var grid = new Grid { RowDefinitions = new RowDefinitions("26,26,26,9,30,30,*"), Margin = new Thickness(0, 3, 0, 0) };
            for (int r = 0; r < rows.Length; r++) grid.Children.Add(GRow(rows[r], r));
            btns.VerticalAlignment = VerticalAlignment.Bottom; clear.VerticalAlignment = VerticalAlignment.Bottom;
            return grid;
        }

        Control OutputTab()
        {
            // 190 high: the knob and meter in one 36 row, three bars, two switches.
            var top = DeviceBodyKit.OutputHead(K(Output, "Out", v => Db(OutDb(v)), cellW: 96, inline: true), () => Sc(S_OutL), () => Sc(S_OutR), readouts.Add);

            var mid = new Grid { RowDefinitions = new RowDefinitions("26,26,26"), Children = {
                SliderRow("Mix", DryWet, Pct, dim: () => On(WetOnly)),
                GRow(SliderRow("Width", WidthP, v => NotaNum.F($"{v * 200:0}")), 1),
                GRow(SliderRow("Mono f", BassMono, v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0}")), 2) } };

            var wetOnly = Toggle(WetOnly, "Wet only (send)");
            ToolTip.SetTip(wetOnly, "Wet only — repeats with no dry, for a return / send track");
            var comp = Toggle(LatencyComp, "Latency comp.");
            ToolTip.SetTip(comp, "The diffuser lengthens the loop by a fixed group delay; on, the read pointer is pulled back by exactly that so each repeat lands on the set time.");
            var bottom = new StackPanel { Spacing = 0, Children = { wetOnly, comp } };

            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(top, Dock.Top), Docked(Divider(mid), Dock.Top), Docked(Divider(bottom), Dock.Bottom) } };
            ((Control)body.Children[1]).Margin = new Thickness(0, 4, 0, 0);
            body.Margin = new Thickness(0, 4, 0, 0);
            return body;
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
                    : P(LowCut) <= 0.001 && P(HighCut) >= 0.999 ? "filter open"
                    : NotaNum.F($"low cut {LowCutF(P(LowCut))} · high cut {HighCutF(P(HighCut))}");
                extras.Foreground = On(Freeze) ? AccentBright : TextTertiary;
            }
        });
        Control CentreBody(int t) => centreBodies[t] ??= t == 1 ? LoopTab() : TimeTab();
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : LevelsTab();

        var centre = FxParts.Tabs(new[] { "Time", "Loop · Wow" }, centreHost, CentreBody, t => { centreTab = t; foreach (var a in readouts) a(); }, extras);
        var rightFrame = FxParts.Tabs(new[] { "Levels", "Output" }, rightHost, RightBody, _ => { foreach (var a in readouts) a(); });
        var right = FxParts.Section(rightFrame); right.Width = 200;
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = FxParts.Section(centre); centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { loopCol, right, centreBox } };
        var root = bodyRow;

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
