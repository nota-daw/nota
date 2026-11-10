// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Beat Repeat body (tempo-synced repeater, device kind 11), a
// build of the "Nota Beat Repeat" mockup (700 × 260) on the Shutter / Auto Shift frame: a centre
// panel with Timeline / Slices / Filter tabs — capture and repeats over two intervals on the
// grid (Chance · Variation beside it), the interval's sound with the offset, gate and slice on
// it (drag them; Offset · Gate beside it), the repeat filter with a later repeat's band (Freq ·
// Width beside it) — and a right panel with no tabs of its own that follows them: Repeat (mode,
// Repeat / Reset, Latch, state), Capture (interval, grid), Filter (on, type, narrowing, pitch) —
// over the output (Decay · Dry/wet · Volume knobs), which stays. Each param has one home
// (DESIGN.md § Devices › Card layout). The pictures come from the engine (BeatRepeat.h scopeRead). Every control is a device
// param, so automation / MIDI learn / presets / A-B / persistence come for free; Reset and the
// one-shot trigger are device actions.
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
    // S / L (#531): the size is the device's View param (DeviceView); S is a 260 card.
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => DeviceView.IsMini(engine, trackId, deviceIndex) ? 260 : Width;
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex)
        => DeviceView.Index(ctx.Engine, ctx.TrackId, deviceIndex) < 0 ? null
            : MidiCardHeader.SizeToggle(ctx, () => DeviceView.IsMini(ctx.Engine, ctx.TrackId, deviceIndex), m => DeviceView.SetMini(ctx.Engine, ctx.TrackId, deviceIndex, m));
    public bool FullBleed => true;
    public string? Subtitle => "Repeat";

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
        static string PctF(double v) => NotaNum.F($"{v * 100:0}\u2009%");
        static string StF(double st) => NotaNum.F($"{st:+0.#;−0.#;0}\u2009st");
        static int Steps16(double v) => Math.Clamp((int)Math.Round(v * 16), 0, 16);
        int OffSteps() => Steps16(P(Offset)) % 16;
        int GateSteps() => Steps16(P(Gate));
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
        int Passes() => Math.Max(1, (int)Math.Round(Math.Max(GridBeats(), GateSteps() / 16.0 * IntervalBeats()) / GridBeats()));
        string WindowText() { double b = IntervalBeats() * 2, bb = BarBeats(); return b >= bb ? NotaNum.F($"{b / bb:0.#} {(b / bb > 1.01 ? "bars" : "bar")}") : NotaNum.F($"{b:0.##} beats"); }
        string IntervalText() { double b = IntervalBeats(), bb = BarBeats(); return b >= bb ? NotaNum.F($"{b / bb:0.#} {(b / bb > 1.01 ? "bars" : "bar")}") : IntervalName[Sel(Interval, 6)]; }
        int State() => Math.Clamp((int)Math.Round(Sc(S_State)), 0, 2);
        int RepeatNo() => State() == 2 ? Math.Max(1, (int)Sc(S_Pass) - 1) : 0;

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        // `steps` > 1 snaps a discrete param; `lit` brightens the label and value (the knob that matters now).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, int steps = 0, string? tip = null, Func<bool>? lit = null,
                  double cellW = 52, double size = 34, bool bipolar = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Bipolar = bipolar, Default = Def(p), Width = size, Height = size };
            knob.Format = v => fmt(steps > 1 ? Math.Round(v * (steps - 1)) / (steps - 1) : v);   // read-out at any value: typing searches it
            knob.ValueChanged += v =>
            {
                if (steps > 1) v = Math.Round(v * (steps - 1)) / (steps - 1);
                Raw(p, v); val.Text = fmt(P(p)); RefreshAll();
            };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            if (tip is not null) ToolTip.SetTip(knob, tip);
            var cell = KnobCell(name, knob, val, cellW);
            TextBlock? label = null;
            if (lit is not null && cell is Panel pn) foreach (var c in pn.Children) if (c is TextBlock tb && tb != val) { label = tb; break; }
            readouts.Add(() =>
            {
                if (!knob.Dragging) { double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(p));
                if (lit is not null)
                {
                    bool on = lit();
                    val.Foreground = on ? TextPrimary : TextSecondary;
                    if (label is not null) label.Foreground = on ? TextSecondary : TextTertiary;
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
            Loc.T("device.beat_repeat.mode_mix_the_repeats_play"));
        Control IntervalSegC() => Seg(IntervalSeg, () => Sel(Interval, 6), i => SetP(Interval, i / 5f), Interval,
            Loc.T("device.beat_repeat.interval_how_often_it_rolls_2"), 5);
        // Grid picks a straight value and keeps the triplet state (a legacy 1/8T / 1/16T turns into the Triplet switch).
        void PickGrid(int baseIdx, bool trip)
        {
            Begin(GridP); Raw(GridP, baseIdx / 5f); End(GridP);
            if (On(Triplet) != trip) SetP(Triplet, trip ? 1f : 0f);
        }

        // A latching chip: lit when `lit()`, a click runs `click`. `fill` = a full-width button.
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
            if (p >= 0) Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }

        // Repeat: held while pressed; with Latch on, a click turns it on and the next one off.
        Border RepeatButton(bool fill)
        {
            var tb = new TextBlock { Text = "Repeat", FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            bool held = false;
            void Hi()
            {
                bool on = On(RepeatP);   // accent only while it repeats
                b.Background = on ? Brass : Raised;
                b.BorderBrush = on ? Brass : NotaPalette.BorderStrong;
                tb.Foreground = on ? OnAccent : TextPrimary;
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
            ToolTip.SetTip(b, Loc.T("device.beat_repeat.repeat_repeat_the_current_slice"));
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
                    foreach (var c in bar.Children) if (c is TextBlock tb && tb.TextAlignment == TextAlignment.Right) tb.Foreground = on ? TextPrimary : TextSecondary;
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
                Height = 16, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell,
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
                    mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = i == cur;
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

        void WireDrag<T>(T v, Func<int, int> param, params ValueField[] handles) where T : ShDragView
        {
            // ⌥-click a handle to type it (specs/035): one field per handle, in the view's handle order.
            ValueEntry.AttachHandles(v, handles.Select(f => (IReadOnlyList<ValueField>)new[] { f }).ToArray());
            v.Value = h => P(param(h));
            v.Changed += (h, x) => { Raw(param(h), x); Refresh(); };
            v.GestureBegin += h => Begin(param(h));
            v.GestureEnd += h => End(param(h));
            v.ResetRequested += h => { Reset(param(h)); RefreshAll(); };
        }

        ValueField F(int p, string name, Func<double, string> fmt) => ValueEntry.DeviceParam(engine, track, di, p, name, fmt, RefreshAll);

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
        // Layout pieces (DESIGN.md § Devices › Card layout: graph, tabs and output)
        // ======================================================================
        static Control KnobColumn(params Control[] cells)
        {
            var g = new Grid { Width = 52 };
            for (int i = 0; i < cells.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                cells[i].VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(GRow(cells[i], i));
            }
            return g;
        }
        static Control View(Control graph, Control? knobs)
        {
            var d = new DockPanel { LastChildFill = true, Margin = new Thickness(6, 6, 6, 5) };
            if (knobs != null) d.Children.Add(Docked(new Border { Margin = new Thickness(6, 0, 0, 0), Child = knobs }, Dock.Right));
            d.Children.Add(graph);
            return d;
        }
        static Control Head(Control left, Control? right = null)
        {
            var d = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center };
            if (right != null) d.Children.Add(Docked(right, Dock.Right));
            d.Children.Add(Docked(left, Dock.Left));
            return d;
        }
        static TextBlock Title(string t) => new() { Text = t, FontSize = 9, FontWeight = FontWeight.Medium, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        static Control Rows(params Control[] rows)
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
        static Control LabelRow(string label, Control c, double labW = 44)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            c.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(Col(c, 1));
            return g;
        }
        static Control Tip(Control c, string tip) { ToolTip.SetTip(c, tip); return c; }
        // A read-out line in the panel (ink 2, trims).
        Control Info(Func<string> text, Func<IBrush>? ink = null)
        {
            var t = Mono("", 8, TextSecondary);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { t.Text = text(); t.Foreground = ink?.Invoke() ?? TextSecondary; });
            return t;
        }

        // ======================================================================
        // CENTRE — Timeline: capture and repeats over two intervals; Chance and Variation
        // decide which intervals roll and where the grid floats.
        // ======================================================================
        BrTimelineView? timeline = null;
        Control TimelineTab()
        {
            timeline = new BrTimelineView();
            ToolTip.SetTip(timeline, Loc.T("device.beat_repeat.two_intervals_on_the_grid"));
            return View(timeline, KnobColumn(
                K(Chance, "CHANCE", ChanceF, tip: Loc.T("device.beat_repeat.chance_how_likely_each_interval")),
                K(Variation, "VARIATION", VarF, Teal, tip: Loc.T("device.beat_repeat.variation_the_grid_floats_up"))));
        }

        // ======================================================================
        // CENTRE — Slices: the interval's sound with the offset, gate and slice on it (drag them).
        // ======================================================================
        BrSlicesView? slices = null;
        Control SlicesTab()
        {
            slices = new BrSlicesView();
            WireDrag(slices, h => h == BrSlicesView.HGate ? Gate : Offset, F(Offset, "Offset", OffsetF), F(Gate, "Gate", GateF));
            ToolTip.SetTip(slices, MenuKit.Keys(Loc.T("device.beat_repeat.the_last_intervals_sound_teal")));
            Learn(slices, Offset);
            return View(slices, KnobColumn(
                K(Offset, "OFFSET", OffsetF, steps: 17, tip: Loc.T("device.beat_repeat.offset_where_in_the_interval")),
                K(Gate, "GATE", GateF, steps: 17, tip: Loc.T("device.beat_repeat.gate_how_long_the_burst"))));
        }

        // ======================================================================
        // CENTRE — Filter: the repeat filter with a later repeat's band, on the shared filter graph
        // (drag sideways for the frequency, up / down for the width).
        // ======================================================================
        BrFilterView? filter = null;
        int LaterRepeat() => Math.Clamp(Passes() - 1, 1, 4);
        double LaterPitch() => PitchSt() - PDecaySt() * (LaterRepeat() - 1);
        Control FilterTab()
        {
            filter = new BrFilterView();
            filter.GestureBegin += () => { Begin(FilterFreq); Begin(FilterWidth); };
            filter.GestureEnd += () => { End(FilterFreq); End(FilterWidth); };
            filter.FreqChanged += v => Raw(FilterFreq, v);
            filter.WidthChanged += v => Raw(FilterWidth, v);
            filter.ResetRequested += () => { Reset(FilterFreq); Reset(FilterWidth); RefreshAll(); };
            ValueEntry.AttachAll(filter, F(FilterFreq, "Freq", FreqF), F(FilterWidth, "Width", WidthF));
            ToolTip.SetTip(filter, MenuKit.Keys(Loc.T("device.beat_repeat.the_repeat_filter_only_the")));
            Learn(filter, FilterFreq);
            return View(filter, KnobColumn(
                K(FilterFreq, "FREQ", FreqF, tip: Loc.T("device.beat_repeat.freq_the_filters_frequency"), lit: () => On(FilterOn)),
                K(FilterWidth, "WIDTH", WidthF, tip: Loc.T("device.beat_repeat.width_the_band_in_octaves"), lit: () => On(FilterOn))));
        }

        // ======================================================================
        // RIGHT — follows the centre tab: Repeat (Timeline) · Capture (Slices) · Filter (Filter)
        // ======================================================================
        (Control, Control) RepeatPanel()
        {
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            btns.Children.Add(RepeatButton(fill: true));
            btns.Children.Add(Col(Latch(-1, () => false, () => engine.DeviceAction(track, di, A_Reset, 0, 0), () => "Reset",
                Loc.T("device.beat_repeat.reset_stop_the_repeat_and"), fill: true), 1));
            bool Forced() => Sc(S_Forced) > 0.5;
            var state = Info(() =>
            {
                int passes = (int)Sc(S_Passes);
                string total = passes > 0 ? NotaNum.F($"/{Math.Max(1, passes - 1)}") : "";
                return State() switch
                {
                    2 when Forced() => NotaNum.F($"{(On(LatchP) ? "latch" : "held")} · repeat {RepeatNo()}"),
                    2 => NotaNum.F($"repeat · {RepeatNo()}{total}"),
                    1 => "capture · " + MsF(Sc(S_SliceMs)),
                    _ => Sc(S_Playing) > 0.5 ? "waiting · " + IntervalEvery[Sel(Interval, 6)] : "stopped",
                };
            }, () => Forced() ? AccentBright : TextPrimary);
            ToolTip.SetTip(state, Loc.T("device.beat_repeat.what_the_repeater_is_doing"));
            var where = Info(() => Forced()
                ? NotaNum.F($"slice {MsF(Sc(S_SliceMs))} · {StF(Sc(S_PitchSt))}")
                : NotaNum.F($"bar {Math.Max(1, Sc(S_Bar)):0} · beat {Math.Max(1, Sc(S_BeatInBar)):0.0}"));
            return (Head(Title("Repeat"), ModeSeg()), Rows(
                btns,
                Toggle(LatchP, "Latch", Loc.T("device.beat_repeat.latch_the_repeat_button_stays")),
                Rule(),
                state,
                where));
        }

        (Control, Control) CapturePanel() => (
            Head(Title(Loc.T("device.beat_repeat.capture"))),
            Rows(
                LabelRow("INTERVAL", IntervalSegC()),
                LabelRow("GRID", Drop(GridP, gridDropNames, GridDropIdx, i => PickGrid(i % 4, i >= 4), Loc.T("device.beat_repeat.grid_the_length_of_the"))),
                Rule(),
                Info(() => NotaNum.F($"capture {GridName()} · slice {MsF(SliceMs(GridBeats()))}")),
                Info(() => NotaNum.F($"{IntervalEvery[Sel(Interval, 6)]} → {Passes() - 1} {(Passes() - 1 == 1 ? "repeat" : "repeats")}"))));

        (Control, Control) FilterPanel()
        {
            var on = Toggle(FilterOn, "Filter", Loc.T("device.beat_repeat.repeat_filter_the_repeats_go"));
            var type = Seg(new[] { "LP", "BP", "HP" }, FType, i => { SetP(FilterType, i / 2f); if (!On(FilterOn)) SetP(FilterOn, 1f); },
                FilterType, Loc.T("device.beat_repeat.filter_off_or_a_low"));
            return (Head(on, type), Rows(
                Toggle(FilterNarrow, Loc.T("device.beat_repeat.narrow_with_repeats"), Loc.T("device.beat_repeat.narrow_with_repeats_each_repeat")),
                Rule(),
                Tip(SliderRow("PITCH", Pitch, PitchF, bipolar: true, modulation: true), Loc.T("device.beat_repeat.pitch_the_repeats_transposed_12")),
                Tip(SliderRow("P. DECAY", PitchDecay, PDecF), Loc.T("device.beat_repeat.pitch_decay_each_repeat_drops")),
                Info(() => PDecaySt() > 0.01 ? NotaNum.F($"{StF(PitchSt())} → {StF(LaterPitch())} by repeat {LaterRepeat()}") : "pitch " + StF(PitchSt()))));
        }

        // Under every panel: the output — Decay · Dry/wet (the main knob) · Volume of the repeats.
        Control OutputStrip()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(-4, 5, -4, 4) };
            Control Cell(Control c, int col) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Bottom; return Col(c, col); }
            g.Children.Add(Cell(K(Decay, "DECAY", DecayF, tip: Loc.T("device.beat_repeat.decay_each_repeat_quieter"), cellW: 54), 0));
            g.Children.Add(Cell(K(MixP, "DRY/WET", PctF, cellW: 58, size: 44), 1));
            g.Children.Add(Cell(K(Volume, "VOLUME", VolF, Teal, tip: Loc.T("device.beat_repeat.volume_the_repeats_level"), cellW: 54, bipolar: true), 2));
            return new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(8, 0), Child = g };
        }

        // ======================================================================
        // Tab frame + the panel that follows it
        // ======================================================================
        int centreTab = 0;
        var centreHost = new ContentControl();
        var panelHost = new ContentControl();
        var headHost = new ContentControl();
        var centreBodies = new Control?[3];
        var panels = new (Control Head, Control Body)?[3];
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"{IntervalText()} · slice {MsF(SliceMs(GridBeats()))}"),
                2 => On(FilterOn) ? (FType() == 1
                        ? NotaNum.F($"repeats only · band {HzF(FreqHz() / Math.Pow(2, WidthOct() / 2))}…{HzF(FreqHz() * Math.Pow(2, WidthOct() / 2))}")
                        : NotaNum.F($"repeats only · {(FType() == 0 ? "below" : "above")} {HzF(FreqHz())}"))
                    : Loc.T("device.beat_repeat.the_repeats_pass_unfiltered"),
                _ => NotaNum.F($"{WindowText()} · {Bpm():0.##} BPM"),
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => SlicesTab(), 2 => FilterTab(), _ => TimelineTab() };
        void ShowPanel(int t)
        {
            var (head, body) = panels[t] ??= t switch { 1 => CapturePanel(), 2 => FilterPanel(), _ => RepeatPanel() };
            headHost.Content = head;
            panelHost.Content = body;
            RefreshAll();
        }

        // ======================================================================
        // L: tab frame + the panel that follows it
        // ======================================================================
        Control LargeBody()
        {
            var centre = TabFrame(new[] { "Timeline", "Slices", "Filter" }, centreHost, CentreBody, false, t => { centreTab = t; ShowPanel(t); Refresh(); }, extras);
            ShowPanel(centreTab);
            var headBar = new Border { Height = 20, Padding = new Thickness(8, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = headHost };
            var rightCol = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), Docked(OutputStrip(), Dock.Bottom), panelHost } };
            var right = new RuleSection { Width = 186, Child = rightCol };
            DockPanel.SetDock(right, Dock.Right);
            var centreBox = new RuleSection { Margin = new Thickness(0, 0, 5, 0), Child = centre };

            // ---- assemble ---------------------------------------------------------------
            var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, centreBox } };
            return new Border { Background = NotaPalette.SurfaceCard, Child = bodyRow };
        }
        // ======================================================================
        // S: the Timeline over Interval · Repeat and Chance · Gate · Decay · Dry/wet
        // ======================================================================
        Control MiniBody()
        {
            centreTab = 0;
            timeline = new BrTimelineView();
            ToolTip.SetTip(timeline, Loc.T("device.beat_repeat.two_intervals_on_the_grid"));
            var sel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0),
                Children = { IntervalSegC(), RepeatButton(fill: false) } };
            var knobs = new UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 0), Children = {
                K(Chance, "CHANCE", ChanceF, tip: Loc.T("device.beat_repeat.chance_how_likely_each_interval"), cellW: 56),
                K(Gate, "GATE", GateF, steps: 17, tip: Loc.T("device.beat_repeat.gate_how_long_the_burst"), cellW: 56),
                K(Decay, "DECAY", DecayF, tip: Loc.T("device.beat_repeat.decay_each_repeat_quieter"), cellW: 56),
                K(MixP, "DRY/WET", PctF, cellW: 56) } };
            var body = new DockPanel { Margin = new Thickness(6), Children = { Docked(knobs, Dock.Bottom), Docked(sel, Dock.Bottom), timeline } };
            return new Border { Background = NotaPalette.SurfaceCard, Child = new RuleSection { Margin = new Thickness(5), Child = body } };
        }
        Control root = DeviceView.IsMini(engine, track, di) ? MiniBody() : LargeBody();

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            bool full = scN >= kScope;
            double ib = IntervalBeats();
            if (centreTab == 0 && timeline is not null)
                timeline.Set(scope, kCellAt, full ? kCells : 0, ib * 2, ib, GridBeats(), BarBeats(), OffSteps() / 16.0,
                    Sc(S_WinPhase), Sc(S_Playing) > 0.5, State() == 2 ? Loc.F("device.beat_repeat.capture_and_repeat_pass", RepeatNo()) : Loc.T("device.beat_repeat.capture_and_repeat"));
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
                    tb.Foreground = on ? TextPrimary : TextTertiary;
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
