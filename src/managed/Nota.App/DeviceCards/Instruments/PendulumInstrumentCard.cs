// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Pendulum editor (instrument kind 8), rebuilt to mockup 2l
// (700×260 on the shared shell). The pendulum field IS the instrument, so it gets the
// stage: a LIVE strip (Sync/Free · divisions · bipolar Rate · Motion curve) over a body
// of tab rail (70: Balls / Voice + ball count) | the field viz | a right area that is
// either the per-ball list (Balls tab) or the voice/env knobs + spread rail (Voice tab,
// with the field collapsed to a 62px strip so it's never lost). Teal marks only the
// generative bits — the swing paths and Humanize. All controls are plugin params →
// automation / persist / clone.

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

internal sealed class PendulumInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "GEN KEYS";
    public double CardWidth => 700;

    private const int MaxBalls = 6;
    private static readonly double[] DivBeats = { 4, 2, 1, 0.5, 0.25 };
    private static readonly string[] DivNames = { "1/1", "1/2", "1/4", "1/8", "1/16" };
    private static readonly string[] MotionNames = { "Linear", "Pendulum", "Ease", "Bounce" };
    private static readonly string[] WaveNames = { "Keys", "Glass", "Saw", "Sqr", "Bell" };
    private static readonly string[] QuantNames = { "Off", "1/16", "1/8" };
    private static readonly string[] ModeNames = { "Off", "Major", "Minor", "Dorian", "Mixo", "Penta" };
    private static readonly string[] RootNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);
    private static readonly IBrush RowLit = NotaPalette.SurfaceRaised;

    private static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        int GI(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);
        void SetId(string id, double v) { if (I(id) is var ci and >= 0) engine.PluginParamSet(track, -1, ci, (float)v); }

        var readouts = new List<Action>();
        var fieldFull = new PendulumViz { VerticalAlignment = VerticalAlignment.Stretch };
        var fieldMini = new PendulumViz(mini: true) { Height = 62 };
        var heldBuf = new int[16];
        int heldN = 0;

        void Refresh()
        {
            int count = Math.Clamp(2 + (int)Math.Round(G("balls") * 4), 2, MaxBalls);
            int motion = GI("motion", 4);
            double spread = G("spread");
            double rateMult = (G("rate") - 0.5) * 4.0;
            bool synced = G("sync") >= 0.5f;
            double divB = DivBeats[GI("division", 5)];
            double freeSec = Exp(G("freerate"), 0.1, 4.0);
            double cyclesPerSec = synced ? rateMult * (120.0 / 60.0) / divB : rateMult / freeSec;
            heldN = engine.InstrumentHeldNotes(track, heldBuf);
            var held = new int[heldN];
            Array.Copy(heldBuf, held, heldN);
            fieldFull.Set(count, motion, cyclesPerSec, spread, held);
            fieldMini.Set(count, motion, cyclesPerSec, spread, held);
            foreach (var a in readouts) a();
        }

        // ---------- shared builders ----------
        Control Cap(string t, IBrush? c = null) => new TextBlock { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };

        // Param-backed segmented control (fill = active choice), writing i/(n-1).
        Control Seg(string id, string[] names, double fs = 9, double padX = 6)
        {
            int n = names.Length; var arr = new Border[n];
            void Hi() { int cur = GI(id, n); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Brushes.Transparent; arr[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++)
            {
                int iv = i;
                var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(padX, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, Foreground = MutedC } };
                c.PointerPressed += (_, _) => { SetId(id, n > 1 ? iv / (double)(n - 1) : 0); Refresh(); };
                arr[i] = c; row.Children.Add(c);
            }
            readouts.Add(Hi);
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        // Outlined 0/1 toggle (filled only when on); teal option marks modulation.
        Control OutToggle(string id, string label, bool teal = false)
        {
            var accent = teal ? TealC : Amber;
            var accentLit = teal ? TealC : AmberLit;
            var fillOn = teal ? NotaPalette.Wash(NotaPalette.Teal, 0x24) : AmberSubtle;
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold } };
            void Hi() { bool on = G(id) > 0.5f; b.Background = on ? fillOn : Brushes.Transparent; b.BorderBrush = on ? accent : Border2; ((TextBlock)b.Child!).Foreground = on ? accentLit : MutedC; }
            b.PointerPressed += (_, _) => { SetId(id, G(id) > 0.5f ? 0 : 1); Refresh(); };
            readouts.Add(Hi);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(b, MidiTarget.PluginParam(track, -1, pi), label);
            return b;
        }

        // Momentary action button (writes 1 → the engine self-clears it).
        Control ActionBtn(string label, Action click)
        {
            var b = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Border2, Background = RowLit, Padding = new Thickness(0, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = label, FontSize = 9, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, _) => click();
            return b;
        }

        // Real-unit gauge knob (own live-follow so the unit text isn't overwritten by Pct).
        Control KUnit(string id, string name, Func<double, string> fmt, bool mod = false, double size = 34, double cellW = 54)
        {
            if (!idx.TryGetValue(id, out var i)) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), FontSize = 8, Foreground = TxtC };
            value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var knob = new Knob(G(id), 1.0) { Accent = true, ArcColor = mod ? Teal : null, Default = engine.InstrumentParamDefault(track, i), Width = size, Height = size };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, i, (float)v); value.Text = fmt(v); Refresh(); };
            knob.GestureBegin += () => engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
            knob.GestureEnd += () => engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, i), name);
            readouts.Add(() => { if (!knob.Dragging) { double v = G(id); if (Math.Abs(v - knob.Value) > 1e-3) knob.Value = v; value.Text = fmt(v); } });
            return KnobCell(name, knob, value, cellW);
        }

        // Horizontal param slider (label · track · value). Optional teal (modulation) fill.
        Control HSlider(string id, string label, Func<double, string> fmt, bool teal = false, double lw = 38)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var fill = new Border { Height = 3, Background = teal ? TealC : Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var track2 = new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 12, Children = { track2, fill } };
            var val = new TextBlock { Text = fmt(G(id)), FontSize = 9, Foreground = TxtC, Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void SetFromX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); engine.PluginParamSet(track, -1, pi, (float)v); val.Text = fmt(v); fill.Width = v * slot.Bounds.Width; }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); SetFromX(e.GetPosition(slot).X); Refresh(); };
            slot.PointerMoved += (_, e) => { if (drag) SetFromX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); } };
            MidiLearn.Bind(slot, MidiTarget.PluginParam(track, -1, pi), label);
            readouts.Add(() => { if (!drag) { double v = G(id); val.Text = fmt(v); fill.Width = v * slot.Bounds.Width; } });
            var lbl = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = teal ? TealC : MutedC, Width = lw, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5 };
            g.Children.Add(lbl);
            Grid.SetColumn(slot, 1); g.Children.Add(slot);
            Grid.SetColumn(val, 2); g.Children.Add(val);
            return g;
        }

        // Bipolar Rate slider: centre detent, fills from the middle, signed %, reverse hint.
        Control RateBipolar()
        {
            const double TW = 84;
            var basec = new Border { Height = 5, Background = Inset, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
            var center = new Border { Width = 1, Background = NotaPalette.BorderStrong, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(4, 1) };
            var fill = new Border { Height = 5, Background = Amber, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 8, Height = 11, Background = NotaPalette.TextSecondary, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Width = TW, Height = 11, Children = { basec, center, fill, handle } };
            var val = new TextBlock { Text = "", FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void Upd() { double v = G("rate"); double cxp = TW * 0.5, hx = v * TW; handle.Margin = new Thickness(Math.Clamp(hx - 4, 0, TW - 8), 0, 0, 0); double a = Math.Min(cxp, hx), b = Math.Max(cxp, hx); fill.Margin = new Thickness(a + 4, 0, 0, 0); fill.Width = Math.Max(0, b - a - 4); val.Text = $"{(v - 0.5) * 2 * 100:+0;-0;0} %"; }
            void SetFromX(double x) { SetId("rate", Math.Clamp(x / TW, 0, 1)); Upd(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "rate"); SetFromX(e.GetPosition(slot).X); Refresh(); };
            slot.PointerMoved += (_, e) => { if (drag) SetFromX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "rate"); } };
            readouts.Add(() => { if (!drag) Upd(); });
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                Cap("RATE"), slot, val, new TextBlock { Text = "← reverse", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center } } };
        }

        string Pct(double v) => $"{v * 100:0} %";
        string Ms(double v, double lo, double hi) { double s = Exp(v, lo, hi); return s < 1.0 ? $"{s * 1000:0} ms" : $"{s:0.00} s"; }
        string Db(double v) => v <= 0.001 ? "−∞ dB" : $"{20 * Math.Log10(v):+0.0;−0.0;0.0} dB";
        string Cents(double v) => $"{v * 14:0} c";
        string Tone(double v) => v < 0.34 ? "dark" : v < 0.67 ? "warm" : "bright";

        // ---------- LIVE strip ----------
        var motionSeg = Seg("motion", MotionNames, 9, 7);
        var live = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Seg("sync", new[] { "Free", "Sync" }, 9, 7), Seg("division", DivNames, 8, 5), RateBipolar() } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right, Children = {
                    Cap("MOTION"), motionSeg } } } } };

        // ---------- tab rail ----------
        var tabNames = new[] { "Balls", "Voice" };
        var tabBtns = new Border[tabNames.Length];
        int tab = 0;
        var bodyContent = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Control ballsTab = new Panel(), voiceTab = new Panel();
        void SelectTab(int t)
        {
            tab = t; bodyContent.Content = t == 0 ? ballsTab : voiceTab;
            for (int i = 0; i < tabBtns.Length; i++) { bool on = i == t; tabBtns[i].Background = on ? RowLit : Brushes.Transparent; tabBtns[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)tabBtns[i].Child!).Foreground = on ? TxtC : MutedC; }
        }
        var railCol = new StackPanel { Spacing = 2 };
        for (int i = 0; i < tabNames.Length; i++)
        {
            int ti = i;
            var b = new Border { Height = 20, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 0), BorderThickness = new Thickness(2, 0, 0, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = tabNames[i], FontSize = 10, FontWeight = FontWeight.Medium, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center } };
            b.PointerPressed += (_, _) => SelectTab(ti);
            tabBtns[i] = b; railCol.Children.Add(b);
        }
        // Ball-count stepper pinned to the rail bottom.
        var ballsNum = new TextBlock { Text = "3", FontSize = 9, Foreground = TxtC, [DockPanel.DockProperty] = Dock.Right };
        ballsNum.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        void StepBalls(int d) { int c = Math.Clamp(2 + (int)Math.Round(G("balls") * 4) + d, 2, MaxBalls); SetId("balls", (c - 2) / 4.0); Refresh(); }
        Border StepBtn(string t, int d) { var b = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = NotaPalette.BorderStrong, Background = RowLit, Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = t, FontSize = 9, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center } }; b.PointerPressed += (_, _) => StepBalls(d); return b; }
        var stepGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 3 };
        var sm = StepBtn("−", -1); var sp = StepBtn("+", +1); Grid.SetColumn(sp, 1); stepGrid.Children.Add(sm); stepGrid.Children.Add(sp);
        var ballsBox = new StackPanel { Spacing = 3, Children = {
            new DockPanel { LastChildFill = false, Children = { Cap("BALLS"), ballsNum } }, stepGrid } };
        DockPanel.SetDock(railCol, Dock.Top); DockPanel.SetDock(ballsBox, Dock.Bottom);
        readouts.Add(() => ballsNum.Text = Math.Clamp(2 + (int)Math.Round(G("balls") * 4), 2, MaxBalls).ToString());
        var tabRail = new Border { Width = 70, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(5), Child = new DockPanel { LastChildFill = false, Children = { railCol, ballsBox } } };

        // ---------- Balls tab: field + per-ball list ----------
        var fieldWrap = new Border { Padding = new Thickness(8, 7), Child = fieldFull };

        var rowBorder = new Border[MaxBalls]; var rowDot = new Border[MaxBalls];
        var rDiv = new TextBlock[MaxBalls]; var rRate = new TextBlock[MaxBalls]; var rPhase = new TextBlock[MaxBalls]; var rDir = new TextBlock[MaxBalls]; var rNote = new TextBlock[MaxBalls];
        var ballList = new StackPanel { Spacing = 3 };
        for (int i = 0; i < MaxBalls; i++)
        {
            TextBlock Mn(double w, IBrush c) { var t = new TextBlock { FontSize = 9, Foreground = c, Width = w, VerticalAlignment = VerticalAlignment.Center }; t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return t; }
            rDot(i, out var dot); rowDot[i] = dot;
            rDiv[i] = Mn(30, TxtC); rRate[i] = Mn(30, NotaPalette.TextSecondary); rPhase[i] = Mn(30, MutedC);
            rDir[i] = new TextBlock { FontSize = 9, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center };
            rNote[i] = new TextBlock { FontSize = 9, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right };
            rNote[i].BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var inner = new DockPanel { LastChildFill = false, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, rDiv[i], rRate[i], rPhase[i], rDir[i] } }, rNote[i] } };
            rowBorder[i] = new Border { BorderThickness = new Thickness(1), BorderBrush = Border2, Background = RailBg, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3), Child = inner };
            ballList.Children.Add(rowBorder[i]);
        }
        void rDot(int i, out Border dot) => dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Amber, VerticalAlignment = VerticalAlignment.Center };

        var listHeader = new DockPanel { LastChildFill = false, Children = {
            Cap("BALLS"), new TextBlock { Text = "rate · phase · note", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right } } };
        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
            Cap("SORT"), Seg("chordsort", new[] { "Up", "Down" }, 8, 6),
            new TextBlock { Text = "QNT", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }, Seg("quantize", QuantNames, 8, 5) } };
        var actionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 4 };
        var holdBtn = OutToggle("hold", "Hold"); var firstBtn = OutToggle("firstnote", "First note"); var resetBtn = ActionBtn("Reset", () => { SetId("reset", 1); });
        Grid.SetColumn((Control)firstBtn, 1); Grid.SetColumn((Control)resetBtn, 2);
        actionRow.Children.Add(holdBtn); actionRow.Children.Add((Control)firstBtn); actionRow.Children.Add((Control)resetBtn);
        var ballsPanelInner = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(listHeader, Dock.Top);
        var bottomStack = new StackPanel { Spacing = 5, [DockPanel.DockProperty] = Dock.Bottom, Children = { new Border { Height = 1, Background = RowLit }, sortRow, actionRow } };
        ballsPanelInner.Children.Add(listHeader); ballsPanelInner.Children.Add(bottomStack);
        ballsPanelInner.Children.Add(new StackPanel { Spacing = 4, Margin = new Thickness(0, 5, 0, 5), Children = { ballList } });
        var ballsRight = new Border { Width = 214, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child = ballsPanelInner };
        DockPanel.SetDock(ballsRight, Dock.Right);
        ballsTab = new DockPanel { LastChildFill = true, Children = { ballsRight, fieldWrap } };

        // Live per-ball rows.
        readouts.Add(() =>
        {
            int count = Math.Clamp(2 + (int)Math.Round(G("balls") * 4), 2, MaxBalls);
            int motion = GI("motion", 4); double baseRate = (G("rate") - 0.5) * 2; double spread = G("spread"); bool sortDown = G("chordsort") >= 0.5f;
            for (int i = 0; i < MaxBalls; i++)
            {
                rowBorder[i].IsVisible = i < count;
                if (i >= count) continue;
                double rr = baseRate * (1.0 + spread * i * 0.37);
                double ph = fieldFull.BallPhase(i);
                double pos = PosCurve(ph, motion);
                double scaled = pos * Math.Max(1, heldN); double frac = scaled - Math.Floor(scaled);
                bool near = heldN > 0 && (frac < 0.14 || frac > 0.86);
                int deg = fieldFull.BallDegree(i);
                rDiv[i].Text = DivNames[GI("division", 5)];
                rRate[i].Text = $"{rr * 100:+0;-0;0}%";
                rPhase[i].Text = $"{ph * 100:0}%";
                rDir[i].Text = baseRate >= 0 ? "→" : "←";
                rNote[i].Text = (deg >= 0 && heldN > 0) ? NoteName(heldBuf[Math.Clamp(sortDown ? heldN - 1 - deg : deg, 0, heldN - 1)]) : "—";
                rowDot[i].Background = near ? AccentBright : Amber;
                rowBorder[i].Background = near ? RowLit : RailBg;
                rowBorder[i].BorderBrush = near ? Amber : Border2;
                rNote[i].Foreground = near ? AmberLit : TxtC;
            }
        });

        // ---------- Voice tab: collapsed field + voice/env + spread rail ----------
        var miniStrip = new Border { Height = 62, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Child = new Panel { Children = {
            fieldMini,
            new TextBlock { Text = "FIELD", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, Margin = new Thickness(8, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top },
            new TextBlock { Text = "▴ Balls tab", FontSize = 8, Foreground = MutedC, Margin = new Thickness(0, 0, 8, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom } } } };
        var wavesRow = Seg("wave", WaveNames, 9, 9);
        var voiceKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = {
            KUnit("tone", "TONE", Tone), KUnit("bright", "BRIGHT", Pct), KUnit("fm", "FM", Pct, mod: true) } };
        var envKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = {
            KUnit("attack", "ATTACK", v => Ms(v, 0.001, 1.0)), KUnit("decay", "DECAY", v => Ms(v, 0.01, 2.0)),
            KUnit("release", "RELEASE", v => Ms(v, 0.02, 3.0)), KUnit("volume", "VOLUME", Db) } };
        voiceKnobs.VerticalAlignment = VerticalAlignment.Center; envKnobs.VerticalAlignment = VerticalAlignment.Center;
        var voiceCenter = new Grid { Margin = new Thickness(9, 6), RowDefinitions = new RowDefinitions("Auto,*,Auto,*") };
        Grid.SetRow(wavesRow, 0);
        var vRow = new Border { Child = voiceKnobs }; Grid.SetRow(vRow, 1);
        var vDiv = new Border { Height = 1, Background = RowLit, VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(vDiv, 2);
        var eRow = new Border { Child = envKnobs }; Grid.SetRow(eRow, 3);
        voiceCenter.Children.Add(wavesRow); voiceCenter.Children.Add(vRow); voiceCenter.Children.Add(vDiv); voiceCenter.Children.Add(eRow);

        // scale root + mode cyclers
        var rootBox = new Border { Height = 18, MinWidth = 30, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
        var rootTx = new TextBlock { FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center }; rootTx.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); rootBox.Child = rootTx;
        rootBox.PointerPressed += (_, _) => { int r = (GI("root", 12) + 1) % 12; SetId("root", r / 11.0); Refresh(); };
        var modeBox = new Border { Height = 18, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
        var modeTx = new TextBlock { FontSize = 9, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center }; modeBox.Child = new DockPanel { Children = { new TextBlock { Text = "▾", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right, Margin = new Thickness(6, 0, 0, 0) }, modeTx } };
        modeBox.PointerPressed += (_, _) => { int m = (GI("scalemode", 6) + 1) % 6; SetId("scalemode", m / 5.0); Refresh(); };
        readouts.Add(() => { rootTx.Text = RootNames[GI("root", 12)]; modeTx.Text = ModeNames[GI("scalemode", 6)]; });
        var spreadRail = new Border { Width = 104, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child =
            new DockPanel { LastChildFill = false, Children = {
                new StackPanel { Spacing = 5, [DockPanel.DockProperty] = Dock.Top, Children = {
                    Cap("SPREAD"),
                    HSlider("spread", "Rate", Pct, lw: 30), HSlider("detune", "Detune", Cents, lw: 30), HSlider("panspread", "Pan", Pct, lw: 30),
                    new Border { Height = 1, Background = RowLit },
                    OutToggle("humanize", "Humanize", teal: true) } },
                new StackPanel { Spacing = 3, [DockPanel.DockProperty] = Dock.Bottom, Children = {
                    new DockPanel { LastChildFill = false, Children = { Cap("SCALE"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, [DockPanel.DockProperty] = Dock.Right, Children = { rootBox } } } },
                    modeBox } } } } };
        DockPanel.SetDock(spreadRail, Dock.Right);
        DockPanel.SetDock(miniStrip, Dock.Top);
        voiceTab = new DockPanel { LastChildFill = true, Children = { miniStrip, new DockPanel { LastChildFill = true, Children = { spreadRail, voiceCenter } } } };

        // ---------- assemble ----------
        DockPanel.SetDock(tabRail, Dock.Left);
        var body = new DockPanel { LastChildFill = true, Children = { tabRail, bodyContent } };
        DockPanel.SetDock(live, Dock.Top);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { live, body } };

        SelectTab(0);
        ctx.SetInstLiveViz(Refresh);
        Refresh();
        return root;
    }

    // Same four swing curves the DSP + viz use (for the per-ball list's trigger proximity).
    private static double PosCurve(double ph, int type)
    {
        double tri = 1.0 - 2.0 * Math.Abs(ph - 0.5);
        return type switch
        {
            1 => 0.5 - 0.5 * Math.Cos(2 * Math.PI * ph),
            2 => tri * tri * (3.0 - 2.0 * tri),
            3 => Math.Clamp(1.0 - Math.Pow(1.0 - tri, 2.0) + 0.06 * Math.Sin(2 * Math.PI * 3.0 * tri) * (1.0 - tri), 0, 1),
            _ => tri,
        };
    }
}
