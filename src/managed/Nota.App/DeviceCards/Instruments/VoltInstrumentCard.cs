// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Volt editor (instrument kind 6): a two-path subtractive
// synth in the 700 × 260 card the almanac draws for it.
//
//   Wheels 56   pitch bend and mod wheel, on screen on every tab — the two hand controls
//               a player reaches for first.
//   Centre      six tabs:
//               Osc    — Osc 1 / Osc 2 / Noise as a table: wave, octave, semi, fine,
//                        start phase, level, and which filter the source is sent to.
//               Filter — the response of the selected filter as a drag pad, with type,
//                        slope, cutoff, resonance, env / key / LFO amounts and, on
//                        Filter 1, how much of it spills into Filter 2.
//               Env    — the amp and filter envelopes side by side, drawn and draggable.
//               LFO    — LFO 1 / LFO 2 / Vibrato as a table, each naming where it lands.
//               Mod    — the 7 × 6 matrix: drag a cell up for +, down for −.
//               Macro  — eight macros, each a value, an amount and one of twelve targets.
//   Rail 186    Global (voice mode, bend range, cutoff, resonance, glide, gain, pan and
//               the track's meter) or Voice (both output amps, unison and the two
//               velocity amounts).
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

internal sealed class VoltInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "SUBTRACTIVE";
    public double CardWidth => 700;

    private const double WheelsW = 56, RailW = 186, TabH = 20, StatusH = 18, BodyH = 186;

    private static readonly string[] TabNames = { "Osc", "Filter", "Env", "LFO", "Mod", "Macro" };
    private static readonly string[] RailTabs = { "Global", "Voice" };
    private static readonly string[] WaveNames = { "saw", "square", "triangle", "sine" };
    private static readonly string[] FilterNames = { "LP", "HP", "BP", "Notch" };
    private static readonly string[] NoiseNames = { "dark", "pink", "white" };
    private static readonly float[] NoiseStops = { 0f, 0.5f, 1f };
    private static readonly float[] RouteStops = { 0f, 1f };
    private static readonly string[] ShapeNames = { "sin", "tri", "sqr", "S&H" };
    private static readonly string[] SyncNames = { "free", "1 bar", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };
    private static readonly string[] BendNames = { "±2", "±5", "±12" };
    private static readonly float[] BendStops = { 1f / 11f, 4f / 11f, 1f };
    private static readonly string[] SrcNames = { "Amp Env", "Flt Env", "LFO 1", "LFO 2", "Velocity", "Key", "Mod Whl" };
    private static readonly string[] DestNames = { "Pitch", "Osc 2", "Cutoff", "Reso", "Level", "Pan" };

    // The twelve macro targets, in the engine's order (VoltSynth::kMacroDests). The six
    // matrix destinations sit at 0, 2, 4, 7, 9 and 11 so an old patch still points home.
    private static readonly string[] MacroDests =
    {
        "Pitch", "Osc 1 level", "Osc 2 pitch", "Osc 2 level", "Cutoff", "Noise level",
        "Filter 2", "Reso", "LFO 1 rate", "Level", "LFO 2 rate", "Pan",
    };
    // What a macro on that target is called on the patch — the word a player reads, not
    // the parameter's name.
    private static readonly string[] MacroWords =
    {
        "PITCH", "BODY", "INTERVAL", "DRIVE", "BRIGHT", "AIR",
        "TONE 2", "EDGE", "MOTION", "SWELL", "DRIFT", "WIDTH",
    };

    // The engine's own perceptual maps (VoltSynth.h), so a readout says what it does.
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));
    private static string Hz(float v) => NotaNum.Hz(ExpMap(v, 20, 18000));
    // An LFO runs well below 1 Hz, where NotaNum.Hz would round every slow rate to "0 Hz".
    private static string SlowHz(double hz) => hz >= 9.995 ? NotaNum.Hz(hz)
        : NotaNum.Unit(hz, hz < 0.995 ? "0.00" : "0.0", "Hz");
    private static string Secs(float v, double lo, double hi) => NotaNum.Time(ExpMap(v, lo, hi));
    private static string Pct(float v) => NotaNum.Pct(v);
    private static string Bip(float v) => NotaNum.Unit((v - 0.5f) * 200, "+0;−0;0", "%");
    private static string Oct(float v) => NotaNum.Str(Math.Round((v - 0.5) * 6), "+0;−0;0");
    private static string Semi(float v) => NotaNum.Str(Math.Round((v - 0.5) * 24), "+0;−0;0");
    private static string Cents(float v) => NotaNum.Unit(Math.Round((v - 0.5) * 100), "+0;−0;0", "c");
    private static string Phase(float v) => v <= 0.001f ? "free" : NotaNum.Unit(v * 360, "0", "°");
    private static string GlideT(float v) => v <= 0.001f ? "off" : NotaNum.Time(ExpMap(v, 0.005, 0.6));
    private static string FadeT(float v) => v <= 0.001f ? "off" : NotaNum.Time(ExpMap(v, 0.01, 5.0));
    private static string Reso(float v) => NotaNum.Str(v, "0.00");
    private static string VibDepth(float v) => NotaNum.Unit(v * 50, "0", "c");
    private static string PanText(float v)
    {
        double p = (v - 0.5) * 200;
        return Math.Abs(p) < 0.5 ? "C" : (p < 0 ? "L" : "R") + NotaNum.Str(Math.Abs(p), "0");
    }
    // Noise colour is continuous; the three chips name the band it falls in rather than
    // going dark whenever the knob sits between them.
    private static int NoiseBand(float v) => v < 0.25f ? 0 : v < 0.75f ? 1 : 2;
    private static int SyncDiv(float v) => Math.Clamp((int)Math.Round(v * 7), 0, 7);
    private static string SyncText(float v) => SyncNames[SyncDiv(v)];
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

        var readouts = new List<Action>();
        int tab = 0, railTab = 0, fsel = 0;   // Osc · Global · Filter 1

        var filtGraph = new VoltFilter(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 60 };
        var ampEnv = new VoltEnv(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 50 };
        var filtEnv = new VoltEnv(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 50 };
        var hint = new TextBlock
        {
            FontSize = NotaType.Axis, Foreground = TextTertiary, FontFamily = NotaFonts.MonoFamily,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily };
        Action applyFilter = () => { };
        Action showTab = () => { };

        void Refresh()
        {
            applyFilter();
            ampEnv.Refresh(); filtEnv.Refresh();
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
        static TextBlock Section(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.DeviceSection, FontWeight = FontWeight.SemiBold,
            Foreground = c ?? TextPrimary, VerticalAlignment = VerticalAlignment.Center,
        };

        // Every knob on this card is a table cell: the cell carries a short label (or, in a
        // macro tile, none at all, because the tile is already named) while MIDI learn and
        // the CV menu keep the parameter's full name.
        Control PKnob(string id, string label, string learn, Func<float, string> fmt,
            double size = 28, double cellW = 44, IBrush? arc = null)
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
            return label.Length == 0
                ? new StackPanel { Width = cellW, Spacing = 0, Children = { knob, value } }
                : KnobCell(label, knob, value, cellW);
        }

        Control Chips(string id, string[] names, Func<int>? current = null, Action<int>? pick = null, bool fill = false)
        {
            int n = names.Length;
            var seg = Segments(names, current ?? (() => Sel(id, n)),
                pick ?? (iv => { SetP(id, iv / (float)(n - 1)); Refresh(); }), out var sync, fill: fill);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        // A chip strip that lights only on exact stops, so a value set between them (by
        // automation, the matrix or a macro) reads as "somewhere in between" rather than
        // snapping the display to a lie.
        Control StopChips(string id, string[] names, float[] stops, bool fill = false)
            => Chips(id, names, () => NearestExact(G(id), stops), iv => { SetP(id, stops[iv]); Refresh(); }, fill);

        Grid Slider(string label, string id, Func<string> text, double labelW = 0, double valueW = 40)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                labelWidth: labelW, valueWidth: valueW);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), label);
            return row;
        }

        Control Toggle(string label, string id, Func<string>? live = null)
        {
            var host = Switch(label, () => G(id) >= 0.5f, () => { SetP(id, G(id) >= 0.5f ? 0f : 1f); Refresh(); }, out var sync, liveLabel: live);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(host, MidiTarget.PluginParam(track, -1, pi), label);
            return host;
        }

        // Waveform chips: the same recessed strip as Segments, with the glyph instead of a
        // word — four waves read faster drawn than spelled.
        Control WaveChips(string id)
        {
            var cells = new Border[4];
            var icons = new WaveIcon[4];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < 4; i++)
            {
                int iv = i;
                var ic = new WaveIcon(i) { Width = 18, Height = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border
                {
                    CornerRadius = NotaRadius.Badge, Padding = new Thickness(3, 2), Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand), Child = ic,
                };
                c.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                    SetP(id, iv / 3f); Refresh(); e.Handled = true;
                };
                cells[i] = c; icons[i] = ic; row.Children.Add(c);
            }
            void Paint()
            {
                int cur = Sel(id, 4);
                for (int i = 0; i < 4; i++)
                {
                    bool on = i == cur;
                    cells[i].Background = on ? Brass : Brushes.Transparent;
                    icons[i].Stroke = on ? OnAccent : TextTertiary;
                    icons[i].InvalidateVisual();
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
        readouts.Add(() =>
        {
            int range = BendRange(G("bendrange"));
            double semis = (G("bend") - 0.5) * 2 * range;
            bool centred = Math.Abs(semis) < 0.05;
            bendVal.Text = centred ? $"±{range}\u2009st" : NotaNum.Unit(semis, "+0.0;−0.0;0", "st");
            bendVal.Foreground = centred ? TextSecondary : AccentBright;
            modVal.Text = NotaNum.Pct(G("modwheel"));
            modVal.Foreground = G("modwheel") > 1e-3f ? AccentBright : TextSecondary;
        });

        Grid Pair(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            g.Children.Add(a); Grid.SetColumn(b, 1); g.Children.Add(b);
            return g;
        }
        var pitchCap = Cap("PITCH"); pitchCap.HorizontalAlignment = HorizontalAlignment.Center;
        var modCap = Cap("MOD"); modCap.HorizontalAlignment = HorizontalAlignment.Center;
        var wheelsCap = Cap("WHEELS"); wheelsCap.HorizontalAlignment = HorizontalAlignment.Center;
        var wheelsGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 4, Margin = new Thickness(4, 5) };
        var wheelRows = new Control[] { wheelsCap, Pair(bendWheel, modWheel), Pair(pitchCap, modCap), Pair(bendVal, modVal) };
        for (int i = 0; i < wheelRows.Length; i++) { Grid.SetRow(wheelRows[i], i); wheelsGrid.Children.Add(wheelRows[i]); }
        var wheelsPanel = SectionBox(wheelsGrid);
        wheelsPanel.Width = WheelsW;

        // ---- Osc tab ----------------------------------------------------------
        // One grid for all three sources, so the column headers sit over their own cells.
        var oscGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("46,Auto,*,*,*,*,*,Auto"), ColumnSpacing = 4,
            RowDefinitions = new RowDefinitions("*,*,*"),
        };
        void OscCell(Control c, int row, int col, int span = 1)
        { Grid.SetRow(c, row); Grid.SetColumn(c, col); if (span > 1) Grid.SetColumnSpan(c, span); oscGrid.Children.Add(c); }

        // "ROUTE" over the F1 / F2 chips, right-aligned like the almanac's route cell.
        Control RouteCell(string id)
        {
            var cap = Cap("ROUTE"); cap.HorizontalAlignment = HorizontalAlignment.Right;
            var chips = StopChips(id, new[] { "F1", "F2" }, RouteStops);
            chips.HorizontalAlignment = HorizontalAlignment.Right;
            return new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { cap, chips } };
        }

        for (int o = 0; o < 2; o++)
        {
            string pre = o == 0 ? "osc1" : "osc2";
            string nm = o == 0 ? "OSC 1" : "OSC 2";
            if (o > 0)
            {
                var rule = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0) };
                Grid.SetRow(rule, o); Grid.SetColumnSpan(rule, 8); oscGrid.Children.Add(rule);
            }
            OscCell(Section(nm), o, 0);
            OscCell(WaveChips(pre + "wave"), o, 1);
            OscCell(PKnob(pre + "octave", "OCT", nm + " Octave", Oct, 26, 40), o, 2);
            OscCell(PKnob(pre + "semi", "SEMI", nm + " Semi", Semi, 26, 40), o, 3);
            OscCell(PKnob(pre + "detune", "FINE", nm + " Fine", Cents, 26, 40, Teal), o, 4);
            OscCell(PKnob(pre + "phase", "PHASE", nm + " Phase", Phase, 26, 40), o, 5);
            OscCell(PKnob(pre + "level", "LEVEL", nm + " Level", Pct, 26, 40), o, 6);
            OscCell(RouteCell(pre + "route"), o, 7);
        }
        {
            var rule = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetRow(rule, 2); Grid.SetColumnSpan(rule, 8); oscGrid.Children.Add(rule);
            OscCell(Section("NOISE"), 2, 0);
            OscCell(Chips("noisecolor", NoiseNames, () => NoiseBand(G("noisecolor")), iv => { SetP("noisecolor", NoiseStops[iv]); Refresh(); }), 2, 1);
            OscCell(PKnob("noisecolor", "COLOR", "Noise Color", Pct, 26, 40), 2, 2);
            OscCell(PKnob("noise", "LEVEL", "Noise Level", Pct, 26, 40), 2, 6);

            var mixBar = new MixBar(engine, track, new[] { I("osc1level"), I("osc2level"), I("noise") });
            readouts.Add(mixBar.Refresh);
            var mixVal = Mono("", TextSecondary, NotaType.KnobValue);
            readouts.Add(() =>
            {
                float sum = G("osc1level") + G("osc2level") + G("noise");
                mixVal.Text = sum <= 1e-3f ? "silent" : NotaNum.Db(AudioMath.LinToDb(Math.Min(1f, sum)));
            });
            var mixCell = new StackPanel
            {
                Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0),
                Children = { Cap("MIX SUM"), mixBar, mixVal },
            };
            OscCell(mixCell, 2, 3, 3);
            OscCell(RouteCell("noiseroute"), 2, 7);
        }
        var oscBody = new Border { Padding = new Thickness(7, 3), Child = oscGrid };

        // ---- Filter tab -------------------------------------------------------
        // Both filters share one column of controls; picking 1 or 2 swaps which set of
        // knobs is visible — a knob is bound to one param index for its life.
        var filtCells = new Panel[6];
        var filtStack = new Control[2, 6];
        string[] filtIds = { "freq", "reso", "env", "key", "lfo", "tof2" };
        string[] filtLbl = { "Cutoff", "Reso", "Env", "Key", "LFO", "To F2" };
        var filtKnobGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("*,*"),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        for (int k = 0; k < 6; k++)
        {
            filtCells[k] = new Panel();
            Grid.SetColumn(filtCells[k], k % 3); Grid.SetRow(filtCells[k], k / 3);
            filtKnobGrid.Children.Add(filtCells[k]);
        }
        for (int f = 0; f < 2; f++)
        {
            string pre = f == 0 ? "fil1" : "fil2";
            for (int k = 0; k < 6; k++)
            {
                string id = pre + filtIds[k];
                Func<float, string> fmt = k switch
                {
                    0 => Hz, 1 => Reso, 2 => Bip, 3 => Pct, 4 => Bip, _ => Pct,
                };
                // Filter 2 has no "To F2" — it is the end of that path.
                Control cell = k == 5 && f == 1
                    ? new Panel()
                    : PKnob(id, filtLbl[k].ToUpperInvariant(), $"Filter {f + 1} {filtLbl[k]}", fmt, 26, 44, k >= 2 ? Teal : null);
                cell.IsVisible = false;
                filtStack[f, k] = cell;
                filtCells[k].Children.Add(cell);
            }
        }
        var filtTypeHosts = new Panel[2];
        var filtSlopeHosts = new Panel[2];
        var filtTypeChips = new Control[2];
        var filtSlopeChips = new Control[2];
        for (int f = 0; f < 2; f++)
        {
            string pre = f == 0 ? "fil1" : "fil2";
            filtTypeChips[f] = Chips(pre + "type", FilterNames);
            filtSlopeChips[f] = Chips(pre + "slope", new[] { "12\u2009dB", "24\u2009dB" }, () => G(pre + "slope") >= 0.5f ? 1 : 0,
                iv => { SetP(pre + "slope", iv); Refresh(); }, fill: true);
            filtTypeHosts[f] = new Panel { Children = { filtTypeChips[f] } };
            filtSlopeHosts[f] = new Panel { Children = { filtSlopeChips[f] } };
        }
        var filtPick = Segments(new[] { "1", "2" }, () => fsel, iv => { fsel = iv; Refresh(); }, out var filtPickSync);
        readouts.Add(filtPickSync);
        filtPick.HorizontalAlignment = HorizontalAlignment.Right;
        var filtHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        filtHead.Children.Add(Cap("FILTER"));
        Grid.SetColumn(filtPick, 1); filtHead.Children.Add(filtPick);

        var filtTypeStack = new Panel();
        foreach (var h in filtTypeHosts) filtTypeStack.Children.Add(h);
        var filtSlopeStack = new Panel();
        foreach (var h in filtSlopeHosts) filtSlopeStack.Children.Add(h);

        var filtRight = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 4, Width = 136 };
        var filtRightRows = new Control[] { filtHead, filtTypeStack, filtSlopeStack, filtKnobGrid };
        for (int i = 0; i < filtRightRows.Length; i++) { Grid.SetRow(filtRightRows[i], i); filtRight.Children.Add(filtRightRows[i]); }

        applyFilter = () =>
        {
            string pre = fsel == 0 ? "fil1" : "fil2";
            filtGraph.Target($"FILTER {fsel + 1}", P(pre + "freq"), P(pre + "reso"), Sel(pre + "type", 4));
            filtGraph.Refresh();
            for (int f = 0; f < 2; f++)
            {
                filtTypeHosts[f].IsVisible = filtSlopeHosts[f].IsVisible = f == fsel;
                for (int k = 0; k < 6; k++) filtStack[f, k].IsVisible = f == fsel;
            }
        };

        var filtGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 7 };
        filtGrid.Children.Add(filtGraph);
        Grid.SetColumn(filtRight, 1); filtGrid.Children.Add(filtRight);
        var filtBody = new Border { Padding = new Thickness(7, 5), Child = filtGrid };

        // ---- Env tab ----------------------------------------------------------
        Control EnvPanel(string title, string route, VoltEnv view, string[] ids, Func<string> summary)
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
                var v = Mono(fk(G(id)), TextPrimary, NotaType.RowLabel);
                v.HorizontalAlignment = HorizontalAlignment.Center;
                readouts.Add(() => v.Text = fk(G(id)));
                var cap = Cap(stage[k]); cap.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Border
                {
                    Background = NotaPalette.SurfaceRaised, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                    CornerRadius = NotaRadius.Badge, Padding = new Thickness(2, 1),
                    Child = new StackPanel { Children = { cap, v } },
                };
                if (I(id) is var pi and >= 0) MidiLearn.Bind(cell, MidiTarget.PluginParam(track, -1, pi), title + " · " + stage[k]);
                Grid.SetColumn(cell, k); cells.Children.Add(cell);
            }
            var sum = Mono("", TextSecondary, NotaType.Axis);
            sum.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => sum.Text = summary());
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 5, Height = 11 };
            head.Children.Add(Cap(title));
            var dest = Mono(route, Teal, NotaType.Axis); Grid.SetColumn(dest, 1); head.Children.Add(dest);
            Grid.SetColumn(sum, 2); head.Children.Add(sum);

            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 4 };
            g.Children.Add(head);
            Grid.SetRow(view, 1); g.Children.Add(view);
            Grid.SetRow(cells, 2); g.Children.Add(cells);
            return g;
        }
        string AdsrSum(string a, string d, string s, string r)
            => $"{Secs(G(a), 0.001, 2.0)} · {Secs(G(d), 0.002, 4.0)} · {Pct(G(s))} · {Secs(G(r), 0.002, 5.0)}";
        var ampPanel = EnvPanel("AMP ENV", "→ Level", ampEnv, new[] { "attack", "decay", "sustain", "release" },
            () => AdsrSum("attack", "decay", "sustain", "release"));
        var filtEnvPanel = EnvPanel("FILTER ENV", "→ Cutoff", filtEnv, new[] { "fattack", "fdecay", "fsustain", "frelease" },
            () => AdsrSum("fattack", "fdecay", "fsustain", "frelease"));
        var filtEnvHost = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(6, 0, 0, 0), Child = filtEnvPanel,
        };
        var envGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        envGrid.Children.Add(ampPanel);
        Grid.SetColumn(filtEnvHost, 1); envGrid.Children.Add(filtEnvHost);
        var envBody = new Border { Padding = new Thickness(7, 5), Child = envGrid };

        // ---- LFO tab ----------------------------------------------------------
        // Where an LFO actually lands: its strongest matrix route, or the filter cutoff it
        // is wired to by the fixed routing. A modulator that goes nowhere says so.
        string LfoDest(int src)
        {
            int best = -1; double mag = 0.02;
            for (int d = 0; d < 6; d++)
            {
                double a = Math.Abs(G($"mtx{src}_{d}") - 0.5) * 2;
                if (a > mag) { mag = a; best = d; }
            }
            string fixedId = src == 2 ? "fil1lfo" : "fil2lfo";
            double fixedAmt = Math.Abs(G(fixedId) - 0.5) * 2;
            if (fixedAmt > mag) return $"→ Filter {(src == 2 ? 1 : 2)}";
            return best < 0 ? "unrouted" : "→ " + DestNames[best];
        }

        var lfoGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("52,Auto,*,*,*,*,Auto"), ColumnSpacing = 5,
            RowDefinitions = new RowDefinitions("*,*,*"),
        };
        void LfoCell(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); lfoGrid.Children.Add(c); }
        for (int l = 0; l < 2; l++)
        {
            string pre = l == 0 ? "lfo1" : "lfo2";
            string nm = l == 0 ? "LFO 1" : "LFO 2";
            int src = l + 2;
            if (l > 0)
            {
                var rule = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0) };
                Grid.SetRow(rule, l); Grid.SetColumnSpan(rule, 7); lfoGrid.Children.Add(rule);
            }
            LfoCell(Section(nm), l, 0);
            LfoCell(Chips(pre + "shape", ShapeNames), l, 1);
            string rateId = pre + "rate", syncId = pre + "sync";
            LfoCell(PKnob(rateId, "RATE", nm + " Rate", v => SyncDiv(G(syncId)) > 0 ? SyncText(G(syncId)) : SlowHz(ExpMap(v, 0.05, 20)), 28, 46), l, 2);
            LfoCell(PKnob(pre + "depth", "DEPTH", nm + " Depth", Pct, 28, 46, Teal), l, 3);
            LfoCell(PKnob(syncId, "SYNC", nm + " Sync", SyncText, 28, 46), l, 4);
            LfoCell(PKnob(pre + "fade", "FADE", nm + " Fade", FadeT, 28, 46, Teal), l, 5);
            var dest = Mono("", Teal, NotaType.Axis);
            dest.HorizontalAlignment = HorizontalAlignment.Right; dest.Width = 62; dest.TextAlignment = TextAlignment.Right;
            readouts.Add(() => dest.Text = LfoDest(src));
            LfoCell(dest, l, 6);
        }
        {
            var rule = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetRow(rule, 2); Grid.SetColumnSpan(rule, 7); lfoGrid.Children.Add(rule);
            LfoCell(Section("VIBRATO"), 2, 0);
            LfoCell(Toggle("By wheel", "vibwheel"), 2, 1);
            LfoCell(PKnob("vibrate", "RATE", "Vibrato Rate", v => SlowHz(ExpMap(v, 0.1, 12)), 28, 46), 2, 2);
            LfoCell(PKnob("vibamt", "DEPTH", "Vibrato Depth", VibDepth, 28, 46, Teal), 2, 3);
            var note = new TextBlock
            {
                Text = "vibrato always rides the pitch", FontSize = NotaType.Axis, Foreground = TextDisabled,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(note, 2); Grid.SetColumn(note, 4); Grid.SetColumnSpan(note, 2); lfoGrid.Children.Add(note);
            var dest = Mono("→ Pitch", Teal, NotaType.Axis);
            dest.HorizontalAlignment = HorizontalAlignment.Right; dest.Width = 62; dest.TextAlignment = TextAlignment.Right;
            LfoCell(dest, 2, 6);
        }
        var lfoBody = new Border { Padding = new Thickness(7, 3), Child = lfoGrid };

        // ---- Mod tab ----------------------------------------------------------
        var mtxIdx = new int[7, 6];
        for (int s = 0; s < 7; s++) for (int d = 0; d < 6; d++) mtxIdx[s, d] = I($"mtx{s}_{d}");
        var matrix = new VoltMatrix(engine, track, mtxIdx, SrcNames, DestNames) { VerticalAlignment = VerticalAlignment.Stretch };
        matrix.Changed += Refresh;
        readouts.Add(matrix.Refresh);
        int RouteCount()
        {
            int n = 0;
            for (int s = 0; s < 7; s++) for (int d = 0; d < 6; d++) if (Math.Abs(G($"mtx{s}_{d}") - 0.5) > 0.01) n++;
            return n;
        }
        var modHint = new TextBlock
        {
            Text = "drag a cell — up adds, down subtracts · double-click clears", FontSize = NotaType.Axis,
            Foreground = TextDisabled, VerticalAlignment = VerticalAlignment.Center,
        };
        var modGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 3 };
        modGrid.Children.Add(matrix);
        Grid.SetRow(modHint, 1); modGrid.Children.Add(modHint);
        var modBody = new Border { Padding = new Thickness(7, 4), Child = modGrid };

        // ---- Macro tab --------------------------------------------------------
        var macroGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("*,*"),
            ColumnSpacing = 5, RowSpacing = 5,
        };
        int MacroDest(int m) => Math.Clamp((int)Math.Round(G($"mac{m}dest") * (MacroDests.Length - 1)), 0, MacroDests.Length - 1);
        bool MacroOn(int m) => Math.Abs(G($"mac{m}amt") - 0.5f) > 0.01f;
        for (int m = 0; m < 8; m++)
        {
            int mv = m;
            string destId = $"mac{mv}dest";
            var name = new TextBlock
            {
                FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.KnobLabelTracking,
                Foreground = NotaPalette.TextStrong, HorizontalAlignment = HorizontalAlignment.Center,
            };
            var dest = new TextBlock
            {
                FontSize = NotaType.Axis, Foreground = Teal, HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(dest, "Click for the next target, right-click for the previous");
            void Step(int dir)
            {
                int n = MacroDests.Length;
                Begin(destId);
                SetP(destId, ((MacroDest(mv) + dir % n + n) % n) / (float)(n - 1));
                End(destId);
                Refresh();
            }
            dest.PointerPressed += (_, e) =>
            {
                var pt = e.GetCurrentPoint(dest).Properties;
                if (pt.IsLeftButtonPressed) { Step(+1); e.Handled = true; }
                else if (pt.IsRightButtonPressed) { Step(-1); e.Handled = true; }
            };
            if (I(destId) is var dpi and >= 0) MidiLearn.Bind(dest, MidiTarget.PluginParam(track, -1, dpi), $"Macro {mv + 1} Dest");

            var cellBorder = new Border
            {
                Background = NotaPalette.SurfaceRaised, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Control, Padding = new Thickness(2, 2),
                Child = new StackPanel
                {
                    Spacing = 1, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                            {
                                PKnob($"mac{mv}val", "", $"Macro {mv + 1}", Pct, 30, 38),
                                PKnob($"mac{mv}amt", "", $"Macro {mv + 1} Amount", Bip, 26, 38, Teal),
                            },
                        },
                        name, dest,
                    },
                },
            };
            readouts.Add(() =>
            {
                bool on = MacroOn(mv);
                int d = MacroDest(mv);
                name.Text = on ? $"MACRO {mv + 1} · {MacroWords[d]}" : $"MACRO {mv + 1}";
                name.Foreground = on ? NotaPalette.TextStrong : TextDisabled;
                dest.Text = on ? "→ " + MacroDests[d] : "unassigned";
                dest.Foreground = on ? Teal : TextDisabled;
                cellBorder.Background = on ? NotaPalette.SurfaceRaised : NotaPalette.BgSunken;
                cellBorder.BorderBrush = on ? BorderDef : NotaPalette.GraphBorder;
            });
            Grid.SetColumn(cellBorder, mv % 4); Grid.SetRow(cellBorder, mv / 4);
            macroGrid.Children.Add(cellBorder);
        }
        var macroBody = new Border { Padding = new Thickness(7, 5), Child = macroGrid };

        // ---- the tab strip ----------------------------------------------------
        var bodies = new Control[] { oscBody, filtBody, envBody, lfoBody, modBody, macroBody };
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var clearAll = TextButton("Clear all");
        clearAll.Margin = new Thickness(0, 0, 6, 0);
        ToolTip.SetTip(clearAll, "Zero every route in the matrix");
        clearAll.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(clearAll).Properties.IsLeftButtonPressed) return;
            matrix.ClearAll(); Refresh(); e.Handled = true;
        };

        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < TabNames.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = TabNames[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border
            {
                Padding = new Thickness(7, 0), BorderThickness = new Thickness(0, 0, 0, 2),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                tab = iv; showTab(); e.Handled = true;
            };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }
        var tabStrip = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        tabStrip.Children.Add(tabRow);
        Grid.SetColumn(hint, 1); tabStrip.Children.Add(hint);
        Grid.SetColumn(clearAll, 2); tabStrip.Children.Add(clearAll);

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
            clearAll.IsVisible = tab == 4;
            Refresh();
        };

        var tabHost = new Panel { MinHeight = BodyH - TabH };
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · Global ----------------------------------------------------
        var monoChips = Chips("mono", new[] { "Poly", "Mono" }, () => G("mono") >= 0.5f ? 1 : 0, iv => { SetP("mono", iv); Refresh(); });
        monoChips.HorizontalAlignment = HorizontalAlignment.Right;
        var monoRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        monoRow.Children.Add(Cap("VOICES"));
        Grid.SetColumn(monoChips, 1); monoRow.Children.Add(monoChips);

        var bendChips = Segments(BendNames, () => NearestExact(G("bendrange"), BendStops),
            iv => { SetP("bendrange", BendStops[iv]); Refresh(); }, out var bendSync);
        readouts.Add(bendSync);
        if (I("bendrange") is var bri and >= 0) MidiLearn.Bind(bendChips, MidiTarget.PluginParam(track, -1, bri), "Bend Range");
        bendChips.HorizontalAlignment = HorizontalAlignment.Right;
        var bendRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        bendRow.Children.Add(Cap("BEND"));
        Grid.SetColumn(bendChips, 1); bendRow.Children.Add(bendChips);

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
        var gainCell = PKnob("volume", "GAIN", "Gain", Pct, NotaSize.KnobMain, 48);
        var panCell = PKnob("outpan", "PAN", "Out Pan", PanText, NotaSize.KnobRegular, 44);
        globalBottom.Children.Add(gainCell);
        Grid.SetColumn(panCell, 1); globalBottom.Children.Add(panCell);
        Grid.SetColumn(meterBlock, 2); globalBottom.Children.Add(meterBlock);

        var globalTop = new StackPanel
        {
            Spacing = 5, Margin = new Thickness(8, 6, 8, 0),
            Children =
            {
                monoRow,
                bendRow,
                Slider("CUTOFF", "fil1freq", () => Hz(G("fil1freq")), labelW: 40),
                Slider("RESO", "fil1reso", () => Reso(G("fil1reso")), labelW: 40),
                Slider("GLIDE", "glide", () => GlideT(G("glide")), labelW: 40),
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

        // ---- rail · Voice -----------------------------------------------------
        Control AmpRow(string title, string levelId, string panId, string name)
        {
            var knobs = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                Children = { PKnob(levelId, "LEVEL", name + " Level", Pct, 30, 44), PKnob(panId, "PAN", name + " Pan", PanText, 30, 44) },
            };
            knobs.HorizontalAlignment = HorizontalAlignment.Right;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(Cap(title));
            Grid.SetColumn(knobs, 1); g.Children.Add(knobs);
            return g;
        }
        var voiceGrid = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(8, 5),
            Children =
            {
                AmpRow("AMP 1", "amp1level", "amp1pan", "Amp 1"),
                AmpRow("AMP 2", "amp2level", "amp2pan", "Amp 2"),
                new Border
                {
                    BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(0, 5, 0, 0),
                    Child = new StackPanel
                    {
                        Spacing = 5,
                        Children =
                        {
                            Slider("UNISON", "unison", () => Pct(G("unison")), labelW: 46),
                            Slider("VEL→AMP", "velamp", () => Pct(G("velamp")), labelW: 46),
                            Slider("VEL→FIL", "velfilter", () => Pct(G("velfilter")), labelW: 46),
                        },
                    },
                },
            },
        };

        // ---- the rail's own two tabs ------------------------------------------
        var railBodies = new Control[] { globalGrid, voiceGrid };
        var railCells = new Border[RailTabs.Length];
        var railTexts = new TextBlock[RailTabs.Length];
        var railTabGrid = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("*,*") };
        void ShowRailTab()
        {
            for (int i = 0; i < RailTabs.Length; i++)
            {
                bool on = i == railTab;
                railCells[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                railCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                railTexts[i].Foreground = on ? AccentBright : TextTertiary;
                railTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                railBodies[i].IsVisible = on;
            }
            Refresh();
        }
        for (int i = 0; i < RailTabs.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock
            {
                Text = RailTabs[i], FontSize = NotaType.DeviceSection,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var cell = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                railTab = iv; ShowRailTab(); e.Handled = true;
            };
            railCells[i] = cell; railTexts[i] = tb;
            Grid.SetColumn(cell, i); railTabGrid.Children.Add(cell);
        }
        var railHost = new Panel();
        foreach (var b in railBodies) railHost.Children.Add(b);
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(HeaderStrip(railTabGrid));
        Grid.SetRow(railHost, 1); railGrid.Children.Add(railHost);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ---- status strip -----------------------------------------------------
        string RouteWord(string id) => NearestExact(G(id), RouteStops) switch { 0 => "F1", 1 => "F2", _ => "F1+F2" };
        int MacroAssigned() { int n = 0; for (int m = 0; m < 8; m++) if (MacroOn(m)) n++; return n; }
        static string Plural(int n, string one, string many) => n + " " + (n == 1 ? one : many);

        // The line at the right of the tab strip: what this tab holds, in three words.
        string Hint() => tab switch
        {
            0 => $"{WaveNames[Sel("osc1wave", 4)]} · {WaveNames[Sel("osc2wave", 4)]} · {NoiseNames[NoiseBand(G("noisecolor"))]}",
            1 => $"{FilterNames[Sel((fsel == 0 ? "fil1" : "fil2") + "type", 4)]} {(G((fsel == 0 ? "fil1" : "fil2") + "slope") >= 0.5f ? "24" : "12")}\u2009dB/oct",
            2 => "amp · filter",
            3 => $"{(SyncDiv(G("lfo1sync")) > 0 ? SyncText(G("lfo1sync")) : "free")} · {(SyncDiv(G("lfo2sync")) > 0 ? SyncText(G("lfo2sync")) : "free")} · vibrato {(G("vibamt") <= 1e-3f ? "off" : "on")}",
            4 => Plural(RouteCount(), "route", "routes"),
            _ => $"8 slots · {MacroAssigned()} assigned",
        };
        string Summary() => tab switch
        {
            0 => $"Osc 1 {WaveNames[Sel("osc1wave", 4)]} {Cents(G("osc1detune"))} → {RouteWord("osc1route")} · "
               + $"Osc 2 {WaveNames[Sel("osc2wave", 4)]} {Cents(G("osc2detune"))} → {RouteWord("osc2route")} · "
               + $"noise {NoiseNames[NoiseBand(G("noisecolor"))]} {Pct(G("noise"))}",
            1 => $"Filter {fsel + 1} · {FilterNames[Sel((fsel == 0 ? "fil1" : "fil2") + "type", 4)]} "
               + $"{(G((fsel == 0 ? "fil1" : "fil2") + "slope") >= 0.5f ? "24" : "12")}\u2009dB/oct · "
               + $"{Hz(G((fsel == 0 ? "fil1" : "fil2") + "freq"))} · reso {Reso(G((fsel == 0 ? "fil1" : "fil2") + "reso"))} · "
               + $"env {Bip(G((fsel == 0 ? "fil1" : "fil2") + "env"))} · key {Pct(G((fsel == 0 ? "fil1" : "fil2") + "key"))}",
            2 => $"Amp env {AdsrSum("attack", "decay", "sustain", "release")} · filter env → cutoff {Bip(G("fil1env"))}",
            3 => $"LFO 1 {ShapeNames[Sel("lfo1shape", 4)]} {(SyncDiv(G("lfo1sync")) > 0 ? SyncText(G("lfo1sync")) : SlowHz(ExpMap(G("lfo1rate"), 0.05, 20)))} {LfoDest(2)} · "
               + $"LFO 2 {ShapeNames[Sel("lfo2shape", 4)]} {(SyncDiv(G("lfo2sync")) > 0 ? SyncText(G("lfo2sync")) : SlowHz(ExpMap(G("lfo2rate"), 0.05, 20)))} {LfoDest(3)} · "
               + $"vibrato {(G("vibamt") <= 1e-3f ? "off" : VibDepth(G("vibamt")) + (G("vibwheel") >= 0.5f ? " by wheel" : ""))}",
            4 => RouteCount() == 0 ? "no routes — drag a cell to make one" : Plural(RouteCount(), "route", "routes") + " in the matrix",
            _ => $"8 slots · {MacroAssigned()} assigned",
        };
        string Meta()
        {
            string mode = G("mono") >= 0.5f ? "MONO" : "POLY 16";
            return $"{mode} · ±{BendRange(G("bendrange"))} ST · GAIN {Pct(G("volume"))}";
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
        ShowRailTab();
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
