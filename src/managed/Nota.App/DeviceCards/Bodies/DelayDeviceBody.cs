// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Delay body (device kind 3), a build of the "Nota Delay"
// mockup, laid out the #531 way (CardLayout): a centre panel with Time / Loop · Wow tabs over
// the live repeat window (L · R time — ms or a note division — and Feedback beside it on Time,
// the mode and Link / Ping / Fade under it; Diffuse and the wow beside it on Loop, Tape and Clear
// loop under it) and a right panel with no tabs of its own that follows them: Stereo (Tap tempo,
// Spread, Width, Mono f, Wet only, latency comp) and Loop (Freeze, the repeats' filter, the wet
// meter) — over Dry · Dry/wet · Out. S is the window over the mode and L, R, Feedback, Dry/wet. The repeat window and the
// feedback filter are controls, not decoration. Every control is a device param, so automation /
// MIDI learn / presets / A-B / persistence come for free; Tap tempo and Clear loop are the
// two actions that are not (the first writes Time L/R, the second wipes the loop).
// FullBleed — the shared shell draws the header (name · preset · badge · bypass).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using Nota.Application.Localization;

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
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
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
        var scope = new float[kScope + SpaceDraw.Floats];   // the telemetry, then the level history (#377)
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
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 50, IBrush? arc = null, Action? onChange = null,
            Func<double, double>? snap = null, Func<bool>? dim = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
            knob.ValueChanged += v => { Raw(p, (float)(snap is null ? v : snap(v))); val.Text = fmt(P(p)); onChange?.Invoke(); };
            if (dim is not null) readouts.Add(() => knob.IsDim = dim());
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
            var b = new Border { Height = 18, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Field, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
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
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, Background = Raised, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
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
        var start = DateTime.UtcNow;   // Tap tempo's clock
        bool tapDrag = false;
        float fb0 = 0;
        DelayTaps NewTaps()
        {
            var t = new DelayTaps();
            ToolTip.SetTip(t, Loc.T("device.delay.the_repeat_train_l_above"));
            // The first echo of a lane is its handle: sideways sets the lane's time (the note
            // division when synced), up / down sets Feedback. Linked, both lanes move L.
            int TimeP(int lane) => On(SyncMode) ? (lane == 0 || On(LinkLR) ? DivL : DivR) : (lane == 0 || On(LinkLR) ? TimeL : TimeR);
            t.GestureBegin += lane => { tapDrag = true; fb0 = P(Feedback); Begin(TimeP(lane)); Begin(Feedback); };
            t.GestureEnd += lane => { End(TimeP(lane)); End(Feedback); tapDrag = false; SyncTaps(); };
            t.TimeDragged += (lane, frac) =>
            {
                bool l = lane == 0 || On(LinkLR);
                double ms = frac * windowSec * 1000.0;
                // R's first echo comes after L's in ping-pong, after the spread otherwise.
                if (!l) ms -= On(PingPong) ? MsL() : SpreadMs();
                if (On(SyncMode))
                {
                    double beats = ms / BeatMs();
                    int best = 0;
                    for (int k = 1; k < DivBeats.Length; k++) if (Math.Abs(DivBeats[k] - beats) < Math.Abs(DivBeats[best] - beats)) best = k;
                    Raw(l ? DivL : DivR, best / 7f);
                }
                else Raw(l ? TimeL : TimeR, (float)(Math.Max(1, ms) / 2000.0));
                SyncTaps(); foreach (var a in readouts) a();
            };
            t.FeedbackDragged += (_, dy) => { if (!On(Freeze)) Raw(Feedback, (float)(fb0 + dy / 160.0)); };
            tapViews.Add(t);
            return t;
        }
        void SyncTaps()
        {
            if (tapViews.Count == 0) return;
            bool sync = On(SyncMode), link = On(LinkLR), freeze = On(Freeze);
            double msl = MsL(), msr = link ? msl : MsR();
            // The window holds still while a handle is dragged, so the echo stays under the pointer.
            if (!tapDrag)
                windowSec = sync ? SyncWindowBeats * BeatMs() / 1000.0 : NiceWindow(Math.Max(msl, msr + SpreadMs()) * 3.2 / 1000.0);
            double winMs = windowSec * 1000.0;
            double fracL = msl / winMs, fracR = (msr + (On(PingPong) ? 0 : SpreadMs())) / winMs;
            string labelL, labelR;
            var ticks = new List<(double, string)>();
            if (sync)
            {
                int dl = Sel(DivL, 8), dr = link ? dl : Sel(DivR, 8);
                labelL = NotaNum.F($"{DivNames[dl]} · {Ms(msl)}");
                labelR = NotaNum.F($"{DivNames[dr]} · {Ms(msr)}");
                string[] beatNames = { "1/4", "1/2", "3/4", "1 bar", "5/4", "6/4", "7/4", "2 bars" };
                double bpw = winMs / BeatMs();
                for (int b = 1; b <= beatNames.Length && b <= bpw + 1e-6; b++) ticks.Add((b * BeatMs() / winMs, beatNames[b - 1]));
            }
            else
            {
                labelL = Ms(msl);
                labelR = Ms(msr);
                double step = new[] { 25.0, 50, 100, 200, 250, 500, 1000, 2000 }.First(st => winMs / st <= 6);
                for (double m = step; m <= winMs + 1e-6; m += step) ticks.Add((m / winMs, Ms(m)));
            }
            if (freeze) labelL = Loc.T("device.delay.loop_held_no_decay");
            foreach (var t in tapViews) t.Set(fracL, fracR, P(Feedback), On(PingPong), freeze, link, labelL, labelR, ticks.ToArray());

            // Your echoes behind the stems (the level history after the telemetry), and the repeat
            // of the latest hit that is sounding now lit — counted from the hit itself, while the
            // wet path is making sound.
            bool has = scN >= kScope + SpaceDraw.Floats;
            double age = -1;
            foreach (var t in tapViews) age = t.SetLive(scope, kScope, has, windowSec);
            int litL = -1, litR = -1;
            if (age >= 0 && Sc(S_WetPk) > 5e-4 && msl > 1 && msr > 1)
            {
                // The same spacing as the stems (DelayTaps.Echoes): ping-pong alternates the
                // lanes, so each lane repeats every L + R; otherwise L every L time and R every
                // R time plus the spread. Count from each lane's first echo.
                double ms = age * 1000.0;
                bool ping = On(PingPong);
                double firstL = msl, periodL = ping ? msl + msr : msl;
                double firstR = ping ? msl + msr : msr + SpreadMs(), periodR = ping ? msl + msr : msr + SpreadMs();
                static int Lit(double t, double first, double period) => t >= first ? (int)((t - first) / period) : -1;
                litL = Lit(ms, firstL, periodL);
                litR = Lit(ms, firstR, periodR);
            }
            foreach (var t in tapViews) t.SetLit(litL, litR);
        }
        readouts.Add(SyncTaps);

        // ======================================================================
        // Time knobs: the free time in ms, or the note division when synced (the same place)
        // ======================================================================
        Control TimeKnob(string name, int timeP, int divP, Func<bool>? linked, double cellW)
        {
            var free = K(timeP, name, v => linked?.Invoke() == true ? Ms(P(TimeL) * 2000) : Ms(v * 2000), 34, cellW, null, SyncTaps, dim: linked);
            var div = K(divP, name, v => linked?.Invoke() == true ? DivNames[Sel(DivL, 8)] : DivNames[Math.Clamp((int)Math.Round(v * 7), 0, 7)], 34, cellW, null, SyncTaps,
                snap: v => Math.Round(v * 7) / 7, dim: linked);
            readouts.Add(() => { free.IsVisible = !On(SyncMode); div.IsVisible = On(SyncMode); });
            return new Panel { Children = { free, div } };
        }
        Control FeedbackKnob(double cellW) => K(Feedback, "FEEDBACK", v => On(Freeze) ? "HOLD" : Pct(v), 34, cellW, null, SyncTaps);
        Control MixKnob(double cellW, double size) => K(DryWet, "DRY/WET", Pct, size, cellW, dim: () => On(WetOnly));
        Control ModeSeg() => Seg(SyncMode, new[] { "ms", "Sync" }, onPick: SyncTaps);

        // ======================================================================
        // CENTRE — Time (the repeat window: L · R · Feedback beside it; the mode and Link,
        // Ping, Fade under it) / Loop · Wow (Diffuse · Wow rate · Wow depth beside it)
        // ======================================================================
        Control TimeTab() => CardLayout.View(NewTaps(),
            CardLayout.KnobColumn(TimeKnob("L TIME", TimeL, DivL, null, 50), TimeKnob("R TIME", TimeR, DivR, () => On(LinkLR), 50), FeedbackKnob(50)),
            CardLayout.Line(5, ModeSeg(),
                Latch(LinkLR, "Link", Loc.T("device.delay.link_l_r_the_right")),
                Latch(PingPong, "Ping", Loc.T("device.delay.ping_pong_each_repeat_crosses")),
                Latch(FadeChange, "Fade", Loc.T("device.delay.fade_on_change_crossfade_to"))));

        Control FilterTab()
        {
            var band = new DelayBandCurve();
            band.Changed += (h, v) => { Raw(h == 0 ? LowCut : HighCut, (float)v); foreach (var r in readouts) r(); };
            band.GestureBegin += h => Begin(h == 0 ? LowCut : HighCut);
            band.GestureEnd += h => End(h == 0 ? LowCut : HighCut);
            band.Reset += h => Reset(h == 0 ? LowCut : HighCut);
            ValueEntry.AttachHandles(band, [ValueEntry.DeviceParam(engine, track, di, LowCut, "Low cut", v => Hz(LowHz(v)))],
                [ValueEntry.DeviceParam(engine, track, di, HighCut, "High cut", v => Hz(HighHz(v)))]);
            ToolTip.SetTip(band, MenuKit.Keys(Loc.T("device.delay.the_band_the_repeats_live")));
            MidiLearn.Bind(band, MidiTarget.DeviceParam(track, di, LowCut), "Low Cut");
            // Behind the curve, the spectrum of your echoes (the level history's latest wet samples).
            var spectrum = new FilterSpectrum();
            band.Spectrum = spectrum;
            const int specOff = kScope + 2 * SpaceDraw.Points;
            readouts.Add(() =>
            {
                if (!band.Dragging) band.Set(LowHz(P(LowCut)), HighHz(P(HighCut)), P(LowCut) <= 0.001, P(HighCut) >= 0.999);
                if (band.IsEffectivelyVisible && spectrum.Feed(scope, specOff, scN - specOff, Sc(S_SampleRate), 20, 20000)) band.InvalidateVisual();
            });
            return CardLayout.View(band,
                CardLayout.KnobColumn(K(LowCut, "LOW CUT", LowCutF, 34, 50), K(HighCut, "HIGH CUT", HighCutF, 34, 52)));
        }

        Control LoopTab() => CardLayout.View(NewTaps(),
            CardLayout.KnobColumn(
                K(Diffuse, "DIFFUSE", Pct, 34, 50),
                K(WowRate, "WOW RATE", v => { double hz = Exp(v, 0.05, 8); return hz >= 1 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz"); }, 34, 52, Teal),
                K(WowDepth, "WOW DEPTH", Pct, 34, 52, Teal)),
            CardLayout.Line(6, Latch(TapeMode, "Tape mode", Loc.T("device.delay.tape_mode")),
                Btn(Loc.T("device.delay.clear_loop"), () => engine.DeviceAction(track, di, A_ClearLoop, 0, 0), Loc.T("device.delay.empty_the_delay_buffer_silences"))));

        // ======================================================================
        // RIGHT — follows the centre tab: Stereo (Time) · Loop (Loop · Wow)
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

        (Control, Control) StereoPanel()
        {
            var wetOnly = Toggle(WetOnly, Loc.T("common.wet_only_send"));
            ToolTip.SetTip(wetOnly, Loc.T("device.delay.wet_only_repeats_with_no"));
            var comp = Toggle(LatencyComp, Loc.T("device.delay.latency_comp"));
            ToolTip.SetTip(comp, Loc.T("device.delay.the_diffuser_lengthens_the_loop"));
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.delay.stereo")), Btn(Loc.T("device.delay.tap_tempo"), TapTempo, Loc.T("device.delay.tap_four_times_to_set"))),
                CardLayout.Rows(
                    SliderRow("SPREAD", Spread, v => NotaNum.F($"{v * 50:0} ms"), 46, valW: 36),
                    SliderRow("WIDTH", WidthP, v => NotaNum.F($"{v * 200:0} %"), 46, valW: 36),
                    SliderRow("Mono f", BassMono, v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0} Hz"), 46, valW: 36),
                    CardLayout.Rule(),
                    wetOnly,
                    comp));
        }

        (Control, Control) LoopPanel()
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
            var freeze = Toggle(Freeze, "Freeze");
            ToolTip.SetTip(freeze, Loc.T("device.delay.freeze_hold_the_loop_forever"));
            return (CardLayout.Head(freeze), CardLayout.Rows(
                wet,
                Info(() => NotaNum.F($"loop {Secs(MsL() / 1000)} · repeats {LowCutF(P(LowCut))} — {HighCutF(P(HighCut))}"))));
        }
        // The Filter tab's panel: how long the repeats ring and what's left of them.
        (Control, Control) TonePanel()
        {
            Control Line(Func<string> k, Func<string> v)
            {
                var key = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
                var val = Mono("", 8, TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => { key.Text = k(); val.Text = v(); });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(key); g.Children.Add(Col(val, 1));
                return g;
            }
            // Repeats until they fall 48 dB (the lanes' floor); −1 = for ever.
            int Ring()
            {
                double fb = P(Feedback);
                if (On(Freeze) || fb >= 0.999) return -1;
                if (fb <= 0.001) return 1;
                return (int)Math.Ceiling(-48 / (20 * Math.Log10(fb))) + 1;
            }
            var box = CardLayout.ReadoutBox(new StackPanel { Spacing = 2, Children = {
                Caps(Loc.T("device.delay.repeats")),
                Line(() => Loc.T("device.delay.band"), () => NotaNum.F($"{LowCutF(P(LowCut))} — {HighCutF(P(HighCut))}")),
                Line(() => Loc.T("device.delay.repeats_to_48_db"), () => Ring() < 0 ? "∞" : Ring().ToString(NotaNum.Culture)),
                Line(() => Loc.T("device.delay.ring_time"), () => Ring() < 0 ? "∞" : Secs(Ring() * MsL() / 1000)) } });
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.delay.repeats"))), CardLayout.Rows(box));
        }
        Control Info(Func<string> text)
        {
            var t = Mono("", 8, TextSecondary);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => t.Text = text());
            return t;
        }

        // ======================================================================
        // Frames: L (Time / Loop · Wow, the panel that follows them, Dry · Dry/wet · Out under
        // it) · S (the repeat window over the mode and L · R · Feedback · Dry/wet)
        // ======================================================================
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
            else if (centreTab == 1)
            {
                extras.Text = NotaNum.F($"repeats {LowCutF(P(LowCut))} — {HighCutF(P(HighCut))}");
                extras.Foreground = TextTertiary;
            }
            else
            {
                extras.Text = On(Freeze)
                    ? NotaNum.F($"input muted · loop {Secs(MsL() / 1000)}")
                    : NotaNum.F($"diffuse {Pct(P(Diffuse))} · wow {Pct(P(WowDepth))}");
                extras.Foreground = On(Freeze) ? AccentBright : TextTertiary;
            }
        });
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(NewTaps(), ModeSeg(),
                TimeKnob("L TIME", TimeL, DivL, null, 56), TimeKnob("R TIME", TimeR, DivR, () => On(LinkLR), 56), FeedbackKnob(56), MixKnob(56, 34))
            : CardLayout.Large(new[] { "Time", "Filter", "Loop · Wow" }, 0,
                t => t switch { 1 => FilterTab(), 2 => LoopTab(), _ => TimeTab() },
                t => t switch { 1 => TonePanel(), 2 => LoopPanel(), _ => StereoPanel() },
                extras,
                CardLayout.Output(K(DryLevel, "DRY", v => ShortDb(DryDb(v)), 34, 54, dim: () => On(WetOnly)), MixKnob(58, 44), K(Output, "OUT", v => Db(OutDb(v)), 34, 54)),
                t => { centreTab = t; foreach (var a in readouts) a(); });

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, scope.Length);
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

    }
}
