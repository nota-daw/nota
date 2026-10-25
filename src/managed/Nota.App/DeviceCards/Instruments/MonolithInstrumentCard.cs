// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Monolith editor (instrument kind 13): a Minimoog Model-D
// layout as three pressed-in sections (style guide, spec 038) — an always-visible wheels
// column (pitch / mod), a centre tabbed panel (Controllers · Osc Bank / Modifiers) and a
// right tabbed panel (Mixer / Output). Oscillator ranges/detune, six waveform chips, the
// ladder filter response and both contours are all shown; every control is a plugin-param
// so automation / MIDI-learn / persist come for free. The card is BodyOnly — the shared
// shell provides the header (name / preset / type tag / power).

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

internal sealed class MonolithInstrumentCard : IInstrumentCard
{
    private static readonly string[] Feet = { "Lo", "32′", "16′", "8′", "4′", "2′" };

    public bool BodyOnly => true;
    public string Subtitle => "Mono · Model-D";

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        var filtCurve = new MonolithFilterCurve();
        var fEnvCurve = new MonolithEnvCurve { Accent = NotaPalette.VizInk };
        var aEnvCurve = new MonolithEnvCurve { Accent = NotaPalette.VizInk };

        void Refresh()
        {
            filtCurve.Set(G("cutoff"), G("emph")); filtCurve.InvalidateVisual();
            fEnvCurve.Set(G("fattack"), G("fdecay"), G("fsustain")); fEnvCurve.InvalidateVisual();
            aEnvCurve.Set(G("aattack"), G("adecay"), G("asustain")); aEnvCurve.InvalidateVisual();
            foreach (var a in readouts) a();
        }

