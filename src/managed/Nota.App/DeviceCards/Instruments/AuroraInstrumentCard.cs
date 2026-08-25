// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Aurora editor (instrument kind 5), rebuilt to mockup
// 2j: body-only content on the shared shell (700×260) — a LIVE strip (Pos 1 · Cutoff ·
// Unison + Poly/Mono) over a 74px tab rail (Osc · Filter · Env · LFO · Mod · FX). The
// Osc tab shows the 16-frame wavetable stack + two oscillators + sub + unison + warp
// modes; Filter has two filters with per-source routing; Mod is an 8×7 matrix + macros;
// FX holds drive/chorus/reverb. Reuses AuroraStack / VoltFilter / VoltEnv / VoltMatrix.

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
    private static readonly IBrush CardBg = new SolidColorBrush(Color.Parse("#171613"));
    private static readonly IBrush HdrBg = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush RailBg = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush Border2 = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush Inset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush AmberLit = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly IBrush TealC = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush TxtC = new SolidColorBrush(Color.Parse("#E9E4D8"));
    private static readonly IBrush MutedC = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush AmberSubtle = new SolidColorBrush(Color.FromArgb(0x28, 0xD8, 0xA0, 0x3D));

    private static readonly string[] Banks = { "Analog", "Pulse", "Formant", "Chroma" };
    private static readonly string[] WarpModes = { "Off", "Sync", "Bend", "PWM", "Fold" };
    private static readonly string[] FiltTypes = { "LP", "HP", "BP", "Notch", "Morph" };
    private static readonly string[] Routes = { "F1", "F2", "Both", "Dry" };
    private static readonly string[] DestNames = { "Pitch", "Osc2", "Position", "Cutoff", "Reso", "Level", "Pan" };
    private static readonly string[] SrcNames = { "Env 1", "Env 2", "LFO 1", "LFO 2", "Velocity", "Key", "Mod Whl", "Random" };

    public bool BodyOnly => true;
    public string Subtitle => "WAVETABLE";

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine; int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        (int, string) P(string id) => (I(id), id);
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        var stack = new AuroraStack { VerticalAlignment = VerticalAlignment.Stretch };
        var filtGraph = new VoltFilter(engine, track);
        var env1 = new VoltEnv(engine, track);
        var env2 = new VoltEnv(engine, track);
        int fsel = 0;

        void Refresh()
        {
            stack.Set(Sel("table", 4), G("position"));
            string pre = fsel == 0 ? "fil1" : "fil2";
            if (fsel == 0) filtGraph.Target("FILTER 1", P("cutoff"), P("resonance"), Sel("fil1type", 5));
            else filtGraph.Target("FILTER 2", P("fil2freq"), P("fil2reso"), Sel("fil2type", 5));
            filtGraph.Refresh(); env1.Refresh(); env2.Refresh();
            foreach (var a in readouts) a();
        }

        Control K(string id, string name, bool mod = false, double sz = 32, double cw = 42)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, sz, cw, mod ? TealC : null);
        Control Row(double sp, params Control[] cs) { var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) r.Children.Add(c); return r; }
        Control Lbl(string t, double fs, IBrush c) => new TextBlock { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c, VerticalAlignment = VerticalAlignment.Center };

        Control Chips(string id, string[] names, double fs = 9)
        {
            int n = names.Length; var arr = new Border[n];
            void Hi() { int cur = Sel(id, n); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Brushes.Transparent; arr[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(4, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, Foreground = MutedC } }; c.PointerPressed += (_, _) => { SetP(id, iv / (float)(n - 1)); Hi(); Refresh(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi); Hi();
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }
        // Local segmented toggle (not a param).
        Control Seg(string[] names, int initial, Action<int> onPick, double fs = 9)
        {
            var arr = new Border[names.Length]; int cur = initial;
            void Hi() { for (int i = 0; i < arr.Length; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < names.Length; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, FontWeight = FontWeight.SemiBold, Foreground = MutedC } }; c.PointerPressed += (_, _) => { cur = iv; Hi(); onPick(iv); }; arr[i] = c; row.Children.Add(c); }
            Hi();
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
        }
        // Param-backed on/off pill.
        Control Toggle(string id, string label)
        {
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.SemiBold } };
            void Hi() { bool on = G(id) > 0.5f; b.Background = on ? AmberSubtle : Inset; b.BorderBrush = on ? Amber : Border2; ((TextBlock)b.Child!).Foreground = on ? AmberLit : MutedC; }
            b.PointerPressed += (_, _) => { SetP(id, G(id) > 0.5f ? 0f : 1f); Hi(); Refresh(); };
            readouts.Add(Hi); Hi();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(b, MidiTarget.PluginParam(track, -1, pi), label);
            return b;
        }
        Control Slider(string id, string name, Func<double, string> fmt, double w)
        {
            int pi = I(id);
            var bg = new Border { Width = w, Height = 3, Background = Inset, CornerRadius = new CornerRadius(2) };
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2) };
            var handle = new Border { Width = 8, Height = 9, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2) };
            var canvas = new Canvas { Width = w, Height = 9, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            Canvas.SetTop(bg, 3); Canvas.SetTop(fill, 3); Canvas.SetTop(handle, 0);
            canvas.Children.Add(bg); canvas.Children.Add(fill); canvas.Children.Add(handle);
            var val = new TextBlock { FontSize = 9, Foreground = TxtC, Width = 54, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void Vis(double v) { fill.Width = Math.Max(0, v * w); Canvas.SetLeft(handle, v * w - 4); val.Text = fmt(v); }
            void From(PointerEventArgs e) { double v = Math.Clamp(e.GetPosition(canvas).X / w, 0, 1); if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Vis(v); Refresh(); }
            canvas.PointerPressed += (_, e) => { drag = true; if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(canvas); From(e); };
            canvas.PointerMoved += (_, e) => { if (drag) From(e); };
            canvas.PointerReleased += (_, e) => { if (drag) { drag = false; if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(null); } };
            readouts.Add(() => { if (!drag) Vis(G(id)); });
            Vis(G(id));
            var host = Row(6, Lbl(name, 8, MutedC), canvas, val);
            if (pi >= 0) MidiLearn.Bind(host, MidiTarget.PluginParam(track, -1, pi), name);
            return host;
        }

        // ---- LIVE strip ----
        string Hz(double v) { double f = 20 * Math.Pow(900, v); return f >= 1000 ? $"{f / 1000:0.00} kHz" : $"{f:0} Hz"; }
        var monoSeg = Seg(new[] { "Poly", "Mono" }, G("mono") > 0.5f ? 1 : 0, i => { SetP("mono", i); }, 9);
        DockPanel.SetDock(monoSeg, Dock.Right);
        var liveStrip = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { Margin = new Thickness(9, 0), LastChildFill = false, Children = { monoSeg,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Slider("position", "POS 1", v => $"frame {1 + (int)Math.Round(v * 15)}/16", 76),
                    Slider("cutoff", "CUTOFF", Hz, 76),
                    Slider("unidetune", "UNISON", v => $"{v * 50:0} c", 56) } } } } };

        // ---- Osc tab ----
        // Clickable label that cycles through an n-way selector param (for osc2's bank).
        Control Cycle(string id, string[] names)
        {
            var b = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Border2, Background = Inset, Padding = new Thickness(5, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { FontSize = 8, Foreground = AmberLit } };
            void Hi() => ((TextBlock)b.Child!).Text = names[Sel(id, names.Length)];
            b.PointerPressed += (_, _) => { SetP(id, (float)((Sel(id, names.Length) + 1) % names.Length) / (names.Length - 1)); Hi(); Refresh(); };
            readouts.Add(Hi); Hi(); return b;
        }
        // Compact drag value field (mockup SUB/UNI): a label over a field you drag
        // horizontally; double-click resets to default. Takes less room than a gauge knob.
        Control DragCell(string id, string name, Func<double, string> fmt)
        {
            int pi = I(id);
            var val = new TextBlock { Text = fmt(G(id)), FontSize = 10, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var field = new Border { MinWidth = 40, Height = 17, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(3, 0), Cursor = new Cursor(StandardCursorType.SizeWestEast), Child = val };
            bool drag = false; double sx = 0, sv = 0;
            field.PointerPressed += (_, e) =>
            {
                if (e.ClickCount == 2) { if (pi >= 0) { float d = engine.InstrumentParamDefault(track, pi); engine.PluginParamSet(track, -1, pi, d); val.Text = fmt(d); } Refresh(); e.Handled = true; return; }
                drag = true; sx = e.GetPosition(field).X; sv = G(id); if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(field); e.Handled = true;
            };
            field.PointerMoved += (_, e) => { if (!drag) return; double v = Math.Clamp(sv + (e.GetPosition(field).X - sx) / 120.0, 0, 1); if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); val.Text = fmt(v); Refresh(); };
            field.PointerReleased += (_, e) => { if (drag) { drag = false; if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(null); } };
            readouts.Add(() => { if (!drag) val.Text = fmt(G(id)); });
            return new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = { new TextBlock { Text = name, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center }, field } };
        }
        // Flat oscillator row (no island; rows separated by a divider, per the mockup).
        Control OscRow(string title, Control? head, params Control[] knobs)
        {
            var lab = new StackPanel { Width = 44, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TxtC } } };
            if (head != null) lab.Children.Add(head);
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { lab, Row(2, knobs) } };
        }
        Control HDiv() => new Border { Height = 1, Background = Border2, Margin = new Thickness(2, 1) };
        var tableCap = new TextBlock { FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC };
        var frameCap = new TextBlock { FontSize = 8, Foreground = TxtC };
        readouts.Add(() => { tableCap.Text = $"TABLE · {Banks[Sel("table", 4)].ToUpperInvariant()}"; frameCap.Text = $"{1 + (int)Math.Round(G("position") * 15)} / 16"; });
        Control OscTab()
        {
            // 3-D wavetable view (238px) with the table/frame/mod labels overlaid on top.
            stack.Margin = new Thickness(2);
            var overlay = new StackPanel { Margin = new Thickness(9, 7), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 1, IsHitTestVisible = false,
                Children = { tableCap, frameCap, new TextBlock { Text = "env1 → pos", FontSize = 8, Foreground = TealC } } };
            var view = new Border { Width = 238, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = new Panel { Children = { stack, overlay } } };

            var bankWarp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
                Chips("table", Banks),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Lbl("WARP", 8, MutedC), Chips("osc1warpmode", WarpModes, 8) } } } };
            string OctF(double v) { int o = (int)Math.Round((v - 0.5) * 4) - 1; return $"{o:+0;-0;0} oct"; }
            var subUni = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                Lbl("SUB", 9, TxtC), Chips("subwave", new[] { "sin", "sqr", "tri" }, 8),
                DragCell("sublevel", "LVL", v => $"{v * 100:0}%"), DragCell("suboct", "OCT", OctF),
                new Border { Width = 1, Height = 30, Background = Border2, Margin = new Thickness(3, 0) },
                Lbl("UNI", 9, TxtC),
                DragCell("unison", "VC", v => $"{1 + (int)Math.Round(v * 6)}"),
                DragCell("unidetune", "DET", v => $"{v * 50:0}c"),
                DragCell("unispread", "SPR", v => $"{v * 100:0}%") } };
            Control Kb(string id, string name) => K(id, name, false, 32, 54);
            var rightCol = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = {
                bankWarp,
                OscRow("OSC 1", Toggle("osc1on", "on"), Kb("position", "POS"), Kb("warp", "WARP"), Kb("osc1level", "LEVEL"), Kb("osc1semi", "PITCH")),
                HDiv(),
                OscRow("OSC 2", Cycle("osc2table", Banks), Kb("osc2position", "POS"), Kb("osc2warp", "WARP"), Kb("osc2level", "LEVEL"), Kb("osc2semi", "PITCH")),
                HDiv(),
                subUni } };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
            g.Children.Add(view); Grid.SetColumn(rightCol, 1); g.Children.Add(rightCol);
            return g;
        }

        // ---- Filter tab (type/slope in a top strip; graph + knobs/routing below) ----
        var filtTypeHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var filtSlopeHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var filtKnobsHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var filtCap = new TextBlock { FontSize = 8, Foreground = TealC, VerticalAlignment = VerticalAlignment.Center };
        filtCap.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        readouts.Add(() => { string pre = fsel == 0 ? "fil1" : "fil2"; int en = (int)Math.Round((G(pre + "env") - 0.5) * 200); int lf = (int)Math.Round((G(pre + "lfo") - 0.5) * 200); filtCap.Text = $"← env2 {en:+0;-0;0} · lfo1 {lf:+0;-0;0}"; });
        void ApplyFilterSel()
        {
            string pre = fsel == 0 ? "fil1" : "fil2"; string freq = fsel == 0 ? "cutoff" : "fil2freq", res = fsel == 0 ? "resonance" : "fil2reso";
            filtTypeHost.Content = Chips(pre + "type", FiltTypes);
            filtSlopeHost.Content = Seg(new[] { "12 dB", "24 dB" }, G(pre + "slope") > 0.5f ? 1 : 0, i => { SetP(pre + "slope", i); }, 8);
            filtKnobsHost.Content = Row(2, K(freq, "FREQ"), K(res, "RES"), K(pre + "env", "ENV", true), K(pre + "lfo", "LFO", true));
            Refresh();
        }
        Control RouteRow(string title, string id) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = title, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, Width = 30, VerticalAlignment = VerticalAlignment.Center }, Chips(id, Routes, 8) } };
        filtGraph.VerticalAlignment = VerticalAlignment.Stretch;
        Control FilterTab()
        {
            ApplyFilterSel();
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = {
                Lbl("FILTER", 9, TxtC), Seg(new[] { "1", "2" }, fsel, i => { fsel = i; ApplyFilterSel(); }, 9), filtTypeHost, filtSlopeHost } };
            DockPanel.SetDock(strip, Dock.Left); DockPanel.SetDock(filtCap, Dock.Right);
            var stripBar = new Border { Margin = new Thickness(0, 0, 0, 6), Child = new DockPanel { LastChildFill = false, Children = { strip, filtCap } } };
            var routing = new StackPanel { Spacing = 4, Children = { Lbl("ROUTING", 8, TxtC), RouteRow("Osc 1", "routeosc1"), RouteRow("Osc 2", "routeosc2"), RouteRow("Sub", "routesub"),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Lbl("MODE", 8, MutedC), Seg(new[] { "Parallel", "Series" }, G("filseries") > 0.5f ? 1 : 0, i => { SetP("filseries", i); }, 8) } } } };
            var right = new StackPanel { Width = 206, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { filtKnobsHost, routing } };
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
            body.Children.Add(filtGraph); Grid.SetColumn(right, 1); body.Children.Add(right);
            DockPanel.SetDock(stripBar, Dock.Top);
            return new DockPanel { LastChildFill = true, Children = { stripBar, body } };
        }

        // ---- Env tab ----
        Control EnvPanel(string title, string route, VoltEnv gv, string[] ids)
        {
            gv.Target(title, P(ids[0]), P(ids[1]), P(ids[2]), P(ids[3])); gv.VerticalAlignment = VerticalAlignment.Stretch;
            string[] lbl = { "A", "D", "S", "R" };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 4, Margin = new Thickness(0, 5, 0, 0) };
            for (int k = 0; k < 4; k++) { var v = new TextBlock { Text = Pct(G(ids[k])), FontSize = 9, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center }; v.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); string pid = ids[k]; readouts.Add(() => v.Text = Pct(G(pid))); var cell = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2, 2), Child = new StackPanel { Children = { new TextBlock { Text = lbl[k], FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center }, v } } }; Grid.SetColumn(cell, k); grid.Children.Add(cell); }
            DockPanel.SetDock(grid, Dock.Bottom);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Lbl(title, 9, TxtC), new TextBlock { Text = route, FontSize = 8, Foreground = TealC, VerticalAlignment = VerticalAlignment.Center } } };
            DockPanel.SetDock(head, Dock.Top);
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Child = new DockPanel { LastChildFill = true, Children = { head, grid, gv } } };
        }
        Control EnvTab()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
            var a = EnvPanel("ENV 1", "→ Amp", env1, new[] { "attack", "decay", "sustain", "release" });
            var f = EnvPanel("ENV 2", "→ Position/Cutoff", env2, new[] { "env2attack", "env2decay", "env2sustain", "env2release" });
            Grid.SetColumn(f, 1); g.Children.Add(a); g.Children.Add(f);
            return g;
        }

        // ---- LFO tab ----
        Control LfoLane(string title, Control? extra, params Control[] knobs)
        {
            var head = new StackPanel { Width = 116, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Lbl(title, 9, TxtC) } };
            if (extra != null) head.Children.Add(extra);
            return new Border { Height = 64, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { head, Row(2, knobs) } } };
        }
        Control LfoTab() => new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = {
            LfoLane("LFO 1", Chips("lfo1shape", new[] { "sin", "tri", "sqr", "S&H" }, 8), K("lfo1rate", "RATE"), K("lfo1depth", "DEPTH", true), K("lfo1sync", "SYNC")),
            LfoLane("LFO 2", Chips("lfo2shape", new[] { "sin", "tri", "sqr", "S&H" }, 8), K("lfo2rate", "RATE"), K("lfo2depth", "DEPTH", true)) } };

        // ---- Mod tab (matrix + macros) ----
        var mtxIdx = new int[8, 7];
        for (int s = 0; s < 8; s++) for (int d = 0; d < 7; d++) mtxIdx[s, d] = I($"mtx{s}_{d}");
        var matrix = new VoltMatrix(engine, track, mtxIdx, SrcNames, DestNames) { VerticalAlignment = VerticalAlignment.Stretch };
        readouts.Add(matrix.Refresh);
        Control MacroCell(int m)
        {
            var dest = new TextBlock { FontSize = 8, Foreground = TealC, HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
            void SyncDest() => dest.Text = "→ " + DestNames[Sel($"mac{m}dest", 7)];
            dest.PointerPressed += (_, _) => { SetP($"mac{m}dest", ((Sel($"mac{m}dest", 7) + 1) % 7) / 6f); SyncDest(); };
            readouts.Add(SyncDest); SyncDest();
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 4), Child = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, Children = { Row(2, K($"mac{m}val", $"M{m + 1}"), K($"mac{m}amt", "AMT", true)), dest } } };
        }
        Control ModTab()
        {
            var macros = new StackPanel { Width = 130, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Lbl("MACROS", 8, TxtC) } };
            for (int m = 0; m < 4; m++) macros.Children.Add(MacroCell(m));
            var clear = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "Clear all", FontSize = 9, Foreground = MutedC } };
            clear.PointerPressed += (_, _) => matrix.ClearAll();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4), LastChildFill = false, Children = { clear, Lbl("MOD MATRIX", 9, TxtC) } };
            DockPanel.SetDock(clear, Dock.Right); DockPanel.SetDock(head, Dock.Top);
            var left = new DockPanel { LastChildFill = true, Children = { head, matrix } };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
            g.Children.Add(left); Grid.SetColumn(macros, 1); g.Children.Add(macros);
            return g;
        }

        // ---- FX tab ----
        Control FxTab() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
            new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Child = new StackPanel { Children = { Lbl("DRIVE", 8, TxtC), Row(2, K("fxdrive", "AMOUNT", false, 44, 60)) } } },
            new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Child = new StackPanel { Children = { Lbl("CHORUS", 8, TxtC), Row(2, K("fxchorus", "AMOUNT", false, 44, 60), K("fxchorusrate", "RATE", false, 44, 60)) } } },
            new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Child = new StackPanel { Children = { Lbl("REVERB", 8, TxtC), Row(2, K("fxreverb", "AMOUNT", false, 44, 60)) } } } } };

        // ---- tab rail + body ----
        var tabHost = new ContentControl { VerticalAlignment = VerticalAlignment.Stretch };
        var tabCache = new Control?[6];
        Control TabBody(int t) => tabCache[t] ??= t switch { 1 => FilterTab(), 2 => EnvTab(), 3 => LfoTab(), 4 => ModTab(), 5 => FxTab(), _ => OscTab() };
        string[] tabs = { "Osc", "Filter", "Env", "LFO", "Mod", "FX" };
        bool[] tealTab = { false, false, false, true, true, false };
        int tabSel = 0; var tabBtns = new Border[tabs.Length];
        void HiTabs() { for (int i = 0; i < tabs.Length; i++) { bool on = i == tabSel; tabBtns[i].Background = on ? new SolidColorBrush(Color.Parse("#26231E")) : Brushes.Transparent; tabBtns[i].BorderBrush = on ? (tealTab[i] ? TealC : Amber) : Brushes.Transparent; ((TextBlock)tabBtns[i].Child!).Foreground = on ? TxtC : MutedC; } }
        var railCol = new StackPanel { Spacing = 2 };
        for (int i = 0; i < tabs.Length; i++)
        {
            int iv = i;
            var b = new Border { Height = 22, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 0), BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Brushes.Transparent, Child = new TextBlock { Text = tabs[i], FontSize = 10, FontWeight = FontWeight.Medium, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center } };
            b.PointerPressed += (_, _) => { tabSel = iv; HiTabs(); tabHost.Content = TabBody(iv); };
            tabBtns[i] = b; railCol.Children.Add(b);
        }
        var rail = new Border { Width = 74, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(5, 8), Child = railCol };
        DockPanel.SetDock(rail, Dock.Left);
        var body = new DockPanel { LastChildFill = true, Children = { rail, new Border { Padding = new Thickness(9, 8), Child = tabHost } } };

        DockPanel.SetDock(liveStrip, Dock.Top);
        var dockRoot = new DockPanel { LastChildFill = true, Background = CardBg, Children = { liveStrip, body } };

        HiTabs(); tabHost.Content = TabBody(tabSel);
        ctx.SetInstLiveViz(Refresh);
        Refresh();
        return dockRoot;
    }
}
