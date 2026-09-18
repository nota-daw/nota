// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Bass editor (instrument kind 7): a mono-first bass
// synth in the 700 × 260 card the almanac draws for it.
//
//   Wheels 56   pitch bend and mod wheel (the wheel opens the LFO onto the cutoff), on
//               screen on both tabs.
//   Centre      two tabs:
//               Signal — Osc, Sub and Filter as three rows top to bottom: the main
//                        oscillator's wave, shape, pulse width, tuning and level; the sub's
//                        wave, octave and level with the mix sum; the filter's type and
//                        slope, its response as a drag pad, and cutoff, resonance, drive,
//                        env and key amounts.
//               Mod    — the amp and filter envelopes drawn and draggable, and the LFO
//                        with its two destinations and the velocity amounts.
//   Rail 186    Global: voice mode, glide, unison, output drive, bend range, legato,
//               volume, pan and the track's meter.
//
// Brass is the parameter itself; teal is modulation and what receives it. Everything is a
// plugin param → automation / persist / clone, and the card follows automation live.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class BassInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "BASS SYNTH";
    public double CardWidth => 700;

    private const double WheelsW = 56, RailW = 186, TabH = 20, StatusH = 18, BodyH = 186;

    private static readonly string[] TabNames = { "Signal", "Mod" };
    // The main oscillator is a continuous morph sine → tri → saw → pulse; its chips jump to
    // the four corners and light the band the shape sits in. Drawn saw · square · tri · sine.
    private static readonly int[] OscIcons = { 0, 1, 2, 3 };
    private static readonly float[] OscStops = { 2f / 3f, 1f, 1f / 3f, 0f };
    private static readonly string[] ShapeWords = { "sine", "tri", "saw", "pulse" };
    // Sub oscillator: sine · square · triangle, the engine's order.
    private static readonly int[] SubIcons = { 3, 1, 2 };
    private static readonly float[] SubStops = { 0f, 0.5f, 1f };
    private static readonly string[] SubWords = { "sine", "square", "tri" };
    private static readonly string[] FilterNames = { "LP", "HP", "BP", "Notch" };
    private static readonly string[] LfoNames = { "Sin", "Tri", "Saw", "Sqr", "S&H" };
    private static readonly string[] BendNames = { "±2", "±5", "±12" };
    private static readonly float[] BendStops = { 1f / 11f, 4f / 11f, 1f };

    // The engine's own maps (BassSynth.h), so a readout says what it does.
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));
    private static string Hz(float v) => NotaNum.Hz(ExpMap(v, 30, 18000));
    // An LFO runs well below 1 Hz, where NotaNum.Hz would round every slow rate to "0 Hz".
    private static string SlowHz(double hz) => hz >= 9.995 ? NotaNum.Hz(hz)
        : NotaNum.Unit(hz, hz < 0.995 ? "0.00" : "0.0", "Hz");
    private static string Secs(float v, double lo, double hi) => NotaNum.Time(ExpMap(v, lo, hi));
    private static string Pct(float v) => NotaNum.Pct(v);
    private static string Bip(float v) => NotaNum.Unit((v - 0.5f) * 200, "+0;−0;0", "%");
    private static string Oct(float v) => NotaNum.Unit(Math.Round((v - 0.5) * 4), "+0;−0;0", "oct");
    private static string Semi(float v) => NotaNum.Unit(Math.Round((v - 0.5) * 24), "+0;−0;0", "st");
    private static string PulseW(float v) => NotaNum.Pct(0.05 + 0.9 * v);
    private static string LfoPitch(float v) => NotaNum.Unit((v - 0.5) * 24, "+0.0;−0.0;0", "st");
    private static string GlideT(float v) => v <= 0.001f ? "off" : NotaNum.Time(ExpMap(v, 0.005, 0.6));
    private static string Unison(float v) => v <= 0.001f ? "off" : NotaNum.Pct(v);
    private static string PanText(float v)
    {
        double p = (v - 0.5) * 200;
        return Math.Abs(p) < 0.5 ? "C" : (p < 0 ? "L" : "R") + NotaNum.Str(Math.Abs(p), "0");
    }
    private static int ShapeBand(float v) => Math.Clamp((int)Math.Round(v * 3), 0, 3);   // 0 sine … 3 pulse
    private static int BendRange(float v) => (int)Math.Round(1 + v * 11);

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        float G(string id) => I(id) is var i and >= 0 ? engine.PluginParamGet(track, -1, i) : 0f;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        void Begin(string id) { if (I(id) >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void End(string id) { if (I(id) >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);
        (int, string) P(string id) => (I(id), id);
        bool IsMono() => G("mono") >= 0.5f;

        var readouts = new List<Action>();
        int tab = 0;

        var filtGraph = new VoltFilter(engine, track)
        {
            FreqRange = (30.0, 18000.0), VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 100, MinHeight = 50,
        };
        var ampEnv = new VoltEnv(engine, track) { ShowTitle = false, VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 100, MinHeight = 50 };
        var filtEnv = new VoltEnv(engine, track) { ShowTitle = false, VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 100, MinHeight = 50, Accent = NotaPalette.TealBright };
        var hint = new TextBlock
        {
            FontSize = NotaType.Axis, Foreground = TextTertiary, FontFamily = NotaFonts.MonoFamily,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily };
        Action showTab = () => { };

        void Refresh()
        {
            filtGraph.Target("FILTER", P("filfreq"), P("filreso"), Sel("filtype", 4));
            filtGraph.Refresh(); ampEnv.Refresh(); filtEnv.Refresh();
            foreach (var r in readouts) r();
            hint.Text = Hint();
            status.Text = Summary();
            meta.Text = Meta();
        }

        // ---- shared builders --------------------------------------------------
        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = 8) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily,
        };
        static TextBlock Section(string t) => new()
        {
            Text = t, FontSize = NotaType.DeviceSection, FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center,
        };
        static Border Rule(Control child, bool top = true) => new()
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = top ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0),
            Child = child,
        };

        // Every knob is a table cell: a short label on the card while MIDI learn and the CV
        // menu keep the parameter's full name.
        Control PKnob(string id, string label, string learn, Func<float, string> fmt,
            double size = 26, double cellW = 44, IBrush? arc = null)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock
            {
                Text = fmt(G(id)), FontSize = NotaType.KnobValue, FontFamily = NotaFonts.MonoFamily,
                Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, LineHeight = 10,
            };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, ArcColor = arc, Default = engine.InstrumentParamDefault(track, pi),
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center,
            };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), learn);
            var cell = KnobCell(label, knob, value, cellW);
            cell.VerticalAlignment = VerticalAlignment.Center;
            return cell;
        }

        Border Chips(string id, string[] names, Func<int>? current = null, Action<int>? pick = null, bool fill = false, double padX = 6)
        {
            int n = names.Length;
            var seg = Segments(names, current ?? (() => Sel(id, n)),
                pick ?? (iv => { SetP(id, iv / (float)(n - 1)); Refresh(); }), out var sync, fill: fill, padX: padX);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        Grid Slider(string label, string id, Func<string> text, double labelW = 0, double valueW = 40,
            bool mod = false, Func<bool>? dim = null)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                dim: dim, labelWidth: labelW, valueWidth: valueW, modulation: mod);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), label);
            return row;
        }

        // Waveform chips: the recessed strip of Segments with a drawn wave in each cell.
        // `current` lights a cell (−1 for none); a click writes that cell's stop.
        Control WaveChips(string id, int[] icons, float[] stops, Func<int> current)
        {
            int n = icons.Length;
            var cells = new Border[n];
            var marks = new WaveIcon[n];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++)
            {
                int iv = i;
                var ic = new WaveIcon(icons[i]) { Width = 16, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border
                {
                    CornerRadius = NotaRadius.Badge, Padding = new Thickness(2, 2), Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand), Child = ic,
                };
                c.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                    Begin(id); SetP(id, stops[iv]); End(id); Refresh(); e.Handled = true;
                };
                cells[i] = c; marks[i] = ic; row.Children.Add(c);
            }
            void Paint()
            {
                int cur = current();
                for (int i = 0; i < n; i++)
                {
                    bool on = i == cur;
                    cells[i].Background = on ? Brass : Brushes.Transparent;
                    marks[i].Stroke = on ? OnAccent : TextTertiary;
                    marks[i].InvalidateVisual();
                }
            }
            readouts.Add(Paint); Paint();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            var host = new Border
            {
                Background = NotaPalette.BgSunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Control, Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Child = row,
            };
            host.BindResource(Border.BoxShadowProperty, "Shadow.Sunken");
            return host;
        }

        // The filter type as a 2 × 2 block of segments — four words in a 62 px column.
        Control TypeGrid(string id)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 1, RowSpacing = 1 };
            var cells = new Border[4];
            var texts = new TextBlock[4];
            for (int i = 0; i < 4; i++)
            {
                int iv = i;
                var tb = new TextBlock { Text = FilterNames[i], FontSize = NotaType.KnobLabel, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border
                {
                    CornerRadius = NotaRadius.Badge, Padding = new Thickness(0, 1), Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
                };
                c.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                    Begin(id); SetP(id, iv / 3f); End(id); Refresh(); e.Handled = true;
                };
                Grid.SetColumn(c, i % 2); Grid.SetRow(c, i / 2);
                cells[i] = c; texts[i] = tb; g.Children.Add(c);
            }
            void Paint()
            {
                int cur = Sel(id, 4);
                for (int i = 0; i < 4; i++)
                {
                    bool on = i == cur;
                    cells[i].Background = on ? Brass : Brushes.Transparent;
                    texts[i].Foreground = on ? OnAccent : TextTertiary;
                    texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            readouts.Add(Paint); Paint();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(g, MidiTarget.PluginParam(track, -1, pi), "Filter Type");
            var host = new Border
            {
                Background = NotaPalette.BgSunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Control, Padding = new Thickness(1), Child = g,
            };
            host.BindResource(Border.BoxShadowProperty, "Shadow.Sunken");
            return host;
        }

        // ---- the wheels rail --------------------------------------------------
        Control Wheel(string id, double def, bool spring)
        {
            var w = new PerformWheel { Default = def, Spring = spring, VerticalAlignment = VerticalAlignment.Stretch, Norm = G(id), HorizontalAlignment = HorizontalAlignment.Center };
            w.ValueChanged += v => { SetP(id, (float)v); Refresh(); };
            w.GestureBegin += () => Begin(id);
            w.GestureEnd += () => End(id);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(w, MidiTarget.PluginParam(track, -1, pi), id);
            readouts.Add(() => { if (!w.Dragging) w.Norm = G(id); });
            return w;
        }

        var bendWheel = Wheel("bend", 0.5, true);
        var modWheel = Wheel("modwheel", 0, false);
        var bendVal = Mono("", TextSecondary, NotaType.KnobValue);
        var modVal = Mono("", TextSecondary, NotaType.KnobValue);
        bendVal.HorizontalAlignment = modVal.HorizontalAlignment = HorizontalAlignment.Center;
        var pitchCap = Cap("PITCH"); pitchCap.HorizontalAlignment = HorizontalAlignment.Center;
        var modCap = Cap("MOD"); modCap.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(modWheel, "Mod wheel — opens the LFO onto the cutoff");
        readouts.Add(() =>
        {
            int range = BendRange(G("bendrange"));
            double semis = (G("bend") - 0.5) * 2 * range;
            bool centred = Math.Abs(semis) < 0.05;
            bendVal.Text = centred ? $"±{range} st" : NotaNum.Unit(semis, "+0.0;−0.0;0", "st");
            bendVal.Foreground = centred ? TextSecondary : AccentBright;
            bool modOn = G("modwheel") > 1e-3f;
            modVal.Text = NotaNum.Pct(G("modwheel"));
            modVal.Foreground = modOn ? AccentBright : TextSecondary;
            // On the Mod tab the wheel is part of what's on screen — it drives the LFO.
            modCap.Foreground = tab == 1 ? AccentBright : TextTertiary;
        });

        Grid Pair(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            g.Children.Add(a); Grid.SetColumn(b, 1); g.Children.Add(b);
            return g;
        }
        var wheelsCap = Cap("WHEELS"); wheelsCap.HorizontalAlignment = HorizontalAlignment.Center;
        var wheelsGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 4, Margin = new Thickness(4, 5) };
        var wheelRows = new Control[] { wheelsCap, Pair(bendWheel, modWheel), Pair(pitchCap, modCap), Pair(bendVal, modVal) };
        for (int i = 0; i < wheelRows.Length; i++) { Grid.SetRow(wheelRows[i], i); wheelsGrid.Children.Add(wheelRows[i]); }
        var wheelsPanel = SectionBox(wheelsGrid);
        wheelsPanel.Width = WheelsW;

        // ---- Signal tab -------------------------------------------------------
        // OSC: the morph oscillator's corners, then shape, pulse width, tuning and level.
        var oscRow = new Grid { ColumnDefinitions = new ColumnDefinitions("34,Auto,*,*,*,*,*"), ColumnSpacing = 4 };
        {
            var cs = new Control[]
            {
                Section("OSC"),
                WaveChips("oscshape", OscIcons, OscStops, () => ShapeBand(G("oscshape")) switch { 0 => 3, 1 => 2, 2 => 0, _ => 1 }),
                PKnob("oscshape", "SHAPE", "Osc Shape", Pct),
                PKnob("oscpw", "PW", "Pulse Width", PulseW),
                PKnob("oscoctave", "OCTAVE", "Osc Octave", Oct),
                PKnob("oscsemi", "SEMI", "Osc Semi", Semi),
                PKnob("osclevel", "LEVEL", "Osc Level", Pct),
            };
            for (int i = 0; i < cs.Length; i++) { Grid.SetColumn(cs[i], i); oscRow.Children.Add(cs[i]); }
        }

        // SUB: its wave, the octave below, level — and what osc + sub add up to.
        var subRow = new Grid { ColumnDefinitions = new ColumnDefinitions("34,Auto,Auto,*,*"), ColumnSpacing = 6 };
        {
            var octChips = Chips("suboctave", new[] { "−1", "−2" });
            var octCell = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("OCT"), octChips } };

            var mixBar = new MixBar(engine, track, new[] { I("osclevel"), I("sublevel"), -1 }) { Height = 4 };
            readouts.Add(mixBar.Refresh);
            var mixVal = Mono("", TextSecondary, NotaType.KnobValue);
            readouts.Add(() =>
            {
                float sum = G("osclevel") + G("sublevel");
                mixVal.Text = sum <= 1e-3f ? "silent" : NotaNum.Db(AudioMath.LinToDb(Math.Min(1f, sum)));
            });
            var mixCell = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("MIX SUM"), mixBar, mixVal } };

            var cs = new Control[]
            {
                Section("SUB"),
                WaveChips("subwave", SubIcons, SubStops, () => Sel("subwave", 3)),
                octCell,
                PKnob("sublevel", "LEVEL", "Sub Level", Pct),
                mixCell,
            };
            for (int i = 0; i < cs.Length; i++) { Grid.SetColumn(cs[i], i); subRow.Children.Add(cs[i]); }
        }

        // FILTER: type and slope, the response pad, and its five knobs.
        var filtRow = new Grid { ColumnDefinitions = new ColumnDefinitions("62,*,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 3, 0, 0) };
        {
            var envCap = Mono("", Teal, NotaType.Axis);
            readouts.Add(() => envCap.Text = "env " + NotaNum.Str((G("filenv") - 0.5f) * 200, "+0;−0;0"));
            var left = new StackPanel
            {
                Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Cap("FILTER"),
                    TypeGrid("filtype"),
                    Chips("filslope", new[] { "12", "24" }, () => G("filslope") >= 0.5f ? 1 : 0, iv => { SetP("filslope", iv); Refresh(); }, fill: true),
                    envCap,
                },
            };
            var knobs = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    PKnob("filfreq", "CUTOFF", "Filter Freq", Hz, 26, 38),
                    PKnob("filreso", "RESO", "Filter Reso", Pct, 26, 30),
                    PKnob("fildrive", "DRIVE", "Filter Drive", Pct, 26, 32),
                    PKnob("filenv", "ENV", "Filter Env", Bip, 26, 30, Teal),
                    PKnob("filkey", "KEY", "Filter Key", Pct, 26, 30, Teal),
                },
            };
            filtRow.Children.Add(left);
            Grid.SetColumn(filtGraph, 1); filtRow.Children.Add(filtGraph);
            Grid.SetColumn(knobs, 2); filtRow.Children.Add(knobs);
        }

        var signalGrid = new Grid { RowDefinitions = new RowDefinitions("56,54,*") };
        signalGrid.Children.Add(oscRow);
        var subHost = Rule(subRow); Grid.SetRow(subHost, 1); signalGrid.Children.Add(subHost);
        var filtHost = Rule(filtRow); Grid.SetRow(filtHost, 2); signalGrid.Children.Add(filtHost);
        var signalBody = new Border { Padding = new Thickness(7, 0, 7, 4), Child = signalGrid };

        // ---- Mod tab ----------------------------------------------------------
        Control EnvPanel(string title, string route, VoltEnv view, string[] ids)
        {
            view.Target(title, P(ids[0]), P(ids[1]), P(ids[2]), P(ids[3]));
            string[] stage = { "A", "D", "S", "R" };
            Func<float, string>[] fmt =
            {
                v => Secs(v, 0.001, 2.0), v => Secs(v, 0.002, 4.0), Pct, v => Secs(v, 0.002, 5.0),
            };
            var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 3 };
            for (int k = 0; k < 4; k++)
            {
                string id = ids[k];
                var fk = fmt[k];
                var v = Mono(fk(G(id)), TextPrimary, NotaType.KnobValue);
                v.HorizontalAlignment = HorizontalAlignment.Center;
                readouts.Add(() => v.Text = fk(G(id)));
                var cap = Cap(stage[k]); cap.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Border
                {
                    Background = NotaPalette.BgSunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1),
                    CornerRadius = NotaRadius.Badge, Padding = new Thickness(1, 1),
                    Child = new StackPanel { Children = { cap, v } },
                };
                if (I(id) is var pi and >= 0) MidiLearn.Bind(cell, MidiTarget.PluginParam(track, -1, pi), title + " · " + stage[k]);
                Grid.SetColumn(cell, k); cells.Children.Add(cell);
            }
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Height = 11, Children = { Cap(title), Mono(route, Teal, NotaType.Axis) } };
            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 4 };
            g.Children.Add(head);
            Grid.SetRow(view, 1); g.Children.Add(view);
            Grid.SetRow(cells, 2); g.Children.Add(cells);
            return g;
        }
        var ampPanel = EnvPanel("AMP ENV", "→ level", ampEnv, new[] { "attack", "decay", "sustain", "release" });
        var filtEnvPanel = EnvPanel("FILTER ENV", "→ cutoff", filtEnv, new[] { "fattack", "fdecay", "fsustain", "frelease" });

        // LFO: shape, rate and where it lands — cutoff and pitch — then the velocity amounts.
        var lfoDest = Mono("", Teal, NotaType.Axis);
        readouts.Add(() => lfoDest.Text = G("modwheel") > 1e-3f ? "→ cut/pitch · wheel" : "→ cut/pitch");
        var lfoKnobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), VerticalAlignment = VerticalAlignment.Center };
        {
            var ks = new[]
            {
                PKnob("lforate", "RATE", "LFO Rate", v => SlowHz(ExpMap(v, 0.05, 30)), 26, 40),
                PKnob("fillfo", "→ FLT", "Filter LFO", Bip, 26, 40, Teal),
                PKnob("lfopitch", "→ PIT", "LFO → Pitch", LfoPitch, 26, 40, Teal),
            };
            for (int i = 0; i < ks.Length; i++) { Grid.SetColumn(ks[i], i); lfoKnobs.Children.Add(ks[i]); }
        }
        var velRows = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(0, 0, 0, 1),
            Children =
            {
                Slider("VEL→AMP", "velamp", () => Pct(G("velamp")), labelW: 46, valueW: 26, mod: true),
                Slider("VEL→FIL", "velfilter", () => Pct(G("velfilter")), labelW: 46, valueW: 26, mod: true),
            },
        };
        var lfoGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 4 };
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Height = 11, Children = { Cap("LFO"), lfoDest } };
            var rows = new Control[]
            {
                head,
                Chips("lfowave", LfoNames, fill: true, padX: 1),
                lfoKnobs,
                new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = velRows },
            };
            for (int i = 0; i < rows.Length; i++) { Grid.SetRow(rows[i], i); lfoGrid.Children.Add(rows[i]); }
        }
        var lfoHost = Rule(lfoGrid, top: false);
        lfoHost.Padding = new Thickness(6, 0, 0, 0);
        lfoHost.Width = 140;

        var modGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto"), ColumnSpacing = 6 };
        modGrid.Children.Add(ampPanel);
        Grid.SetColumn(filtEnvPanel, 1); modGrid.Children.Add(filtEnvPanel);
        Grid.SetColumn(lfoHost, 2); modGrid.Children.Add(lfoHost);
        var modBody = new Border { Padding = new Thickness(7, 5), Child = modGrid };

        // ---- the tab strip ----------------------------------------------------
        var bodies = new Control[] { signalBody, modBody };
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < TabNames.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = TabNames[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border
            {
                Padding = new Thickness(8, 0), BorderThickness = new Thickness(0, 0, 0, 2),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                tab = iv; showTab(); e.Handled = true;
            };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }
        var tabStrip = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        tabStrip.Children.Add(tabRow);
        Grid.SetColumn(hint, 1); tabStrip.Children.Add(hint);

        showTab = () =>
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == tab;
                tabCells[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                tabCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                tabTexts[i].Foreground = on ? AccentBright : TextTertiary;
                tabTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                bodies[i].IsVisible = on;
            }
            Refresh();
        };

        var tabHost = new Panel { MinHeight = BodyH - TabH };
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · Global ----------------------------------------------------
        var monoChips = Chips("mono", new[] { "Poly", "Mono" }, () => IsMono() ? 1 : 0, iv => { SetP("mono", iv); Refresh(); });
        monoChips.HorizontalAlignment = HorizontalAlignment.Right;
        monoChips.Margin = new Thickness(0, 0, 6, 0);
        var globalCap = Cap("GLOBAL"); globalCap.Margin = new Thickness(8, 0, 0, 0);
        var globalHead = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        globalHead.Children.Add(globalCap);
        Grid.SetColumn(monoChips, 1); globalHead.Children.Add(monoChips);

        var bendChips = Segments(BendNames, () => NearestExact(G("bendrange"), BendStops),
            iv => { SetP("bendrange", BendStops[iv]); Refresh(); }, out var bendSync, padX: 3);
        readouts.Add(bendSync);
        if (I("bendrange") is var bri and >= 0) MidiLearn.Bind(bendChips, MidiTarget.PluginParam(track, -1, bri), "Bend Range");
        var legato = Switch("Legato", () => G("legato") >= 0.5f, () => { Begin("legato"); SetP("legato", G("legato") >= 0.5f ? 0f : 1f); End("legato"); Refresh(); },
            out var legatoSync, dim: () => !IsMono());
        readouts.Add(legatoSync);
        if (I("legato") is var lgi and >= 0) MidiLearn.Bind(legato, MidiTarget.PluginParam(track, -1, lgi), "Legato");
        ToolTip.SetTip(legato, "Slide into a note played over a held one without a new attack");
        legato.HorizontalAlignment = HorizontalAlignment.Right;
        var bendRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 5 };
        bendRow.Children.Add(Cap("BEND"));
        Grid.SetColumn(bendChips, 1); bendRow.Children.Add(bendChips);
        Grid.SetColumn(legato, 2); bendRow.Children.Add(legato);

        var meter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) meter.Push(m); });
        var meterScale = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            RowDefinitions = new RowDefinitions(string.Join(",", ScaleRows(0, -12, -48))),
        };
        for (int i = 0; i < 3; i++)
        {
            var c = Mono(i == 0 ? "0" : i == 1 ? "−12" : "−48", NotaPalette.TextAxis, NotaType.Axis);
            Grid.SetRow(c, i * 2 + 1); meterScale.Children.Add(c);
        }
        var meterBlock = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 9),
        };
        meterBlock.Children.Add(meter);
        Grid.SetColumn(meterScale, 1); meterBlock.Children.Add(meterScale);

        var globalBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        var volCell = PKnob("volume", "VOLUME", "Volume", Pct, NotaSize.KnobMain, 48);
        var panCell = PKnob("outpan", "PAN", "Out Pan", PanText, NotaSize.KnobRegular, 44);
        globalBottom.Children.Add(volCell);
        Grid.SetColumn(panCell, 1); globalBottom.Children.Add(panCell);
        Grid.SetColumn(meterBlock, 2); globalBottom.Children.Add(meterBlock);

        var globalTop = new StackPanel
        {
            Spacing = 5, Margin = new Thickness(8, 6, 8, 0),
            Children =
            {
                // Glide and legato belong to the mono voice — poly starts every note on its own pitch.
                Slider("GLIDE", "glide", () => GlideT(G("glide")), labelW: 38, dim: () => !IsMono()),
                Slider("UNISON", "unison", () => Unison(G("unison")), labelW: 38),
                Slider("DRIVE", "drive", () => Pct(G("drive")), labelW: 38),
                bendRow,
            },
        };
        var globalGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        globalGrid.Children.Add(globalTop);
        var globalBottomFrame = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(8, 5, 8, 5), Padding = new Thickness(0, 4, 0, 0), Child = globalBottom,
        };
        Grid.SetRow(globalBottomFrame, 1); globalGrid.Children.Add(globalBottomFrame);

        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(HeaderStrip(globalHead));
        Grid.SetRow(globalGrid, 1); railGrid.Children.Add(globalGrid);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ---- status strip -----------------------------------------------------
        string Shape() => ShapeWords[ShapeBand(G("oscshape"))];
        string SubOct() => G("suboctave") >= 0.5f ? "−2" : "−1";
        string FilterWord() => $"{FilterNames[Sel("filtype", 4)]} {(G("filslope") >= 0.5f ? "24" : "12")}";
        string AdsrSum(string a, string d, string s, string r)
            => $"{Secs(G(a), 0.001, 2.0)} · {Secs(G(d), 0.002, 4.0)} · {Pct(G(s))} · {Secs(G(r), 0.002, 5.0)}";

        // The line at the right of the tab strip: what this tab holds, in three words.
        string Hint() => tab switch
        {
            0 => $"{Shape()} · {SubWords[Sel("subwave", 3)]} {SubOct()} · {FilterWord()}",
            _ => $"amp · flt · lfo {LfoNames[Sel("lfowave", 5)].ToLowerInvariant()}",
        };
        string Summary() => tab switch
        {
            0 => $"Osc {Shape()} {Pct(G("osclevel"))} · sub {SubWords[Sel("subwave", 3)]} {SubOct()} oct {Pct(G("sublevel"))} → "
               + $"{FilterWord()} dB/oct {Hz(G("filfreq"))} · env {Bip(G("filenv"))} · drive {Pct(G("fildrive"))}",
            _ => $"Amp env {AdsrSum("attack", "decay", "sustain", "release")} → level · "
               + $"filter env {AdsrSum("fattack", "fdecay", "fsustain", "frelease")} → cutoff · "
               + $"LFO {LfoNames[Sel("lfowave", 5)].ToLowerInvariant()} {SlowHz(ExpMap(G("lforate"), 0.05, 30))} → flt {Bip(G("fillfo"))} · pit {LfoPitch(G("lfopitch"))}",
        };
        string Meta()
        {
            string mode = IsMono() ? (G("legato") >= 0.5f ? "MONO LEGATO" : "MONO") : "POLY 16";
            return $"{mode} · ±{BendRange(G("bendrange"))} ST · VOL {Pct(G("volume"))}";
        }
        var statusBar = new Grid { Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusBar.Children.Add(status);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = statusBar,
        };

        // ---- assembly ---------------------------------------------------------
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = NotaSpace.DeviceGap,
            Margin = new Thickness(NotaSpace.DeviceGap),
        };
        body.Children.Add(wheelsPanel);
        Grid.SetColumn(tabPanel, 1); body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 2); body.Children.Add(rail);

        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel
        {
            LastChildFill = true, Background = NotaPalette.Gutter,
            Children = { statusHost, body },
        };

        ctx.SetInstLiveViz(Refresh);
        showTab();
        return root;
    }

    // A device section: card ground, hairline, radius 6 — the almanac's section box.
    private static Border SectionBox(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = child,
    };

    // A section's own header strip: a hairline under it, nothing else.
    private static Border HeaderStrip(Control child) => new()
    {
        BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = child,
    };

    /// <summary>Row definitions that put a caption for each dB mark at its place on the
    /// meter's scale: a star gap, the caption (Auto), … and a star gap for the remainder.</summary>
    private static IEnumerable<string> ScaleRows(params double[] dbs)
    {
        double prev = 0;
        foreach (var db in dbs)
        {
            double fromTop = 1.0 - MeterScale.NormDb(db);
            yield return Math.Max(0, fromTop - prev).ToString("0.000", NotaNum.Culture) + "*";
            yield return "Auto";
            prev = fromTop;
        }
        yield return Math.Max(0, 1.0 - prev).ToString("0.000", NotaNum.Culture) + "*";
    }
}
