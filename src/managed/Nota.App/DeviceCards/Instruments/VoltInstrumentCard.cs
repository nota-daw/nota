// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Volt editor (instrument kind 6), rebuilt to the 2e
// mockup (700×260, the same compact shell as Nota Grain): header (26, with a live
// voice meter) · always-visible LIVE strip (34: Cutoff/Res/Glide sliders + Poly/Mono)
// · vertical tab rail (80: Osc/Filter/Env/LFO/Mod/Macro) swapping the body · output
// rail (96: Gain knob + peak meter + Pan). The filter response and both envelopes are
// interactive drag graphs; the Mod tab is a 7×6 drag matrix; waveform/filter-type are
// icon/text chips; modulation depths use teal knobs. All controls are plugin params;
// the card follows automation live via RefreshSynthLive.

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

internal sealed class VoltInstrumentCard : IInstrumentCard
{
    // Exact mockup palette (matches GrainInstrumentCard).
    private static readonly IBrush CardBg = NotaPalette.BgApp;
    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush Handle = NotaPalette.TextSecondary;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);

    private static readonly string[] DestNames = { "Pitch", "Osc2", "Cutoff", "Reso", "Level", "Pan" };

    public bool BodyOnly => true;
    public string Subtitle => "SUBTRACTIVE";

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
        (int, string) P(string id) => (I(id), id);
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        var filtGraph = new VoltFilter(engine, track);
        var ampEnv = new VoltEnv(engine, track);
        var filtEnv = new VoltEnv(engine, track);
        int fsel = 0;

        void Refresh()
        {
            string pre = fsel == 0 ? "fil1" : "fil2";
            filtGraph.Target(pre == "fil1" ? "FILTER 1" : "FILTER 2", P(pre + "freq"), P(pre + "reso"), Sel(pre + "type", 4));
            filtGraph.Refresh(); ampEnv.Refresh(); filtEnv.Refresh();
            foreach (var a in readouts) a();
        }

        // ---- shared builders ----
        Control K(string id, string name, bool mod = false, double sz = 34, double cw = 44)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, sz, cw, mod ? TealC : null);
        Control Row(double spacing, params Control[] cs)
        { var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) sp.Children.Add(c); return sp; }
        Control Lbl(string t, double fs, IBrush c) => new TextBlock { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c, VerticalAlignment = VerticalAlignment.Center };

        // Param-backed chip strip (writes id = pick/(n-1)); optional per-index tint.
        Control Chips(string id, string[] names, double fs = 9)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        // Waveform icon chips (Saw/Square/Tri/Sine).
        Control WaveChips(string id)
        {
            var arr = new Border[4]; var ic = new WaveIcon[4];
            void Hi() { int cur = Sel(id, 4); for (int i = 0; i < 4; i++) { bool on = i == cur; arr[i].Background = on ? NotaPalette.Accent : Inset; arr[i].BorderBrush = on ? NotaPalette.Accent : Border2; ic[i].Stroke = on ? NotaPalette.TextOnAccent : MutedC; ic[i].InvalidateVisual(); } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 4; i++)
            { int iv = i; var wi = new WaveIcon(i) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; var b = new Border { Width = 26, Height = 18, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Cursor = new Cursor(StandardCursorType.Hand), Child = wi }; b.PointerPressed += (_, _) => { SetP(id, iv / 3f); Hi(); Refresh(); }; arr[i] = b; ic[i] = wi; row.Children.Add(b); }
            readouts.Add(Hi); Hi();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            return row;
        }

        // Local (non-param) segmented toggle.
        Control Seg(string[] names, int initial, Action<int> onPick, double fs = 9)
        {
            int cur = initial;
            return DeviceCardKit.Segments(names, () => cur, iv => { cur = iv; onPick(iv); }, out _);
        }

        // Header (dot / name / subtitle / voice count) is provided by the shared shell.

        // ---- LIVE strip (always visible) ----
        Control Slider(string id, double trackW, string name, Func<double, string> fmt)
        {
            int pi = I(id);
            var row = DeviceCardKit.SliderRow(name, () => G(id), n => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)n); Refresh(); }, () => fmt(G(id)), out var sync,
                begin: () => { if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); },
                end: () => { if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); },
                trackWidth: trackW, valueWidth: 46);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), name);
            return row;
        }
        string GlideFmt(double v) => v <= 0.001 ? "off" : $"{20 * Math.Pow(120, v):0}\u2009ms";
        var monoSeg = Seg(new[] { "Poly", "Mono" }, G("mono") > 0.5f ? 1 : 0, i => { SetP("mono", i); Refresh(); }, 9);
        DockPanel.SetDock(monoSeg, Dock.Right);
        var liveStrip = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { Margin = new Thickness(9, 0), LastChildFill = false, Children = { monoSeg,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Slider("fil1freq", 88, "CUTOFF", v => { double hz = 20 * Math.Pow(900, v); return hz >= 1000 ? $"{hz / 1000:0.0}\u2009k" : $"{hz:0}\u2009Hz"; }),
                    Slider("fil1reso", 66, "RESO", v => $"{v:0.00}"),
                    Slider("glide", 60, "GLIDE", GlideFmt) } } } } };

        // ---- tab bodies ----
        // Osc tab: three lanes (Osc1 / Osc2 / Noise) with a shared knob column.
        Control OscLane(string title, Control picker, params Control[] knobs)
        {
            var head = new StackPanel { Width = 96, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Lbl(title, 9, TxtC), picker } };
            var kr = Row(2, knobs);
            var lane = new Border { Height = 62, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(8, 2),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { head, kr } } };
            return lane;
        }
        var mixBar = new MixBar(engine, track, new[] { I("osc1level"), I("osc2level"), I("noise") });
        readouts.Add(mixBar.Refresh);
        Control OscTab() => new StackPanel { Spacing = 5, Children = {
            OscLane("OSC 1", WaveChips("osc1wave"), K("osc1octave", "OCT"), K("osc1semi", "SEMI"), K("osc1detune", "FINE"), K("osc1level", "LEVEL"), K("osc1route", "ROUTE"), K("osc1phase", "PHASE")),
            OscLane("OSC 2", WaveChips("osc2wave"), K("osc2octave", "OCT"), K("osc2semi", "SEMI"), K("osc2detune", "FINE"), K("osc2level", "LEVEL"), K("osc2route", "ROUTE"), K("osc2phase", "PHASE")),
            OscLane("NOISE", Chips("noisecolor", new[] { "dark", "pink", "white" }),
                K("noise", "LEVEL"), K("noisecolor", "COLOR"), K("noiseroute", "ROUTE"),
                new StackPanel { Width = 120, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "MIX SUM", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC }, mixBar } }) } };

        // Filter tab: big response graph + Fil1/Fil2 + type/slope + vertical knob list.
        var filtRight = new StackPanel { Width = 132, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        void FillFilterRight()
        {
            string pre = fsel == 0 ? "fil1" : "fil2";
            filtRight.Children.Clear();
            filtRight.Children.Add(Chips(pre + "type", new[] { "LP", "HP", "BP", "Notch" }));
            filtRight.Children.Add(Seg(new[] { "12\u2009dB", "24\u2009dB" }, G(pre + "slope") > 0.5f ? 1 : 0, i => { SetP(pre + "slope", i); Refresh(); }, 8));
            filtRight.Children.Add(Row(2, K(pre + "freq", "CUTOFF"), K(pre + "reso", "RESO")));
            filtRight.Children.Add(Row(2, K(pre + "env", "ENV", true), K(pre + "key", "KEY", true)));
        }
        filtGraph.VerticalAlignment = VerticalAlignment.Stretch;
        Control FilterTab()
        {
            FillFilterRight();
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
            g.Children.Add(filtGraph);
            var right = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Lbl("FILTER", 9, TxtC), Seg(new[] { "1", "2" }, fsel, i => { fsel = i; FillFilterRight(); Refresh(); }, 9) } },
                filtRight } };
            Grid.SetColumn(right, 1); g.Children.Add(right);
            return g;
        }

        // Env tab: amp + filter envelopes side by side with A/D/S/R value cells.
        Control EnvPanel(string title, string route, VoltEnv gView, string[] ids)
        {
            gView.Target(title, P(ids[0]), P(ids[1]), P(ids[2]), P(ids[3]));
            gView.VerticalAlignment = VerticalAlignment.Stretch;
            string[] lbl = { "A", "D", "S", "R" };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 4, Margin = new Thickness(0, 5, 0, 0) };
            for (int k = 0; k < 4; k++)
            {
                var v = new TextBlock { Text = Pct(G(ids[k])), FontSize = 9, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center };
                v.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
                string pid = ids[k]; var tv = v; readouts.Add(() => tv.Text = Pct(G(pid)));
                var cell = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(2, 2),
                    Child = new StackPanel { Children = { new TextBlock { Text = lbl[k], FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center }, v } } };
                Grid.SetColumn(cell, k); grid.Children.Add(cell);
            }
            DockPanel.SetDock(grid, Dock.Bottom);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Lbl(title, 9, TxtC), new TextBlock { Text = route, FontSize = 8, Foreground = TealC, VerticalAlignment = VerticalAlignment.Center } } };
            DockPanel.SetDock(head, Dock.Top);
            var body = new DockPanel { LastChildFill = true, Children = { head, grid, gView } };
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(8, 6), Child = body };
        }
        Control EnvTab()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
            var a = EnvPanel("AMP ENV", "→ Level", ampEnv, new[] { "attack", "decay", "sustain", "release" });
            var f = EnvPanel("FILTER ENV", "→ Cutoff", filtEnv, new[] { "fattack", "fdecay", "fsustain", "frelease" });
            Grid.SetColumn(f, 1); g.Children.Add(a); g.Children.Add(f);
            return g;
        }

        // LFO tab: LFO 1 / LFO 2 / Vibrato lanes.
        Control LfoLane(string title, Control? extras, params Control[] knobs)
        {
            var head = new StackPanel { Width = 116, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Lbl(title, 9, TxtC) } };
            if (extras != null) head.Children.Add(extras);
            var lane = new Border { Height = 62, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(8, 2),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { head, Row(2, knobs) } } };
            return lane;
        }
        Control LfoTab() => new StackPanel { Spacing = 5, Children = {
            LfoLane("LFO 1", Chips("lfo1shape", new[] { "sin", "tri", "sqr", "S&H" }, 8), K("lfo1rate", "RATE"), K("lfo1depth", "DEPTH", true), K("lfo1sync", "SYNC"), K("lfo1fade", "FADE", true)),
            LfoLane("LFO 2", Chips("lfo2shape", new[] { "sin", "tri", "sqr", "S&H" }, 8), K("lfo2rate", "RATE"), K("lfo2depth", "DEPTH", true), K("lfo2sync", "SYNC"), K("lfo2fade", "FADE", true)),
            LfoLane("VIBRATO", null, K("vibrate", "RATE"), K("vibamt", "DEPTH", true)) } };

        // Mod tab: 7×6 drag matrix.
        var mtxIdx = new int[7, 6];
        for (int s = 0; s < 7; s++) for (int d = 0; d < 6; d++) mtxIdx[s, d] = I($"mtx{s}_{d}");
        var matrix = new VoltMatrix(engine, track, mtxIdx, new[] { "Amp Env", "Flt Env", "LFO 1", "LFO 2", "Velocity", "Key", "Mod Whl" }, DestNames) { VerticalAlignment = VerticalAlignment.Stretch };
        readouts.Add(matrix.Refresh);
        Control ModTab()
        {
            var clear = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "Clear all", FontSize = 9, Foreground = MutedC } };
            clear.PointerPressed += (_, _) => matrix.ClearAll();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4), LastChildFill = false, Children = { clear,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { Lbl("MOD MATRIX", 9, TxtC), new TextBlock { Text = "drag a cell · up +, down −", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center } } } } };
            DockPanel.SetDock(clear, Dock.Right);
            DockPanel.SetDock(head, Dock.Top);
            return new DockPanel { LastChildFill = true, Children = { head, matrix } };
        }

        // Macro tab: 8 macros (value knob + destination cycle + amount).
        Control MacroCell(int m)
        {
            var dest = new TextBlock { FontSize = 8, Foreground = TealC, HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
            void SyncDest() => dest.Text = "→ " + DestNames[Sel($"mac{m}dest", 6)];
            dest.PointerPressed += (_, _) => { SetP($"mac{m}dest", ((Sel($"mac{m}dest", 6) + 1) % 6) / 5f); SyncDest(); };
            readouts.Add(SyncDest); SyncDest();
            var body = new StackPanel { Width = 116, Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, Children = {
                Row(2, K($"mac{m}val", $"MACRO {m + 1}"), K($"mac{m}amt", "AMOUNT", true)), dest } };
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(4, 4), Child = body };
        }
        Control MacroTab()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("*,*"), ColumnSpacing = 5, RowSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            for (int m = 0; m < 8; m++) { var c = MacroCell(m); Grid.SetColumn(c, m % 4); Grid.SetRow(c, m / 4); g.Children.Add(c); }
            return g;
        }

        // ---- output rail: Gain + Pan (the peak meter now lives in the shared shell header) ----
        var outRail = new Border { Width = 96, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 8),
            Child = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
                K("volume", "GAIN", false, 40, 60), K("outpan", "PAN", false, 36, 54) } } };
        DockPanel.SetDock(outRail, Dock.Right);

        // ---- tab rail + swapped body ----
        string[] tabs = { "Osc", "Filter", "Env", "LFO", "Mod", "Macro" };
        bool[] tealTab = { false, false, false, true, true, false };
        var tabHost = new ContentControl { VerticalAlignment = VerticalAlignment.Stretch };
        // Build each tab once and cache it: several tabs embed a single shared control
        // (mix bar, filter/env graphs, matrix), so rebuilding on every switch would try
        // to re-parent them (crash) and re-register live-follow knobs (leak).
        var tabCache = new Control?[tabs.Length];
        Control TabBody(int t) => tabCache[t] ??= t switch { 1 => FilterTab(), 2 => EnvTab(), 3 => LfoTab(), 4 => ModTab(), 5 => MacroTab(), _ => OscTab() };
        int tabSel = 0; var tabBtns = new Border[tabs.Length];
        void HiTabs() { for (int i = 0; i < tabs.Length; i++) { bool on = i == tabSel; tabBtns[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent; tabBtns[i].BorderBrush = on ? (tealTab[i] ? TealC : Amber) : Brushes.Transparent; ((TextBlock)tabBtns[i].Child!).Foreground = on ? TxtC : MutedC; } }
        var railCol = new StackPanel { Spacing = 2 };
        for (int i = 0; i < tabs.Length; i++)
        {
            int iv = i;
            var b = new Border { Height = 22, CornerRadius = NotaRadius.Control, Padding = new Thickness(8, 0), BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Brushes.Transparent,
                Child = new TextBlock { Text = tabs[i], FontSize = 9, FontWeight = FontWeight.Medium, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center } };
            b.PointerPressed += (_, _) => { tabSel = iv; HiTabs(); tabHost.Content = TabBody(iv); };
            tabBtns[i] = b; railCol.Children.Add(b);
        }
        var rail = new Border { Width = 80, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(5, 8), Child = railCol };
        DockPanel.SetDock(rail, Dock.Left);
        var body = new DockPanel { LastChildFill = true, Children = { rail, outRail, new Border { Padding = new Thickness(10, 8), Child = tabHost } } };

        // ---- assemble (body only; the shared shell provides the header + frame) ----
        DockPanel.SetDock(liveStrip, Dock.Top);
        var dockRoot = new DockPanel { LastChildFill = true, Background = CardBg, Children = { liveStrip, body } };

        HiTabs(); tabHost.Content = TabBody(tabSel);
        ampEnv.Target("AMP ENV", P("attack"), P("decay"), P("sustain"), P("release"));
        filtEnv.Target("FILTER ENV", P("fattack"), P("fdecay"), P("fsustain"), P("frelease"));
        ctx.SetInstLiveViz(Refresh);
        Refresh();
        return dockRoot;
    }
}
