// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Grain editor (instrument kind 10): the granular sampler
// in a 700 × 260 card of two pressed-in sections (style guide, spec 038).
//
//   Centre      the sample as a graph — the Spray band around the dashed read Position
//               (drag across it to move Position), each held voice's read head and the
//               live grain cloud — under a strip of five tabs, each swapping the row of
//               controls beneath the graph:
//               Grain     — the window shape (Hann · Gauss · Tukey · Tri) as drawn chips,
//                           then size, density and spread.
//               Pitch     — the root (the key that plays the sample as recorded; a drop
//                           takes it from a note in the file name), coarse and fine.
//               Variation — per-grain position, pitch and pan jitter.
//               Filter    — LP / HP / BP, cutoff and resonance, after the cloud.
//               Amp       — the envelope of the whole cloud.
//   Rail 200    Read — Scan / Freeze / Key, then Position, Scan speed and Spray — and the
//               output: volume, dry/wet (the sample itself against the cloud) and the
//               track's meter. On screen on every tab.
//
// Drop a sample from the browser to replace the
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
using static Nota.App.InstrumentCardKit;

namespace Nota.App;

internal sealed class GrainInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "Granular";

    private const double RailW = 200;

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
        Func<string> Hint = () => "";

        // Live state: each voice's read head and the grain cloud, from the engine's scope.
        var heads = new float[8];
        int headN = 0;
        var scope = new float[2 + 8 * 12 * 3];
        var cloud = new float[8 * 12 * 3];
        int grains = 0;

        // ---- the sample -------------------------------------------------------
        var wave = new GrainWaveViz { VerticalAlignment = VerticalAlignment.Stretch };
        long sampleId = -1;
        int rootNote = 60;
        string sampleName = "";
        void LoadPeaks()
        {
            engine.TryGetGrainInfo(track, out var gi);
            rootNote = gi.RootNote;
            if (gi.SampleId == sampleId) return;
            sampleId = gi.SampleId;
            sampleName = gi.SampleId != 0 ? engine.SampleName(gi.SampleId) : "";
            if (gi.SampleId != 0 && engine.TryGetSampleInfo(gi.SampleId, out var si) && si.Channels > 0 && si.Frames > 0)
            {
                var raw = engine.ReadSample(gi.SampleId);
                wave.SetPeaks(Peaks(raw, si.Channels, si.Frames), si.SampleRate > 0 ? (double)si.Frames / si.SampleRate : 0);
            }
            else
            {
                // The procedural default the engine plays until a sample is dropped.
                var (peaks, sec) = DefaultSamplePeaks();
                wave.SetPeaks(peaks, sec);
            }
        }
        LoadPeaks();

        var hint = new TextBlock
        {
            FontSize = NotaType.RowLabel, Foreground = TextTertiary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        void Refresh()
        {
            if (!wave.Dragging) wave.SetState(G("position"), G("spray"), Mode(), GrainsPerSec(G("grainsize"), G("density")));
            wave.SetPlayheads(heads, headN);
            wave.SetGrains(cloud, grains);
            foreach (var r in readouts) r();
            hint.Text = Hint();
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
        // Every knob is a table cell: a short label on the card while MIDI learn and the CV
        // menu keep the parameter's full name.
        Control PKnob(string id, string label, string learn, Func<float, string> fmt,
            double size = 26, double cellW = 68)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, Default = engine.InstrumentParamDefault(track, pi),
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center,
            };
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

        Border Chips(string id, string[] names, string learn, Func<bool>? dim = null)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { Begin(id); SetP(id, iv / (float)(n - 1)); End(id); Refresh(); },
                out var sync, dim: dim);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), learn);
            return seg;
        }

        // The window shape: four drawn cells in a 2 × 2 groove; the chosen one raised, its curve orange.
        Control ShapeChips()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("34,34"), RowDefinitions = new RowDefinitions("22,22"), ColumnSpacing = 2, RowSpacing = 2 };
            var cells = new Border[4];
            var icons = new GrainShapeIcon[4];
            for (int i = 0; i < 4; i++)
            {
                int iv = i;
                var ic = new GrainShapeIcon(i) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border { CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = ic };
                ToolTip.SetTip(c, $"{char.ToUpperInvariant(ShapeWords[i][0])}{ShapeWords[i][1..]} window");
                c.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                    Begin("grainshape"); SetP("grainshape", iv / 3f); End("grainshape"); Refresh(); e.Handled = true;
                };
                cells[i] = c.At(i % 2, i / 2); icons[i] = ic; g.Children.Add(c);
            }
            void Paint()
            {
                int cur = Sel("grainshape", 4);
                for (int i = 0; i < 4; i++)
                {
                    bool on = i == cur;
                    PaintCell(cells[i], on);
                    icons[i].Stroke = on ? AccentBright : TextSecondary;
                    icons[i].InvalidateVisual();
                }
            }
            readouts.Add(Paint); Paint();
            var groove = Groove(g);
            groove.HorizontalAlignment = HorizontalAlignment.Left;
            if (I("grainshape") is var pi and >= 0) MidiLearn.Bind(groove, MidiTarget.PluginParam(track, -1, pi), "Grain Shape");
            return groove;
        }

        static StackPanel Row(params Control[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in items) sp.Children.Add(c);
            return sp;
        }

        // The root: the key that plays the sample at its own pitch — a stepper, since it
        // belongs to the sample (saved with it) rather than being an automatable param.
        Control RootCell()
        {
            void SetRoot(int n) { rootNote = Math.Clamp(n, 0, 127); engine.SetTrackGrainRoot(track, rootNote); Refresh(); }
            var text = Value(SamplerModel.NoteName(rootNote));
            text.HorizontalAlignment = HorizontalAlignment.Center;
            readouts.Add(() => text.Text = SamplerModel.NoteName(rootNote));
            Border Step(GlyphKind g, int d, string tip)
            {
                var b = new Border
                {
                    Width = 20, Height = NotaSize.Seg, CornerRadius = NotaRadius.Cell, Background = NotaPalette.SurfaceHover,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new Glyph(g, 8) { Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
                ToolTip.SetTip(b, tip);
                b.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                    SetRoot(rootNote + d); e.Handled = true;
                };
                return b;
            }
            var box = new Border { Height = NotaSize.Seg, MinWidth = 40, CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent, Child = text };
            box.PointerWheelChanged += (wt, e) => { if (WheelInput.Step(e, wt) is not { } s) return; e.Handled = true; if (s != 0) SetRoot(rootNote + s); };
            // Double-click: the note the file name names, else C4.
            box.DoubleTapped += (_, e) => { SetRoot(SamplerModel.DetectRoot(sampleName) is var d and >= 0 ? d : 60); e.Handled = true; };
            ToolTip.SetTip(box, "The root — the key that plays the sample as recorded. Scroll to step it · double-click takes it from the file name (C4 if none)");
            var stepper = Groove(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2,
                Children = { Step(GlyphKind.Minus, -1, "Root a semitone down"), box, Step(GlyphKind.Plus, +1, "Root a semitone up") },
            });
            var cap = Cap("Root"); cap.HorizontalAlignment = HorizontalAlignment.Center;
            return new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Children = { stepper, cap } };
        }

        // ---- the tabs ---------------------------------------------------------
        var filterChips = Chips("filtype", FilterNames, "Filter Type");
        filterChips.Margin = new Thickness(0, 0, 16, 0);
        var shapes = ShapeChips();
        shapes.Margin = new Thickness(0, 0, 30, 0);
        var bodies = new Control[]
        {
            Row(shapes,
                PKnob("grainsize", "Size", "Grain Size", Size),
                PKnob("density", "Density", "Density", Density),
                PKnob("spread", "Spread", "Spread", Pct)),
            Row(RootCell(),
                PKnob("coarse", "Coarse", "Coarse", Coarse),
                PKnob("fine", "Fine", "Fine", Fine)),
            Row(PKnob("posrand", "Position", "Pos Rand", Pct),
                PKnob("pitchrand", "Pitch", "Pitch Rand", PitchJitter),
                PKnob("panrand", "Pan", "Pan Rand", Pct)),
            Row(filterChips,
                PKnob("filfreq", "Freq", "Filter Freq", Hz),
                PKnob("filreso", "Reso", "Filter Reso", Pct)),
            Row(PKnob("attack", "Attack", "Attack", Attack),
                PKnob("decay", "Decay", "Decay", Decay),
                PKnob("sustain", "Sustain", "Sustain", Pct),
                PKnob("release", "Release", "Release", Release)),
        };

        hint.Margin = new Thickness(0);
        var tabRow = Tabs(TabNames, () => tab, i => { tab = i; showTab(); }, out var tabSync, hint);
        showTab = () =>
        {
            tabSync();
            for (int i = 0; i < TabNames.Length; i++) bodies[i].IsVisible = i == tab;
            Refresh();
        };

        var controls = new Panel();
        foreach (var b in bodies) controls.Children.Add(b);
        var controlsHost = new Border
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Height = 66,
            Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = controls,
        };
        var centreBody = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(0, 7, 0, 0) };
        centreBody.Children.Add(wave);
        centreBody.Children.Add(controlsHost.At(0, 1));
        var tabPanel = Pane(new DockPanel { Children = { tabRow.Docked(Dock.Top), centreBody } }, new Thickness(SectionPad, 10, SectionPad, 8));

        // ---- rail · Read + output ---------------------------------------------
        var modeChips = Chips("scanmode", ModeNames, "Scan Mode");
        modeChips.HorizontalAlignment = HorizontalAlignment.Right;
        ToolTip.SetTip(modeChips, "Scan moves through the file · Freeze holds Position · Key moves it with the note");
        var readHead = new Grid { Height = TabRowH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        readHead.Children.Add(Title("Read"));
        readHead.Children.Add(modeChips.At(1));

        Grid Slider(string label, string id, Func<float, string> fmt, bool bipolar = false, Func<bool>? dim = null)
        {
            var row = ParamRow(ctx, idx, id, label, fmt, Refresh, readouts, labelWidth: 51, valueWidth: 34, bipolar: bipolar, dim: dim);
            row.Height = 24;
            return row;
        }
        var readRows = new StackPanel
        {
            Children =
            {
                Slider("Position", "position", Pct),
                // Scan speed only moves the read position in Scan mode.
                Slider("Scan", "scan", ScanSpeed, bipolar: true, dim: () => Mode() != 0),
                Slider("Spray", "spray", Pct),
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
            var c = Cap(i == 0 ? "0" : i == 1 ? "−12" : "−48", NotaPalette.TextAxis, NotaGraph.AxisSize);
            Grid.SetRow(c, i * 2 + 1); meterScale.Children.Add(c);
        }
        var meterBlock = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 12),
        };
        meterBlock.Children.Add(meter);
        meterBlock.Children.Add(meterScale.At(1));

        var outRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        var volCell = PKnob("volume", "Volume", "Volume", Pct, NotaSize.Knob, 56);
        var mixCell = PKnob("drywet", "Dry/wet", "Dry/Wet", Pct, NotaSize.Knob, 56);
        ToolTip.SetTip(mixCell, "Blend the sample itself (dry) with the grain cloud (wet)");
        outRow.Children.Add(volCell);
        outRow.Children.Add(mixCell.At(1));
        outRow.Children.Add(meterBlock.At(2));
        var outFrame = new Border
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(0, 6, 0, 0), Child = outRow,
        };

        var railBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railBody.Children.Add(readRows);
        railBody.Children.Add(outFrame.At(0, 1));
        var rail = Pane(new DockPanel { Children = { readHead.Docked(Dock.Top), railBody } }, new Thickness(SectionPad, 10, SectionPad, 8));
        rail.Width = RailW;

        string ModeWord() => ModeNames[Mode()].ToLowerInvariant();
        // The line at the right of the tab row: what this tab holds, in a few words.
        Hint = () => tab switch
        {
            0 => $"{ModeWord()} · {ShapeWords[Sel("grainshape", 4)]} · {Size(G("grainsize"))} · {Density(G("density"))}",
            1 => $"{ModeWord()} · root {SamplerModel.NoteName(rootNote)} · {Coarse(G("coarse"))} · {Fine(G("fine"))}",
            2 => $"{ModeWord()} · position · pitch · pan",
            3 => $"{ModeWord()} · {FilterNames[Sel("filtype", 3)]} · {Hz(G("filfreq"))}",
            _ => $"{ModeWord()} · a · d · s · r",
        };

        // ---- assembly ---------------------------------------------------------
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = SectionGap,
            Margin = new Thickness(0, CardPad, 0, 0),
        };
        root.Children.Add(tabPanel);
        root.Children.Add(rail.At(1));

        // Live tick: the read heads and the grain cloud, then every readout.
        void Tick()
        {
            LoadPeaks();
            headN = engine.GrainPlayPositions(track, heads);
            int n = engine.InstrumentScope(track, scope);
            if (n >= 2)
            {
                grains = Math.Clamp((int)scope[1], 0, Math.Min(cloud.Length / 3, (n - 2) / 3));
                Array.Copy(scope, 2, cloud, 0, grains * 3);
            }
            else grains = 0;
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
