// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Flux editor (instrument kind 11): the vector synth that
// listens to a track, in the 700 × 260 card the almanac draws for it.
//
//   Vector 186   the XY field — four timbre worlds at the corners, the brass dot is the
//                vector, a dashed teal ring is where Motion and React pull it.
//   React        the sidechain source, a live scope of what the synth hears (envelope,
//                transients, tilt) beside the reaction's own level, LISTEN and the TARGET
//                it drives (Filter / Pitch / Space / Vector). With no source React is off:
//                the scope goes flat and LISTEN and TARGET dim.
//   Macros 186   the five one-knob macros (Age, Motion, Filter, Env, Space), then glide,
//                tune and gain; resonance, attack/release and unison follow the vector.
//   Status 18    the patch read back in words — the world blend, or what React is doing.
//
// The four role chromas live only inside the vector field. Brass is the parameter itself;
// teal is the reaction and what receives it. Everything is a plugin param → automation /
// persist / clone, and the card follows automation live.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class FluxInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "VECTOR";
    public double CardWidth => 700;

    private const double SideW = 186, HeadH = 20, StatusH = 18;

    private static readonly string[] Targets = { "Filter", "Pitch", "Space", "Vector" };
    private static readonly string[] RateNames = { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16" };
    private static readonly string[] WorldNames = { "warm", "glass", "moog", "grain" };

    // The engine's worlds and maps (FluxSynth.h), so the FILTER readout says what it does.
    private static readonly (double Cutoff, double Bright)[] Worlds = { (0.42, 0.20), (0.88, 0.95), (0.38, 0.10), (0.55, 0.55) };
    private static double[] Weights(double x, double y) => new[] { (1 - x) * (1 - y), x * (1 - y), (1 - x) * y, x * y };
    private static double CutoffHz(float filter, double x, double y)
    {
        var w = Weights(x, y);
        double cut = 0, bright = 0;
        for (int i = 0; i < 4; i++) { cut += Worlds[i].Cutoff * w[i]; bright += Worlds[i].Bright * w[i]; }
        double n = Math.Clamp(cut * 0.55 + (filter - 0.5) * 0.9 + bright * 0.18, 0.02, 0.99);
        return Math.Clamp(50 * Math.Pow(18000.0 / 50.0, n), 40, 20000);
    }
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));

    private static string AgeWord(float v) => v < 0.12f ? "clean" : v < 0.45f ? "vintage" : v < 0.78f ? "worn" : "broken";
    private static string EnvWord(float v) => v < 0.4f ? "pad" : v < 0.6f ? "pad→pluck" : "pluck";
    private static string GlideT(float v) => v <= 0.001f ? "off" : NotaNum.Time(ExpMap(v, 0.005, 0.6));
    private static string Cents(float v) => NotaNum.Unit(Math.Round((v - 0.5) * 100), "+0;−0;0", "ct");
    private static string Pct(float v) => NotaNum.Pct(v);
    private static string Coord(double v) => NotaNum.Str(v, "0.00");

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
        int TargetIdx() => Math.Clamp((int)Math.Round(G("target") * 3f), 0, 3);
        int RateIdx() => Math.Clamp((int)Math.Round(G("motrate") * 5f), 0, 5);

        var readouts = new List<Action>();
        // Live state from the engine's scope, refreshed on the playhead tick.
        var sc = new float[9];
        double effX = G("vecx"), effY = G("vecy");
        float react = 0;
        bool live = engine.InstrumentSidechainSource(track) >= 0;
        var onsetLog = new Queue<(double T, float Count)>();
        var clock = Stopwatch.StartNew();
        int perBar = 0;

        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily };

        void Refresh()
        {
            foreach (var r in readouts) r();
            status.Text = Summary();
            meta.Text = Meta();
        }

        // ---- shared builders --------------------------------------------------
        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking * 1.5, Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Note(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.KnobLabel, Foreground = c ?? TextDisabled,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        static TextBlock Mono(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.Axis, Foreground = c ?? TextTertiary, FontFamily = NotaFonts.MonoFamily,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        // A knob as a table cell: short label on the card, the full name for MIDI learn.
        (Control Cell, Knob Knob, TextBlock Value) PKnob(string id, string label, string learn, Func<float, string> fmt,
            double size, double cellW, IBrush? arc = null)
        {
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, ArcColor = arc, Width = size, Height = size,
                Default = I(id) is var d and >= 0 ? engine.InstrumentParamDefault(track, d) : double.NaN,
            };
            if (I(id) is var pi and >= 0)
            {
                knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
                knob.GestureBegin += () => Begin(id);
                knob.GestureEnd += () => End(id);
                ctx.AddInstFader(pi, knob, value, fmt);
                MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), learn);
            }
            var cell = KnobCell(label, knob, value, cellW);
            cell.HorizontalAlignment = HorizontalAlignment.Center;
            cell.VerticalAlignment = VerticalAlignment.Center;
            return (cell, knob, value);
        }

        Grid Slider(string label, string id, Func<string> text)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                labelWidth: 34, valueWidth: 38);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), label);
            return row;
        }

        Grid Head(Control left, Control? right = null)
        {
            var g = new Grid { Height = HeadH, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 0) };
            g.Children.Add(left);
            if (right is not null)
            {
                right.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(right, 1); g.Children.Add(right);
            }
            return g;
        }

        // ---- Vector -------------------------------------------------------------
        var pad = new FluxVectorPad { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        pad.ValueChanged += (x, y) => { SetP("vecx", (float)x); SetP("vecy", (float)y); Refresh(); };
        pad.GestureBegin += () => { Begin("vecx"); Begin("vecy"); };
        pad.GestureEnd += () => { End("vecx"); End("vecy"); };
        pad.Reset = () =>
        {
            Begin("vecx"); Begin("vecy");
            if (I("vecx") is var xi and >= 0) SetP("vecx", engine.InstrumentParamDefault(track, xi));
            if (I("vecy") is var yi and >= 0) SetP("vecy", engine.InstrumentParamDefault(track, yi));
            End("vecx"); End("vecy");
            Refresh();
        };
        ToolTip.SetTip(pad, "Drag to blend the four timbres · double-click to reset");
        if (I("vecx") is var vxi and >= 0) MidiLearn.Bind(pad, MidiTarget.PluginParam(track, -1, vxi), "Vector X");

        bool VectorPulled() => live && TargetIdx() == 3;
        bool Ghost() => Math.Abs(effX - G("vecx")) > 0.012 || Math.Abs(effY - G("vecy")) > 0.012;

        var vecReadout = Mono("");
        var vecFoot = Mono("");
        readouts.Add(() =>
        {
            if (!pad.Dragging) pad.SetValue(G("vecx"), G("vecy"));
            pad.SetGhost(effX, effY, Ghost());
            vecReadout.Text = $"x {Coord(G("vecx"))} · y {Coord(G("vecy"))}";
            vecReadout.Foreground = VectorPulled() ? AccentBright : TextSecondary;
            bool pulled = VectorPulled();
            vecFoot.Text = pulled ? "dashed — where the reaction pulls"
                : Ghost() ? "dashed — where motion drifts" : "four timbres · drag the point";
            vecFoot.Foreground = pulled ? NotaPalette.TealBright : TextTertiary;
        });

        var vecGrid = new Grid { RowDefinitions = new RowDefinitions($"{HeadH},*,{StatusH}") };
        vecGrid.Children.Add(HeaderStrip(Head(Cap("VECTOR"), vecReadout)));
        var padHost = new Border { Padding = new Thickness(6), Child = pad };
        Grid.SetRow(padHost, 1); vecGrid.Children.Add(padHost);
        var vecFootHost = FootStrip(vecFoot);
        Grid.SetRow(vecFootHost, 2); vecGrid.Children.Add(vecFootHost);
        var vecPanel = SectionBox(vecGrid);
        vecPanel.Width = SideW;

        // ---- React --------------------------------------------------------------
        // Source picker: a sunken field that opens the other tracks; the list is read when
        // it opens, so a track added since the card was built is there.
        string SourceName(int id)
        {
            if (id < 0) return "No source";
            for (int i = 0; i < engine.TrackCount; i++)
            {
                if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id != id) continue;
                string name = TrackNames.Of(engine, ti);
                string kind = ti.Type switch { 2 => " · return", 3 => " · bus", _ => "" };
                return name + kind;
            }
            return "No source";
        }
        var srcText = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 110 };
        var srcChevron = new Glyph(GlyphKind.ChevronDown, 7) { Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var srcField = new Border
        {
            Height = 15, MinWidth = 92, CornerRadius = NotaRadius.Badge, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 0, 5, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new DockPanel { Children = { Docked(srcChevron, Dock.Right), srcText } },
        };
        ToolTip.SetTip(srcField, "Choose the track Flux listens to");
        void SetSource(int id)
        {
            engine.SetInstrumentSidechainSource(track, id);
            live = engine.InstrumentSidechainSource(track) >= 0;
            onsetLog.Clear(); perBar = 0;
            ctx.NotifyChanged();
            Refresh();
        }
        srcField.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(srcField).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            int cur = engine.InstrumentSidechainSource(track);
            var flyout = new MenuFlyout();
            var none = new MenuItem { Header = "No source", ToggleType = MenuItemToggleType.Radio, IsChecked = cur < 0 };
            none.Click += (_, _) => SetSource(-1);
            flyout.Items.Add(none);
            for (int i = 0; i < engine.TrackCount; i++)
            {
                if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue;
                int id = ti.Id;
                var mi = new MenuItem { Header = $"{i + 1} · {SourceName(id)}", ToggleType = MenuItemToggleType.Radio, IsChecked = id == cur };
                mi.Click += (_, _) => SetSource(id);
                flyout.Items.Add(mi);
            }
            flyout.ShowAt(srcField);
        };

        var reactCap = Cap("REACT");
        var reactSub = Note("the synth listens to a track");
        reactSub.Margin = new Thickness(6, 0, 0, 0);
        var reactHead = new Grid { Height = HeadH, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(8, 0) };
        reactHead.Children.Add(reactCap);
        Grid.SetColumn(reactSub, 1); reactHead.Children.Add(reactSub);
        Grid.SetColumn(srcField, 2); reactHead.Children.Add(srcField);

        var scope = new FluxReactScope { VerticalAlignment = VerticalAlignment.Stretch, Live = live };

        var listen = PKnob("listen", "LISTEN", "Listen", Pct, NotaSize.KnobSecondary, 44, Teal);
        var targetSeg = Segments(Targets, TargetIdx, iv => { Begin("target"); SetP("target", iv / 3f); End("target"); Refresh(); },
            out var targetSync, fill: true, dim: () => !live, padX: 2);
        readouts.Add(targetSync);
        if (I("target") is var tgi and >= 0) MidiLearn.Bind(targetSeg, MidiTarget.PluginParam(track, -1, tgi), "Target");
        var targetHint = Mono("");
        var targetStack = new StackPanel
        {
            Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children = { Cap("TARGET — what the reaction drives"), targetSeg, targetHint },
        };
        string TargetLine() => !live ? "→ assign a source to turn the reaction on" : TargetIdx() switch
        {
            0 => "→ the sidechain opens the filter",
            1 => "→ the sidechain bends the pitch up",
            2 => "→ the sidechain swells the space",
            _ => "→ the sidechain drags the vector in rhythm",
        };
        readouts.Add(() =>
        {
            scope.Live = live;
            listen.Knob.IsDim = !live;
            reactCap.Foreground = live ? NotaPalette.TealBright : TextTertiary;
            reactSub.Foreground = live ? NotaPalette.TextMuted : TextDisabled;
            int src = engine.InstrumentSidechainSource(track);
            srcText.Text = SourceName(src);
            srcText.Foreground = live ? TextPrimary : TextDisabled;
            srcField.Background = live ? NotaPalette.SurfaceRaised : NotaPalette.BgSunken;
            targetHint.Text = TargetLine();
            targetHint.Foreground = live ? NotaPalette.TealBright : TextDisabled;
        });

        var reactBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        reactBottom.Children.Add(listen.Cell);
        Grid.SetColumn(targetStack, 1); reactBottom.Children.Add(targetStack);
        var reactBody = new Grid { RowDefinitions = new RowDefinitions("*,62"), RowSpacing = 5, Margin = new Thickness(7, 5, 7, 4) };
        reactBody.Children.Add(scope);
        var bottomHost = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = reactBottom,
        };
        Grid.SetRow(bottomHost, 1); reactBody.Children.Add(bottomHost);

        var reactGrid = new Grid { RowDefinitions = new RowDefinitions($"{HeadH},*") };
        reactGrid.Children.Add(HeaderStrip(reactHead));
        Grid.SetRow(reactBody, 1); reactGrid.Children.Add(reactBody);
        var reactPanel = SectionBox(reactGrid);

        // ---- Macros -------------------------------------------------------------
        const double MacroSize = 26, MacroCell = 54;
        var motion = PKnob("motion", "MOTION", "Motion", v => $"{RateNames[RateIdx()]} · {Pct(v)}", MacroSize, MacroCell);
        // Click the Motion readout to step its tempo-sync rate.
        motion.Value.Cursor = new Cursor(StandardCursorType.Hand);
        motion.Value.Background = Brushes.Transparent;
        ToolTip.SetTip(motion.Value, "Click to change the Motion sync rate");
        motion.Value.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(motion.Value).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Begin("motrate"); SetP("motrate", (RateIdx() + 1) % 6 / 5f); End("motrate");
            motion.Value.Text = $"{RateNames[RateIdx()]} · {Pct(G("motion"))}";
            Refresh();
        };
        var filter = PKnob("filter", "FILTER", "Filter", v => NotaNum.Hz(CutoffHz(v, G("vecx"), G("vecy"))), MacroSize, MacroCell);
        // The cutoff also moves with the vector — keep the readout honest while dragging it.
        readouts.Add(() => { if (!filter.Knob.Dragging) filter.Value.Text = NotaNum.Hz(CutoffHz(G("filter"), G("vecx"), G("vecy"))); });
        readouts.Add(() => { if (!motion.Knob.Dragging) motion.Value.Text = $"{RateNames[RateIdx()]} · {Pct(G("motion"))}"; });
        var macros = new[]
        {
            PKnob("age", "AGE", "Age", AgeWord, MacroSize, MacroCell).Cell,
            motion.Cell,
            filter.Cell,
            PKnob("env", "ENV", "Env", EnvWord, MacroSize, MacroCell).Cell,
            PKnob("space", "SPACE", "Space", Pct, MacroSize, MacroCell).Cell,
        };
        var macroGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("*,*") };
        for (int i = 0; i < macros.Length; i++)
        {
            Grid.SetColumn(macros[i], i % 3); Grid.SetRow(macros[i], i / 3);
            macroGrid.Children.Add(macros[i]);
        }
        var voiceRows = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Slider("GLIDE", "glide", () => GlideT(G("glide"))),
                Slider("TUNE", "tune", () => Cents(G("tune"))),
                Slider("GAIN", "gain", () => Pct(G("gain"))),
            },
        };
        var macroFoot = Mono("adaptive: reso · a/r · unison", TextDisabled);
        var macroBody = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), Margin = new Thickness(8, 4, 8, 0) };
        macroBody.Children.Add(macroGrid);
        var voiceHost = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 5), Child = voiceRows,
        };
        Grid.SetRow(voiceHost, 1); macroBody.Children.Add(voiceHost);
        var macroFootHost = FootStrip(macroFoot);
        macroFootHost.Margin = new Thickness(-8, 0);
        Grid.SetRow(macroFootHost, 2); macroBody.Children.Add(macroFootHost);

        var macroGridHost = new Grid { RowDefinitions = new RowDefinitions($"{HeadH},*") };
        macroGridHost.Children.Add(HeaderStrip(Head(Cap("MACROS"), Note("one knob each"))));
        Grid.SetRow(macroBody, 1); macroGridHost.Children.Add(macroBody);
        var macroPanel = SectionBox(macroGridHost);
        macroPanel.Width = SideW;

        // ---- status strip -------------------------------------------------------
        string Summary()
        {
            if (!live)
            {
                var w = Weights(G("vecx"), G("vecy"));
                var parts = new List<string>();
                for (int i = 0; i < 4; i++) parts.Add($"{WorldNames[i]} {Pct((float)w[i])}");
                return $"Vector x {Coord(G("vecx"))} · y {Coord(G("vecy"))} → {string.Join(" · ", parts)} · react off";
            }
            string src = SourceName(engine.InstrumentSidechainSource(track));
            string what = TargetIdx() switch
            {
                3 => $"vector x {Coord(G("vecx"))} → {Coord(effX)}",
                0 => $"cutoff +{Pct(react * 0.45f)} of range",
                1 => NotaNum.Unit(react * 3, "+0.0;−0.0;0", "st"),
                _ => $"space +{Pct(react * 0.5f)}",
            };
            string hits = perBar == 1 ? "1 transient per bar" : $"{perBar} transients per bar";
            return $"{src} → {Targets[TargetIdx()].ToLowerInvariant()} · listen {Pct(G("listen"))} · {hits} · {what}";
        }
        string Meta() => $"MOTION {RateNames[RateIdx()]} · TUNE {Cents(G("tune"))} · GAIN {Pct(G("gain"))}";

        var statusBar = new Grid { Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusBar.Children.Add(status);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = statusBar,
        };

        // ---- assembly -----------------------------------------------------------
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = NotaSpace.DeviceGap,
            Margin = new Thickness(NotaSpace.DeviceGap),
        };
        body.Children.Add(vecPanel);
        Grid.SetColumn(reactPanel, 1); body.Children.Add(reactPanel);
        Grid.SetColumn(macroPanel, 2); body.Children.Add(macroPanel);

        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, body } };

        // Live tick: the scope, the pulled vector, transients per bar, then every readout.
        void Tick()
        {
            int n = engine.InstrumentScope(track, sc);
            live = engine.InstrumentSidechainSource(track) >= 0;
            if (n >= 6)
            {
                react = live ? sc[3] : 0f;
                effX = sc[4]; effY = sc[5];
                scope.Live = live;
                if (live) scope.Push(sc[0], sc[1], sc[2], react);
            }
            if (n >= 8 && live)
            {
                double now = clock.Elapsed.TotalSeconds;
                onsetLog.Enqueue((now, sc[7]));
                double bar = 4 * 60.0 / Math.Max(20, engine.Bpm);
                while (onsetLog.Count > 1 && now - onsetLog.Peek().T > bar) onsetLog.Dequeue();
                perBar = (int)Math.Max(0, sc[7] - onsetLog.Peek().Count);
            }
            Refresh();
        }
        ctx.SetInstLiveViz(Tick);
        Tick();
        return root;
    }

    private static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }

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

    // A section's foot line: a hairline over one mono sentence.
    private static Border FootStrip(Control child) => new()
    {
        BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(8, 0), MinHeight = 16, Child = child,
    };
}
