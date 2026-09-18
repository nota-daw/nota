// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Operator editor (instrument kind 9): a 4-operator FM
// synth in the 700 × 260 card the almanac draws for it.
//
//   Wheels 56   pitch bend and mod wheel, on screen on every tab — the two hand controls
//               a player reaches for first.
//   Centre      three tabs:
//               Operators   — the four operators as a table: role in words, wave, coarse
//                             ratio, fine detune, level with its dB.
//               Algorithm   — the 11 topologies as sketches, feedback, and the routing
//                             diagram (drag one operator onto another to re-route).
//               Filter · Amp— the subtractive filter's response and the live harmonic
//                             spectrum, with type / freq / reso / key track / volume.
//   Rail 186    Global (FM depth, feedback, tone, glide, bend range, voice mode, volume
//               and the track's meter) or Env (one operator's ADSR, drawn and draggable,
//               with key and velocity tracking).
//
// Brass is a carrier — what reaches the output; teal is a modulator. Everything is a
// plugin param → automation / persist / clone.

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
    public double CardWidth => 700;

    private const double WheelsW = 56, RailW = 186, TabH = 20, StatusH = 18;

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
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        meta.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
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
            status.Text = Summary();
            meta.Text = Meta();
        }
        routing.AlgoPicked += a => { SetP("algo", a / (float)(OperatorTopo.Count - 1)); applyEnv(); Refresh(); };
        ctx.AddDeviceRefresher(SyncSpectrum);

        // ---- shared builders --------------------------------------------------
        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = 8)
        {
            var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c ?? TextSecondary, VerticalAlignment = VerticalAlignment.Center };
            tb.FontFamily = NotaFonts.MonoFamily;
            return tb;
        }

        Control Knob(string id, string name, Func<float, string> fmt, double size = 34, double cellW = 46, IBrush? arc = null)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, size, cellW, arc);

        // A knob for a table cell: the column header carries the label, so the cell is
        // just the knob and its value — two lines instead of three.
        Control TableKnob(string id, string name, Func<float, string> fmt, IBrush? arc = null)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock
            {
                Text = fmt(G(id)), FontSize = NotaType.KnobValue, FontFamily = NotaFonts.MonoFamily,
                Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center,
            };
            var knob = new Nota.App.Knob(G(id), 1.0)
            {
                Accent = true, ArcColor = arc, Default = engine.InstrumentParamDefault(track, pi),
                Width = NotaSize.KnobSecondary, Height = NotaSize.KnobSecondary, VerticalAlignment = VerticalAlignment.Center,
            };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), name);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
                Children = { knob, value },
            };
        }

        Control Chips(string id, string[] names, bool fill = false)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); applyEnv(); Refresh(); }, out var sync, fill: fill);
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
            dragging = () => w.Dragging;
            return w;
        }

        var bendWheel = Wheel("bend", 0.5, true, out _);
        var modWheel = Wheel("modwheel", 0, false, out _);
        var bendVal = Mono("", AccentBright, NotaType.KnobValue);
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

        var wheelsGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 4, Margin = new Thickness(4, 5) };
        var wheelsCap = Cap("WHEELS"); wheelsCap.HorizontalAlignment = HorizontalAlignment.Center;
        wheelsGrid.Children.Add(wheelsCap);
        Grid.SetRow(wheelPair, 1); wheelsGrid.Children.Add(wheelPair);
        Grid.SetRow(wheelLabels, 2); wheelsGrid.Children.Add(wheelLabels);
        Grid.SetRow(wheelVals, 3); wheelsGrid.Children.Add(wheelVals);
        var wheelsPanel = SectionBox(wheelsGrid);
        wheelsPanel.Width = WheelsW;

        // ---- Operators tab ----------------------------------------------------
        // One grid for the whole table — a per-row grid would size its Auto columns on its
        // own content and the headers would drift off the cells they name.
        var opsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("62,Auto,64,64,*"), ColumnSpacing = 6,
            RowDefinitions = new RowDefinitions("Auto,*,*,*,*"),
        };
        void Cell(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); opsGrid.Children.Add(c); }

        var levelHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var dbCap = Cap("dB"); Grid.SetColumn(dbCap, 1);
        levelHead.Children.Add(Cap("LEVEL")); levelHead.Children.Add(dbCap);
        var headCells = new Control[] { Cap("OP"), Cap("WAVE"), Cap("CRS"), Cap("FINE"), levelHead };
        for (int c = 0; c < headCells.Length; c++) Cell(headCells[c], 0, c);

        for (int o = 0; o < 4; o++)
        {
            int op = o;
            string pre = OpId[op];
            var bar = new Border { Width = 2, Height = 18, CornerRadius = NotaRadius.Bar, VerticalAlignment = VerticalAlignment.Center };
            var letter = new TextBlock
            {
                Text = OperatorTopo.Names[op], FontSize = NotaType.DeviceSection, FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var role = new TextBlock
            {
                FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.KnobLabelTracking,
                VerticalAlignment = VerticalAlignment.Center,
            };
            readouts.Add(() =>
            {
                var tgt = OperatorTopo.Algo[AlgoIdx()];
                bool carrier = tgt[op] == 4;
                var ink = carrier ? AccentBright : Teal;
                role.Text = carrier ? "OUT" : "→ " + OperatorTopo.Names[tgt[op]];
                role.Foreground = ink; letter.Foreground = ink; bar.Background = carrier ? Brass : Teal;
            });

            // A hairline under each row, drawn behind its cells across the full width.
            if (op > 0)
            {
                var rule = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0) };
                Grid.SetRow(rule, op + 1); Grid.SetColumnSpan(rule, 5); opsGrid.Children.Add(rule);
            }
            string lid = pre + "level";
            Cell(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
                Children = { bar, letter, role },
            }, op + 1, 0);
            Cell(Chips(pre + "wave", WaveShort), op + 1, 1);
            Cell(TableKnob(pre + "coarse", OperatorTopo.Names[op] + " Coarse", RatioText), op + 1, 2);
            Cell(TableKnob(pre + "fine", OperatorTopo.Names[op] + " Fine", Cents, arc: Teal), op + 1, 3);
            Cell(Slider("", lid, () => Level(G(lid)), labelW: 0, valueW: 42), op + 1, 4);
        }
        var opsBody = new Border { Padding = new Thickness(7, 3), Child = opsGrid };

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

        var algoGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = NotaSpace.DeviceGap };
        algoGrid.Children.Add(tiles);
        Grid.SetRow(fbRow, 1); algoGrid.Children.Add(fbRow);
        Grid.SetRow(routing, 2); algoGrid.Children.Add(routing);
        var algoBody = new Border { Padding = new Thickness(7, 5), Child = algoGrid };

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
            Children = { Cap("KEY TRK"), keyTrkChips, Mono("", TextSecondary, NotaType.KnobValue) },
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
                    BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(0, 4, 0, 0), Child = filtBottom,
                },
            },
        };

        var respHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        var respVal = Mono("", TextSecondary, NotaType.Axis); respVal.HorizontalAlignment = HorizontalAlignment.Right;
        respHead.Children.Add(Cap("RESPONSE"));
        Grid.SetColumn(respVal, 1); respHead.Children.Add(respVal);
        readouts.Add(() => respVal.Text = $"{Hz(G("filfreq"))} · Q {NotaNum.Str(1.0 / Math.Max(0.05, 2.0 - 1.9 * G("filreso")), "0.0")}");

        var specHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        var specNote = Mono("f · 10f · 20f · 30f", NotaPalette.TextAxis, NotaType.Axis); specNote.HorizontalAlignment = HorizontalAlignment.Right;
        specHead.Children.Add(Cap("SPECTRUM"));
        Grid.SetColumn(specNote, 1); specHead.Children.Add(specNote);

        var filtRight = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,66"), RowSpacing = 3 };
        filtRight.Children.Add(respHead);
        Grid.SetRow(filtCurve, 1); filtRight.Children.Add(filtCurve);
        Grid.SetRow(specHead, 2); filtRight.Children.Add(specHead);
        Grid.SetRow(spectrum, 3); filtRight.Children.Add(spectrum);

        var filtGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), ColumnSpacing = 9 };
        filtGrid.Children.Add(filtLeft);
        Grid.SetColumn(filtRight, 1); filtGrid.Children.Add(filtRight);
        var filtBody = new Border { Padding = new Thickness(7, 5), Child = filtGrid };

        // ---- the tab strip ----------------------------------------------------
        var bodies = new Control[] { opsBody, algoBody, filtBody };
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var hint = Mono("", TextTertiary, NotaType.Axis);
        hint.Margin = new Thickness(0, 0, 8, 0);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        void ShowTab()
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
            hint.Text = TabHints[tab];
            Refresh();
        }
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
                tab = iv; ShowTab(); e.Handled = true;
            };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }
        var tabStrip = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        tabStrip.Children.Add(tabRow);
        Grid.SetColumn(hint, 1); tabStrip.Children.Add(hint);

        var tabHost = new Panel();
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · Global ----------------------------------------------------
        var bendChips = Segments(BendNames, () => NearestExact(G("bendrange"), BendStops),
            iv => { SetP("bendrange", BendStops[iv]); Refresh(); }, out var bendSync);
        readouts.Add(bendSync);
        if (I("bendrange") is var bri and >= 0) MidiLearn.Bind(bendChips, MidiTarget.PluginParam(track, -1, bri), "Bend Range");
        var bendRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        bendChips.HorizontalAlignment = HorizontalAlignment.Right;
        bendRow.Children.Add(Cap("BEND"));
        Grid.SetColumn(bendChips, 1); bendRow.Children.Add(bendChips);

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center,
            Children = { Toggle("Mono", "mono"), Toggle("Vel → FM", "veltofm", () => G("veltofm") >= 0.5f ? $"Vel → FM  {NotaNum.Pct(G("veltofm"))}" : "Vel → FM  off") },
        };

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
        var globalBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        globalBottom.Children.Add(Knob("volume", "Volume", v => NotaNum.Pct(v), size: NotaSize.KnobMain, cellW: 52));
        Grid.SetColumn(meterBlock, 1); globalBottom.Children.Add(meterBlock);

        var globalTop = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(8, 6, 8, 0),
            Children =
            {
                Slider("FM", "fmdepth", () => NotaNum.Pct(G("fmdepth") * 2)),
                Slider("FEEDBACK", "feedback", () => NotaNum.Pct(G("feedback"))),
                Slider("TONE", "filfreq", () => Hz(G("filfreq"))),
                Slider("GLIDE", "glide", () => Glide(G("glide"))),
                bendRow,
                modeRow,
            },
        };
        var globalGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        globalGrid.Children.Add(globalTop);
        var globalBottomFrame = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(8, 6, 8, 6), Padding = new Thickness(0, 5, 0, 0), Child = globalBottom,
        };
        Grid.SetRow(globalBottomFrame, 1); globalGrid.Children.Add(globalBottomFrame);

        // ---- rail · Env -------------------------------------------------------
        var opCells = new Border[4];
        var opTexts = new TextBlock[4];
        var opPick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        for (int i = 0; i < 4; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = OperatorTopo.Names[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border
            {
                CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 1), Cursor = new Cursor(StandardCursorType.Hand),
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
            Background = NotaPalette.BgSunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Control, Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right,
            Child = opPick,
        };
        var envRoleText = Mono("", TextSecondary, NotaType.Axis);
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
                stageCells[o, s] = Knob(id, stageNames[s], fmt, cellW: 40);
                stageCells[o, s].IsVisible = false;
                envKnobHosts[s].Children.Add(stageCells[o, s]);
            }
        }

        applyEnv = () =>
        {
            string p = OpId[envOp];
            var tgt = OperatorTopo.Algo[AlgoIdx()];
            bool carrier = tgt[envOp] == 4;
            env.Accent = carrier ? AccentBright : Teal;
            env.Target($"{OperatorTopo.Names[envOp]} ENV",
                (I(p + "atk"), p + "atk"), (I(p + "dec"), p + "dec"), (I(p + "sus"), p + "sus"), (I(p + "rel"), p + "rel"));
            envRoleText.Text = carrier ? "carrier → out" : "mod → " + OperatorTopo.Names[tgt[envOp]];
            envRoleText.Foreground = carrier ? AccentBright : Teal;
            for (int o = 0; o < 4; o++)
                for (int s = 0; s < 4; s++) stageCells[o, s].IsVisible = o == envOp;
            for (int i = 0; i < 4; i++)
            {
                bool on = i == envOp;
                bool carr = tgt[i] == 4;
                opCells[i].Background = on ? (carr ? Brass : Teal) : Brushes.Transparent;
                opTexts[i].Foreground = on ? OnAccent : carr ? AccentBright : Teal;
                opTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
        };
        readouts.Add(() => applyEnv());

        var envGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), RowSpacing = 5, Margin = new Thickness(8, 6),
        };
        var envRows = new Control[]
        {
            envPickRow, env, envStages,
            Slider("KEY→LVL", "keylevel", () => NotaNum.Pct(G("keylevel"))),
            Slider("VEL→LVL", "veltolevel", () => NotaNum.Pct(G("veltolevel"))),
        };
        for (int i = 0; i < envRows.Length; i++) { Grid.SetRow(envRows[i], i); envGrid.Children.Add(envRows[i]); }

        // ---- the rail's own two tabs ------------------------------------------
        var railBodies = new Control[] { globalGrid, envGrid };
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
        string Chain()
        {
            var tgt = OperatorTopo.Algo[AlgoIdx()];
            var parts = new List<string>();
            for (int o = 0; o < 4; o++) parts.Add(tgt[o] == 4 ? $"{OperatorTopo.Names[o]}→out" : $"{OperatorTopo.Names[o]}→{OperatorTopo.Names[tgt[o]]}");
            return string.Join(" · ", parts);
        }
        string Summary() => tab switch
        {
            0 => Chain(),
            1 => $"Algo {AlgoIdx() + 1} of {OperatorTopo.Count} · {OperatorTopo.Desc[AlgoIdx()]} · feedback {NotaNum.Pct(G("feedback"))}",
            _ => $"Filter {FilterNames[Sel("filtype", 3)]} · {Hz(G("filfreq"))} · reso {NotaNum.Pct(G("filreso"))} · key track {NotaNum.Pct(G("filkeytrk"))}",
        };
        string Meta()
        {
            string mode = G("mono") >= 0.5f ? "MONO" : "POLY 12";
            return $"ALGO {AlgoIdx() + 1} · FM {NotaNum.Pct(G("fmdepth") * 2)} · {mode} · {NotaNum.Pct(G("volume"))}";
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
