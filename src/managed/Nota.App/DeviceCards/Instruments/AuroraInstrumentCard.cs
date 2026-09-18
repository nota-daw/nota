// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Aurora editor (instrument kind 5): a wavetable synth in
// the 700 × 260 card the almanac draws for it, the same body Operator and Volt wear.
//
//   Wheels 56   pitch bend and mod wheel, on screen on every tab.
//   Centre      six tabs:
//               Osc    — the 16-frame wavetable stack beside the two oscillators, the sub
//                        and the unison stack; the strip on top carries the bank and the
//                        warp mode of whichever oscillator you are looking at.
//               Filter — the selected filter's response as a drag pad, with its type,
//                        slope, cutoff, resonance, env / LFO / key amounts and where each
//                        source (Osc 1 / Osc 2 / Sub) is sent.
//               Env    — Env 1 (amp) and Env 2 (free) drawn and draggable.
//               LFO    — both LFOs as a table, each naming where it lands, over a window
//                        that draws the selected LFO's own shape.
//               Mod    — the 8 × 7 matrix: drag a cell up for +, down for −.
//               FX     — drive · chorus · reverb, each on its own switch.
//   Rail 186    Global (voice mode, bend range, position, cutoff, unison, gain, pan and
//               the track's meter) or Macros (eight, three to a page, each a value, an
//               amount and one of twelve targets). Picking Mod brings the macros up, as
//               the almanac draws that state.
//
// Brass is the parameter itself; teal is modulation and what receives it. Everything is a
// plugin param → automation / persist / clone, and the card follows automation live.
//
// The live tick runs thirty times a second, so Refresh first asks whether ANY parameter
// moved and returns at once when none did: the readouts, the four graphs and the matrix
// are only touched when there is something new to say.

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

