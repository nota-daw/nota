// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Beat Repeat (device kind 11) body, mockup 3h: a full
// 700×260 shell with a LIVE strip (mode + chance + gate + Repeat/Latch), a GRID rail
// (Interval / Grid buttons stating their musical consequence + Offset/Variation), the
// TIMELINE viz (captured slice → fading repeats → playhead) and a REPEAT CHARACTER rail
// (Pitch/Pitch Decay/Decay/Volume, a collapsing Filter, and Mix). Generic device params.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class BeatRepeatDeviceBody : IDeviceBody
{
    // Param indices — must match BeatRepeat.h.
    private const int Interval = 0, Offset = 1, GridP = 2, Variation = 3, Chance = 4, Gate = 5,
                      Pitch = 6, PitchDecay = 7, Volume = 8, Decay = 9, FilterOn = 10,
                      FilterFreq = 11, FilterWidth = 12, Mode = 13, MixP = 14, Latch = 15;
    private static readonly double[] Intervals = { 0.5, 1, 2, 4, 8, 16 };
    private static readonly string[] IntervalLbl = { "1/8", "1/4", "1/2", "1 Bar", "2 Bar", "4 Bar" };
    private static readonly string[] IntervalEvery = { "every 1/2 beat", "every 1 beat", "every 1/2 bar", "every 1 bar", "every 2 bars", "every 4 bars" };
    private static readonly double[] Grids = { 1, 0.5, 0.25, 0.125, 1.0 / 3.0, 1.0 / 6.0 };
    private static readonly string[] GridLbl = { "1/4", "1/8", "1/16", "1/32", "1/8T", "1/16T" };
    private static readonly string[] ModeLbl = { "Mix", "Insert", "Gate" };

    private static readonly IBrush Hdr = NotaPalette.SurfaceCard;
    private static readonly IBrush Rail = NotaPalette.SurfaceInset;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush CardBg = NotaPalette.SurfaceRaised;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealB = NotaPalette.Teal;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush MutedB = NotaPalette.TextTertiary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Hue = NotaPalette.BorderStrong;
    private static readonly IBrush Ink = NotaPalette.TextOnAccent;

    public double Width => 700;

    public string? Subtitle => "REPEATER";   // the processing type, shown as the header badge
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        int Idx(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static TextBlock Cap(string t, IBrush? c = null) => new() { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedB, VerticalAlignment = VerticalAlignment.Center };

        // ---- timeline viz ----
        var viz = new BeatRepeatViz { VerticalAlignment = VerticalAlignment.Stretch };
        var slots = new float[64];
        void SyncViz()
        {
            int n = engine.DeviceScope(track, di, slots, slots.Length);
            double phase = engine.DeviceGainReduction(track, di);
            viz.Set(slots, n, Intervals[Idx(Interval, 6)], engine.Bpm, phase);
        }
        ctx.AddDeviceRefresher(SyncViz);

        // ---- horizontal slider (label · track · value) ----
        Control Slider(string label, int p, Func<double, string> fmt, IBrush fill, double labelW = 46, bool bipolar = false)
        {
            var row = DeviceCardKit.SliderRow(label, () => P(p), n => { SetP(p, (float)n); SyncViz(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), bipolar: bipolar, labelWidth: labelW, valueWidth: 38);
            ctx.AddDeviceRefresher(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), label);
            return row;
        }
        // Compact inline slider for the LIVE strip (fixed width, no flexible column).
        Control MiniSlider(string label, int p, Func<double, string> fmt, double w)
        {
            var row = DeviceCardKit.SliderRow(label, () => P(p), n => SetP(p, (float)n), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), trackWidth: w, valueWidth: 28);
            ctx.AddDeviceRefresher(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), label);
            return row;
        }

        // ---- segmented / toggles / buttons ----
        Control Seg(string[] names, Func<int> get, Action<int> set)
        {
            var seg = DeviceCardKit.Segments(names, get, set, out var sync);
            ctx.AddDeviceRefresher(sync);
            return seg;
        }
        Control PillToggle(string label, int p, IBrush accent, out Action sync)
        {
            var b = DeviceCardKit.Switch(label, () => P(p) >= 0.5f, () => SetP(p, P(p) >= 0.5f ? 0f : 1f), out sync);
            ctx.AddDeviceRefresher(sync);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return b;
        }
        Control TagBtn(string label, IBrush border, IBrush bg, IBrush fg, Action<bool> onPress)
        {
            var b = new Border { BorderBrush = border, BorderThickness = new Thickness(1), Background = bg, CornerRadius = NotaRadius.Control, Padding = new Thickness(9, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = label, FontSize = 9, Foreground = fg } };
            b.PointerPressed += (_, e) => { e.Handled = true; onPress(true); };
            b.PointerReleased += (_, _) => onPress(false);
            return b;
        }

        // ---- INTERVAL / GRID button grid (3×2) with a live readout ----
        Control BtnGrid(int p, string[] labels, string headLabel, Func<string> readout, out Action syncReadout)
        {
            var readTb = Mono(readout(), Sub, 8);
            var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 3), Children = { Cap(headLabel), WithRight(readTb) } };
            var grid = new Grid { ColumnSpacing = 3, RowSpacing = 3 };
            for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            for (int r = 0; r < 2; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = new Border[labels.Length]; var texts = new TextBlock[labels.Length];
            void Sync() { int cur = Idx(p, labels.Length); for (int i = 0; i < labels.Length; i++) { bool on = i == cur; cells[i].Background = on ? Amber : Inset; cells[i].BorderBrush = on ? Amber : Bd; texts[i].Foreground = on ? Ink : Sub; } readTb.Text = readout(); }
            for (int i = 0; i < labels.Length; i++)
            {
                int iv = i;
                var t = Mono(labels[i], Sub); t.HorizontalAlignment = HorizontalAlignment.Center;
                var c = new Border { Height = 18, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Cursor = new Cursor(StandardCursorType.Hand), Child = t };
                c.PointerPressed += (_, e) => { e.Handled = true; SetP(p, iv / (float)(labels.Length - 1)); Sync(); SyncViz(); };
                cells[i] = c; texts[i] = t; Grid.SetColumn(c, i % 3); Grid.SetRow(c, i / 3); grid.Children.Add(c);
            }
            syncReadout = Sync; Sync(); ctx.AddDeviceRefresher(Sync);
            return new StackPanel { Children = { head, grid } };
        }
        static Control WithRight(Control c) { DockPanel.SetDock(c, Dock.Right); return c; }

        // ---- formatters ----
        static string PctF(double v) => $"{v * 100:0}\u2009%";
        static string StF(double v) => $"{(v - 0.5) * 24:+0;−0;0}\u2009st";
        static string DbF(double v) => $"{(v - 0.5) * 24:+0.0;−0.0;0.0}\u2009dB";
        static string StepF(double v) => $"{(int)Math.Round(v * 16)}/16";
        string HzF(double v) { double f = Exp(v, 50, 18000); return f >= 1000 ? $"{f / 1000:0.0}k" : $"{f:0}\u2009Hz"; }
        static string OctF(double v) => $"{0.5 + v * 3:0.0}\u2009oct";
        static string VarF(double v) => v <= 0.001 ? "off" : $"{v * 100:0}\u2009%";
        string SliceF() { double spb = 60.0 / Math.Max(1e-3, engine.Bpm); return $"slice {Grids[Idx(GridP, 6)] * spb * 1000:0}\u2009ms"; }

        // ============ LIVE strip ============
        var modeSeg = Seg(ModeLbl, () => Idx(Mode, 3), i => SetP(Mode, i / 2f));
        MidiLearn.Bind(modeSeg, MidiTarget.DeviceParam(track, di, Mode), engine.DeviceParamName(track, di, Mode));
        var repeatBtn = TagBtn("Repeat ⏎", Amber, NotaPalette.Wash(NotaPalette.Accent, 0x24), AmberLit, held => SetP(Latch, held ? 1f : 0f));
        var latchBtn = PillToggle("Latch", Latch, Amber, out _);
        var liveLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = {
            modeSeg, MiniSlider("CHANCE", Chance, PctF, 66), MiniSlider("GATE", Gate, StepF, 56) } };
        var liveRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right, Children = { repeatBtn, latchBtn } };
        var liveStrip = new Border { Height = 34, Background = Hdr, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = new DockPanel { LastChildFill = false, Children = { liveLeft, liveRight } } };

        // ============ GRID rail (174) ============
        var intervalGrid = BtnGrid(Interval, IntervalLbl, "INTERVAL", () => IntervalEvery[Idx(Interval, 6)], out _);
        var gridGrid = BtnGrid(GridP, GridLbl, "GRID", SliceF, out _);
        var offsetVar = new StackPanel { Spacing = 4, [DockPanel.DockProperty] = Dock.Bottom, Children = {
            Slider("OFFSET", Offset, StepF, Amber, 52), Slider("VARIATION", Variation, VarF, TealB, 52) } };
        var gridInner = new DockPanel { LastChildFill = true, Children = { offsetVar, new StackPanel { Spacing = 6, Children = { intervalGrid, gridGrid } } } };
        var gridPanel = new Border { Width = 174, Background = Rail, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 6), Child = gridInner };

        // ============ REPEAT CHARACTER rail (186) ============
        var freqW = Slider("FREQ", FilterFreq, HzF, Amber, 40);
        var widthW = Slider("WIDTH", FilterWidth, OctF, Amber, 40);
        var filterBlock = new StackPanel { Spacing = 4, Children = { freqW, widthW } };
        void FilterSync() => Inactive.Set(filterBlock, P(FilterOn) < 0.5f);
        var filterToggle = PillToggle("FILTER", FilterOn, Amber, out _);
        ctx.AddDeviceRefresher(FilterSync); FilterSync();
        var mix = Slider("MIX", MixP, PctF, Amber, 52);
        DockPanel.SetDock(mix, Dock.Bottom);
        var railTop = new StackPanel { Spacing = 4, Children = {
            Cap("REPEAT CHARACTER"),
            Slider("Pitch", Pitch, StF, Amber, 52, bipolar: true),
            Slider("Pitch Dec", PitchDecay, PctF, Amber, 52),
            Slider("Decay", Decay, PctF, Amber, 52),
            Slider("Volume", Volume, DbF, Amber, 52, bipolar: true),
            new Border { Height = 1, Background = CardBg, Margin = new Thickness(0, 2) },
            filterToggle, filterBlock } };
        var rail = new Border { Width = 186, Background = Rail, BorderBrush = Bd, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6),
            Child = new DockPanel { LastChildFill = true, Children = { mix, railTop } } };

        // ============ timeline (fill) ============
        var tracePanel = new Border { Padding = new Thickness(8, 6), Child = viz };

        DockPanel.SetDock(gridPanel, Dock.Left); DockPanel.SetDock(rail, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { gridPanel, rail, tracePanel } };
        DockPanel.SetDock(liveStrip, Dock.Top);
        SyncViz();
        return new DockPanel { LastChildFill = true, Children = { liveStrip, body } };
    }
}
