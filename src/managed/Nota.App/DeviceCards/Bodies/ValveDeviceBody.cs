// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Valve body (guitar amp, device kind 6), drawn to the style
// guide's picture of it (790 × 260): an always-visible Stage column (gain · output faders), a centre panel with Amp / Cab / Harmonics
// tabs — the tone stack's response, the cabinet's response against on-axis, the harmonics
// the preamp adds — each with the model or cabinet list in its head row, a right panel with
// Cabinet / Output tabs. The responses and harmonics come from the
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

    public double Width => 790;
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
        string OsText() => Sel(OS, 4) == 0 ? "OS off" : NotaNum.F($"{OsNames[Sel(OS, 4)]} OS");
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
        static TextBlock Caps(string t, IBrush? c = null, double fs = 0) => DeviceBodyKit.Label(t, c);
        // An acronym NotaText.Label does not know (OS, GR) keeps its case: the label as written.
        static TextBlock Acr(string t, IBrush? c = null) { var tb = Caps(t, c); tb.Text = t; return tb; }
        // One value size (11); `fs` is kept for the call sites. Ink 1 = a value, else a note line.
        static TextBlock Mono(string t, double fs, IBrush c) => c == TextPrimary ? DeviceBodyKit.Value(t, c) : DeviceBodyKit.Note(t, c);
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow);
        // fmt gets the param's own value (raw units for 0..13).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, double cellW = 53)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(N(p), 1.0) { Accent = true, Default = DefN(p) };
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
            int Cur() { for (int i = 0; i < names.Length; i++) if (lit(i)) return i; return -1; }
            var row = Segments(names, Cur, iv => { pick(iv); RefreshAll(); }, out var sync, padX: padX);
            readouts.Add(sync);
            
            if (learnParam >= 0) Learn(row, learnParam);
            return row;
        }
        Control IndexChips(int p, string[] names, double h = 16, double padX = 7)
            => Chips(names, i => Idx(p) == i, i => SetP(p, i), p, h, padX);

        // A latching button (raised; lit = the soft orange): a click runs `click`; its text can follow the value.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false)
        {
            var b = TextButton(text());
            var tb = (TextBlock)b.Child!;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            b.VerticalAlignment = VerticalAlignment.Center;
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? AccentBright : TextPrimary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tb.Text = text();
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            if (p >= 0) Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Border ToggleLatch(int p, Func<string> text, string tip, bool fill = false)
            => Latch(p, () => On(p), () => SetP(p, On(p) ? 0f : 1f), text, tip, fill);

        // Slider row: caps label · track · mono value (the track works on 0..1).
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, double labW = 58)
        {
            var bar = DeviceCardKit.SliderRow("", () => N(p), v => RawN(p, v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), bipolar: bipolar, valueWidth: 48);
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
            var name = new TextBlock { FontSize = NotaType.Caption, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border
            {
                Height = 22, MinWidth = 108, Background = NotaPalette.SurfaceHover, CornerRadius = NotaRadius.Field,
                Padding = new Thickness(8, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 8) { Foreground = TextSecondary, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right), name } },
            };
            box.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
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
        Control ModelDrop() => Drop(Model, Models, "Model — the amp's voicing: how hard the preamp drives, how many stages clip, how tight the low end is and which cabinet it matches. Scroll to step.");
        Control CabDrop() => Drop(CabType, Cabs, "Cabinet — Match follows the model; the others bring their own low resonance and presence peak. Scroll to step.");

        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 26, ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control KnobRow(Control[] knobs, Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 11, TextSecondary); i1.HorizontalAlignment = HorizontalAlignment.Right; i1.TextTrimming = TextTrimming.CharacterEllipsis;
            var i2 = Mono("", 11, TextSecondary); i2.HorizontalAlignment = HorizontalAlignment.Right; i2.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 56 };   // knob 26 + value + label
            g.Children.Add(Row(0, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 8, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true,
            Children = { Docked(head, Dock.Top), Docked(new Border { Margin = new Thickness(0, 6, 0, 0), Child = knobs }, Dock.Bottom), new Border { Margin = new Thickness(0, 8, 0, 0), Child = window } },
        };

        // ======================================================================
        // LEFT — STAGE column (gain · output)
        // ======================================================================
        DeviceBodyKit.RailFader StageFader(int p, string label, Func<string> text, string tip)
        {
            var f = DeviceBodyKit.Upright(label, () => N(p), v => { RawN(p, v); RefreshAll(); }, text, out var val, out var sync,
                () => Begin(p), () => End(p), () => Reset(p));
            Learn(f, p);
            ToolTip.SetTip(f, tip);
            readouts.Add(sync);
            return new DeviceBodyKit.RailFader(label, f, val);
        }
        var stageRail = DeviceBodyKit.FaderRail("Stage", true,
            StageFader(Gain, "Gain", () => Ten(P(Gain)), "Gain — how hard the signal hits the preamp. Double-click resets."),
            StageFader(Output, "Out", () => OutDb(P(Output)), "Output — the master level after the cabinet, ±12 dB. Double-click resets."));
        var stageCol = DeviceBodyKit.Sec(stageRail, new Thickness(8, 8, 8, 6), 75);
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

            var thd = Mono("", 11, TextSecondary);
            readouts.Add(() => thd.Text = "THD " + ThdF(Sc(S_Thd)));
            var count = Mono("", 11, TextSecondary);
            readouts.Add(() => count.Text = NotaNum.F($"{Idx(Model) + 1} / {Models.Length}"));
            var head = HeadRow(Row(8, Caps("Model"), ModelDrop(), count, new Border { Width = 4 }, Acr("OS"), Seg(OS, OsNames)), thd);
            var knobs = KnobRow(new[]
            {
                K(Bass, "BASS", Ten),
                K(Middle, "MIDDLE", Ten),
                K(MidFreq, "MID FREQ", MidF),
                K(Treble, "TREBLE", Ten),
                K(Presence, "PRESENCE", Ten),
            },
            () => NotaNum.F($"{DbF(Sc(S_InPk)).Replace("\u2009dB", "").Replace(" dB", "")} → {DbF(Sc(S_OutPk))} · mix {Pct(P(Mix))}"),
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

            var loss = Mono("", 7, TextSecondary);
            readouts.Add(() => loss.Text = On(CabOn) ? NotaNum.F($"{Sc(S_CabLossDb):+0.0;−0.0;0.0} dB @ 4 k") : "cabinet off");
            var mics = IndexChips(Mic, Mics);
            ToolTip.SetTip(mics, "Mic — Dynamic: the classic close mic; Condenser: brighter, less proximity bass; Ribbon: dark, the most proximity bass");
            var pos = Seg(MicPosition, new[] { "Cap", "Edge" });
            ToolTip.SetTip(pos, "Mic position — at the cone's cap (bright) or its edge (darker)");
            var head = HeadRow(Row(8, Caps("Cab"), CabDrop(), mics, pos), loss);
            var knobs = KnobRow(new[]
            {
                K(MicDistance, "DISTANCE", DistF),
                K(Axis, "AXIS", AxisF),
                K(LowCut, "LOW CUT", LowCutF),
                K(HighCut, "HIGH CUT", HighCutF),
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

            var peak = Mono("", 7, TextSecondary);
            readouts.Add(() => peak.Text = DbfsF(Sc(S_OutPk)));
            var even = ToggleLatch(EvenOnly, () => "Even only", "Even only — the preamp adds even harmonics only: warmth without the fizz of the odd ones");
            var head = HeadRow(Row(8, Caps("Model"), ModelDrop(), new Border { Width = 4 }, Acr("OS"), Seg(OS, OsNames), even), peak);
            var knobs = KnobRow(new[]
            {
                K(Gain, "GAIN", Ten),
                K(Mix, "MIX", Pct),
                K(Gate, "GATE", GateF),
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
            var l1 = Mono("", 11, TextPrimary);
            var l2 = Mono("", 11, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var b = DeviceBodyKit.Well(new StackPanel { Spacing = 2, Children = { Row(8, t, l1), l2 } }, new Thickness(10, 4));
            readouts.Add(() =>
            {
                t.Text = NotaText.Label(title()); l1.Text = line1(); l2.Text = line2();
                bool on = lit?.Invoke() ?? false;   // the box that describes the tab in view
                t.Foreground = on ? TextSecondary : TextTertiary;
            });
            return b;
        }
        static Control LabelRow(string label, Control c, double labW = 58)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")) };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(c, 1));
            return g;
        }
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(0, 4, 0, 0), Child = g };
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
            var cabs = IndexChips(CabType, CabShort, padX: 3);
            ToolTip.SetTip(cabs, "Cabinet — Match follows the model; 1×12 open, 2×12 combo, 4×12 closed, 1×15 bass");
            var mics = IndexChips(Mic, Mics, padX: 6);
            var sims = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
            sims.Children.Add(Toggle(CabOn, "Cabinet sim"));
            sims.Children.Add(Col(GateToggle(), 1));
            return Spread(
                LabelRow("Cab", cabs, 36),
                LabelRow("Mic", mics, 36),
                SliderRow("Axis", Axis, AxisF, labW: 36),
                SliderRow("Gate", Gate, GateF, labW: 36),
                Box(() => centreTab == 1 ? "CABINET" : "MODEL",
                    () => centreTab == 1 ? NotaNum.F($"{Cabs[Idx(CabType)]} · {Mics[Idx(Mic)]}") : NotaNum.F($"{ModelName()} · gain {Ten(P(Gain))}"),
                    () => centreTab == 1
                        ? (On(CabOn) ? NotaNum.F($"{DistF(P(MicDistance))} · {AxisText()} · {(On(MicPosition) ? "edge" : "cap")}") : "cabinet off")
                        : (On(CabOn) ? (Idx(CabType) == 0 ? "cab by model · " : CabShort[Idx(CabType)] + " · ") + NotaNum.F($"{Mics[Idx(Mic)].ToLowerInvariant()} {AxisText()}") : "no cabinet"),
                    () => centreTab == 1 && On(CabOn)),
                sims);
        }

        Control OutTab()
        {
            Control Line(string k, Func<string> v, IBrush? ink = null)
            {
                var val = Mono("", 8, ink ?? TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => val.Text = v());
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(Caps(k, TextSecondary));
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = new Border
            {
                Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field, Padding = new Thickness(10, 4),
                // One line: the tab is named Output and THD is in the centre's head row.
                Child = Line("in · out", () => DbF(Sc(S_InPk)).Replace("\u2009dB", "").Replace(" dB", "") + " · " + DbF(Sc(S_OutPk))),
            };
            var sliders = new Grid { RowDefinitions = new RowDefinitions("20,20,20") };
            sliders.Children.Add(SliderRow("MIX", Mix, Pct));
            sliders.Children.Add(GRow(SliderRow("GATE", Gate, GateF, modulation: true), 1));
            sliders.Children.Add(GRow(SliderRow("OUTPUT", Output, OutDb, bipolar: true), 2));
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
            btns.Children.Add(ToggleLatch(Bright, () => "Bright", "Bright — a treble lift before the preamp, strongest at low gain (the bright cap on a volume pot)", fill: true));
            btns.Children.Add(Col(ToggleLatch(Deep, () => "Deep", "Deep — a low resonance boost after the tone stack, for a fuller, tighter bottom", fill: true), 1));
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(box, Dock.Top),
                Docked(new Border { Margin = new Thickness(0, 4, 0, 0), Child = sliders }, Dock.Top),
                Docked(new StackPanel { Spacing = 0, Children = { Toggle(AutoComp, "Auto-compensation", () => On(AutoComp) ? NotaNum.F($"Auto-comp {Sc(S_CompDb):+0.0;−0.0;0.0} dB") : "Auto-compensation"), Toggle(EvenOnly, "Even harmonics only") } }, Dock.Bottom),
                Docked(new Border { Padding = new Thickness(0, 0, 0, 4), Child = btns }, Dock.Bottom) } };
            return new Border { Padding = new Thickness(0, 4, 0, 0), Child = body };   // 148 of 160
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 11, TextSecondary);
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

        var centre = DeviceBodyKit.TabFrame(new[] { "Amp", "Cab", "Harmonics" }, 0, centreHost, CentreBody, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = DeviceBodyKit.TabFrame(new[] { "Cabinet", "Output" }, 0, rightHost, RightBody, _ => RefreshAll());
        var right = DeviceBodyKit.Sec(rightFrame, SectionPad, 230);
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = DeviceBodyKit.Sec(centre);
        centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        var root = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { stageCol, right, centreBox } });

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
