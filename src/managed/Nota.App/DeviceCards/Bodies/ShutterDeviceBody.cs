// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Shutter body (noise gate / ducker, device kind 19), a build
// of the "Nota Shutter" mockup (700 × 260) on the Utility / Valve / Vintage frame: an always-
// visible STATE column (input or key level with the threshold · reduction), a centre panel
// with Signal / Envelope / Sidechain tabs — the input and the gated output over the window
// with the threshold and the close level on it, one opening drawn from attack / hold /
// release / shape / floor, the detector's band-pass under the reduction — a right panel with
// Detector / Meters tabs, and a status strip. The pictures come from the engine (Shutter.h
// scopeRead), so they show what the gate does. Every control is a device param, so
// automation / MIDI learn / presets / A-B / persistence come for free; the window length,
// Live (freeze) and Reset are view / meter actions, not params.
// FullBleed — the shared shell draws the header (name · preset · badge · bypass).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class ShutterDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Shutter.h) ─────────────────────────────
    private const int Threshold = 0, Return = 1, Attack = 2, Hold = 3, Release = 4, Floor = 5, Lookahead = 6, Flip = 7,
        DetHP = 8, DetLP = 9, Listen = 10, Shape = 11, Retrigger = 12, DetFilter = 13, PeakHold = 14, ExternalKey = 15;
    // Scope layout (Shutter::S_* / kTele / kHist).
    private const int S_InDb = 0, S_GateGain = 1, S_GrDb = 2, S_DetDb = 3, S_Open = 4, S_State = 5, S_OutDb = 6, S_OpenRatio = 7,
        S_TrigPerBar = 8, S_Triggers = 9, S_SampleRate = 10, S_Cpu = 11, S_Latency = 12, S_ExtKey = 13, S_KeyDb = 14,
        S_PeakGrDb = 18, S_WindowSec = 20, S_HistN = 21, kTele = 24, kHist = 128;
    private const int kScope = kTele + 3 * kHist;
    private const int H_In = kTele, H_Gate = kTele + kHist, H_Det = kTele + 2 * kHist;
    private const int A_ResetMeters = 0, A_Window = 1;

    private static readonly string[] Shapes = { "Linear", "Log", "Snap" };
    private static readonly string[] StateNames = { "closed", "attack", "open", "hold", "release" };
    private static readonly string[] WindowNames = { "250\u2009ms", "1\u2009s", "4\u2009s" };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "GATE";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) => engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        double Def(int p) => engine.DeviceParamDefault(track, di, p);
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kScope];
        var frozen = new float[kScope];
        int scN = 0;
        bool live = true;
        double Sc(int i) => scN > i ? scope[i] : 0;
        float[] Hist() => live ? scope : frozen;

        // ---- units ----------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string MsF(double ms) => ms >= 100 ? NotaNum.F($"{ms:0}\u2009ms") : ms >= 10 ? NotaNum.F($"{ms:0.0}\u2009ms") : NotaNum.F($"{ms:0.0#}\u2009ms");
        static string MsN(double ms) => ms >= 100 ? NotaNum.F($"{ms:0}") : ms >= 10 ? NotaNum.F($"{ms:0.#}") : NotaNum.F($"{ms:0.0#}");
        static string DbF(double db) => db <= -119 ? "−∞\u2009dB" : NotaNum.F($"{db:0.0}\u2009dB");
        static string HzF(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0}\u2009k") : NotaNum.F($"{hz:0}");
        double ThrDb() => -70 + P(Threshold) * 70;
        double RetDb() => P(Return) * 24;
        double AtkMs() => Exp(P(Attack), 0.01, 100);
        double HoldMs() => Exp(P(Hold), 0.1, 500);
        double RelMs() => Exp(P(Release), 1, 2000);
        double FloorDb() => P(Floor) <= 0.001f ? -120 : -70 + P(Floor) * 70;
        double FloorLin() => P(Floor) <= 0.001f ? 0 : Math.Pow(10, FloorDb() / 20);
        int LookMs() => new[] { 0, 1, 5 }[Sel(Lookahead, 3)];
        bool Duck() => On(Flip);
        string ThrShort(double v) => NotaNum.F($"{-70 + v * 70:0.0}");
        string RetF(double v) => NotaNum.F($"{v * 24:0.0}\u2009dB");
        string RetShort(double v) => NotaNum.F($"{v * 24:0.0}");
        string AtkF(double v) => MsF(Exp(v, 0.01, 100));
        string HoldF(double v) => MsF(Exp(v, 0.1, 500));
        string RelF(double v) => MsF(Exp(v, 1, 2000));
        string FloorF(double v) => v <= 0.001 ? "−∞\u2009dB" : NotaNum.F($"{-70 + v * 70:0.0}\u2009dB");
        string FloorShort(double v) => v <= 0.001 ? "−∞" : NotaNum.F($"{-70 + v * 70:0.0}");
        string LookF(double v) => NotaNum.F($"{new[] { 0, 1, 5 }[Math.Clamp((int)Math.Round(v * 2), 0, 2)]}\u2009ms");
        string HpF(double v) => HzF(Exp(v, 20, 2000));
        string LpF(double v) => HzF(Exp(v, 200, 20000));
        string Times() => NotaNum.F($"{MsN(AtkMs())} / {MsN(HoldMs())} / {MsN(RelMs())}\u2009ms");
        string GrText() => Sc(S_GrDb) < 0.05 ? "GR 0.0\u2009dB" : Sc(S_GrDb) > 99 ? "GR −∞\u2009dB" : NotaNum.F($"GR −{Sc(S_GrDb):0.0}\u2009dB");
        string WindowText() { double s = Sc(S_WindowSec); return s <= 0 ? "1\u2009s" : s < 1 ? NotaNum.F($"{s * 1000:0}\u2009ms") : NotaNum.F($"{s:0.#}\u2009s"); }
        int WindowIdx() { double s = Sc(S_WindowSec); return s <= 0 ? 1 : s < 0.5 ? 0 : s < 2 ? 1 : 2; }
        int State() => Math.Clamp((int)Math.Round(Sc(S_State)), 0, 4);

        // Sidechain key: the source track and whether it is in use.
        string SrcName(int id) => TrackNames.Of(engine, id);
        int Src() => engine.DeviceSidechainSource(track, di);
        bool ExtOn() => On(ExternalKey) && Src() >= 0;
        string KeyName() => ExtOn() ? SrcName(Src()) : "Internal";

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        // `steps` > 1 snaps a discrete param (Lookahead).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, int steps = 0, string? tip = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = Def(p), Width = 34, Height = 34 };
            knob.ValueChanged += v =>
            {
                if (steps > 1) v = Math.Round(v * (steps - 1)) / (steps - 1);
                Raw(p, v); val.Text = fmt(P(p)); RefreshAll();
            };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            if (tip is not null) ToolTip.SetTip(knob, tip);
            readouts.Add(() => { if (knob.Dragging) return; double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(P(p)); });
            return KnobCell(name, knob, val, 44);
        }

        // On/off switch bound to a param (>= 0.5 = on).
        Control Toggle(int p, string label, string tip)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); RefreshAll(); }, out var sync);
            readouts.Add(sync);
            Learn(wrap, p);
            ToolTip.SetTip(wrap, tip);
            return wrap;
        }

        // Segmented pill over a normalized discrete param.
        Control Seg(int p, string[] names, string tip)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); RefreshAll(); }, out var sync, padX: 6);
            readouts.Add(sync);
            Learn(seg, p);
            ToolTip.SetTip(seg, tip);
            return seg;
        }
        Control ModeSeg() => Seg(Flip, new[] { "Gate", "Duck" }, "Mode — Gate lets the signal through above the threshold; Duck turns it down while the key is above it");

        // Outlined chips: lit = brass edge + wash.
        Control Chips(string[] names, Func<int, bool> lit, Action<int> pick, int learnParam, string tip)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var cells = new Border[names.Length];
            var texts = new TextBlock[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var tb = new TextBlock { Text = names[i], FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; pick(iv); RefreshAll(); e.Handled = true; };
                cells[i] = b; texts[i] = tb; row.Children.Add(b);
            }
            readouts.Add(() =>
            {
                for (int i = 0; i < names.Length; i++)
                {
                    bool on = lit(i);
                    cells[i].BorderBrush = on ? Brass : NotaPalette.BorderStrong;
                    cells[i].Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent;
                    texts[i].Foreground = on ? AccentBright : TextSecondary;
                    texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            });
            Learn(row, learnParam);
            ToolTip.SetTip(row, tip);
            return row;
        }

        // A latching chip: lit when `lit()`, a click runs `click`. `fill` = a full-width button.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false)
        {
            var tb = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSubtle : fill ? Raised : Brushes.Transparent;
                b.BorderBrush = on ? Brass : fill ? Brushes.Transparent : NotaPalette.BorderStrong;
                tb.Foreground = on ? AccentBright : fill ? TextPrimary : TextSecondary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tb.Text = text();
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            if (p >= 0) Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Border ToggleLatch(int p, string text, string tip, bool fill = false)
            => Latch(p, () => On(p), () => SetP(p, On(p) ? 0f : 1f), () => text, tip, fill);
        Border ListenLatch(bool fill = false) => ToggleLatch(Listen, "Listen", "Listen — hear the key as the detector hears it (after its filter), to tune the band", fill);

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool modulation = false, double labW = 40)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, v); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: 34, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // The key source: [name ▾] lists Internal and the other tracks. Picking a track switches
        // External Key on; Internal clears the source.
        Control KeyDrop(bool stretch)
        {
            var name = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Margin = new Thickness(6, 0, 0, 0) };
            var box = new Border
            {
                Height = 16, MinWidth = 74, Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(chev, Dock.Right), name } },
            };
            if (stretch) box.HorizontalAlignment = HorizontalAlignment.Stretch;
            readouts.Add(() =>
            {
                bool ext = ExtOn();
                name.Text = KeyName();
                name.Foreground = ext ? AccentBright : TextPrimary;
                name.FontWeight = ext ? FontWeight.SemiBold : FontWeight.Normal;
                box.BorderBrush = ext ? Brass : BorderDef;
                chev.Foreground = ext ? NotaPalette.AccentDim : TextTertiary;
            });
            ToolTip.SetTip(box, "Key source — Internal keys off this track; pick another track to gate or duck against it");
            Learn(box, ExternalKey);
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                void Item(string label, int id)
                {
                    bool cur = id < 0 ? !ExtOn() : ExtOn() && Src() == id;
                    var mi = new MenuItem { Header = label };
                    if (cur) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brass };
                    mi.Click += (_, _) =>
                    {
                        engine.SetDeviceSidechainSource(track, di, id);
                        if (id >= 0 && !On(ExternalKey)) SetP(ExternalKey, 1f);
                        ctx.NotifyChanged(); RefreshAll();
                    };
                    fly.Items.Add(mi);
                }
                Item("Internal — this track", -1);
                for (int i = 0; i < engine.TrackCount; i++)
                    if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track) Item(SrcName(ti.Id), ti.Id);
                fly.ShowAt(box);
                e.Handled = true;
            };
            return box;
        }

        // External sidechain switch: on uses the routed key (picks the first other track when
        // none is routed yet), off keeps the source but keys off this track.
        Control ExtSwitch()
        {
            var sw = Switch("External sidechain", ExtOn, () =>
            {
                if (ExtOn()) SetP(ExternalKey, 0f);
                else
                {
                    if (Src() < 0)
                        for (int i = 0; i < engine.TrackCount; i++)
                            if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track) { engine.SetDeviceSidechainSource(track, di, ti.Id); break; }
                    if (Src() >= 0) SetP(ExternalKey, 1f);
                }
                ctx.NotifyChanged(); RefreshAll();
            }, out var sync);
            readouts.Add(sync);
            Learn(sw, ExternalKey);
            ToolTip.SetTip(sw, "External sidechain — the detector listens to the key source instead of this track");
            return sw;
        }

        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 18, ColumnSpacing = 6 };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control KnobRow(Control[] knobs, Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 7, TextTertiary); i1.HorizontalAlignment = HorizontalAlignment.Right;
            var i2 = Mono("", 7, TextTertiary); i2.HorizontalAlignment = HorizontalAlignment.Right;
            i1.TextTrimming = i2.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 52, ColumnSpacing = 6 };
            g.Children.Add(Row(4, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(8, 5, 8, 0),
            Children = { Docked(head, Dock.Top), Docked(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 5, 0, 0), Child = window } },
        };
        void WireDrag(ShDragView v, Func<int, int> param)
        {
            v.Value = h => P(param(h));
            v.Changed += (h, x) => { Raw(param(h), x); Refresh(); };   // the graph redraws from the new value at once
            v.GestureBegin += h => Begin(param(h));
            v.GestureEnd += h => End(param(h));
            v.ResetRequested += h => { Reset(param(h)); RefreshAll(); };
        }

        // ======================================================================
        // LEFT — STATE column (input or key level · reduction)
        // ======================================================================
        var stateTitle = Caps("STATE"); stateTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var inPill = new ShPill { Ink = Brass, VerticalAlignment = VerticalAlignment.Stretch };
        var grPill = new ShPill { Ink = Teal, FromTop = true, VerticalAlignment = VerticalAlignment.Stretch };
        var inLbl = Caps("IN"); inLbl.HorizontalAlignment = HorizontalAlignment.Center;
        var grLbl = Caps("GR"); grLbl.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(inPill, "The level the detector compares — this track's input, or the key when an external sidechain drives it; the dashed tick is the threshold");
        ToolTip.SetTip(grPill, "Gain reduction — how far the gate has turned the signal down now");
        var stateIn = Mono("", 7, AccentBright); stateIn.HorizontalAlignment = HorizontalAlignment.Center;
        var stateGr = Mono("", 7, TextPrimary); stateGr.HorizontalAlignment = HorizontalAlignment.Center;
        static double Norm60(double db) => Math.Clamp((db + 60) / 60, 0, 1);
        readouts.Add(() =>
        {
            bool ext = Sc(S_ExtKey) > 0.5;
            double lvl = ext ? Sc(S_KeyDb) : Sc(S_InDb);
            inLbl.Text = ext ? "SC" : "IN";
            inPill.Set(Norm60(lvl), Norm60(ThrDb()));
            grPill.Set(Math.Clamp(Sc(S_GrDb) / 60, 0, 1), double.NaN);
            stateIn.Text = lvl <= -119 ? "−∞" : NotaNum.F($"{lvl:0.0}");
            stateGr.Text = Sc(S_GrDb) < 0.05 ? "0.0\u2009dB" : Sc(S_GrDb) > 99 ? "−∞\u2009dB" : NotaNum.F($"−{Sc(S_GrDb):0.0}\u2009dB");
            stateTitle.Foreground = State() is 1 or 2 or 3 ? AccentBright : TextTertiary;   // lit while the gate is open
        });
        Control PillCol(ShPill pill, TextBlock lbl) => new DockPanel { Children = { Docked(lbl, Dock.Bottom), new Border { Margin = new Thickness(0, 0, 0, 3), Child = pill } } };
        var pills = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        pills.Children.Add(PillCol(inPill, inLbl));
        pills.Children.Add(Col(PillCol(grPill, grLbl), 1));
        var stateCol = new Border
        {
            Width = 56, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(stateTitle, Dock.Top), Docked(stateGr, Dock.Bottom), Docked(stateIn, Dock.Bottom), pills } },
        };
        DockPanel.SetDock(stateCol, Dock.Left);

        // ======================================================================
        // CENTRE — Signal
        // ======================================================================
        ShSignalView? signal = null;
        Control SignalTab()
        {
            signal = new ShSignalView();
            WireDrag(signal, h => h == ShSignalView.HThreshold ? Threshold : Return);
            ToolTip.SetTip(signal, "The input (teal) and what passes (brass) over the window, with the threshold (dashed brass) and where the gate closes again (dashed). Drag a line up or down; double-click resets it.");
            Learn(signal, Threshold);

            var liveBtn = Latch(-1, () => live, () => { live = !live; if (!live) Array.Copy(scope, frozen, kScope); }, () => live ? "Live" : "Frozen",
                "Live — the graph follows the audio; click to freeze the picture");
            var ret = Mono("", 7, AccentBright);
            readouts.Add(() => ret.Text = NotaNum.F($"return {RetDb():0.0}\u2009dB"));
            var head = HeadRow(Row(6, Caps("MODE"), ModeSeg(), new Border { Width = 2 }, Caps("LOOK"),
                Seg(Lookahead, new[] { "0", "1", "5\u2009ms" }, "Lookahead — the gate opens this much before the transient arrives (the track is delayed by it)"), liveBtn), ret);
            var knobs = KnobRow(new[]
            {
                K(Threshold, "THRESH", ThrShort),
                K(Attack, "ATTACK", AtkF),
                K(Hold, "HOLD", HoldF),
                K(Release, "RELEASE", RelF),
            },
            () => NotaNum.F($"threshold {ThrDb():0.0}\u2009dB · return {RetDb():0.0}\u2009dB"),
            () => NotaNum.F($"{(Duck() ? "ducked" : "open")} {Sc(S_OpenRatio) * 100:0}\u2009% of window · {GrText()}"));
            return TabBody(head, signal, knobs);
        }

        // ======================================================================
        // CENTRE — Envelope
        // ======================================================================
        ShEnvelopeView? envView = null;
        Control EnvelopeTab()
        {
            envView = new ShEnvelopeView();
            WireDrag(envView, h => h switch { ShEnvelopeView.HAttack => Attack, ShEnvelopeView.HHold => Hold, ShEnvelopeView.HRelease => Release, _ => Floor });
            ToolTip.SetTip(envView, "One opening: attack, hold, release in the chosen shape, down to the floor (inverted for Duck). Drag a node sideways for its time, the teal floor up or down; double-click resets.");
            Learn(envView, Release);

            var shapes = Chips(Shapes, i => Sel(Shape, 3) == i, i => SetP(Shape, i / 2f), Shape,
                "Shape — Linear: straight ramps over the times; Log: the classic one-pole curve; Snap: stays open, then shuts hard");
            var retrig = ToggleLatch(Retrigger, "Retrig", "Retrigger — trigger mode: each hit fires one attack → hold → release, however long the signal stays above the threshold");
            var times = Mono("", 7, AccentBright);
            readouts.Add(() => times.Text = Times());
            var head = HeadRow(Row(6, Caps("SHAPE"), shapes, new Border { Width = 2 }, ModeSeg(), retrig), times);
            var knobs = KnobRow(new[]
            {
                K(Attack, "ATTACK", AtkF),
                K(Hold, "HOLD", HoldF),
                K(Release, "RELEASE", RelF),
                K(Floor, "FLOOR", FloorF, Teal, tip: "Floor — the level when closed (−∞ = silence); in Duck, how far the signal goes down"),
            },
            () => P(Floor) <= 0.001f ? (Duck() ? "ducks to silence" : "closes to silence") : NotaNum.F($"floor {FloorDb():0.0}\u2009dB instead of silence"),
            () => NotaNum.F($"openings {Sc(S_TrigPerBar):0} / bar · {GrText()}"));
            return TabBody(head, envView, knobs);
        }

        // ======================================================================
        // CENTRE — Sidechain
        // ======================================================================
        ShKeyView? keyView = null;
        Control SidechainTab()
        {
            keyView = new ShKeyView();
            WireDrag(keyView, h => h == ShKeyView.HHp ? DetHP : DetLP);
            ToolTip.SetTip(keyView, "The detector's band-pass (teal) — only this part of the key opens the gate — over the reduction in the window (brass). Drag the nodes sideways; double-click resets.");
            Learn(keyView, DetHP);

            var eq = Latch(DetFilter, () => On(DetFilter), () => SetP(DetFilter, On(DetFilter) ? 0f : 1f), () => On(DetFilter) ? "SC EQ on" : "SC EQ off",
                "SC EQ — the detector hears the key through its band-pass; off = the full band");
            var look = Mono("", 7, AccentBright);
            readouts.Add(() => look.Text = NotaNum.F($"look {LookMs()}\u2009ms"));
            var head = HeadRow(Row(6, Caps("KEY"), KeyDrop(false), ModeSeg(), ListenLatch(), eq), look);
            var knobs = KnobRow(new[]
            {
                K(Threshold, "THRESH", ThrShort),
                K(Floor, "RANGE", FloorF, tip: "Range — how far the gate closes or the duck goes down (−∞ = all the way)"),
                K(Return, "RETURN", RetF, Teal, tip: "Return — how far under the threshold the key must fall before the gate closes (stops chatter)"),
                K(Lookahead, "LOOK", LookF, Teal, steps: 3, tip: "Lookahead — 0, 1 or 5\u2009ms"),
            },
            () => NotaNum.F($"key {KeyName()} · {(On(DetFilter) ? "fires on " + HpF(P(DetHP)) + "…" + LpF(P(DetLP)) : "full band")}"),
            () => NotaNum.F($"{(Duck() ? "ducks" : "closes")} to {(P(Floor) <= 0.001f ? "−∞\u2009dB" : NotaNum.F($"{FloorDb():0.0}\u2009dB"))} · {GrText()}"));
            return TabBody(head, keyView, knobs);
        }

        // ======================================================================
        // RIGHT — Detector / Meters
        // ======================================================================
        static Control LabelRow(string label, Control c, double labW = 40)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")) };
            g.Children.Add(new TextBlock { Text = label, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(Col(c, 1));
            return g;
        }
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(8, 6), Child = g };
        }

        Control StateBox()
        {
            var t = Caps("STATE");
            var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Center };
            var l1 = Mono("", 9, TextPrimary);
            var l2 = Mono("", 8, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var b = new Border
            {
                Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = { t, Row(5, dot, l1), l2 } },
            };
            readouts.Add(() =>
            {
                int st = State();
                bool moving = st is 1 or 3 or 4;
                string name = Duck() ? st switch { 2 => "ducking", 0 => "clear", _ => StateNames[st] } : StateNames[st];
                l1.Text = NotaNum.F($"{name} · {Sc(S_GateGain) * 100:0}\u2009%");
                l2.Text = P(Floor) > 0.001f && st == 0 ? NotaNum.F($"{GrText()} · floor {FloorDb():0.0}") : NotaNum.F($"{GrText()} · look {LookMs()}\u2009ms");
                dot.Background = st == 2 ? Success : moving ? Brass : NotaPalette.BorderStrong;
                l1.Foreground = moving ? AccentBright : TextPrimary;
                b.BorderBrush = moving ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
                t.Foreground = moving ? AccentBright : TextTertiary;
            });
            ToolTip.SetTip(b, "What the gate is doing now — closed, attack, open, hold or release — and its gain");
            return b;
        }

        Control DetectorTab()
        {
            var floor = SliderRow("FLOOR", Floor, FloorShort, modulation: true);
            return Spread(
                LabelRow("SOURCE", KeyDrop(true)),
                SliderRow("THRESH", Threshold, ThrShort),
                SliderRow("RETURN", Return, RetShort),
                new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = floor },
                StateBox(),
                new StackPanel { Spacing = 5, Children = {
                    ExtSwitch(),
                    Toggle(DetFilter, "Detector filter", "Detector filter — the key goes through the band-pass (Sidechain tab) before the detector"),
                    Toggle(PeakHold, "Peak hold", "Peak hold — the detector holds each peak longer, so low notes and slow waves don't chatter the gate") } });
        }

        Control MetersTab()
        {
            Control Line(Func<string> k, Func<string> v, IBrush? ink = null)
            {
                var key = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
                var val = Mono("", 8, ink ?? TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => { key.Text = k(); val.Text = v(); });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(key);
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = new Border
            {
                Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = {
                    Caps("MEASUREMENTS"),
                    Line(() => Sc(S_ExtKey) > 0.5 ? "sc in" : "in", () => DbF(Sc(S_ExtKey) > 0.5 ? Sc(S_KeyDb) : Sc(S_InDb))),
                    Line(() => "out", () => DbF(Sc(S_OutDb))),
                    Line(() => "GR · peak", () => NotaNum.F($"{(Sc(S_GrDb) < 0.05 ? "0.0" : Sc(S_GrDb) > 99 ? "−∞" : NotaNum.F($"−{Sc(S_GrDb):0.0}"))} · {(Sc(S_PeakGrDb) < 0.05 ? "0.0" : Sc(S_PeakGrDb) > 99 ? "−∞" : NotaNum.F($"−{Sc(S_PeakGrDb):0.0}"))}\u2009dB"), AccentBright) } },
            };
            var sliders = new StackPanel { Spacing = 6, Children = {
                SliderRow("SC HP", DetHP, HpF, modulation: true),
                SliderRow("SC LP", DetLP, LpF, modulation: true),
                SliderRow("RANGE", Floor, FloorShort) } };
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            btns.Children.Add(ListenLatch(fill: true));
            btns.Children.Add(Col(Latch(-1, () => false, () => engine.DeviceAction(track, di, A_ResetMeters, 0, 0), () => "Reset",
                "Reset — clear the peak reduction, the opening count and the history", fill: true), 1));
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(box, Dock.Top),
                Docked(new Border { Margin = new Thickness(0, 6, 0, 0), Child = sliders }, Dock.Top),
                Docked(new StackPanel { Spacing = 5, Children = {
                    ExtSwitch(),
                    Toggle(DetFilter, "Detector filter", "Detector filter — the key goes through the band-pass before the detector") } }, Dock.Bottom),
                Docked(new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 5), Child = btns }, Dock.Bottom) } };
            return new Border { Padding = new Thickness(8, 6), Child = body };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        int centreTab = 0;
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 7, TextTertiary);
        var extrasHost = new Border { Background = Brushes.Transparent, Child = extras, VerticalAlignment = VerticalAlignment.Stretch };
        extrasHost.PointerPressed += (_, e) =>
        {
            if (centreTab != 0 || !e.GetCurrentPoint(extrasHost).Properties.IsLeftButtonPressed) return;
            engine.DeviceAction(track, di, A_Window, (WindowIdx() + 1) % 3, 0);
            e.Handled = true;
        };
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"one opening · {MsN(AtkMs() + HoldMs() + RelMs())}\u2009ms"),
                2 => NotaNum.F($"key: {KeyName()} · {(On(DetFilter) ? "filter " + HpF(P(DetHP)) + "…" + LpF(P(DetLP)) : "full band")}"),
                _ => NotaNum.F($"window {WindowText()} · look {LookMs()}\u2009ms"),
            };
            extrasHost.Cursor = centreTab == 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        });
        ToolTip.SetTip(extrasHost, "Signal tab: click to change the window — " + string.Join(" · ", WindowNames));
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => EnvelopeTab(), 2 => SidechainTab(), _ => SignalTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? MetersTab() : DetectorTab();

        var centre = TabFrame(new[] { "Signal", "Envelope", "Sidechain" }, centreHost, CentreBody, false, t => { centreTab = t; Refresh(); }, extrasHost);
        var rightFrame = TabFrame(new[] { "Detector", "Meters" }, rightHost, RightBody, true, _ => RefreshAll(), null);
        var right = new Border { Width = 186, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Margin = new Thickness(5, 0), ClipToBounds = true, Child = centre };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        string StatusText()
        {
            string floor = P(Floor) <= 0.001f ? "−∞" : NotaNum.F($"{FloorDb():0.0}\u2009dB");
            var parts = new List<string> { Duck() ? "Duck" : "Gate" };
            switch (centreTab)
            {
                case 1:
                    parts.Add(Shapes[Sel(Shape, 3)].ToLowerInvariant());
                    parts.Add(Times());
                    parts.Add("floor " + floor);
                    parts.Add(NotaNum.F($"threshold {ThrDb():0.0}"));
                    parts.Add(NotaNum.F($"return {RetDb():0.0}"));
                    if (On(Retrigger)) parts.Add("retrigger");
                    break;
                case 2:
                    parts.Add("key " + KeyName());
                    parts.Add(NotaNum.F($"threshold {ThrDb():0.0}"));
                    parts.Add("range " + floor);
                    parts.Add(On(DetFilter) ? "SC " + HpF(P(DetHP)) + "…" + LpF(P(DetLP)) : "SC full band");
                    parts.Add(NotaNum.F($"look {LookMs()}\u2009ms"));
                    if (On(Listen)) parts.Add("listening");
                    break;
                default:
                    parts.Add(NotaNum.F($"threshold {ThrDb():0.0}\u2009dB"));
                    parts.Add(NotaNum.F($"return {RetDb():0.0}"));
                    parts.Add(Times());
                    parts.Add("floor " + floor);
                    parts.Add(NotaNum.F($"look {LookMs()}\u2009ms"));
                    if (ExtOn()) parts.Add("key " + KeyName());
                    break;
            }
            if (On(PeakHold) && centreTab != 1) parts.Add("peak hold");
            return string.Join(" · ", parts);
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            double sr = Sc(S_SampleRate);
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#}\u2009kHz · latency {Sc(S_Latency):0}\u2009smp · CPU {Sc(S_Cpu) * 100:0.0}\u2009%") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { stateCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            int n = scN >= kScope ? Math.Clamp((int)Sc(S_HistN), 0, kHist) : 0;
            var h = Hist();
            if (centreTab == 0 && signal is not null)
                signal.Set(h, H_In, H_Gate, n, ThrDb(), ThrDb() - RetDb(), Duck(), WindowText());
            if (centreTab == 1 && envView is not null)
                envView.Set(AtkMs(), HoldMs(), RelMs(), FloorLin(), Sel(Shape, 3), Duck(), On(Retrigger),
                    P(Floor) <= 0.001f ? "−∞\u2009dB" : NotaNum.F($"{FloorDb():0.0}\u2009dB"), MsN(AtkMs()), MsN(HoldMs()), MsF(RelMs()));
            if (centreTab == 2 && keyView is not null)
                keyView.Set(Exp(P(DetHP), 20, 2000), Exp(P(DetLP), 200, 20000), On(DetFilter), h, H_Gate, n, Duck() ? "duck" : "reduction");
            RefreshAll();
        }
        // Graphs follow the params every tick, also while dragged (the envelope keeps its time span then).
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl)
        {
            int sel = 0;
            var btns = new Border[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? Card2 : Brushes.Transparent;
                    btns[i].BorderBrush = on ? Brass : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!;
                    tb.Foreground = on ? AccentBright : TextTertiary;
                    tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var bar = centered ? (Avalonia.Controls.Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border
                {
                    Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                };
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
                btns[i] = b;
                bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi();
            host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }
    }
}
