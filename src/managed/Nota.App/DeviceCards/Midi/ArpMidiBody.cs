// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Arp editor (MIDI effect kind 0), almanac mockups 1a / 1b.
// Two sizes share every value; the S / L toggle in the shell header flips the card (the
// choice is the device's "View" param, so it persists with the project):
//   • L 770 × 260 — ORDER list | Pattern / Groove window over the Free·Sync + division
//     strip | PLAY (Gate · Swing · Vel Amt sliders, Octaves, Retrig, Transpose and Steps,
//     Hold, Restart), in pressed-in sections.
//   • S 260 × 260 — the window with a Pattern / Groove toggle (the lane picked by clicking
//     its name) over four knobs (Rate · Gate · Oct · Swing). The header keeps the full preset
//     picker (same list, same current preset as L); what S can't edit, presets still set.
// The window (ArpGrid) animates from the engine's telemetry: the head band walks the
// steps, played cells dim to a neutral wash and the sounding one turns accent.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.ArpGrid;

namespace Nota.App;

internal sealed class ArpMidiBody : IMidiDeviceBody
{
    internal static readonly string[] RateNames = { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    internal static readonly double[] RateDivisions = { 4.0, 2.0, 1.0, 0.5, 1.0 / 3.0, 0.25, 1.0 / 6.0, 0.125 };
    // The ORDER list, in musical order → the persisted Order value (new orders were appended).
    internal static readonly (string Name, int Value)[] Orders =
    {
        ("Up", ArpPattern.OrdUp), ("Down", ArpPattern.OrdDown), ("Up · Down", ArpPattern.OrdUpDown),
        ("Down · Up", ArpPattern.OrdDownUp), ("Converge", ArpPattern.OrdConverge), ("Diverge", ArpPattern.OrdDiverge),
        ("Random", ArpPattern.OrdRandom), ("Chord", ArpPattern.OrdChord), ("As played", ArpPattern.OrdAsPlayed),
    };
    private static readonly string[] RetrigNames = { "Off", "Note", "Beat" };
    private const double FreeMsMin = 20, FreeMsMax = 1000, LabelW = 60;

    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;

    // Per-card editor state that survives rebuilds (tab + Groove lane) — UI only, not persisted.
    private sealed class Ui { public bool Groove; public int Lane; }
    private static readonly Dictionary<(int, int), Ui> UiState = new();
    private static Ui UiFor(int track, int mi) { if (!UiState.TryGetValue((track, mi), out var u)) UiState[(track, mi)] = u = new Ui(); return u; }

    public double Width => 770;
    public bool FullBleed => true;

    private static bool IsMini(IAudioEngine e, int track, int mi) => e.MidiEffectParamCount(track, mi) > PView && e.MidiEffectGetParam(track, mi, PView) >= 0.5f;
    public double WidthFor(IAudioEngine engine, int trackId, int index) => IsMini(engine, trackId, index) ? 260 : 770;

    // ---- small builders -------------------------------------------------------------------
    private static TextBlock Caps(string t, IBrush? c = null, double fs = 0) => DeviceBodyKit.Label(t, c);
    // The old small sizes land on the device scale: 6–7 → 10.5 (a label / axis), 8–9 → 11 (a value).
    private static double Fs(double fs) => fs < 8 ? NotaType.RowLabel : fs < 10 ? NotaType.KnobValue : fs;
    private static TextBlock Mono(string t, IBrush c, double fs = NotaType.KnobValue) => new()
    { Text = t, FontSize = Fs(fs), FontFamily = NotaFonts.UiFamily, Foreground = c, VerticalAlignment = VerticalAlignment.Center };

    private static string RateLabel(IAudioEngine e, int t, int mi)
        => e.MidiEffectGetParam(t, mi, GSync) > 0.5f ? RateNames[Math.Clamp((int)Math.Round(e.MidiEffectGetParam(t, mi, GRate)), 0, 7)]
                                                       : $"{FreeMs(e.MidiEffectGetParam(t, mi, GFreeRate)):0}\u2009ms";
    private static double FreeMs(double hz) => Math.Clamp(1000.0 / Math.Max(0.1, hz), FreeMsMin, FreeMsMax);
    private static string OrderName(int v) => Orders.FirstOrDefault(o => o.Value == v).Name ?? "Up";

    // ---- header accessory: L = MIDI Learn · tempo · S/L; S = S/L only (the shell keeps the same
    //      preset picker in both sizes — presets belong to the device, not to the card size) ----
    public Control? HeaderAccessory(DeviceCardContext ctx, int index)
    {
        var e = ctx.Engine; int t = ctx.TrackId, mi = index;
        if (e.MidiEffectParamCount(t, mi) <= PView) return null;
        bool mini = IsMini(e, t, mi);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        if (!mini)
        {
            var bpm = new TextBlock { FontSize = NotaType.Caption, Foreground = Cap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-4, 0, 0, 0) };
            ctx.AddDeviceRefresher(() => bpm.Text = $"· {e.Bpm:0.0} BPM");
            bpm.Text = $"· {e.Bpm:0.0} BPM";
            row.Children.Add(bpm); row.Children.Add(MidiCardHeader.LearnButton(ctx));
        }

        row.Children.Add(MidiCardHeader.SizeToggle(ctx, mi, PView, () => IsMini(e, t, mi)));
        return row;
    }

