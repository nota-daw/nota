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
//     Reroll and Lock, Stay in scale), in pressed-in sections (style guide).
//   • S 260 × 260 — the distribution chip over the OUTPUT window and four knobs (Chance · Note
//     · Velocity · Timing); the head row's seed rerolls and its lock holds the roll. Skip, Octave,
//     Rate and Stay in scale are L-only controls (presets still set them).
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
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class RandomMidiBody : IMidiDeviceBody
{
    internal static readonly string[] Dists = { "Gauss", "Even", "Walk" };
    internal static readonly string[] Rates = { "Per note", "Per bar" };

    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;

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
            var tag = new TextBlock { FontSize = NotaType.Caption, Foreground = Cap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-4, 0, 0, 0) };
            void Sync() => tag.Text = $"· {Dists[Math.Clamp((int)Math.Round(e.MidiEffectGetParam(t, index, PDist)), 0, 2)]} · {e.Bpm:0.0} BPM";
            Sync();
            ctx.AddDeviceRefresher(Sync);
            ToolTip.SetTip(tag, "Distribution · tempo");
            row.Children.Add(tag); row.Children.Add(MidiCardHeader.LearnButton(ctx));
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
    private static TextBlock Caps(string t, IBrush? c = null, double fs = 0) => DeviceBodyKit.Label(t, c);
    // The old small sizes land on the device scale: 6–7 → 10.5 (a label / axis), 8–9 → 11 (a value).
    private static double Fs(double fs) => fs < 8 ? NotaType.RowLabel : fs < 10 ? NotaType.KnobValue : fs;
    private static TextBlock Mono(string t, IBrush c, double fs = NotaType.KnobValue) => new()
    { Text = t, FontSize = Fs(fs), FontFamily = NotaFonts.UiFamily, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
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
            var rows = new Grid { RowDefinitions = new RowDefinitions("*,*,*,*,*") };
            var specs = new (string Label, int P, double Max, Func<double, string> Fmt, string Tip)[]
            {
                ("Note", PNoteRange, 12, NoteText, "Transpose up or down by up to this many semitones"),
                ("Velocity", PVelAmt, 1, VelText, "Push the velocity up or down by up to this much"),
                ("Timing", PTimeAmt, 1, TimeText, "Play late by up to this long (a live effect can't play early)"),
                ("Skip", PSkip, 1, SkipText, "Chance that a varied note is dropped"),
                ("Octave", POctAmt, 1, OctText, "Jump up or down by up to two octaves"),
            };
            for (int i = 0; i < specs.Length; i++)
            {
                var sp = specs[i];
                var row = VaryRow(sp.Label, sp.P, sp.Max, sp.Fmt, sp.Tip);
                Grid.SetRow(row, i); rows.Children.Add(row);
            }
            var varyIsland = IslandBox(IslandHead(Caps("What varies"), Mono("amount · range", Cap, 7)), rows, 168);

            // ---- OUTPUT ----
            var viz = new RandomOutputViz { Full = true };
            ToolTip.SetTip(viz, "One bar of eight notes: dashed — the note in, filled — the note out (brightness = velocity, shifted right when delayed), × — skipped");
            var outInfo = Mono("", Cap, 7);
            readouts.Add(() =>
            {
                outInfo.Text = $"bar {ShownBar() + 1} · {Changed()} changed · {Skipped()} skipped" + (Locked(G) ? " · held" : "");
                viz.Set(notes, now, Bypassed());
            });
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Children =
            {
                LegendItem(new Rectangle { Width = 10, Height = 6, Stroke = Sub, StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2, 1 }, RadiusX = 2, RadiusY = 2 }, "input"),
                LegendItem(new Border { Width = 10, Height = 6, Background = NotaPalette.VizInk, CornerRadius = NotaRadius.Fine }, "output · brightness = velocity"),
                LegendItem(Mono("×", Sub, NotaType.RowLabel), "skip"),
            } };
            var outBody = new DockPanel { Children = { WithDock(legend, Dock.Bottom), viz } };
            var outIsland = IslandBox(IslandHead(Caps("Output"), outInfo), outBody, double.NaN);

            // ---- DICE ----
            var chanceTrk = Slider(PChance, 1);
            var chanceLbl = Caps("Chance"); chanceLbl.Width = 48;
            var chanceVal = Mono("", Txt); chanceVal.Width = 44; chanceVal.TextAlignment = TextAlignment.Right;
            readouts.Add(() =>
            {
                bool mod = Math.Abs(G(PChance) - Def(PChance)) > 0.004;
                chanceLbl.Foreground = mod ? Txt : Cap;
                chanceVal.Foreground = Txt;
                chanceVal.Text = ChanceText(G(PChance));
            });
            ToolTip.SetTip(chanceTrk, "Chance — how many notes get varied at all");
            var chanceRow = new DockPanel { Children = { WithDock(chanceLbl, Dock.Left), WithDock(chanceVal, Dock.Right), chanceTrk } };

            var distSeg = Seg(PDist, Dists, "Distribution: Gauss — mostly small moves · Even — any amount equally · Walk — drifts from note to note");
            var hist = new RandomHistViz();
            ToolTip.SetTip(hist, "The shape of the roll — 1500 draws from the same generator");
            readouts.Add(() => hist.Set(Dist(G), Seed(G)));
            var axis = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var ac = Mono("centre", Cap, 6); ac.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(ac, 1);
            var ar = Mono("+max", Cap, 6); Grid.SetColumn(ar, 2);
            axis.Children.Add(Mono("−max", Cap, 6)); axis.Children.Add(ac); axis.Children.Add(ar);
            var histBox = new DockPanel { Children = { WithDock(axis, Dock.Bottom), hist } };
            axis.Margin = new Thickness(0, 2, 0, 0);

            var rateSeg = Seg(PRate, Rates, "Per note — every note rolls its own values · Per bar — one set of values for the whole bar");

            var seedTb = Mono("", Txt);
            readouts.Add(() => seedTb.Text = $"seed {Seed(G)}");
            Learn(seedTb, PSeed, "Seed");
            var rerollBtn = Button("Reroll", "Next seed — a new roll", Reroll, out _);
            var lockBtn = Button("Lock", "Hold the roll showing now — it repeats every bar", ToggleLock, out var lockSync);
            Learn(lockBtn, PLocked, "Locked");
            readouts.Add(() => lockSync(Locked(G)));
            var seedRow = new DockPanel { Children =
            {
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { rerollBtn, lockBtn } }, Dock.Right),
                seedTb,
            } };

            var scaleRow = SwitchRow(PStayInScale, "Stay in scale · C maj", "Snap every varied note into C major");

            var dice = new DockPanel { Children =
            {
                WithDock(new StackPanel { Spacing = 6, Children = { chanceRow, distSeg } }, Dock.Top),
                WithDock(new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Children = { rateSeg, seedRow, scaleRow } }, Dock.Bottom),
                new Border { Margin = new Thickness(0, 6, 0, 0), Child = histBox },
            } };
            var diceIsland = IslandBox(IslandHead(Caps("Dice"), null), dice, 180);

            varyIsland.Margin = new Thickness(0, 0, DeviceCardKit.SectionGap, 0); diceIsland.Margin = new Thickness(DeviceCardKit.SectionGap, 0, 0, 0);
            var islands = new DockPanel { Children = { WithDock(varyIsland, Dock.Left), WithDock(diceIsland, Dock.Right), outIsland } };

            return DeviceBodyKit.Frame(islands);
        }

        // =========================================================================================
        Control BuildMini()
        {
            // Distribution chip: click for the next, Shift-click for the previous.
            var distTb = new TextBlock { FontSize = NotaType.KnobValue, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
            var chev = new Glyph(GlyphKind.ChevronDown, 8) { Foreground = Cap, VerticalAlignment = VerticalAlignment.Center };
            var distChip = new Border { Height = 22, Width = 84, CornerRadius = NotaRadius.Field, Background = NotaPalette.SurfaceHover,
                Padding = new Thickness(8, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new DockPanel { Children = { WithDock(chev, Dock.Right), distTb } } };
            distChip.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(distChip).Properties.IsLeftButtonPressed) return;
                int d = ev.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2 : 1;
                Set(PDist, (Dist(G) + d) % 3); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(distChip, "Distribution: Gauss / Even / Walk — click for the next, Shift-click for the previous");
            Learn(distChip, PDist, "Dist");
            var barTb = Mono("", Cap, 7);
            var head = new DockPanel { Height = 22, Children = { WithDock(distChip, Dock.Left), WithDock(barTb, Dock.Right) } };
            barTb.HorizontalAlignment = HorizontalAlignment.Right;

            var viz = new RandomOutputViz { Full = false };
            ToolTip.SetTip(viz, "Dashed — in · filled — out (brightness = velocity) · × — skipped");

            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            var cells = new[]
            {
                KnobCell("Chance", PChance, 1, ChanceText),
                KnobCell("Note", PNoteRange, 12, NoteText),
                KnobCell("Vel", PVelAmt, 1, VelText),
                KnobCell("Time", PTimeAmt, 1, TimeText),
            };
            for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); knobs.Children.Add(cells[i]); }

            var inner = new DockPanel { Children =
            {
                WithDock(head, Dock.Top), WithDock(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 8), Child = viz },
            } };
            var island = DeviceBodyKit.Sec(inner, new Thickness(10, 10, 10, 8));
            readouts.Add(() =>
            {
                distTb.Text = Dists[Dist(G)];
                viz.Set(notes, now, Bypassed());
            });

            // "seed N" rerolls, "lock" holds the roll (on the head row, beside the chip).
            var seedTb = FooterLink("", "Reroll — the next seed", Reroll);
            Learn(seedTb, PSeed, "Seed");
            var lockTb = FooterLink("", "Lock — hold the roll showing now", ToggleLock, mono: false);
            Learn(lockTb, PLocked, "Locked");
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { seedTb, lockTb } };
            head.Children.Insert(1, WithDock(left, Dock.Left));
            readouts.Add(() =>
            {
                seedTb.Text = $"seed {Seed(G)}";
                bool on = Locked(G);
                lockTb.Text = on ? "locked" : "lock";
                lockTb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                lockTb.Tag = on ? NotaPalette.AccentHover : Sub;
                if (!lockTb.IsPointerOver) lockTb.Foreground = (IBrush)lockTb.Tag;
                barTb.Text = Bypassed() ? "bypassed" : $"bar {ShownBar() + 1}" + (Locked(G) ? " · held" : "");
            });
            return DeviceBodyKit.Frame(island);
        }

        // ---- shared pieces -----------------------------------------------------------------------

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

        Control LegendItem(Control swatch, string text)
        {
            swatch.VerticalAlignment = VerticalAlignment.Center;
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children =
                { swatch, new TextBlock { Text = text, FontSize = NotaType.RowLabel, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center } } };
        }

        // A plain amount slider (0..max), automation-bracketed; double-click resets.
        SliderTrack Slider(int p, double max)
        {
            var trk = new SliderTrack { Reset = () => { Set(p, Def(p)); Refresh(); } };
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
            var l = new TextBlock { Text = label, FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center };
            var v = Mono("", Txt); v.HorizontalAlignment = HorizontalAlignment.Right;
            var trk = Slider(p, max);
            ToolTip.SetTip(trk, $"{tip} — drag up / down, double-click resets");
            ValueEntry.Attach(v, () => G(p) / max, n => S(p, max > 1 ? Math.Round(n * max) : Math.Round(n * max, 3)), () => fmt(G(p)),
                () => Begin(p), () => End(p), Refresh, control: trk);
            readouts.Add(() =>
            {
                double x = G(p);
                l.Foreground = x > 0.0005 ? Txt : Cap;
                v.Text = fmt(x);
            });
            return new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children =
            {
                new DockPanel { Children = { WithDock(v, Dock.Right), l } }, trk,
            } };
        }

        Control Seg(int p, string[] names, string tip)
        {
            var seg = DeviceCardKit.Segments(names, () => Math.Clamp((int)Math.Round(G(p)), 0, names.Length - 1),
                i => { Set(p, i); Refresh(); }, out var sync, fill: true, padX: 2);
            readouts.Add(sync);
            Learn(seg, p, engine.MidiEffectParamName(track, mi, p));
            ToolTip.SetTip(seg, tip);
            return seg;
        }

        // A raised action button (Reroll / Lock); the sync paints the engaged (soft orange) state.
        Border Button(string text, string tip, Action click, out Action<bool> sync)
        {
            var b = TextButton(text);
            var tb = (TextBlock)b.Child!;
            b.VerticalAlignment = VerticalAlignment.Center;
            b.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                click(); ev.Handled = true;
            };
            ToolTip.SetTip(b, tip);
            sync = on =>
            {
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? NotaPalette.AccentBright : Txt;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            };
            return b;
        }

        // A switch + sentence-case label (Stay in scale).
        Control SwitchRow(int p, string label, string tip)
        {
            var sw = new SwitchTrack { VerticalAlignment = VerticalAlignment.Center };
            var tb = new TextBlock { Text = label, FontSize = NotaType.Caption, VerticalAlignment = VerticalAlignment.Center };
            var row = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sw, tb } } };
            row.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                Set(p, G(p) >= 0.5f ? 0 : 1); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(row, tip);
            Learn(row, p, label);
            readouts.Add(() => { bool on = G(p) >= 0.5f; sw.IsOn = on; tb.Foreground = on ? Txt : Sub; });
            return row;
        }

        // A clickable word (mini head row): ink 2, ink 1 on hover.
        TextBlock FooterLink(string text, string tip, Action click, bool mono = true)
        {
            var tb = mono ? Mono(text, Sub, NotaType.RowLabel) : new TextBlock { Text = text, FontSize = NotaType.RowLabel, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center };
            tb.Tag = Sub;
            tb.Cursor = new Cursor(StandardCursorType.Hand); tb.Background = Brushes.Transparent;
            tb.PointerEntered += (_, _) => tb.Foreground = Txt;
            tb.PointerExited += (_, _) => tb.Foreground = (IBrush)tb.Tag!;
            tb.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(tb).Properties.IsLeftButtonPressed) return;
                click(); ev.Handled = true;
            };
            ToolTip.SetTip(tb, tip);
            return tb;
        }

        // A mini-view knob: the device knob, value (11, ink 1) over label (10.5, ink 3).
        Control KnobCell(string label, int p, double max, Func<double, string> text)
        {
            var knob = new Knob(G(p) / max, 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center, Default = Def(p) / max };
            knob.ValueChanged += n => { S(p, max > 1 ? Math.Round(n * max) : Math.Round(n * max, 3)); Refresh(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p, label);
            var v = Mono("", Txt); v.HorizontalAlignment = HorizontalAlignment.Center; v.Margin = new Thickness(0, 4, 0, 0);
            knob.Format = n => text(max > 1 ? Math.Round(n * max) : Math.Round(n * max, 3));
            ValueEntry.Attach(knob, label, v);
            readouts.Add(() => { double n = G(p) / max; if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n; v.Text = text(G(p)); });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { knob, v, new TextBlock { Text = NotaText.Label(label), FontSize = NotaType.KnobLabel, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 13 } } };
        }
    }
}
