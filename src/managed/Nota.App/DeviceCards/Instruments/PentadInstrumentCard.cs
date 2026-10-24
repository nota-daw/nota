// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Pentad editor (instrument kind 14), 724 × 260 in the
// style guide's frame: an always-visible Wheels section (the round pitch / mod wheels), a
// centre tabbed section (Oscillators / Filter · Amp / Poly Mod) and a right tabbed section
// (Mixer / Output) whose tab row carries the Voice setup menu (polyphony, allocation,
// oversampling, vintage seed …). No status line. The five-voice activity strip and
// the output meters read the instrument's scope telemetry. Every control is a plugin-param,
// so automation / MIDI learn / presets / persistence come for free. BodyOnly — the shared
// shell draws the header (name / preset / A-B / meter).

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class PentadInstrumentCard : IInstrumentCard
{
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush BorderIn = NotaPalette.GraphBorder;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush DimC = NotaPalette.TextDisabled;
    private static readonly IBrush Handle = NotaPalette.TextSecondary;

    private static readonly string[] Feet = { "32′", "16′", "8′", "4′" };
    private static readonly string[] SyncNames = { "8 bar", "4 bar", "2 bar", "1 bar", "1/2", "1/4.", "1/4", "1/8.", "1/4T", "1/8", "1/8T", "1/16", "1/16T", "1/32" };

    public bool BodyOnly => true;
    public string Subtitle => "Analog poly";
    public double CardWidth => 724;

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        int n = engine.PluginParamCount(trackId, -1);
        for (int i = 0; i < n; i++)
            if (engine.PluginParamId(trackId, -1, i) == "polyphony")
                return $"{Math.Max(0, active)}/{Poly(engine.PluginParamGet(trackId, -1, i))}";
        return null;
    }

    private static int Poly(float v) => v < 0.25f ? 5 : v < 0.75f ? 10 : 16;

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        bool On(string id) => G(id) > 0.5f;
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);
        // A discrete edit (click) is one automation gesture, so it records while the transport does.
        void SetP(string id, float v)
        {
            if (I(id) is not (var i and >= 0)) return;
            engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
            engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f));
            engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
        }

        var readouts = new List<Action>();
        var scope = new float[32];
        var filtCurve = new PentadFilterCurve();
        var fEnvCurve = new PentadEnvCurve { Accent = NotaPalette.VizInk };
        var aEnvCurve = new PentadEnvCurve { Accent = NotaPalette.VizInk };
        var voiceStrips = new List<VoiceKeys>();
        var meterL = new PentadLevelBar { Vertical = true, Width = 11, Height = 40 };
        var meterR = new PentadLevelBar { Vertical = true, Width = 11, Height = 40 };
        var sumBar = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
        var sumTxt = MonoText("", NotaType.KnobValue, TxtC);
        var voiceCountTxt = MonoText("", NotaType.KnobValue, Txt2);
        int scN = 0;

        void Refresh()
        {
            filtCurve.Set(G("cutoff"), G("reso"), On("lowcomp")); filtCurve.InvalidateVisual();
            fEnvCurve.Set(G("fattack"), G("fdecay"), G("fsustain"), G("frelease")); fEnvCurve.InvalidateVisual();
            aEnvCurve.Set(G("aattack"), G("adecay"), G("asustain"), On("releaseon") ? G("arelease") : 0); aEnvCurve.InvalidateVisual();
            scN = engine.InstrumentScope(track, scope);
            if (scN >= 24)
            {
                int poly = (int)scope[0];
                foreach (var vs in voiceStrips) vs.Set(poly, scope.AsSpan(8, 16));
                meterL.SetLinear(scope[2]); meterR.SetLinear(scope[3]);
                sumBar.SetLinear(scope[4] * 0.6);
                sumTxt.Text = scope[4] > 1e-4 ? $"{20 * Math.Log10(scope[4] * 0.6):0.0}" : "−∞";
                voiceCountTxt.Text = $"{(int)scope[1]} / {poly}";
            }
            foreach (var a in readouts) a();
        }

        // ---- small builders --------------------------------------------------------
        static TextBlock Lbl(string t, double fs, IBrush c, FontWeight w = FontWeight.SemiBold)
            => new() { Text = NotaText.Label(t), FontSize = fs, FontWeight = w, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Caps(string t) => new() { Text = NotaText.Label(t), FontSize = NotaType.Caption, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock MonoText(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Border Divider(Control child, double top = 5) => new() { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };

        // `mod` is kept for the call sites: modulation amounts are orange values like the rest.
        Control K(string id, string name, Func<float, string> fmt, bool mod = false, double sz = Knob.Size, double cw = 40, bool inline = false)
        {
            var cell = InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, sz, cw, null, inline);
            // A knob in a table row under a column head: no name line under its value.
            if (name.Length == 0 && !inline && cell is StackPanel sp && sp.Children.Count == 3) sp.Children[2].IsVisible = false;
            return cell;
        }

        // On/off pill toggle backed by a param (> 0.5 = on).
        Control Toggle(string id, string label, List<Action>? into = null)
        {
            var wrap = Switch(label, () => On(id), () => { SetP(id, On(id) ? 0f : 1f); Refresh(); }, out var sync);
            (into ?? readouts).Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(wrap, MidiTarget.PluginParam(track, -1, pi), label.Length > 0 ? label : id);
            return wrap;
        }

        // Segmented chips. values[i] is written on click; the lit chip is the nearest value.
        Control Chips(string id, string[] names, float[]? values = null, List<Action>? into = null, double fs = 7)
        {
            var vals = values ?? BuildValues(names.Length);
            var seg = DeviceCardKit.Segments(names, () => DeviceCardKit.NearestExact(G(id), vals), iv => { SetP(id, vals[iv]); Refresh(); }, out var sync);
            (into ?? readouts).Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }
        static float[] BuildValues(int n) { var v = new float[n]; for (int i = 0; i < n; i++) v[i] = n > 1 ? i / (float)(n - 1) : 0f; return v; }

        // Waveform on/off cell (the P5's independent, summable wave switches): a segment-style
        // cell that rises out of its groove in the soft tint, its wave in orange, while on.
        Control WaveChip(string id, int wave, double w = 32, double h = NotaSize.Seg)
        {
            var ic = new PentadWaveIcon(wave) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = w, Height = h, CornerRadius = NotaRadius.Cell, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Child = ic };
            void Hi()
            {
                bool on = On(id);
                b.Background = on ? NotaPalette.AccentSoft : Brushes.Transparent;
                if (on) b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted"); else b.BoxShadow = default;
                ic.Stroke = on ? NotaPalette.AccentBright : MutedC; ic.InvalidateVisual();
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(id, On(id) ? 0f : 1f); Refresh(); e.Handled = true; };
            readouts.Add(Hi); Hi();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(b, MidiTarget.PluginParam(track, -1, pi), id);
            return b;
        }
        // The wave cells share one groove, like a segment strip.
        static Border WaveGroup(params Control[] cells)
        {
            var g = new Border
            {
                Background = NotaPalette.Groove, CornerRadius = NotaRadius.Group, Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                Child = Row(0, cells),
            };
            g.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
            return g;
        }

        // Horizontal fill slider bound to a param; `dim` greys it out (inactive route).
        Control HSlider(string id, Func<double, string> fmt, double valW = 24, Func<bool>? dim = null, List<Action>? into = null)
        {
            int pi = I(id);
            var row = DeviceCardKit.SliderRow("", () => G(id), n => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)n); Refresh(); }, () => fmt(G(id)), out var sync,
                begin: () => { if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); },
                end: () => { if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); },
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                dim: dim, valueWidth: valW);
            (into ?? readouts).Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            return row;
        }
        // Label + slider on one grid row.
        Control SliderRow(string label, string id, Func<double, string> fmt, double labW = 38, double valW = 24, Func<bool>? dim = null, List<Action>? into = null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labW},*"), VerticalAlignment = VerticalAlignment.Center };
            var cap = Caps(label);
            if (dim is not null) (into ?? readouts).Add(() => cap.Foreground = dim() ? DimC : MutedC);
            if (label.StartsWith("AT ")) cap.Text = "AT" + NotaText.Label(label)[2..];   // aftertouch: an acronym Label would lowercase
            g.Children.Add(cap); g.Children.Add(Col(HSlider(id, fmt, valW, dim, into), 1));
            return g;
        }

        // The round wheel (PerformWheel) bound to a param; the pitch wheel springs back to centre.
        Control VWheel(string id, string name, bool spring)
        {
            int pi = I(id);
            var w = new PerformWheel { Default = spring ? 0.5 : 0, Spring = spring, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Center, Norm = G(id) };
            w.ValueChanged += v => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Refresh(); };
            w.GestureBegin += () => { if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); };
            w.GestureEnd += () => { if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); };
            readouts.Add(() => { if (!w.Dragging) w.Norm = G(id); });
            if (!spring) ValueEntry.Attach(w, w.EntryField(name, v => Pct(v)));   // the bend wheel springs back
            if (pi >= 0) MidiLearn.Bind(w, MidiTarget.PluginParam(track, -1, pi), name);
            return w;
        }

        // ---- formatters -------------------------------------------------------------
        static double Exp(double lo, double hi, double v) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Time(double s) => s >= 1 ? $"{s:0.0}\u2009s" : s >= 0.0995 ? $"{s * 1000:0}\u2009ms" : $"{s * 1000:0.#}\u2009ms";
        static string ShortTime(double s) => s >= 1 ? $"{s:0.0}\u2009s" : $"{s * 1000:0}\u2009ms";
        string Atk(float v) => ShortTime(Exp(0.0005, 10, v));
        string Dec(float v) => ShortTime(Exp(0.002, 15, v));
        string Glide(double v) => v < 0.002 ? "off" : Time(Exp(0.005, 10, v));
        string HzFmt(double hz) => hz >= 1000 ? $"{hz / 1000:0.00}k" : $"{hz:0}";
        string Cut(float v) => HzFmt(20 * Math.Pow(1000, v));
        static string Pct(double v) => $"{v * 100:0}\u2009%";
        static string Tenths(double v) => $"{v * 10:0.0}";
        string LfoRate(float v) => On("lfosync") ? SyncNames[Math.Clamp((int)Math.Round(v * 13), 0, 13)] : $"{Exp(0.05, 20, v):0.0#}\u2009Hz";
        string VolDb(float v) { double g = 2 * v * v; return g <= 1e-4 ? "−∞" : $"{20 * Math.Log10(g):0.0}\u2009dB"; }
        int BendSt() => 1 + Math.Clamp((int)Math.Round(G("bendrange") * 11), 0, 11);

        // ======================================================================
        // LEFT — wheels
        // ======================================================================
        var bendTxt = MonoText("", NotaType.KnobValue, TxtC); var modTxt = MonoText("", NotaType.KnobValue, MutedC);
        bendTxt.HorizontalAlignment = HorizontalAlignment.Center; modTxt.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() => { bendTxt.Text = $"±{BendSt()}\u2009st"; modTxt.Text = $"{G("modwheel") * 100:0}\u2009%"; modTxt.Foreground = G("modwheel") > 0.01f ? TxtC : MutedC; });
        var wheelPair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Width = 56, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
        wheelPair.Children.Add(VWheel("bend", "Pitch", spring: true));
        wheelPair.Children.Add(Col(VWheel("modwheel", "Mod wheel", spring: false), 1));
        var wheelNames = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Width = 60, HorizontalAlignment = HorizontalAlignment.Center };
        var pitchCap = Caps("Pitch"); pitchCap.FontSize = NotaType.KnobLabel; pitchCap.HorizontalAlignment = HorizontalAlignment.Center;
        var modCap = Caps("Mod"); modCap.FontSize = NotaType.KnobLabel; modCap.HorizontalAlignment = HorizontalAlignment.Center;
        wheelNames.Children.Add(pitchCap); wheelNames.Children.Add(Col(modCap, 1));
        var wTitle = Caps("Wheels"); wTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var wheelsBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), RowSpacing = 4 };
        void WRow(Control c, int r) { Grid.SetRow(c, r); wheelsBody.Children.Add(c); }
        WRow(wTitle, 0); WRow(wheelPair, 1); WRow(wheelNames, 2); WRow(bendTxt, 3); WRow(modTxt, 4);
        modTxt.Margin = new Thickness(0, -4, 0, 0);
        var wheels = new RuleSection { Width = 76, Padding = new Thickness(4, 10, 4, 10), Child = wheelsBody };

        // ======================================================================
        // CENTRE — Oscillators
        // ======================================================================
        const string OscCols = "18,40,40,40,*,Auto";
        Control Cell(Control c, int col, HorizontalAlignment ha = HorizontalAlignment.Center) { c.HorizontalAlignment = ha; c.VerticalAlignment = VerticalAlignment.Center; return Col(new Panel { Children = { c } }, col); }
        Control OscRow(string o)
        {
            bool isA = o == "a";
            string p = "o" + o;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols) };
            g.Children.Add(Cell(Lbl(isA ? "A" : "B", NotaType.Body, TxtC, FontWeight.SemiBold), 0, HorizontalAlignment.Left));
            g.Children.Add(Cell(K($"{p}oct", "", v => Feet[Math.Clamp((int)Math.Round(v * 3), 0, 3)], false, Knob.Size, 40), 1));
            g.Children.Add(Cell(K($"{p}semi", "", v => { int s = (int)Math.Round((v - 0.5f) * 24); return s == 0 ? "0\u2009st" : $"{s:+0;−0}\u2009st"; }, false, Knob.Size, 40), 2));
            g.Children.Add(Cell(K($"{p}fine", "", v => { double c = (v - 0.5) * 100; return Math.Abs(c) < 0.5 ? "0\u2009c" : $"{c:+0;−0}\u2009c"; }, false, Knob.Size, 40), 3));
            var chips = isA ? WaveGroup(WaveChip("oasaw", 0), WaveChip("oapulse", 1)) : WaveGroup(WaveChip("obsaw", 0), WaveChip("obtri", 2), WaveChip("obpulse", 1));
            var pw = HSlider($"{p}pw", v => Pct(v), 34, () => !On($"{p}pulse"));
            var shape = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(isA ? 11 : 1, 0, 8, 0) };
            shape.Children.Add(chips); shape.Children.Add(Col(pw, 1));
            shape.HorizontalAlignment = HorizontalAlignment.Stretch;
            shape.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(Col(shape, 4));
            Control opts = isA ? Toggle("oasync", "Sync")
                : new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { Toggle("oblofreq", "Lo freq"), Toggle("obkbd", "Kbd") } };
            g.Children.Add(Cell(opts, 5, HorizontalAlignment.Left));
            return new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
        }
        Control OscTab()
        {
            var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols), Height = 17 };
            string[] h = { "Osc", "Oct", "Semi", "Fine", "Shape · pulse width", "Options" };
            for (int i = 0; i < h.Length; i++)
            {
                var t = new TextBlock { Text = h[i], FontSize = NotaType.Eyebrow, Foreground = DimC, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == h.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center };
                hdr.Children.Add(Col(t, i));
            }
            // bottom row: glide · glide mode · voice mode · voices
            var glide = K("glide", "Glide", v => Glide(v), false, Knob.SizeInline, 54, true);
            var mode = Row(6, Caps("Mode"), Chips("voicemode", new[] { "Poly", "Uni", "Mono" }));
            var strip = new VoiceKeys { Height = 16, MinWidth = 82, HorizontalAlignment = HorizontalAlignment.Right }; voiceStrips.Add(strip);
            var voices = Row(6, Caps("Voices"), strip);
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,*,Auto"), ColumnSpacing = 4, Height = 44 };
            bottom.Children.Add(glide); bottom.Children.Add(Col(Chips("glidemode", new[] { "Off", "On", "Leg" }), 1));
            bottom.Children.Add(Col(mode, 3)); bottom.Children.Add(Col(voices, 5));
            foreach (Control c in bottom.Children) c.VerticalAlignment = VerticalAlignment.Center;
            var rows = new Grid { RowDefinitions = new RowDefinitions("*,*") };
            var rb = OscRow("b"); Grid.SetRow(rb, 1);
            rows.Children.Add(OscRow("a")); rows.Children.Add(rb);
            return new DockPanel { LastChildFill = true, Children = {
                Docked(hdr, Avalonia.Controls.Dock.Top),
                Docked(new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Child = bottom }, Avalonia.Controls.Dock.Bottom),
                rows } };
        }

        // ======================================================================
        // CENTRE — Filter · Amp
        // ======================================================================
        Control FilterPanel()
        {
            filtCurve.Changed = (c, r) =>
            {
                if (I("cutoff") is var ci and >= 0) engine.PluginParamSet(track, -1, ci, (float)c);
                if (I("reso") is var ri and >= 0) engine.PluginParamSet(track, -1, ri, (float)r);
                Refresh();
            };
            filtCurve.DragStarted += () => { engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "cutoff"); engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "reso"); };
            filtCurve.DragEnded += () => { engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "cutoff"); engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "reso"); };
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Field, Margin = new Thickness(0, 4, 0, 4), Child = filtCurve };
            var keyPct = MonoText("", NotaType.KnobValue, TxtC);
            readouts.Add(() => keyPct.Text = Pct(G("keytrk")));
            keyPct.HorizontalAlignment = HorizontalAlignment.Center;
            var key = new StackPanel { Spacing = 3, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { Caps("Keytrack"), Chips("keytrk", new[] { "0", "½", "1" }), keyPct } };
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,Auto") };
            knobs.Children.Add(K("cutoff", "CUTOFF", Cut, false, Knob.Size, 40));
            knobs.Children.Add(Col(K("reso", "RESO", v => Tenths(v), false, Knob.Size, 40), 1));
            knobs.Children.Add(Col(K("fenvamt", "Env", v => Tenths(v), true, Knob.Size, 40), 2));
            knobs.Children.Add(Col(key, 3));
            var body = new DockPanel { LastChildFill = true, Children = { Docked(Caps("FILTER"), Avalonia.Controls.Dock.Top), Docked(knobs, Avalonia.Controls.Dock.Bottom), graph } };
            return new Border { Width = 190, BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 2, 10, 0), Child = body };
        }
        Control EnvPanel(string title, string pre, PentadEnvCurve curve, bool amp)
        {
            var times = MonoText("", NotaType.Eyebrow, MutedC);
            times.TextTrimming = TextTrimming.CharacterEllipsis; times.TextAlignment = TextAlignment.Right;
            readouts.Add(() => times.Text = $"{Atk(G(pre + "attack"))} · {Dec(G(pre + "decay"))} · {G(pre + "sustain") * 100:0}\u2009% · {(On("releaseon") ? Dec(G(pre + "release")) : "min")}");
            var head = new DockPanel { Height = 18, LastChildFill = true };
            head.Children.Add(Docked(Caps(title), Avalonia.Controls.Dock.Left));
            if (amp) head.Children.Add(Docked(Toggle("releaseon", "Release"), Avalonia.Controls.Dock.Right));
            head.Children.Add(new Border { Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, Child = times });
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Field, Child = curve };
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 2, RowSpacing = 0, VerticalAlignment = VerticalAlignment.Center };
            Control[] ks = { K(pre + "attack", "A", Atk, amp, Knob.SizeInline, 64, true), K(pre + "decay", "D", Dec, amp, Knob.SizeInline, 64, true), K(pre + "sustain", "S", v => Pct(v), amp, Knob.SizeInline, 64, true), K(pre + "release", "R", Dec, amp, Knob.SizeInline, 64, true) };
            for (int i = 0; i < 4; i++) { Grid.SetColumn(ks[i], i % 2); Grid.SetRow(ks[i], i / 2); knobs.Children.Add(ks[i]); }
            knobs.Margin = new Thickness(8, 0, 0, 0);
            return new DockPanel { LastChildFill = true, Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(knobs, Avalonia.Controls.Dock.Right), graph } };
        }
        Control FilterAmpTab()
        {
            var envs = new Grid { RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(10, 2, 0, 0) };
            var fe = EnvPanel("Filter env", "f", fEnvCurve, false);
            var ae = Divider(EnvPanel("Amp env", "a", aEnvCurve, true), 4); ae.Margin = new Thickness(0, 4, 0, 0); Grid.SetRow(ae, 1);
            envs.Children.Add(fe); envs.Children.Add(ae);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(FilterPanel()); g.Children.Add(Col(envs, 1));
            return g;
        }

        // ======================================================================
        // CENTRE — Poly Mod · LFO · Wheel Mod
        // ======================================================================
        string Dests()
        {
            var d = new List<string>();
            if (On("pmfreqa")) d.Add("Freq A"); if (On("pmpwa")) d.Add("PW A"); if (On("pmfilter")) d.Add("Filter");
            return d.Count == 0 ? "no dest" : string.Join(" + ", d);
        }
        int ActiveRoutes()
        {
            int dn = (On("pmfreqa") ? 1 : 0) + (On("pmpwa") ? 1 : 0) + (On("pmfilter") ? 1 : 0);
            return dn * ((G("pmenv") > 0.001f ? 1 : 0) + (G("pmoscb") > 0.001f ? 1 : 0));
        }
        Control RouteRow(string src, string id, bool isPm, Func<string> dest, string? dimWhenZeroOf = null)
        {
            var srcT = new TextBlock { Text = src, FontSize = NotaType.RowLabel, Foreground = TxtC, Width = 52, VerticalAlignment = VerticalAlignment.Center };
            var destT = new TextBlock { FontSize = NotaType.RowLabel, Foreground = TxtC, Width = 92, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            bool Dim() => G(id) < 0.001f || (isPm && !On("pmfreqa") && !On("pmpwa") && !On("pmfilter"));
            readouts.Add(() => { destT.Text = dest(); bool d = Dim(); srcT.Foreground = d ? DimC : Txt2; destT.Foreground = d ? DimC : Txt2; });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 6, Height = 16 };
            g.Children.Add(srcT);
            g.Children.Add(Col(new TextBlock { Text = "→", FontSize = NotaType.RowLabel, Foreground = DimC, VerticalAlignment = VerticalAlignment.Center }, 1));
            g.Children.Add(Col(destT, 2));
            g.Children.Add(Col(HSlider(id, v => Tenths(v), 26, Dim), 3));
            return g;
        }
        Control PolyModTab()
        {
            // The tab hint names it (source → destination · active routes), so the rows start at the top.
            var dests = Row(12, Caps("Dest"), Toggle("pmfreqa", "Osc A freq"), Toggle("pmpwa", "Osc A PW"), Toggle("pmfilter", "Filter cutoff"));
            dests.Height = 18;
            var vel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12, Height = 16, Children = {
                SliderRow("Vel → amp", "velamp", v => Pct(v), 52, 30),
                Col(SliderRow("Vel → filt", "velfilt", v => Pct(v), 52, 30), 1) } };
            var at = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12, Height = 16, Children = {
                SliderRow("AT → cut", "atcutoff", v => Pct(v), 52, 30),
                Col(SliderRow("AT → LFO", "atlfo", v => Pct(v), 52, 30), 1) } };
            var routes = new StackPanel { Spacing = 4, Children = {
                RouteRow("Filter env", "pmenv", true, Dests),
                RouteRow("Osc B", "pmoscb", true, Dests),
                dests, vel, at } };

            // LFO: Rate and Amount beside their names, then the wave and Free / Sync.
            var lfoWaves = WaveGroup(WaveChip("lfotri", 2, 26), WaveChip("lfosaw", 0, 26), WaveChip("lfosquare", 3, 26));
            var lfo = new StackPanel { Spacing = 4, Children = {
                Row(4, K("lforate", "Rate", LfoRate, false, Knob.SizeInline, 84, true), K("lfoamt", "Amount", v => Pct(v), true, Knob.SizeInline, 76, true)),
                Row(6, Caps("LFO"), lfoWaves, Chips("lfosync", new[] { "Free", "Sync" })) } };
            // Wheel mod: its source mix, then the five destinations.
            var wmGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 3, ColumnSpacing = 6 };
            void W(string id, string l, int r, int c) { var t = Toggle(id, l); Grid.SetRow(t, r); Grid.SetColumn(t, c); wmGrid.Children.Add(t); }
            W("wmfreqa", "Freq A", 0, 0); W("wmfreqb", "Freq B", 0, 1); W("wmfilter", "Filter", 0, 2);
            W("wmpwa", "PW A", 1, 0); W("wmpwb", "PW B", 1, 1);
            var mixRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 5, Children = {
                Caps("Wheel mod"), Col(Lbl("LFO", NotaType.Eyebrow, DimC, FontWeight.Normal), 1), Col(HSlider("wmmix", v => Pct(v), 30), 2), Col(Lbl("Noise", NotaType.Eyebrow, DimC, FontWeight.Normal), 3) } };
            var wm = new StackPanel { Spacing = 4, Children = { mixRow, wmGrid } };
            var wmBox = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0), Child = wm };
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Children = { lfo, Col(wmBox, 1) } };

            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0), Children = {
                Docked(Divider(bottom, 6), Avalonia.Controls.Dock.Bottom),
                routes } };
        }

        // ======================================================================
        // RIGHT — Mixer / Output
        // ======================================================================
        Control MixRow(string onId, string lvlId, string name)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,40,*"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Toggle(onId, ""));
            var cap = Caps(name);
            readouts.Add(() => cap.Foreground = On(onId) ? Txt2 : DimC);
            g.Children.Add(Col(cap, 1));
            g.Children.Add(Col(HSlider(lvlId, v => Tenths(v), 28, () => !On(onId)), 2));
            return g;
        }
        Control MixerTab()
        {
            bool UniOff() => G("voicemode") < 0.25f && G("unison") < 0.25f;
            var noiseType = (Border)Chips("noisecolor", new[] { "White", "Pink" }); noiseType.HorizontalAlignment = HorizontalAlignment.Right;
            var uni = Row(5, new TextBlock { Text = "Unison", FontSize = NotaType.Caption, Foreground = MutedC, Width = 44, VerticalAlignment = VerticalAlignment.Center }, Chips("unison", new[] { "Off", "×2", "×5" }));
            var det = SliderRow("Detune", "unidetune", v => $"{v * 50:0}\u2009c", 49, 32, UniOff);
            var sum = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,32"), ColumnSpacing = 5, Children = { Caps("Sum"), Col(sumBar, 1), Col(Right(sumTxt), 2) } };
            var g = new Grid { RowDefinitions = new RowDefinitions("*,*,*,Auto,Auto,*,*,*") };
            Control[] rows = { MixRow("mixaon", "mixa", "Osc A"), MixRow("mixbon", "mixb", "Osc B"), MixRow("mixnoiseon", "mixnoise", "Noise"), noiseType,
                new Border { Height = 1, Background = NotaPalette.Rule, Margin = new Thickness(0, 6, 0, 2) }, uni, det, sum };
            for (int r = 0; r < rows.Length; r++) { rows[r].VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(rows[r], r); g.Children.Add(rows[r]); }
            return new Border { Padding = new Thickness(0, 6, 0, 0), Child = g };
        }
        Control OutputTab()
        {
            var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), Height = 42, Children = {
                MonoText("0", NotaType.Axis, NotaPalette.TextAxis), WithRow(MonoText("−12", NotaType.Axis, NotaPalette.TextAxis), 1), WithRow(MonoText("−48", NotaType.Axis, NotaPalette.TextAxis), 2) } };
            var meters = Row(4, new StackPanel { Spacing = 2, Children = { meterL, Center(Lbl("L", NotaType.Axis, MutedC, FontWeight.Normal)) } },
                                new StackPanel { Spacing = 2, Children = { meterR, Center(Lbl("R", NotaType.Axis, MutedC, FontWeight.Normal)) } }, scale);
            meters.VerticalAlignment = VerticalAlignment.Top;
            var top = Row(10, K("volume", "Volume", VolDb, false, Knob.Size, 54), meters);
            var bend = Row(6, new TextBlock { Text = "Bend", FontSize = NotaType.Caption, Foreground = MutedC, Width = 44, VerticalAlignment = VerticalAlignment.Center },
                Chips("bendrange", new[] { "2", "5", "7", "12" }, new[] { 1 / 11f, 4 / 11f, 6 / 11f, 1f }), Lbl("st", NotaType.Caption, MutedC, FontWeight.Normal));
            // Velocity → filter: a quick switch over the Velocity Filter amount (0 ↔ 50 %).
            var velF = Switch("Vel → filter", () => G("velfilt") > 0.001f, () => { SetP("velfilt", G("velfilt") > 0.001f ? 0f : 0.5f); Refresh(); }, out var velSync);
            readouts.Add(velSync);
            var strip = new VoiceKeys { Width = 82, Height = 16 }; voiceStrips.Add(strip);
            var voices = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6, Children = { Caps("Voices"), Col(strip, 1), Col(Right(voiceCountTxt), 2) } };
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(top, Avalonia.Controls.Dock.Top),
                Docked(Divider(new StackPanel { Spacing = 4, Children = { SliderRow("Spread", "spread", v => Pct(v), 49, 32), bend, velF } }), Avalonia.Controls.Dock.Top),
                Docked(Divider(voices), Avalonia.Controls.Dock.Bottom) } };
            ((Control)body.Children[1]).Margin = new Thickness(0, 6, 0, 0);
            return new Border { Padding = new Thickness(0, 6, 0, 0), Child = body };
        }
        static Control WithRow(Control c, int r) { Grid.SetRow(c, r); return c; }
        static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }
        static Control Right(Control c) { c.HorizontalAlignment = HorizontalAlignment.Right; return c; }

        // ======================================================================
        // Tab frames + extras
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var extrasHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var tabBodies = new Control?[3]; var rightBodies = new Control?[2];
        int centreTab = 0;

        // Drift slider + seed re-roll (Oscillators tab bar)
        var reroll = new Border { Padding = new Thickness(3, 0), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new Glyph(GlyphKind.Cycle, 11) { Foreground = MutedC } };
        ToolTip.SetTip(reroll, "Re-roll the vintage seed (new per-voice spread)");
        reroll.PointerPressed += (_, e) => { SetP("seed", (float)Random.Shared.NextDouble()); Refresh(); e.Handled = true; };
        var driftSl = HSlider("drift", v => $"{v * 100:0}\u2009%", 30); driftSl.Width = 88;
        Control[] extras = {
            Row(6, Caps("Drift"), driftSl, reroll),
            MonoText("4-pole LP · 24\u2009dB/oct", NotaType.Eyebrow, MutedC),
            MonoText("", NotaType.Eyebrow, MutedC) };
        readouts.Add(() => { if (extras[2] is TextBlock t) { int n = ActiveRoutes(); t.Text = $"source → destination · {n} active route{(n == 1 ? "" : "s")}"; } });

        Control CentreBody(int t) => tabBodies[t] ??= t switch { 1 => FilterAmpTab(), 2 => PolyModTab(), _ => OscTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : MixerTab();

        var centre = TabFrame(new[] { "Oscillators", "Filter · Amp", "Poly Mod" }, centreHost, CentreBody, t => { centreTab = t; extrasHost.Content = extras[t]; Refresh(); }, extrasHost);

        // ======================================================================
        // Voice setup menu (on the right section's tab row)
        // ======================================================================
        var flyReadouts = new List<Action>();
        Flyout? setupFly = null;
        Control SetupPanel()
        {
            var seedTxt = MonoText("", NotaType.KnobValue, TxtC);
            flyReadouts.Add(() => seedTxt.Text = $"#{(int)Math.Round(G("seed") * 16777215):X6}");
            var rr = TextButton("Re-roll");
            rr.PointerPressed += (_, e) => { SetP("seed", (float)Random.Shared.NextDouble()); Refresh(); foreach (var a in flyReadouts) a(); e.Handled = true; };
            Control R(string label, Control c) { var g = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*"), Height = 26 }; g.Children.Add(Caps(label)); c.HorizontalAlignment = HorizontalAlignment.Left; c.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(Col(c, 1)); return g; }
            var tune = HSlider("tune", v => { double c = (v - 0.5) * 200; return Math.Abs(c) < 0.5 ? "0\u2009c" : $"{c:+0;−0}\u2009c"; }, 30, null, flyReadouts); tune.Width = 130;
            var at = HSlider("aftertouch", v => Pct(v), 30, null, flyReadouts); at.Width = 130;
            var p = new StackPanel { Spacing = 2, Width = 290, Children = {
                Lbl("Voice setup", NotaType.Body, TxtC),
                new Border { Height = 4 },
                R("VOICES", Chips("polyphony", new[] { "5", "10", "16" }, null, flyReadouts, 8)),
                R("ALLOCATION", Chips("alloc", new[] { "Rotate", "Oldest" }, null, flyReadouts, 8)),
                R("GLIDE", Chips("glidemode", new[] { "Off", "On", "Legato" }, null, flyReadouts, 8)),
                R("RELEASE", Toggle("releaseon", "Release switch", flyReadouts)),
                R("OVERSAMPLING", Chips("oversample", new[] { "×2", "×4" }, null, flyReadouts, 8)),
                R("BASS COMP", Toggle("lowcomp", "Restore lows at high Q", flyReadouts)),
                R("NOISE FLOOR", Toggle("noisefloor", "−100\u2009dBFS hiss", flyReadouts)),
                R("VINTAGE SEED", Row(8, seedTxt, rr)),
                R("MASTER TUNE", tune),
                R("AFTERTOUCH", at) } };
            return new Border { Padding = new Thickness(4, 2), Child = p };
        }
        // A raised menu button inside the device (22 high, corner 6), at the right of the Mixer / Output tabs.
        var voiceBtn = new Border { Height = NotaSize.Dropdown, Padding = new Thickness(9, 0, 8, 0), CornerRadius = NotaRadius.Field, Background = NotaPalette.SurfaceHover, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Child = Glyph.WithChevron(new TextBlock { Text = "Voice", FontSize = NotaType.Body, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center }, 7) };
        voiceBtn.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        ToolTip.SetTip(voiceBtn, "Voice setup: polyphony, allocation, glide mode, oversampling, vintage seed");
        voiceBtn.PointerPressed += (_, e) =>
        {
            setupFly ??= new Flyout { Content = SetupPanel(), Placement = PlacementMode.TopEdgeAlignedRight };
            foreach (var a in flyReadouts) a();
            setupFly.ShowAt(voiceBtn);
            e.Handled = true;
        };
        readouts.Add(() => { if (setupFly?.IsOpen == true) foreach (var a in flyReadouts) a(); });

        var rightFrame = TabFrame(new[] { "Mixer", "Output" }, rightHost, RightBody, null, voiceBtn);
        var right = new RuleSection { Width = 190, Padding = new Thickness(SectionPad), Child = rightFrame };
        var centreBox = new RuleSection { Padding = new Thickness(SectionPad), Child = centre };

        // ---- assemble ---------------------------------------------------------------
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = SectionGap, Margin = new Thickness(0, CardPad, 0, 0) };
        root.Children.Add(wheels); root.Children.Add(Col(centreBox, 1)); root.Children.Add(Col(right, 2));

        ctx.SetInstLiveViz(Refresh);
        Refresh();
        return root;

        // local: a tab frame (the device tab row + a swapping body), extras at the right end of the row.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, Action<int>? changed, Control? extrasCtl)
        {
            int sel = 0;
            Action sync = () => { };
            var bar = SynthInstrumentCard.TabStrip(tabs, () => sel, iv => { sel = iv; sync(); host.Content = body(iv); changed?.Invoke(iv); }, out var s, extrasCtl);
            sync = s;
            if (extrasCtl is not null) { extrasCtl.HorizontalAlignment = HorizontalAlignment.Right; extrasCtl.Margin = new Thickness(0, 0, 0, 3); }
            DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
            host.Content = body(sel); changed?.Invoke(sel);
            return new DockPanel { LastChildFill = true, Children = { bar, host } };
        }
    }

    // The voices as device number buttons (14 × 16, raised, 9.5 / 600 in ink 2), right-aligned;
    // a sounding voice takes the soft tint with its number in orange. Cells narrow to fit
    // when the polyphony is larger than the strip.
    private sealed class VoiceKeys : Control
    {
        private readonly float[] _lv = new float[16];
        private int _n = 5;

        public void Set(int count, ReadOnlySpan<float> levels)
        {
            _n = Math.Clamp(count, 1, 16);
            for (int i = 0; i < 16; i++) _lv[i] = i < levels.Length ? levels[i] : 0f;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w < 2 || h < 2) return;
            const double gap = 3, cellW = 14;
            double cw = Math.Min(cellW, (w - gap * (_n - 1)) / _n);
            double x0 = w - (cw * _n + gap * (_n - 1));
            for (int i = 0; i < _n; i++)
            {
                var r = new Rect(x0 + i * (cw + gap), 0, cw, h);
                bool lit = _lv[i] > 0.0005f;
                ctx.DrawRectangle(lit ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover, null, r, 3, 3);
                if (!lit) ctx.DrawLine(new Pen(NotaPalette.Bevel, 1), new Point(r.X + 2, r.Y + 0.5), new Point(r.Right - 2, r.Y + 0.5));
                if (cw >= 9)
                {
                    var ft = new NotaFormattedText((i + 1).ToString(NotaNum.Culture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        NotaFonts.SansSemiBold, 9.5, lit ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
                    ctx.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
                }
            }
        }
    }
}
