// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Volt editor (instrument kind 6): a two-path subtractive
// synth in an 862 × 260 card (the almanac drew it 700 wide; it grew when Volt took over Nota
// Synth and Nota Bass, specs/026, and again so the oscillator table and the Global rail fit).
//
//   Wheels 76   pitch bend and mod wheel, on screen on every tab — the two hand controls
//               a player reaches for first.
//   Centre      six tabs:
//               Osc    — Osc 1 / Osc 2 / Noise as a table under one row of column heads:
//                        wave, octave, semi, fine, shape (a morph toward the next wave),
//                        pulse width, start phase, level, and which filter the source is
//                        sent to.
//               Filter — the response of the selected filter as a drag pad, with type,
//                        slope, cutoff, resonance, env / key / LFO amounts and, on
//                        Filter 1, how much of it spills into Filter 2; the drive in
//                        front of both filters and Wheel → LFO sit beside them.
//               Env    — the amp and filter envelopes side by side, drawn and draggable.
//               LFO    — LFO 1 / LFO 2 / Vibrato as a table, each naming where it lands.
//               Mod    — the 7 × 6 matrix: drag a cell up for +, down for −.
//               Macro  — eight macros, each a value, an amount and one of twelve targets.
//   Rail 220    Global (Poly / Mono / Legato, bend range, cutoff, resonance and glide as
//               value bars, gain, output drive, pan, and the track's meter at the right) or
//               Voice (both output amps, the unison stack — voices, detune, spread — and
//               the two velocity amounts).
//
// Orange marks values only (arcs, value bars, the picked cell); everything else is grey,
// told apart by shade. Everything is a plugin param → automation / persist / clone, and the
// card follows automation live.

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
    public string Subtitle => "Subtractive";
    public double CardWidth => 862;

    private const double RailW = 220;

    private static readonly string[] TabNames = { "Osc", "Filter", "Env", "LFO", "Mod", "Macro" };
    private static readonly string[] RailTabs = { "Global", "Voice" };
    private static readonly string[] WaveNames = { "saw", "square", "triangle", "sine" };
    private static readonly string[] FilterNames = { "LP", "HP", "BP", "Notch" };
    private static readonly string[] NoiseNames = { "dark", "pink", "white" };
    private static readonly float[] NoiseStops = { 0f, 0.5f, 1f };
    private static readonly float[] RouteStops = { 0f, 1f };
    private static readonly string[] ShapeNames = { "sin", "tri", "sqr", "S&H" };
    private static readonly string[] SyncNames = { "free", "1 bar", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };
    private static readonly string[] VoiceModes = { "Poly", "Mono", "Legato" };
    private static readonly string[] UniNames = { "1", "2", "4", "7" };
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
        "pitch", "body", "interval", "drive", "bright", "air",
        "tone 2", "edge", "motion", "swell", "drift", "width",
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
    private static string ShapeText(float v) => v <= 0.001f ? "off" : NotaNum.Pct(v);
    private static string PwText(float v) => NotaNum.Pct(v == 0.5f ? 0.5f : 0.05f + 0.9f * v);
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
        var ampEnv = new VoltEnv(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 50, ShowTitle = false };   // the panel head names it
        var filtEnv = new VoltEnv(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 50, ShowTitle = false };   // the panel head names it
        var hint = new TextBlock
        {
            FontSize = NotaType.Eyebrow, Foreground = TextTertiary, FontFamily = NotaFonts.UiFamily,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Action applyFilter = () => { };
        Action showTab = () => { };

        void Refresh()
        {
            applyFilter();
            ampEnv.Refresh(); filtEnv.Refresh();
            foreach (var r in readouts) r();
            hint.Text = Hint();
        }

        // ---- shared builders --------------------------------------------------
        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = NotaText.Label(t), FontSize = NotaType.KnobLabel,
            Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = NotaType.KnobValue) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.UiFamily,
        };
        static TextBlock Section(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.DeviceBody, FontWeight = FontWeight.SemiBold,
            Foreground = c ?? TextPrimary, VerticalAlignment = VerticalAlignment.Center,
        };

        static TextBlock Head(string t, HorizontalAlignment h = HorizontalAlignment.Center) => new()
        {
            Text = t, FontSize = NotaType.Eyebrow, Foreground = TextDisabled,
            HorizontalAlignment = h, VerticalAlignment = VerticalAlignment.Bottom,
        };

        // Every knob on this card is a table cell: the cell carries a short label (or, in a
        // macro tile, none at all, because the tile is already named) while MIDI learn and
        // the CV menu keep the parameter's full name.
        Control PKnob(string id, string label, string learn, Func<float, string> fmt,
            double cellW = 44, bool inline = false, bool bipolar = false)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock
            {
                Text = fmt(G(id)), FontSize = NotaType.KnobValue, FontFamily = NotaFonts.UiFamily,
                Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, LineHeight = 13, Margin = new Thickness(0, 2, 0, 0),
            };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, Inline = inline, Bipolar = bipolar, Default = engine.InstrumentParamDefault(track, pi),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            knob.Format = v => fmt((float)v);
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), learn);
            if (label.Length > 0) return KnobCell(label, knob, value, cellW);
            ValueEntry.Attach(knob, learn, value);
            return new StackPanel { Width = cellW, Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { knob, value } };
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

        // Waveform chips: the same groove as Segments, with the glyph instead of a word — four
        // waves read faster drawn than spelled. The picked cell rises in the soft tint, its
        // wave in orange.
        Control WaveChips(string id)
        {
            var cells = new Border[4];
            var icons = new WaveIcon[4];
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < 4; i++)
            {
                int iv = i;
                var ic = new WaveIcon(i) { Width = 18, Height = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border
                {
                    Width = 31, Height = NotaSize.Seg, CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent,
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
                    cells[i].Background = on ? NotaPalette.AccentSoft : Brushes.Transparent;
                    if (on) cells[i].BindResource(Border.BoxShadowProperty, "Shadow.Lifted"); else cells[i].BoxShadow = default;
                    icons[i].Stroke = on ? AccentBright : TextTertiary;
                    icons[i].InvalidateVisual();
                }
            }
            readouts.Add(Paint); Paint();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            var host = new Border
            {
                Background = NotaPalette.Groove, CornerRadius = NotaRadius.Group, Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = row,
            };
            host.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
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
            if (!spring) ValueEntry.Attach(w, w.EntryField(id == "modwheel" ? "Mod wheel" : id, NotaNum.Pct));   // the bend wheel springs back
            return w;
        }

        var bendWheel = Wheel("bend", 0.5, true);
        var modWheel = Wheel("modwheel", 0, false);
        var bendVal = Mono("", TextSecondary);
        var modVal = Mono("", TextSecondary);
        bendVal.HorizontalAlignment = modVal.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            int range = BendRange(G("bendrange"));
            double semis = (G("bend") - 0.5) * 2 * range;
            bool centred = Math.Abs(semis) < 0.05;
            bendVal.Text = centred ? $"±{range}\u2009st" : NotaNum.Unit(semis, "+0.0;−0.0;0", "st");
            bendVal.Foreground = centred ? TextSecondary : TextPrimary;
            modVal.Text = NotaNum.Pct(G("modwheel"));
            modVal.Foreground = G("modwheel") > 1e-3f ? TextPrimary : TextSecondary;
        });

        var wheelsPanel = InstrumentCardKit.WheelsPanel(bendWheel, modWheel, bendVal, modVal);

        // ---- Osc tab ----------------------------------------------------------
        // One grid for all three sources under one row of column heads, so each head sits
        // over its own cells and the knobs carry no labels of their own (Noise keeps its
        // Color label: that column means Oct above it).
        var oscGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("46,Auto,*,36,36,36,36,36,36,36,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,44,44,*"),
        };
        void OscCell(Control c, int row, int col, int span = 1)
        { Grid.SetRow(c, row); Grid.SetColumn(c, col); if (span > 1) Grid.SetColumnSpan(c, span); oscGrid.Children.Add(c); }

        {
            var waveHead = Head("Wave", HorizontalAlignment.Left);
            OscCell(waveHead, 0, 1);
            string[] heads = { "Oct", "Semi", "Fine", "Shape", "PW", "Phase", "Level" };
            for (int k = 0; k < heads.Length; k++) OscCell(Head(heads[k]), 0, 3 + k);
            OscCell(Head("Route", HorizontalAlignment.Right), 0, 11);
        }

        Control RouteCell(string id)
        {
            var chips = StopChips(id, new[] { "F1", "F2" }, RouteStops);
            chips.HorizontalAlignment = HorizontalAlignment.Right;
            return chips;
        }

        for (int o = 0; o < 2; o++)
        {
            string pre = o == 0 ? "osc1" : "osc2";
            string nm = o == 0 ? "Osc 1" : "Osc 2";
            string learn = o == 0 ? "OSC 1" : "OSC 2";
            int r = o + 1;
            OscCell(Section(nm), r, 0);
            OscCell(WaveChips(pre + "wave"), r, 1);
            OscCell(PKnob(pre + "octave", "", learn + " Octave", Oct, 36, bipolar: true), r, 3);
            OscCell(PKnob(pre + "semi", "", learn + " Semi", Semi, 36, bipolar: true), r, 4);
            OscCell(PKnob(pre + "detune", "", learn + " Fine", Cents, 36, bipolar: true), r, 5);
            OscCell(PKnob(pre + "shape", "", learn + " Shape", ShapeText, 36), r, 6);
            OscCell(PKnob(pre + "pw", "", learn + " Pulse Width", PwText, 36), r, 7);
            OscCell(PKnob(pre + "phase", "", learn + " Phase", Phase, 36), r, 8);
            OscCell(PKnob(pre + "level", "", learn + " Level", Pct, 36), r, 9);
            OscCell(RouteCell(pre + "route"), r, 11);
        }
        {
            OscCell(Section("Noise"), 3, 0);
            OscCell(Chips("noisecolor", NoiseNames, () => NoiseBand(G("noisecolor")), iv => { SetP("noisecolor", NoiseStops[iv]); Refresh(); }), 3, 1);
            var color = PKnob("noisecolor", "Color", "Noise Color", Pct, 40);
            color.HorizontalAlignment = HorizontalAlignment.Center; color.VerticalAlignment = VerticalAlignment.Center;
            OscCell(color, 3, 3);
            OscCell(PKnob("noise", "", "Noise Level", Pct, 36), 3, 9);

            var mixBar = new MixBar(engine, track, new[] { I("osc1level"), I("osc2level"), I("noise") }) { Height = 8 };
            readouts.Add(mixBar.Refresh);
            var mixVal = Mono("", TextPrimary);
            readouts.Add(() =>
            {
                float sum = G("osc1level") + G("osc2level") + G("noise");
                mixVal.Text = sum <= 1e-3f ? "silent" : NotaNum.Db(AudioMath.LinToDb(Math.Min(1f, sum)));
            });
            var mixCell = new StackPanel
            {
                Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0),
                Children = { Cap("Mix sum"), mixBar, mixVal },
            };
            mixVal.HorizontalAlignment = HorizontalAlignment.Left;
            OscCell(mixCell, 3, 4, 5);
            OscCell(RouteCell("noiseroute"), 3, 11);
        }
        var oscBody = new Border { Padding = new Thickness(0, 6, 0, 0), Child = oscGrid };

        // ---- Filter tab -------------------------------------------------------
        // Both filters share one column of controls; picking 1 or 2 swaps which set of
        // knobs is visible — a knob is bound to one param index for its life.
        var filtCells = new Panel[6];
        var filtStack = new Control[2, 6];
        string[] filtIds = { "freq", "reso", "env", "key", "lfo", "tof2" };
        string[] filtLbl = { "Cutoff", "Reso", "Env", "Key", "LFO", "To F2" };
        // Inline knobs (name and value beside the knob), so the two rows fit under the chips.
        var filtKnobGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 8, VerticalAlignment = VerticalAlignment.Top,
        };
        // Cutoff Reso Env | Drive  over  Key LFO | Wheel To F2 — Drive and Wheel → LFO act on
        // both filters, so they stay put when the 1 / 2 pick swaps the rest.
        (int Col, int Row)[] filtAt = { (0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (3, 1) };
        for (int k = 0; k < 6; k++)
        {
            filtCells[k] = new Panel();
            Grid.SetColumn(filtCells[k], filtAt[k].Col); Grid.SetRow(filtCells[k], filtAt[k].Row);
            filtKnobGrid.Children.Add(filtCells[k]);
        }
        {
            var drive = PKnob("fildrive", "Drive", "Filter Drive", Pct, 64, inline: true);
            Grid.SetColumn(drive, 3); filtKnobGrid.Children.Add(drive);
            var wheel = PKnob("wheellfo", "Wheel", "Wheel → LFO", Pct, 64, inline: true);
            Grid.SetColumn(wheel, 2); Grid.SetRow(wheel, 1); filtKnobGrid.Children.Add(wheel);
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
                    : PKnob(id, filtLbl[k], $"Filter {f + 1} {filtLbl[k]}", fmt, 64, inline: true, bipolar: k is 2 or 4);
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
                iv => { SetP(pre + "slope", iv); Refresh(); });
            filtTypeHosts[f] = new Panel { Children = { filtTypeChips[f] } };
            filtSlopeHosts[f] = new Panel { Children = { filtSlopeChips[f] } };
        }
        var filtPick = Segments(new[] { "1", "2" }, () => fsel, iv => { fsel = iv; Refresh(); }, out var filtPickSync);
        readouts.Add(filtPickSync);
        filtPick.HorizontalAlignment = HorizontalAlignment.Right;
        var filtHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        filtHead.Children.Add(Cap("Filter"));
        Grid.SetColumn(filtPick, 1); filtHead.Children.Add(filtPick);

        var filtTypeStack = new Panel();
        foreach (var h in filtTypeHosts) filtTypeStack.Children.Add(h);
        var filtSlopeStack = new Panel();
        foreach (var h in filtSlopeHosts) filtSlopeStack.Children.Add(h);

        filtSlopeStack.HorizontalAlignment = HorizontalAlignment.Right;
        var filtChipRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        filtChipRow.Children.Add(filtTypeStack);
        Grid.SetColumn(filtSlopeStack, 1); filtChipRow.Children.Add(filtSlopeStack);
        filtKnobGrid.Margin = new Thickness(0, 6, 0, 0);
        var filtRight = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 6, Width = 264 };
        var filtRightRows = new Control[] { filtHead, filtChipRow, filtKnobGrid };
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

        var filtGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        filtGrid.Children.Add(filtGraph);
        Grid.SetColumn(filtRight, 1); filtGrid.Children.Add(filtRight);
        var filtBody = new Border { Padding = new Thickness(0, 8, 0, 0), Child = filtGrid };

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
                var v = Mono(fk(G(id)), TextPrimary);
                v.HorizontalAlignment = HorizontalAlignment.Center;
                readouts.Add(() => v.Text = fk(G(id)));
                var cap = Cap(stage[k]); cap.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Border
                {
                    Background = Brushes.Transparent,
                    Child = new StackPanel { Children = { v, cap } },
                };
                if (I(id) is var pi and >= 0) MidiLearn.Bind(cell, MidiTarget.PluginParam(track, -1, pi), title + " · " + stage[k]);
                Grid.SetColumn(cell, k); cells.Children.Add(cell);
            }
            var sum = Mono("", TextTertiary, NotaType.KnobLabel);
            sum.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => sum.Text = summary());
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
            var titleText = Cap(title); titleText.Foreground = TextSecondary;
            head.Children.Add(titleText);
            var dest = Mono(route, TextTertiary, NotaType.KnobLabel); Grid.SetColumn(dest, 1); head.Children.Add(dest);
            Grid.SetColumn(sum, 2); head.Children.Add(sum);

            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 4 };
            g.Children.Add(head);
            Grid.SetRow(view, 1); g.Children.Add(view);
            Grid.SetRow(cells, 2); g.Children.Add(cells);
            return g;
        }
        string AdsrSum(string a, string d, string s, string r)
            => $"{Secs(G(a), 0.001, 2.0)} · {Secs(G(d), 0.002, 4.0)} · {Pct(G(s))} · {Secs(G(r), 0.002, 5.0)}";
        var ampPanel = EnvPanel("Amp env", "→ Level", ampEnv, new[] { "attack", "decay", "sustain", "release" },
            () => AdsrSum("attack", "decay", "sustain", "release"));
        var filtEnvPanel = EnvPanel("Filter env", "→ Cutoff", filtEnv, new[] { "fattack", "fdecay", "fsustain", "frelease" },
            () => AdsrSum("fattack", "fdecay", "fsustain", "frelease"));
        var filtEnvHost = new Border
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0), Child = filtEnvPanel,
        };
        var envGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        envGrid.Children.Add(ampPanel);
        Grid.SetColumn(filtEnvHost, 1); envGrid.Children.Add(filtEnvHost);
        var envBody = new Border { Padding = new Thickness(0, 8, 0, 0), Child = envGrid };

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

        // A table like the Osc tab: the column names once in a head row, the knobs under them
        // with their values only, a rule between the rows.
        var lfoGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("52,Auto,*,*,*,*,Auto"), ColumnSpacing = 5,
            RowDefinitions = new RowDefinitions("Auto,*,*,*"),
        };
        void LfoCell(Control c, int row, int col, int span = 1)
        { Grid.SetRow(c, row); Grid.SetColumn(c, col); if (span > 1) Grid.SetColumnSpan(c, span); lfoGrid.Children.Add(c); }
        {
            LfoCell(Head("Shape", HorizontalAlignment.Left), 0, 1);
            string[] heads = { "Rate", "Depth", "Sync", "Fade" };
            for (int k = 0; k < heads.Length; k++) LfoCell(Head(heads[k]), 0, 2 + k);
            LfoCell(Head("Goes to", HorizontalAlignment.Right), 0, 6);
        }
        void LfoRule(int row)
        {
            var rule = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetRow(rule, row); Grid.SetColumnSpan(rule, 7); lfoGrid.Children.Add(rule);
        }
        for (int l = 0; l < 2; l++)
        {
            string pre = l == 0 ? "lfo1" : "lfo2";
            string nm = l == 0 ? "LFO 1" : "LFO 2";   // MIDI-learn names stay as they were
            int src = l + 2, r = l + 1;
            if (l > 0) LfoRule(r);
            LfoCell(Section(nm), r, 0);
            LfoCell(Chips(pre + "shape", ShapeNames), r, 1);
            string rateId = pre + "rate", syncId = pre + "sync";
            LfoCell(PKnob(rateId, "", nm + " Rate", v => SyncDiv(G(syncId)) > 0 ? SyncText(G(syncId)) : SlowHz(ExpMap(v, 0.05, 20)), 46), r, 2);
            LfoCell(PKnob(pre + "depth", "", nm + " Depth", Pct, 46), r, 3);
            LfoCell(PKnob(syncId, "", nm + " Sync", SyncText, 46), r, 4);
            LfoCell(PKnob(pre + "fade", "", nm + " Fade", FadeT, 46), r, 5);
            var dest = Mono("", TextSecondary, NotaType.KnobLabel);
            dest.HorizontalAlignment = HorizontalAlignment.Right; dest.Width = 62; dest.TextAlignment = TextAlignment.Right;
            readouts.Add(() => dest.Text = LfoDest(src));
            LfoCell(dest, r, 6);
        }
        {
            LfoRule(3);
            LfoCell(Section("Vibrato"), 3, 0);
            LfoCell(Toggle("By wheel", "vibwheel"), 3, 1);
            LfoCell(PKnob("vibrate", "", "Vibrato Rate", v => SlowHz(ExpMap(v, 0.1, 12)), 46), 3, 2);
            LfoCell(PKnob("vibamt", "", "Vibrato Depth", VibDepth, 46), 3, 3);
            var note = new TextBlock
            {
                Text = "vibrato always rides the pitch", FontSize = NotaType.KnobLabel, Foreground = TextTertiary,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            };
            LfoCell(note, 3, 4, 2);
            var dest = Mono("→ Pitch", TextSecondary, NotaType.KnobLabel);
            dest.HorizontalAlignment = HorizontalAlignment.Right; dest.Width = 62; dest.TextAlignment = TextAlignment.Right;
            LfoCell(dest, 3, 6);
        }
        var lfoBody = new Border { Padding = new Thickness(0, 6, 0, 0), Child = lfoGrid };

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
            Text = "drag a cell — up adds, down subtracts · double-click clears", FontSize = NotaType.KnobLabel,
            Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        var modGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 3 };
        modGrid.Children.Add(matrix);
        Grid.SetRow(modHint, 1); modGrid.Children.Add(modHint);
        var modBody = new Border { Padding = new Thickness(0, 8, 0, 0), Child = modGrid };

        // ---- Macro tab --------------------------------------------------------
        var macroGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("*,*"),
            ColumnSpacing = 6, RowSpacing = 6,
        };
        int MacroDest(int m) => Math.Clamp((int)Math.Round(G($"mac{m}dest") * (MacroDests.Length - 1)), 0, MacroDests.Length - 1);
        bool MacroOn(int m) => Math.Abs(G($"mac{m}amt") - 0.5f) > 0.01f;
        for (int m = 0; m < 8; m++)
        {
            int mv = m;
            string destId = $"mac{mv}dest";
            var name = new TextBlock
            {
                FontSize = NotaType.KnobLabel,
                Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center,
            };
            var dest = new TextBlock
            {
                FontSize = NotaType.KnobLabel, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center,
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
                Background = Brushes.Transparent,
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
                                PKnob($"mac{mv}val", "", $"Macro {mv + 1}", Pct, 40),
                                PKnob($"mac{mv}amt", "", $"Macro {mv + 1} Amount", Bip, 40, bipolar: true),
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
                name.Text = on ? $"Macro {mv + 1} · {MacroWords[d]}" : $"Macro {mv + 1}";
                name.Foreground = on ? TextSecondary : TextDisabled;
                dest.Text = on ? "→ " + MacroDests[d] : "unassigned";
                dest.Foreground = on ? TextTertiary : TextDisabled;
            });
            Grid.SetColumn(cellBorder, mv % 4); Grid.SetRow(cellBorder, mv / 4);
            macroGrid.Children.Add(cellBorder);
        }
        var macroBody = new Border { Padding = new Thickness(0, 6, 0, 0), Child = macroGrid };

        // ---- the tab strip ----------------------------------------------------
        var bodies = new Control[] { oscBody, filtBody, envBody, lfoBody, modBody, macroBody };
        var clearAll = TextButton("Clear all");
        clearAll.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(clearAll, "Zero every route in the matrix");
        clearAll.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(clearAll).Properties.IsLeftButtonPressed) return;
            matrix.ClearAll(); Refresh(); e.Handled = true;
        };
        var tabTrail = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 3), Children = { hint, clearAll },
        };

        Action syncTabs = () => { };
        var tabStrip = SynthInstrumentCard.TabStrip(TabNames, () => tab, i => { tab = i; showTab(); }, out var tabSync, tabTrail);
        syncTabs = tabSync;

        showTab = () =>
        {
            syncTabs();
            for (int i = 0; i < TabNames.Length; i++) bodies[i].IsVisible = i == tab;
            clearAll.IsVisible = tab == 4;
            Refresh();
        };

        var tabHost = new Panel();
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(tabStrip);
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · Global ----------------------------------------------------
        // Poly / Mono / Legato over two params: Mono, and Legato (which only acts in Mono).
        var monoChips = Chips("mono", VoiceModes, () => G("mono") < 0.5f ? 0 : G("legato") >= 0.5f ? 2 : 1,
            iv => { SetP("mono", iv > 0 ? 1f : 0f); if (iv > 0) SetP("legato", iv == 2 ? 1f : 0f); Refresh(); });
        monoChips.HorizontalAlignment = HorizontalAlignment.Right;
        var monoRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        monoRow.Children.Add(Cap("Voices"));
        Grid.SetColumn(monoChips, 1); monoRow.Children.Add(monoChips);

        var bendChips = Segments(BendNames, () => NearestExact(G("bendrange"), BendStops),
            iv => { SetP("bendrange", BendStops[iv]); Refresh(); }, out var bendSync);
        readouts.Add(bendSync);
        if (I("bendrange") is var bri and >= 0) MidiLearn.Bind(bendChips, MidiTarget.PluginParam(track, -1, bri), "Bend Range");
        bendChips.HorizontalAlignment = HorizontalAlignment.Right;
        var bendRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        bendRow.Children.Add(Cap("Bend"));
        Grid.SetColumn(bendChips, 1); bendRow.Children.Add(bendChips);

        // The meter stands at the rail's right edge, beside the three value bars and the
        // Gain / Drive / Pan row, its scale true to the meter's own dB map.
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
            c.LineHeight = 10;
            Grid.SetRow(c, i * 2 + 1); meterScale.Children.Add(c);
        }
        var meterBlock = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 6 };
        meterBlock.Children.Add(meter);
        Grid.SetColumn(meterScale, 1); meterBlock.Children.Add(meterScale);

        var knobRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        knobRow.Children.Add(PKnob("volume", "Gain", "Gain", Pct, 56, inline: true));
        knobRow.Children.Add(PKnob("drive", "Drive", "Drive", Pct, 56, inline: true));
        knobRow.Children.Add(PKnob("outpan", "Pan", "Out Pan", PanText, 48, inline: true, bipolar: true));
        var leftCol = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                Slider("Cutoff", "fil1freq", () => Hz(G("fil1freq")), labelW: 40, valueW: 48),
                Slider("Reso", "fil1reso", () => Reso(G("fil1reso")), labelW: 40, valueW: 48),
                Slider("Glide", "glide", () => GlideT(G("glide")), labelW: 40, valueW: 48),
                knobRow,
            },
        };
        var lower = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 12, 0, 0) };
        lower.Children.Add(leftCol);
        Grid.SetColumn(meterBlock, 1); lower.Children.Add(meterBlock);

        var globalGrid = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(0, 4, 0, 0),
            Children = { monoRow, bendRow, lower },
        };

        // ---- rail · Voice -----------------------------------------------------
        // A row of four small knobs under its captions — the rail is 186 wide.
        Grid KnobRow(params Control[] cells)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            for (int k = 0; k < cells.Length; k++) { Grid.SetColumn(cells[k], k); g.Children.Add(cells[k]); }
            return g;
        }
        Grid CapRow(string a, string b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            var ca = Cap(a); ca.HorizontalAlignment = HorizontalAlignment.Center;
            var cb = Cap(b); cb.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(ca); Grid.SetColumn(cb, 1); g.Children.Add(cb);
            return g;
        }
        Border Rule(Control child) => new()
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 3, 0, 0), Child = child,
        };
        var uniChips = Chips("univoices", UniNames);
        uniChips.HorizontalAlignment = HorizontalAlignment.Right;
        var uniRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        uniRow.Children.Add(Cap("Unison"));
        Grid.SetColumn(uniChips, 1); uniRow.Children.Add(uniChips);
        var voiceGrid = new StackPanel
        {
            Spacing = 3, Margin = new Thickness(0, 2, 0, 0),
            Children =
            {
                CapRow("Amp 1", "Amp 2"),
                KnobRow(PKnob("amp1level", "Level", "Amp 1 Level", Pct, 46), PKnob("amp1pan", "Pan", "Amp 1 Pan", PanText, 46, bipolar: true),
                        PKnob("amp2level", "Level", "Amp 2 Level", Pct, 46), PKnob("amp2pan", "Pan", "Amp 2 Pan", PanText, 46, bipolar: true)),
                Rule(new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        uniRow,
                        // With one voice Detune is the partner oscillator's distance (up to 25 c);
                        // with 2 / 4 / 7 it is the spread of the stack (±50 c) and Spread pans it.
                        KnobRow(PKnob("unison", "Detune", "Unison", Pct, 46), PKnob("unispread", "Spread", "Unison Spread", Pct, 46),
                                PKnob("velamp", "Vel amp", "Vel→Amp", Pct, 46), PKnob("velfilter", "Vel fil", "Vel→Filter", Pct, 46)),
                    },
                }),
            },
        };

        // ---- the rail's own two tabs ------------------------------------------
        var railBodies = new Control[] { globalGrid, voiceGrid };
        Action syncRail = () => { };
        void ShowRailTab()
        {
            syncRail();
            for (int i = 0; i < RailTabs.Length; i++) railBodies[i].IsVisible = i == railTab;
            Refresh();
        }
        var railTabGrid = SynthInstrumentCard.TabStrip(RailTabs, () => railTab, i => { railTab = i; ShowRailTab(); }, out var railSync);
        syncRail = railSync;
        var railHost = new Panel();
        foreach (var b in railBodies) railHost.Children.Add(b);
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(railTabGrid);
        Grid.SetRow(railHost, 1); railGrid.Children.Add(railHost);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;
        tabPanel.Padding = rail.Padding = new Thickness(SectionPad);

        // ---- tab hint ---------------------------------------------------------
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
        // ---- assembly ---------------------------------------------------------
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = SectionGap, Margin = new Thickness(0, CardPad, 0, 0) };
        root.Children.Add(wheelsPanel);
        Grid.SetColumn(tabPanel, 1); root.Children.Add(tabPanel);
        Grid.SetColumn(rail, 2); root.Children.Add(rail);

        ctx.SetInstLiveViz(Refresh);
        ShowRailTab();
        showTab();
        return root;
    }

    // A pressed-in section (style guide).
    private static Decorator SectionBox(Control child) => DeviceCardKit.Section(child);

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
