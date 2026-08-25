// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
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

    private static readonly IBrush Hdr = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush Rail = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush Inset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush Bd = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush CardBg = new SolidColorBrush(Color.Parse("#26231E"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush AmberLit = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly IBrush TealB = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush Txt = new SolidColorBrush(Color.Parse("#E9E4D8"));
    private static readonly IBrush MutedB = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush Sub = new SolidColorBrush(Color.Parse("#A39D8F"));
    private static readonly IBrush Hue = new SolidColorBrush(Color.Parse("#3A362D"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#171613"));

    public double Width => 700;
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
            var lbl = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedB, Width = labelW, VerticalAlignment = VerticalAlignment.Center };
            var val = Mono(fmt(P(p)), Txt); val.Width = 38; val.TextAlignment = TextAlignment.Right;
            var fillBar = new Border { Height = 3, Background = fill, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 8, Height = 9, Background = Sub, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            slot.Children.Add(new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
            if (bipolar) slot.Children.Add(new Border { Width = 1, Background = Hue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1) });
            slot.Children.Add(fillBar); slot.Children.Add(handle);
            void Vis(double v) { double W = slot.Bounds.Width; fillBar.Width = v * W; handle.Margin = new Thickness(Math.Clamp(v * W - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); }
            bool drag = false;
            void SetX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); SetP(p, (float)v); Vis(v); val.Text = fmt(v); SyncViz(); }
            slot.PointerPressed += (_, e) => { drag = true; Begin(p); e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            MidiLearn.Bind(slot, MidiTarget.DeviceParam(track, di, p), label);
            ctx.AddDeviceRefresher(() => { if (!drag) { double v = P(p); Vis(v); val.Text = fmt(v); } });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slot, 1); Grid.SetColumn(val, 2);
            g.Children.Add(lbl); g.Children.Add(slot); g.Children.Add(val);
            return g;
        }
        // Compact inline slider for the LIVE strip (fixed width, no flexible column).
        Control MiniSlider(string label, int p, Func<double, string> fmt, double w)
        {
            var val = Mono(fmt(P(p)), Txt); val.MinWidth = 24;
            var fillBar = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 8, Height = 9, Background = Sub, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Width = w, Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
                Children = { new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fillBar, handle } };
            void Vis(double v) { fillBar.Width = v * w; handle.Margin = new Thickness(Math.Clamp(v * w - 4, 0, Math.Max(0, w - 8)), 0, 0, 0); }
            bool drag = false;
            void SetX(double x) { double v = Math.Clamp(x / w, 0, 1); SetP(p, (float)v); Vis(v); val.Text = fmt(v); }
            slot.PointerPressed += (_, e) => { drag = true; Begin(p); e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            MidiLearn.Bind(slot, MidiTarget.DeviceParam(track, di, p), label);
            ctx.AddDeviceRefresher(() => { if (!drag) { double v = P(p); Vis(v); val.Text = fmt(v); } });
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap(label), slot, val } };
        }

        // ---- segmented / toggles / buttons ----
        Control Seg(string[] names, Func<int> get, Action<int> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            var cells = new Border[names.Length]; var texts = new TextBlock[names.Length];
            void Sync() { int cur = get(); for (int i = 0; i < names.Length; i++) { bool on = i == cur; cells[i].Background = on ? Amber : Brushes.Transparent; texts[i].Foreground = on ? Ink : MutedB; texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; } }
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var t = new TextBlock { Text = names[i], FontSize = 9, Foreground = MutedB, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(7, 1) };
                var c = new Border { CornerRadius = new CornerRadius(2), Cursor = new Cursor(StandardCursorType.Hand), Child = t };
                c.PointerPressed += (_, e) => { e.Handled = true; set(iv); Sync(); };
                cells[i] = c; texts[i] = t; row.Children.Add(c);
            }
            Sync(); ctx.AddDeviceRefresher(Sync);
            return new Border { Background = Inset, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
        }
        Control PillToggle(string label, int p, IBrush accent, out Action sync)
        {
            var knob = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Ink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1.5, 0) };
            var pill = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), Background = CardBg, BorderBrush = Hue, BorderThickness = new Thickness(1), Child = knob };
            var lbl = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedB, VerticalAlignment = VerticalAlignment.Center };
            void Sync() { bool on = P(p) >= 0.5f; pill.Background = on ? accent : CardBg; pill.BorderBrush = on ? Brushes.Transparent : Hue; knob.Background = on ? Ink : MutedB; knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; lbl.Foreground = on ? accent : MutedB; }
            var b = new Border { Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { pill, lbl } } };
            b.PointerPressed += (_, e) => { e.Handled = true; SetP(p, P(p) >= 0.5f ? 0f : 1f); Sync(); };
            sync = Sync; Sync(); ctx.AddDeviceRefresher(Sync);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return b;
        }
        Control TagBtn(string label, IBrush border, IBrush bg, IBrush fg, Action<bool> onPress)
        {
            var b = new Border { BorderBrush = border, BorderThickness = new Thickness(1), Background = bg, CornerRadius = new CornerRadius(4), Padding = new Thickness(9, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
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
                var c = new Border { Height = 18, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Cursor = new Cursor(StandardCursorType.Hand), Child = t };
                c.PointerPressed += (_, e) => { e.Handled = true; SetP(p, iv / (float)(labels.Length - 1)); Sync(); SyncViz(); };
                cells[i] = c; texts[i] = t; Grid.SetColumn(c, i % 3); Grid.SetRow(c, i / 3); grid.Children.Add(c);
            }
            syncReadout = Sync; Sync(); ctx.AddDeviceRefresher(Sync);
            return new StackPanel { Children = { head, grid } };
        }
        static Control WithRight(Control c) { DockPanel.SetDock(c, Dock.Right); return c; }

        // ---- formatters ----
        static string PctF(double v) => $"{v * 100:0}%";
        static string StF(double v) => $"{(v - 0.5) * 24:+0;-0;0} st";
        static string DbF(double v) => $"{(v - 0.5) * 24:+0.0;-0.0;0.0} dB";
        static string StepF(double v) => $"{(int)Math.Round(v * 16)}/16";
        string HzF(double v) { double f = Exp(v, 50, 18000); return f >= 1000 ? $"{f / 1000:0.0}k" : $"{f:0} Hz"; }
        static string OctF(double v) => $"{0.5 + v * 3:0.0} oct";
        static string VarF(double v) => v <= 0.001 ? "off" : $"{v * 100:0}%";
        string SliceF() { double spb = 60.0 / Math.Max(1e-3, engine.Bpm); return $"slice {Grids[Idx(GridP, 6)] * spb * 1000:0} ms"; }

        // ============ LIVE strip ============
        var modeSeg = Seg(ModeLbl, () => Idx(Mode, 3), i => SetP(Mode, i / 2f));
        MidiLearn.Bind(modeSeg, MidiTarget.DeviceParam(track, di, Mode), engine.DeviceParamName(track, di, Mode));
        var repeatBtn = TagBtn("Repeat ⏎", Amber, new SolidColorBrush(Color.FromArgb(0x24, 0xD8, 0xA0, 0x3D)), AmberLit, held => SetP(Latch, held ? 1f : 0f));
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
        void FilterSync() { filterBlock.Opacity = P(FilterOn) >= 0.5f ? 1.0 : 0.4; filterBlock.IsHitTestVisible = P(FilterOn) >= 0.5f; }
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
