// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Grain editor (instrument kind 10): the granular sampler
// in the 700 × 260 card the almanac draws for it.
//
//   Centre      the sample as a graph — the Spray band around the dashed read Position
//               (drag across it to move Position), each held voice's read head and the
//               live grain cloud — under a strip of five tabs, each swapping the row of
//               controls beneath the graph:
//               Grain     — the window shape (Hann · Gauss · Tukey · Tri) as drawn chips,
//                           then size, density and spread.
//               Pitch     — coarse and fine.
//               Variation — per-grain position, pitch and pan jitter.
//               Filter    — LP / HP / BP, cutoff and resonance, after the cloud.
//               Amp       — the envelope of the whole cloud.
//   Rail 186    READ — Scan / Freeze / Key, then Position, Scan speed and Spray — and the
//               output: volume, dry/wet (the sample itself against the cloud) and the
//               track's meter. On screen on every tab.
//   Status 18   the tab read back in words, and the sample, voices and grains live.
//
// Brass is the parameter itself; teal is variation and scatter (spread, spray, the three
// jitters and the grains they make). Drop a sample from the browser to replace the
// procedural default. Everything is a plugin param → automation / persist / clone, and the
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

internal sealed class GrainInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "GRANULAR";

    private const double RailW = 186, TabH = 20, StatusH = 18;

    private static readonly string[] TabNames = { "Grain", "Pitch", "Variation", "Filter", "Amp" };
    private static readonly string[] ModeNames = { "Scan", "Freeze", "Key" };
    private static readonly string[] ShapeWords = { "hann", "gauss", "tukey", "tri" };
    private static readonly string[] FilterNames = { "LP", "HP", "BP" };

    // The engine's own maps (GrainSynth.h), so a readout says what it does.
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));
    private static double SizeSec(float v) => ExpMap(v, 0.004, 0.4);
    private static double Overlap(float v) => 1 + v * 7;
    private static double GrainsPerSec(float size, float density) => Overlap(density) / SizeSec(size);
    private static string Pct(float v) => NotaNum.Pct(v);
    private static string Size(float v) => NotaNum.Time(SizeSec(v));
    private static string Density(float v) => NotaNum.Str(Overlap(v), "0.0") + "×";
    private static string Coarse(float v) => NotaNum.Unit((v - 0.5) * 48, "+0.0;−0.0;0.0", "st");
    private static string Fine(float v) => NotaNum.Unit(Math.Round((v - 0.5) * 200), "+0;−0;0", "ct");
    private static string PitchJitter(float v) => v < 0.0005f ? "0 st" : "±" + NotaNum.Unit(v * 12, "0.0", "st");
    private static string ScanSpeed(float v) => NotaNum.Str((v - 0.5) * 8, "+0.0;−0.0;0.0") + "×";
    private static string Hz(float v) => NotaNum.Hz(ExpMap(v, 60, 18000));
    private static string Attack(float v) => NotaNum.Time(ExpMap(v, 0.001, 3.0));
    private static string Decay(float v) => NotaNum.Time(ExpMap(v, 0.002, 4.0));
    private static string Release(float v) => NotaNum.Time(ExpMap(v, 0.003, 6.0));
    private static string GrRate(double v) => NotaNum.Unit(v, "0", "gr/s");

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        float size = 0.42f, density = 0.6f;
        int pc = engine.PluginParamCount(trackId, -1);
        for (int k = 0; k < pc; k++)
        {
            string id = engine.PluginParamId(trackId, -1, k);
            if (id == "grainsize") size = engine.PluginParamGet(trackId, -1, k);
            else if (id == "density") density = engine.PluginParamGet(trackId, -1, k);
        }
        return GrRate(GrainsPerSec(size, density));
    }

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
        int Mode() => Sel("scanmode", 3);

        var readouts = new List<Action>();
        int tab = 0;
        Action showTab = () => { };

        // Live state: each voice's read head and the grain cloud, from the engine's scope.
        var heads = new float[8];
        int headN = 0;
        var scope = new float[2 + 8 * 12 * 3];
        var cloud = new float[8 * 12 * 3];
        int voices = 0, grains = 0;

        // ---- the sample -------------------------------------------------------
        var wave = new GrainWaveViz { VerticalAlignment = VerticalAlignment.Stretch };
        long sampleId = -1;
        double sampleSec = 0, sampleKHz = 0;
        void LoadPeaks()
        {
            engine.TryGetGrainInfo(track, out var gi);
            if (gi.SampleId == sampleId) return;
            sampleId = gi.SampleId;
            if (gi.SampleId != 0 && engine.TryGetSampleInfo(gi.SampleId, out var si) && si.Channels > 0 && si.Frames > 0)
            {
                var raw = engine.ReadSample(gi.SampleId);
                wave.SetPeaks(Peaks(raw, si.Channels, si.Frames), si.SampleRate > 0 ? (double)si.Frames / si.SampleRate : 0);
                sampleSec = si.SampleRate > 0 ? (double)si.Frames / si.SampleRate : 0;
                sampleKHz = si.SampleRate / 1000.0;
            }
            else
            {
                // The procedural default the engine plays until a sample is dropped.
                var (peaks, sec) = DefaultSamplePeaks();
                wave.SetPeaks(peaks, sec);
                sampleSec = sec; sampleKHz = 0;
            }
        }
        LoadPeaks();

        var hint = new TextBlock
        {
            FontSize = NotaType.Axis, Foreground = TextTertiary, FontFamily = NotaFonts.MonoFamily,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily };

        void Refresh()
        {
            if (!wave.Dragging) wave.SetState(G("position"), G("spray"), Mode(), GrainsPerSec(G("grainsize"), G("density")));
            wave.SetPlayheads(heads, headN);
            wave.SetGrains(cloud, grains);
            foreach (var r in readouts) r();
            hint.Text = Hint();
            status.Text = Summary();
            meta.Text = Meta();
        }

        wave.ValueChanged += v => { SetP("position", (float)v); Refresh(); };
        wave.GestureBegin += () => Begin("position");
        wave.GestureEnd += () => End("position");
        wave.Reset = () =>
        {
            if (I("position") is not (var pi and >= 0)) return;
            Begin("position"); SetP("position", engine.InstrumentParamDefault(track, pi)); End("position");
            Refresh();
        };
        ToolTip.SetTip(wave, "Drag to move the read position · double-click to reset");
        if (I("position") is var wpi and >= 0) MidiLearn.Bind(wave, MidiTarget.PluginParam(track, -1, wpi), "Position");

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

        // Every knob is a table cell: a short label on the card while MIDI learn and the CV
        // menu keep the parameter's full name.
        Control PKnob(string id, string label, string learn, Func<float, string> fmt,
            double size = 26, double cellW = 46, IBrush? arc = null)
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

        Border Chips(string id, string[] names, string learn, Func<bool>? dim = null)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { Begin(id); SetP(id, iv / (float)(n - 1)); End(id); Refresh(); },
                out var sync, dim: dim);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), learn);
            return seg;
        }

        Grid Slider(string label, string id, Func<string> text, bool bipolar = false, bool mod = false, Func<bool>? dim = null)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                bipolar: bipolar, dim: dim, labelWidth: 46, valueWidth: 40, modulation: mod);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), label);
            return row;
        }

        // The window shape: four drawn chips in a 2 × 2 block; the chosen one in Brass Wash
        // with a brass edge and a brass curve.
        Control ShapeChips()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 4, RowSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var cells = new Border[4];
            var icons = new GrainShapeIcon[4];
            for (int i = 0; i < 4; i++)
            {
                int iv = i;
                var ic = new GrainShapeIcon(i) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border
                {
                    Width = 26, Height = 19, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control,
                    Cursor = new Cursor(StandardCursorType.Hand), Child = ic,
                };
                ToolTip.SetTip(c, $"{char.ToUpperInvariant(ShapeWords[i][0])}{ShapeWords[i][1..]} window");
                c.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                    Begin("grainshape"); SetP("grainshape", iv / 3f); End("grainshape"); Refresh(); e.Handled = true;
                };
                Grid.SetColumn(c, i % 2); Grid.SetRow(c, i / 2);
                cells[i] = c; icons[i] = ic; g.Children.Add(c);
            }
            void Paint()
            {
                int cur = Sel("grainshape", 4);
                for (int i = 0; i < 4; i++)
                {
                    bool on = i == cur;
                    cells[i].Background = on ? NotaPalette.AccentSubtle : NotaPalette.BgSunken;
                    cells[i].BorderBrush = on ? Brass : BorderDef;
                    icons[i].Stroke = on ? AccentBright : TextTertiary;
                    icons[i].InvalidateVisual();
                }
            }
            readouts.Add(Paint); Paint();
            if (I("grainshape") is var pi and >= 0) MidiLearn.Bind(g, MidiTarget.PluginParam(track, -1, pi), "Grain Shape");
            return g;
        }

        static StackPanel Row(params Control[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in items) sp.Children.Add(c);
            return sp;
        }

        // ---- the tabs ---------------------------------------------------------
        var filterChips = Chips("filtype", FilterNames, "Filter Type");
        filterChips.Margin = new Thickness(0, 0, 8, 0);
        var shapes = ShapeChips();
        shapes.Margin = new Thickness(0, 0, 8, 0);
        var bodies = new Control[]
        {
            Row(shapes,
                PKnob("grainsize", "SIZE", "Grain Size", Size),
                PKnob("density", "DENSITY", "Density", Density),
                PKnob("spread", "SPREAD", "Spread", Pct, arc: Teal)),
            Row(PKnob("coarse", "COARSE", "Coarse", Coarse),
                PKnob("fine", "FINE", "Fine", Fine)),
            Row(PKnob("posrand", "POSITION", "Pos Rand", Pct, arc: Teal),
                PKnob("pitchrand", "PITCH", "Pitch Rand", PitchJitter, arc: Teal),
                PKnob("panrand", "PAN", "Pan Rand", Pct, arc: Teal)),
            Row(filterChips,
                PKnob("filfreq", "FREQ", "Filter Freq", Hz),
                PKnob("filreso", "RESO", "Filter Reso", Pct)),
            Row(PKnob("attack", "ATTACK", "Attack", Attack),
                PKnob("decay", "DECAY", "Decay", Decay),
                PKnob("sustain", "SUSTAIN", "Sustain", Pct),
                PKnob("release", "RELEASE", "Release", Release)),
        };

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

        var controls = new Panel { Height = NotaSize.ParamCell };
        foreach (var b in bodies) controls.Children.Add(b);
        var controlsHost = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 5, 0, 0), Child = controls,
        };
        var centreBody = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 5, Margin = new Thickness(7, 5, 7, 5) };
        centreBody.Children.Add(wave);
        Grid.SetRow(controlsHost, 1); centreBody.Children.Add(controlsHost);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(centreBody, 1); tabGrid.Children.Add(centreBody);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · READ + output ---------------------------------------------
        var modeChips = Chips("scanmode", ModeNames, "Scan Mode");
        modeChips.HorizontalAlignment = HorizontalAlignment.Right;
        modeChips.Margin = new Thickness(0, 0, 6, 0);
        ToolTip.SetTip(modeChips, "Scan moves through the file · Freeze holds Position · Key moves it with the note");
        var readCap = Cap("READ"); readCap.Margin = new Thickness(8, 0, 0, 0);
        var readHead = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        readHead.Children.Add(readCap);
        Grid.SetColumn(modeChips, 1); readHead.Children.Add(modeChips);

        var readRows = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(8, 7, 8, 0),
            Children =
            {
                Slider("POSITION", "position", () => Pct(G("position"))),
                // Scan speed only moves the read position in Scan mode.
                Slider("SCAN", "scan", () => ScanSpeed(G("scan")), bipolar: true, dim: () => Mode() != 0),
                Slider("SPRAY", "spray", () => Pct(G("spray")), mod: true),
            },
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

        var outRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        var volCell = PKnob("volume", "VOLUME", "Volume", Pct, NotaSize.KnobMain, 50);
        var mixCell = PKnob("drywet", "DRY/WET", "Dry/Wet", Pct, NotaSize.KnobRegular, 46);
        ToolTip.SetTip(mixCell, "Blend the sample itself (dry) with the grain cloud (wet)");
        outRow.Children.Add(volCell);
        Grid.SetColumn(mixCell, 1); outRow.Children.Add(mixCell);
        Grid.SetColumn(meterBlock, 2); outRow.Children.Add(meterBlock);
        var outFrame = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(8, 7, 8, 5), Padding = new Thickness(0, 4, 0, 0), Child = outRow,
        };

        var railBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railBody.Children.Add(readRows);
        Grid.SetRow(outFrame, 1); railBody.Children.Add(outFrame);
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(HeaderStrip(readHead));
        Grid.SetRow(railBody, 1); railGrid.Children.Add(railBody);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ---- status strip -----------------------------------------------------
        string ModeWord() => ModeNames[Mode()].ToLowerInvariant();
        string Read() => Mode() switch
        {
            0 => $"scan {ScanSpeed(G("scan"))} from {Pct(G("position"))}",
            1 => $"freeze at {Pct(G("position"))}",
            _ => $"key from {Pct(G("position"))} at C4",
        };
        string Mix() => G("drywet") >= 0.9995f ? "" : $" · dry/wet {Pct(G("drywet"))}";

        // The line at the right of the tab strip: what this tab holds, in a few words.
        string Hint() => tab switch
        {
            0 => $"{ModeWord()} · {ShapeWords[Sel("grainshape", 4)]} · {Size(G("grainsize"))} · {Density(G("density"))}",
            1 => $"{ModeWord()} · {Coarse(G("coarse"))} · {Fine(G("fine"))}",
            2 => $"{ModeWord()} · position · pitch · pan",
            3 => $"{ModeWord()} · {FilterNames[Sel("filtype", 3)]} · {Hz(G("filfreq"))}",
            _ => $"{ModeWord()} · a · d · s · r",
        };
        string Summary() => tab switch
        {
            0 => $"Window {ShapeWords[Sel("grainshape", 4)]} · size {Size(G("grainsize"))} · density {Density(G("density"))} · spread {Pct(G("spread"))} · {Read()}{Mix()}",
            1 => Math.Abs(G("coarse") - 0.5f) < 0.001f && Math.Abs(G("fine") - 0.5f) < 0.0026f
                ? "Coarse 0 · fine 0 — the grains play at the note's pitch"
                : $"Coarse {Coarse(G("coarse"))} · fine {Fine(G("fine"))} — every grain transposed by it",
            2 => $"Variation: position {Pct(G("posrand"))} · pitch {PitchJitter(G("pitchrand"))} · pan {Pct(G("panrand"))} — per grain",
            3 => $"Filter {FilterNames[Sel("filtype", 3)]} · {Hz(G("filfreq"))} · reso {Pct(G("filreso"))} — after the grain cloud",
            _ => $"Amp {Attack(G("attack"))} · {Decay(G("decay"))} · {Pct(G("sustain"))} · {Release(G("release"))} — the envelope shapes the cloud, not the grain",
        };
        string Meta()
        {
            string sample = sampleId == 0 ? "DEFAULT" : "SAMPLE";
            string rate = sampleKHz > 0 ? " · " + NotaNum.Unit(sampleKHz, "0.#", "kHz") : "";
            return $"{sample} {NotaNum.Time(sampleSec)}{rate} · {voices} VOICES · {grains} GRAINS";
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
            ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap,
            Margin = new Thickness(NotaSpace.DeviceGap),
        };
        body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); body.Children.Add(rail);

        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel
        {
            LastChildFill = true, Background = NotaPalette.Gutter,
            Children = { statusHost, body },
        };

        // Live tick: the read heads and the grain cloud, then every readout.
        void Tick()
        {
            LoadPeaks();
            headN = engine.GrainPlayPositions(track, heads);
            int n = engine.InstrumentScope(track, scope);
            if (n >= 2)
            {
                voices = (int)scope[0];
                grains = Math.Clamp((int)scope[1], 0, Math.Min(cloud.Length / 3, (n - 2) / 3));
                Array.Copy(scope, 2, cloud, 0, grains * 3);
            }
            else { voices = 0; grains = 0; }
            Refresh();
        }
        ctx.SetInstLiveViz(Tick);
        showTab();
        Tick();
        return root;
    }

    // Min/max pairs across the file, one per slice, from interleaved samples.
    private static float[] Peaks(float[] raw, int channels, long frames, int buckets = 360)
    {
        var peaks = new float[buckets * 2];
        for (int b = 0; b < buckets; b++)
        {
            long a = b * frames / buckets, e = (b + 1) * frames / buckets;
            float mn = 0, mx = 0;
            for (long i = a; i < e; i++)
                for (int c = 0; c < channels; c++)
                {
                    long k = i * channels + c;
                    if (k >= raw.Length) break;
                    float v = raw[k];
                    if (v < mn) mn = v; if (v > mx) mx = v;
                }
            peaks[b * 2] = mn; peaks[b * 2 + 1] = mx;
        }
        return peaks;
    }

    // GrainSynth::makeDefaultSample, drawn: a 1.6 s harmonic pad on 110 Hz with a slow drift
    // and a 50 ms fade-in. Rendered at a low rate — enough for its peaks.
    private static (float[] Peaks, double Sec) DefaultSamplePeaks()
    {
        const double sr = 22050, sec = 1.6;
        int n = (int)(sec * sr);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / sr, v = 0;
            for (int k = 1; k <= 6; k++)
            {
                double det = 1.0 + 0.002 * Math.Sin(2 * Math.PI * 0.3 * t * k);
                v += 1.0 / k * Math.Sin(2 * Math.PI * 110 * k * det * t);
            }
            double env = i < 0.05 * sr ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * Math.Min(1.0, i / (0.05 * sr))) : 1.0;
            s[i] = (float)(v * 0.16 * env);
        }
        return (Peaks(s, 1, n), sec);
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