        // ---- shared builders --------------------------------------------------
        Control Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }

        Control K(string id, string name, Func<float, string>? fmt = null, double cw = 52)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, Knob.Size, cw);

        // A table-row knob: [inline knob] value, side by side — for the oscillator rows and unison.
        Control RowKnob(string id, Func<float, string> fmt, double valW)
        {
            if (I(id) is not (var i and >= 0)) return new Panel();
            var value = Value(fmt(G(id)));
            value.Width = valW;
            var knob = new Knob(G(id), 1.0) { Accent = true, Inline = true, Default = engine.InstrumentParamDefault(track, i) };
            knob.Format = v => fmt((float)v);
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, i, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
            knob.GestureEnd += () => engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
            ctx.AddInstFader(i, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, i), id);
            ValueEntry.Attach(knob, engine.PluginParamName(track, -1, i), value);
            return Row(6, knob, value);
        }

        // On/off switch backed by a param (>0.5 = on).
        Control Toggle(string id, string label)
        {
            var wrap = Switch(label, () => G(id) > 0.5f, () => { SetP(id, G(id) > 0.5f ? 0f : 1f); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(wrap, MidiTarget.PluginParam(track, -1, pi), label);
            return wrap;
        }

        // Param-backed segment strip (writes id = pick/(n-1)).
        Control Chips(string id, string[] names, double padX = 6)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); Refresh(); }, out var sync, padX: padX);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        // Six waveform cells in a groove (osc3 swaps shark-tooth → reverse saw); the picked one raised.
        Control WaveChips(string id, bool osc3)
        {
            var arr = new Border[6]; var ic = new MonolithWaveIcon[6];
            void Hi()
            {
                int cur = Sel(id, 6);
                for (int i = 0; i < 6; i++)
                {
                    bool on = i == cur;
                    PaintCell(arr[i], on);
                    ic[i].Stroke = on ? AccentBright : TextSecondary; ic[i].InvalidateVisual();
                }
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*") };
            for (int i = 0; i < 6; i++)
            {
                int iv = i;
                var wi = new MonolithWaveIcon(i, osc3) { Width = 16, Height = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Height = NotaSize.Seg, CornerRadius = NotaRadius.Cell, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Child = wi };
                b.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                    SetP(id, iv / 5f); Hi(); Refresh(); e.Handled = true;
                };
                Grid.SetColumn(b, i);
                arr[i] = b; ic[i] = wi; row.Children.Add(b);
            }
            readouts.Add(Hi); Hi();
            var groove = Groove(row);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(groove, MidiTarget.PluginParam(track, -1, pi), id);
            return groove;
        }

        // A value-bar row bound to a param: the bar and its value, right-aligned in the column.
        Control Bar(string id, string name, Func<float, string> fmt, double trackW = 56, double valW = 24)
        {
            var r = ParamRow(ctx, idx, id, "", fmt, Refresh, readouts, trackWidth: trackW, valueWidth: valW, learnName: name);
            r.HorizontalAlignment = HorizontalAlignment.Right;
            return r;
        }

        // ---- formatters -------------------------------------------------------
        string StFmt(float v) => $"{(v - 0.5f) * 5f:+0.0;−0.0;0.0} st";
        string GlideFmt(float v) => v <= 0.001f ? "off" : (Sec(0.002, 10, v) is var s && s >= 1 ? $"{s:0.0} s" : $"{s * 1000:0} ms");
        string DetFmt(float v) => $"{(v - 0.5f) * 14f:+0.00;−0.00;0.00}";
        string HzFmt(float v) { double hz = 16 * Math.Pow(1250, v); return hz >= 1000 ? $"{hz / 1000:0.0}k" : $"{hz:0}"; }
        string EmphFmt(float v) => $"{v * 10f:0.0}";
        string TimeFmt(double lo, double hi, float v) { double s = Sec(lo, hi, v); return s >= 1 ? $"{s:0.0} s" : $"{s * 1000:0} ms"; }
        string VolFmt(float v) => v <= 0.001f ? "−∞" : $"{20 * Math.Log10(v):0.0} dB";
        string Lvl(float v) => $"{v * 10:0.0}";
        static double Sec(double lo, double hi, double v) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

        // ======================================================================
        // LEFT — wheels column (always visible)
        // ======================================================================
        var bendWheel = Wheel(ctx, idx, "bend", 0.5, true, Refresh, readouts);
        var modWheel = Wheel(ctx, idx, "modwheel", 0, false, Refresh, readouts);
        var bendVal = Value(); var modVal = Value();
        readouts.Add(() =>
        {
            float b = G("bend");
            bendVal.Text = $"{(b - 0.5) * 2 * (2 + G("bendrange") * 10):+0.0;−0.0;0.0}";
            float m = G("modwheel");
            modVal.Text = $"{m * 100:0} %";
            modVal.Foreground = m > 1e-3f ? TextPrimary : TextTertiary;
        });
        var wheels = WheelsPanel(bendWheel, modWheel, bendVal, modVal);
        DockPanel.SetDock(wheels, Dock.Left);

        // ======================================================================
        // CENTRE — Osc Bank / Modifiers tabs
        // ======================================================================
        // -- Osc Bank tab --
        Control ControllersStrip()
        {
            var toggles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("19,19,19"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
            void T(string id, string lbl, int r, int c) => toggles.Children.Add(Toggle(id, lbl).At(c, r));
            T("glideon", "Glide", 0, 0); T("legato", "Legato", 0, 1);
            T("decayon", "Decay", 1, 0); T("oscmodon", "Osc mod", 1, 1);
            T("filtmodon", "Filter mod", 2, 0); T("osc3kbd", "Osc 3 kbd", 2, 1);
            var knobs = Row(0, K("tune", "Tune", StFmt), K("glide", "Glide", GlideFmt), K("modmix", "Mod mix", v => $"{v * 100:0} %"));
            var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 60, Margin = new Thickness(0, 4, 0, 0) };
            strip.Children.Add(knobs.At(0)); strip.Children.Add(toggles.At(2));
            toggles.Width = 176;
            return new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 4), Child = strip };
        }
        const string OscCols = "22,62,74,*,44";
        Control OscRow(int n)
        {
            string p = $"o{n}";
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols), Height = 24 };
            var num = new TextBlock { Text = n.ToString(), FontSize = NotaType.DeviceTab, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Cell(num, 0));
            g.Children.Add(Cell(RowKnob($"{p}range", v => Feet[Math.Clamp((int)Math.Round(v * 5), 0, 5)], 26), 1));
            Control freq = n == 1 ? Cap("master") : RowKnob($"{p}tune", DetFmt, 34);
            g.Children.Add(Cell(freq, 2));
            var waves = WaveChips($"{p}wave", n == 3);
            waves.HorizontalAlignment = HorizontalAlignment.Stretch; waves.Margin = new Thickness(0, 0, 8, 0);
            g.Children.Add(Cell(waves, 3));
            var mt = Cap(n == 3 ? "free" : "kbd");
            if (n == 3) readouts.Add(() => mt.Text = G("osc3kbd") > 0.5f ? "kbd" : "free");
            g.Children.Add(Cell(mt, 4));
            return g;
        }
        Control Cell(Control c, int col)
        {
            if (col != 3) c.HorizontalAlignment = col == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            c.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(c, col); return c;
        }
        Control OscTab()
        {
            var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols), Height = 22 };
            string[] h = { "#", "Range", "Freq", "Waveform", "Mod" };
            for (int i = 0; i < 5; i++)
            {
                var t = Cap(h[i], TextDisabled);
                t.HorizontalAlignment = i == 3 ? HorizontalAlignment.Center : i == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                if (i == 3) t.Margin = new Thickness(0, 0, 8, 0);
                hdr.Children.Add(t.At(i));
            }
            var table = new StackPanel { Children = { hdr, OscRow(1), OscRow(2), OscRow(3) } };
            return new DockPanel { LastChildFill = true, Children = { ControllersStrip().Docked(Dock.Top), table } };
        }

        // -- Modifiers tab --
        Control FilterPanel()
        {
            filtCurve.Changed = (c, r) => {
                if (I("cutoff") is var ci and >= 0) engine.PluginParamSet(track, -1, ci, (float)c);
                if (I("emph") is var ri and >= 0) engine.PluginParamSet(track, -1, ri, (float)r);
                Refresh();
            };
            filtCurve.DragStarted += () => { engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "cutoff"); };
            filtCurve.DragEnded += () => { engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "cutoff"); };
            var graph = GraphWindow(filtCurve);
            graph.Margin = new Thickness(0, 4, 0, 2);
            var knobs = Row(0, K("cutoff", "Cutoff", HzFmt, 56), K("emph", "Emphasis", EmphFmt, 56), K("contour", "Contour", v => $"{v * 100:0} %", 56));
            var head = new DockPanel { Height = 18, Children = {
                Row(8, Cap("Kbd"), Toggle("kbd1", "1/3"), Toggle("kbd2", "2/3")).Docked(Dock.Right),
                Cap("Filter") } };
            return new DockPanel { Width = 172, LastChildFill = true, Children = { head.Docked(Dock.Top), knobs.Docked(Dock.Bottom), graph } };
        }
        Control ContourPanel(string title, string route, MonolithEnvCurve curve, string pre)
        {
            var graph = GraphWindow(curve);
            graph.Margin = new Thickness(0, 0, 4, 0);
            var knobs = Row(0, K($"{pre}attack", "A", v => TimeFmt(0.001, 10, v), 42), K($"{pre}decay", "D", v => TimeFmt(0.004, 20, v), 42), K($"{pre}sustain", "S", v => $"{v * 100:0} %", 40));
            var head = Row(6, Cap(title), Cap(route, TextDisabled));
            head.Height = 16;
            var inner = new DockPanel { LastChildFill = true, Children = { knobs.Docked(Dock.Right), graph } };
            return new DockPanel { LastChildFill = true, Children = { head.Docked(Dock.Top), inner } };
        }
        Control ModifiersTab()
        {
            var contours = new Grid { RowDefinitions = new RowDefinitions("*,*"), RowSpacing = 4 };
            contours.Children.Add(ContourPanel("Filter contour", "→ cutoff", fEnvCurve, "f"));
            contours.Children.Add(ContourPanel("Loudness contour", "→ amp", aEnvCurve, "a").At(0, 1));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, Margin = new Thickness(0, 6, 0, 0) };
            g.Children.Add(FilterPanel()); g.Children.Add(contours.At(1));
            return g;
        }

        // ======================================================================
        // RIGHT — Mixer / Output tabs
        // ======================================================================
        Control MixRow(string onId, string lvlId, string name)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto"), ColumnSpacing = 7, Height = 25 };
            g.Children.Add(Toggle(onId, "").At(0));
            g.Children.Add(Cap(name).At(1));
            g.Children.Add(Bar(lvlId, name, Lvl).At(2));
            return g;
        }
        Control MixerTab()
        {
            var nrow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Height = 28 };
            nrow.Children.Add(Chips("noisetype", new[] { "White", "Pink" }));
            nrow.Children.Add(ParamRow(ctx, idx, "extlvl", "Ext", Lvl, Refresh, readouts, valueWidth: 24).At(1));
            var fb = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = MixRow("exton", "feedback", "Feedback") };
            return new StackPanel { Margin = new Thickness(0, 5, 0, 0), Children = {
                MixRow("mix1on", "mix1lvl", "Osc 1"), MixRow("mix2on", "mix2lvl", "Osc 2"), MixRow("mix3on", "mix3lvl", "Osc 3"),
                MixRow("noiseon", "noiselvl", "Noise"), nrow, fb } };
        }
        Control OutputTab()
        {
            var vol = K("volume", "Volume", VolFmt, 60);
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            top.Children.Add(vol);
            top.Children.Add(new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { Toggle("a440", "A-440 tone"), Toggle("basscomp", "Bass comp") } }.At(1));
            return new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0), Children = {
                top,
                LblRow("Unison", Row(10, RowKnob("unison", v => $"{1 + (int)Math.Round(v * 6)}", 12), RowKnob("unidetune", v => $"{v * 50:0} c", 30))),
                LblRow("Priority", Chips("priority", new[] { "Low", "High", "Last" })),
                LblRow("Bend", Chips("bendrange", new[] { "±2", "±4", "±5", "±7", "±9", "±12" }, 3)) } };
        }
        Control LblRow(string t, Control c)
        {
            c.HorizontalAlignment = HorizontalAlignment.Left;
            return new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), Height = 28, Children = { Cap(t), c.At(1) } };
        }

        // ---- tab shells -------------------------------------------------------
        Control TabPanel(string[] tabs, Control? trailing, Func<int, Control> body)
        {
            int sel = 0;
            var host = new ContentControl { Content = body(0) };
            var bar = Tabs(tabs, () => sel, i => { sel = i; host.Content = body(i); }, out _, trailing);
            return new DockPanel { LastChildFill = true, Children = { bar.Docked(Dock.Top), host } };
        }
        Control? oscTabC = null, modTabC = null, mixTabC = null, outTabC = null;

        var drift = ParamRow(ctx, idx, "drift", "Drift", v => $"{v * 100:0} %", Refresh, readouts, trackWidth: 54, valueWidth: 30);
        var centre = Pane(TabPanel(new[] { "Controllers · Osc Bank", "Modifiers" }, drift,
            t => t == 1 ? (modTabC ??= ModifiersTab()) : (oscTabC ??= OscTab())), new Thickness(SectionPad, 9, SectionPad, 8));
        centre.Margin = new Thickness(SectionGap, 0);
        var right = Pane(TabPanel(new[] { "Mixer", "Output" }, null,
            t => t == 1 ? (outTabC ??= OutputTab()) : (mixTabC ??= MixerTab())), new Thickness(SectionPad, 9, SectionPad, 8));
        right.Width = 196;
        DockPanel.SetDock(right, Dock.Right);

        // ---- assemble ---------------------------------------------------------
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { wheels, right, centre } };

        ctx.SetInstLiveViz(Refresh);
        Refresh();
        return root;
    }
}
