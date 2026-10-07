// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Chord editor (MIDI effect kind 1), almanac mockups 1a / 1b.
// Two sizes share every value; the S / L toggle in the shell header flips the card (the
// choice is the device's "View" param, so it persists with the project):
//   • L 700 × 260 — TYPE list (Maj7 / Min7 / Sus4 / 5th / Custom, Keep root, Fold in scale +
//     its key) | SHIFTS: six rows — switch dot, semitones, a bipolar ±12 ruler, the interval
//     name and a draggable velocity offset | RESULT: the chord's note names, a three-octave
//     keyboard (played Brass Light, added Brass Deep, an unplayed root outlined), Strum and
//     Spread — over a status line.
//   • S 260 × 260 — note names + the keyboard over six vertical bipolar shift bars (the
//     number under each toggles it), Strum / Spread knobs and the two flags. The header keeps
//     the same preset picker as L; presets set everything, S just edits less.
// Editing a shift turns TYPE to Custom. The keyboard animates: a held chord lights as the
// engine strums it (telemetry), and otherwise the preview strums once per bar while the
// transport runs, and once after each edit.

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

namespace Nota.App;

internal sealed class ChordMidiBody : IMidiDeviceBody
{
    // Param indices — mirror MidiChord.h.
    internal const int Voice1 = 0, Strum = 6, KeepRoot = 7, Spread = 8, Fold = 9, Vel1 = 10, On1 = 16,
                       FoldKey = 22, FoldMode = 23, PView = 24, Slots = 6;
    private const int DefaultRoot = 48;   // C3 — the preview's input before any note was played

    internal static readonly (string Name, int[]? Shifts)[] Types =
    {
        ("Maj7", new[] { 4, 7, 11 }), ("Min7", new[] { 3, 7, 10 }), ("Sus4", new[] { 5, 7 }), ("5th", new[] { 7 }), ("Custom", null),
    };
    private static readonly string[] Intervals = { "P1", "m2", "M2", "m3", "M3", "P4", "TT", "P5", "m6", "M6", "m7", "M7", "P8" };
    private static readonly string[] KeyNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly bool[] MajorPc = { true, false, true, false, true, true, false, true, false, true, false, true };
    private static readonly bool[] MinorPc = { true, false, true, true, false, true, false, true, true, false, true, false };

    private static readonly IBrush Ground = NotaPalette.SurfaceInset;
    private static readonly IBrush Island = NotaPalette.SurfaceCard;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Rule = NotaPalette.GraphBorder;
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush BrassLit = NotaPalette.AccentBright;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;
    private static readonly IBrush Axis = NotaPalette.TextAxis;

