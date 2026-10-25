// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Auto Filter body (device kind 7), a build of the "Nota Auto
// Filter" mockup (700 × 260) on the Lens / Compressor / Delay frame: an always-visible
// SHAPE column (cutoff · resonance faders, with the modulated value marked), a centre
// panel with Filter / Envelope / LFO tabs — each one window where the cutoff's movement
// is seen (the response, the cutoff over time, the LFO over two bars) — a right panel with
// Mod / Output tabs; no status line. Every control is a device param, so automation /
// MIDI learn / presets / A-B / persistence come for free; the sidechain source is routing.
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

internal sealed class AutoFilterDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match AutoFilter.h) ──────────────────────────
    private const int Freq = 0, Res = 1, Type = 2, Slope = 3, Morph = 4, EnvAmt = 5, EnvAtt = 6,
        EnvRel = 7, EnvHold = 8, LfoAmt = 9, LfoRate = 10, LfoWave = 11, LfoMorph = 12,
        Drive = 13, DryWet = 14, EnvOn = 15, LfoOn = 16, Gain = 17, Circuit = 18,
        LfoSync = 19, LfoPhase = 20, ModTarget = 21, EnvHoldTime = 22, ModSmooth = 23,
        LfoOffset = 24, LfoRetrig = 25;
    // Scope layout (AutoFilter::S_* / kHist / kScope).
    private const int S_Cut = 0, S_CutR = 1, S_Res = 2, S_Env = 3, S_Lfo = 4, S_LfoPhase = 5, S_InPk = 6,
        S_OutPk = 7, S_Cpu = 8, S_SampleRate = 9, S_Bpm = 10, S_Onsets = 11, S_EnvHeld = 12, kTele = 16;
    private const int kHist = 2048, kRing = AutoFilterCurve.FftN, kScope = kTele + 2 * kHist + kRing;
    private const double OnsetLin = 0.063;   // −24 dBFS, the engine's onset threshold

    private static readonly string[] TypeNames = { "LP", "BP", "HP", "Notch" };
    private static readonly string[] WaveNames = { "Sine", "Tri", "Saw", "Sqr", "S&H" };
    private static readonly string[] DivNames = { "2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };
    private static readonly double[] DivBeats = { 8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625 };
    private static readonly double[] EnvWindows = { 600, 800, 1000, 1500, 2000 };
    private static readonly string[] NoteNames = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "Filter";

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
        void RefreshAll() { foreach (var a in readouts) a(); }
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;
        var envHist = new float[kHist];
        var cutHist = new float[kHist];

        // ---- units ----------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static double HzOf(double norm) => Exp(norm, 30, 18000);
        static double NormOf(double hz) => Math.Clamp(Math.Log(hz / 30.0) / Math.Log(18000.0 / 30.0), 0, 1);
        static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.00} k") : NotaNum.F($"{hz:0} Hz");
        static string HzLong(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.00} kHz") : NotaNum.F($"{hz:0} Hz");
        static string Pct(double v) => NotaNum.F($"{v * 100:0} %");
        static string Bip(double v) => NotaNum.F($"{(v - 0.5) * 200:+0;−0;0} %");
        static string Ms(double ms) => ms >= 100 ? NotaNum.F($"{ms:0} ms") : ms >= 10 ? NotaNum.F($"{ms:0.0} ms") : NotaNum.F($"{ms:0.0#} ms");
        static double Q(double res) => 0.5 + res * 14.5;
        static string GainF(double v) => NotaNum.F($"{(v - 0.5) * 48:+0.0;−0.0;0.0}");
        static string DbF(double lin) => lin > 1e-5 ? NotaNum.F($"{20 * Math.Log10(lin):0.0;−0.0} dB") : "−∞ dB";
        static double AttMs(double v) => Exp(v, 0.1, 500);
        static double RelMs(double v) => Exp(v, 1, 2000);
        static bool HoldInf(double v) => v >= 0.995;
        static double HoldMs(double v) => 400 * v * v;
        static string HoldF(double v) => HoldInf(v) ? "∞" : Ms(HoldMs(v));
        static double SmoothMs(double v) => 120 * v * v;
        static string SmoothF(double v) => v <= 0.001 ? "off" : Ms(SmoothMs(v));
        static string Deg(double v, double full) => NotaNum.F($"{v * full:0}°");
        string NoteOf(double hz)
        {
            double midi = 69 + 12 * Math.Log2(hz / 440.0);
            int n = (int)Math.Round(midi);
            int cents = (int)Math.Round((midi - n) * 100);
            return NotaNum.F($"{NoteNames[((n % 12) + 12) % 12]}{n / 12 - 1} {cents:+0;−0;0} ¢");
        }

        double Bpm() => Sc(S_Bpm) > 1 ? Sc(S_Bpm) : 120;
        bool Synced() => On(LfoSync);
        double LfoHz() => Synced() ? Bpm() / 60.0 / DivBeats[Sel(LfoRate, 8)] : Exp(P(LfoRate), 0.01, 40);
        string RateF(double v) => Synced() ? DivNames[Math.Clamp((int)Math.Round(v * 7), 0, 7)] : LfoHzF(Exp(v, 0.01, 40));
        static string LfoHzF(double hz) => hz >= 10 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz");
        int Target() => Sel(ModTarget, 3);   // 0 Freq, 1 Reso, 2 Both
        bool ToFreq() => Target() != 1;
        bool ToRes() => Target() != 0;
        string TargetName() => Target() switch { 0 => "Freq", 1 => "Reso", _ => "Freq + Reso" };
        double EnvBip() => On(EnvOn) ? (P(EnvAmt) - 0.5) * 2 : 0;
        double LfoDepth() => On(LfoOn) ? P(LfoAmt) : 0;
        bool EnvAssigned() => Math.Abs(EnvBip()) > 0.004;
        bool LfoAssigned() => LfoDepth() > 0.004;
        bool Assigned() => EnvAssigned() || LfoAssigned();
        double BaseHz() => HzOf(P(Freq));
        double LiveHz() => scN > S_Cut ? HzOf(Sc(S_Cut)) : BaseHz();
        // The cutoff range the settings allow (ENV one way, LFO both ways).
        (double Lo, double Hi) RangeOct()
        {
            if (!ToFreq()) return (0, 0);
            double e = EnvBip() * 6, l = LfoDepth() * 4;
            return (Math.Min(0, e) - l, Math.Max(0, e) + l);
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 0) => FxParts.Label(t, c);
        static TextBlock Mono(string t, double fs, IBrush c) { var tb = FxParts.Info(t, c); tb.TextTrimming = TextTrimming.None; return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, double cellW = 46)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = 34, Height = 34 };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            readouts.Add(() => { if (knob.Dragging) return; float c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(c); });
            return KnobCell(name, knob, val, cellW);
        }

        // On/off switch bound to a param (> 0.5 = on).
        Control Toggle(int p, string label, Func<string>? live = null, Func<bool>? dim = null)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); RefreshAll(); }, out var sync, dim, live);
            readouts.Add(sync);
            Learn(wrap, p);
            return wrap;
        }

        // Segmented pill over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names, bool fill = false)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); RefreshAll(); }, out var sync, fill: fill, padX: 6);
            readouts.Add(sync);
            Learn(seg, p);
            return seg;
        }

        // Outlined chips (the mockup's Type / Wave / target row): the chosen one = accent edge + wash.
        Control Chips(string[] names, Func<int, bool> lit, Action<int> pick, int learnParam, double h = 16, double padX = 7)
        {
            // Drawn like Segments (a groove, the lit cells raised), but more than one cell can be lit.
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var cells = new Border[names.Length];
            var texts = new TextBlock[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var tb = new TextBlock { Text = names[i], FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var b = new Border { Height = NotaSize.Seg, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; pick(iv); RefreshAll(); e.Handled = true; };
                cells[i] = b; texts[i] = tb; row.Children.Add(b);
            }
            readouts.Add(() =>
            {
                for (int i = 0; i < names.Length; i++)
                {
                    bool on = lit(i);
                    cells[i].Background = on ? NotaPalette.AccentSoft : Brushes.Transparent;
                    if (on) cells[i].BindResource(Border.BoxShadowProperty, "Shadow.Lifted"); else cells[i].BoxShadow = default;
                    texts[i].Foreground = on ? AccentBright : TextTertiary;
                }
            });
            var groove = new Border { Background = NotaPalette.Groove, CornerRadius = NotaRadius.Group, Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = row };
            groove.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
            Learn(groove, learnParam);
            return groove;
        }

        // A latching chip over a toggle param; its text can follow the value.
        Border Latch(int p, Func<string> text, string tip, bool fill = false)
        {
            // Raised at rest; on, the soft tint with orange text.
            var tb = new TextBlock { FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = NotaSize.Dropdown, Padding = new Thickness(9, 0), CornerRadius = NotaRadius.Field, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? AccentBright : TextPrimary;
                tb.Text = text();
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, Func<bool>? dim = null, double labW = 48)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => Raw(p, (float)v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), bipolar: bipolar, dim: dim, valueWidth: 42, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // Tab head row: a fixed 18px strip over the window.
        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 24, ColumnSpacing = 8 };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        // The knob row under a window: knobs on the left, two mono info lines on the right.
        Control KnobRow(Control[] knobs, Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 7, TextTertiary); i1.HorizontalAlignment = HorizontalAlignment.Right;
            var i2 = Mono("", 7, TextTertiary); i2.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 54 };
            g.Children.Add(Row(0, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2), Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(0, 6, 0, 0),
            Children = { Docked(head, Dock.Top), Docked(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 6, 0, 6), Child = window } },
        };

        // ======================================================================
        // LEFT — SHAPE column (cutoff · resonance)
        // ======================================================================
        var shapeFreq = Mono("", 7, TextPrimary); shapeFreq.HorizontalAlignment = HorizontalAlignment.Center;
        var shapeRes = Mono("", 7, TextPrimary); shapeRes.HorizontalAlignment = HorizontalAlignment.Center;
        Control ShapeFader(int p, string label, int liveSlot, string tip, TextBlock readout, Func<string> text)
        {
            var f = FxParts.Fader(engine.DeviceParamName(track, di, p), () => P(p), v => Raw(p, (float)v), text,
                () => Begin(p), () => End(p), () => Reset(p), readout, out var fSync, after: RefreshAll);
            Learn(f, p);
            ToolTip.SetTip(f, tip);
            readouts.Add(() => { fSync(); f.Marker = scN > liveSlot && Assigned() ? Sc(liveSlot) : double.NaN; });
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center; lb.Margin = new Thickness(0, 4, 0, 0);
            return new DockPanel { Children = { Docked(lb, Dock.Bottom), f } };
        }
        var shapeTitle = Caps("Shape"); shapeTitle.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            shapeFreq.Text = Hz(BaseHz());
            shapeRes.Text = Pct(P(Res));
            shapeTitle.Foreground = Assigned() && scN > 0 && Math.Abs(Sc(S_Cut) - P(Freq)) > 0.01 ? AccentBright : TextTertiary;
        });
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5) };
        faders.Children.Add(ShapeFader(Freq, "Frq", S_Cut, "Cutoff — the base the modulation moves from; the mark is where it is now. Double-click resets.", shapeFreq, () => Hz(BaseHz())));
        faders.Children.Add(Col(ShapeFader(Res, "Res", S_Res, "Resonance — the peak at the cutoff; the mark is where it is now. Double-click resets.", shapeRes, () => Pct(P(Res))), 1));
        var shapeCol = new RuleSection
        {
            Width = 64, Padding = new Thickness(0, 8, 0, 6),
            Child = new DockPanel { Children = { Docked(shapeTitle, Dock.Top), Docked(shapeRes, Dock.Bottom), Docked(shapeFreq, Dock.Bottom), faders } },
        };
        DockPanel.SetDock(shapeCol, Dock.Left);

        // ======================================================================
        // CENTRE — Filter
        // ======================================================================
        AutoFilterCurve? curve = null;
        Control FilterTab()
        {
            curve = new AutoFilterCurve();
            curve.GestureBegin += () => { Begin(Freq); Begin(Res); };
            curve.GestureEnd += () => { End(Freq); End(Res); };
            curve.CutoffChanged += v => Raw(Freq, (float)v);
            curve.ResChanged += v => Raw(Res, (float)v);
            ToolTip.SetTip(curve, "Drag sideways for the cutoff, up and down for the resonance. The grey band is how far the envelope and LFO can move the cutoff; the dashed curve is where they have it now.");
            Learn(curve, Freq);

            var q = Mono("", 7, TextTertiary);
            readouts.Add(() => q.Text = NotaNum.F($"Q {Q(P(Res)):0.00}"));
            var types = Chips(TypeNames, i => Sel(Type, 4) == i, i => SetP(Type, i / 3f), Type);
            var head = HeadRow(Row(8, Caps("Type"), types, Seg(Slope, new[] { "12", "24" }), Seg(Circuit, new[] { "Clean", "Analog" })), q);

            var knobs = KnobRow(new[]
            {
                K(Freq, "Freq", v => Hz(HzOf(v))),
                K(Res, "Reso", Pct),
                K(Morph, "Morph", Pct),
                K(Drive, "Drive", v => v <= 0.001 ? "off" : Pct(v)),
                K(DryWet, "Dry/wet", Pct),
            },
            () =>
            {
                if (!Assigned() || !ToFreq()) return NotaNum.F($"cutoff {Hz(BaseHz())} → no modulation");
                var (lo, hi) = RangeOct();
                return NotaNum.F($"cutoff {Hz(BaseHz())} → {Hz(Math.Min(18000, BaseHz() * Math.Pow(2, lo)))}…{Hz(Math.Min(18000, BaseHz() * Math.Pow(2, hi)))}");
            },
            () => NotaNum.F($"slope {(On(Slope) ? 24 : 12)} dB/oct · Q {Q(P(Res)):0.00}"));
            return TabBody(head, curve, knobs);
        }

        // ======================================================================
        // CENTRE — Envelope
        // ======================================================================
        AfEnvView? envView = null;
        double envWindowMs = 600;
        int envEvents = 0;
        double envPeakHz = 0;
        Control EnvelopeTab()
        {
            envView = new AfEnvView();
            envView.StartV = () => P(EnvAmt);
            envView.DragV += v => Raw(EnvAmt, (float)v);
            envView.GestureBegin += () => Begin(EnvAmt);
            envView.GestureEnd += () => End(EnvAmt);
            ToolTip.SetTip(envView, "The input's envelope (grey) and the cutoff it drives (neutral ink), the base cutoff dashed. Drag up or down for the amount.");
            Learn(envView, EnvAmt);

            // → Freq / → Reso: the shared Mod Target, as two switches that can both be lit.
            void PickTarget(int i)
            {
                bool f = ToFreq(), r = ToRes();
                if (i == 0) f = !f; else r = !r;
                if (!f && !r) return;   // one of them stays on
                SetP(ModTarget, f && r ? 1f : r ? 0.5f : 0f);
            }
            var target = Chips(new[] { "→ Freq", "→ Reso" }, i => i == 0 ? ToFreq() : ToRes(), PickTarget, ModTarget);
            // Up / Down: the sign of the bipolar amount.
            var dir = Segments(new[] { "Up", "Down" }, () => P(EnvAmt) >= 0.5f ? 0 : 1, iv =>
            {
                double mag = Math.Abs(P(EnvAmt) - 0.5);
                if (mag < 0.004) mag = 0.19;
                SetP(EnvAmt, (float)(iv == 0 ? 0.5 + mag : 0.5 - mag));
                RefreshAll();
            }, out var dirSync, padX: 6);
            readouts.Add(dirSync);
            Learn(dir, EnvAmt);
            var hold = Latch(EnvHold, () => NotaNum.F($"Hold {HoldF(P(EnvHoldTime))}"), "Hold — keep the envelope at its peak for the HOLD time before it releases (∞ freezes it)");
            var amt = Mono("", 7, TextPrimary);
            readouts.Add(() => { amt.Text = NotaNum.F($"amount {Bip(P(EnvAmt))}"); amt.Foreground = EnvAssigned() ? TextPrimary : TextTertiary; });
            var envSw = Toggle(EnvOn, "ENV");
            ToolTip.SetTip(envSw, "Envelope follower on / off");
            var head = HeadRow(Row(8, envSw, target, dir, hold), amt);

            var knobs = KnobRow(new[]
            {
                K(EnvAtt, "ATTACK", v => Ms(AttMs(v))),
                K(EnvRel, "RELEASE", v => Ms(RelMs(v))),
                K(EnvAmt, "AMOUNT", Bip),
                K(EnvHoldTime, "HOLD", HoldF),
            },
            () => ToFreq() ? NotaNum.F($"cutoff {Hz(BaseHz())} → {Hz(envPeakHz)}") : NotaNum.F($"reso {Pct(P(Res))} {Bip(P(EnvAmt))}"),
            () => NotaNum.F($"{envEvents} {(envEvents == 1 ? "event" : "events")} / window · threshold −24 dB"));
            return TabBody(head, envView, knobs);
        }
        void SyncEnvView()
        {
            double span = AttMs(P(EnvAtt)) + RelMs(P(EnvRel)) + (On(EnvHold) && !HoldInf(P(EnvHoldTime)) ? HoldMs(P(EnvHoldTime)) : 0);
            envWindowMs = EnvWindows[^1];
            foreach (var wv in EnvWindows) if (wv >= span * 2.5) { envWindowMs = wv; break; }
            int n = Math.Clamp((int)envWindowMs, 2, kHist);
            if (scN >= kTele + 2 * kHist)
            {
                Array.Copy(scope, kTele + kHist - n, envHist, 0, n);
                Array.Copy(scope, kTele + 2 * kHist - n, cutHist, 0, n);
            }
            else { Array.Clear(envHist); Array.Clear(cutHist); }
            envEvents = 0; double peak = 0; bool armed = true;
            for (int i = 0; i < n; i++)
            {
                if (armed && envHist[i] >= OnsetLin) { envEvents++; armed = false; }
                else if (!armed && envHist[i] < OnsetLin / 2) armed = true;
                peak = Math.Max(peak, cutHist[i]);
            }
            envPeakHz = scN >= kTele + 2 * kHist ? HzOf(peak) : BaseHz();
            envView?.Set(envHist, cutHist, n, P(Freq), NotaNum.F($"base {Hz(BaseHz())}"), envWindowMs, envPeakHz);
        }

        // ======================================================================
        // CENTRE — LFO
        // ======================================================================
        AfLfoView? lfoView = null;
        double LfoCycles() => Synced() ? 8.0 / DivBeats[Sel(LfoRate, 8)] : Math.Clamp(LfoHz() * 2, 1, 16);
        Control LfoTab()
        {
            lfoView = new AfLfoView();
            lfoView.StartV = () => P(LfoAmt);
            lfoView.StartH = () => P(LfoOffset);
            lfoView.DragV += v => Raw(LfoAmt, (float)v);
            lfoView.DragH += v => Raw(LfoOffset, (float)v);
            lfoView.GestureBegin += () => { Begin(LfoAmt); Begin(LfoOffset); };
            lfoView.GestureEnd += () => { End(LfoAmt); End(LfoOffset); };
            ToolTip.SetTip(lfoView, "The LFO's movement over the window — raw shape dashed, what the filter follows (after Smooth) in neutral ink, the right channel in a darker grey when stereo is on. Drag up or down for the amount, sideways for the start phase.");
            Learn(lfoView, LfoAmt);

            var waves = Chips(WaveNames, i => Sel(LfoWave, 5) == i, i => SetP(LfoWave, i / 4f), LfoWave, padX: 6);
            var rate = Mono("", 7, TextSecondary);
            readouts.Add(() => rate.Text = LfoHzF(LfoHz()));
            var lfoSw = Toggle(LfoOn, "LFO");
            ToolTip.SetTip(lfoSw, "LFO on / off");
            var head = HeadRow(Row(8, lfoSw, waves, Seg(LfoSync, new[] { "Free", "Sync" })), rate);

            var knobs = KnobRow(new[]
            {
                K(LfoRate, "RATE", RateF),
                K(LfoAmt, "AMOUNT", Pct),
                K(LfoMorph, "MORPH", Pct),
                K(LfoOffset, "PHASE", v => Deg(v, 360)),
            },
            () =>
            {
                if (!ToFreq()) return NotaNum.F($"reso {Pct(P(Res))} ±{Pct(LfoDepth() * 0.5)}");
                double b = BaseHz(), o = LfoDepth() * 4;
                return NotaNum.F($"cutoff {Hz(b)} ±{Pct(P(LfoAmt))} · {Hz(b * Math.Pow(2, -o))}…{Hz(Math.Min(18000, b * Math.Pow(2, o)))}");
            },
            () => Synced() ? NotaNum.F($"{DivNames[Sel(LfoRate, 8)]} at {Bpm():0} BPM = {LfoHzF(LfoHz())}") : NotaNum.F($"free · period {Ms(1000 / LfoHz())}"));
            return TabBody(head, lfoView, knobs);
        }
        void SyncLfoView()
        {
            if (lfoView is null) return;
            double cycles = LfoCycles();
            double hz = LfoHz();
            double smoothCycles = SmoothMs(P(ModSmooth)) / 1000.0 * hz;
            string mid, end;
            if (Synced()) { mid = "2"; end = "bar"; }
            else { double sec = cycles / hz; mid = NotaNum.Time(sec / 2); end = NotaNum.Time(sec); }
            string title = "LFO → " + (Target() switch { 0 => "Freq", 1 => "Reso", _ => "Freq + Reso" });
            double phase = scN > S_LfoPhase && On(LfoOn) && Sc(S_Cpu) > 0 ? Sc(S_LfoPhase) : double.NaN;
            lfoView.Set(On(LfoOn), Sel(LfoWave, 5), P(LfoMorph), P(LfoAmt), P(LfoOffset), P(LfoPhase) * 0.5, smoothCycles, cycles, phase,
                title, NotaNum.F($"±{Pct(P(LfoAmt))}"), P(LfoMorph) > 0.004 ? NotaNum.F($"morph {Pct(P(LfoMorph))}") : "", mid, end);
        }

        // ======================================================================
        // RIGHT — Mod / Output
        // ======================================================================
        int centreTab = 0;

        // Sidechain key: the switch picks a source (a flyout with the tracks and the key gain).
        string SrcName(int id) => TrackNames.Of(engine, id);
        Control SidechainSwitch()
        {
            Border? host = null;
            void ShowPicker()
            {
                var list = new StackPanel { Spacing = 1 };
                var fly = new Flyout();
                void Item(string name, int id)
                {
                    bool cur = engine.DeviceSidechainSource(track, di) == id;
                    var tb = new TextBlock { Text = name, FontSize = 10, Foreground = cur ? AccentBright : TextPrimary, FontWeight = cur ? FontWeight.SemiBold : FontWeight.Normal };
                    var b = new Border { Padding = new Thickness(8, 3), CornerRadius = NotaRadius.Cell, Background = cur ? NotaPalette.AccentSubtle : Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                    b.PointerPressed += (_, e) => { engine.SetDeviceSidechainSource(track, di, id); ctx.NotifyChanged(); RefreshAll(); fly.Hide(); e.Handled = true; };
                    list.Children.Add(b);
                }
                Item("None — this track's input", -1);
                for (int i = 0; i < engine.TrackCount; i++)
                    if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track) Item(SrcName(ti.Id), ti.Id);
                var gain = DeviceCardKit.SliderRow("KEY GAIN", () => (engine.DeviceSidechainGain(track, di) + 24) / 48.0,
                    v => engine.SetDeviceSidechainGain(track, di, (float)(v * 48 - 24)),
                    () => NotaNum.F($"{engine.DeviceSidechainGain(track, di):+0.0;−0.0;0.0} dB"), out _, bipolar: true,
                    reset: () => engine.SetDeviceSidechainGain(track, di, 0), valueWidth: 46);
                fly.Content = new StackPanel { Width = 200, Spacing = 6, Children = { Caps("Sidechain key — envelope source", null, 8), list, new Border { Height = 1, Background = BorderDef }, gain } };
                fly.ShowAt(host!);
            }
            var sw = Switch("", () => engine.DeviceSidechainSource(track, di) >= 0, () =>
            {
                if (engine.DeviceSidechainSource(track, di) >= 0) { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); RefreshAll(); }
                else ShowPicker();
            }, out var sync, liveLabel: () =>
            {
                int src = engine.DeviceSidechainSource(track, di);
                return src >= 0 ? "Key: " + SrcName(src) : "Sidechain key";
            });
            host = sw;
            readouts.Add(sync);
            // a right-click (or a click on the name while on) re-opens the picker
            sw.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Right) { ShowPicker(); e.Handled = true; } };
            ToolTip.SetTip(sw, "Sidechain key — the envelope follows another track instead of this one. Switch on to pick the source and the key gain; right-click to change it.");
            return sw;
        }

        Control ModTab()
        {
            var target = Chips(new[] { "Freq", "Reso", "Both" }, i => Target() == i, i => SetP(ModTarget, i / 2f), ModTarget);
            var tRow = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*") };
            tRow.Children.Add(Caps("Target")); tRow.Children.Add(Col(target, 1));

            var env = SliderRow("Env", EnvAmt, Bip, bipolar: true, dim: () => !On(EnvOn));
            var lfo = SliderRow("LFO", LfoAmt, Pct, modulation: true, dim: () => !On(LfoOn));
            var gain = SliderRow("Gain", Gain, GainF, bipolar: true);
            var smooth = SliderRow("Smooth", ModSmooth, SmoothF);
            var ctxHost = new ContentControl();
            readouts.Add(() => { Control want = centreTab == 0 ? gain : smooth; if (!ReferenceEquals(ctxHost.Content, want)) ctxHost.Content = want; });
            var ctxRow = ctxHost;

            // The cutoff, live: Hz · note · cents, and where the movement comes from.
            var boxTitle = Caps("Cutoff");
            var boxHz = Mono("", 9, TextPrimary); boxHz.HorizontalAlignment = HorizontalAlignment.Right;
            var boxSub = Mono("", 8, TextTertiary); boxSub.Height = 16; boxSub.TextTrimming = TextTrimming.CharacterEllipsis;
            var boxTop = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Height = 16, Children = { boxTitle, Col(boxHz, 1) } };
            var box = new Border
            {
                Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 4),
                Child = new StackPanel { Spacing = 1, Children = { boxTop, boxSub } },
            };
            readouts.Add(() =>
            {
                double live = LiveHz();
                bool moving = Assigned() && Math.Abs(NormOf(live) - P(Freq)) > 0.01;
                boxHz.Text = NotaNum.F($"{HzLong(live)} · {NoteOf(live)}");
                boxTitle.Foreground = moving ? AccentBright : TextTertiary;
                if (!Assigned()) boxSub.Text = "no modulation assigned";
                else
                {
                    var parts = new List<string> { NotaNum.F($"base {Hz(BaseHz())}") };
                    if (EnvAssigned()) parts.Add(NotaNum.F($"{Bip(P(EnvAmt))} ENV"));
                    if (LfoAssigned()) parts.Add(NotaNum.F($"±{Pct(P(LfoAmt))} LFO"));
                    boxSub.Text = string.Join(" · ", parts);
                }
            });

            // Two switches that follow the centre tab.
            var sc = SidechainSwitch();
            var analog = Toggle(Circuit, "Analog model");
            var envIn = Toggle(EnvOn, "Envelope on input", live: () => engine.DeviceSidechainSource(track, di) >= 0 ? "Envelope on key" : "Envelope on input");
            var lfoOnSw = Toggle(LfoOn, "LFO on");
            var retrig = Toggle(LfoRetrig, "Retrig on onsets");
            var sw1 = new ContentControl();
            var sw2 = new ContentControl();
            readouts.Add(() =>
            {
                (Control a, Control b) = centreTab switch { 1 => (envIn, sc), 2 => (lfoOnSw, retrig), _ => (sc, analog) };
                if (!ReferenceEquals(sw1.Content, a)) { sw1.Content = null; sw2.Content = null; sw1.Content = a; sw2.Content = b; }
            });

            box.Margin = new Thickness(0, 1, 0, 0);
            var grid = new Grid { RowDefinitions = new RowDefinitions("24,20,20,3,20,Auto,*"), Margin = new Thickness(0, 2, 0, 0) };
            grid.Children.Add(GRow(tRow, 0));
            grid.Children.Add(GRow(env, 1));
            grid.Children.Add(GRow(lfo, 2));
            grid.Children.Add(GRow(new Border { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center }, 3));
            grid.Children.Add(GRow(ctxRow, 4));
            grid.Children.Add(GRow(box, 5));
            grid.Children.Add(GRow(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { sw1, sw2 } }, 6));
            return grid;
        }

        Control OutputTab()
        {
            Control Line(string k, Func<string> v, IBrush? ink = null)
            {
                var val = Mono("", 8, ink ?? TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => val.Text = v());
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 16 };
                g.Children.Add(Caps(k));
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = new Border
            {
                Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 4),
                // One line: the tab is named Output, and the live cutoff is on the Mod tab's read-out.
                Child = Line("in · out", () => DbF(Sc(S_InPk)).Replace("\u2009dB", "").Replace(" dB", "") + " · " + DbF(Sc(S_OutPk))),
            };
            var sliders = new Grid { RowDefinitions = new RowDefinitions("20,20,20"), Margin = new Thickness(0, 4, 0, 0) };
            sliders.Children.Add(SliderRow("Drive", Drive, v => v <= 0.001 ? "off" : Pct(v)));
            sliders.Children.Add(GRow(SliderRow("Dry/wet", DryWet, Pct), 1));
            sliders.Children.Add(GRow(SliderRow("Gain", Gain, GainF, bipolar: true), 2));
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            btns.Children.Add(Latch(LfoRetrig, () => "Retrig", "Retrig — restart the LFO on each input onset (−24 dBFS) and when play starts", fill: true));
            btns.Children.Add(Col(Latch(Circuit, () => "Analog", "Analog — a gentle saturation after the filter, like an analog circuit", fill: true), 1));
            var tempo = Toggle(LfoSync, "Tempo sync");
            // Stereo phase: on sets 90°, a click on the value steps 45° → 180°.
            var stereo = Switch("", () => P(LfoPhase) > 0.001f, () => SetP(LfoPhase, P(LfoPhase) > 0.001f ? 0f : 0.5f), out var stSync,
                liveLabel: () => P(LfoPhase) > 0.001f ? NotaNum.F($"Stereo phase {P(LfoPhase) * 180:0}°") : "Stereo phase");
            readouts.Add(stSync);
            Learn(stereo, LfoPhase);
            stereo.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Right || P(LfoPhase) <= 0.001f) return;
                float nx = P(LfoPhase) + 0.25f; if (nx > 1.001f) nx = 0.25f;
                SetP(LfoPhase, nx); RefreshAll(); e.Handled = true;
            };
            ToolTip.SetTip(stereo, "Stereo phase — the right channel's LFO runs ahead of the left (90° when switched on; right-click steps 45° … 180°)");

            // 160 high: the read-out, three bars, the buttons, two switches.
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(box, Dock.Top),
                Docked(sliders, Dock.Top),
                Docked(new StackPanel { Spacing = 0, Children = { tempo, stereo } }, Dock.Bottom),
                Docked(new Border { Padding = new Thickness(0, 4, 0, 4), Child = btns }, Dock.Bottom) } };
            body.Margin = new Thickness(0, 6, 0, 0);
            return body;
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"window {(envWindowMs >= 1000 ? NotaNum.F($"{envWindowMs / 1000:0.#} s") : NotaNum.F($"{envWindowMs:0} ms"))} · {TypeNames[Sel(Type, 4)]} {(On(Slope) ? 24 : 12)}"),
                2 => Synced() ? NotaNum.F($"2 bars · sync {DivNames[Sel(LfoRate, 8)]} · phase {Deg(P(LfoOffset), 360)}")
                              : NotaNum.F($"free {LfoHzF(LfoHz())} · phase {Deg(P(LfoOffset), 360)}"),
                _ => NotaNum.F($"{TypeNames[Sel(Type, 4)]} · {(On(Slope) ? 24 : 12)} dB/oct · {(On(Circuit) ? "analog" : "clean")}"),
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => EnvelopeTab(), 2 => LfoTab(), _ => FilterTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : ModTab();

        var centre = FxParts.Tabs(new[] { "Filter", "Envelope", "LFO" }, centreHost, CentreBody, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = FxParts.Tabs(new[] { "Mod", "Output" }, rightHost, RightBody, _ => RefreshAll());
        var right = FxParts.Section(rightFrame); right.Width = 196;
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = FxParts.Section(centre); centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { shapeCol, right, centreBox } };
        var root = bodyRow;

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            if (centreTab == 0 && curve is not null)
            {
                if (!curve.Dragging) curve.Set(P(Freq), P(Res), Sel(Type, 4), P(Morph), On(Slope));
                var (lo, hi) = RangeOct();
                double b = P(Freq), oct = Math.Log2(18000.0 / 30.0);
                curve.SetLive(scN > S_Cut ? Sc(S_Cut) : b, scN > S_Res ? Sc(S_Res) : P(Res), b + lo / oct, b + hi / oct, Assigned() && ToFreq());
                curve.FeedSpectrum(scope, kTele + 2 * kHist, scN - (kTele + 2 * kHist), Sc(S_SampleRate));
            }
            if (centreTab == 1) SyncEnvView();
            if (centreTab == 2) SyncLfoView();
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