    private static T WithDock<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }

    // ---- body -------------------------------------------------------------------------------
    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, mi = index;
        if (engine.MidiEffectParamCount(track, mi) <= PView) return new TextBlock { Text = "Nota Arp", Margin = new Thickness(8) };
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        int GI(int p) => (int)Math.Round(G(p));
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        float Def(int p) => engine.MidiEffectParamDefault(track, mi, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void Learn(Control c, int p, string name) => MidiLearn.Bind(c, MidiTarget.MidiDeviceParam(track, mi, p), name);

        var ui = UiFor(track, mi);
        bool mini = IsMini(engine, track, mi);
        var readouts = new List<Action>();
        void Refresh() { foreach (var r in readouts) r(); }

        var grid = new ArpGrid(engine, track, mi) { Mini = mini, Groove = ui.Groove, LaneIndex = ui.Lane };
        grid.Edited += Refresh;

        // Segmented control bound to a param (value = index + off).
        Border Seg(int p, string[] names, int off = 0, bool fill = true, double padX = 6)
        {
            var seg = DeviceCardKit.Segments(names, () => { int c = GI(p) - off; return c >= 0 && c < names.Length ? c : -1; },
                i => { S(p, i + off); Refresh(); }, out var sync, fill: fill, padX: padX);
            readouts.Add(sync);
            Learn(seg, p, engine.MidiEffectParamName(track, mi, p));
            return seg;
        }



        // ---- the window's header readout ----
        string InfoText(bool shortForm)
        {
            if (grid.Groove)
            {
                var lane = Lanes[grid.LaneIndex];
                (int s, double v) = grid.LastEdit ?? (Math.Max(0, grid.Position), lane.ToDisp(G(lane.Base + Math.Max(0, grid.Position))));
                v = Math.Clamp(v, lane.Min, lane.Max);
                return shortForm ? $"{s + 1} · {lane.Fmt(v)}" : $"step {s + 1} · {lane.Fmt(v)}";
            }
            int len = Math.Max(1, grid.CurrentSequence().Count);
            if (shortForm) return $"{(grid.Position >= 0 ? grid.Position + 1 : 1)}/{grid.StepCount}";
            if (grid.Chord.Count == 0) return "preview · play a chord";
            return $"{grid.Chord.Count} notes · {Math.Clamp(GI(GOctaves), 1, 8)} oct · cycle {len} steps";
        }

        Control root = mini ? BuildMini() : BuildLarge();
        ctx.AddDeviceRefresher(() => { grid.Poll(); Refresh(); });
        Refresh();
        return root;

        // =======================================================================================
        Control BuildLarge()
        {
            // ---- ORDER ----
            var list = new StackPanel();
            foreach (var (name, value) in Orders)
            {
                int v = value;
                var tb = new TextBlock { Text = name, FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                var rowB = new Border { Height = 19, CornerRadius = NotaRadius.Field, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                bool hover = false;
                void Paint()
                {
                    bool on = GI(GOrder) == v;
                    rowB.Background = on ? NotaPalette.AccentSoft : hover ? NotaPalette.GridBeat : Brushes.Transparent;
                    tb.Foreground = on ? NotaPalette.AccentBright : Sub; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
                rowB.PointerEntered += (_, _) => { hover = true; Paint(); };
                rowB.PointerExited += (_, _) => { hover = false; Paint(); };
                rowB.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(rowB).Properties.IsLeftButtonPressed) return; S(GOrder, v); Refresh(); ev.Handled = true; };
                Learn(rowB, GOrder, "Order");
                readouts.Add(Paint);
                list.Children.Add(rowB);
            }
            var orderIsland = IslandBox(IslandHead(Caps("Order"), null), list, 110);

            // ---- center: tabs + window + rate strip ----
            var info = Mono("", Cap, 7);
            Action layoutFor = () => { };
            var tabs = DeviceBodyKit.Tabs(new[] { "Pattern", "Groove" }, () => grid.Groove ? 1 : 0,
                i => { ui.Groove = grid.Groove = i == 1; layoutFor(); Refresh(); }, out var tabSync, info);
            tabs.Margin = new Thickness(0, 0, 0, 8);
            readouts.Add(tabSync);

            // Groove lane chips.
            var laneSeg = DeviceCardKit.Segments(Lanes.Select(l => l.Label).ToArray(), () => grid.LaneIndex,
                i => { ui.Lane = grid.LaneIndex = i; Refresh(); }, out var laneSync, fill: true, padX: 2);
            laneSeg.Margin = new Thickness(0, 0, 0, 6);
            readouts.Add(laneSync);

            // Rate strip: Free / Sync + divisions, or the free-rate slider.
            var mode = Seg(GSync, new[] { "Free", "Sync" }, fill: false, padX: 4);
            var rateGrid = Seg(GRate, RateNames, padX: 0);
            Func<double> freeNorm = () => (FreeMs(G(GFreeRate)) - FreeMsMin) / (FreeMsMax - FreeMsMin);
            Action<double> setFree = n => S(GFreeRate, 1000.0 / (FreeMsMin + n * (FreeMsMax - FreeMsMin)));
            var freeRow = DeviceCardKit.SliderRow("", freeNorm, n => { setFree(n); Refresh(); }, () => $"{FreeMs(G(GFreeRate)):0}\u2009ms", out var freeSync,
                () => Begin(GFreeRate), () => End(GFreeRate), () => { Begin(GFreeRate); S(GFreeRate, Def(GFreeRate)); End(GFreeRate); Refresh(); }, valueWidth: 48);
            Learn(freeRow.Children.OfType<SliderTrack>().First(), GFreeRate, "Free rate");
            readouts.Add(freeSync);
            var rateHost = new Panel { Margin = new Thickness(8, 0, 0, 0), Children = { rateGrid, freeRow } };
            var rateStrip = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { WithDock(mode, Dock.Left), rateHost } };

            var winRow = new DockPanel { Children = { WithDock(tabs, Dock.Top), WithDock(laneSeg, Dock.Top), WithDock(rateStrip, Dock.Bottom), grid } };
            var centerIsland = DeviceBodyKit.Sec(winRow);

            void LayoutFor() => laneSeg.IsVisible = grid.Groove;
            layoutFor = LayoutFor;
            LayoutFor();
            readouts.Add(() =>
            {
                info.Text = InfoText(false);
                info.Foreground = grid.Groove ? Txt : Sub;
                bool sync = G(GSync) > 0.5f;
                rateGrid.IsVisible = sync; freeRow.IsVisible = !sync;
            });

            // ---- PLAY ----
            var steps = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelW - 8},*,Auto,*"), ColumnSpacing = 8 };
            steps.Children.Add(Caps("Transpose"));
            steps.Children.Add(DeviceBodyKit.At(Stepper("Transpose", GTranspose, -24, 24, v => v == 0 ? "0\u2009st" : $"{(v > 0 ? "+" : "−")}{Math.Abs(v)}\u2009st"), 1));
            steps.Children.Add(DeviceBodyKit.At(Caps("Steps"), 2));
            steps.Children.Add(DeviceBodyKit.At(Stepper("Steps", GLoop, 1, 16, v => v.ToString()), 3));
            var play = new StackPanel { Spacing = 3, Children =
            {
                SliderLine("Gate", GGate, v => v / 2, n => n * 2, v => $"{v * 100:0}\u2009%"),
                SliderLine("Swing", GSwing, v => v, n => n, v => $"{v * 50:0}\u2009%"),
                SliderLine("Vel amt", PVelAmt, v => v, n => n, v => $"{v * 100:0}\u2009%"),
                DeviceBodyKit.Rule(),
                Line("Octaves", Seg(GOctaves, new[] { "1", "2", "3", "4" }, off: 1)),
                Line("Retrig", Seg(GRetrig, RetrigNames)),
                steps,
            } };
            var buttons = ButtonsRow();
            var playHead = IslandHead(Caps("Play"), Mono("MIDI out · ch 1", Sub));
            playHead.Margin = new Thickness(0, 0, 0, 6);
            var playIsland = IslandBox(playHead, new DockPanel { Children =
            {
                WithDock(new StackPanel { Spacing = 3, Margin = new Thickness(0, 3, 0, 0), Children = { DeviceBodyKit.Rule(), buttons } }, Dock.Bottom), play,
            } }, 253);
            playIsland.Padding = new Thickness(12, 12, 12, 10);

            var islands = new DockPanel { Children = { WithDock(orderIsland, Dock.Left), WithDock(playIsland, Dock.Right), centerIsland } };
            orderIsland.Margin = new Thickness(0, 0, DeviceCardKit.SectionGap, 0); playIsland.Margin = new Thickness(DeviceCardKit.SectionGap, 0, 0, 0);
            return DeviceBodyKit.Frame(islands);
        }

        // =======================================================================================
        Control BuildMini()
        {
            var tabs = DeviceCardKit.Segments(new[] { "Pattern", "Groove" }, () => grid.Groove ? 1 : 0,
                i => { ui.Groove = grid.Groove = i == 1; Refresh(); }, out var tabSync, padX: 6);
            readouts.Add(tabSync);
            var laneName = new TextBlock { FontSize = NotaType.Caption, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
            var laneBtn = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
                Children = { laneName, new Glyph { Kind = GlyphKind.ChevronDown, Width = 8, Height = 8, Foreground = Cap, VerticalAlignment = VerticalAlignment.Center } } };
            ToolTip.SetTip(laneBtn, "Next Groove lane");
            laneBtn.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(laneBtn).Properties.IsLeftButtonPressed) return;
                ui.Lane = grid.LaneIndex = (grid.LaneIndex + 1) % Lanes.Length; Refresh(); ev.Handled = true;
            };
            var info = Mono("", Cap, 7);
            var head = new DockPanel { Height = 24, Margin = new Thickness(0, 0, 0, 8),
                Children = { WithDock(tabs, Dock.Left), WithDock(info, Dock.Right), laneBtn } };
            laneBtn.Margin = new Thickness(8, 0, 0, 0);

            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(0, 8, 0, 0) };
            var cells = new[]
            {
                KnobCell("Rate", GRate,
                    () => G(GSync) > 0.5f ? GI(GRate) / 7.0 : (FreeMs(G(GFreeRate)) - FreeMsMin) / (FreeMsMax - FreeMsMin),
                    n => { if (G(GSync) > 0.5f) S(GRate, Math.Round(n * 7)); else S(GFreeRate, 1000.0 / (FreeMsMin + n * (FreeMsMax - FreeMsMin))); },
                    () => RateLabel(engine, track, mi), () => G(GSync) > 0.5f ? GRate : GFreeRate),
                KnobCell("Gate", GGate, () => G(GGate) / 2, n => S(GGate, n * 2), () => $"{G(GGate) * 100:0}\u2009%"),
                KnobCell("Oct", GOctaves, () => (Math.Clamp(GI(GOctaves), 1, 4) - 1) / 3.0, n => S(GOctaves, Math.Round(n * 3) + 1), () => $"{GI(GOctaves)}"),
                KnobCell("Swing", GSwing, () => G(GSwing), n => S(GSwing, n), () => $"{G(GSwing) * 50:0}\u2009%"),
            };
            for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); knobs.Children.Add(cells[i]); }

            var inner = new DockPanel { Children = { WithDock(head, Dock.Top), WithDock(knobs, Dock.Bottom), grid } };
            var island = DeviceBodyKit.Sec(inner, new Thickness(10, 10, 10, 8));

            readouts.Add(() =>
            {
                laneBtn.IsVisible = grid.Groove;
                laneName.Text = Lanes[grid.LaneIndex].Label;
                info.Text = engine.MidiEffectBypassed(track, mi) ? "bypassed" : InfoText(true);
                info.Foreground = grid.Groove ? Txt : Cap;
            });

            return DeviceBodyKit.Frame(island);
        }

        // ---- shared pieces ---------------------------------------------------------------------

        Control IslandHead(Control left, Control? right)
        {
            if (left is TextBlock t) { t.FontSize = NotaType.Caption; t.FontWeight = FontWeight.SemiBold; t.Foreground = NotaPalette.TextPrimary; }
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            if (right is not null) { right.HorizontalAlignment = HorizontalAlignment.Right; d.Children.Add(WithDock(right, Dock.Right)); }
            d.Children.Add(left);
            return d;
        }

        Decorator IslandBox(Control head, Control content, double width)
        {
            return DeviceBodyKit.Sec(new DockPanel { Children = { WithDock(head, Dock.Top), content } }, DeviceCardKit.SectionPad, width);
        }

        Control Line(string label, Control c)
        {
            var l = Caps(label); l.Width = LabelW;
            return new DockPanel { Children = { WithDock(l, Dock.Left), c } };
        }

        // A play slider: label · value bar · value.
        Control SliderLine(string label, int p, Func<double, double> toNorm, Func<double, double> fromNorm, Func<double, string> fmt)
        {
            var row = DeviceCardKit.SliderRow(label, () => toNorm(G(p)), n => { S(p, fromNorm(n)); Refresh(); }, () => fmt(G(p)), out var sync,
                () => Begin(p), () => End(p), () => { Begin(p); S(p, Def(p)); End(p); Refresh(); },
                labelWidth: LabelW - 8, valueWidth: 40);
            Learn(row.Children.OfType<SliderTrack>().First(), p, label);
            readouts.Add(sync);
            return row;
        }

        Control Stepper(string label, int p, int min, int max, Func<int, string> fmt)
        {
            var val = Mono("", Txt); val.HorizontalAlignment = HorizontalAlignment.Center;
            Border Btn(string glyph, int d)
            {
                var tb = Mono(glyph, Sub); tb.HorizontalAlignment = HorizontalAlignment.Center;
                var b = new Border { Width = 18, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerEntered += (_, _) => tb.Foreground = Txt; b.PointerExited += (_, _) => tb.Foreground = Sub;
                b.PointerPressed += (_, ev) =>
                {
                    if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                    S(p, Math.Clamp(GI(p) + d, min, max)); Refresh(); ev.Handled = true;
                };
                return b;
            }
            var box = new Border { Height = 22, Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field,
                Child = new DockPanel { Children = { WithDock(Btn("−", -1), Dock.Left), WithDock(Btn("+", +1), Dock.Right), val } } };
            box.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
            box.PointerPressed += (_, ev) => { if ((ev.ClickCount == 2 || (ev.KeyModifiers & KeyModifiers.Alt) != 0)) { S(p, Def(p)); Refresh(); } };
            Learn(box, p, label);
            readouts.Add(() => val.Text = fmt(GI(p)));
            return box;
        }

        Control ButtonsRow()
        {
            Border Btn(string text, Action click, out TextBlock tb)
            {
                var b = DeviceCardKit.TextButton(text);
                var t = (TextBlock)b.Child!;
                b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Height = 22; b.Padding = new Thickness(6, 0);
                t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
                b.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); ev.Handled = true; };
                tb = t; return b;
            }
            var hold = Btn("Hold", () => { S(GHold, G(GHold) > 0.5f ? 0 : 1); Refresh(); }, out var holdTb);
            ToolTip.SetTip(hold, "Hold: keep arpeggiating after the keys are released");
            Learn(hold, GHold, "Hold");
            readouts.Add(() =>
            {
                bool on = G(GHold) > 0.5f;
                hold.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                holdTb.Foreground = on ? NotaPalette.AccentBright : Txt;
                holdTb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            });
            var restart = Btn("Restart", () => engine.MidiEffectCommand(track, mi, 1), out _);
            ToolTip.SetTip(restart, "Restart the pattern from step 1");
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
            Grid.SetColumn(restart, 1); g.Children.Add(hold); g.Children.Add(restart);
            return g;
        }

        // A mini-view knob: the device knob, value (11, ink 1) over label (10.5, ink 3). `learnParam` names the param the
        // knob drives right now (Rate is the division when synced, the free rate otherwise).
        Control KnobCell(string label, int p, Func<double> norm, Action<double> set, Func<string> text, Func<int>? learnParam = null)
        {
            var knob = new Knob(norm(), 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center };
            int LP() => learnParam?.Invoke() ?? p;
            knob.ValueChanged += n => { set(n); Refresh(); };
            knob.GestureBegin += () => Begin(LP()); knob.GestureEnd += () => End(LP());
            Learn(knob, p, label);
            var val = Mono("", Txt); val.HorizontalAlignment = HorizontalAlignment.Center; val.Margin = new Thickness(0, 4, 0, 0);
            ValueEntry.Attach(knob, label, val, text);
            readouts.Add(() => { double n = norm(); if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n; val.Text = text(); });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { knob, val, new TextBlock { Text = NotaText.Label(label), FontSize = NotaType.KnobLabel, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 13 } } };
        }
    }
}