internal sealed class AuroraInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "WAVETABLE";
    public double CardWidth => 700;

    private const double WheelsW = 56, RailW = 186, TabH = 20, StatusH = 18, BodyH = 186;

    private static readonly string[] TabNames = { "Osc", "Filter", "Env", "LFO", "Mod", "FX" };
    private static readonly string[] RailTabs = { "Global", "Macros" };
    private static readonly string[] Banks = { "Analog", "Pulse", "Formant", "Chroma" };
    private static readonly string[] BankWords = { "analog", "pulse", "formant", "chroma" };
    private static readonly string[] WarpModes = { "Off", "Sync", "Bend", "PWM", "Fold" };
    private static readonly string[] FiltTypes = { "LP", "HP", "BP", "Notch", "Morph" };
    private static readonly string[] Routes = { "F1", "F2", "1+2", "Dry" };
    private static readonly string[] SubWaves = { "sin", "sqr", "tri" };
    private static readonly string[] ShapeNames = { "sin", "tri", "sqr", "S&H" };
    private static readonly string[] SyncNames = { "free", "1 bar", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };
    private static readonly string[] BendNames = { "±2", "±5", "±12" };
    private static readonly float[] BendStops = { 1f / 11f, 4f / 11f, 1f };
    private static readonly string[] DriveModes = { "Tube", "Tape", "Fold" };
    private static readonly string[] ChorusVoices = { "1×", "2×", "4×" };
    private static readonly string[] ReverbModes = { "Room", "Hall", "Plate" };
    private static readonly string[] SrcNames = { "Env 1", "Env 2", "LFO 1", "LFO 2", "Velocity", "Key", "Mod Whl", "Random" };
    private static readonly string[] DestNames = { "Pitch", "Osc 2", "Position", "Cutoff", "Reso", "Level", "Pan" };

    // The twelve macro targets in the engine's order (WavetableSynth::kMacroDests). The
    // seven matrix destinations sit at 0, 2, 4, 6, 7, 9 and 11, so a macro saved against
    // the old seven-entry list still points at the same one.
    private static readonly string[] MacroDests =
    {
        "Pitch", "Warp", "Osc 2 pitch", "Osc 2 level", "Position", "Sub level",
        "Cutoff", "Reso", "Unison", "Level", "Drive", "Pan",
    };
    // What a macro on that target is called on the patch — the word a player reads.
    private static readonly string[] MacroWords =
    {
        "PITCH", "SHAPE", "INTERVAL", "LAYER", "SHIMMER", "WEIGHT",
        "AIR", "EDGE", "WIDTH", "SWELL", "GRIT", "PLACE",
    };

    // The engine's own maps (WavetableSynth.h), so a readout says what it does.
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));
    private static string Hz(float v) => NotaNum.Hz(ExpMap(v, 20, 18000));
    private static string SlowHz(double hz) => hz >= 9.995 ? NotaNum.Hz(hz) : NotaNum.Unit(hz, hz < 0.995 ? "0.00" : "0.0", "Hz");
    private static string Secs(float v, double lo, double hi) => NotaNum.Time(ExpMap(v, lo, hi));
    private static string Pct(float v) => NotaNum.Pct(v);
    private static string Bip(float v) => NotaNum.Unit((v - 0.5f) * 200, "+0;−0;0", "%");
    private static string Oct(float v) => NotaNum.Str(Math.Round((v - 0.5) * 6), "+0;−0;0");
    private static string Semi(float v) => NotaNum.Str(Math.Round((v - 0.5) * 24), "+0;−0;0");
    private static string Cents(float v) => NotaNum.Unit(Math.Round((v - 0.5) * 100), "+0;−0;0", "c");
    private static string Detune(float v) => NotaNum.Unit(v * 50, "0", "c");
    private static string SubOct(float v) => NotaNum.Str(Math.Round((v - 0.5) * 4) - 1, "+0;−0;0");
    private static int Voices(float v) => 1 + (int)Math.Round(v * 6);
    private static int Frame(float v) => 1 + (int)Math.Round(v * 15);
    private static string FrameText(float v) => $"{Frame(v)}/16";
    private static string ChorusHz(float v) => SlowHz(ExpMap(v, 0.1, 6.0));
    private static int SyncDiv(float v) => Math.Clamp((int)Math.Round(v * 7), 0, 7);
    private static string SyncText(float v) => SyncNames[SyncDiv(v)];
    private static int BendRange(float v) => (int)Math.Round(1 + v * 11);
    private static string PanText(float v)
    {
        double p = (v - 0.5) * 200;
        return Math.Abs(p) < 0.5 ? "C" : (p < 0 ? "L" : "R") + NotaNum.Str(Math.Abs(p), "0");
    }

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>(pc * 2);
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        float G(string id) => I(id) is var i and >= 0 ? engine.PluginParamGet(track, -1, i) : 0f;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        void Begin(string id) { if (I(id) >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void End(string id) { if (I(id) >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);
        (int, string) P(string id) => (I(id), id);

        var readouts = new List<Action>();
        int tab = 0, railTab = 0, fsel = 0, osel = 0;   // Osc · Global · Filter 1 · Osc 1

        var stack = new AuroraStack { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 60 };
        var filtGraph = new VoltFilter(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 60 };
        var env1 = new VoltEnv(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 50 };
        var env2 = new VoltEnv(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 50, Accent = Teal };
        var lfoStrip = new AuroraLfoStrip { VerticalAlignment = VerticalAlignment.Stretch };

        var hint = new TextBlock
        {
            FontSize = NotaType.Axis, Foreground = TextTertiary, FontFamily = NotaFonts.MonoFamily,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily };
        Action applyFilter = () => { };
        Action applyOsc = () => { };
        Action showTab = () => { };

        // ---- the live tick's gate -------------------------------------------------
        // Thirty times a second the card is asked to follow automation. Reading the
        // parameter block is one cheap pass; repainting four graphs, a 56-cell matrix and
        // two hundred readouts is not — so do the pass, and stop there when nothing moved.
        var snapshot = new float[pc];
        bool primed = false;
        bool ParamsMoved()
        {
            bool moved = !primed;
            for (int i = 0; i < pc; i++)
            {
                float v = engine.PluginParamGet(track, -1, i);
                if (v != snapshot[i]) { snapshot[i] = v; moved = true; }
            }
            primed = true;
            return moved;
        }
        void Refresh(bool force)
        {
            if (!ParamsMoved() && !force) return;
            applyOsc(); applyFilter();
            stack.Set(Sel(osel == 0 ? "table" : "osc2table", 4), G(osel == 0 ? "position" : "osc2position"));
            stack.Dimmed = G(osel == 0 ? "osc1on" : "osc2on") < 0.5f;
            env1.Refresh(); env2.Refresh();
            lfoStrip.Set(Sel("lfo1shape", 4), LfoCycles(0), G("lfo1depth"));
            foreach (var r in readouts) r();
            hint.Text = Hint();
            status.Text = Summary();
            meta.Text = Meta();
        }
        void Touch() => Refresh(true);

        // ---- shared builders ------------------------------------------------------
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
        static Border Rule() => new() { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0) };

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
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Touch(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), learn);
            return label.Length == 0
                ? new StackPanel { Width = cellW, Spacing = 0, Children = { knob, value } }
                : KnobCell(label, knob, value, cellW);
        }

        // A knob with no value line: a macro box prints both its numbers beside the name,
        // so the two knobs above them need none of their own.
        Control BareKnob(string id, string learn, Func<float, string> fmt, double size, IBrush? arc = null)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var shadow = new TextBlock();   // not in the tree; AddInstFader needs somewhere to write
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, ArcColor = arc, Default = engine.InstrumentParamDefault(track, pi),
                Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
            };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); Touch(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, shadow, fmt);
            readouts.Add(() => ToolTip.SetTip(knob, learn + " · " + fmt(G(id))));
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), learn);
            return knob;
        }

        // A drag value: a caps label over a mono readout. Vertical drag moves it (a
        // stepped param snaps to its steps), double-click puts it back to its default.
        Control DragValue(string id, string label, string learn, Func<float, string> fmt, float step = 0, IBrush? ink = null)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var cap = Cap(label); cap.HorizontalAlignment = HorizontalAlignment.Left;
            var value = Mono(fmt(G(id)), ink ?? TextPrimary, NotaType.RowLabel);
            var cell = new StackPanel
            {
                Spacing = 0, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { cap, value },
            };
            bool drag = false; double y0 = 0; float v0 = 0;
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2)
                {
                    Begin(id); SetP(id, engine.InstrumentParamDefault(track, pi)); End(id);
                    Touch(); e.Handled = true; return;
                }
                drag = true; y0 = e.GetPosition(cell).Y; v0 = G(id);
                Begin(id); e.Pointer.Capture(cell); e.Handled = true;
            };
            cell.PointerMoved += (_, e) =>
            {
                if (!drag) return;
                float v = Math.Clamp(v0 + (float)((y0 - e.GetPosition(cell).Y) / 120.0), 0f, 1f);
                if (step > 0) v = MathF.Round(v / step) * step;
                if (v == G(id)) return;
                SetP(id, v); Touch();
            };
            void Stop(PointerEventArgs e) { if (!drag) return; drag = false; e.Pointer.Capture(null); End(id); }
            cell.PointerReleased += (_, e) => Stop(e);
            cell.PointerCaptureLost += (_, _) => { if (drag) { drag = false; End(id); } };
            readouts.Add(() =>
            {
                value.Text = fmt(G(id));
                bool moved = Math.Abs(G(id) - engine.InstrumentParamDefault(track, pi)) > 1e-4f;
                cap.Foreground = moved && ink is null ? AccentBright : TextTertiary;
            });
            ToolTip.SetTip(cell, learn + " · drag to change, double-click to reset");
            MidiLearn.Bind(cell, MidiTarget.PluginParam(track, -1, pi), learn);
            return cell;
        }

        Control Chips(string id, string[] names, Func<int>? current = null, Action<int>? pick = null, bool fill = false)
        {
            int n = names.Length;
            var seg = Segments(names, current ?? (() => Sel(id, n)),
                pick ?? (iv => { SetP(id, iv / (float)(n - 1)); Touch(); }), out var sync, fill: fill);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }
        Control StopChips(string id, string[] names, float[] stops, bool fill = false)
            => Chips(id, names, () => NearestExact(G(id), stops), iv => { SetP(id, stops[iv]); Touch(); }, fill);

        Grid Slider(string label, string id, Func<string> text, double labelW = 0, double valueW = 40)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Touch(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Touch(); } : null,
                labelWidth: labelW, valueWidth: valueW);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), label);
            return row;
        }

        Control Toggle(string label, string id, Func<string>? live = null)
        {
            var host = Switch(label, () => G(id) >= 0.5f, () => { SetP(id, G(id) >= 0.5f ? 0f : 1f); Touch(); }, out var sync, liveLabel: live);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(host, MidiTarget.PluginParam(track, -1, pi), label);
            return host;
        }

        // Where a modulator actually lands: its strongest route in the matrix, or the
        // filter it is wired to by the fixed amounts. One that goes nowhere says so.
        string LfoDest(int src)
        {
            int best = -1; double mag = 0.02;
            for (int d = 0; d < DestNames.Length; d++)
            {
                double a = Math.Abs(G($"mtx{src}_{d}") - 0.5) * 2;
                if (a > mag) { mag = a; best = d; }
            }
            string fixedId = src == 2 ? "fil1lfo" : "fil2lfo";
            double fixedAmt = Math.Abs(G(fixedId) - 0.5) * 2;
            if (fixedAmt > mag) return $"→ Filter {(src == 2 ? 1 : 2)}";
            return best < 0 ? "unrouted" : "→ " + DestNames[best];
        }
        string EnvDest(int src)
        {
            var hits = new List<string>(2);
            for (int d = 0; d < DestNames.Length; d++)
                if (Math.Abs(G($"mtx{src}_{d}") - 0.5) * 2 > 0.02) hits.Add(DestNames[d]);
            if (src == 1)
            {
                if (Math.Abs(G("fil1env") - 0.5) * 2 > 0.02 && !hits.Contains("Cutoff")) hits.Add("Cutoff");
                else if (Math.Abs(G("fil2env") - 0.5) * 2 > 0.02 && !hits.Contains("Cutoff")) hits.Add("Cutoff");
            }
            if (src == 0) return "→ Amp";
            return hits.Count == 0 ? "unrouted" : "→ " + string.Join(" · ", hits);
        }
        // How many cycles of an LFO fill the shape window: about two seconds of it, or
        // four bars' worth when it is locked to the grid.
        double LfoCycles(int lfo)
        {
            string pre = lfo == 0 ? "lfo1" : "lfo2";
            int div = SyncDiv(G(pre + "sync"));
            if (div > 0) { double[] beats = { 0, 4, 2, 1, 0.5, 0.25, 0.125, 0.0625 }; return Math.Clamp(16.0 / beats[div] / 4.0, 0.5, 16); }
            return Math.Clamp(ExpMap(G(pre + "rate"), 0.05, 20.0) * 2.0, 0.5, 16);
        }
        int RouteCount()
        {
            int n = 0;
            for (int s = 0; s < SrcNames.Length; s++)
                for (int d = 0; d < DestNames.Length; d++)
                    if (Math.Abs(G($"mtx{s}_{d}") - 0.5) > 0.01) n++;
            return n;
        }
        int MacroDest(int m) => Math.Clamp((int)Math.Round(G($"mac{m}dest") * (MacroDests.Length - 1)), 0, MacroDests.Length - 1);
        bool MacroOn(int m) => Math.Abs(G($"mac{m}amt") - 0.5f) > 0.01f;
        int MacroAssigned() { int n = 0; for (int m = 0; m < 8; m++) if (MacroOn(m)) n++; return n; }
        static string Plural(int n, string one, string many) => n + " " + (n == 1 ? one : many);

        // ---- the wheels rail ------------------------------------------------------
        Control Wheel(string id, double def, bool spring)
        {
            var w = new PerformWheel { Default = def, Spring = spring, VerticalAlignment = VerticalAlignment.Stretch, Norm = G(id), HorizontalAlignment = HorizontalAlignment.Center };
            w.ValueChanged += v => { SetP(id, (float)v); Touch(); };
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
            bendVal.Text = centred ? $"±{range} st" : NotaNum.Unit(semis, "+0.0;−0.0;0", "st");
            bendVal.Foreground = centred ? TextSecondary : AccentBright;
            modVal.Text = NotaNum.Pct(G("modwheel"));
            modVal.Foreground = G("modwheel") > 1e-3f ? AccentBright : TextSecondary;
        });
        static Grid Pair(Control a, Control b)
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

        // ---- Osc tab --------------------------------------------------------------
        // Both oscillators own a bank and a warp mode. Two chip strips per oscillator
        // would not fit, so ONE strip across the top carries them for whichever oscillator
        // is selected — the 1 / 2 pick leads it, and clicking an oscillator's name picks it
        // too. The pick and the bank names say what they are, so they go without captions. Under it: the table window on the left, the two oscillator rows on the
        // right, and a line of drag values for the sub and the unison stack.
        var oscTableHosts = new Panel[2];
        var oscWarpHosts = new Panel[2];
        for (int o = 0; o < 2; o++)
        {
            string pre = o == 0 ? "osc1" : "osc2";
            oscTableHosts[o] = new Panel { Children = { Chips(o == 0 ? "table" : "osc2table", Banks) } };
            oscWarpHosts[o] = new Panel { Children = { Chips(pre + "warpmode", WarpModes) } };
        }
        var oscPick = Segments(new[] { "1", "2" }, () => osel, iv => { osel = iv; Touch(); }, out var oscPickSync);
        readouts.Add(oscPickSync);
        ToolTip.SetTip(oscPick, "The oscillator whose table and warp mode this strip edits");
        var oscTableStack = new Panel();
        foreach (var h in oscTableHosts) oscTableStack.Children.Add(h);
        var oscWarpStack = new Panel();
        foreach (var h in oscWarpHosts) oscWarpStack.Children.Add(h);
        var oscStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { oscPick, oscTableStack, Cap("WARP"), oscWarpStack },
        };

        var oscGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("40,*,*,*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var oscNames = new TextBlock[2];
        for (int o = 0; o < 2; o++)
        {
            int ov = o;
            string pre = o == 0 ? "osc1" : "osc2";
            string nm = o == 0 ? "OSC 1" : "OSC 2";
            int row = o * 2;
            if (o > 0) { var rule = Rule(); rule.Margin = new Thickness(0, 1); Grid.SetRow(rule, 1); Grid.SetColumnSpan(rule, 7); oscGrid.Children.Add(rule); }
            var name = Section(nm);
            oscNames[o] = name;
            var head = new StackPanel
            {
                Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand),
                Children = { name, Toggle("", pre + "on", () => G(pre + "on") >= 0.5f ? "on" : "off") },
            };
            head.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(head).Properties.IsLeftButtonPressed) return;
                osel = ov; Touch();
            };
            void Cell(Control c, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); oscGrid.Children.Add(c); }
            Cell(head, 0);
            // Position is what the modulators reach for on this synth, so it wears teal.
            Cell(PKnob(o == 0 ? "position" : "osc2position", "POSITION", nm + " Position", FrameText, 34, 42, Teal), 1);
            Cell(PKnob(o == 0 ? "warp" : "osc2warp", "WARP", nm + " Warp", Pct, 34, 42), 2);
            Cell(PKnob(pre + "level", "LEVEL", nm + " Level", Pct, 34, 42), 3);
            Cell(PKnob(pre + "oct", "OCT", nm + " Octave", Oct, 34, 42), 4);
            Cell(PKnob(pre + "semi", "SEMI", nm + " Semi", Semi, 34, 42), 5);
            Cell(PKnob(pre + "detune", "FINE", nm + " Fine", Cents, 34, 42, Teal), 6);
        }
        applyOsc = () =>
        {
            for (int o = 0; o < 2; o++)
            {
                oscTableHosts[o].IsVisible = oscWarpHosts[o].IsVisible = o == osel;
                oscNames[o].Foreground = o == osel ? AccentBright : TextPrimary;
            }
        };

        // The sub and the unison stack are drag values, as the almanac draws them: a caps
        // label over a mono readout, dragged up or down, double-clicked back to default.
        var subUni = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in new Control[]
        {
            Cap("SUB"), Chips("subwave", SubWaves),
            DragValue("sublevel", "LEVEL", "Sub Level", Pct),
            DragValue("suboct", "OCT", "Sub Octave", SubOct, 1f / 4f),
            new Border { Width = 1, Margin = new Thickness(2, 3), Background = NotaPalette.GraphBorder },
            Cap("UNI"),
            DragValue("unison", "VOICES", "Unison Voices", v => NotaNum.Str(Voices(v), "0"), 1f / 6f),
            DragValue("unidetune", "DETUNE", "Unison Detune", Detune, 0, Teal),
            DragValue("unispread", "SPREAD", "Unison Spread", Pct, 0, Teal),
        }) subUni.Children.Add(c);
        var subUniRow = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 3, 0, 0), Child = subUni,
        };

        // The table window carries its own caption: which bank, which frame, and what is
        // steering the position.
        var tableCap = Mono("", TextTertiary, NotaType.Axis);
        var frameCap = Mono("", TextPrimary, NotaType.Axis);
        var posMod = Mono("", Teal, NotaType.Axis);
        readouts.Add(() =>
        {
            tableCap.Text = $"TABLE {osel + 1} · {Banks[Sel(osel == 0 ? "table" : "osc2table", 4)].ToUpperInvariant()}";
            frameCap.Text = "frame " + FrameText(G(osel == 0 ? "position" : "osc2position"));
            int best = -1; double mag = 0.02;
            for (int s = 0; s < SrcNames.Length; s++)
            {
                double a = Math.Abs(G($"mtx{s}_2") - 0.5) * 2;
                if (a > mag) { mag = a; best = s; }
            }
            posMod.Text = best < 0 ? "no modulation" : SrcNames[best].ToLowerInvariant() + " → pos";
            posMod.Foreground = best < 0 ? TextDisabled : Teal;
        });
        var tableOverlay = new StackPanel
        {
            Margin = new Thickness(7, 5), VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left, Spacing = 0, IsHitTestVisible = false,
            Children = { tableCap, frameCap, posMod },
        };
        stack.TopInset = 26;   // the three caption lines the overlay puts in that corner
        var tableWindow = new Panel { Width = 128, Children = { stack, tableOverlay } };
        var oscMid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
        oscMid.Children.Add(tableWindow);
        Grid.SetColumn(oscGrid, 1); oscMid.Children.Add(oscGrid);
        var oscBodyGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 3 };
        var oscRows = new Control[] { oscStrip, oscMid, subUniRow };
        for (int i = 0; i < oscRows.Length; i++) { Grid.SetRow(oscRows[i], i); oscBodyGrid.Children.Add(oscRows[i]); }
        var oscBody = new Border { Padding = new Thickness(5, 3), Child = oscBodyGrid };

        // ---- Filter tab -----------------------------------------------------------
        // Both filters share one column of controls; picking 1 or 2 swaps which set is on
        // screen — a knob is bound to one param index for its life.
        var filtCells = new Panel[5];
        var filtStack = new Control[2, 5];
        string[] filtIds = { "freq", "reso", "env", "lfo", "key" };
        string[] filtLbl = { "Freq", "Reso", "Env", "LFO", "Key" };
        var filtKnobRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"), VerticalAlignment = VerticalAlignment.Center };
        for (int k = 0; k < 5; k++)
        {
            filtCells[k] = new Panel();
            Grid.SetColumn(filtCells[k], k);
            filtKnobRow.Children.Add(filtCells[k]);
        }
        for (int f = 0; f < 2; f++)
        {
            for (int k = 0; k < 5; k++)
            {
                // Filter 1's cutoff and resonance kept the ids the first Aurora shipped.
                string id = f == 0
                    ? k switch { 0 => "cutoff", 1 => "resonance", _ => "fil1" + filtIds[k] }
                    : "fil2" + filtIds[k];
                Func<float, string> fmt = k switch { 0 => Hz, 1 => Pct, 4 => Pct, _ => Bip };
                Control cell = PKnob(id, filtLbl[k].ToUpperInvariant(), $"Filter {f + 1} {filtLbl[k]}", fmt, 24, 33, k >= 2 ? Teal : null);
                cell.IsVisible = false;
                filtStack[f, k] = cell;
                filtCells[k].Children.Add(cell);
            }
        }
        var filtTypeHosts = new Panel[2];
        var filtSlopeHosts = new Panel[2];
        for (int f = 0; f < 2; f++)
        {
            string pre = f == 0 ? "fil1" : "fil2";
            filtTypeHosts[f] = new Panel { Children = { Chips(pre + "type", FiltTypes) } };
            filtSlopeHosts[f] = new Panel
            {
                Children =
                {
                    Chips(pre + "slope", new[] { "12 dB", "24 dB" }, () => G(pre + "slope") >= 0.5f ? 1 : 0,
                        iv => { SetP(pre + "slope", iv); Touch(); }),
                },
            };
        }
        var filtPick = Segments(new[] { "1", "2" }, () => fsel, iv => { fsel = iv; Touch(); }, out var filtPickSync);
        readouts.Add(filtPickSync);
        var filtTypeStack = new Panel();
        foreach (var h in filtTypeHosts) filtTypeStack.Children.Add(h);
        var filtSlopeStack = new Panel();
        foreach (var h in filtSlopeHosts) filtSlopeStack.Children.Add(h);
        filtSlopeStack.HorizontalAlignment = HorizontalAlignment.Right;

        var filtHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 5 };
        var filtHeadCells = new Control[] { Cap("FILTER"), filtPick, filtSlopeStack };
        for (int i = 0; i < filtHeadCells.Length; i++) { Grid.SetColumn(filtHeadCells[i], i); filtHead.Children.Add(filtHeadCells[i]); }

        Control RouteRow(string title, string id)
        {
            var chips = Chips(id, Routes, fill: true);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), ColumnSpacing = 4 };
            g.Children.Add(Cap(title));
            Grid.SetColumn(chips, 1); g.Children.Add(chips);
            return g;
        }
        var modeChips = Chips("filseries", new[] { "Parallel", "Series" }, () => G("filseries") >= 0.5f ? 1 : 0,
            iv => { SetP("filseries", iv); Touch(); }, fill: true);
        var modeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), ColumnSpacing = 4 };
        modeRow.Children.Add(Cap("MODE"));
        Grid.SetColumn(modeChips, 1); modeRow.Children.Add(modeChips);

        // No "ROUTING" caption: three rows named OSC 1 / OSC 2 / SUB over F1 · F2 · 1+2 ·
        // Dry say what they are, and the line it would cost is the line MODE needs.
        var routing = new StackPanel
        {
            Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children = { RouteRow("OSC 1", "routeosc1"), RouteRow("OSC 2", "routeosc2"), RouteRow("SUB", "routesub"), modeRow },
        };
        var routingFrame = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 3, 0, 0), Child = routing,
        };
        var filtRight = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 3, Width = 186 };
        var filtRightRows = new Control[] { filtHead, filtTypeStack, filtKnobRow, routingFrame };
        for (int i = 0; i < filtRightRows.Length; i++) { Grid.SetRow(filtRightRows[i], i); filtRight.Children.Add(filtRightRows[i]); }

        applyFilter = () =>
        {
            string pre = fsel == 0 ? "fil1" : "fil2";
            string freq = fsel == 0 ? "cutoff" : "fil2freq", res = fsel == 0 ? "resonance" : "fil2reso";
            filtGraph.Target($"FILTER {fsel + 1}", P(freq), P(res), Sel(pre + "type", 5));
            filtGraph.Refresh();
            for (int f = 0; f < 2; f++)
            {
                filtTypeHosts[f].IsVisible = filtSlopeHosts[f].IsVisible = f == fsel;
                for (int k = 0; k < 5; k++) filtStack[f, k].IsVisible = f == fsel;
            }
        };
        var filtGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 7 };
        filtGrid.Children.Add(filtGraph);
        Grid.SetColumn(filtRight, 1); filtGrid.Children.Add(filtRight);
        var filtBody = new Border { Padding = new Thickness(7, 5), Child = filtGrid };

        // ---- Env tab --------------------------------------------------------------
        Control EnvPanel(string title, Func<string> route, VoltEnv view, string[] ids, Func<string> summary)
        {
            view.Target(title, P(ids[0]), P(ids[1]), P(ids[2]), P(ids[3]));
            string[] stage = { "A", "D", "S", "R" };
            Func<float, string>[] fmt = { v => Secs(v, 0.001, 2.0), v => Secs(v, 0.002, 3.0), Pct, v => Secs(v, 0.002, 4.0) };
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
            var dest = Mono("", Teal, NotaType.Axis);
            readouts.Add(() => dest.Text = route());
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 5, Height = 11 };
            head.Children.Add(Cap(title));
            Grid.SetColumn(dest, 1); head.Children.Add(dest);
            Grid.SetColumn(sum, 2); head.Children.Add(sum);
            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 4 };
            g.Children.Add(head);
            Grid.SetRow(view, 1); g.Children.Add(view);
            Grid.SetRow(cells, 2); g.Children.Add(cells);
            return g;
        }
        string AdsrSum(string a, string d, string s, string r)
            => $"{Secs(G(a), 0.001, 2.0)} · {Secs(G(d), 0.002, 3.0)} · {Pct(G(s))} · {Secs(G(r), 0.002, 4.0)}";
        var envPanel1 = EnvPanel("ENV 1", () => "→ Amp", env1, new[] { "attack", "decay", "sustain", "release" },
            () => AdsrSum("attack", "decay", "sustain", "release"));
        var envPanel2 = EnvPanel("ENV 2", () => EnvDest(1), env2, new[] { "env2attack", "env2decay", "env2sustain", "env2release" },
            () => AdsrSum("env2attack", "env2decay", "env2sustain", "env2release"));
        var env2Host = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(6, 0, 0, 0), Child = envPanel2,
        };
        var envGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        envGrid.Children.Add(envPanel1);
        Grid.SetColumn(env2Host, 1); envGrid.Children.Add(env2Host);
        var envBody = new Border { Padding = new Thickness(7, 5), Child = envGrid };

        // ---- LFO tab --------------------------------------------------------------
        var lfoGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("48,Auto,*,*,*,Auto"), ColumnSpacing = 5,
            RowDefinitions = new RowDefinitions("*,*"),
        };
        for (int l = 0; l < 2; l++)
        {
            string pre = l == 0 ? "lfo1" : "lfo2";
            string nm = l == 0 ? "LFO 1" : "LFO 2";
            int src = l + 2;
            if (l > 0) { var rule = Rule(); Grid.SetRow(rule, 1); Grid.SetColumnSpan(rule, 6); lfoGrid.Children.Add(rule); }
            void Cell(Control c, int col) { Grid.SetRow(c, l); Grid.SetColumn(c, col); lfoGrid.Children.Add(c); }
            Cell(Section(nm), 0);
            Cell(Chips(pre + "shape", ShapeNames), 1);
            string rateId = pre + "rate", syncId = pre + "sync";
            Cell(PKnob(rateId, "RATE", nm + " Rate", v => SyncDiv(G(syncId)) > 0 ? SyncText(G(syncId)) : SlowHz(ExpMap(v, 0.05, 20)), 28, 46), 2);
            Cell(PKnob(pre + "depth", "DEPTH", nm + " Depth", Pct, 28, 46, Teal), 3);
            Cell(PKnob(syncId, "SYNC", nm + " Sync", SyncText, 28, 46), 4);
            var dest = Mono("", Teal, NotaType.Axis);
            dest.Width = 58; dest.TextAlignment = TextAlignment.Right;
            readouts.Add(() => dest.Text = LfoDest(src));
            Cell(dest, 5);
        }
        // The shape window follows whichever LFO you last touched — the pick doubles as
        // the label, so the picture is never anonymous.
        int lfoShown = 0;
        var lfoShapeCap = Cap("SHAPE · LFO 1");
        var lfoShapeSum = Mono("", TextSecondary, NotaType.Axis);
        var lfoPick = Segments(new[] { "1", "2" }, () => lfoShown, iv => { lfoShown = iv; Touch(); }, out var lfoPickSync);
        readouts.Add(lfoPickSync);
        readouts.Add(() =>
        {
            string pre = lfoShown == 0 ? "lfo1" : "lfo2";
            lfoShapeCap.Text = $"SHAPE · LFO {lfoShown + 1}";
            lfoStrip.Set(Sel(pre + "shape", 4), LfoCycles(lfoShown), G(pre + "depth"));
            lfoShapeSum.Text = $"{ShapeNames[Sel(pre + "shape", 4)]} · "
                + (SyncDiv(G(pre + "sync")) > 0 ? SyncText(G(pre + "sync")) : SlowHz(ExpMap(G(pre + "rate"), 0.05, 20)))
                + $" · depth {Pct(G(pre + "depth"))}";
        });
        var lfoShapeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 6, Height = 40 };
        var lfoShapeCells = new Control[] { lfoShapeCap, lfoPick, lfoStrip, lfoShapeSum };
        for (int i = 0; i < lfoShapeCells.Length; i++) { Grid.SetColumn(lfoShapeCells[i], i); lfoShapeRow.Children.Add(lfoShapeCells[i]); }
        lfoStrip.Margin = new Thickness(0, 5);
        var lfoShapeFrame = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Child = lfoShapeRow,
        };
        var lfoOuter = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        lfoOuter.Children.Add(lfoGrid);
        Grid.SetRow(lfoShapeFrame, 1); lfoOuter.Children.Add(lfoShapeFrame);
        var lfoBody = new Border { Padding = new Thickness(7, 3), Child = lfoOuter };

        // ---- Mod tab --------------------------------------------------------------
        var mtxIdx = new int[SrcNames.Length, DestNames.Length];
        for (int s = 0; s < SrcNames.Length; s++)
            for (int d = 0; d < DestNames.Length; d++) mtxIdx[s, d] = I($"mtx{s}_{d}");
        var matrix = new VoltMatrix(engine, track, mtxIdx, SrcNames, DestNames) { VerticalAlignment = VerticalAlignment.Stretch };
        matrix.Changed += Touch;
        readouts.Add(matrix.Refresh);
        var modHint = new TextBlock
        {
            Text = "drag a cell — up adds, down subtracts · double-click clears", FontSize = NotaType.Axis,
            Foreground = TextDisabled, VerticalAlignment = VerticalAlignment.Center,
        };
        var modGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 1 };
        modGrid.Children.Add(matrix);
        Grid.SetRow(modHint, 1); modGrid.Children.Add(modHint);
        var modBody = new Border { Padding = new Thickness(7, 3), Child = modGrid };

        // ---- FX tab ---------------------------------------------------------------
        Control FxBlock(string title, string onId, string[] modes, string modeId, params Control[] knobs)
        {
            var chips = Chips(modeId, modes, fill: true);
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var cap = Cap(title, NotaPalette.TextStrong);
            head.Children.Add(cap);
            var sw = Toggle("", onId);
            Grid.SetColumn(sw, 1); head.Children.Add(sw);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            foreach (var k in knobs) row.Children.Add(k);
            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 4 };
            g.Children.Add(head);
            Grid.SetRow(chips, 1); g.Children.Add(chips);
            Grid.SetRow(row, 2); g.Children.Add(row);
            var box = new Border
            {
                Background = NotaPalette.SurfaceRaised, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Control, Padding = new Thickness(6, 5), Child = g,
            };
            readouts.Add(() =>
            {
                bool on = G(onId) >= 0.5f;
                box.Background = on ? NotaPalette.SurfaceRaised : NotaPalette.BgSunken;
                box.BorderBrush = on ? BorderDef : NotaPalette.GraphBorder;
                cap.Foreground = on ? NotaPalette.TextStrong : TextDisabled;
            });
            return box;
        }
        var fxGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
        var fxBlocks = new[]
        {
            FxBlock("DRIVE", "fxdriveon", DriveModes, "fxdrivemode",
                PKnob("fxdrive", "AMOUNT", "Drive", Pct, 32, 48),
                PKnob("fxtone", "TONE", "Drive Tone", Bip, 28, 44)),
            FxBlock("CHORUS", "fxchoruson", ChorusVoices, "fxchorusvoices",
                PKnob("fxchorus", "AMOUNT", "Chorus", Pct, 32, 48),
                PKnob("fxchorusrate", "RATE", "Chorus Rate", ChorusHz, 28, 44)),
            FxBlock("REVERB", "fxreverbon", ReverbModes, "fxreverbmode",
                PKnob("fxreverb", "AMOUNT", "Reverb", Pct, 32, 48),
                PKnob("fxreverbsize", "SIZE", "Reverb Size", Pct, 28, 44)),
        };
        for (int i = 0; i < fxBlocks.Length; i++) { Grid.SetColumn(fxBlocks[i], i); fxGrid.Children.Add(fxBlocks[i]); }
        var fxBody = new Border { Padding = new Thickness(7, 5), Child = fxGrid };

        // ---- the tab strip --------------------------------------------------------
        var bodies = new Control[] { oscBody, filtBody, envBody, lfoBody, modBody, fxBody };
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var clearAll = TextButton("Clear all");
        clearAll.Margin = new Thickness(0, 0, 6, 0);
        ToolTip.SetTip(clearAll, "Zero every route in the matrix");
        clearAll.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(clearAll).Properties.IsLeftButtonPressed) return;
            matrix.ClearAll(); Touch(); e.Handled = true;
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

        Action showRailTab = () => { };
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
            // The matrix and the macros are one subject: opening Mod brings the macros up
            // beside it, the way the almanac draws that state.
            if (tab == 4 && railTab != 1) { railTab = 1; showRailTab(); return; }
            Touch();
        };
        var tabHost = new Panel { MinHeight = BodyH - TabH };
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · Global --------------------------------------------------------
        var monoChips = Chips("mono", new[] { "Poly", "Mono" }, () => G("mono") >= 0.5f ? 1 : 0, iv => { SetP("mono", iv); Touch(); });
        monoChips.HorizontalAlignment = HorizontalAlignment.Right;
        var monoRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        monoRow.Children.Add(Cap("VOICES"));
        Grid.SetColumn(monoChips, 1); monoRow.Children.Add(monoChips);

        var bendChips = StopChips("bendrange", BendNames, BendStops);
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
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 7),
        };
        meterBlock.Children.Add(meter);
        Grid.SetColumn(meterScale, 1); meterBlock.Children.Add(meterScale);

        var globalBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        var gainCell = PKnob("gain", "GAIN", "Gain", Pct, 38, 46);
        var panCell = PKnob("outpan", "PAN", "Out Pan", PanText, 30, 42);
        globalBottom.Children.Add(gainCell);
        Grid.SetColumn(panCell, 1); globalBottom.Children.Add(panCell);
        Grid.SetColumn(meterBlock, 2); globalBottom.Children.Add(meterBlock);

        var globalTop = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(8, 5, 8, 0),
            Children =
            {
                monoRow,
                bendRow,
                Slider("POS 1", "position", () => FrameText(G("position")), labelW: 42, valueW: 42),
                Slider("CUTOFF", "cutoff", () => Hz(G("cutoff")), labelW: 42, valueW: 42),
                Slider("UNISON", "unidetune", () => Detune(G("unidetune")), labelW: 42, valueW: 42),
            },
        };
        var globalGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        globalGrid.Children.Add(globalTop);
        var globalBottomFrame = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(8, 4, 8, 4), Padding = new Thickness(0, 3, 0, 0), Child = globalBottom,
        };
        Grid.SetRow(globalBottomFrame, 1); globalGrid.Children.Add(globalBottomFrame);

        // ---- rail · Macros --------------------------------------------------------
        // Three to a page, as the almanac draws them: a value knob, an amount knob and the
        // target it names. The dashed strip under them names the next page and whether
        // anything on it is in use; clicking it turns the page (right-click turns back).
        const int MacrosPerPage = 3;
        int macroPage = 0;
        int MacroPages() => (8 + MacrosPerPage - 1) / MacrosPerPage;
        var macroBoxes = new Border[8];
        var macroList = new StackPanel { Spacing = 3 };
        for (int m = 0; m < 8; m++)
        {
            int mv = m;
            string destId = $"mac{mv}dest";
            var name = new TextBlock
            {
                FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.KnobLabelTracking,
                Foreground = NotaPalette.TextStrong, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var dest = new TextBlock
            {
                FontSize = NotaType.Axis, Foreground = Teal, Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand), TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var nums = Mono("", TextSecondary, NotaType.Axis);
            ToolTip.SetTip(dest, "Click for the next target, right-click for the previous");
            void Step(int dir)
            {
                int n = MacroDests.Length;
                Begin(destId);
                SetP(destId, ((MacroDest(mv) + dir % n + n) % n) / (float)(n - 1));
                End(destId);
                Touch();
            }
            dest.PointerPressed += (_, e) =>
            {
                var pt = e.GetCurrentPoint(dest).Properties;
                if (pt.IsLeftButtonPressed) { Step(+1); e.Handled = true; }
                else if (pt.IsRightButtonPressed) { Step(-1); e.Handled = true; }
            };
            if (I(destId) is var dpi and >= 0) MidiLearn.Bind(dest, MidiTarget.PluginParam(track, -1, dpi), $"Macro {mv + 1} Dest");

            var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { name, dest, nums } };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 3 };
            var valKnob = BareKnob($"mac{mv}val", $"Macro {mv + 1}", Pct, 34);
            var amtKnob = BareKnob($"mac{mv}amt", $"Macro {mv + 1} Amount", Bip, 34, Teal);
            row.Children.Add(valKnob);
            Grid.SetColumn(amtKnob, 1); row.Children.Add(amtKnob);
            words.Margin = new Thickness(3, 0, 0, 0);
            Grid.SetColumn(words, 2); row.Children.Add(words);
            var box = new Border
            {
                Background = NotaPalette.SurfaceRaised, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Control, Padding = new Thickness(3, 2), Child = row,
            };
            readouts.Add(() =>
            {
                bool on = MacroOn(mv);
                int d = MacroDest(mv);
                name.Text = on ? $"M{mv + 1} · {MacroWords[d]}" : $"M{mv + 1}";
                name.Foreground = on ? NotaPalette.TextStrong : TextDisabled;
                dest.Text = on ? "→ " + MacroDests[d] : "→ " + MacroDests[d] + " · off";
                dest.Foreground = on ? Teal : TextDisabled;
                nums.Text = $"{Pct(G($"mac{mv}val"))} · {Bip(G($"mac{mv}amt"))}";
                nums.Foreground = on ? TextSecondary : TextDisabled;
                box.Background = on ? NotaPalette.SurfaceRaised : NotaPalette.BgSunken;
                box.BorderBrush = on ? BorderDef : NotaPalette.GraphBorder;
                box.IsVisible = mv / MacrosPerPage == macroPage;
            });
            macroBoxes[m] = box;
            macroList.Children.Add(box);
        }
        var pagerText = new TextBlock
        {
            FontSize = NotaType.Axis, Foreground = TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var pager = new Panel
        {
            Height = 20, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Children =
            {
                new Avalonia.Controls.Shapes.Rectangle
                {
                    Stroke = BorderDef, StrokeThickness = 1, RadiusX = 4, RadiusY = 4,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2.5 },
                },
                pagerText,
            },
        };
        ToolTip.SetTip(pager, "Click for the next macros, right-click for the previous");
        pager.PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(pager).Properties;
            int n = MacroPages();
            if (pt.IsLeftButtonPressed) macroPage = (macroPage + 1) % n;
            else if (pt.IsRightButtonPressed) macroPage = (macroPage + n - 1) % n;
            else return;
            e.Handled = true; Touch();
        };
        readouts.Add(() =>
        {
            int next = (macroPage + 1) % MacroPages();
            int first = next * MacrosPerPage, last = Math.Min(8, first + MacrosPerPage) - 1;
            int used = 0;
            for (int m = first; m <= last; m++) if (MacroOn(m)) used++;
            pagerText.Text = $"M{first + 1} — M{last + 1} · " + (used == 0 ? "free" : $"{used} in use");
            pagerText.Foreground = used == 0 ? TextTertiary : Teal;
        });
        var macroGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 3, Margin = new Thickness(6, 4) };
        macroGrid.Children.Add(macroList);
        Grid.SetRow(pager, 1); macroGrid.Children.Add(pager);

        // ---- the rail's own two tabs ----------------------------------------------
        var railBodies = new Control[] { globalGrid, macroGrid };
        var railCells = new Border[RailTabs.Length];
        var railTexts = new TextBlock[RailTabs.Length];
        var railCount = Mono("", TextTertiary, NotaType.Axis);
        railCount.HorizontalAlignment = HorizontalAlignment.Right;
        railCount.Margin = new Thickness(0, 0, 7, 0);
        readouts.Add(() => railCount.Text = railTab == 1 ? $"{MacroAssigned()} / 8 in use" : "");
        var railTabGrid = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        showRailTab = () =>
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
            Touch();
        };
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
                Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                railTab = iv; showRailTab(); e.Handled = true;
            };
            railCells[i] = cell; railTexts[i] = tb;
            Grid.SetColumn(cell, i); railTabGrid.Children.Add(cell);
        }
        Grid.SetColumn(railCount, 2); railTabGrid.Children.Add(railCount);
        var railHost = new Panel();
        foreach (var b in railBodies) railHost.Children.Add(b);
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(HeaderStrip(railTabGrid));
        Grid.SetRow(railHost, 1); railGrid.Children.Add(railHost);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ---- status strip ---------------------------------------------------------
        string RouteWord(string id) => Routes[Sel(id, 4)];
        string OscWord(int o) => BankWords[Sel(o == 0 ? "table" : "osc2table", 4)];
        string FiltPre() => fsel == 0 ? "fil1" : "fil2";
        string FiltFreqId() => fsel == 0 ? "cutoff" : "fil2freq";
        string FiltResoId() => fsel == 0 ? "resonance" : "fil2reso";

        string Hint() => tab switch
        {
            0 => $"{OscWord(0)} · {OscWord(1)} · sub {SubWaves[Sel("subwave", 3)]}",
            1 => $"{FiltTypes[Sel(FiltPre() + "type", 5)]} {(G(FiltPre() + "slope") >= 0.5f ? "24" : "12")} dB/oct",
            2 => "amp · free",
            3 => $"{(SyncDiv(G("lfo1sync")) > 0 ? SyncText(G("lfo1sync")) : "free")} · {(SyncDiv(G("lfo2sync")) > 0 ? SyncText(G("lfo2sync")) : "free")}",
            4 => Plural(RouteCount(), "route", "routes"),
            _ => $"{(G("fxdriveon") >= 0.5f && G("fxdrive") > 1e-3f ? "drive" : "—")} → "
               + $"{(G("fxchoruson") >= 0.5f && G("fxchorus") > 1e-3f ? "chorus" : "—")} → "
               + $"{(G("fxreverbon") >= 0.5f && G("fxreverb") > 1e-3f ? "reverb" : "—")}",
        };
        string Summary() => tab switch
        {
            0 => $"Osc 1 {OscWord(0)} {FrameText(G("position"))} {Pct(G("osc1level"))} → {RouteWord("routeosc1")} · "
               + $"Osc 2 {OscWord(1)} {FrameText(G("osc2position"))} {Pct(G("osc2level"))} → {RouteWord("routeosc2")} · "
               + $"sub {SubWaves[Sel("subwave", 3)]} {SubOct(G("suboct"))} oct {Pct(G("sublevel"))} · "
               + $"unison ×{Voices(G("unison"))} {Detune(G("unidetune"))} spread {Pct(G("unispread"))}",
            1 => $"Filter {fsel + 1} · {FiltTypes[Sel(FiltPre() + "type", 5)]} "
               + $"{(G(FiltPre() + "slope") >= 0.5f ? "24" : "12")} dB/oct · {Hz(G(FiltFreqId()))} · "
               + $"reso {Pct(G(FiltResoId()))} · env {Bip(G(FiltPre() + "env"))} · lfo {Bip(G(FiltPre() + "lfo"))} · "
               + (G("filseries") >= 0.5f ? "series" : "parallel"),
            2 => $"Env 1 → amp {AdsrSum("attack", "decay", "sustain", "release")} · Env 2 {EnvDest(1)}",
            3 => $"LFO 1 {ShapeNames[Sel("lfo1shape", 4)]} {(SyncDiv(G("lfo1sync")) > 0 ? SyncText(G("lfo1sync")) : SlowHz(ExpMap(G("lfo1rate"), 0.05, 20)))} {LfoDest(2)} · "
               + $"LFO 2 {ShapeNames[Sel("lfo2shape", 4)]} {(SyncDiv(G("lfo2sync")) > 0 ? SyncText(G("lfo2sync")) : SlowHz(ExpMap(G("lfo2rate"), 0.05, 20)))} {LfoDest(3)}",
            4 => RouteCount() == 0 ? "no routes — drag a cell to make one" : Plural(RouteCount(), "route", "routes") + " in the matrix",
            _ => $"Drive {(G("fxdriveon") >= 0.5f ? DriveModes[Sel("fxdrivemode", 3)].ToLowerInvariant() + " " + Pct(G("fxdrive")) : "off")} · "
               + $"chorus {(G("fxchoruson") >= 0.5f ? ChorusVoices[Sel("fxchorusvoices", 3)] + " " + Pct(G("fxchorus")) : "off")} · "
               + $"reverb {(G("fxreverbon") >= 0.5f ? ReverbModes[Sel("fxreverbmode", 3)].ToLowerInvariant() + " " + Pct(G("fxreverb")) : "off")}",
        };
        string Meta()
        {
            string mode = G("mono") >= 0.5f ? "MONO" : "POLY 16";
            return $"{mode} · ±{BendRange(G("bendrange"))} ST · GAIN {Pct(G("gain"))}";
        }
        var statusBar = new Grid { Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusBar.Children.Add(status);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = statusBar,
        };

        // ---- assembly -------------------------------------------------------------
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

        ctx.SetInstLiveViz(() => Refresh(false));
        showRailTab();
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
