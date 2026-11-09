// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Random editor (MIDI effect kind 5), almanac mockups 1a / 1b.
// Two sizes share every value; the S / L toggle in the shell header flips the card (the
// choice is the device's "View" param, so it persists with the project):
//   • L 700 × 260 — WHAT VARIES (Note · Velocity · Timing · Skip · Octave, amount sliders) |
//     OUTPUT (one bar of eight demo notes: the input dashed, the output filled — brightness =
//     velocity, a link when the pitch moved, × when skipped — under a playhead) | DICE (Chance,
//     the distribution Gauss / Even / Walk and its histogram, Per note / Per bar, seed with
//     Reroll and Lock, Stay in scale).
//   • S 260 × 260 — the distribution chip over the OUTPUT window and four knobs (Chance · Note
//     · Velocity · Timing). Seed, Lock, Skip, Octave, Rate and Stay in scale are L-only controls (presets still set them).
// The playhead follows the transport's bar while it runs and free-runs at the project tempo
// otherwise; every bar is a new roll until the seed is locked — Lock holds the bar showing
// (its "Lock Bar"), so what you see is what keeps playing. A bypassed device greys out.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.RandomModel;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class RandomMidiBody : IMidiDeviceBody
{
    internal static readonly string[] Dists = { "Gauss", "Even", "Walk" };
    internal static readonly string[] Rates = { "Per note", "Per bar" };

    private static readonly IBrush Ground = NotaPalette.SurfaceCard;   // the card surface: sections sit on it, divided by rules (spec 012)
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Muted = NotaPalette.TextMuted;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;
    private static readonly IBrush Faint = NotaPalette.TextDisabled;

    // The free-running preview clock (transport stopped) — shared, so every card agrees.
    private static readonly long ClockStart = Stopwatch.GetTimestamp();

    public double Width => 700;
    public bool FullBleed => true;

    private static bool HasLayout(IAudioEngine e, int track, int mi) => e.MidiEffectParamCount(track, mi) > PLockBar;
    private static bool IsMini(IAudioEngine e, int track, int mi) => HasLayout(e, track, mi) && e.MidiEffectGetParam(track, mi, PView) >= 0.5f;
    public double WidthFor(IAudioEngine engine, int trackId, int index) => IsMini(engine, trackId, index) ? 260 : 700;

    public Control? HeaderAccessory(DeviceCardContext ctx, int index)
    {
        var e = ctx.Engine; int t = ctx.TrackId;
        if (!HasLayout(e, t, index)) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (!IsMini(e, t, index))
        {
            var tag = Mono("", Cap, 8);
            var bpm = Mono("", Cap, 8);
            void Sync()
            {
                tag.Text = Dists[Math.Clamp((int)Math.Round(e.MidiEffectGetParam(t, index, PDist)), 0, 2)];
                bpm.Text = $"{e.Bpm:0.0} BPM";
            }
            Sync();
            ctx.AddDeviceRefresher(Sync);
            ToolTip.SetTip(tag, "Distribution");
            row.Children.Add(tag); row.Children.Add(MidiCardHeader.LearnButton(ctx)); row.Children.Add(bpm);
        }
        row.Children.Add(MidiCardHeader.SizeToggle(ctx, index, PView, () => IsMini(e, t, index)));
        return row;
    }

    // ---- formats ----------------------------------------------------------------------------------
    internal static string NoteText(double v) => $"±{Math.Round(v, MidpointRounding.AwayFromZero):0} st";
    internal static string VelText(double v) => $"±{Math.Round(v * 64, MidpointRounding.AwayFromZero):0}";
    internal static string TimeText(double v) => $"≤{Math.Round(v * MaxDelayMs, MidpointRounding.AwayFromZero):0} ms";
    internal static string SkipText(double v) => $"{Math.Round(v * 100, MidpointRounding.AwayFromZero):0} %";
    internal static string OctText(double v) => $"±{Math.Round(v * 2, MidpointRounding.AwayFromZero):0} oct";
    internal static string ChanceText(double v) => $"{Math.Round(v * 100, MidpointRounding.AwayFromZero):0} %";

    // ---- small builders -------------------------------------------------------------------------
    private static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
    { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? Cap, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Mono(string t, IBrush c, double fs = 8) => new()
    { Text = t, FontSize = fs, FontFamily = NotaFonts.UiFamily, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
    private static T WithDock<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }

    // ---- body -------------------------------------------------------------------------------------
    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, mi = index;
        if (!HasLayout(engine, track, mi)) return new TextBlock { Text = "Nota Random", Margin = new Thickness(8) };
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        float Def(int p) => engine.MidiEffectParamDefault(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void Set(int p, double v) { Begin(p); S(p, v); End(p); }
        void Learn(Control c, int p, string name) => MidiLearn.Bind(c, MidiTarget.MidiDeviceParam(track, mi, p), name);
        double Bpm() => engine.Bpm > 0 ? engine.Bpm : 120;
        bool Bypassed() => engine.MidiEffectBypassed(track, mi);

        bool mini = IsMini(engine, track, mi);
        var readouts = new List<Action>();
        void Refresh() { foreach (var r in readouts) r(); }

        // ---- the animated model: the playhead, and a new roll per bar ------------------------------
        double now = 0; int bar = 0;
        var notes = Compute(G, 0, Bpm());
        void Tick()
        {
            if (!Bypassed())
            {
                double pos = engine.IsPlaying
                    ? engine.PositionBeats
                    : Stopwatch.GetElapsedTime(ClockStart).TotalMinutes * Bpm();
                now = (pos % BarBeats + BarBeats) % BarBeats;
                bar = Math.Max(0, (int)Math.Floor(pos / BarBeats));
            }
            notes = Compute(G, bar, Bpm());
        }
        int ShownBar() => Locked(G) ? (int)Math.Round(Math.Max(0, G(PLockBar))) : bar;
        int Changed() => notes.Count(n => n.Changed);
        int Skipped() => notes.Count(n => n.Skip);

        // Lock holds the roll showing now; Reroll moves to the next seed (1..999).
        void ToggleLock()
        {
            if (!Locked(G)) Set(PLockBar, bar);
            Set(PLocked, Locked(G) ? 0 : 1);
            Refresh();
        }
        void Reroll() { Set(PSeed, Seed(G) % 999 + 1); Refresh(); }

        Tick();
        Control rootView = mini ? BuildMini() : BuildLarge();
        ctx.AddDeviceRefresher(() => { Tick(); Refresh(); });
        Refresh();
        return rootView;

        // =========================================================================================
        Control BuildLarge()
        {
            // ---- WHAT VARIES ----
            var rows = new Grid { Margin = new Thickness(8, 4, 8, 5), RowDefinitions = new RowDefinitions("*,*,*,*,*") };
            var specs = new (string Label, int P, double Max, Func<double, string> Fmt, string Tip)[]
            {
                ("Note", PNoteRange, 12, NoteText, Loc.T("device.random.transpose_up_or_down_by")),
                ("Velocity", PVelAmt, 1, VelText, Loc.T("device.random.push_the_velocity_up_or")),
                ("Timing", PTimeAmt, 1, TimeText, Loc.T("device.random.play_late_by_up_to")),
                ("Skip", PSkip, 1, SkipText, Loc.T("device.random.chance_that_a_varied_note")),
                ("Octave", POctAmt, 1, OctText, Loc.T("device.random.jump_up_or_down_by")),
            };
            for (int i = 0; i < specs.Length; i++)
            {
                var sp = specs[i];
                var row = VaryRow(sp.Label, sp.P, sp.Max, sp.Fmt, sp.Tip);
                Grid.SetRow(row, i); rows.Children.Add(row);
            }
            var varyIsland = IslandBox(IslandHead(Caps("WHAT VARIES"), Mono("amount · range", Cap, 7)), rows, 170);

            // ---- OUTPUT ----
            var viz = new RandomOutputViz { Full = true };
            ToolTip.SetTip(viz, Loc.T("device.random.one_bar_of_eight_notes"));
            var outInfo = Mono("", Cap, 7);
            readouts.Add(() =>
            {
                outInfo.Text = Loc.F("device.random.bar_changed_skipped", ShownBar() + 1, Changed(), Skipped()) + (Locked(G) ? " · held" : "");
                viz.Set(notes, now, Bypassed());
            });
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Height = 10, Margin = new Thickness(22, 3, 0, 0), Children =
            {
                LegendItem(new Rectangle { Width = 9, Height = 5, Stroke = Muted, StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2, 1 }, RadiusX = 2, RadiusY = 2 }, "input"),
                LegendItem(new Border { Width = 9, Height = 5, Background = NotaPalette.VizInk, CornerRadius = NotaRadius.Fine }, Loc.T("device.random.output_brightness_velocity")),
                LegendItem(Mono("×", Muted, 8), "skip"),
            } };
            var outBody = new DockPanel { Margin = new Thickness(8, 5, 8, 4), Children = { WithDock(legend, Dock.Bottom), viz } };
            var outIsland = IslandBox(IslandHead(Caps("OUTPUT"), outInfo), outBody, double.NaN);

            // ---- DICE ----
            var chanceTrk = Slider(PChance, 1);
            var chanceLbl = Caps("CHANCE"); chanceLbl.Width = 38;
            var chanceVal = Mono("", Txt); chanceVal.Width = 26; chanceVal.TextAlignment = TextAlignment.Right;
            readouts.Add(() =>
            {
                bool mod = Math.Abs(G(PChance) - Def(PChance)) > 0.004;
                chanceLbl.Foreground = mod ? Txt : Cap;
                chanceVal.Foreground = Txt;
                chanceVal.Text = ChanceText(G(PChance));
            });
            ToolTip.SetTip(chanceTrk, Loc.T("device.random.chance_how_many_notes_get"));
            var chanceRow = new DockPanel { Children = { WithDock(chanceLbl, Dock.Left), WithDock(chanceVal, Dock.Right), chanceTrk } };

            var distSeg = Seg(PDist, Dists, Loc.T("device.random.distribution_gauss_mostly_small_moves"));
            var hist = new RandomHistViz();
            ToolTip.SetTip(hist, Loc.T("device.random.the_shape_of_the_roll"));
            readouts.Add(() => hist.Set(Dist(G), Seed(G)));
            var axis = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var ac = Mono("centre", Faint, 6); ac.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(ac, 1);
            var ar = Mono("+max", Faint, 6); Grid.SetColumn(ar, 2);
            axis.Children.Add(Mono("−max", Faint, 6)); axis.Children.Add(ac); axis.Children.Add(ar);
            var histBox = new DockPanel { Children = { WithDock(axis, Dock.Bottom), hist } };
            axis.Margin = new Thickness(0, 2, 0, 0);

            var rateSeg = Seg(PRate, Rates, Loc.T("device.random.per_note_every_note_rolls"));

            var seedTb = Mono("", Txt);
            readouts.Add(() => seedTb.Text = $"seed {Seed(G)}");
            Learn(seedTb, PSeed, "Seed");
            var rerollBtn = Button("Reroll", Loc.T("device.random.next_seed_a_new_roll"), Reroll, out _);
            var lockBtn = Button("Lock", Loc.T("device.random.hold_the_roll_showing_now"), ToggleLock, out var lockSync);
            Learn(lockBtn, PLocked, "Locked");
            readouts.Add(() => lockSync(Locked(G)));
            var seedRow = new DockPanel { Children =
            {
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { rerollBtn, lockBtn } }, Dock.Right),
                seedTb,
            } };

            var scaleRow = SwitchRow(PStayInScale, Loc.T("device.random.stay_in_scale_c_maj"), Loc.T("device.random.snap_every_varied_note_into"));

            var dice = new DockPanel { Margin = new Thickness(8, 6), Children =
            {
                WithDock(new StackPanel { Spacing = 5, Children = { chanceRow, distSeg } }, Dock.Top),
                WithDock(new StackPanel { Spacing = 5, Margin = new Thickness(0, 5, 0, 0), Children = { rateSeg, seedRow, scaleRow } }, Dock.Bottom),
                new Border { Margin = new Thickness(0, 5, 0, 0), Child = histBox },
            } };
            var diceIsland = IslandBox(IslandHead(Caps("DICE"), null), dice, 160);

            varyIsland.Margin = new Thickness(0, 0, 5, 0); diceIsland.Margin = new Thickness(5, 0, 0, 0);
            var islands = new DockPanel { Children = { WithDock(varyIsland, Dock.Left), WithDock(diceIsland, Dock.Right), outIsland } };

            return Framed(islands);
        }

        // =========================================================================================
        Control BuildMini()
        {
            // Distribution chip: click for the next, Shift-click for the previous.
            var distTb = new TextBlock { FontSize = 9, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Foreground = Cap, VerticalAlignment = VerticalAlignment.Center };
            var distChip = new Border { Height = 14, Width = 62, CornerRadius = NotaRadius.Cell, Background = NotaPalette.GridBeat, BorderBrush = Bd, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new DockPanel { Children = { WithDock(chev, Dock.Right), distTb } } };
            distChip.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(distChip).Properties.IsLeftButtonPressed) return;
                int d = ev.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2 : 1;
                Set(PDist, (Dist(G) + d) % 3); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(distChip, Loc.T("device.random.distribution_gauss_even_walk_click"));
            Learn(distChip, PDist, "Dist");
            var barTb = Mono("", Cap, 7);
            var head = new DockPanel { Height = 14, Children = { WithDock(distChip, Dock.Left), WithDock(barTb, Dock.Right) } };
            barTb.HorizontalAlignment = HorizontalAlignment.Right;

            var viz = new RandomOutputViz { Full = false };
            ToolTip.SetTip(viz, Loc.T("device.random.dashed_in_filled_out_brightness"));

            var knobs = new Grid { Height = 53, ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            var cells = new[]
            {
                KnobCell("CHANCE", PChance, 1, ChanceText),
                KnobCell("NOTE", PNoteRange, 12, NoteText),
                KnobCell("VEL", PVelAmt, 1, VelText),
                KnobCell("TIME", PTimeAmt, 1, TimeText),
            };
            for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); knobs.Children.Add(cells[i]); }

            var inner = new DockPanel { Children =
            {
                WithDock(head, Dock.Top), WithDock(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 4), Child = viz },
            } };
            var island = new RuleSection { Padding = new Thickness(6, 5, 6, 0), Child = inner };
            readouts.Add(() =>
            {
                distTb.Text = Dists[Dist(G)];
                barTb.Text = $"bar {ShownBar() + 1}" + (Locked(G) ? " · held" : "");
                viz.Set(notes, now, Bypassed());
            });

            return Framed(island);
        }

        // ---- shared pieces -----------------------------------------------------------------------
        Control Framed(Control main) => new Border { Background = Ground, Padding = new Thickness(5), Child = main };

        Control IslandHead(Control left, Control? right)
        {
            var d = new DockPanel { Margin = new Thickness(8, 0) };
            if (right is not null) { right.HorizontalAlignment = HorizontalAlignment.Right; d.Children.Add(WithDock(right, Dock.Right)); }
            d.Children.Add(left);
            return new Border { Height = 20, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Child = d };
        }

        Decorator IslandBox(Control head, Control content, double width)
        {
            var b = DeviceCardKit.Section(new DockPanel { Children = { WithDock(head, Dock.Top), content } });
            if (!double.IsNaN(width)) b.Width = width;
            return b;
        }

        Control LegendItem(Control swatch, string text)
        {
            swatch.VerticalAlignment = VerticalAlignment.Center;
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children =
                { swatch, new TextBlock { Text = text, FontSize = 7, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center } } };
        }

        // A plain amount slider (0..max), automation-bracketed; double-click resets.
        SliderTrack Slider(int p, double max)
        {
            var trk = new SliderTrack { Height = 11, Reset = () => { Set(p, Def(p)); Refresh(); } };
            bool stepped = max > 1;
            trk.Changed += n => { S(p, stepped ? Math.Round(n * max) : Math.Round(n * max, 3)); Refresh(); };
            trk.GestureBegin += () => Begin(p);
            trk.GestureEnd += () => End(p);
            Learn(trk, p, engine.MidiEffectParamName(track, mi, p));
            readouts.Add(() => { if (!trk.Dragging) trk.Norm = G(p) / max; });
            return trk;
        }

        // One WHAT VARIES row: the label (lit once it does something), the value (primary text once moved
        // off the default) and the amount slider under them.
        Control VaryRow(string label, int p, double max, Func<double, string> fmt, string tip)
        {
            var l = new TextBlock { Text = label, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            var v = Mono("", Sub); v.HorizontalAlignment = HorizontalAlignment.Right;
            var trk = Slider(p, max);
            ToolTip.SetTip(trk, Loc.F("device.random.drag_up_down_double_click", tip));
            ValueEntry.Attach(v, () => G(p) / max, n => S(p, max > 1 ? Math.Round(n * max) : Math.Round(n * max, 3)), () => fmt(G(p)),
                () => Begin(p), () => End(p), Refresh, control: trk);
            readouts.Add(() =>
            {
                double x = G(p);
                l.Foreground = x > 0.0005 ? Txt : Cap;
                v.Foreground = Math.Abs(x - Def(p)) > 0.0005 ? Txt : Sub;
                v.Text = fmt(x);
            });
            return new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children =
            {
                new DockPanel { Children = { WithDock(v, Dock.Right), l } }, trk,
            } };
        }

        Control Seg(int p, string[] names, string tip)
        {
            var seg = DeviceCardKit.Segments(names, () => Math.Clamp((int)Math.Round(G(p)), 0, names.Length - 1),
                i => { Set(p, i); Refresh(); }, out var sync, fill: true, padX: 2, fontSize: 8);
            readouts.Add(sync);
            Learn(seg, p, engine.MidiEffectParamName(track, mi, p));
            ToolTip.SetTip(seg, tip);
            return seg;
        }

        // A small action button (Reroll / Lock); the sync paints the engaged (accent-wash) state.
        Border Button(string text, string tip, Action click, out Action<bool> sync)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Height = 16, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Padding = new Thickness(6, 0),
                Background = NotaPalette.TrackOff, BorderBrush = NotaPalette.TrackOff, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            bool engaged = false;
            b.PointerEntered += (_, _) => { if (!engaged) b.Background = NotaPalette.SurfaceHover; };
            b.PointerExited += (_, _) => { if (!engaged) b.Background = NotaPalette.TrackOff; };
            b.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                click(); ev.Handled = true;
            };
            ToolTip.SetTip(b, tip);
            sync = on =>
            {
                engaged = on;
                b.Background = on ? NotaPalette.AccentSubtle : b.IsPointerOver ? NotaPalette.SurfaceHover : NotaPalette.TrackOff;
                b.BorderBrush = on ? NotaPalette.BorderBrass : NotaPalette.TrackOff;
                tb.Foreground = on ? NotaPalette.AccentHover : Txt;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            };
            return b;
        }

        // A switch + sentence-case label (Stay in scale).
        Control SwitchRow(int p, string label, string tip)
        {
            var sw = new SwitchTrack { VerticalAlignment = VerticalAlignment.Center };
            var tb = new TextBlock { Text = label, FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
            var row = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { sw, tb } } };
            row.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                Set(p, G(p) >= 0.5f ? 0 : 1); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(row, tip);
            Learn(row, p, label);
            readouts.Add(() => { bool on = G(p) >= 0.5f; sw.IsOn = on; tb.Foreground = on ? Txt : Muted; });
            return row;
        }

        // A mini-view knob: 34px gauge, caps label, mono value.
        Control KnobCell(string label, int p, double max, Func<double, string> text)
        {
            var knob = new Knob(G(p) / max, 1.0) { Accent = true, Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center, Default = Def(p) / max };
            knob.ValueChanged += n => { S(p, max > 1 ? Math.Round(n * max) : Math.Round(n * max, 3)); Refresh(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p, label);
            var v = Mono("", Sub, 7); v.HorizontalAlignment = HorizontalAlignment.Center;
            knob.Format = n => text(max > 1 ? Math.Round(n * max) : Math.Round(n * max, 3));
            ValueEntry.Attach(knob, label, v);
            readouts.Add(() => { double n = G(p) / max; if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n; v.Text = text(G(p)); });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { knob, new TextBlock { Text = NotaText.Label(label), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 9 }, v } };
        }
    }
}
