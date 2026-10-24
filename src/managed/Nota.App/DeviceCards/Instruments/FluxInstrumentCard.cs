// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Flux editor (instrument kind 11): the vector synth that
// listens to a track, 750 × 260 in three pressed-in sections (style guide, spec 038).
//
//   Vector 190   the XY field — four timbre worlds at the corners, the orange dot is the
//                vector, a dashed ring is where Motion and React pull it.
//   React        the sidechain source, a live scope of what the synth hears (envelope,
//                transients, tilt) beside the reaction's own level, Listen and the Target
//                it drives (Filter / Pitch / Space / Vector). With no source React is off:
//                the scope goes flat and Listen, Target and the source menu drop to ink 4.
//   Macros 204   the five one-knob macros (Age, Motion, Filter, Env, Space), then glide,
//                tune and gain; resonance, attack/release and unison follow the vector.
//
// Everything is a plugin param → automation / persist / clone, and the card follows
// automation live.

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
using static Nota.App.InstrumentCardKit;

namespace Nota.App;

internal sealed class FluxInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "Vector";
    public double CardWidth => 750;

    private const double VectorW = 190, MacroW = 204;

    private static readonly string[] Targets = { "Filter", "Pitch", "Space", "Vector" };
    private static readonly string[] RateNames = { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16" };

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
        bool live = engine.InstrumentSidechainSource(track) >= 0;

        void Refresh() { foreach (var r in readouts) r(); }

        // A knob with its value and name under it, short name on the card, the full name for MIDI learn.
        (Control Cell, Knob Knob, TextBlock Value) PKnob(string id, string label, string learn, Func<float, string> fmt,
            double size, double cellW, bool inline = false)
        {
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, Inline = inline, Width = size, Height = size,
                Default = I(id) is var d and >= 0 ? engine.InstrumentParamDefault(track, d) : double.NaN,
            };
            knob.Format = v => fmt((float)v);
            if (I(id) is var pi and >= 0)
            {
                knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
                knob.GestureBegin += () => Begin(id);
                knob.GestureEnd += () => End(id);
                ctx.AddInstFader(pi, knob, value, fmt);
                MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), learn);
            }
            var cell = KnobCell(label, knob, value, cellW);
            cell.VerticalAlignment = VerticalAlignment.Center;
            if (!inline) cell.HorizontalAlignment = HorizontalAlignment.Center;
            return (cell, knob, value);
        }

        // A section's head line: its title, then a note or a read-out on the right.
        Control Head(Control left, Control? right = null)
        {
            var g = new Grid { Height = TabRowH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(left);
            if (right is not null)
            {
                right.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(right, 1); g.Children.Add(right);
            }
            DockPanel.SetDock(g, Dock.Top);
            return g;
        }
        static Control Foot(TextBlock t) { var b = new Border { Height = 22, Child = t }; DockPanel.SetDock(b, Dock.Bottom); return b; }
        static Thickness SectionPadding() => new(SectionPad, 10, SectionPad, 8);

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

        var vecReadout = Value("");
        var vecFoot = Cap("");
        readouts.Add(() =>
        {
            if (!pad.Dragging) pad.SetValue(G("vecx"), G("vecy"));
            pad.SetGhost(effX, effY, Ghost());
            vecReadout.Text = $"x {Coord(G("vecx"))} · y {Coord(G("vecy"))}";
            vecFoot.Text = VectorPulled() ? "dashed — where the reaction pulls"
                : Ghost() ? "dashed — where motion drifts" : "four timbres · drag the point";
        });
        var vecPanel = Pane(new DockPanel { Children = { Head(Title("Vector"), vecReadout), Foot(vecFoot), pad } }, SectionPadding());
        vecPanel.Width = VectorW;

        // ---- React --------------------------------------------------------------
        // Source picker: a raised field that opens the other tracks; the list is read when
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
        var srcText = new TextBlock { FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 };
        var srcChevron = new Glyph(GlyphKind.ChevronDown, 8) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var srcField = new Border
        {
            Height = 22, MinWidth = 104, CornerRadius = NotaRadius.Field, Background = NotaPalette.SurfaceHover,
            Padding = new Thickness(8, 0, 8, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new DockPanel { Children = { srcChevron.Docked(Dock.Right), srcText } },
        };
        srcField.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        ToolTip.SetTip(srcField, "Choose the track Flux listens to");
        void SetSource(int id)
        {
            engine.SetInstrumentSidechainSource(track, id);
            live = engine.InstrumentSidechainSource(track) >= 0;
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

        var reactSub = Cap("the synth listens to a track");
        reactSub.Margin = new Thickness(10, 0, 0, 0);
        var reactHead = new Grid { Height = TabRowH, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        reactHead.Children.Add(Title("React"));
        reactHead.Children.Add(reactSub.At(1));
        reactHead.Children.Add(srcField.At(2));
        DockPanel.SetDock(reactHead, Dock.Top);

        var scope = new FluxReactScope { VerticalAlignment = VerticalAlignment.Stretch, Live = live };

        // Listen: a knob with its value and name under it; all three drop to ink 4 with no source.
        var listenValue = Value(Pct(G("listen")));
        var listenKnob = new Knob(G("listen"), 1.0) { Accent = true, Width = NotaSize.Knob, Height = NotaSize.Knob, HorizontalAlignment = HorizontalAlignment.Center,
            Default = I("listen") is var ld and >= 0 ? engine.InstrumentParamDefault(track, ld) : double.NaN };
        listenKnob.Format = v => Pct((float)v);
        if (I("listen") is var lpi and >= 0)
        {
            listenKnob.ValueChanged += v => { engine.PluginParamSet(track, -1, lpi, (float)v); listenValue.Text = Pct((float)v); Refresh(); };
            listenKnob.GestureBegin += () => Begin("listen");
            listenKnob.GestureEnd += () => End("listen");
            ctx.AddInstFader(lpi, listenKnob, listenValue, Pct);
            MidiLearn.Bind(listenKnob, MidiTarget.PluginParam(track, -1, lpi), "Listen");
        }
        ValueEntry.Attach(listenKnob, "Listen", listenValue);
        var listenLabel = Cap("Listen");
        listenValue.HorizontalAlignment = listenLabel.HorizontalAlignment = HorizontalAlignment.Center;
        listenValue.Margin = new Thickness(0, 2, 0, 0);
        var listenCell = new StackPanel { Width = 46, VerticalAlignment = VerticalAlignment.Center, Children = { listenKnob, listenValue, listenLabel } };

        var targetSeg = Segments(Targets, TargetIdx, iv => { Begin("target"); SetP("target", iv / 3f); End("target"); Refresh(); },
            out var targetSync, fill: true, dim: () => !live);
        readouts.Add(targetSync);
        if (I("target") is var tgi and >= 0) MidiLearn.Bind(targetSeg, MidiTarget.PluginParam(track, -1, tgi), "Target");
        var targetCap = Cap("Target — what the reaction drives");
        var targetHint = Cap("");
        var targetStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { targetCap, targetSeg, targetHint } };
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
            listenKnob.IsDim = !live;
            listenValue.Foreground = live ? TextPrimary : TextDisabled;
            listenLabel.Foreground = live ? TextTertiary : TextDisabled;
            srcText.Text = SourceName(engine.InstrumentSidechainSource(track));
            srcText.Foreground = live ? TextPrimary : TextDisabled;
            srcChevron.Foreground = live ? TextSecondary : TextDisabled;
            targetCap.Foreground = live ? TextTertiary : TextDisabled;
            targetHint.Text = TargetLine();
            targetHint.Foreground = live ? TextSecondary : TextTertiary;
        });

        var reactBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        reactBottom.Children.Add(listenCell);
        reactBottom.Children.Add(targetStack.At(1));
        var reactBody = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        reactBody.Children.Add(scope);
        var bottomHost = new Border
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = reactBottom,
        };
        reactBody.Children.Add(bottomHost.At(0, 1));
        var reactPanel = Pane(new DockPanel { Children = { reactHead, reactBody } }, SectionPadding());

        // ---- Macros -------------------------------------------------------------
        const double MacroCell = 90;
        double Inl = Knob.SizeInline;
        var motion = PKnob("motion", "Motion", "Motion", v => $"{RateNames[RateIdx()]} · {Pct(v)}", Inl, MacroCell, true);
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
        var filter = PKnob("filter", "Filter", "Filter", v => NotaNum.Hz(CutoffHz(v, G("vecx"), G("vecy"))), Inl, MacroCell, true);
        // The cutoff also moves with the vector — keep the readout honest while dragging it.
        readouts.Add(() => { if (!filter.Knob.Dragging) filter.Value.Text = NotaNum.Hz(CutoffHz(G("filter"), G("vecx"), G("vecy"))); });
        readouts.Add(() => { if (!motion.Knob.Dragging) motion.Value.Text = $"{RateNames[RateIdx()]} · {Pct(G("motion"))}"; });
        // Two columns: Age, Filter, Space down the left; Motion, Env on the right.
        var macros = new (Control Cell, int Col, int Row)[]
        {
            (PKnob("age", "Age", "Age", AgeWord, Inl, MacroCell, true).Cell, 0, 0),
            (motion.Cell, 1, 0),
            (filter.Cell, 0, 1),
            (PKnob("env", "Env", "Env", EnvWord, Inl, MacroCell, true).Cell, 1, 1),
            (PKnob("space", "Space", "Space", Pct, Inl, MacroCell, true).Cell, 0, 2),
        };
        var macroGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("27,27,27") };
        foreach (var (cell, col, row) in macros) macroGrid.Children.Add(cell.At(col, row));
        DockPanel.SetDock(macroGrid, Dock.Top);
        Grid Slider(string label, string id, Func<float, string> fmt)
        {
            var row = ParamRow(ctx, idx, id, label, fmt, Refresh, readouts, labelWidth: 35, valueWidth: 40);
            row.Height = 20;
            return row;
        }
        var voiceRows = new StackPanel
        {
            Children =
            {
                Slider("Glide", "glide", GlideT),
                Slider("Tune", "tune", Cents),
                Slider("Gain", "gain", Pct),
            },
        };
        var voiceHost = new Border
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(0, 3, 0, 0), Child = voiceRows,
        };
        DockPanel.SetDock(voiceHost, Dock.Top);
        var macroPanel = Pane(new DockPanel
        {
            Children = { Head(Title("Macros"), Cap("one knob each")), Foot(Cap("adaptive: reso · a/r · unison")), macroGrid, voiceHost, new Panel() },
        }, SectionPadding());
        macroPanel.Width = MacroW;

        // ---- assembly -----------------------------------------------------------
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = SectionGap,
            Margin = new Thickness(0, CardPad, 0, 0),
        };
        root.Children.Add(vecPanel);
        root.Children.Add(reactPanel.At(1));
        root.Children.Add(macroPanel.At(2));

        // Live tick: the scope, the pulled vector, then every readout.
        void Tick()
        {
            int n = engine.InstrumentScope(track, sc);
            live = engine.InstrumentSidechainSource(track) >= 0;
            if (n >= 6)
            {
                float react = live ? sc[3] : 0f;
                effX = sc[4]; effY = sc[5];
                scope.Live = live;
                if (live) scope.Push(sc[0], sc[1], sc[2], react);
            }
            Refresh();
        }
        ctx.SetInstLiveViz(Tick);
        Tick();
        return root;
    }
}
