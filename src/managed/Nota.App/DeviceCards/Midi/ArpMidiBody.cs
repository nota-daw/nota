// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Arp editor (MIDI effect kind 0), almanac mockups 1a / 1b.
// Two sizes share every value; the S / L toggle in the shell header flips the card (the
// choice is the device's "View" param, so it persists with the project):
//   • L 700 × 260 — ORDER list | Pattern / Groove window over the Free·Sync + division
//     strip | PLAY (Gate · Swing · Vel Amt sliders, Octaves, Retrig, Transpose, Steps,
//     Hold, Restart) — over a status line (held chord · order · rate · steps · retrig).
//   • S 260 × 260 — the window with a Pattern / Groove toggle (the lane picked by clicking
//     its name) over four knobs (Rate · Gate · Oct · Swing). The header keeps the full preset
//     picker (same list, same current preset as L); what S can't edit, presets still set.
// The window (ArpGrid) animates from the engine's telemetry: the head band walks the
// steps, played cells turn Brass Deep and the sounding one Brass Light.

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
    private const double FreeMsMin = 20, FreeMsMax = 1000, LabelW = 50;

    private static readonly IBrush Ground = NotaPalette.SurfaceInset;
    private static readonly IBrush Island = NotaPalette.SurfaceCard;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Rule = NotaPalette.GraphBorder;
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush Chip = NotaPalette.GridBeat;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush BrassLit = NotaPalette.AccentBright;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;
    private static readonly IBrush Ink = NotaPalette.TextOnAccent;

    // Per-card editor state that survives rebuilds (tab + Groove lane) — UI only, not persisted.
    private sealed class Ui { public bool Groove; public int Lane; }
    private static readonly Dictionary<(int, int), Ui> UiState = new();
    private static Ui UiFor(int track, int mi) { if (!UiState.TryGetValue((track, mi), out var u)) UiState[(track, mi)] = u = new Ui(); return u; }

    public double Width => 700;
    public bool FullBleed => true;

    private static bool IsMini(IAudioEngine e, int track, int mi) => e.MidiEffectParamCount(track, mi) > PView && e.MidiEffectGetParam(track, mi, PView) >= 0.5f;
    public double WidthFor(IAudioEngine engine, int trackId, int index) => IsMini(engine, trackId, index) ? 260 : 700;

    // ---- small builders -------------------------------------------------------------------
    private static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
    { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, LetterSpacing = 0.6, Foreground = c ?? Cap, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Mono(string t, IBrush c, double fs = 8) => new()
    { Text = t, FontSize = fs, FontFamily = NotaFonts.MonoFamily, Foreground = c, VerticalAlignment = VerticalAlignment.Center };

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
            var bpm = Mono("", Cap);
            ctx.AddDeviceRefresher(() => bpm.Text = $"{e.Bpm:0.0} BPM");
            bpm.Text = $"{e.Bpm:0.0} BPM";
            row.Children.Add(MidiCardHeader.LearnButton(ctx)); row.Children.Add(bpm);
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
                i => { S(p, i + off); Refresh(); }, out var sync, fill: fill, padX: padX, fontSize: 8);
            readouts.Add(sync);
            Learn(seg, p, engine.MidiEffectParamName(track, mi, p));
            return seg;
        }

        // ---- the status strings ----
        string ChordText()
        {
            var c = grid.Chord;
            return c.Count == 0 ? "no notes yet" : string.Join(" ", c.Select(DeviceCardKit.NoteName));
        }
        string RetrigText() => RetrigNames[Math.Clamp(GI(GRetrig), 0, 2)].ToLowerInvariant();

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
            var list = new StackPanel { Margin = new Thickness(0, 3) };
            foreach (var (name, value) in Orders)
            {
                int v = value;
                var bar = new Border { Width = 2, Margin = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Left };
                var tb = new TextBlock { Text = name, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                var rowB = new Border { Height = 18, Cursor = new Cursor(StandardCursorType.Hand), Child = new Panel { Children = { bar, tb } } };
                bool hover = false;
                void Paint()
                {
                    bool on = GI(GOrder) == v;
                    rowB.Background = on ? NotaPalette.AccentSubtle : hover ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                    bar.Background = on ? Brass : Brushes.Transparent;
                    tb.Foreground = on ? Txt : Sub; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
                rowB.PointerEntered += (_, _) => { hover = true; Paint(); };
                rowB.PointerExited += (_, _) => { hover = false; Paint(); };
                rowB.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(rowB).Properties.IsLeftButtonPressed) return; S(GOrder, v); Refresh(); ev.Handled = true; };
                Learn(rowB, GOrder, "Order");
                readouts.Add(Paint);
                list.Children.Add(rowB);
            }
            var orderIsland = IslandBox(IslandHead(Caps("ORDER"), null), list, 100);

            // ---- center: tabs + window + rate strip ----
            var info = Mono("", Cap, 7); info.Margin = new Thickness(0, 0, 8, 0);
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
            var tabs = new (Border b, TextBlock tb)[2];
            Action layoutFor = () => { };
            string[] tabNames = { "Pattern", "Groove" };
            for (int i = 0; i < 2; i++)
            {
                bool groove = i == 1;
                var tb = new TextBlock { Text = tabNames[i], FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; ui.Groove = grid.Groove = groove; layoutFor(); Refresh(); ev.Handled = true; };
                tabs[i] = (b, tb); tabRow.Children.Add(b);
            }
            var tabHead = new Border { Height = 20, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new DockPanel { Children = { WithDock(info, Dock.Right), tabRow } } };

            // Groove lane chips.
            var laneGrid = new Grid { Height = 15, ColumnSpacing = 2, Margin = new Thickness(0, 0, 0, 4) };
            var laneChips = new (Border b, TextBlock tb)[Lanes.Length];
            for (int i = 0; i < Lanes.Length; i++)
            {
                int li = i;
                laneGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                var tb = new TextBlock { Text = Lanes[i].Label, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { CornerRadius = NotaRadius.Clip, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; ui.Lane = grid.LaneIndex = li; Refresh(); ev.Handled = true; };
                Grid.SetColumn(b, i); laneGrid.Children.Add(b); laneChips[i] = (b, tb);
            }

            // Rate strip: Free / Sync + divisions, or the free-rate slider.
            var mode = Seg(GSync, new[] { "Free", "Sync" }, fill: false);
            var rateGrid = new Grid { ColumnSpacing = 2, Height = 15, VerticalAlignment = VerticalAlignment.Center };
            var rateChips = new (Border b, TextBlock tb)[RateNames.Length];
            for (int i = 0; i < RateNames.Length; i++)
            {
                int ri = i;
                rateGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                var tb = Mono(RateNames[i], Cap); tb.HorizontalAlignment = HorizontalAlignment.Center;
                var b = new Border { CornerRadius = NotaRadius.Clip, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; S(GRate, ri); Refresh(); ev.Handled = true; };
                Learn(b, GRate, "Rate");
                Grid.SetColumn(b, i); rateGrid.Children.Add(b); rateChips[i] = (b, tb);
            }
            var freeTrack = new SliderTrack { Margin = new Thickness(3, 0), Reset = () => { S(GFreeRate, Def(GFreeRate)); Refresh(); } };
            freeTrack.Changed += n => { S(GFreeRate, 1000.0 / (FreeMsMin + n * (FreeMsMax - FreeMsMin))); Refresh(); };
            freeTrack.GestureBegin += () => Begin(GFreeRate); freeTrack.GestureEnd += () => End(GFreeRate);
            Learn(freeTrack, GFreeRate, "Free rate");
            var freeVal = Mono("", Txt); freeVal.Width = 40; freeVal.TextAlignment = TextAlignment.Right;
            ValueEntry.Attach(freeVal, () => (FreeMs(G(GFreeRate)) - FreeMsMin) / (FreeMsMax - FreeMsMin),
                n => S(GFreeRate, 1000.0 / (FreeMsMin + n * (FreeMsMax - FreeMsMin))), () => $"{FreeMs(G(GFreeRate)):0}\u2009ms",
                () => Begin(GFreeRate), () => End(GFreeRate), Refresh);
            var freeRow = new DockPanel { Children = { WithDock(freeVal, Dock.Right), freeTrack } };
            var rateHost = new Panel { Margin = new Thickness(4, 0, 0, 0), Children = { rateGrid, freeRow } };
            var rateStrip = new DockPanel { Height = 16, Margin = new Thickness(0, 4, 0, 0), Children = { WithDock(mode, Dock.Left), rateHost } };

            var winRow = new DockPanel { Children = { WithDock(laneGrid, Dock.Top), WithDock(rateStrip, Dock.Bottom), grid } };
            var centerIsland = IslandBox(tabHead, new Border { Padding = new Thickness(8, 5), Child = winRow }, double.NaN);

            void LayoutFor()
            {
                laneGrid.IsVisible = grid.Groove;
                for (int i = 0; i < 2; i++)
                {
                    bool on = (i == 1) == grid.Groove;
                    tabs[i].b.Background = on ? Chip : Brushes.Transparent;
                    tabs[i].b.BorderBrush = on ? Brass : Brushes.Transparent;
                    tabs[i].tb.Foreground = on ? BrassLit : Cap;
                    tabs[i].tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            layoutFor = LayoutFor;
            LayoutFor();
            readouts.Add(() =>
            {
                info.Text = InfoText(false);
                info.Foreground = grid.Groove ? BrassLit : Cap;
                for (int i = 0; i < Lanes.Length; i++)
                {
                    bool on = i == grid.LaneIndex;
                    laneChips[i].b.Background = on ? Brass : Chip;
                    laneChips[i].tb.Foreground = on ? Ink : Sub;
                    laneChips[i].tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
                bool sync = G(GSync) > 0.5f;
                rateGrid.IsVisible = sync; freeRow.IsVisible = !sync;
                int rate = GI(GRate);
                for (int i = 0; i < RateNames.Length; i++)
                {
                    bool on = i == rate;
                    rateChips[i].b.Background = on ? Brass : Chip;
                    rateChips[i].tb.Foreground = on ? Ink : Cap;
                }
                double ms = FreeMs(G(GFreeRate));
                if (!freeTrack.Dragging) freeTrack.Norm = (ms - FreeMsMin) / (FreeMsMax - FreeMsMin);
                freeVal.Text = $"{ms:0}\u2009ms";
            });

            // ---- PLAY ----
            var play = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto,*,Auto,*,Auto,*,Auto,*,Auto") };
            var playRows = new List<Control>
            {
                SliderLine("GATE", GGate, v => v / 2, n => n * 2, v => $"{v * 100:0}\u2009%"),
                SliderLine("SWING", GSwing, v => v, n => n, v => $"{v * 50:0}\u2009%"),
                SliderLine("VEL AMT", PVelAmt, v => v, n => n, v => $"{v * 100:0}\u2009%"),
                Ruled(Line("OCTAVES", Seg(GOctaves, new[] { "1", "2", "3", "4" }, off: 1))),
                Line("RETRIG", Seg(GRetrig, RetrigNames)),
                Stepper("TRANSPOSE", GTranspose, -24, 24, v => v == 0 ? "0\u2009st" : $"{(v > 0 ? "+" : "−")}{Math.Abs(v)}\u2009st"),
                Stepper("STEPS", GLoop, 1, 16, v => v.ToString()),
                Ruled(ButtonsRow()),
            };
            for (int i = 0; i < playRows.Count; i++) { Grid.SetRow(playRows[i], i * 2); play.Children.Add(playRows[i]); }
            var playHead = IslandHead(Caps("PLAY"), Mono("MIDI out · ch 1", Cap, 7));
            var playIsland = IslandBox(playHead, new Border { Padding = new Thickness(8, 5), Child = play }, 176);

            var islands = new DockPanel { Background = Ground, Children = { WithDock(orderIsland, Dock.Left), WithDock(playIsland, Dock.Right), centerIsland } };
            islands.Margin = new Thickness(0);
            orderIsland.Margin = new Thickness(0, 0, 5, 0); playIsland.Margin = new Thickness(5, 0, 0, 0);

            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                statusL.Text = $"{ChordText()} · {OrderName(GI(GOrder))} · {Math.Clamp(GI(GOctaves), 1, 8)} oct · {RateLabel(engine, track, mi)} · {grid.StepCount} steps · retrig {RetrigText()}{(G(GHold) > 0.5f ? " · hold" : "")}";
                statusR.Text = engine.MidiEffectBypassed(track, mi) ? "bypassed" : grid.Running ? "MIDI · latency 0 smp" : "MIDI · waiting for notes";
            });
            return Framed(islands, statusL, statusR);
        }

        // =======================================================================================
        Control BuildMini()
        {
            var tabs = DeviceCardKit.Segments(new[] { "Pattern", "Groove" }, () => grid.Groove ? 1 : 0,
                i => { ui.Groove = grid.Groove = i == 1; Refresh(); }, out var tabSync, padX: 6, fontSize: 8);
            readouts.Add(tabSync);
            var laneName = new TextBlock { FontSize = 8, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
            var laneBtn = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
                Children = { laneName, new Glyph { Kind = GlyphKind.ChevronDown, Width = 6, Height = 6, Foreground = Cap, VerticalAlignment = VerticalAlignment.Center } } };
            ToolTip.SetTip(laneBtn, "Next Groove lane");
            laneBtn.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(laneBtn).Properties.IsLeftButtonPressed) return;
                ui.Lane = grid.LaneIndex = (grid.LaneIndex + 1) % Lanes.Length; Refresh(); ev.Handled = true;
            };
            var info = Mono("", Cap, 7);
            var head = new DockPanel { Height = 14, Margin = new Thickness(0, 0, 0, 4),
                Children = { WithDock(tabs, Dock.Left), WithDock(info, Dock.Right), laneBtn } };
            laneBtn.Margin = new Thickness(6, 0, 0, 0);

            var knobs = new Grid { Height = 53, ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(0, 3, 0, 0) };
            var cells = new[]
            {
                KnobCell("RATE", GRate,
                    () => G(GSync) > 0.5f ? GI(GRate) / 7.0 : (FreeMs(G(GFreeRate)) - FreeMsMin) / (FreeMsMax - FreeMsMin),
                    n => { if (G(GSync) > 0.5f) S(GRate, Math.Round(n * 7)); else S(GFreeRate, 1000.0 / (FreeMsMin + n * (FreeMsMax - FreeMsMin))); },
                    () => RateLabel(engine, track, mi), () => G(GSync) > 0.5f ? GRate : GFreeRate),
                KnobCell("GATE", GGate, () => G(GGate) / 2, n => S(GGate, n * 2), () => $"{G(GGate) * 100:0}\u2009%"),
                KnobCell("OCT", GOctaves, () => (Math.Clamp(GI(GOctaves), 1, 4) - 1) / 3.0, n => S(GOctaves, Math.Round(n * 3) + 1), () => $"{GI(GOctaves)}"),
                KnobCell("SWING", GSwing, () => G(GSwing), n => S(GSwing, n), () => $"{G(GSwing) * 50:0}\u2009%"),
            };
            for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); knobs.Children.Add(cells[i]); }

            var inner = new DockPanel { Children = { WithDock(head, Dock.Top), WithDock(knobs, Dock.Bottom), grid } };
            var island = new Border { Background = Island, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
                Padding = new Thickness(6, 5, 6, 0), Child = inner };

            readouts.Add(() =>
            {
                laneBtn.IsVisible = grid.Groove;
                laneName.Text = Lanes[grid.LaneIndex].Label;
                info.Text = InfoText(true);
                info.Foreground = grid.Groove ? BrassLit : Cap;
            });

            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                statusL.Text = $"{ChordText()} · {Math.Clamp(GI(GOctaves), 1, 8)} oct";
                statusR.Text = engine.MidiEffectBypassed(track, mi) ? "bypassed" : RateLabel(engine, track, mi);
            });
            return Framed(new Border { Background = Ground, Child = island }, statusL, statusR);
        }

        // ---- shared pieces ---------------------------------------------------------------------
        Control Framed(Control main, TextBlock left, TextBlock right)
        {
            var status = new Border { Height = 18, Background = Well, BorderBrush = Bd, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0),
                Child = new DockPanel { Children = { WithDock(right, Dock.Right), left } } };
            right.Margin = new Thickness(8, 0, 0, 0);
            return new DockPanel { Background = Ground, Children = { WithDock(status, Dock.Bottom), new Border { Padding = new Thickness(5), Child = main } } };
        }

        Control IslandHead(Control left, Control? right)
        {
            var d = new DockPanel { Margin = new Thickness(8, 0) };
            if (right is not null) d.Children.Add(WithDock(right, Dock.Right));
            d.Children.Add(left);
            return new Border { Height = 20, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Child = d };
        }

        Border IslandBox(Control head, Control content, double width)
        {
            var b = new Border { Background = Island, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, ClipToBounds = true,
                Child = new DockPanel { Children = { WithDock(head, Dock.Top), content } } };
            if (!double.IsNaN(width)) b.Width = width;
            return b;
        }

        Control Line(string label, Control c)
        {
            var l = Caps(label); l.Width = LabelW;
            return new DockPanel { Children = { WithDock(l, Dock.Left), c } };
        }
        Control Ruled(Control c) => new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = c };

        // A PLAY slider: label brightens to brass once moved off its default.
        Control SliderLine(string label, int p, Func<double, double> toNorm, Func<double, double> fromNorm, Func<double, string> fmt)
        {
            var l = Caps(label); l.Width = LabelW;
            var val = Mono("", Txt); val.Width = 30; val.TextAlignment = TextAlignment.Right;
            var track = new SliderTrack { Height = 11, Reset = () => { S(p, Def(p)); Refresh(); } };
            track.Changed += n => { S(p, fromNorm(n)); Refresh(); };
            track.GestureBegin += () => Begin(p); track.GestureEnd += () => End(p);
            Learn(track, p, label);
            ValueEntry.Attach(val, () => toNorm(G(p)), n => S(p, fromNorm(n)), () => fmt(G(p)), () => Begin(p), () => End(p), Refresh);
            readouts.Add(() =>
            {
                double v = G(p);
                if (!track.Dragging) track.Norm = toNorm(v);
                bool mod = Math.Abs(v - Def(p)) > 0.005;
                l.Foreground = mod ? BrassLit : Cap;
                val.Foreground = mod ? BrassLit : Txt;
                val.Text = fmt(v);
            });
            track.Margin = new Thickness(0, 0, 4, 0);
            return new DockPanel { Children = { WithDock(l, Dock.Left), WithDock(val, Dock.Right), track } };
        }

        Control Stepper(string label, int p, int min, int max, Func<int, string> fmt)
        {
            var l = Caps(label); l.Width = LabelW;
            var val = Mono("", Txt); val.HorizontalAlignment = HorizontalAlignment.Center;
            Border Btn(string glyph, int d)
            {
                var tb = Mono(glyph, NotaPalette.TextMuted, 9); tb.HorizontalAlignment = HorizontalAlignment.Center;
                var b = new Border { Width = 18, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerEntered += (_, _) => tb.Foreground = Txt; b.PointerExited += (_, _) => tb.Foreground = NotaPalette.TextMuted;
                b.PointerPressed += (_, ev) =>
                {
                    if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                    S(p, Math.Clamp(GI(p) + d, min, max)); Refresh(); ev.Handled = true;
                };
                return b;
            }
            var box = new Border { Height = 14, Background = Well, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Child = new DockPanel { Children = { WithDock(Btn("−", -1), Dock.Left), WithDock(Btn("+", +1), Dock.Right), val } } };
            box.PointerPressed += (_, ev) => { if (ev.ClickCount == 2) { S(p, Def(p)); Refresh(); } };
            Learn(box, p, label);
            readouts.Add(() =>
            {
                int v = GI(p);
                bool mod = Math.Abs(v - Def(p)) > 0.5;
                l.Foreground = mod ? BrassLit : Cap;
                val.Foreground = mod ? BrassLit : Txt;
                val.Text = fmt(v);
            });
            return new DockPanel { Children = { WithDock(l, Dock.Left), box } };
        }

        Control ButtonsRow()
        {
            Border Btn(string text, Action click, out TextBlock tb)
            {
                var t = new TextBlock { Text = text, FontSize = 8, Foreground = Txt, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Height = 16, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Background = NotaPalette.TrackOff, BorderBrush = NotaPalette.TrackOff, Cursor = new Cursor(StandardCursorType.Hand), Child = t };
                b.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); ev.Handled = true; };
                tb = t; return b;
            }
            var hold = Btn("Hold", () => { S(GHold, G(GHold) > 0.5f ? 0 : 1); Refresh(); }, out var holdTb);
            ToolTip.SetTip(hold, "Hold: keep arpeggiating after the keys are released");
            Learn(hold, GHold, "Hold");
            readouts.Add(() =>
            {
                bool on = G(GHold) > 0.5f;
                hold.Background = on ? NotaPalette.AccentSubtle : NotaPalette.TrackOff;
                hold.BorderBrush = on ? NotaPalette.BorderBrass : NotaPalette.TrackOff;
                holdTb.Foreground = on ? NotaPalette.AccentHover : Txt;
                holdTb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            });
            var restart = Btn("Restart", () => engine.MidiEffectCommand(track, mi, 1), out var rTb);
            restart.PointerEntered += (_, _) => restart.Background = NotaPalette.SurfaceHover;
            restart.PointerExited += (_, _) => restart.Background = NotaPalette.TrackOff;
            ToolTip.SetTip(restart, "Restart the pattern from step 1");
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            Grid.SetColumn(restart, 1); g.Children.Add(hold); g.Children.Add(restart);
            return g;
        }

        // A mini-view knob: 34px gauge, caps label, mono value. `learnParam` names the param the
        // knob drives right now (Rate is the division when synced, the free rate otherwise).
        Control KnobCell(string label, int p, Func<double> norm, Action<double> set, Func<string> text, Func<int>? learnParam = null)
        {
            var knob = new Knob(norm(), 1.0) { Accent = true, Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center };
            int LP() => learnParam?.Invoke() ?? p;
            knob.ValueChanged += n => { set(n); Refresh(); };
            knob.GestureBegin += () => Begin(LP()); knob.GestureEnd += () => End(LP());
            Learn(knob, p, label);
            var val = Mono("", Sub, 7); val.HorizontalAlignment = HorizontalAlignment.Center;
            readouts.Add(() => { double n = norm(); if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n; val.Text = text(); });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { knob, new TextBlock { Text = label, FontSize = 7, FontWeight = FontWeight.Bold, LetterSpacing = 0.6, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 9 }, val } };
        }
    }
}
