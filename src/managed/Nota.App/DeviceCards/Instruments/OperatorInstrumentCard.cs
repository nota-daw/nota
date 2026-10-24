// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Operator editor (instrument kind 9): a 4-operator FM
// synth in a 724 × 260 card, three pressed-in sections (style guide), no status line.
//
//   Wheels 76   pitch bend and mod wheel, on screen on every tab — the two hand controls
//               a player reaches for first.
//   Centre      three tabs:
//               Operators   — the four operators as a table: role in words, wave, coarse
//                             ratio, fine detune, level with its dB.
//               Algorithm   — the 11 topologies as sketches, feedback, and the routing
//                             diagram (drag one operator onto another to re-route).
//               Filter · Amp— the subtractive filter's response and the live harmonic
//                             spectrum, with type / freq / reso / key track / volume.
//   Rail 196    Global (FM depth, feedback, tone, glide, bend range, voice mode, volume
//               and the track's meter) or Env (one operator's ADSR, drawn and draggable,
//               with key and velocity tracking).
//
// Ink 1 is a carrier — what reaches the output; a modulator is the quieter grey. Values are
// orange. Everything is a plugin param → automation / persist / clone.

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

internal sealed class OperatorInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "FM";
    public double CardWidth => 724;

    private const double WheelsW = 76, RailW = 196;

    // Coarse ratio table — mirrors OperatorSynth::kRatio.
    private static readonly double[] Ratios = { 0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16 };
    private static readonly string[] WaveShort = { "Sin", "Tri", "Saw", "Sqr" };
    private static readonly string[] FilterNames = { "LP", "HP", "BP" };
    private static readonly string[] KeyTrkNames = { "0", "½", "1" };
    private static readonly string[] BendNames = { "±2", "±5", "±12" };
    private static readonly float[] BendStops = { 1f / 11f, 4f / 11f, 1f };
    private static readonly string[] OpId = { "a", "b", "c", "d" };
    private static readonly string[] TabNames = { "Operators", "Algorithm", "Filter · Amp" };
    private static readonly string[] RailTabs = { "Global", "Env" };
    private static readonly string[] TabHints =
    {
        "wave · ratio · fine · level",
        "11 topologies · feedback on A",
        "type · freq · reso · key track",
    };

    // Engine's perceptual map (OperatorSynth.h): lo * (hi/lo)^v.
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));
    private static string Secs(float v, double lo, double hi) => NotaNum.Time(ExpMap(v, lo, hi));
    private static string Hz(float v) => NotaNum.Hz(ExpMap(v, 60, 18000));
    private static string Glide(float v) => v <= 1e-4f ? "off" : NotaNum.Time(ExpMap(v, 0.005, 1.2));
    private static string Level(float v) => v <= 1e-3f ? "−∞" : NotaNum.Db(AudioMath.LinToDb(v));
    private static string Cents(float v) => NotaNum.Unit((v - 0.5f) * 96, "+0;−0;0", "c");
    private static string RatioText(float v)
    {
        double r = Ratios[Math.Clamp((int)Math.Round(v * 15), 0, 15)];
        return "×" + NotaNum.Str(r, r < 1 ? "0.0#" : "0");
    }
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
        int AlgoIdx() => Math.Clamp((int)Math.Round(G("algo") * (OperatorTopo.Count - 1)), 0, OperatorTopo.Count - 1);

        var readouts = new List<Action>();
        int tab = 0, railTab = 0, envOp = 3;   // Operators · Global · the D carrier
        var held = new int[8];
        var specBins = new float[32];

        var spectrum = new OperatorSpectrumViz { VerticalAlignment = VerticalAlignment.Stretch };
        var routing = new OperatorRoutingViz { VerticalAlignment = VerticalAlignment.Stretch };
        var minis = new OperatorAlgoMini[OperatorTopo.Count];
        var filtCurve = new VoltFilter(engine, track)
        { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 60, FreqRange = (60, 18000) };
        var env = new VoltEnv(engine, track) { MinHeight = 60, VerticalAlignment = VerticalAlignment.Stretch };
        Action applyEnv = () => { };

        void SyncSpectrum()
        {
            int n = engine.InstrumentScope(track, specBins);
            int hn = engine.InstrumentHeldNotes(track, held);
            spectrum.Set(specBins, n, hn > 0 ? NoteName(held[0]) : "—");
        }
        void Refresh()
        {
            int a = AlgoIdx();
            routing.Set(a);
            for (int i = 0; i < minis.Length; i++) minis[i].SetActive(i == a);
            filtCurve.Target("FILTER", (I("filfreq"), "filfreq"), (I("filreso"), "filreso"), Sel("filtype", 3));
            env.Refresh();
            SyncSpectrum();
            foreach (var r in readouts) r();
        }
        routing.AlgoPicked += a => { SetP("algo", a / (float)(OperatorTopo.Count - 1)); applyEnv(); Refresh(); };
        ctx.AddDeviceRefresher(SyncSpectrum);

        // ---- shared builders --------------------------------------------------
        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = NotaText.Label(t), FontSize = NotaType.Caption,
            Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = 8)
        {
            var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c ?? TextSecondary, VerticalAlignment = VerticalAlignment.Center };
            tb.FontFamily = NotaFonts.UiFamily;
            return tb;
        }

        Control Knob(string id, string name, Func<float, string> fmt, double size = 34, double cellW = 46, IBrush? arc = null, bool inline = false)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, size, cellW, arc, inline);

        // A knob for a table cell: the column header carries the label, so the cell is
        // just the knob and its value — two lines instead of three.
        Control TableKnob(string id, string name, Func<float, string> fmt, IBrush? arc = null, bool bipolar = false)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock
            {
                Text = fmt(G(id)), FontSize = NotaType.KnobValue, FontFamily = NotaFonts.UiFamily,
                Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center,
            };
            var knob = new Nota.App.Knob(G(id), 1.0)
            {
                Accent = true, ArcColor = arc, Bipolar = bipolar, Default = engine.InstrumentParamDefault(track, pi),
                Width = NotaSize.Knob, Height = NotaSize.Knob, VerticalAlignment = VerticalAlignment.Center,
            };
            knob.Format = v => fmt((float)v);
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), name);
            ValueEntry.Attach(knob, name, value);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
                Children = { knob, value },
            };
        }

        Control Chips(string id, string[] names, bool fill = false, double padX = 6)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); applyEnv(); Refresh(); }, out var sync, fill: fill, padX: padX);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        Grid Slider(string label, string id, Func<string> text, double labelW = 54, double valueW = 38)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                labelWidth: labelW, valueWidth: valueW);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            return row;
        }

        Control Toggle(string label, string id, Func<string>? live = null)
        {
            var host = Switch(label, () => G(id) >= 0.5f, () => { SetP(id, G(id) >= 0.5f ? 0f : 1f); Refresh(); }, out var sync, liveLabel: live);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(host, MidiTarget.PluginParam(track, -1, pi), label);
            return host;
        }

        // ---- the wheels rail --------------------------------------------------
        Control Wheel(string id, double def, bool spring, out Func<bool> dragging)
        {
            var w = new PerformWheel { Default = def, Spring = spring, VerticalAlignment = VerticalAlignment.Stretch, Norm = G(id) };
            w.ValueChanged += v => { SetP(id, (float)v); Refresh(); };
            w.GestureBegin += () => Begin(id);
            w.GestureEnd += () => End(id);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(w, MidiTarget.PluginParam(track, -1, pi), id);
            readouts.Add(() => { if (!w.Dragging) w.Norm = G(id); });
            if (!spring) ValueEntry.Attach(w, w.EntryField(id == "modwheel" ? "Mod wheel" : id, NotaNum.Pct));   // the bend wheel springs back
            dragging = () => w.Dragging;
            return w;
        }

        var bendWheel = Wheel("bend", 0.5, true, out _);
        var modWheel = Wheel("modwheel", 0, false, out _);
        var bendVal = Mono("", TextPrimary, NotaType.KnobValue);
        var modVal = Mono("", TextSecondary, NotaType.KnobValue);
        bendVal.HorizontalAlignment = modVal.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            int range = BendRange(G("bendrange"));
            double semis = (G("bend") - 0.5) * 2 * range;
            bool centred = Math.Abs(semis) < 0.05;
            bendVal.Text = centred ? $"±{range} st" : NotaNum.Unit(semis, "+0.0;−0.0;0", "st");
            bendVal.Foreground = centred ? TextSecondary : TextPrimary;
            modVal.Text = NotaNum.Pct(G("modwheel"));
            modVal.Foreground = G("modwheel") > 1e-3f ? TextPrimary : TextSecondary;
        });

        var wheelPair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), VerticalAlignment = VerticalAlignment.Stretch };
        bendWheel.HorizontalAlignment = modWheel.HorizontalAlignment = HorizontalAlignment.Center;
        wheelPair.Children.Add(bendWheel);
        Grid.SetColumn(modWheel, 1); wheelPair.Children.Add(modWheel);

        var wheelLabels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var pitchCap = Cap("PITCH"); pitchCap.HorizontalAlignment = HorizontalAlignment.Center;
        var modCap = Cap("MOD"); modCap.HorizontalAlignment = HorizontalAlignment.Center;
        wheelLabels.Children.Add(pitchCap);
        Grid.SetColumn(modCap, 1); wheelLabels.Children.Add(modCap);

        var wheelVals = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        wheelVals.Children.Add(bendVal);
        Grid.SetColumn(modVal, 1); wheelVals.Children.Add(modVal);

        wheelPair.Width = wheelLabels.Width = wheelVals.Width = 60;
        wheelPair.HorizontalAlignment = wheelLabels.HorizontalAlignment = wheelVals.HorizontalAlignment = HorizontalAlignment.Center;
        pitchCap.FontSize = modCap.FontSize = NotaType.KnobLabel;
        var wheelsGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 4 };
        var wheelsCap = Cap("Wheels"); wheelsCap.HorizontalAlignment = HorizontalAlignment.Center;
        wheelsGrid.Children.Add(wheelsCap);
        Grid.SetRow(wheelPair, 1); wheelsGrid.Children.Add(wheelPair);
        Grid.SetRow(wheelLabels, 2); wheelsGrid.Children.Add(wheelLabels);
        Grid.SetRow(wheelVals, 3); wheelsGrid.Children.Add(wheelVals);
        var wheelsPanel = SectionBox(wheelsGrid);
        wheelsPanel.Padding = new Thickness(4, 10, 4, 10);
        wheelsPanel.Width = WheelsW;

        // ---- Operators tab ----------------------------------------------------
        // One grid for the whole table — a per-row grid would size its Auto columns on its
        // own content and the headers would drift off the cells they name.
        var opsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("44,Auto,54,54,*"), ColumnSpacing = 4,
            RowDefinitions = new RowDefinitions("17,*,*,*,*"),
        };
        void Cell(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); opsGrid.Children.Add(c); }

        var levelHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        // Column heads: 10, ink 4, one row over the table.
        static TextBlock Head(string t) => new() { Text = t, FontSize = NotaType.Eyebrow, Foreground = TextDisabled, VerticalAlignment = VerticalAlignment.Center };
        var dbCap = Head("dB"); Grid.SetColumn(dbCap, 1);
        levelHead.Children.Add(Head("Level")); levelHead.Children.Add(dbCap);
        var crsHead = Head("Crs"); crsHead.HorizontalAlignment = HorizontalAlignment.Center;
        var fineHead = Head("Fine"); fineHead.HorizontalAlignment = HorizontalAlignment.Center;
        var headCells = new Control[] { Head("Op"), Head("Wave"), crsHead, fineHead, levelHead };
        for (int c = 0; c < headCells.Length; c++) Cell(headCells[c], 0, c);

        for (int o = 0; o < 4; o++)
        {
            int op = o;
            string pre = OpId[op];
            var bar = new Border { Width = 2, Height = 18, CornerRadius = NotaRadius.Fine, VerticalAlignment = VerticalAlignment.Center };
            var letter = new TextBlock
            {
                Text = OperatorTopo.Names[op], FontSize = NotaType.Body, FontWeight = FontWeight.SemiBold,
                Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center,
            };
            var role = new TextBlock { FontSize = NotaType.Caption, VerticalAlignment = VerticalAlignment.Center };
            readouts.Add(() =>
            {
                var tgt = OperatorTopo.Algo[AlgoIdx()];
                bool carrier = tgt[op] == 4;
                role.Text = carrier ? "out" : "→ " + OperatorTopo.Names[tgt[op]];
                role.Foreground = carrier ? TextPrimary : TextTertiary;
                bar.Background = carrier ? TextPrimary : TextTertiary;
            });

            // A thin rule over each row, drawn behind its cells across the full width.
            var rule = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetRow(rule, op + 1); Grid.SetColumnSpan(rule, 5); opsGrid.Children.Add(rule);
            string lid = pre + "level";
            Cell(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
                Children = { bar, letter, role },
            }, op + 1, 0);
            Cell(Chips(pre + "wave", WaveShort, padX: 5), op + 1, 1);
            Cell(TableKnob(pre + "coarse", OperatorTopo.Names[op] + " Coarse", RatioText), op + 1, 2);
            Cell(TableKnob(pre + "fine", OperatorTopo.Names[op] + " Fine", Cents, bipolar: true), op + 1, 3);
            Cell(Slider("", lid, () => Level(G(lid)), labelW: 0, valueW: 46), op + 1, 4);
        }
        var opsBody = new Border { Padding = new Thickness(0, 4, 0, 0), Child = opsGrid };

        // ---- Algorithm tab ----------------------------------------------------
        // The eleven sketches on one row, then feedback, then the routing diagram — which
        // needs the height, because the deepest topology stacks four operators.
        var tiles = new Grid { ColumnSpacing = 4, Height = 56 };
        for (int i = 0; i < OperatorTopo.Count; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (int i = 0; i < OperatorTopo.Count; i++)
        {
            var mini = new OperatorAlgoMini(i);
            mini.Clicked += a => { SetP("algo", a / (float)(OperatorTopo.Count - 1)); applyEnv(); Refresh(); };
            minis[i] = mini;
            Grid.SetColumn(mini, i); tiles.Children.Add(mini);
        }
        var fbRow = Slider("FEEDBACK", "feedback", () => NotaNum.Pct(G("feedback")));

        var algoGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 6 };
        algoGrid.Children.Add(tiles);
        Grid.SetRow(fbRow, 1); algoGrid.Children.Add(fbRow);
        Grid.SetRow(routing, 2); algoGrid.Children.Add(routing);
        var algoBody = new Border { Padding = new Thickness(0, 6, 0, 0), Child = algoGrid };

        // ---- Filter · Amp tab -------------------------------------------------
        var typeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var typeChips = Chips("filtype", FilterNames);
        typeChips.HorizontalAlignment = HorizontalAlignment.Right;
        typeRow.Children.Add(Cap("FILTER"));
        Grid.SetColumn(typeChips, 1); typeRow.Children.Add(typeChips);

        var keyTrkChips = Chips("filkeytrk", KeyTrkNames);
        var keyTrkCell = new StackPanel
        {
            Spacing = 3, VerticalAlignment = VerticalAlignment.Center,
            Children = { Cap("Key track"), keyTrkChips, Mono("", TextPrimary, NotaType.KnobValue) },
        };
        var keyTrkVal = (TextBlock)keyTrkCell.Children[2];
        readouts.Add(() => keyTrkVal.Text = NotaNum.Pct(G("filkeytrk")));

        var filtBottom = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { Knob("volume", "Volume", v => NotaNum.Pct(v), cellW: 44), Knob("glide", "Glide", Glide, cellW: 44), keyTrkCell },
        };
        var filtLeft = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                typeRow,
                Slider("FREQ", "filfreq", () => Hz(G("filfreq")), labelW: 32),
                Slider("RESO", "filreso", () => NotaNum.Pct(G("filreso")), labelW: 32),
                new Border
                {
                    BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(0, 4, 0, 0), Child = filtBottom,
                },
            },
        };

        var respHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 14 };
        var respVal = Mono("", TextTertiary, NotaType.Eyebrow); respVal.HorizontalAlignment = HorizontalAlignment.Right;
        respHead.Children.Add(Cap("RESPONSE"));
        Grid.SetColumn(respVal, 1); respHead.Children.Add(respVal);
        readouts.Add(() => respVal.Text = $"{Hz(G("filfreq"))} · Q {NotaNum.Str(1.0 / Math.Max(0.05, 2.0 - 1.9 * G("filreso")), "0.0")}");

        var specHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 14 };
        var specNote = Mono("f · 10f · 20f · 30f", TextDisabled, NotaType.Eyebrow); specNote.HorizontalAlignment = HorizontalAlignment.Right;
        specHead.Children.Add(Cap("SPECTRUM"));
        Grid.SetColumn(specNote, 1); specHead.Children.Add(specNote);

        var filtRight = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,60"), RowSpacing = 3 };
        filtRight.Children.Add(respHead);
        Grid.SetRow(filtCurve, 1); filtRight.Children.Add(filtCurve);
        Grid.SetRow(specHead, 2); filtRight.Children.Add(specHead);
        Grid.SetRow(spectrum, 3); filtRight.Children.Add(spectrum);

        var filtGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), ColumnSpacing = 12 };
        filtGrid.Children.Add(filtLeft);
        Grid.SetColumn(filtRight, 1); filtGrid.Children.Add(filtRight);
        var filtBody = new Border { Padding = new Thickness(0, 6, 0, 0), Child = filtGrid };

        // ---- the tab strip ----------------------------------------------------
        var bodies = new Control[] { opsBody, algoBody, filtBody };
        var hint = Mono("", TextTertiary, NotaType.Eyebrow);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        Action syncTabs = () => { };
        void ShowTab()
        {
            syncTabs();
            for (int i = 0; i < TabNames.Length; i++) bodies[i].IsVisible = i == tab;
            hint.Text = TabHints[tab];
            Refresh();
        }
        var tabStrip = SynthInstrumentCard.TabStrip(TabNames, () => tab, i => { tab = i; ShowTab(); }, out var tabSync, hint);
        syncTabs = tabSync;

        var tabHost = new Panel();
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(tabStrip);
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · Global ----------------------------------------------------
        var bendChips = Segments(BendNames, () => NearestExact(G("bendrange"), BendStops),
            iv => { SetP("bendrange", BendStops[iv]); Refresh(); }, out var bendSync);
        readouts.Add(bendSync);
        if (I("bendrange") is var bri and >= 0) MidiLearn.Bind(bendChips, MidiTarget.PluginParam(track, -1, bri), "Bend Range");
        var bendRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        bendChips.HorizontalAlignment = HorizontalAlignment.Right;
        bendRow.Children.Add(Cap("Bend"));
        Grid.SetColumn(bendChips, 1); bendRow.Children.Add(bendChips);

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center,
            Children = { Toggle("Mono", "mono"), Toggle("Vel → FM", "veltofm", () => G("veltofm") >= 0.5f ? $"Vel → FM  {NotaNum.Pct(G("veltofm"))}" : "Vel → FM  off") },
        };

        var meter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) meter.Push(m); });
        // The rail's short meter keeps its three marks stacked, 10 apart.
        var meterScale = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 3; i++)
        {
            var c = Mono(i == 0 ? "0" : i == 1 ? "−12" : "−48", NotaPalette.TextAxis, NotaType.Axis);
            c.LineHeight = 10;
            meterScale.Children.Add(c);
        }
        // The Volume knob sits beside the meter (label and value beside the 22 knob) so the rail fits.
        var meterBlock = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch,
        };
        meterBlock.Children.Add(meter);
        Grid.SetColumn(meterScale, 1); meterBlock.Children.Add(meterScale);
        var globalBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 0) };
        var volCell = Knob("volume", "Volume", v => NotaNum.Pct(v), size: NotaSize.KnobInline, cellW: 90, inline: true);
        volCell.VerticalAlignment = VerticalAlignment.Center;
        globalBottom.Children.Add(volCell);
        Grid.SetColumn(meterBlock, 1); globalBottom.Children.Add(meterBlock);

        var globalTop = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Slider("FM", "fmdepth", () => NotaNum.Pct(G("fmdepth") * 2)),
                Slider("FEEDBACK", "feedback", () => NotaNum.Pct(G("feedback"))),
                Slider("TONE", "filfreq", () => Hz(G("filfreq"))),
                Slider("GLIDE", "glide", () => Glide(G("glide"))),
                Spaced(bendRow, 1),
                modeRow,
            },
        };
        var globalGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        globalGrid.Children.Add(globalTop);
        var globalBottomFrame = new Border
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 5, 0, 0), Child = globalBottom,
        };
        Grid.SetRow(globalBottomFrame, 1); globalGrid.Children.Add(globalBottomFrame);

        // ---- rail · Env -------------------------------------------------------
        var opCells = new Border[4];
        var opTexts = new TextBlock[4];
        var opPick = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < 4; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = OperatorTopo.Names[i], FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border
            {
                Height = NotaSize.Seg, CornerRadius = NotaRadius.Cell, Padding = new Thickness(7, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent, Child = tb,
            };
            chip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
                envOp = iv; applyEnv(); Refresh(); e.Handled = true;
            };
            opCells[i] = chip; opTexts[i] = tb; opPick.Children.Add(chip);
        }
        var opPickHost = new Border
        {
            Background = NotaPalette.Groove, CornerRadius = NotaRadius.Group, Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Right,
            Child = opPick,
        };
        opPickHost.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
        var envRoleText = Mono("", TextSecondary, NotaType.Caption);
        var envPickRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        envPickRow.Children.Add(envRoleText);
        Grid.SetColumn(opPickHost, 1); envPickRow.Children.Add(opPickHost);

        var envStages = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        var envKnobHosts = new Panel[4];
        for (int i = 0; i < 4; i++) { envKnobHosts[i] = new Panel(); Grid.SetColumn(envKnobHosts[i], i); envStages.Children.Add(envKnobHosts[i]); }
        // The four ADSR knobs belong to whichever operator is selected, so they are rebuilt
        // per operator once and swapped — a knob is bound to one param index for its life.
        var stageCells = new Control[4, 4];
        string[] stageIds = { "atk", "dec", "sus", "rel" };
        string[] stageNames = { "Attack", "Decay", "Sustain", "Release" };
        for (int o = 0; o < 4; o++)
        {
            for (int s = 0; s < 4; s++)
            {
                string id = OpId[o] + stageIds[s];
                Func<float, string> fmt = s switch
                {
                    0 => v => Secs(v, 0.001, 4.0),
                    1 => v => Secs(v, 0.002, 6.0),
                    2 => v => Level(v),
                    _ => v => Secs(v, 0.002, 8.0),
                };
                stageCells[o, s] = Knob(id, stageNames[s], fmt, cellW: 42);
                stageCells[o, s].IsVisible = false;
                envKnobHosts[s].Children.Add(stageCells[o, s]);
            }
        }

        applyEnv = () =>
        {
            string p = OpId[envOp];
            var tgt = OperatorTopo.Algo[AlgoIdx()];
            bool carrier = tgt[envOp] == 4;
            env.Accent = NotaPalette.VizInk;
            env.Target($"{OperatorTopo.Names[envOp]} env",
                (I(p + "atk"), p + "atk"), (I(p + "dec"), p + "dec"), (I(p + "sus"), p + "sus"), (I(p + "rel"), p + "rel"));
            envRoleText.Text = carrier ? "carrier → out" : "mod → " + OperatorTopo.Names[tgt[envOp]];
            envRoleText.Foreground = carrier ? TextPrimary : TextTertiary;
            for (int o = 0; o < 4; o++)
                for (int s = 0; s < 4; s++) stageCells[o, s].IsVisible = o == envOp;
            for (int i = 0; i < 4; i++)
            {
                bool on = i == envOp;
                bool carr = tgt[i] == 4;
                opCells[i].Background = on ? NotaPalette.AccentSoft : Brushes.Transparent;
                if (on) opCells[i].BindResource(Border.BoxShadowProperty, "Shadow.Lifted"); else opCells[i].BoxShadow = default;
                opTexts[i].Foreground = on ? AccentBright : carr ? TextSecondary : TextTertiary;
            }
        };
        readouts.Add(() => applyEnv());

        var envGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), RowSpacing = 4, Margin = new Thickness(0, 6, 0, 0),
        };
        var envRows = new Control[]
        {
            envPickRow, env, envStages,
            Slider("Key → lvl", "keylevel", () => NotaNum.Pct(G("keylevel"))),
            Slider("Vel → lvl", "veltolevel", () => NotaNum.Pct(G("veltolevel"))),
        };
        for (int i = 0; i < envRows.Length; i++) { Grid.SetRow(envRows[i], i); envGrid.Children.Add(envRows[i]); }

        // ---- the rail's own two tabs ------------------------------------------
        var railBodies = new Control[] { globalGrid, envGrid };
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

        // ---- assembly ---------------------------------------------------------
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = SectionGap, Margin = new Thickness(0, CardPad, 0, 0) };
        root.Children.Add(wheelsPanel);
        Grid.SetColumn(tabPanel, 1); root.Children.Add(tabPanel);
        Grid.SetColumn(rail, 2); root.Children.Add(rail);

        ctx.SetInstLiveViz(Refresh);
        applyEnv();
        ShowRailTab();
        ShowTab();
        return root;
    }

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        int i = -1, pc = engine.PluginParamCount(trackId, -1);
        for (int k = 0; k < pc; k++) if (engine.PluginParamId(trackId, -1, k) == "mono") { i = k; break; }
        if (i < 0) return null;
        return engine.PluginParamGet(trackId, -1, i) >= 0.5f ? $"{Math.Min(active, 1)}/1" : null;
    }

    // A pressed-in section (style guide).
    private static Decorator SectionBox(Control child) => DeviceCardKit.Section(child);

    private static Control Spaced(Control c, double top) { c.Margin = new Thickness(0, top, 0, 0); return c; }
}
