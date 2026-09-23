// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Beat Repeat body (tempo-synced repeater, device kind 11), a
// build of the "Nota Beat Repeat" mockup (700 × 260) on the Shutter / Auto Shift frame: an
// always-visible REPEAT column (the input and the repeats' level · the repeat count), a centre
// panel with Timeline / Slices / Filter tabs — capture and repeats over two intervals on the
// grid, the interval's sound with the offset, gate and slice on it (drag them), the repeat
// filter with a later repeat's band — a right panel with Character / Output tabs, and a status
// strip. Interval and Grid sit in the row over the graph; Chance, Gate, Offset and Variation
// are knobs under it. The pictures come from the engine (BeatRepeat.h scopeRead). Every
// control is a device param, so automation / MIDI learn / presets / A-B / persistence come for
// free; Reset and the one-shot trigger are device actions.
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

internal sealed class BeatRepeatDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match BeatRepeat.h) ──────────────────────────
    private const int Interval = 0, Offset = 1, GridP = 2, Variation = 3, Chance = 4, Gate = 5, Pitch = 6, PitchDecay = 7,
        Volume = 8, Decay = 9, FilterOn = 10, FilterFreq = 11, FilterWidth = 12, Mode = 13, MixP = 14, RepeatP = 15,
        LatchP = 16, Triplet = 17, FilterType = 18, FilterNarrow = 19;
    // Scope layout (BeatRepeat::S_* / kTele / kCells / kWave).
    private const int S_InDb = 0, S_RepDb = 1, S_OutDb = 2, S_State = 3, S_Pass = 4, S_Passes = 5, S_WinPhase = 7,
        S_WinBeats = 8, S_IntervalBeats = 9, S_SliceBeats = 10, S_SliceMs = 11, S_Bpm = 12, S_Bar = 13, S_BeatInBar = 14,
        S_SampleRate = 15, S_Cpu = 16, S_Latency = 17, S_Forced = 18, S_PitchSt = 20, S_Playing = 24, S_BarBeats = 26,
        kTele = 32, kCells = 64, kWave = 128;
    private const int kCellAt = kTele, kWaveAt = kTele + 3 * kCells, kScope = kWaveAt + kWave;
    private const int A_Reset = 0;

    private static readonly string[] IntervalSeg = { "1/8", "1/4", "1/2", "1", "2", "4" };
    private static readonly string[] IntervalName = { "1/8", "1/4", "1/2", "1 Bar", "2 Bars", "4 Bars" };
    private static readonly string[] IntervalEvery = { "every 1/8", "every 1/4", "every 1/2", "every bar", "every 2 bars", "every 4 bars" };
    private static readonly string[] GridSeg = { "1/4", "1/8", "1/16", "1/32" };
    private static readonly double[] GridStraight = { 1, 0.5, 0.25, 0.125 };
    private static readonly string[] ModeNames = { "Mix", "Insert", "Gate" };
    private static readonly string[] FilterNames = { "Off", "LP", "BP", "HP" };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "REPEAT";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) => engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        double Def(int p) => engine.DeviceParamDefault(track, di, p);
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        // ---- units ----------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string HzF(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0}\u2009k") : NotaNum.F($"{hz:0}\u2009Hz");
        static string DbF(double db) => db <= -119 ? "−∞\u2009dB" : NotaNum.F($"{db:0.0}\u2009dB");
        static string PctF(double v) => NotaNum.F($"{v * 100:0}\u2009%");
        static string StF(double st) => NotaNum.F($"{st:+0.#;−0.#;0}\u2009st");
        static int Steps16(double v) => Math.Clamp((int)Math.Round(v * 16), 0, 16);
        int OffSteps() => Steps16(P(Offset)) % 16;
        int GateSteps() => Steps16(P(Gate));
        int VarSteps() => Math.Clamp((int)Math.Round(P(Variation) * 6), 0, 6);
        double BarBeats() => Sc(S_BarBeats) > 0 ? Sc(S_BarBeats) : 4;
        double IntervalBeats() { double b = BarBeats(); return new[] { 0.5, 1, 2, b, 2 * b, 4 * b }[Sel(Interval, 6)]; }
        double Bpm() => Sc(S_Bpm) > 1 ? Sc(S_Bpm) : Math.Max(1, engine.Bpm);
        // Grid: 0..3 straight, 4/5 the legacy 1/8T and 1/16T; Triplet turns a straight grid to triplets.
        int GridBase() { int g = Sel(GridP, 6); return g >= 4 ? g - 3 : g; }
        bool Trip() => Sel(GridP, 6) >= 4 || On(Triplet);
        double GridBeats() => GridStraight[GridBase()] * (Trip() ? 2.0 / 3.0 : 1);
        string GridName() => GridSeg[GridBase()] + (Trip() ? "T" : "");
        double SliceMs(double beats) => beats * 60000.0 / Bpm();
        string MsF(double ms) => ms >= 1000 ? NotaNum.F($"{ms / 1000:0.00}\u2009s") : NotaNum.F($"{ms:0}\u2009ms");
        double PitchSt() => (P(Pitch) - 0.5) * 24;
        double PDecaySt() => P(PitchDecay) * 6;
        double FreqHz() => Exp(P(FilterFreq), 50, 18000);
        double WidthOct() => 0.5 + P(FilterWidth) * 3;
        int FType() => Sel(FilterType, 3);
        string FilterText() => On(FilterOn) ? NotaNum.F($"{FilterNames[FType() + 1]} {HzF(FreqHz())} · {WidthOct():0.0}\u2009oct") : "filter off";
        int Passes() => Math.Max(1, (int)Math.Round(Math.Max(GridBeats(), GateSteps() / 16.0 * IntervalBeats()) / GridBeats()));
        string WindowText() { double b = IntervalBeats() * 2, bb = BarBeats(); return b >= bb ? NotaNum.F($"{b / bb:0.#} {(b / bb > 1.01 ? "bars" : "bar")}") : NotaNum.F($"{b:0.##} beats"); }
        string IntervalText() { double b = IntervalBeats(), bb = BarBeats(); return b >= bb ? NotaNum.F($"{b / bb:0.#} {(b / bb > 1.01 ? "bars" : "bar")}") : IntervalName[Sel(Interval, 6)]; }
        int State() => Math.Clamp((int)Math.Round(Sc(S_State)), 0, 2);
        int RepeatNo() => State() == 2 ? Math.Max(1, (int)Sc(S_Pass) - 1) : 0;

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

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        // `steps` > 1 snaps a discrete param; `lit` lights the label and value (the knob that matters now).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, int steps = 0, string? tip = null, Func<bool>? lit = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = Def(p), Width = 34, Height = 34 };
            knob.ValueChanged += v =>
            {
                if (steps > 1) v = Math.Round(v * (steps - 1)) / (steps - 1);
                Raw(p, v); val.Text = fmt(P(p)); RefreshAll();
            };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            if (tip is not null) ToolTip.SetTip(knob, tip);
            var cell = KnobCell(name, knob, val, 52);
            TextBlock? label = null;
            if (lit is not null && cell is Panel pn) foreach (var c in pn.Children) if (c is TextBlock tb && tb != val) { label = tb; break; }
            readouts.Add(() =>
            {
                if (!knob.Dragging) { double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(p));
                if (lit is not null)
                {
                    bool on = lit();
                    val.Foreground = on ? AccentBright : TextPrimary;
                    if (label is not null) label.Foreground = on ? AccentBright : TextTertiary;
                }
            });
            return cell;
        }

        // On/off switch bound to a param (>= 0.5 = on).
        Control Toggle(int p, string label, string tip)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); RefreshAll(); }, out var sync);
            readouts.Add(sync);
            Learn(wrap, p);
            ToolTip.SetTip(wrap, tip);
            return wrap;
        }

        // Segmented pill: `get` / `pick` over the names; learns `learnParam`.
        Control Seg(string[] names, Func<int> get, Action<int> pick, int learnParam, string tip, double padX = 6)
        {
            var seg = Segments(names, get, iv => { pick(iv); RefreshAll(); }, out var sync, padX: padX);
            readouts.Add(sync);
            Learn(seg, learnParam);
            ToolTip.SetTip(seg, tip);
            return seg;
        }
        Control ModeSeg() => Seg(ModeNames, () => Sel(Mode, 3), i => SetP(Mode, i / 2f), Mode,
            "Mode — Mix: the repeats play over the dry; Insert: they replace it while they play; Gate: only the burst sounds");
        Control IntervalSegC() => Seg(IntervalSeg, () => Sel(Interval, 6), i => SetP(Interval, i / 5f), Interval,
            "Interval — how often it rolls Chance: 1/8, 1/4, 1/2 note, or every 1, 2, 4 bars", 5);
        // Grid picks a straight value and keeps the triplet state (a legacy 1/8T / 1/16T turns into the Triplet switch).
        void PickGrid(int baseIdx, bool trip)
        {
            Begin(GridP); Raw(GridP, baseIdx / 5f); End(GridP);
            if (On(Triplet) != trip) SetP(Triplet, trip ? 1f : 0f);
        }
        Control GridSegC() => Seg(GridSeg, GridBase, i => PickGrid(i, Trip()), GridP,
            "Grid — the length of the slice that repeats", 5);

        // A latching chip: lit when `lit()`, a click runs `click`. `fill` = a full-width button.
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
            if (p >= 0) Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Control TripletChip() => Latch(Triplet, Trip, () => PickGrid(GridBase(), !Trip()), () => "Triplet", "Triplet — the grid in triplets (a third shorter)");

        // Repeat: held while pressed; with Latch on, a click turns it on and the next one off.
        Border RepeatButton(bool fill)
        {
            var tb = new TextBlock { Text = "Repeat", FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            bool held = false;
            void Hi()
            {
                bool on = On(RepeatP);
                b.Background = on ? Brass : NotaPalette.AccentSubtle;
                b.BorderBrush = Brass;
                tb.Foreground = on ? OnAccent : AccentBright;
                tb.FontWeight = FontWeight.SemiBold;
            }
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                if (On(LatchP)) { SetP(RepeatP, On(RepeatP) ? 0f : 1f); RefreshAll(); return; }
                held = true; Begin(RepeatP); Raw(RepeatP, 1); e.Pointer.Capture(b); RefreshAll();
            };
            void Release()
            {
                if (!held) return;
                held = false; Raw(RepeatP, 0); End(RepeatP); RefreshAll();
            }
            b.PointerReleased += (_, e) => { if (held) { e.Pointer.Capture(null); Release(); } };
            b.PointerCaptureLost += (_, _) => Release();
            ToolTip.SetTip(b, "Repeat — repeat the current slice now, for as long as it is held (with Latch: click on, click off)");
            Learn(b, RepeatP);
            readouts.Add(Hi); Hi();
            return b;
        }

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, double labW = 40, Func<bool>? lit = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, v); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: 34, bipolar: bipolar, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            var cap = Caps(label);
            g.Children.Add(cap);
            g.Children.Add(Col(bar, 1));
            if (lit is not null)
                readouts.Add(() =>
                {
                    bool on = lit();
                    foreach (var c in bar.Children) if (c is TextBlock tb && tb.TextAlignment == TextAlignment.Right) tb.Foreground = on ? AccentBright : TextPrimary;
                });
            return g;
        }

        // A dropdown over a discrete choice: [value ▾] opens the list.
        Control Drop(int learnParam, string[] names, Func<int> get, Action<int> pick, string tip)
        {
            var name = new TextBlock { FontSize = 8, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Margin = new Thickness(8, 0, 0, 0), Foreground = TextTertiary };
            var box = new Border
            {
                Height = 16, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(chev, Dock.Right), name } },
            };
            readouts.Add(() => name.Text = names[Math.Clamp(get(), 0, names.Length - 1)]);
            ToolTip.SetTip(box, tip);
            Learn(box, learnParam);
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                int cur = get();
                for (int i = 0; i < names.Length; i++)
                {
                    int iv = i;
                    var mi = new MenuItem { Header = names[i] };
                    if (i == cur) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brass };
                    mi.Click += (_, _) => { pick(iv); RefreshAll(); };
                    fly.Items.Add(mi);
                }
                fly.ShowAt(box);
                e.Handled = true;
            };
            return box;
        }
        // Grid dropdown: the four straight values and their triplets.
        string[] gridDropNames = { "1/4", "1/8", "1/16", "1/32", "1/4T", "1/8T", "1/16T", "1/32T" };
        int GridDropIdx() => GridBase() + (Trip() ? 4 : 0);

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
            i1.TextTrimming = i2.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 52, ColumnSpacing = 6 };
            g.Children.Add(Row(2, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(8, 5, 8, 0),
            Children = { Docked(head, Dock.Top), Docked(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 5, 0, 0), Child = window } },
        };
        void WireDrag(ShDragView v, Func<int, int> param)
        {
            v.Value = h => P(param(h));
            v.Changed += (h, x) => { Raw(param(h), x); Refresh(); };
            v.GestureBegin += h => Begin(param(h));
            v.GestureEnd += h => End(param(h));
            v.ResetRequested += h => { Reset(param(h)); RefreshAll(); };
        }

        // ---- shared knob formats ----
        string ChanceF(double v) => PctF(v);
        string GateF(double v) => NotaNum.F($"{Steps16(v)}/16");
        string OffsetF(double v) => NotaNum.F($"{Steps16(v) % 16}/16");
        string VarF(double v) { int s = Math.Clamp((int)Math.Round(v * 6), 0, 6); return s == 0 ? "off" : NotaNum.F($"{s}"); }
        string PitchF(double v) => StF((v - 0.5) * 24);
        string PDecF(double v) => PctF(v);
        string DecayF(double v) => PctF(v);
        string VolF(double v) { double d = (v - 0.5) * 24; return NotaNum.F($"{d:+0.0;−0.0;0.0}\u2009dB"); }
        string FreqF(double v) => HzF(Exp(v, 50, 18000));
        string WidthF(double v) => NotaNum.F($"{0.5 + v * 3:0.0}\u2009oct");

        // ======================================================================
        // LEFT — REPEAT column (input · repeats)
        // ======================================================================
        var colTitle = Caps("REPEAT"); colTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var inPill = new ShPill { Ink = Brass, VerticalAlignment = VerticalAlignment.Stretch };
        var repPill = new ShPill { Ink = Teal, VerticalAlignment = VerticalAlignment.Stretch };
        var inLbl = Caps("IN"); inLbl.HorizontalAlignment = HorizontalAlignment.Center;
        var repLbl = Caps("REP"); repLbl.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(inPill, "The input level");
        ToolTip.SetTip(repPill, "The repeats' level — after pitch, filter, decay and volume");
        var colIn = Mono("", 7, AccentBright); colIn.HorizontalAlignment = HorizontalAlignment.Center;
        var colRep = Mono("", 7, TextPrimary); colRep.HorizontalAlignment = HorizontalAlignment.Center;
        static double Norm60(double db) => Math.Clamp((db + 60) / 60, 0, 1);
        readouts.Add(() =>
        {
            inPill.Set(Norm60(Sc(S_InDb)), double.NaN);
            repPill.Set(Norm60(Sc(S_RepDb)), double.NaN);
            colIn.Text = Sc(S_InDb) <= -119 ? "−∞" : NotaNum.F($"{Sc(S_InDb):0.0}");
            colRep.Text = State() == 2 ? NotaNum.F($"rep {RepeatNo()}") : State() == 1 ? "capture" : "rep —";
            colTitle.Foreground = State() > 0 ? AccentBright : TextTertiary;   // lit while a burst runs
        });
        Control PillCol(ShPill pill, TextBlock lbl) => new DockPanel { Children = { Docked(lbl, Dock.Bottom), new Border { Margin = new Thickness(0, 0, 0, 3), Child = pill } } };
        var pills = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        pills.Children.Add(PillCol(inPill, inLbl));
        pills.Children.Add(Col(PillCol(repPill, repLbl), 1));
        var leftCol = new Border
        {
            Width = 56, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(colTitle, Dock.Top), Docked(colRep, Dock.Bottom), Docked(colIn, Dock.Bottom), pills } },
        };
        DockPanel.SetDock(leftCol, Dock.Left);

        // ======================================================================
        // CENTRE — Timeline
        // ======================================================================
        BrTimelineView? timeline = null;
        Control TimelineTab()
        {
            timeline = new BrTimelineView();
            ToolTip.SetTip(timeline, "Two intervals on the grid: each step is the input's level — dim when dry, full brass where the slice is captured, dimmer with every repeat as it decays. Brass ticks mark where the interval fires.");
            var slice = Mono("", 7, AccentBright);
            readouts.Add(() => slice.Text = "slice " + MsF(SliceMs(GridBeats())));
            var head = HeadRow(Row(6, ModeSeg(), new Border { Width = 2 }, Caps("INTERVAL"),
                Drop(Interval, IntervalName, () => Sel(Interval, 6), i => SetP(Interval, i / 5f), "Interval — how often it rolls Chance"),
                Caps("GRID"),
                Drop(GridP, gridDropNames, GridDropIdx, i => PickGrid(i % 4, i >= 4), "Grid — the length of the slice that repeats")), slice);
            var knobs = KnobRow(new[]
            {
                K(Chance, "CHANCE", ChanceF, tip: "Chance — how likely each interval fires a repeat", lit: () => true),
                K(Gate, "GATE", GateF, steps: 17, tip: "Gate — how long the burst lasts, in 16ths of the interval"),
                K(Offset, "OFFSET", OffsetF, steps: 17, tip: "Offset — where in the interval it fires, in 16ths"),
                K(Variation, "VARIATION", VarF, Teal, tip: "Variation — the grid floats up to this many steps (double / half) on each trigger"),
            },
            () => NotaNum.F($"capture {GridName()} → {Passes() - 1} {(Passes() - 1 == 1 ? "repeat" : "repeats")}"),
            () => NotaNum.F($"{IntervalEvery[Sel(Interval, 6)]} · decay {P(Decay) * 100:0}\u2009%"));
            return TabBody(head, timeline, knobs);
        }

        // ======================================================================
        // CENTRE — Slices
        // ======================================================================
        BrSlicesView? slices = null;
        Control SlicesTab()
        {
            slices = new BrSlicesView();
            WireDrag(slices, h => h == BrSlicesView.HGate ? Gate : Offset);
            ToolTip.SetTip(slices, "The last interval's sound (teal) on the grid: the gate from the offset is shaded and the captured slice is brass. Drag the offset line or the gate's end sideways; double-click resets.");
            Learn(slices, Offset);
            var head = HeadRow(Row(6, Caps("INTERVAL"), IntervalSegC(), Caps("GRID"), GridSegC(), TripletChip()), new Border());
            var knobs = KnobRow(new[]
            {
                K(Offset, "OFFSET", OffsetF, steps: 17, tip: "Offset — where in the interval it fires, in 16ths", lit: () => true),
                K(Gate, "GATE", GateF, steps: 17, tip: "Gate — how long the burst lasts, in 16ths of the interval", lit: () => true),
                K(Chance, "CHANCE", ChanceF, tip: "Chance — how likely each interval fires a repeat"),
                K(Variation, "VARIATION", VarF, Teal, tip: "Variation — the grid floats up to this many steps (double / half) on each trigger"),
            },
            () => NotaNum.F($"takes {GridName()} at {OffSteps()}/16 · {Passes()} {(Passes() == 1 ? "step" : "steps")}"),
            () => VarSteps() == 0 ? "the grid holds still" : NotaNum.F($"grid floats ±{VarSteps()} {(VarSteps() == 1 ? "step" : "steps")}"));
            return TabBody(head, slices, knobs);
        }

        // ======================================================================
        // CENTRE — Filter
        // ======================================================================
        BrFilterView? filter = null;
        int LaterRepeat() => Math.Clamp(Passes() - 1, 1, 4);
        double LaterPitch() => PitchSt() - PDecaySt() * (LaterRepeat() - 1);
        Control FilterTab()
        {
            filter = new BrFilterView();
            WireDrag(filter, h => h == BrFilterView.HFreq ? FilterFreq : FilterWidth);
            ToolTip.SetTip(filter, "The repeat filter — only the repeats go through it. Brass: the first repeat; teal dashed: a later one when the band narrows and follows the pitch. Drag the node for the frequency, a band edge for the width; double-click resets.");
            Learn(filter, FilterFreq);
            var type = Seg(FilterNames, () => On(FilterOn) ? FType() + 1 : 0,
                i => { if (i == 0) SetP(FilterOn, 0f); else { SetP(FilterType, (i - 1) / 2f); if (!On(FilterOn)) SetP(FilterOn, 1f); } },
                FilterType, "Filter — Off, or a low-pass, band-pass or high-pass on the repeats");
            var narrow = Latch(FilterNarrow, () => On(FilterNarrow), () => SetP(FilterNarrow, On(FilterNarrow) ? 0f : 1f), () => "Narrow with repeats",
                "Narrow with repeats — each repeat narrows the band and moves it with the pitch");
            var drop = Mono("", 7, AccentBright);
            readouts.Add(() => drop.Text = PDecaySt() > 0.01 ? NotaNum.F($"{StF(PitchSt())} → {StF(LaterPitch())} by repeat {LaterRepeat()}") : "pitch " + StF(PitchSt()));
            var head = HeadRow(Row(6, Caps("FILTER"), type, narrow), drop);
            var knobs = KnobRow(new[]
            {
                K(FilterFreq, "FREQ", FreqF, tip: "Freq — the filter's frequency", lit: () => On(FilterOn)),
                K(FilterWidth, "WIDTH", WidthF, tip: "Width — the band in octaves (low- and high-pass: less is more resonant)", lit: () => On(FilterOn)),
                K(Pitch, "PITCH", PitchF, Teal, tip: "Pitch — the repeats transposed, ±12 semitones"),
                K(PitchDecay, "P. DECAY", PDecF, tip: "Pitch decay — each repeat drops lower, up to 6 semitones a repeat"),
            },
            () =>
            {
                if (!On(FilterOn)) return "the repeats pass unfiltered";
                double lo = FreqHz() / Math.Pow(2, WidthOct() / 2), hi = FreqHz() * Math.Pow(2, WidthOct() / 2);
                return FType() == 1 ? NotaNum.F($"band {HzF(lo)}…{HzF(hi)}") : NotaNum.F($"{(FType() == 0 ? "below" : "above")} {HzF(FreqHz())}");
            },
            () => On(FilterNarrow) ? "each repeat lower and narrower" : PDecaySt() > 0.01 ? "each repeat lower, same band" : "the same on every repeat");
            return TabBody(head, filter, knobs);
        }

        // ======================================================================
        // RIGHT — Character / Output
        // ======================================================================
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(8, 6), Child = g };
        }

        Control StateBox()
        {
            var t = Caps("STATE");
            var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Center };
            var l1 = Mono("", 9, TextPrimary);
            var l2 = Mono("", 8, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var b = new Border
            {
                Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = { t, Row(5, dot, l1), l2 } },
            };
            readouts.Add(() =>
            {
                int st = State();
                bool forced = Sc(S_Forced) > 0.5;
                int passes = (int)Sc(S_Passes);
                string total = passes > 0 ? NotaNum.F($"/{Math.Max(1, passes - 1)}") : "";
                l1.Text = st switch
                {
                    2 when forced => NotaNum.F($"{(On(LatchP) ? "latch" : "held")} · repeat {RepeatNo()}"),
                    2 => NotaNum.F($"repeat · {RepeatNo()}{total}"),
                    1 => "capture · " + MsF(Sc(S_SliceMs)),
                    _ => Sc(S_Playing) > 0.5 ? "waiting · " + IntervalEvery[Sel(Interval, 6)] : "stopped",
                };
                l2.Text = forced
                    ? NotaNum.F($"slice {MsF(Sc(S_SliceMs))} · {StF(Sc(S_PitchSt))}")
                    : NotaNum.F($"bar {Math.Max(1, Sc(S_Bar)):0} · beat {Math.Max(1, Sc(S_BeatInBar)):0.0} · mix {P(MixP) * 100:0}\u2009%");
                dot.Background = forced ? Brass : st > 0 ? Success : NotaPalette.BorderStrong;
                l1.Foreground = forced ? AccentBright : TextPrimary;
                b.BorderBrush = forced ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
                t.Foreground = forced ? AccentBright : TextTertiary;
            });
            ToolTip.SetTip(b, "What the repeater is doing now — waiting for the interval, capturing the slice, or repeating it — and where the transport is");
            return b;
        }

        Control Toggles() => new StackPanel { Spacing = 5, Children = {
            Toggle(FilterOn, "Repeat filter", "Repeat filter — the repeats go through the filter (Filter tab)"),
            Toggle(LatchP, "Latch", "Latch — the Repeat button stays on after a click instead of only while held") } };

        Control CharacterTab()
        {
            var vol = SliderRow("VOLUME", Volume, VolF, bipolar: true, modulation: true);
            return Spread(
                SliderRow("PITCH", Pitch, PitchF, bipolar: true, modulation: true),
                SliderRow("P. DECAY", PitchDecay, PDecF),
                SliderRow("DECAY", Decay, DecayF, lit: () => P(Decay) > 0.001f),
                new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = vol },
                StateBox(),
                Toggles());
        }

        Control OutputTab()
        {
            Control Line(string k, Func<string> v, IBrush? ink = null)
            {
                var key = new TextBlock { Text = k, FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
                var val = Mono("", 8, ink ?? TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => val.Text = v());
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(key);
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = new Border
            {
                Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 3, Children = {
                    Caps("MEASUREMENTS"),
                    Line("in", () => DbF(Sc(S_InDb))),
                    Line("repeat", () => DbF(Sc(S_RepDb))),
                    Line("out", () => DbF(Sc(S_OutDb)), AccentBright) } },
            };
            var sliders = new StackPanel { Spacing = 6, Children = {
                SliderRow("VOLUME", Volume, VolF, bipolar: true, modulation: true),
                SliderRow("MIX", MixP, PctF, lit: () => true) } };
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            btns.Children.Add(RepeatButton(fill: true));
            btns.Children.Add(Col(Latch(-1, () => false, () => engine.DeviceAction(track, di, A_Reset, 0, 0), () => "Reset",
                "Reset — stop the repeat and clear the timeline and the meters", fill: true), 1));
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(box, Dock.Top),
                Docked(new Border { Margin = new Thickness(0, 6, 0, 0), Child = sliders }, Dock.Top),
                Docked(Toggles(), Dock.Bottom),
                Docked(new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 5), Child = btns }, Dock.Bottom) } };
            return new Border { Padding = new Thickness(8, 6), Child = body };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        int centreTab = 0;
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"{IntervalText()} · slice {MsF(SliceMs(GridBeats()))}"),
                2 => "repeats only",
                _ => NotaNum.F($"{WindowText()} · {Bpm():0.##} BPM"),
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => SlicesTab(), 2 => FilterTab(), _ => TimelineTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : CharacterTab();

        var centre = TabFrame(new[] { "Timeline", "Slices", "Filter" }, centreHost, CentreBody, false, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = TabFrame(new[] { "Character", "Output" }, rightHost, RightBody, true, _ => RefreshAll(), null);
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
            var parts = new List<string> { ModeNames[Sel(Mode, 3)] };
            switch (centreTab)
            {
                case 1:
                    parts.Add(IntervalEvery[Sel(Interval, 6)]);
                    parts.Add("grid " + GridName());
                    parts.Add(NotaNum.F($"offset {OffSteps()}/16"));
                    parts.Add(NotaNum.F($"gate {GateSteps()}/16"));
                    parts.Add("chance " + PctF(P(Chance)));
                    if (VarSteps() > 0) parts.Add(NotaNum.F($"var {VarSteps()}"));
                    break;
                case 2:
                    parts.Add(FilterText());
                    parts.Add("pitch " + StF(PitchSt()));
                    parts.Add("p. decay " + PctF(P(PitchDecay)));
                    if (On(FilterNarrow) && On(FilterOn)) parts.Add("narrowing");
                    parts.Add("mix " + PctF(P(MixP)));
                    break;
                default:
                    parts.Add(IntervalEvery[Sel(Interval, 6)]);
                    parts.Add("grid " + GridName());
                    parts.Add("chance " + PctF(P(Chance)));
                    parts.Add(NotaNum.F($"gate {GateSteps()}/16"));
                    parts.Add("decay " + PctF(P(Decay)));
                    break;
            }
            if (On(RepeatP)) parts.Add("repeat held");
            return string.Join(" · ", parts);
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            double sr = Sc(S_SampleRate);
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#}\u2009kHz · latency {Sc(S_Latency):0}\u2009smp · CPU {Sc(S_Cpu) * 100:0.0}\u2009%") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { leftCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            bool full = scN >= kScope;
            double ib = IntervalBeats();
            if (centreTab == 0 && timeline is not null)
                timeline.Set(scope, kCellAt, full ? kCells : 0, ib * 2, ib, GridBeats(), BarBeats(), OffSteps() / 16.0,
                    Sc(S_WinPhase), Sc(S_Playing) > 0.5, State() == 2 ? NotaNum.F($"Capture and repeat · {RepeatNo()}") : "Capture and repeat");
            if (centreTab == 1 && slices is not null)
            {
                double sliceBeats = Sc(S_SliceBeats) > 0 && State() > 0 ? Sc(S_SliceBeats) : GridBeats();
                slices.Set(scope, kWaveAt, full ? kWave : 0, OffSteps() / 16.0, GateSteps() / 16.0, sliceBeats / ib, GridBeats() / ib, State() > 0);
            }
            if (centreTab == 2 && filter is not null)
            {
                int later = On(FilterNarrow) || PDecaySt() > 0.01 ? LaterRepeat() : 0;
                double lo = WidthOct(), lh = FreqHz();
                if (On(FilterNarrow)) { lo = Math.Max(0.2, lo * Math.Pow(0.75, later - 1)); lh = Math.Clamp(lh * Math.Pow(2, (LaterPitch() - PitchSt()) / 12), 20, 20000); }
                filter.Set(On(FilterOn), FType(), FreqHz(), WidthOct(), On(FilterNarrow) ? later : 0, lh, lo);
            }
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
