// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Length editor (MIDI effect kind 3), almanac mockups 1a / 1b.
// Two sizes share every value; the S / L toggle in the shell header flips the card (the
// choice is the device's "View" param, so it persists with the project):
//   • L 700 × 260 — LENGTH (mode Sync / ms / Gate %, the big value, its slider, the eight
//     sync divisions, Start from note-on / note-off) | GATE (one bar of five demo notes: the
//     input outlined, the output filled, Random's range as a band, a playhead) | MODIFIERS
//     (bipolar Vel → Len and Key → Len, Random, Legato, Clip length limit) — over a status line.
//   • S 260 × 260 — the mode chip and value over the GATE window and four knobs (Length ·
//     Vel · Key · Random); the footer's "from note-on" flips the start point. Legato and
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

    private static readonly IBrush Ground = NotaPalette.SurfaceInset;
    private static readonly IBrush Island = NotaPalette.SurfaceCard;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Rule = NotaPalette.GraphBorder;
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush BrassLit = NotaPalette.AccentBright;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Muted = NotaPalette.TextMuted;
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
            var tag = Mono("", Cap, 8); tag.LetterSpacing = 0.6;
            var bpm = Mono("", Cap, 8);
            void Sync()
            {
                tag.Text = Modes[Math.Clamp((int)Math.Round(e.MidiEffectGetParam(t, index, PMode)), 0, 2)].ToUpperInvariant();
                bpm.Text = $"{e.Bpm:0.0} BPM";
            }
            Sync();
            ctx.AddDeviceRefresher(Sync);
            ToolTip.SetTip(tag, "Length mode");
            row.Children.Add(tag); row.Children.Add(MidiCardHeader.LearnButton(ctx)); row.Children.Add(bpm);
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
    private static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
    { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, LetterSpacing = 0.8, Foreground = c ?? Cap, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Mono(string t, IBrush c, double fs = 8) => new()
    { Text = t, FontSize = fs, FontFamily = NotaFonts.MonoFamily, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
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
                fill: true, padX: 2, fontSize: 8);
            readouts.Add(modeSync);
            Learn(modeSeg, PMode, "Mode");
            ToolTip.SetTip(modeSeg, "Sync: a tempo division · ms: a fixed time · Gate %: a share of how long each note was held");

            var bigVal = Mono("", BrassLit, 18); bigVal.FontWeight = FontWeight.Medium; bigVal.LetterSpacing = -0.3;
            var bigSub = Mono("", Cap, 7); bigSub.VerticalAlignment = VerticalAlignment.Bottom; bigSub.Margin = new Thickness(0, 0, 0, 4);
            var valRow = new DockPanel { Children = { WithDock(bigSub, Dock.Right), bigVal } };
            bigSub.HorizontalAlignment = HorizontalAlignment.Right;

            int lenGesture = -1;
            var lenTrack = new SliderTrack { Height = 11, Reset = () => { int p = LenParam(G); Begin(p); S(p, Def(p)); End(p); Refresh(); } };
            lenTrack.Changed += n => { S(LenParam(G), LenFromNorm(G, n)); Refresh(); };
            lenTrack.GestureBegin += () => { lenGesture = LenParam(G); Begin(lenGesture); };
            lenTrack.GestureEnd += () => { if (lenGesture >= 0) End(lenGesture); lenGesture = -1; };
            ToolTip.SetTip(lenTrack, "Length — drag up / down, double-click resets");
            readouts.Add(() =>
            {
                bigVal.Text = LenValue(G); bigSub.Text = LenSub(G, Bpm());
                if (!lenTrack.Dragging) lenTrack.Norm = LenNorm(G);
            });

            var divGrid = new Grid { ColumnSpacing = 2, RowSpacing = 2, ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("14,14") };
            for (int i = 0; i < Divs.Length; i++)
            {
                int di = i;
                var tb = Mono(Divs[i], Cap, 7); tb.HorizontalAlignment = HorizontalAlignment.Center;
                var chip = new Border { CornerRadius = NotaRadius.Clip, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                chip.PointerPressed += (_, ev) =>
                {
                    if (!ev.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
                    Begin(PDivision); S(PDivision, di); End(PDivision); Refresh(); ev.Handled = true;
                };
                ToolTip.SetTip(chip, $"{Divs[i]} note");
                readouts.Add(() =>
                {
                    bool on = Div(G) == di;
                    chip.Background = on ? NotaPalette.Accent : NotaPalette.GridBeat;
                    tb.Foreground = on ? NotaPalette.TextOnAccent : Cap;
                });
                Grid.SetColumn(chip, i % 4); Grid.SetRow(chip, i / 4);
                divGrid.Children.Add(chip);
            }
            Learn(divGrid, PDivision, "Division");
            readouts.Add(() => divGrid.IsVisible = Mode(G) == 0);

            var trigSeg = DeviceCardKit.Segments(new[] { "Note-on", "Note-off" }, () => G(PTrigger) >= 0.5f ? 1 : 0,
                i => { Begin(PTrigger); S(PTrigger, i); End(PTrigger); Refresh(); }, out var trigSync, fill: true, padX: 2, fontSize: 8);
            readouts.Add(trigSync);
            Learn(trigSeg, PTrigger, "Start From");
            ToolTip.SetTip(trigSeg, "Count the length from the note-on, or from the note-off (the note fires when the key is released)");
            var trigBox = new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0),
                Child = new StackPanel { Spacing = 4, Children = { Caps("START FROM"), trigSeg } } };

            var lenBody = new DockPanel { Margin = new Thickness(8, 6), Children =
            {
                WithDock(trigBox, Dock.Bottom),
                new StackPanel { Spacing = 6, Children = { modeSeg, valRow, lenTrack, divGrid } },
            } };
            var lenIsland = IslandBox(IslandHead(Caps("LENGTH"), null), lenBody, 132);

            // ---- GATE ----
            var gate = new LengthGateViz { Full = true };
            var gateInfo = Mono("", Cap, 7);
            readouts.Add(() =>
            {
                gateInfo.Text = $"1 bar at {Bpm():0} BPM · {LengthGateViz.Input.Length} notes";
                gate.Set(notes, now, Bpm(), Bypassed());
            });
            ToolTip.SetTip(gate, "Outline — the note played in · fill — the note out · band — the range Random spreads it over. The shaded part is past the bar line.");
            var gateIsland = IslandBox(IslandHead(Caps("GATE"), gateInfo), new Border { Padding = new Thickness(8, 5, 8, 4), Child = gate }, double.NaN);

            // ---- MODIFIERS ----
            var mods = new StackPanel { Spacing = 9, Children =
            {
                ModSlider("VEL → LEN", PVelToLen, true, "loud notes longer at +"),
                ModSlider("KEY → LEN", PKeyToLen, true, "low notes longer at +"),
                ModSlider("RANDOM", PRandom, false, "spread per note"),
            } };
            var flags = new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0),
                Child = new StackPanel { Spacing = 5, Children =
                {
                    Flag(PLegato, "Legato · retrigger held", "Each note lasts until the next one starts (at most to the bar line)"),
                    Flag(PClipLimit, "Clip length limit", "No note crosses the bar line it started in"),
                } } };
            var modIsland = IslandBox(IslandHead(Caps("MODIFIERS"), null),
                new DockPanel { Margin = new Thickness(8, 7), Children = { WithDock(flags, Dock.Bottom), mods } }, 172);

            lenIsland.Margin = new Thickness(0, 0, 5, 0); modIsland.Margin = new Thickness(5, 0, 0, 0);
            var islands = new DockPanel { Children = { WithDock(lenIsland, Dock.Left), WithDock(modIsland, Dock.Right), gateIsland } };

            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                statusL.Text = $"{Modes[Mode(G)]} {LenValue(G)} · from {TrigName()} · avg out {AvgOut()}"
                             + (G(PLegato) >= 0.5f ? " · legato" : "") + (G(PClipLimit) >= 0.5f ? " · clip" : "");
                statusR.Text = Bypassed() ? "bypassed" : "MIDI · latency 0 smp";
            });
            return Framed(islands, statusL, statusR);
        }

        // =========================================================================================
        Control BuildMini()
        {
            // Mode chip: click for the next mode, Shift-click for the previous.
            var modeTb = new TextBlock { FontSize = 9, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Foreground = Cap, VerticalAlignment = VerticalAlignment.Center };
            var modeChip = new Border { Height = 14, Width = 58, CornerRadius = NotaRadius.Badge, Background = NotaPalette.GridBeat, BorderBrush = Bd, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), Margin = new Thickness(0, 0, 6, 0),
                Child = new DockPanel { Children = { WithDock(chev, Dock.Right), modeTb } } };
            modeChip.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(modeChip).Properties.IsLeftButtonPressed) return;
                int d = ev.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2 : 1;
                Begin(PMode); S(PMode, (Mode(G) + d) % 3); End(PMode); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(modeChip, "Length mode: Sync / ms / Gate % — click for the next, Shift-click for the previous");
            Learn(modeChip, PMode, "Mode");

            var val = Mono("", BrassLit, 9); val.FontWeight = FontWeight.Medium;
            var sub = Mono("", Cap, 7);
            var head = new DockPanel { Height = 14, Children = { WithDock(modeChip, Dock.Left), WithDock(sub, Dock.Right), val } };

            var gate = new LengthGateViz { Full = false };
            ToolTip.SetTip(gate, "Outline — in · fill — out · band — Random's range");

            var knobs = new Grid { Height = 53, ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            var cells = new[]
            {
                KnobCell("LENGTH", () => LenParam(G), () => LenNorm(G), n => S(LenParam(G), LenFromNorm(G, n)), () => LenValue(G)),
                KnobCell("VEL", () => PVelToLen, () => (G(PVelToLen) + 1) / 2, n => S(PVelToLen, Math.Round(n * 2 - 1, 3)), () => Signed(G(PVelToLen)), 0.5),
                KnobCell("KEY", () => PKeyToLen, () => (G(PKeyToLen) + 1) / 2, n => S(PKeyToLen, Math.Round(n * 2 - 1, 3)), () => Signed(G(PKeyToLen)), 0.5),
                KnobCell("RANDOM", () => PRandom, () => G(PRandom), n => S(PRandom, Math.Round(n, 3)), () => $"±{Math.Round(G(PRandom) * 100):0}\u2009%", 0),
            };
            for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); knobs.Children.Add(cells[i]); }

            var inner = new DockPanel { Children =
            {
                WithDock(head, Dock.Top), WithDock(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 4), Child = gate },
            } };
            var island = new Border { Background = Island, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
                Padding = new Thickness(6, 5, 6, 0), Child = inner };
            readouts.Add(() =>
            {
                modeTb.Text = Modes[Mode(G)];
                val.Text = LenValue(G); sub.Text = LenSub(G, Bpm());
                gate.Set(notes, now, Bpm(), Bypassed());
            });

            // Footer: "from note-on" flips the start point.
            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            statusL.PointerEntered += (_, _) => statusL.Foreground = Txt;
            statusL.PointerExited += (_, _) => statusL.Foreground = Sub;
            statusL.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(statusL).Properties.IsLeftButtonPressed) return;
                Begin(PTrigger); S(PTrigger, G(PTrigger) >= 0.5f ? 0 : 1); End(PTrigger); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(statusL, "Count the length from the note-on or the note-off — click to switch");
            Learn(statusL, PTrigger, "Start From");
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                statusL.Text = $"from {TrigName()}";
                statusR.Text = Bypassed() ? "bypassed" : $"avg {AvgOut()}";
            });
            return Framed(island, statusL, statusR);
        }

        // ---- shared pieces -----------------------------------------------------------------------
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
            if (right is not null) { right.HorizontalAlignment = HorizontalAlignment.Right; d.Children.Add(WithDock(right, Dock.Right)); }
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

        // A MODIFIERS slider: caps label + value (brass once moved off neutral), the track, a hint.
        Control ModSlider(string label, int p, bool bipolar, string hint)
        {
            var l = Caps(label);
            var v = Mono("", Txt); v.HorizontalAlignment = HorizontalAlignment.Right;
            Func<double> toNorm = bipolar ? () => (G(p) + 1) / 2 : () => G(p);
            Func<double, double> fromNorm = bipolar ? n => Math.Round(n * 2 - 1, 3) : n => Math.Round(n, 3);
            var trk = new SliderTrack { Height = 11, Bipolar = bipolar, Reset = () => { Begin(p); S(p, Def(p)); End(p); Refresh(); } };
            trk.Changed += n => { S(p, fromNorm(n)); Refresh(); };
            trk.GestureBegin += () => Begin(p);
            trk.GestureEnd += () => End(p);
            Learn(trk, p, label);
            ToolTip.SetTip(trk, $"{label} — drag up / down, double-click resets");
            readouts.Add(() =>
            {
                double x = G(p);
                if (!trk.Dragging) trk.Norm = toNorm();
                bool mod = Math.Abs(x) > 0.004;
                l.Foreground = mod ? BrassLit : Cap;
                v.Foreground = mod ? BrassLit : Txt;
                v.Text = bipolar ? Signed(x) : $"±{Math.Round(x * 100):0}\u2009%";
            });
            return new StackPanel { Spacing = 2, Children =
            {
                new DockPanel { Children = { WithDock(v, Dock.Right), l } }, trk,
                new TextBlock { Text = hint, FontSize = 7, Foreground = Cap },
            } };
        }

        // A switch + sentence-case label (Legato / Clip length limit).
        Control Flag(int p, string label, string tip)
        {
            var sw = new SwitchTrack { VerticalAlignment = VerticalAlignment.Center };
            var tb = new TextBlock { Text = label, FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
            var row = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { sw, tb } } };
            row.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                Begin(p); S(p, G(p) >= 0.5f ? 0 : 1); End(p); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(row, tip);
            Learn(row, p, label);
            readouts.Add(() => { bool on = G(p) >= 0.5f; sw.IsOn = on; tb.Foreground = on ? Txt : Muted; });
            return row;
        }

        // A mini-view knob: 34px gauge, caps label, mono value. The target param may follow the mode (LENGTH).
        Control KnobCell(string label, Func<int> param, Func<double> norm, Action<double> set, Func<string> text, double resetNorm = double.NaN)
        {
            var knob = new Knob(norm(), 1.0) { Accent = true, Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center, Default = resetNorm };
            int gesture = -1;
            knob.ValueChanged += n => { set(n); Refresh(); };
            knob.GestureBegin += () => { gesture = param(); Begin(gesture); };
            knob.GestureEnd += () => { if (gesture >= 0) End(gesture); gesture = -1; };
            Learn(knob, param(), label);
            var v = Mono("", Sub, 7); v.HorizontalAlignment = HorizontalAlignment.Center;
            readouts.Add(() => { double n = norm(); if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n; v.Text = text(); });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { knob, new TextBlock { Text = label, FontSize = 7, FontWeight = FontWeight.Bold, LetterSpacing = 0.6, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 9 }, v } };
        }
    }
}