    // Per-card editor state that survives rebuilds — UI only, not persisted.
    private sealed class Ui { public bool CustomPinned; public long EditTicks; }
    private static readonly Dictionary<(int, int), Ui> UiState = new();
    private static Ui UiFor(int track, int mi) { if (!UiState.TryGetValue((track, mi), out var u)) UiState[(track, mi)] = u = new Ui(); return u; }

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
        if (!IsMini(e, t, index)) row.Children.Add(MidiCardHeader.LearnButton(ctx));
        row.Children.Add(MidiCardHeader.SizeToggle(ctx, index, PView, () => IsMini(e, t, index)));
        return row;
    }

    // ---- the chord model (mirrors MidiChord::process) ------------------------------------------
    internal readonly record struct Note(int Pitch, bool Played, int Vel);

    internal static List<Note> Compute(Func<int, float> g, int root, int inVel = 100)
    {
        bool keep = g(KeepRoot) >= 0.5f, fold = g(Fold) >= 0.5f, minor = g(FoldMode) >= 0.5f;
        int key = (((int)Math.Round(g(FoldKey))) % 12 + 12) % 12;
        int level = Math.Min(2, (int)Math.Floor(Math.Clamp(g(Spread) / 100.0, 0, 1) * 3 + 1e-9));
        var list = new List<Note>();
        void Add(int p, int v, bool played)
        {
            if (p is < 0 or > 127) return;
            int i = list.FindIndex(n => n.Pitch == p);
            if (i >= 0) { if (played) list[i] = new Note(p, true, v); return; }
            list.Add(new Note(p, played, v));
        }
        if (keep) Add(root, inVel, true);
        int k = 0;
        for (int s = 0; s < Slots; s++)
        {
            if (g(On1 + s) < 0.5f) continue;
            int p = root + Math.Clamp((int)Math.Round(g(Voice1 + s)), -24, 24);
            if (fold && !(minor ? MinorPc : MajorPc)[((p - key) % 12 + 12) % 12]) p -= 1;
            if ((k % 2 == 0 && level >= 1) || (k % 2 == 1 && level >= 2)) p += 12;
            k++;
            Add(p, Math.Clamp(inVel + (int)Math.Round(g(Vel1 + s)), 1, 127), false);
        }
        list.Sort((a, b) => a.Pitch.CompareTo(b.Pitch));
        return list;
    }

    /// <summary>The TYPE row the active shifts match (Custom when none).</summary>
    internal static int MatchType(Func<int, float> g)
    {
        var on = Enumerable.Range(0, Slots).Where(s => g(On1 + s) >= 0.5f).Select(s => (int)Math.Round(g(Voice1 + s))).OrderBy(x => x).ToArray();
        for (int i = 0; i < Types.Length; i++)
            if (Types[i].Shifts is { } iv && on.SequenceEqual(iv.OrderBy(x => x))) return i;
        return Types.Length - 1;
    }

    internal static string Sgn(int v) => v > 0 ? $"+{v}" : v < 0 ? $"−{-v}" : "0";
    internal static string IntervalName(int st) => Math.Abs(st) <= 12 ? (st < 0 ? "−" : "") + Intervals[Math.Abs(st)] : Sgn(st);
    internal static string ScaleName(Func<int, float> g, bool shortForm)
    {
        string key = KeyNames[(((int)Math.Round(g(FoldKey))) % 12 + 12) % 12];
        bool minor = g(FoldMode) >= 0.5f;
        return shortForm ? $"{key} {(minor ? "min" : "maj")}" : $"{key} {(minor ? "minor" : "major")}";
    }

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
        if (!HasLayout(engine, track, mi)) return new TextBlock { Text = "Nota Chord", Margin = new Thickness(8) };
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        int GI(int p) => (int)Math.Round(G(p));
        float Def(int p) => engine.MidiEffectParamDefault(track, mi, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void Learn(Control c, int p, string name) => MidiLearn.Bind(c, MidiTarget.MidiDeviceParam(track, mi, p), name);

        var ui = UiFor(track, mi);
        bool mini = IsMini(engine, track, mi);
        var readouts = new List<Action>();
        void Refresh() { foreach (var r in readouts) r(); }
        void Edited() { ui.EditTicks = Stopwatch.GetTimestamp(); Refresh(); }
        void S(int p, double v) { engine.MidiEffectSetParam(track, mi, p, (float)v); }
        void SetShift(int s, int st) { S(Voice1 + s, st); S(On1 + s, 1); ui.CustomPinned = false; Edited(); }
        void ApplyType(int i)
        {
            if (Types[i].Shifts is not { } iv) { ui.CustomPinned = true; Edited(); return; }
            for (int s = 0; s < Slots; s++) { S(Voice1 + s, s < iv.Length ? iv[s] : 0); S(On1 + s, s < iv.Length ? 1 : 0); S(Vel1 + s, 0); }
            ui.CustomPinned = false; Edited();
        }
        int TypeIndex() => ui.CustomPinned ? Types.Length - 1 : MatchType(G);
        int StrumMs() => (int)Math.Round(Math.Clamp(G(Strum), 0, 200));
        string SpreadText() => $"{G(Spread):0} %";

        // ---- live model: the held chord from telemetry, else the preview for the last root -----
        var scope = new float[4 + 2 * (Slots + 1)];
        int root = DefaultRoot, voices = 0;
        var notes = new List<Note>();
        var lit = new List<(int, bool)>();
        void Poll()
        {
            int sn = engine.MidiEffectScope(track, mi, scope);
            int lastRoot = sn >= 4 ? (int)scope[0] : -1, held = sn >= 4 ? (int)scope[1] : 0;
            bool bypassed = engine.MidiEffectBypassed(track, mi);
            bool keep = G(KeepRoot) >= 0.5f;
            lit.Clear();
            if (held > 0 && !bypassed && lastRoot >= 0)
            {
                root = lastRoot;
                int n = Math.Min((int)scope[3], (sn - 4) / 2), sounded = (int)scope[2];
                notes = new List<Note>(n);
                for (int i = 0; i < n; i++) { int p = (int)scope[4 + i]; notes.Add(new Note(p, keep && p == root, (int)scope[4 + n + i])); }
                for (int i = 0; i < Math.Min(sounded, n); i++) lit.Add((notes[i].Pitch, notes[i].Played));
            }
            else
            {
                root = lastRoot >= 0 ? lastRoot : DefaultRoot;
                notes = Compute(G, root);
                int strum = StrumMs();
                double ms = double.PositiveInfinity;
                if (strum > 0 && !bypassed)
                {
                    double sinceEdit = Stopwatch.GetElapsedTime(ui.EditTicks).TotalMilliseconds;
                    if (ui.EditTicks != 0 && sinceEdit < strum * notes.Count + 250) ms = sinceEdit;            // replay after an edit
                    else if (engine.IsPlaying && engine.Bpm > 0) ms = (engine.PositionBeats % 4.0 + 4.0) % 4.0 * 60000.0 / engine.Bpm;   // once per bar
                }
                for (int i = 0; i < notes.Count; i++) if (ms >= i * strum) lit.Add((notes[i].Pitch, notes[i].Played));
            }
            voices = notes.Count;
        }
        int KeyStart() { int lo = notes.Count > 0 ? Math.Min(root, notes[0].Pitch) : root; return (int)Math.Floor(lo / 12.0) * 12; }
        string NoteList() => notes.Count == 0 ? "—" : string.Join(" ", notes.Select(n => DeviceCardKit.NoteName(n.Pitch)));
        int RingPitch() => G(KeepRoot) >= 0.5f ? -1 : root;
        string StatusRight() => engine.MidiEffectBypassed(track, mi) ? "bypassed" : "MIDI · latency 0 smp";

        Poll();
        Control rootView = mini ? BuildMini() : BuildLarge();
        ctx.AddDeviceRefresher(() => { Poll(); Refresh(); });
        Refresh();
        return rootView;

        // =========================================================================================
        Control BuildLarge()
        {
            // ---- TYPE ----
            var list = new StackPanel { Margin = new Thickness(0, 3) };
            for (int i = 0; i < Types.Length; i++)
            {
                int ti = i;
                var bar = new Border { Width = 2, Margin = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Left };
                var name = new TextBlock { Text = Types[i].Name, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                var hint = Mono(Types[i].Shifts is { } iv ? string.Join(" ", iv.Select(Sgn)) : "", Axis, 7);
                hint.HorizontalAlignment = HorizontalAlignment.Right;
                var rowB = new Border { Height = 18, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new Panel { Children = { bar, new DockPanel { Margin = new Thickness(8, 0), Children = { WithDock(hint, Dock.Right), name } } } } };
                bool hover = false;
                void Paint()
                {
                    bool on = TypeIndex() == ti;
                    rowB.Background = on ? NotaPalette.AccentSubtle : hover ? NotaPalette.GridBeat : Brushes.Transparent;
                    bar.Background = on ? NotaPalette.Accent : Brushes.Transparent;
                    name.Foreground = on ? Txt : Sub; name.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
                rowB.PointerEntered += (_, _) => { hover = true; Paint(); };
                rowB.PointerExited += (_, _) => { hover = false; Paint(); };
                rowB.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(rowB).Properties.IsLeftButtonPressed) return; ApplyType(ti); ev.Handled = true; };
                ToolTip.SetTip(rowB, Types[i].Shifts is null ? "Custom: your own shifts" : $"{Types[i].Name}: set the shifts to {hint.Text}");
                readouts.Add(Paint);
                list.Children.Add(rowB);
            }
            var flags = new StackPanel { Spacing = 4, Children = { Flag(KeepRoot, "Keep root", "Play the note you pressed as well as the added notes"),
                                                                   Flag(Fold, "Fold in scale", "Drop added notes outside the scale a semitone into it") } };
            flags.Children.Add(ScaleLabel());
            var typeFoot = new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 6), Child = flags };
            var typeIsland = IslandBox(IslandHead(Caps("TYPE"), null), new DockPanel { Children = { WithDock(typeFoot, Dock.Bottom), list } }, 100);

            // ---- SHIFTS ----
            var rows = new Grid { Margin = new Thickness(6, 5) };
            for (int s = 0; s < Slots; s++)
            {
                rows.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
                var r = ShiftRow(s); Grid.SetRow(r, s); rows.Children.Add(r);
            }
            var shiftsIsland = IslandBox(IslandHead(Caps("SHIFTS"), Mono("semitones · interval · velocity", Cap, 7)), rows, 292);

            // ---- RESULT ----
            var noteText = Mono("", BrassLit); noteText.FontWeight = FontWeight.Medium;
            var range = Mono("", Axis, 7); range.HorizontalAlignment = HorizontalAlignment.Right;
            var legend = new DockPanel { Children = { WithDock(range, Dock.Right), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children =
            {
                Swatch(NotaPalette.AccentBright, null, "played"), Swatch(ChordKeysViz.Added, null, "added"), Swatch(null, NotaPalette.Accent, "root off"),
            } } } };
            var keys = new ChordKeysViz { Labels = true };
            var sliders = new StackPanel { Spacing = 6, Children =
            {
                SliderLine("STRUM", Strum, v => v / 100, n => Math.Round(n * 100), v => $"{v:0} ms"),
                SliderLine("SPREAD", Spread, v => v / 100, n => Math.Round(n * 100), v => $"{v:0} %"),
            } };
            var resBody = new DockPanel { Margin = new Thickness(8, 6), Children =
            {
                WithDock(legend, Dock.Top), WithDock(sliders, Dock.Bottom), new Border { Margin = new Thickness(0, 6), Child = keys },
            } };
            var resultIsland = IslandBox(IslandHead(Caps("RESULT"), noteText), resBody, double.NaN);
            readouts.Add(() =>
            {
                noteText.Text = NoteList();
                int start = KeyStart();
                range.Text = $"{DeviceCardKit.NoteName(start)} – {DeviceCardKit.NoteName(start + 35)}";
                keys.Set(start, lit, RingPitch());
            });

            typeIsland.Margin = new Thickness(0, 0, 5, 0); shiftsIsland.Margin = new Thickness(0, 0, 5, 0);
            var islands = new DockPanel { Children = { WithDock(typeIsland, Dock.Left), WithDock(shiftsIsland, Dock.Left), resultIsland } };

            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                statusL.Text = $"In {DeviceCardKit.NoteName(root)} · {Types[TypeIndex()].Name} · {voices} voices · strum {StrumMs()}\u2009ms · spread {SpreadText()}"
                             + (G(Fold) >= 0.5f ? $" · fold {ScaleName(G, true)}" : "");
                statusR.Text = StatusRight();
            });
            return Framed(islands, statusL, statusR);
        }

        // =========================================================================================
        Control BuildMini()
        {
            var noteText = Mono("", BrassLit); noteText.FontWeight = FontWeight.Medium;
            var voicesText = Mono("", Cap, 7);
            var head = new DockPanel { Height = 12, Children = { WithDock(voicesText, Dock.Right), noteText } };
            var keys = new ChordKeysViz { Labels = false, KeyRadius = 0, Height = 40 };

            var bars = new Grid { ColumnSpacing = 3 };
            for (int s = 0; s < Slots; s++)
            {
                bars.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                var c = ShiftColumn(s); Grid.SetColumn(c, s); bars.Children.Add(c);
            }

            var knobs = new Grid { Height = 53, ColumnDefinitions = new ColumnDefinitions("*,*,78") };
            var strum = KnobCell("STRUM", Strum, () => G(Strum) / 100, n => S(Strum, Math.Round(n * 100)), () => $"{StrumMs()} ms");
            var spread = KnobCell("SPREAD", Spread, () => G(Spread) / 100, n => S(Spread, Math.Round(n * 100)), SpreadText);
            var flags = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children =
            {
                Flag(KeepRoot, "Keep root", "Play the note you pressed as well as the added notes"),
                Flag(Fold, "Fold in scale", $"Drop added notes outside {ScaleName(G, false)} a semitone into it"),
            } };
            Grid.SetColumn(spread, 1); Grid.SetColumn(flags, 2);
            knobs.Children.Add(strum); knobs.Children.Add(spread); knobs.Children.Add(flags);

            var inner = new DockPanel { Children =
            {
                WithDock(head, Dock.Top), WithDock(new Border { Margin = new Thickness(0, 4), Child = keys }, Dock.Top),
                WithDock(knobs, Dock.Bottom), bars,
            } };
            var island = new Border { Background = Island, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
                Padding = new Thickness(6, 5, 6, 0), Child = inner };
            readouts.Add(() =>
            {
                noteText.Text = NoteList();
                voicesText.Text = $"{voices} of {Slots + 1}";
                keys.Set(KeyStart(), lit, RingPitch());
            });

            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                statusL.Text = $"In {DeviceCardKit.NoteName(root)} · {Types[TypeIndex()].Name} · {voices} voices";
                statusR.Text = engine.MidiEffectBypassed(track, mi) ? "bypassed" : $"strum {StrumMs()}\u2009ms";
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

        Control Swatch(IBrush? fill, IBrush? ring, string label)
        {
            var sw = new Border { Width = 7, Height = 7, CornerRadius = NotaRadius.Clip, Background = fill, BorderBrush = ring,
                BorderThickness = new Thickness(ring is null ? 0 : 1), VerticalAlignment = VerticalAlignment.Center };
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { sw, new TextBlock { Text = label, FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center } } };
        }

        // A switch flag (Keep root / Fold in scale), brass-washed when on.
        Control Flag(int p, string label, string tip)
        {
            var tb = new TextBlock { Text = label, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Height = 16, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            b.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                S(p, G(p) >= 0.5f ? 0 : 1); Edited(); ev.Handled = true;
            };
            ToolTip.SetTip(b, tip);
            Learn(b, p, label);
            readouts.Add(() =>
            {
                bool on = G(p) >= 0.5f;
                b.Background = on ? NotaPalette.AccentSubtle : NotaPalette.TrackOff;
                b.BorderBrush = on ? NotaPalette.BorderBrass : NotaPalette.TrackOff;
                tb.Foreground = on ? NotaPalette.AccentHover : Txt;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            });
            return b;
        }

        // "scale C major": click the key for the next one (Shift = previous, wheel scrolls), the mode to flip it.
        Control ScaleLabel()
        {
            var keyTb = Mono("", Axis, 7); var modeTb = Mono("", Axis, 7);
            keyTb.Cursor = modeTb.Cursor = new Cursor(StandardCursorType.Hand);
            void StepKey(int d) { S(FoldKey, ((GI(FoldKey) + d) % 12 + 12) % 12); Edited(); }
            keyTb.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(keyTb).Properties.IsLeftButtonPressed) return;
                StepKey(ev.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); ev.Handled = true;
            };
            keyTb.PointerWheelChanged += (_, ev) => { StepKey(ev.Delta.Y > 0 ? 1 : -1); ev.Handled = true; };
            modeTb.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(modeTb).Properties.IsLeftButtonPressed) return;
                S(FoldMode, G(FoldMode) >= 0.5f ? 0 : 1); Edited(); ev.Handled = true;
            };
            ToolTip.SetTip(keyTb, "Fold key — click for the next, Shift-click for the previous");
            ToolTip.SetTip(modeTb, "Fold scale — major / natural minor");
            Learn(keyTb, FoldKey, "Fold Key"); Learn(modeTb, FoldMode, "Fold Mode");
            readouts.Add(() =>
            {
                var ink = G(Fold) >= 0.5f ? Sub : Axis;
                keyTb.Text = KeyNames[((GI(FoldKey)) % 12 + 12) % 12]; modeTb.Text = G(FoldMode) >= 0.5f ? "minor" : "major";
                keyTb.Foreground = modeTb.Foreground = ink;
            });
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Mono("scale", Axis, 7), keyTb, modeTb } };
        }

        // One SHIFTS row: switch dot · semitones · ±12 ruler · interval · velocity offset.
        Control ShiftRow(int s)
        {
            int pSt = Voice1 + s, pOn = On1 + s, pVel = Vel1 + s;
            var dot = new Ellipse { Width = 6, Height = 6 };
            var dotHit = new Border { Width = 14, Height = 14, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = dot };
            dotHit.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(dotHit).Properties.IsLeftButtonPressed) return;
                S(pOn, G(pOn) >= 0.5f ? 0 : 1); ui.CustomPinned = false; Edited(); ev.Handled = true;
            };
            ToolTip.SetTip(dotHit, $"Shift {s + 1} on / off");
            Learn(dotHit, pOn, $"Shift {s + 1} on");
            var st = Mono("", Txt, 9); st.FontWeight = FontWeight.SemiBold; st.Width = 24;
            var ruler = new ChordShiftTrack { Height = 14, Margin = new Thickness(0, 0, 2, 0) };
            ruler.Changed += v => SetShift(s, v);
            ruler.GestureBegin += () => { Begin(pSt); Begin(pOn); }; ruler.GestureEnd += () => { End(pSt); End(pOn); };
            Learn(ruler, pSt, $"Shift {s + 1}");
            var iv = Mono("", Sub); iv.Width = 22; iv.TextAlignment = TextAlignment.Center;
            var vel = VelDrag(pVel, s);
            var g = new DockPanel { Children = { WithDock(dotHit, Dock.Left), WithDock(st, Dock.Left), WithDock(vel, Dock.Right), WithDock(iv, Dock.Right), ruler } };
            st.Margin = new Thickness(6, 0, 6, 0); iv.Margin = new Thickness(6, 0, 6, 0);
            var row = new Border { Height = 24, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center, Child = g };
            readouts.Add(() =>
            {
                bool on = G(pOn) >= 0.5f; int v = GI(pSt);
                row.Background = on ? Ground : NotaPalette.GridRow;
                row.BorderBrush = on ? Bd : NotaPalette.GridBeat;
                dot.Fill = on ? NotaPalette.Accent : NotaPalette.BorderStrong;
                st.Text = on ? Sgn(v) : "off"; st.Foreground = on ? Txt : NotaPalette.TextDisabled;
                iv.Text = on ? IntervalName(v) : "·"; iv.Foreground = on ? Sub : NotaPalette.BorderStrong;
                if (!ruler.Dragging) ruler.Value = v;
                ruler.On = on;
            });
            return row;
        }

        // Velocity offset: drag sideways (2px per step), double-click resets.
        Control VelDrag(int p, int s)
        {
            var tb = Mono("", Cap); tb.Width = 28; tb.TextAlignment = TextAlignment.Right;
            var hit = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeWestEast), Child = tb, VerticalAlignment = VerticalAlignment.Stretch };
            bool drag = false; double x0 = 0; int v0 = 0;
            hit.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(hit).Properties.IsLeftButtonPressed) return;
                ev.Handled = true;
                if (ev.ClickCount == 2) { Begin(p); S(p, 0); End(p); Edited(); return; }
                drag = true; x0 = ev.GetPosition(hit).X; v0 = GI(p); ev.Pointer.Capture(hit); Begin(p);
            };
            hit.PointerMoved += (_, ev) =>
            {
                if (!drag) return;
                int v = Math.Clamp((int)Math.Round(v0 + (ev.GetPosition(hit).X - x0) / 2), -64, 63);
                if (v != GI(p)) { S(p, v); ui.CustomPinned = true; Refresh(); }
            };
            void Stop() { if (!drag) return; drag = false; End(p); Edited(); }
            hit.PointerReleased += (_, ev) => { if (drag) ev.Pointer.Capture(null); Stop(); };
            hit.PointerCaptureLost += (_, _) => Stop();
            ToolTip.SetTip(hit, $"Shift {s + 1} velocity offset — drag sideways, double-click resets");
            Learn(hit, p, $"Shift {s + 1} velocity");
            readouts.Add(() => { int v = GI(p); tb.Text = Sgn(v); tb.Foreground = v != 0 ? BrassLit : Cap; });
            return hit;
        }

        // A mini-view shift: a vertical ±12 bar over its number (click the number to switch it).
        Control ShiftColumn(int s)
        {
            int pSt = Voice1 + s, pOn = On1 + s;
            var bar = new ChordShiftTrack { Vertical = true };
            bar.Changed += v => SetShift(s, v);
            bar.GestureBegin += () => { Begin(pSt); Begin(pOn); }; bar.GestureEnd += () => { End(pSt); End(pOn); };
            Learn(bar, pSt, $"Shift {s + 1}");
            ToolTip.SetTip(bar, $"Shift {s + 1}: drag up / down (±12 semitones)");
            var lbl = Mono("", Txt); lbl.HorizontalAlignment = HorizontalAlignment.Center;
            var lblHit = new Border { Height = 11, Margin = new Thickness(0, 2, 0, 0), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = lbl };
            bool hover = false;
            lblHit.PointerEntered += (_, _) => { hover = true; Refresh(); };
            lblHit.PointerExited += (_, _) => { hover = false; Refresh(); };
            lblHit.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(lblHit).Properties.IsLeftButtonPressed) return;
                S(pOn, G(pOn) >= 0.5f ? 0 : 1); ui.CustomPinned = false; Edited(); ev.Handled = true;
            };
            Learn(lblHit, pOn, $"Shift {s + 1} on");
            readouts.Add(() =>
            {
                bool on = G(pOn) >= 0.5f; int v = GI(pSt);
                if (!bar.Dragging) bar.Value = v;
                bar.On = on;
                lbl.Text = on ? Sgn(v) : "off";
                lbl.Foreground = hover ? Txt : on ? Txt : NotaPalette.TextDisabled;
            });
            return new DockPanel { Children = { WithDock(lblHit, Dock.Bottom), bar } };
        }

        // A RESULT slider: the label brightens to brass once moved off its default.
        Control SliderLine(string label, int p, Func<double, double> toNorm, Func<double, double> fromNorm, Func<double, string> fmt)
        {
            var l = Caps(label); l.Width = 40;
            var val = Mono("", Txt); val.Width = 34; val.TextAlignment = TextAlignment.Right;
            var track = new SliderTrack { Height = 11, Reset = () => { S(p, Def(p)); Edited(); } };
            track.Changed += n => { S(p, fromNorm(n)); Refresh(); };
            track.GestureBegin += () => Begin(p);
            track.GestureEnd += () => { End(p); Edited(); };
            Learn(track, p, label);
            ValueEntry.Attach(val, () => toNorm(G(p)), n => S(p, fromNorm(n)), () => fmt(G(p)), () => Begin(p), () => End(p), Edited);
            readouts.Add(() =>
            {
                double v = G(p);
                if (!track.Dragging) track.Norm = toNorm(v);
                bool mod = Math.Abs(v - Def(p)) > 0.5;
                l.Foreground = mod ? BrassLit : Cap;
                val.Foreground = mod ? BrassLit : Txt;
                val.Text = fmt(v);
            });
            track.Margin = new Thickness(6, 0, 6, 0);
            return new DockPanel { Children = { WithDock(l, Dock.Left), WithDock(val, Dock.Right), track } };
        }

        // A mini-view knob: 34px gauge, caps label, mono value.
        Control KnobCell(string label, int p, Func<double> norm, Action<double> set, Func<string> text)
        {
            var knob = new Knob(norm(), 1.0) { Accent = true, Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center };
            knob.ValueChanged += n => { set(n); Refresh(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => { End(p); Edited(); };
            Learn(knob, p, label);
            var val = Mono("", Sub, 7); val.HorizontalAlignment = HorizontalAlignment.Center;
            readouts.Add(() => { double n = norm(); if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n; val.Text = text(); });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { knob, new TextBlock { Text = label, FontSize = 7, FontWeight = FontWeight.Bold, LetterSpacing = 0.6, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 9 }, val } };
        }
    }
}
