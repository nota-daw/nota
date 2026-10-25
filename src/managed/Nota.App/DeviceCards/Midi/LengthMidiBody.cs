// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Length editor (MIDI effect kind 3), almanac mockups 1a / 1b.
// Two sizes share every value; the S / L toggle in the shell header flips the card (the
// choice is the device's "View" param, so it persists with the project):
//   • L 700 × 260 — LENGTH (mode Sync / ms / Gate %, the big value, its slider, the eight
//     sync divisions, Start from note-on / note-off) | GATE (one bar of five demo notes: the
//     input outlined, the output filled, Random's range as a band, a playhead) | MODIFIERS
//     (bipolar Vel → Len and Key → Len, Random, Legato, Clip length limit), in pressed-in sections.
//   • S 260 × 260 — the mode chip and value over the GATE window and four knobs (Length ·
//     Vel · Key · Random); the head row's "from note-on" flips the start point. Legato and
//     Clip are L-only controls (presets still set them).
// The playhead follows the transport's bar while it runs, and free-runs at the project tempo
// otherwise; Random re-rolls each pass. A bypassed device greys the output and stops.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class LengthMidiBody : IMidiDeviceBody
{
    // Param indices — mirror MidiNoteLength.h (Rate 0 is the pre-Division sync rate).
    internal const int PGate = 1, PMode = 2, PMs = 3, PPercent = 4, PTrigger = 5, PVelToLen = 6, PKeyToLen = 7,
                       PRandom = 8, PLegato = 9, PClipLimit = 10, PDivision = 11, PView = 12;
    internal static readonly string[] Modes = { "Sync", "ms", "Gate %" };
    internal static readonly string[] Divs = { "1/32", "1/16", "1/8", "1/4", "1/2", "1/1", "1/8.", "1/4T" };

    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;

    // The free-running preview clock (transport stopped) — shared, so every card agrees.
    private static readonly long ClockStart = Stopwatch.GetTimestamp();

    public double Width => 700;
    public bool FullBleed => true;

    private static bool HasLayout(IAudioEngine e, int track, int mi) => e.MidiEffectParamCount(track, mi) > PView;
    private static bool IsMini(IAudioEngine e, int track, int mi) => HasLayout(e, track, mi) && e.MidiEffectGetParam(track, mi, PView) >= 0.5f;
    public double WidthFor(IAudioEngine engine, int trackId, int index) => IsMini(engine, trackId, index) ? 260 : 700;

    public Control? HeaderAccessory(DeviceCardContext ctx, int index)
    {
        var e = ctx.Engine; int t = ctx.TrackId;
        if (!HasLayout(e, t, index)) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (!IsMini(e, t, index))
        {
            var tag = new TextBlock { FontSize = NotaType.DeviceCaption, Foreground = Cap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-4, 0, 0, 0) };
            void Sync() => tag.Text = $"· {Modes[Math.Clamp((int)Math.Round(e.MidiEffectGetParam(t, index, PMode)), 0, 2)]} · {e.Bpm:0.0} BPM";
            Sync();
            ctx.AddDeviceRefresher(Sync);
            ToolTip.SetTip(tag, "Length mode · tempo");
            row.Children.Add(tag); row.Children.Add(MidiCardHeader.LearnButton(ctx));
        }
        row.Children.Add(MidiCardHeader.SizeToggle(ctx, index, PView, () => IsMini(e, t, index)));
        return row;
    }

    // ---- value mapping ----------------------------------------------------------------------------
    /// <summary>The LENGTH control's 0..1 position for the current mode.</summary>
    internal static double LenNorm(Func<int, float> g) => Mode(g) switch
    {
        1 => Math.Clamp(Math.Log(Math.Max(10, g(PMs)) / 10.0) / Math.Log(200), 0, 1),
        2 => Math.Clamp((g(PPercent) - 10) / 190.0, 0, 1),
        _ => Math.Clamp(Math.Round(g(PDivision)), 0, 7) / 7.0,
    };
    internal static int LenParam(Func<int, float> g) => Mode(g) switch { 1 => PMs, 2 => PPercent, _ => PDivision };
    internal static double LenFromNorm(Func<int, float> g, double n) => Mode(g) switch
    {
        1 => Math.Round(10 * Math.Pow(200, Math.Clamp(n, 0, 1))),
        2 => Math.Round(10 + Math.Clamp(n, 0, 1) * 190),
        _ => Math.Round(Math.Clamp(n, 0, 1) * 7),
    };
    internal static int Mode(Func<int, float> g) => Math.Clamp((int)Math.Round(g(PMode)), 0, 2);
    internal static int Div(Func<int, float> g) => Math.Clamp((int)Math.Round(g(PDivision)), 0, Divs.Length - 1);

    internal static string LenValue(Func<int, float> g) => Mode(g) switch
    {
        1 => LengthGateViz.Ms(g(PMs)),
        2 => $"{Math.Round(g(PPercent)):0}\u2009%",
        _ => Divs[Div(g)],
    };
    internal static string LenSub(Func<int, float> g, double bpm) => Mode(g) switch
    {
        1 => "fixed",
        2 => "of input",
        _ => LengthGateViz.Ms(LengthGateViz.DivBeats[Div(g)] * Math.Clamp(g(PGate), 0.05, 2.0) * 60000.0 / (bpm > 0 ? bpm : 120)),
    };
    internal static string Signed(double v) => (v > 0.004 ? "+" : v < -0.004 ? "−" : "") + $"{Math.Abs(Math.Round(v * 100)):0}\u2009%";

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
        if (!HasLayout(engine, track, mi)) return new TextBlock { Text = "Nota Length", Margin = new Thickness(8) };
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        float Def(int p) => engine.MidiEffectParamDefault(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void Learn(Control c, int p, string name) => MidiLearn.Bind(c, MidiTarget.MidiDeviceParam(track, mi, p), name);
        double Bpm() => engine.Bpm > 0 ? engine.Bpm : 120;
        bool Bypassed() => engine.MidiEffectBypassed(track, mi);

        bool mini = IsMini(engine, track, mi);
        var readouts = new List<Action>();
        void Refresh() { foreach (var r in readouts) r(); }

        // ---- the animated model: playhead + Random's roll per pass ---------------------------------
        double now = 0; int cycle = 0;
        var notes = LengthGateViz.Compute(G, Bpm(), 0);
        void Tick()
        {
            if (!Bypassed())
            {
                double pos = engine.IsPlaying
                    ? engine.PositionBeats
                    : Stopwatch.GetElapsedTime(ClockStart).TotalMinutes * Bpm();
                now = (pos % LengthGateViz.Bar + LengthGateViz.Bar) % LengthGateViz.Bar;
                cycle = (int)Math.Floor(pos / LengthGateViz.Bar);
            }
            notes = LengthGateViz.Compute(G, Bpm(), cycle);
        }
        string AvgOut() => LengthGateViz.Ms(notes.Average(n => n.Out) * 60000.0 / Bpm());
        string TrigName() => G(PTrigger) >= 0.5f ? "note-off" : "note-on";

        Tick();
        Control rootView = mini ? BuildMini() : BuildLarge();
        ctx.AddDeviceRefresher(() => { Tick(); Refresh(); });
        Refresh();
        return rootView;

        // =========================================================================================
        Control BuildLarge()
        {
            // ---- LENGTH ----
            var modeSeg = DeviceCardKit.Segments(Modes, () => Mode(G), i => { Begin(PMode); S(PMode, i); End(PMode); Refresh(); }, out var modeSync,
                fill: true, padX: 2);
            readouts.Add(modeSync);
            Learn(modeSeg, PMode, "Mode");
            ToolTip.SetTip(modeSeg, "Sync: a tempo division · ms: a fixed time · Gate %: a share of how long each note was held");

            var bigVal = new TextBlock { FontSize = 20, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center, LineHeight = 24 };
            var bigSub = DeviceBodyKit.Note(); bigSub.HorizontalAlignment = HorizontalAlignment.Right;
            var valRow = new DockPanel { Children = { WithDock(bigSub, Dock.Right), bigVal } };

            int lenGesture = -1;
            var lenTrack = new SliderTrack { Reset = () => { int p = LenParam(G); Begin(p); S(p, Def(p)); End(p); Refresh(); } };
            lenTrack.Changed += n => { S(LenParam(G), LenFromNorm(G, n)); Refresh(); };
            lenTrack.GestureBegin += () => { lenGesture = LenParam(G); Begin(lenGesture); };
            lenTrack.GestureEnd += () => { if (lenGesture >= 0) End(lenGesture); lenGesture = -1; };
            ToolTip.SetTip(lenTrack, "Length — drag up / down, double-click resets");
            ValueEntry.Attach(bigVal, () => LenNorm(G), n => S(LenParam(G), LenFromNorm(G, n)), () => LenValue(G),
                () => { lenGesture = LenParam(G); Begin(lenGesture); }, () => { End(lenGesture); lenGesture = -1; }, Refresh, control: lenTrack);
            readouts.Add(() =>
            {
                bigVal.Text = LenValue(G); bigSub.Text = LenSub(G, Bpm());
                if (!lenTrack.Dragging) lenTrack.Norm = LenNorm(G);
            });

            // The eight sync divisions: a segmented grid in a groove, the picked one raised.
            var divCells = new Grid { RowSpacing = 2, ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("18,18") };
            for (int i = 0; i < Divs.Length; i++)
            {
                int di = i;
                var tb = new TextBlock { Text = Divs[i], FontSize = NotaType.KnobValue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var chip = new Border { CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                chip.PointerPressed += (_, ev) =>
                {
                    if (!ev.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
                    Begin(PDivision); S(PDivision, di); End(PDivision); Refresh(); ev.Handled = true;
                };
                ToolTip.SetTip(chip, $"{Divs[i]} note");
                readouts.Add(() =>
                {
                    bool on = Div(G) == di;
                    chip.Background = on ? NotaPalette.AccentSoft : Brushes.Transparent;
                    if (on) chip.BindResource(Border.BoxShadowProperty, "Shadow.Lifted"); else chip.BoxShadow = default;
                    tb.Foreground = on ? NotaPalette.AccentBright : Sub;
                    tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                });
                Grid.SetColumn(chip, i % 4); Grid.SetRow(chip, i / 4);
                divCells.Children.Add(chip);
            }
            var divGrid = new Border { Background = NotaPalette.Groove, CornerRadius = NotaRadius.Group, Padding = new Thickness(2), Child = divCells };
            divGrid.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
            Learn(divGrid, PDivision, "Division");
            readouts.Add(() => divGrid.IsVisible = Mode(G) == 0);

            var trigSeg = DeviceCardKit.Segments(new[] { "Note-on", "Note-off" }, () => G(PTrigger) >= 0.5f ? 1 : 0,
                i => { Begin(PTrigger); S(PTrigger, i); End(PTrigger); Refresh(); }, out var trigSync, fill: true, padX: 2);
            readouts.Add(trigSync);
            Learn(trigSeg, PTrigger, "Start From");
            ToolTip.SetTip(trigSeg, "Count the length from the note-on, or from the note-off (the note fires when the key is released)");
            var trigBox = new StackPanel { Spacing = 4, Children = { Caps("Start from"), trigSeg } };

            var lenBody = new DockPanel { Children =
            {
                WithDock(trigBox, Dock.Bottom),
                new StackPanel { Spacing = 4, Children = { modeSeg, valRow, lenTrack, divGrid } },
            } };
            var lenIsland = IslandBox(IslandHead(Caps("Length"), null), lenBody, 149);
            lenIsland.Padding = new Thickness(12, 12, 12, 10);

            // ---- GATE ----
            var gate = new LengthGateViz { Full = true };
            var gateInfo = Mono("", Cap, 7);
            readouts.Add(() =>
            {
                gateInfo.Text = $"1 bar at {Bpm():0} BPM · {LengthGateViz.Input.Length} notes";
                gate.Set(notes, now, Bpm(), Bypassed());
            });
            ToolTip.SetTip(gate, "Outline — the note played in · fill — the note out · band — the range Random spreads it over. The shaded part is past the bar line.");
            var gateIsland = IslandBox(IslandHead(Caps("Gate"), gateInfo), gate, double.NaN);

            // ---- MODIFIERS ----
            var mods = new StackPanel { Spacing = 8, Children =
            {
                ModSlider("Vel → len", PVelToLen, true, "loud notes longer at +"),
                ModSlider("Key → len", PKeyToLen, true, "low notes longer at +"),
                ModSlider("Random", PRandom, false, "spread per note"),
            } };
            var flags = new StackPanel { Spacing = 6, Children =
            {
                DeviceBodyKit.Rule(),
                Flag(PLegato, "Legato · retrigger held", "Each note lasts until the next one starts (at most to the bar line)"),
                Flag(PClipLimit, "Clip length limit", "No note crosses the bar line it started in"),
            } };
            var modIsland = IslandBox(IslandHead(Caps("Modifiers"), null),
                new DockPanel { Children = { WithDock(flags, Dock.Bottom), mods } }, 198);

            lenIsland.Margin = new Thickness(0, 0, DeviceCardKit.SectionGap, 0); modIsland.Margin = new Thickness(DeviceCardKit.SectionGap, 0, 0, 0);
            var islands = new DockPanel { Children = { WithDock(lenIsland, Dock.Left), WithDock(modIsland, Dock.Right), gateIsland } };

            return DeviceBodyKit.Frame(islands);
        }

        // =========================================================================================
        Control BuildMini()
        {
            // Mode chip: click for the next mode, Shift-click for the previous.
            var modeTb = new TextBlock { FontSize = NotaType.KnobValue, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
            var chev = new Glyph(GlyphKind.ChevronDown, 8) { Foreground = Cap, VerticalAlignment = VerticalAlignment.Center };
            var modeChip = new Border { Height = 22, Width = 80, CornerRadius = NotaRadius.Field, Background = NotaPalette.SurfaceHover,
                Padding = new Thickness(8, 0), Cursor = new Cursor(StandardCursorType.Hand), Margin = new Thickness(0, 0, 6, 0),
                Child = new DockPanel { Children = { WithDock(chev, Dock.Right), modeTb } } };
            modeChip.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(modeChip).Properties.IsLeftButtonPressed) return;
                int d = ev.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2 : 1;
                Begin(PMode); S(PMode, (Mode(G) + d) % 3); End(PMode); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(modeChip, "Length mode: Sync / ms / Gate % — click for the next, Shift-click for the previous");
            Learn(modeChip, PMode, "Mode");

            var val = Mono("", Txt, 9);
            var sub = Mono("", Cap, 7); sub.Margin = new Thickness(6, 0, 0, 0);
            var head = new DockPanel { Height = 22, Children = { WithDock(modeChip, Dock.Left), WithDock(val, Dock.Left), WithDock(sub, Dock.Left) } };

            var gate = new LengthGateViz { Full = false };
            ToolTip.SetTip(gate, "Outline — in · fill — out · band — Random's range");

            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            var cells = new[]
            {
                KnobCell("Length", () => LenParam(G), () => LenNorm(G), n => S(LenParam(G), LenFromNorm(G, n)), () => LenValue(G)),
                KnobCell("Vel", () => PVelToLen, () => (G(PVelToLen) + 1) / 2, n => S(PVelToLen, Math.Round(n * 2 - 1, 3)), () => Signed(G(PVelToLen)), 0.5),
                KnobCell("Key", () => PKeyToLen, () => (G(PKeyToLen) + 1) / 2, n => S(PKeyToLen, Math.Round(n * 2 - 1, 3)), () => Signed(G(PKeyToLen)), 0.5),
                KnobCell("Random", () => PRandom, () => G(PRandom), n => S(PRandom, Math.Round(n, 3)), () => $"±{Math.Round(G(PRandom) * 100):0}\u2009%", 0),
            };
            for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); knobs.Children.Add(cells[i]); }

            var inner = new DockPanel { Children =
            {
                WithDock(head, Dock.Top), WithDock(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 8), Child = gate },
            } };
            var island = DeviceBodyKit.Sec(inner, new Thickness(10, 10, 10, 8));
            readouts.Add(() =>
            {
                modeTb.Text = Modes[Mode(G)];
                val.Text = LenValue(G); sub.Text = LenSub(G, Bpm());
                gate.Set(notes, now, Bpm(), Bypassed());
            });

            // "from note-on" (on the head row, right) flips the start point.
            var statusL = new TextBlock { FontSize = NotaType.RowLabel, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            statusL.PointerEntered += (_, _) => statusL.Foreground = Txt;
            statusL.PointerExited += (_, _) => statusL.Foreground = Sub;
            statusL.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(statusL).Properties.IsLeftButtonPressed) return;
                Begin(PTrigger); S(PTrigger, G(PTrigger) >= 0.5f ? 0 : 1); End(PTrigger); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(statusL, "Count the length from the note-on or the note-off — click to switch");
            Learn(statusL, PTrigger, "Start From");
            statusL.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Insert(0, WithDock(statusL, Dock.Right));
            readouts.Add(() => statusL.Text = Bypassed() ? "bypassed" : $"from {TrigName()}");
            return DeviceBodyKit.Frame(island);
        }

        // ---- shared pieces -----------------------------------------------------------------------

        Control IslandHead(Control left, Control? right)
        {
            if (left is TextBlock t) { t.FontSize = NotaType.DeviceCaption; t.FontWeight = FontWeight.SemiBold; t.Foreground = NotaPalette.TextPrimary; }
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            if (right is not null) { right.HorizontalAlignment = HorizontalAlignment.Right; d.Children.Add(WithDock(right, Dock.Right)); }
            d.Children.Add(left);
            return d;
        }

        Decorator IslandBox(Control head, Control content, double width)
        {
            return DeviceBodyKit.Sec(new DockPanel { Children = { WithDock(head, Dock.Top), content } }, DeviceCardKit.SectionPad, width);
        }

        // A modifier: label · value bar · value on one row, its hint under the bar.
        Control ModSlider(string label, int p, bool bipolar, string hint)
        {
            Func<double> toNorm = bipolar ? () => (G(p) + 1) / 2 : () => G(p);
            Func<double, double> fromNorm = bipolar ? n => Math.Round(n * 2 - 1, 3) : n => Math.Round(n, 3);
            var row = DeviceCardKit.SliderRow(label, toNorm, n => { S(p, fromNorm(n)); Refresh(); },
                () => bipolar ? Signed(G(p)) : $"±{Math.Round(G(p) * 100):0}\u2009%", out var sync,
                () => Begin(p), () => End(p), () => { Begin(p); S(p, Def(p)); End(p); Refresh(); }, bipolar,
                labelWidth: 50, valueWidth: 38);
            var trk = row.Children.OfType<SliderTrack>().First();
            Learn(trk, p, label);
            ToolTip.SetTip(trk, $"{label} — drag up / down, double-click resets");
            readouts.Add(sync);
            return new StackPanel { Spacing = 3, Children =
            {
                row,
                new TextBlock { Text = hint, FontSize = NotaType.RowLabel, Foreground = Cap, Margin = new Thickness(58, 0, 0, 0) },
            } };
        }

        // A switch + sentence-case label (Legato / Clip length limit).
        Control Flag(int p, string label, string tip)
        {
            var sw = new SwitchTrack { VerticalAlignment = VerticalAlignment.Center };
            var tb = new TextBlock { Text = label, FontSize = NotaType.DeviceCaption, VerticalAlignment = VerticalAlignment.Center };
            var row = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sw, tb } } };
            row.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                Begin(p); S(p, G(p) >= 0.5f ? 0 : 1); End(p); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(row, tip);
            Learn(row, p, label);
            readouts.Add(() => { bool on = G(p) >= 0.5f; sw.IsOn = on; tb.Foreground = on ? Txt : Sub; });
            return row;
        }

        // A mini-view knob: the device knob, value (11, ink 1) over label (10.5, ink 3). The target param may follow the mode (Length).
        Control KnobCell(string label, Func<int> param, Func<double> norm, Action<double> set, Func<string> text, double resetNorm = double.NaN)
        {
            var knob = new Knob(norm(), 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center, Default = resetNorm };
            int gesture = -1;
            knob.ValueChanged += n => { set(n); Refresh(); };
            knob.GestureBegin += () => { gesture = param(); Begin(gesture); };
            knob.GestureEnd += () => { if (gesture >= 0) End(gesture); gesture = -1; };
            Learn(knob, param(), label);
            var v = Mono("", Txt); v.HorizontalAlignment = HorizontalAlignment.Center; v.Margin = new Thickness(0, 4, 0, 0);
            ValueEntry.Attach(knob, label, v, text);
            readouts.Add(() => { double n = norm(); if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n; v.Text = text(); });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { knob, v, new TextBlock { Text = NotaText.Label(label), FontSize = NotaType.KnobLabel, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 13 } } };
        }
    }
}
