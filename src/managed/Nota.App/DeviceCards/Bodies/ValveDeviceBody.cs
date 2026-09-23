// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Valve body (guitar amp, device kind 6), a build of the
// "Nota Valve" mockup (700 × 260) on the Vintage / Auto Filter / Lens frame: an always-
// visible STAGE column (gain · output faders), a centre panel with Amp / Cab / Harmonics
// tabs — the tone stack's response, the cabinet's response against on-axis, the harmonics
// the preamp adds — each with the model or cabinet list in its head row, a right panel with
// Cabinet / Output tabs, and a status strip. The responses and harmonics come from the
// engine (Amp.h scopeRead), so the picture is what the amp runs. Every control is a device
// param, so automation / MIDI learn / presets / A-B / persistence come for free.
// Params 0..13 are in raw units (Gain 0..10 …); the controls work on 0..1 and scale.
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

internal sealed class ValveDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Amp.h) ─────────────────────────────────
    private const int Model = 0, Gain = 1, Bass = 2, Middle = 3, Treble = 4, Presence = 5, Output = 6, Mix = 7,
        CabOn = 8, CabType = 9, Mic = 10, Axis = 11, Gate = 12, OS = 13, MidFreq = 14, MicDistance = 15, MicPosition = 16,
        LowCut = 17, HighCut = 18, EvenOnly = 19, AutoComp = 20, Bright = 21, Deep = 22, kParams = 23;
    // Scope layout (Amp::S_* / kTele / kResp).
    private const int S_InPk = 0, S_OutPk = 1, S_Cpu = 2, S_SampleRate = 3, S_Thd = 4, S_H1 = 5, S_TestLvl = 12, S_CompDb = 13,
        S_GateGain = 14, S_AliasDb = 15, S_CabLossDb = 19, S_MidHz = 20, S_RespLo = 24, S_RespHi = 25, S_RespN = 26,
        kTele = 32, kResp = 96;
    private const int kScope = kTele + 4 * kResp;
    private const int R_Tone = kTele, R_ToneFlat = kTele + kResp, R_Cab = kTele + 2 * kResp, R_CabRef = kTele + 3 * kResp;

    private static readonly string[] Models = { "Clean", "Boost", "Blues", "Rock", "Lead", "Heavy", "Bass" };
    private static readonly string[] Cabs = { "Match", "1×12 Open", "2×12 Combo", "4×12 Closed", "1×15 Bass" };
    private static readonly string[] CabShort = { "Match", "1×12", "2×12", "4×12", "1×15" };
    private static readonly string[] Mics = { "Dyn", "Cond", "Ribbon" };
    private static readonly string[] MicNames = { "dynamic", "condenser", "ribbon" };
    private static readonly string[] OsNames = { "Off", "2×", "4×", "8×" };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "AMP SIM";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        var mx = new float[kParams];
        for (int p = 0; p < kParams; p++) mx[p] = Math.Max(1e-6f, engine.DeviceParamMax(track, di, p));
        float P(int p) => engine.DeviceGetParam(track, di, p);
        double N(int p) => P(p) / mx[p];                                  // 0..1 whatever the unit
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, 0f, mx[p]));
        void RawN(int p, double v) => Raw(p, (float)(Math.Clamp(v, 0, 1) * mx[p]));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        double DefN(int p) => engine.DeviceParamDefault(track, di, p) / mx[p];
        bool On(int p) => P(p) >= 0.5f;
        int Idx(int p) => Math.Clamp((int)Math.Round(P(p)), 0, (int)Math.Round(mx[p]));          // raw index params
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);           // normalized choices

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        // ---- units ----------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Pct(double v) => NotaNum.F($"{v * 100:0} %");
        static string Ten(double v) => NotaNum.F($"{v:0.0}");
        static string HzF(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0#} k") : NotaNum.F($"{hz:0} Hz");
        static string OutDb(double v) => NotaNum.F($"{(v - 5) / 5 * 12:+0.0;−0.0;0.0} dB");
        static string DbF(double lin) => lin > 1e-5 ? NotaNum.F($"{20 * Math.Log10(lin):0.0;−0.0} dB") : "−∞ dB";
        static string DbfsF(double lin) => lin > 1e-5 ? NotaNum.F($"{20 * Math.Log10(lin):0.0;−0.0} dBFS") : "−∞ dBFS";
        static string ThdF(double v) => v < 0.001 ? "< 0.1 %" : NotaNum.F($"{v * 100:0.0} %");
        static string AxisF(double v) => v <= 0.005 ? "on" : NotaNum.F($"{v * 100:0} %");
        static string GateF(double v) => v <= 0.001 ? "off" : NotaNum.F($"−{75 - v * 55:0} dB");
        static string MidF(double v) => HzF(Exp(v, 200, 2000));
        static string DistF(double v) => NotaNum.F($"{Exp(v, 1, 30):0.#} cm");
        static string LowCutF(double v) => v <= 0.005 ? "off" : HzF(Exp(v, 20, 300));
        static string HighCutF(double v) => v >= 0.995 ? "off" : HzF(Exp(v, 2000, 20000));
        string ModelName() => Models[Idx(Model)];
        string OsText() => Sel(OS, 4) == 0 ? "no oversampling" : NotaNum.F($"{OsNames[Sel(OS, 4)]} OS");
        string AliasText() => Sc(S_AliasDb) <= -119 ? "aliasing < −120 dB" : NotaNum.F($"aliasing {Sc(S_AliasDb):0} dB");
        string AxisText() => P(Axis) <= 0.005f ? "on-axis" : NotaNum.F($"{P(Axis) * 100:0} % off-axis");
        string CabLine() => On(CabOn) ? NotaNum.F($"{CabShort[Idx(CabType)]} · {Mics[Idx(Mic)].ToLowerInvariant()} {AxisText()}") : "no cabinet";
        double H(int k) => Sc(S_H1 + k - 1);   // harmonic k, dB re the fundamental
        string Dominant()
        {
            double even = 0, odd = 0;
            for (int k = 2; k <= 7; k++) { double pw = Math.Pow(10, H(k) / 10); if (k % 2 == 0) even += pw; else odd += pw; }
            if (even + odd < 1e-9) return "clean";
            return even > odd * 1.5 ? "even dominant" : odd > even * 1.5 ? "odd dominant" : "even + odd";
        }

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
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow);
        // fmt gets the param's own value (raw units for 0..13).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, double cellW = 44)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(N(p), 1.0) { Accent = true, ArcColor = arc, Default = DefN(p), Width = 34, Height = 34 };
            knob.ValueChanged += v => { RawN(p, v); val.Text = fmt(P(p)); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            readouts.Add(() => { if (knob.Dragging) return; double c = N(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(P(p)); });
            return KnobCell(name, knob, val, cellW);
        }

        // On/off switch bound to a param (> 0.5 = on).
        Control Toggle(int p, string label, Func<string>? live = null)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); RefreshAll(); }, out var sync, null, live);
            readouts.Add(sync);
            Learn(wrap, p);
            return wrap;
        }

        // Segmented pill over a normalized discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); RefreshAll(); }, out var sync, padX: 6);
            readouts.Add(sync);
            Learn(seg, p);
            return seg;
        }

        // Outlined chips: lit = brass edge + wash.
        Control Chips(string[] names, Func<int, bool> lit, Action<int> pick, int learnParam, double h = 16, double padX = 7)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var cells = new Border[names.Length];
            var texts = new TextBlock[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var tb = new TextBlock { Text = names[i], FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var b = new Border { Height = h, Padding = new Thickness(padX, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; pick(iv); RefreshAll(); e.Handled = true; };
                cells[i] = b; texts[i] = tb; row.Children.Add(b);
            }
            readouts.Add(() =>
            {
                for (int i = 0; i < names.Length; i++)
                {
                    bool on = lit(i);
                    cells[i].BorderBrush = on ? Brass : NotaPalette.BorderStrong;
                    cells[i].Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent;
                    texts[i].Foreground = on ? AccentBright : TextSecondary;
                    texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            });
            if (learnParam >= 0) Learn(row, learnParam);
            return row;
        }
        Control IndexChips(int p, string[] names, double h = 16, double padX = 7)
            => Chips(names, i => Idx(p) == i, i => SetP(p, i), p, h, padX);

        // A latching chip: lit when `lit()`, a click runs `click`; its text can follow the value.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false)
        {
            var tb = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSubtle : fill ? Raised : Brushes.Transparent;
                b.BorderBrush = on ? Brass : fill ? Brushes.Transparent : NotaPalette.BorderStrong;
                tb.Foreground = on ? AccentBright : fill ? TextPrimary : TextSecondary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tb.Text = text();
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Border ToggleLatch(int p, Func<string> text, string tip, bool fill = false)
            => Latch(p, () => On(p), () => SetP(p, On(p) ? 0f : 1f), text, tip, fill);

        // Slider row: caps label · track · mono value (the track works on 0..1).
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, double labW = 40)
        {
            var bar = DeviceCardKit.SliderRow("", () => N(p), v => RawN(p, v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), bipolar: bipolar, valueWidth: 34, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // A list box: [name ▾] opens the entries; the wheel steps through them.
        Control Drop(int p, string[] names, string tip)
        {
            var name = new TextBlock { FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = AccentBright, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border
            {
                Height = 16, MinWidth = 74, Background = Sunken, BorderBrush = Brass, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 7) { Foreground = NotaPalette.AccentDim, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right), name } },
            };
            readouts.Add(() => name.Text = names[Idx(p)]);
            ToolTip.SetTip(box, tip);
            Learn(box, p);
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                int cur = Idx(p);
                for (int i = 0; i < names.Length; i++)
                {
                    int iv = i;
                    var mi = new MenuItem { Header = names[i] };
                    if (i == cur) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brass };
                    mi.Click += (_, _) => { SetP(p, iv); RefreshAll(); };
                    fly.Items.Add(mi);
                }
                fly.ShowAt(box);
                e.Handled = true;
            };
            box.PointerWheelChanged += (_, e) =>
            {
                int m = Math.Clamp(Idx(p) + (e.Delta.Y < 0 ? 1 : -1), 0, names.Length - 1);
                SetP(p, m); RefreshAll(); e.Handled = true;
            };
            return box;
        }
        Control ModelDrop() => Drop(Model, Models, "Model — the amp's voicing: how hard the preamp drives, how many stages clip, how tight the low end is and which cabinet it matches. Scroll to step.");
        Control CabDrop() => Drop(CabType, Cabs, "Cabinet — Match follows the model; the others bring their own low resonance and presence peak. Scroll to step.");

        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 18, ColumnSpacing = 6 };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control KnobRow(Control[] knobs, Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 7, TextTertiary); i1.HorizontalAlignment = HorizontalAlignment.Right;
            var i2 = Mono("", 7, TextTertiary); i2.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 52 };
            g.Children.Add(Row(4, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(8, 5, 8, 0),
            Children = { Docked(head, Dock.Top), Docked(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 5, 0, 0), Child = window } },
        };

        // ======================================================================
        // LEFT — STAGE column (gain · output)
        // ======================================================================
        Control StageFader(int p, string label, IBrush ink, string tip)
        {
            var f = new AfFader { Ink = ink, Width = 16, VerticalAlignment = VerticalAlignment.Stretch, Default = DefN(p) };
            f.Changed += v => { RawN(p, v); RefreshAll(); };
            f.GestureBegin += () => Begin(p);
            f.GestureEnd += () => End(p);
            Learn(f, p);
            ToolTip.SetTip(f, tip);
            readouts.Add(() => f.Set(N(p), double.NaN));
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center;
            return new DockPanel { Children = { Docked(lb, Dock.Bottom), new Border { Margin = new Thickness(0, 0, 0, 3), Child = f } } };
        }
        var stageTitle = Caps("STAGE"); stageTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var stageGain = Mono("", 7, AccentBright); stageGain.HorizontalAlignment = HorizontalAlignment.Center;
        var stageOut = Mono("", 7, TextPrimary); stageOut.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            stageGain.Text = Ten(P(Gain));
            stageOut.Text = OutDb(P(Output));
            stageTitle.Foreground = Sc(S_InPk) > 0.01 ? AccentBright : TextTertiary;   // lit while signal passes
        });
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        faders.Children.Add(StageFader(Gain, "GAIN", Brass, "Gain — how hard the signal hits the preamp. Double-click resets."));
        faders.Children.Add(Col(StageFader(Output, "OUT", Teal, "Output — the master level after the cabinet, ±12 dB. Double-click resets."), 1));
        var stageCol = new Border
        {
            Width = 56, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(stageTitle, Dock.Top), Docked(stageOut, Dock.Bottom), Docked(stageGain, Dock.Bottom), faders } },
        };
        DockPanel.SetDock(stageCol, Dock.Left);

        // ======================================================================
        // CENTRE — Amp (tone stack)
        // ======================================================================
        VlResponseView? toneView = null;
        Control AmpTab()
        {
            toneView = new VlResponseView { LoHz = 60, HiHz = 9000, TopDb = 15, BottomDb = -15, Ticks = new double[] { 80, 400, 1500, 6000 } };
            toneView.StartV = () => N(Middle);
            toneView.StartH = () => P(MidFreq);
            toneView.DragV += v => RawN(Middle, v);
            toneView.DragH += v => Raw(MidFreq, (float)v);
            toneView.GestureBegin += () => { Begin(Middle); Begin(MidFreq); };
            toneView.GestureEnd += () => { End(Middle); End(MidFreq); };
            ToolTip.SetTip(toneView, "The tone stack before the cabinet — the pre-distortion tightness of the model, Bright, Bass / Middle / Treble / Presence and Deep; dashed is the stack flat. Drag sideways for the middle's frequency, up or down for the middle.");
            Learn(toneView, Middle);

            var thd = Mono("", 7, TextTertiary);
            readouts.Add(() => thd.Text = "THD " + ThdF(Sc(S_Thd)));
            var count = Mono("", 7, TextTertiary);
            readouts.Add(() => count.Text = NotaNum.F($"{Idx(Model) + 1} / {Models.Length}"));
            var head = HeadRow(Row(6, Caps("MODEL"), ModelDrop(), count, new Border { Width = 4 }, Caps("OS"), Seg(OS, OsNames)), thd);
            var knobs = KnobRow(new[]
            {
                K(Bass, "BASS", Ten),
                K(Middle, "MIDDLE", Ten),
                K(MidFreq, "MID FREQ", MidF),
                K(Treble, "TREBLE", Ten),
                K(Presence, "PRESENCE", Ten),
            },
            () => NotaNum.F($"peak {DbF(Sc(S_InPk))} → {DbF(Sc(S_OutPk))} · mix {Pct(P(Mix))}"),
            () => NotaNum.F($"{OsText()} · {AliasText()}"));
            return TabBody(head, toneView, knobs);
        }

        // ======================================================================
        // CENTRE — Cab (cabinet response)
        // ======================================================================
        VlResponseView? cabView = null;
        Control CabTab()
        {
            cabView = new VlResponseView { LoHz = 50, HiHz = 14000, TopDb = 10, BottomDb = -30, Ticks = new double[] { 60, 800, 4000, 12000 } };
            cabView.StartH = () => P(Axis);
            cabView.StartV = () => 1 - P(MicDistance);
            cabView.DragH += v => Raw(Axis, (float)v);
            cabView.DragV += v => Raw(MicDistance, (float)(1 - v));
            cabView.GestureBegin += () => { Begin(Axis); Begin(MicDistance); };
            cabView.GestureEnd += () => { End(Axis); End(MicDistance); };
            ToolTip.SetTip(cabView, "The cabinet as the mic hears it, with the low / high cuts — dashed is the same cab and mic on axis at the cap, 5 cm away. Drag sideways to move the mic off axis, up to bring it closer.");
            Learn(cabView, Axis);

            var loss = Mono("", 7, AccentBright);
            readouts.Add(() => loss.Text = On(CabOn) ? NotaNum.F($"{Sc(S_CabLossDb):+0.0;−0.0;0.0} dB @ 4 k") : "cabinet off");
            var mics = IndexChips(Mic, Mics);
            ToolTip.SetTip(mics, "Mic — Dynamic: the classic close mic; Condenser: brighter, less proximity bass; Ribbon: dark, the most proximity bass");
            var pos = Seg(MicPosition, new[] { "Cap", "Edge" });
            ToolTip.SetTip(pos, "Mic position — at the cone's cap (bright) or its edge (darker)");
            var head = HeadRow(Row(6, Caps("CAB"), CabDrop(), mics, pos), loss);
            var knobs = KnobRow(new[]
            {
                K(MicDistance, "DISTANCE", DistF),
                K(Axis, "AXIS", AxisF),
                K(LowCut, "LOW CUT", LowCutF, Teal),
                K(HighCut, "HIGH CUT", HighCutF, Teal),
            },
            () => On(CabOn) ? NotaNum.F($"{AxisText()} → {Sc(S_CabLossDb):+0.0;−0.0;0.0} dB at 4 kHz") : "cabinet off · cuts only",
            () => "cab model · minimum phase · latency 0");
            return TabBody(head, cabView, knobs);
        }

        // ======================================================================
        // CENTRE — Harmonics
        // ======================================================================
        VtHarmView? harm = null;
        Control HarmTab()
        {
            harm = new VtHarmView();
            harm.StartV = () => N(Gain);
            harm.DragV += v => RawN(Gain, v);
            harm.GestureBegin += () => Begin(Gain);
            harm.GestureEnd += () => End(Gain);
            ToolTip.SetTip(harm, "The harmonics the preamp adds at the input's level (the fundamental at the top). Drag up or down for the gain.");
            Learn(harm, Gain);

            var peak = Mono("", 7, AccentBright);
            readouts.Add(() => peak.Text = DbfsF(Sc(S_OutPk)));
            var even = ToggleLatch(EvenOnly, () => "Even only", "Even only — the preamp adds even harmonics only: warmth without the fizz of the odd ones");
            var head = HeadRow(Row(6, Caps("MODEL"), ModelDrop(), new Border { Width = 4 }, Caps("OS"), Seg(OS, OsNames), even), peak);
            var knobs = KnobRow(new[]
            {
                K(Gain, "GAIN", Ten),
                K(Mix, "MIX", Pct),
                K(Gate, "GATE", GateF, Teal),
                K(Output, "OUTPUT", OutDb),
            },
            () => H(2) > -100 || H(3) > -100 ? NotaNum.F($"2nd {H(2):0} dB · 3rd {H(3):0} dB · {Dominant()}") : "no harmonics — clean",
            () => NotaNum.F($"{OsText()} · {AliasText()}"));
            return TabBody(head, harm, knobs);
        }

        // ======================================================================
        // RIGHT — Cabinet / Output
        // ======================================================================
        int centreTab = 0;
        Control Box(Func<string> title, Func<string> line1, Func<string> line2, Func<bool>? lit = null)
        {
            var t = Caps("");
            var l1 = Mono("", 9, AccentBright);
            var l2 = Mono("", 8, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var b = new Border { Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = { t, l1, l2 } } };
            readouts.Add(() =>
            {
                t.Text = title(); l1.Text = line1(); l2.Text = line2();
                bool on = lit?.Invoke() ?? false;
                b.BorderBrush = on ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
                t.Foreground = on ? AccentBright : TextTertiary;
            });
            return b;
        }
        static Control LabelRow(string label, Control c, double labW = 40)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")) };
            g.Children.Add(new TextBlock { Text = label, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(Col(c, 1));
            return g;
        }
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(8, 6), Child = g };
        }

        // The gate switch is the Gate amount: off = 0, on = the last setting (or −59 dB).
        float lastGate = P(Gate) > 0.001f ? P(Gate) : 0.3f;
        Control GateToggle()
        {
            var wrap = Switch("Noise gate", () => P(Gate) > 0.001f, () =>
            {
                if (P(Gate) > 0.001f) { lastGate = P(Gate); SetP(Gate, 0f); } else SetP(Gate, lastGate);
                RefreshAll();
            }, out var sync, null, () => P(Gate) > 0.001f ? "Gate " + GateF(P(Gate)) + (Sc(S_GateGain) > 0.5 ? " · open" : " · shut") : "Noise gate");
            readouts.Add(sync);
            Learn(wrap, Gate);
            return wrap;
        }

        Control CabinetTab()
        {
            var cabs = IndexChips(CabType, CabShort, h: 15, padX: 3);
            ToolTip.SetTip(cabs, "Cabinet — Match follows the model; 1×12 open, 2×12 combo, 4×12 closed, 1×15 bass");
            var mics = IndexChips(Mic, Mics, h: 15, padX: 6);
            return Spread(
                LabelRow("CAB", cabs, 26),
                LabelRow("MIC", mics, 26),
                SliderRow("AXIS", Axis, AxisF, labW: 26),
                SliderRow("GATE", Gate, GateF, modulation: true, labW: 26),
                Box(() => centreTab == 1 ? "CABINET" : "MODEL",
                    () => centreTab == 1 ? NotaNum.F($"{Cabs[Idx(CabType)]} · {Mics[Idx(Mic)]}") : NotaNum.F($"{ModelName()} · gain {Ten(P(Gain))}"),
                    () => centreTab == 1
                        ? (On(CabOn) ? NotaNum.F($"{DistF(P(MicDistance))} · {AxisText()} · {(On(MicPosition) ? "edge" : "cap")}") : "cabinet off")
                        : (On(CabOn) ? (Idx(CabType) == 0 ? "cab by model · " : CabShort[Idx(CabType)] + " · ") + NotaNum.F($"{Mics[Idx(Mic)].ToLowerInvariant()} {AxisText()}") : "no cabinet"),
                    () => centreTab == 1 && On(CabOn)),
                new StackPanel { Spacing = 5, Children = {
                    Toggle(CabOn, "Cabinet sim"),
                    GateToggle() } });
        }

        Control OutTab()
        {
            Control Line(string k, Func<string> v, IBrush? ink = null)
            {
                var val = Mono("", 8, ink ?? TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => val.Text = v());
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(new TextBlock { Text = k, FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = new Border
            {
                Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = {
                    Caps("OUTPUT"),
                    Line("in", () => DbF(Sc(S_InPk))),
                    Line("out", () => DbF(Sc(S_OutPk))),
                    Line("THD", () => ThdF(Sc(S_Thd)), AccentBright) } },
            };
            var sliders = new StackPanel { Spacing = 6, Children = {
                SliderRow("MIX", Mix, Pct),
                SliderRow("GATE", Gate, GateF, modulation: true),
                SliderRow("OUTPUT", Output, OutDb, bipolar: true) } };
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            btns.Children.Add(ToggleLatch(Bright, () => "Bright", "Bright — a treble lift before the preamp, strongest at low gain (the bright cap on a volume pot)", fill: true));
            btns.Children.Add(Col(ToggleLatch(Deep, () => "Deep", "Deep — a low resonance boost after the tone stack, for a fuller, tighter bottom", fill: true), 1));
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(box, Dock.Top),
                Docked(new Border { Margin = new Thickness(0, 7, 0, 0), Child = sliders }, Dock.Top),
                Docked(new StackPanel { Spacing = 5, Children = { Toggle(AutoComp, "Auto-compensation", () => On(AutoComp) ? NotaNum.F($"Auto-comp {Sc(S_CompDb):+0.0;−0.0;0.0} dB") : "Auto-compensation"), Toggle(EvenOnly, "Even harmonics only") } }, Dock.Bottom),
                Docked(new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 5), Child = btns }, Dock.Bottom) } };
            return new Border { Padding = new Thickness(8, 6), Child = body };
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
            string m = ModelName().ToLowerInvariant();
            extras.Text = centreTab switch
            {
                1 => On(CabOn) ? NotaNum.F($"{Cabs[Idx(CabType)].ToLowerInvariant()} · {MicNames[Idx(Mic)]} · {AxisText()}") : "cabinet off",
                2 => NotaNum.F($"{m} · {(Sel(OS, 4) == 0 ? "no OS" : OsNames[Sel(OS, 4)] + " OS")} · 2nd…7th"),
                _ => NotaNum.F($"tone stack before the cab · {m}"),
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => CabTab(), 2 => HarmTab(), _ => AmpTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutTab() : CabinetTab();

        var centre = TabFrame(new[] { "Amp", "Cab", "Harmonics" }, centreHost, CentreBody, false, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = TabFrame(new[] { "Cabinet", "Output" }, rightHost, RightBody, true, _ => RefreshAll(), null);
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
            var parts = new List<string> { ModelName(), "gain " + Ten(P(Gain)) };
            switch (centreTab)
            {
                case 1:
                    parts.Add(On(CabOn) ? CabShort[Idx(CabType)] : "no cab");
                    if (On(CabOn))
                    {
                        parts.Add(NotaNum.F($"{Mics[Idx(Mic)]} {DistF(P(MicDistance))}"));
                        parts.Add("axis " + AxisF(P(Axis)));
                        if (On(MicPosition)) parts.Add("edge");
                    }
                    if (P(LowCut) > 0.005f || P(HighCut) < 0.995f)
                        parts.Add(NotaNum.F($"{(P(LowCut) > 0.005f ? LowCutF(P(LowCut)) : "open")}…{(P(HighCut) < 0.995f ? HighCutF(P(HighCut)) : "open")}"));
                    if (P(Gate) > 0.001f) parts.Add("gate " + GateF(P(Gate)));
                    break;
                case 2:
                    parts.Add("THD " + ThdF(Sc(S_Thd)));
                    parts.Add("mix " + Pct(P(Mix)));
                    if (P(Gate) > 0.001f) parts.Add("gate " + GateF(P(Gate)));
                    parts.Add("out " + OutDb(P(Output)));
                    if (On(EvenOnly)) parts.Add("even only");
                    parts.Add(Sel(OS, 4) == 0 ? "OS off" : OsNames[Sel(OS, 4)] + " OS");
                    break;
                default:
                    parts.Add(NotaNum.F($"{Ten(P(Bass))} / {Ten(P(Middle))} / {Ten(P(Treble))} / {Ten(P(Presence))}"));
                    parts.Add("mid " + MidF(P(MidFreq)));
                    if (On(Bright)) parts.Add("bright");
                    if (On(Deep)) parts.Add("deep");
                    parts.Add(CabLine());
                    parts.Add("mix " + Pct(P(Mix)));
                    if (On(AutoComp)) parts.Add("auto-comp");
                    break;
            }
            return string.Join(" · ", parts);
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            double sr = Sc(S_SampleRate);
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · latency 0 smp · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { stageCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            int n = scN >= kScope ? kResp : 0;
            double lo = Sc(S_RespLo), hi = Sc(S_RespHi);
            if (centreTab == 0 && toneView is not null && !toneView.Dragging)
                toneView.Set(scope, R_Tone, R_ToneFlat, n, lo, hi, Sc(S_MidHz) > 0 ? Sc(S_MidHz) : Exp(P(MidFreq), 200, 2000), "Tone stack", "flat");
            if (centreTab == 1 && cabView is not null && !cabView.Dragging)
                cabView.Set(scope, R_Cab, R_CabRef, n, lo, hi, On(CabOn) ? 4000 : double.NaN, "Cabinet response", On(CabOn) ? "on-axis" : "cabinet off");
            if (centreTab == 2 && harm is not null)
                harm.Set(scope, S_H1, -200, "THD " + ThdF(Sc(S_Thd)), "");
            RefreshAll();
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
