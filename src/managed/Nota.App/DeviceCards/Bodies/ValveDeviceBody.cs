// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Valve body (guitar amp, device kind 6), a build of the
// "Nota Valve" mockup (700 × 260) on the Vintage / Auto Filter / Lens frame: a centre panel
// with Amp / Cab / Harmonics tabs, laid out the #531 way (CardLayout) — the tone stack's
// response (Middle and Mid freq beside it, the model and OS under it), the cabinet's response
// against on-axis (Distance and Axis beside it, the cab, mic and position under it), the
// harmonics the preamp adds (drag for Gain) — and a right panel with no tabs of its own that
// follows them: Tone (Bass, Treble, Presence, Bright, Deep, the model box), Cabinet (on, the
// cuts, the cabinet box), Output (auto-comp, the measurements box, the gate) — over Gain ·
// Dry/wet · Output. S is the tone stack over the model and Gain, Bass, Treble, Dry/wet. The
// responses and harmonics come from the engine (Amp.h scopeRead), so the picture is what the
// amp runs. Every control is a device param, so automation / MIDI learn / presets / A-B /
// persistence come for free.
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
using Nota.Application.Localization;

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
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
    public bool FullBleed => true;
    public string? Subtitle => "Amp sim";

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
        string OsText() => Sel(OS, 4) == 0 ? Loc.T("device.valve.no_oversampling") : NotaNum.F($"{OsNames[Sel(OS, 4)]} OS");
        string AliasText() => Sc(S_AliasDb) <= -119 ? "aliasing < −120 dB" : NotaNum.F($"aliasing {Sc(S_AliasDb):0} dB");
        string AxisText() => P(Axis) <= 0.005f ? "on-axis" : NotaNum.F($"{P(Axis) * 100:0} % off-axis");
        double H(int k) => Sc(S_H1 + k - 1);   // harmonic k, dB re the fundamental
        string Dominant()
        {
            double even = 0, odd = 0;
            for (int k = 2; k <= 7; k++) { double pw = Math.Pow(10, H(k) / 10); if (k % 2 == 0) even += pw; else odd += pw; }
            if (even + odd < 1e-9) return "clean";
            return even > odd * 1.5 ? "even dominant" : odd > even * 1.5 ? "odd dominant" : "even + odd";
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        // An acronym NotaText.Label does not know (OS, GR) keeps its case: the label as written.
        static TextBlock Acr(string t, IBrush? c = null) { var tb = Caps(t, c); tb.Text = t; return tb; }
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow);
        // fmt gets the param's own value (raw units for 0..13).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, double cellW = 44, double size = 34, bool bipolar = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(N(p), 1.0) { Accent = true, ArcColor = arc, Bipolar = bipolar, Default = DefN(p), Width = size, Height = size };
            knob.Format = v => fmt(v * mx[p]);   // read-out at any value: typing searches it
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
                var b = new Border { Height = h, Padding = new Thickness(padX, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
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
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
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
            var name = new TextBlock { FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border
            {
                Height = 16, MinWidth = 74, Background = Sunken, BorderBrush = NotaPalette.BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 7) { Foreground = TextTertiary, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right), name } },
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
                    mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = i == cur;
                    mi.Click += (_, _) => { SetP(p, iv); RefreshAll(); };
                    fly.Items.Add(mi);
                }
                fly.ShowAt(box);
                e.Handled = true;
            };
            box.PointerWheelChanged += (wt, e) =>
            {
                if (WheelInput.Step(e, wt) is not { } s) return;
                e.Handled = true; if (s == 0) return;
                int m = Math.Clamp(Idx(p) - s, 0, names.Length - 1);
                SetP(p, m); RefreshAll();
            };
            return box;
        }
        Control ModelDrop() => Drop(Model, Models, Loc.T("device.valve.model_the_amps_voicing_how"));
        Control CabDrop() => Drop(CabType, Cabs, Loc.T("device.valve.cabinet_match_follows_the_model_2"));

        int centreTab = 0;
        Control Box(Func<string> title, Func<string> line1, Func<string> line2)
        {
            var t = Caps("");
            var l1 = Mono("", 9, TextPrimary);
            var l2 = Mono("", 8, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var b = CardLayout.ReadoutBox(new StackPanel { Spacing = 2, Children = { t, l1, l2 } });
            readouts.Add(() => { t.Text = NotaText.Label(title()); l1.Text = line1(); l2.Text = line2(); });
            return b;
        }
        // The gate switch is the Gate amount: off = 0, on = the last setting (or −59 dB).
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
        Control OsSeg() { var s = Seg(OS, OsNames); ToolTip.SetTip(s, Loc.T("device.valve.no_oversampling")); return s; }

        // ======================================================================
        // CENTRE — Amp (the tone stack: Middle · Mid freq beside it, the model and OS under it)
        // ======================================================================
        VlResponseView? toneView = null;
        VlResponseView NewTone()
        {
            toneView = new VlResponseView { LoHz = 60, HiHz = 9000, TopDb = 15, BottomDb = -15, Ticks = new double[] { 80, 400, 1500, 6000 } };
            toneView.StartV = () => N(Middle);
            toneView.StartH = () => P(MidFreq);
            toneView.DragV += v => RawN(Middle, v);
            toneView.DragH += v => Raw(MidFreq, (float)v);
            toneView.GestureBegin += () => { Begin(Middle); Begin(MidFreq); };
            toneView.GestureEnd += () => { End(Middle); End(MidFreq); };
            ToolTip.SetTip(toneView, Loc.T("device.valve.the_tone_stack_before_the"));
            Learn(toneView, Middle);
            return toneView;
        }
        Control AmpTab() => CardLayout.View(NewTone(),
            CardLayout.KnobColumn(K(Middle, "MIDDLE", Ten, cellW: 50), K(MidFreq, "MID FREQ", MidF, cellW: 50)),
            CardLayout.Line(6, ModelDrop(), Acr("OS"), OsSeg()));

        // ======================================================================
        // CENTRE — Cab (the cabinet: Distance · Axis beside it, the cab, mic and position under it)
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
            ToolTip.SetTip(cabView, Loc.T("device.valve.the_cabinet_as_the_mic"));
            Learn(cabView, Axis);
            var mics = IndexChips(Mic, Mics);
            ToolTip.SetTip(mics, Loc.T("device.valve.mic_dynamic_the_classic_close"));
            var pos = Seg(MicPosition, new[] { "Cap", "Edge" });
            ToolTip.SetTip(pos, Loc.T("device.valve.mic_position_at_the_cones"));
            return CardLayout.View(cabView,
                CardLayout.KnobColumn(K(MicDistance, "DISTANCE", DistF, cellW: 50), K(Axis, "AXIS", AxisF, cellW: 50)),
                CardLayout.Line(6, CabDrop(), mics, pos));
        }

        // ======================================================================
        // CENTRE — Harmonics (drag for Gain; Even only under it)
        // ======================================================================
        VtHarmView? harm = null;
        Control HarmTab()
        {
            harm = new VtHarmView();
            harm.StartV = () => N(Gain);
            harm.DragV += v => RawN(Gain, v);
            harm.GestureBegin += () => Begin(Gain);
            harm.GestureEnd += () => End(Gain);
            ToolTip.SetTip(harm, Loc.T("device.valve.the_harmonics_the_preamp_adds"));
            Learn(harm, Gain);
            return CardLayout.View(harm, null, ToggleLatch(EvenOnly, () => "Even only", Loc.T("device.valve.even_only_the_preamp_adds")));
        }

        // ======================================================================
        // RIGHT — follows the centre tab: Tone (Amp) · Cabinet (Cab) · Output (Harmonics)
        // ======================================================================
        (Control, Control) TonePanel()
        {
            var knobs = new UniformGrid { Rows = 1, Children = { K(Bass, "BASS", Ten, cellW: 50), K(Treble, "TREBLE", Ten, cellW: 50), K(Presence, "PRESENCE", Ten, cellW: 52) } };
            var btns = CardLayout.Pair(
                ToggleLatch(Bright, () => "Bright", Loc.T("device.valve.bright_a_treble_lift_before"), fill: true),
                ToggleLatch(Deep, () => "Deep", Loc.T("device.valve.deep_a_low_resonance_boost"), fill: true));
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.valve.tone"))), CardLayout.Rows(
                new Border { Background = Brushes.Transparent, Child = knobs },   // keeps the knobs' height
                btns,
                CardLayout.Rule(),
                Box(() => "MODEL", () => NotaNum.F($"{ModelName()} · gain {Ten(P(Gain))} · {Idx(Model) + 1} / {Models.Length}"),
                    () => NotaNum.F($"{OsText()} · {AliasText()}"))));
        }

        (Control, Control) CabinetPanel() => (CardLayout.Head(Toggle(CabOn, "Cabinet sim")), CardLayout.Rows(
            SliderRow("LOW CUT", LowCut, LowCutF, modulation: true, labW: 46),
            SliderRow("HIGH CUT", HighCut, HighCutF, modulation: true, labW: 46),
            CardLayout.Rule(),
            Box(() => "CABINET", () => NotaNum.F($"{Cabs[Idx(CabType)]} · {Mics[Idx(Mic)]}"),
                () => On(CabOn) ? NotaNum.F($"{DistF(P(MicDistance))} · {AxisText()} · {(On(MicPosition) ? "edge" : "cap")} · {Sc(S_CabLossDb):+0.0;−0.0;0.0} dB @ 4 k") : "cabinet off · cuts only")));

        (Control, Control) OutputPanel()
        {
            Control Line(string k, Func<string> v)
            {
                var val = Mono("", 8, TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => val.Text = v());
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(new TextBlock { Text = k, FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = CardLayout.ReadoutBox(new StackPanel { Spacing = 2, Children = {
                Caps("OUTPUT"),
                Line("in", () => DbF(Sc(S_InPk))),
                Line("out", () => DbF(Sc(S_OutPk))),
                Line("THD", () => ThdF(Sc(S_Thd))) } });
            return (CardLayout.Head(Toggle(AutoComp, "Auto-compensation", () => On(AutoComp) ? NotaNum.F($"Auto-comp {Sc(S_CompDb):+0.0;−0.0;0.0} dB") : "Auto-compensation")),
                CardLayout.Rows(box, CardLayout.Rule(), GateToggle(), SliderRow("GATE", Gate, GateF, modulation: true)));
        }

        // ======================================================================
        // Frames: L (tabs, the panel that follows them, Gain · Dry/wet · Output under it) · S (the
        // tone stack over the model and Gain · Bass · Treble · Dry/wet)
        // ======================================================================
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            string m = ModelName().ToLowerInvariant();
            extras.Text = centreTab switch
            {
                1 => On(CabOn) ? NotaNum.F($"{Cabs[Idx(CabType)].ToLowerInvariant()} · {MicNames[Idx(Mic)]} · {AxisText()}") : "cabinet off",
                2 => (H(2) > -100 || H(3) > -100 ? NotaNum.F($"2nd {H(2):0} dB · 3rd {H(3):0} dB · {Dominant()}") : "no harmonics — clean") + " · " + DbfsF(Sc(S_OutPk)),
                _ => Loc.F("device.valve.tone_stack_before_the_cab", m) + NotaNum.F($" · THD {ThdF(Sc(S_Thd))} · peak {DbF(Sc(S_InPk))} → {DbF(Sc(S_OutPk))}"),
            };
        });
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(NewTone(), ModelDrop(),
                K(Gain, "GAIN", Ten, cellW: 56), K(Bass, "BASS", Ten, cellW: 56), K(Treble, "TREBLE", Ten, cellW: 56), K(Mix, "DRY/WET", Pct, cellW: 56))
            : CardLayout.Large(new[] { "Amp", "Cab", "Harmonics" }, 0,
                t => t switch { 1 => CabTab(), 2 => HarmTab(), _ => AmpTab() },
                t => t switch { 1 => CabinetPanel(), 2 => OutputPanel(), _ => TonePanel() },
                extras,
                CardLayout.Output(K(Gain, "GAIN", Ten, cellW: 54), K(Mix, "DRY/WET", Pct, cellW: 58, size: 44), K(Output, "OUTPUT", OutDb, cellW: 54, bipolar: true)),
                t => { centreTab = t; Refresh(); });

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

    }
}
