// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Bass editor (instrument kind 7): a mono-first bass
// synth in a 790 × 260 card (style guide, spec 038): three pressed-in sections.
//
//   Wheels 76   pitch bend and mod wheel (the wheel opens the LFO onto the cutoff), on
//               screen on both tabs.
//   Centre      two tabs:
//               Signal — Osc, Sub and Filter as three rows top to bottom: the main
//                        oscillator's wave, shape, pulse width, tuning and level; the sub's
//                        wave, octave and level with the mix sum; the filter's type and
//                        slope (one row of four), its response as a drag pad, and
//                        cutoff, resonance, drive, env and key amounts.
//               Mod    — the amp and filter envelopes drawn and draggable, and the LFO
//                        with its two destinations and the velocity amounts.
//   Rail 186    Global: voice mode, glide, unison, output drive, bend range, legato,
//               volume, pan and the track's meter.
//
// Orange is a value, greys the curves; there is no status line. Everything is a
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
using static Nota.App.InstrumentCardKit;

namespace Nota.App;

internal sealed class BassInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "Bass synth";
    public double CardWidth => 790;

    private const double WheelsW = 76, RailW = 186, RailLabelW = 42;

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
        var ampEnv = new VoltEnv(engine, track) { ShowTitle = false, VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 80, MinHeight = 50 };
        var filtEnv = new VoltEnv(engine, track) { ShowTitle = false, VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 80, MinHeight = 50 };
        var hint = new TextBlock
        {
            FontSize = NotaType.RowLabel, Foreground = TextDisabled, FontFamily = NotaFonts.UiFamily,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Action showTab = () => { };

        void Refresh()
        {
            filtGraph.Target("FILTER", P("filfreq"), P("filreso"), Sel("filtype", 4));
            filtGraph.Refresh(); ampEnv.Refresh(); filtEnv.Refresh();
            foreach (var r in readouts) r();
            hint.Text = Hint();
        }

        // ---- shared builders --------------------------------------------------
        static Border Rule(Control child, bool top = true) => new()
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = top ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0),
            Child = child,
        };

        // Every knob is a table cell: a short label on the card while MIDI learn and the CV
        // menu keep the parameter's full name. `inline` puts the label and value beside a 22 knob.
        Control PKnob(string id, string label, string learn, Func<float, string> fmt, double cellW = 52, bool inline = false)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0) { Accent = true, Inline = inline, Default = engine.InstrumentParamDefault(track, pi) };
            knob.Format = v => fmt((float)v);
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

        Grid Slider(string label, string id, Func<string> text, double labelW = 0, double valueW = 40, Func<bool>? dim = null)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                dim: dim, labelWidth: labelW, valueWidth: valueW);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), label);
            return row;
        }

        // Waveform cells in a groove with a drawn wave in each; the lit one is raised.
        // `current` lights a cell (−1 for none); a click writes that cell's stop.
        Control WaveChips(string id, int[] icons, float[] stops, Func<int> current)
        {
            int n = icons.Length;
            var cells = new Border[n];
            var marks = new WaveIcon[n];
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < n; i++)
            {
                int iv = i;
                var ic = new WaveIcon(icons[i]) { Width = 18, Height = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border
                {
                    Width = 32, Height = NotaSize.Seg + 2, CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent,
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
                    PaintCell(cells[i], on);
                    marks[i].Stroke = on ? AccentBright : TextSecondary;
                    marks[i].InvalidateVisual();
                }
            }
            readouts.Add(Paint); Paint();
            var host = Groove(row);
            host.HorizontalAlignment = HorizontalAlignment.Left;
            if (I(id) is var pi and >= 0) MidiLearn.Bind(host, MidiTarget.PluginParam(track, -1, pi), id);
            return host;
        }

        // ---- the wheels column ------------------------------------------------
        var bendWheel = Wheel(ctx, idx, "bend", 0.5, true, Refresh, readouts);
        var modWheel = Wheel(ctx, idx, "modwheel", 0, false, Refresh, readouts);
        var bendVal = Value(); var modVal = Value();
        var modCap = Cap("Mod");
        ToolTip.SetTip(modWheel, "Mod wheel — opens the LFO onto the cutoff");
        readouts.Add(() =>
        {
            int range = BendRange(G("bendrange"));
            double semis = (G("bend") - 0.5) * 2 * range;
            bool centred = Math.Abs(semis) < 0.05;
            bendVal.Text = centred ? $"±{NotaNum.Unit(range, "0", "st")}" : NotaNum.Unit(semis, "+0.0;−0.0;0", "st");
            bendVal.Foreground = centred ? TextSecondary : TextPrimary;
            bool modOn = G("modwheel") > 1e-3f;
            modVal.Text = NotaNum.Pct(G("modwheel"));
            modVal.Foreground = modOn ? TextPrimary : TextTertiary;
            // On the Mod tab the wheel is part of what's on screen — it drives the LFO.
            modCap.Foreground = tab == 1 ? TextSecondary : TextTertiary;
        });
        var wheelsPanel = Pane(WheelsColumn(bendWheel, modWheel, bendVal, modVal, modCap: modCap), new Thickness(8, 9, 8, 6));
        wheelsPanel.Width = WheelsW;

        // ---- Signal tab -------------------------------------------------------
        // Osc: the morph oscillator's corners, then shape, pulse width, tuning and level.
        var oscRow = new Grid { ColumnDefinitions = new ColumnDefinitions("38,Auto,*,Auto") };
        {
            var knobs = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    PKnob("oscshape", "Shape", "Osc Shape", Pct),
                    PKnob("oscpw", "PW", "Pulse Width", PulseW),
                    PKnob("oscoctave", "Octave", "Osc Octave", Oct),
                    PKnob("oscsemi", "Semi", "Osc Semi", Semi),
                    PKnob("osclevel", "Level", "Osc Level", Pct),
                },
            };
            oscRow.Children.Add(Title("Osc"));
            oscRow.Children.Add(WaveChips("oscshape", OscIcons, OscStops, () => ShapeBand(G("oscshape")) switch { 0 => 3, 1 => 2, 2 => 0, _ => 1 }).At(1));
            oscRow.Children.Add(knobs.At(3));
        }

        // Sub: its wave, the octave below, level — and what osc + sub add up to.
        var subRow = new Grid { ColumnDefinitions = new ColumnDefinitions("38,Auto,Auto,Auto,Auto,*"), ColumnSpacing = 0 };
        {
            var octChips = Chips("suboctave", new[] { "−1", "−2" });
            var octCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { Cap("Oct"), octChips } };

            var mixBar = new MixBar(engine, track, new[] { I("osclevel"), I("sublevel"), -1 }) { Height = 4 };
            readouts.Add(mixBar.Refresh);
            var mixVal = Value();
            readouts.Add(() =>
            {
                float sum = G("osclevel") + G("sublevel");
                mixVal.Text = sum <= 1e-3f ? "silent" : NotaNum.Db(AudioMath.LinToDb(Math.Min(1f, sum)));
            });
            var mixCell = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0), Children = { Cap("Mix sum"), mixBar, mixVal } };
            var level = PKnob("sublevel", "Level", "Sub Level", Pct, 70, inline: true);
            level.Margin = new Thickness(16, 0, 8, 0);

            subRow.Children.Add(Title("Sub"));
            subRow.Children.Add(WaveChips("subwave", SubIcons, SubStops, () => Sel("subwave", 3)).At(1));
            subRow.Children.Add(octCell.At(2));
            subRow.Children.Add(level.At(3));
            subRow.Children.Add(mixCell.At(5));
        }

        // Filter: type and slope, the response pad, and its five knobs.
        var filtRow = new Grid { ColumnDefinitions = new ColumnDefinitions("112,*,Auto"), ColumnSpacing = 6 };
        {
            var envCap = Cap("");
            readouts.Add(() => envCap.Text = "env " + NotaNum.Str((G("filenv") - 0.5f) * 200, "+0;−0;0"));
            var head = new DockPanel { Children = { envCap.Docked(Dock.Right), Cap("Filter") } };
            var type = Chips("filtype", FilterNames, padX: 4);
            var left = new StackPanel
            {
                Spacing = 3, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    head,
                    type,
                    Chips("filslope", new[] { "12", "24" }, () => G("filslope") >= 0.5f ? 1 : 0, iv => { SetP("filslope", iv); Refresh(); }, fill: true),
                },
            };
            var knobs = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    PKnob("filfreq", "Cutoff", "Filter Freq", Hz, 42),
                    PKnob("filreso", "Reso", "Filter Reso", Pct, 36),
                    PKnob("fildrive", "Drive", "Filter Drive", Pct, 36),
                    PKnob("filenv", "Env", "Filter Env", Bip, 38),
                    PKnob("filkey", "Key", "Filter Key", Pct, 36),
                },
            };
            filtRow.Children.Add(left);
            filtRow.Children.Add(filtGraph.At(1));
            filtRow.Children.Add(knobs.At(2));
        }

        var signalGrid = new Grid { RowDefinitions = new RowDefinitions("56,42,*") };
        signalGrid.Children.Add(oscRow);
        signalGrid.Children.Add(Rule(subRow).At(0, 1));
        var filtHost = Rule(filtRow); filtHost.Padding = new Thickness(0, 2, 0, 0);
        signalGrid.Children.Add(filtHost.At(0, 2));
        var signalBody = new Border { Padding = new Thickness(0, 2, 0, 0), Child = signalGrid };

        // ---- Mod tab ----------------------------------------------------------
        Control EnvPanel(string title, string route, VoltEnv view, string[] ids)
        {
            view.Target(title, P(ids[0]), P(ids[1]), P(ids[2]), P(ids[3]));
            string[] stage = { "A", "D", "S", "R" };
            Func<float, string>[] fmt =
            {
                v => Secs(v, 0.001, 2.0), v => Secs(v, 0.002, 4.0), Pct, v => Secs(v, 0.002, 5.0),
            };
            var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 2 };
            for (int k = 0; k < 4; k++)
            {
                string id = ids[k];
                var fk = fmt[k];
                var v = Value(fk(G(id)));
                v.HorizontalAlignment = HorizontalAlignment.Center;
                readouts.Add(() => v.Text = fk(G(id)));
                var cap = Cap(stage[k]); cap.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new StackPanel { Children = { v, cap } };
                if (I(id) is var pi and >= 0) MidiLearn.Bind(cell, MidiTarget.PluginParam(track, -1, pi), title + " · " + stage[k]);
                cells.Children.Add(cell.At(k));
            }
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = 16, Children = { Cap(title), Cap(route, TextDisabled) } };
            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 4 };
            g.Children.Add(head);
            g.Children.Add(view.At(0, 1));
            g.Children.Add(cells.At(0, 2));
            return g;
        }
        var ampPanel = EnvPanel("Amp env", "→ level", ampEnv, new[] { "attack", "decay", "sustain", "release" });
        var filtEnvPanel = EnvPanel("Filter env", "→ cutoff", filtEnv, new[] { "fattack", "fdecay", "fsustain", "frelease" });

        // LFO: shape, rate and where it lands — cutoff and pitch — then the velocity amounts.
        var lfoDest = Cap("", TextDisabled);
        readouts.Add(() => lfoDest.Text = G("modwheel") > 1e-3f ? "→ cut/pitch · wheel" : "→ cut/pitch");
        var lfoKnobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), VerticalAlignment = VerticalAlignment.Center };
        lfoKnobs.Children.Add(PKnob("lforate", "Rate", "LFO Rate", v => SlowHz(ExpMap(v, 0.05, 30)), 50));
        lfoKnobs.Children.Add(PKnob("fillfo", "→ Flt", "Filter LFO", Bip, 50).At(1));
        lfoKnobs.Children.Add(PKnob("lfopitch", "→ Pit", "LFO → Pitch", LfoPitch, 50).At(2));
        var velRows = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                Slider("Vel → amp", "velamp", () => Pct(G("velamp")), labelW: 58, valueW: 32),
                Slider("Vel → flt", "velfilter", () => Pct(G("velfilter")), labelW: 58, valueW: 32),
            },
        };
        var lfoGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 3 };
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = 16, Children = { Cap("LFO"), lfoDest } };
            lfoGrid.Children.Add(head);
            lfoGrid.Children.Add(Chips("lfowave", LfoNames, fill: true, padX: 1).At(0, 1));
            lfoGrid.Children.Add(lfoKnobs.At(0, 2));
            lfoGrid.Children.Add(velRows.At(0, 3));
        }
        var lfoHost = Rule(lfoGrid, top: false);
        lfoHost.Padding = new Thickness(10, 0, 0, 0);
        lfoHost.Width = 176;

        var modGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto"), ColumnSpacing = 10 };
        modGrid.Children.Add(ampPanel);
        modGrid.Children.Add(filtEnvPanel.At(1));
        modGrid.Children.Add(lfoHost.At(2));
        var modBody = new Border { Padding = new Thickness(0, 6, 0, 0), Child = modGrid };

        // ---- the tab row ------------------------------------------------------
        var bodies = new Control[] { signalBody, modBody };
        var tabRow = Tabs(TabNames, () => tab, i => { tab = i; showTab(); }, out var tabSync, hint);
        showTab = () =>
        {
            for (int i = 0; i < bodies.Length; i++) bodies[i].IsVisible = i == tab;
            tabSync();
            Refresh();
        };
        var tabHost = new Panel();
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabPanel = Pane(new DockPanel { Children = { tabRow.Docked(Dock.Top), tabHost } }, new Thickness(SectionPad, 9, SectionPad, 8));

        // ---- rail · Global ----------------------------------------------------
        var monoChips = Chips("mono", new[] { "Poly", "Mono" }, () => IsMono() ? 1 : 0, iv => { SetP("mono", iv); Refresh(); });
        var globalHead = new DockPanel { Height = 22, Children = { monoChips.Docked(Dock.Right), Title("Global") } };

        var bendChips = Segments(BendNames, () => NearestExact(G("bendrange"), BendStops),
            iv => { SetP("bendrange", BendStops[iv]); Refresh(); }, out var bendSync, padX: 5);
        readouts.Add(bendSync);
        if (I("bendrange") is var bri and >= 0) MidiLearn.Bind(bendChips, MidiTarget.PluginParam(track, -1, bri), "Bend Range");
        var legato = Switch("Legato", () => G("legato") >= 0.5f, () => { Begin("legato"); SetP("legato", G("legato") >= 0.5f ? 0f : 1f); End("legato"); Refresh(); },
            out var legatoSync, dim: () => !IsMono());
        readouts.Add(legatoSync);
        if (I("legato") is var lgi and >= 0) MidiLearn.Bind(legato, MidiTarget.PluginParam(track, -1, lgi), "Legato");
        ToolTip.SetTip(legato, "Slide into a note played over a held one without a new attack");
        legato.HorizontalAlignment = HorizontalAlignment.Left;
        var bendRow = new Grid { ColumnDefinitions = new ColumnDefinitions($"{RailLabelW},Auto"), ColumnSpacing = 8, Height = 24 };
        bendRow.Children.Add(Cap("Bend"));
        bendRow.Children.Add(bendChips.At(1));
        legato.Margin = new Thickness(RailLabelW + 8, -1, 0, 0);

        var meter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) meter.Push(m); });
        var meterScale = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            RowDefinitions = new RowDefinitions(string.Join(",", ScaleRows(0, -12, -48))),
        };
        for (int i = 0; i < 3; i++)
        {
            var c = Cap(i == 0 ? "0" : i == 1 ? "−12" : "−48", TextDisabled, NotaType.Axis + 1);
            meterScale.Children.Add(c.At(0, i * 2 + 1));
        }
        var meterBlock = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 4),
        };
        meterBlock.Children.Add(meter);
        meterBlock.Children.Add(meterScale.At(1));

        var globalBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        globalBottom.Children.Add(PKnob("volume", "Volume", "Volume", Pct, 54));
        globalBottom.Children.Add(PKnob("outpan", "Pan", "Out Pan", PanText, 50).At(1));
        globalBottom.Children.Add(meterBlock.At(2));

        var globalTop = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                globalHead,
                // Glide and legato belong to the mono voice — poly starts every note on its own pitch.
                Slider("Glide", "glide", () => GlideT(G("glide")), labelW: RailLabelW, valueW: 40, dim: () => !IsMono()),
                Slider("Unison", "unison", () => Unison(G("unison")), labelW: RailLabelW, valueW: 40),
                Slider("Drive", "drive", () => Pct(G("drive")), labelW: RailLabelW, valueW: 40),
                bendRow,
                legato,
            },
        };
        var globalBottomFrame = Rule(globalBottom);
        globalBottomFrame.Margin = new Thickness(0, 4, 0, 0);
        globalBottomFrame.Padding = new Thickness(0, 2, 0, 0);
        var rail = Pane(new DockPanel { Children = { globalTop.Docked(Dock.Top), globalBottomFrame } }, new Thickness(SectionPad, 9, SectionPad, 8));
        rail.Width = RailW;

        // The line at the right of the tab row: what this tab holds, in three words.
        string Shape() => ShapeWords[ShapeBand(G("oscshape"))];
        string SubOct() => G("suboctave") >= 0.5f ? "−2" : "−1";
        string FilterWord() => $"{FilterNames[Sel("filtype", 4)]} {(G("filslope") >= 0.5f ? "24" : "12")}";
        string Hint() => tab switch
        {
            0 => $"{Shape()} · {SubWords[Sel("subwave", 3)]} {SubOct()} · {FilterWord()}",
            _ => $"amp · flt · lfo {LfoNames[Sel("lfowave", 5)].ToLowerInvariant()}",
        };

        // ---- assembly ---------------------------------------------------------
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = SectionGap,
            Margin = new Thickness(0, CardPad, 0, 0),
        };
        root.Children.Add(wheelsPanel);
        root.Children.Add(tabPanel.At(1));
        root.Children.Add(rail.At(2));

        ctx.SetInstLiveViz(Refresh);
        showTab();
        return root;
    }

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
