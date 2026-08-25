// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Flux editor (instrument kind 11), built to the
// shared 900×260 card shell (header 26 · body 234). Three
// columns:
//   VECTOR — a draggable XY pad whose four corners are timbre "worlds" (WARM/GLASS/MOOG/GRAIN),
//            with the four-corner radial gradient and a teal ghost dot for Motion + React.
//   REACT  — the signature feature: a sidechain source picker + a live analysis scope
//            (env / transients / tilt) + LISTEN amount + TARGET (Filter/Pitch/Space/Vector).
//   MACROS — five one-knob controls (Age / Motion / Filter / Env / Space), the rest adaptive.
// Every parameter is a plugin-param → automation / persist / clone. The knobs are the shared
// Nota gauge Knob (not the mockup's flat gauges).

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

internal sealed class FluxInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "VECTOR";
    public double CardWidth => 900;

    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#171613"));
    private static readonly IBrush ReactBg = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush Inset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush Border2 = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush TealB = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush TealBright = new SolidColorBrush(Color.Parse("#7FC9C6"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6A5E"));

    private static readonly string[] Targets = { "Filter", "Pitch", "Space", "Vector" };
    private static readonly string[] RateNames = { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16" };

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        float G(string id) => I(id) is var i and >= 0 ? engine.PluginParamGet(track, -1, i) : 0f;
        void SetId(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        void Begin(string id) { if (I(id) >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void End(string id) { if (I(id) >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }

        var readouts = new List<Action>();
        TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        TextBlock Sec(string t, IBrush? c = null) => new() { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? Muted, VerticalAlignment = VerticalAlignment.Center };

        int TargetIdx() => Math.Clamp((int)Math.Round(G("target") * 3f), 0, 3);
        int RateIdx() => Math.Clamp((int)Math.Round(G("motrate") * 5f), 0, 5);
        string AgeWord(float v) => v < 0.12f ? "clean" : v < 0.45f ? "vintage" : v < 0.78f ? "worn" : "broken";

        // ---- VECTOR column --------------------------------------------------
        var pad = new FluxVectorPad { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        pad.ValueChanged += (x, y) => { SetId("vecx", (float)x); SetId("vecy", (float)y); };
        pad.GestureBegin += () => { Begin("vecx"); Begin("vecy"); };
        pad.GestureEnd += () => { End("vecx"); End("vecy"); };
        var vecReadout = Mono("", Muted, 8);
        readouts.Add(() => vecReadout.Text = $"x {G("vecx"):0.00} · y {G("vecy"):0.00}");
        var vecHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 12 };
        var vr = vecReadout; vr.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(vr, 1);
        vecHeader.Children.Add(Sec("VECTOR")); vecHeader.Children.Add(vr);
        var vecDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(vecHeader, Dock.Top); vecDock.Children.Add(vecHeader);
        vecDock.Children.Add(new Border { Margin = new Thickness(0, 5, 0, 0), Child = pad });
        var vecCol = new Border { Width = 238, Padding = new Thickness(9, 8), Child = vecDock };

        // ---- REACT column ---------------------------------------------------
        var scope = new FluxReactScope { VerticalAlignment = VerticalAlignment.Stretch };
        var scopeVals = new float[8];

        // source picker.
        var srcCombo = new ComboBox { FontSize = 9, MinWidth = 108, Foreground = TealBright, VerticalAlignment = VerticalAlignment.Center };
        var srcIds = new List<int> { -1 };
        srcCombo.Items.Add("No source");
        int nt = engine.TrackCount;
        for (int i = 0; i < nt; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue;
            string kd = ti.IsReturn ? "Return" : ti.IsInstrument ? "Instr" : "Audio";
            srcIds.Add(ti.Id);
            srcCombo.Items.Add($"{i + 1} · {kd}");
        }
        srcCombo.SelectedIndex = Math.Max(0, srcIds.IndexOf(engine.InstrumentSidechainSource(track)));
        srcCombo.SelectionChanged += (_, _) =>
        {
            int sel = srcCombo.SelectedIndex;
            if (sel < 0 || sel >= srcIds.Count) return;
            engine.SetInstrumentSidechainSource(track, srcIds[sel]);
            ctx.NotifyChanged();
        };
        var reactHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 16 };
        reactHead.Children.Add(Sec("REACT", TealB));
        var reactSub = Sec("the synth listens to a track"); reactSub.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(reactSub, 1);
        reactHead.Children.Add(reactSub);
        Grid.SetColumn(srcCombo, 2); reactHead.Children.Add(srcCombo);

        var scopeHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        scopeHead.Children.Add(Sec("SIDECHAIN"));
        var scopeSub = Mono("env · transients · tilt", TealB, 8); scopeSub.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(scopeSub, 1);
        scopeHead.Children.Add(scopeSub);
        var scopeDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(scopeHead, Dock.Top); scopeDock.Children.Add(scopeHead);
        scopeDock.Children.Add(new Border { Margin = new Thickness(0, 3, 0, 0), Child = scope });
        var scopeBox = new Border { Background = Inset, BorderBrush = new SolidColorBrush(Color.Parse("#221F1A")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4), Child = scopeDock };

        // LISTEN knob + TARGET segmented.
        var listen = InstrumentControls.InstKnob(ctx, idx, "listen", "LISTEN", DoRefresh, v => $"{v * 100:0} %", 40, 56, TealB);
        var targetChips = new Border[4];
        var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        void SyncTarget() { int cur = TargetIdx(); for (int i = 0; i < 4; i++) { bool on = i == cur; targetChips[i].Background = on ? new SolidColorBrush(Color.FromArgb(0x28, 0x5B, 0x9E, 0x9C)) : Brushes.Transparent; targetChips[i].BorderBrush = on ? TealB : Brushes.Transparent; ((TextBlock)targetChips[i].Child!).Foreground = on ? TealBright : Muted; } }
        for (int i = 0; i < 4; i++)
        {
            int iv = i;
            var chip = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(7, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = Targets[i], FontSize = 9, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center } };
            chip.PointerPressed += (_, _) => { Begin("target"); SetId("target", iv / 3f); End("target"); SyncTarget(); };
            targetChips[i] = chip; targetRow.Children.Add(chip);
        }
        readouts.Add(SyncTarget);
        var targetBox = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), Child = targetRow };
        var targetStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            Sec("TARGET — what the reaction drives"), targetBox,
            Mono("→ sidechain drags the vector in rhythm", Muted, 8),
        } };
        var reactBottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { listen, targetStack } };
        var reactCol = new Border
        {
            Width = 340, Background = ReactBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 2, 1, 0),
            Padding = new Thickness(10, 8), Child = new DockPanel { LastChildFill = true }
        };
        var reactDock = (DockPanel)reactCol.Child!;
        DockPanel.SetDock(reactHead, Dock.Top); reactDock.Children.Add(reactHead);
        DockPanel.SetDock(reactBottom, Dock.Bottom); reactDock.Children.Add(reactBottom);
        reactDock.Children.Add(new Border { Margin = new Thickness(0, 6, 0, 6), Child = scopeBox });

        // ---- MACROS column --------------------------------------------------
        // A custom macro cell so Motion's readout can cycle the sync rate on click.
        Control Macro(string id, string name, Func<float, string> fmt, Action<TextBlock>? decorate = null)
        {
            if (!idx.TryGetValue(id, out var i)) return new Panel();
            var value = Mono(fmt(G(id)), TextPrimary, 8);
            var knob = new Knob(G(id), 1.0) { Accent = true, Default = engine.InstrumentParamDefault(track, i), Width = 40, Height = 40 };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, i, (float)v); value.Text = fmt((float)v); DoRefresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(i, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, i), name);
            decorate?.Invoke(value);
            return KnobCell(name, knob, value, 56);
        }

        var macroRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            Macro("age", "AGE", v => AgeWord(v)),
            Macro("motion", "MOTION", v => $"{RateNames[RateIdx()]} · {v * 100:0}%", MakeRateCycler),
            Macro("filter", "FILTER", v => $"{60 * Math.Pow(300, v):0} Hz"),
            Macro("env", "ENV", v => v < 0.4f ? "pad" : v < 0.6f ? "pad⇢pluck" : "pluck"),
            Macro("space", "SPACE", v => $"{v * 100:0} %"),
        } };
        var macroHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 12 };
        macroHead.Children.Add(Sec("MACROS"));
        var mh = Sec("one knob each · rest is adaptive"); mh.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(mh, 1);
        macroHead.Children.Add(mh);
        var macroDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(macroHead, Dock.Top); macroDock.Children.Add(macroHead);
        var macroFoot = Mono("resonance · attack/release · unison — adaptive", new SolidColorBrush(Color.Parse("#4A463D")), 8);
        macroFoot.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(macroFoot, Dock.Bottom); macroDock.Children.Add(macroFoot);
        macroDock.Children.Add(macroRow);
        var macroCol = new Border { Padding = new Thickness(10, 8), Child = macroDock };

        // Click the Motion readout to cycle the tempo-sync rate.
        void MakeRateCycler(TextBlock value)
        {
            value.Cursor = new Cursor(StandardCursorType.Hand);
            SetToolTip(value, "Click to change Motion sync rate");
            value.PointerPressed += (_, _) =>
            {
                int r = (RateIdx() + 1) % 6;
                Begin("motrate"); SetId("motrate", r / 5f); End("motrate");
                DoRefresh();
            };
        }

        // ---- assemble body --------------------------------------------------
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Background = Ink };
        Grid.SetColumn(vecCol, 0); Grid.SetColumn(reactCol, 1); Grid.SetColumn(macroCol, 2);
        body.Children.Add(vecCol); body.Children.Add(reactCol); body.Children.Add(macroCol);

        void DoRefresh() { foreach (var r in readouts) r(); }

        void Refresh()
        {
            int n = engine.InstrumentScope(track, scopeVals);
            if (n >= 4)
            {
                scope.Push(scopeVals[0], scopeVals[1], scopeVals[2]);   // env, transient, tilt
                pad.SetValue(G("vecx"), G("vecy"));
                if (n >= 6)
                {
                    double ex = scopeVals[4], ey = scopeVals[5];
                    bool moved = Math.Abs(ex - G("vecx")) > 0.012 || Math.Abs(ey - G("vecy")) > 0.012;
                    pad.SetGhost(ex, ey, moved);
                }
            }
            DoRefresh();
        }
        ctx.SetInstLiveViz(Refresh);
        DoRefresh();
        return body;
    }

    private static void SetToolTip(Control c, string text) => ToolTip.SetTip(c, text);
}
