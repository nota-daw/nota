// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Shutter body (noise gate / ducker, device kind 19), drawn to
// the style guide's picture of it (760 × 260): an always-visible State column (input or key level with the threshold · reduction), a centre panel
// with Signal / Envelope / Sidechain / Pattern tabs — the input and the gated output over the
// window with the threshold and the close level on it, one opening drawn from attack / hold /
// release / shape / floor, the detector's band-pass under the reduction, the rhythmic gate's
// step grid — a right panel with Detector / Meters tabs (Rhythm / Detector on the Pattern
// tab). The pictures come from the engine (Shutter.h
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
    // Pattern (appended params + the block after the histories).
    private const int PatTarget = 16, PatRate = 17, PatDepth = 18, PatSync = 19, PatSmooth = 20, PatSwing = 21, PatSteps = 22,
        PatLevels = 23, PatRetrig = 24, PatGated = 25, PatComplexity = 26, Step1 = 28, kMaxSteps = 32;
    private const int S_Bpm = 19;
    private const int kPatAt = kTele + 3 * kHist, P_Phase = 0, P_Step = 1, P_Value = 3, kPat = 8;
    private const int kScope = kPatAt + kPat;
    private const int H_In = kTele, H_Gate = kTele + kHist, H_Det = kTele + 2 * kHist;
    private const int A_ResetMeters = 0, A_Window = 1;

    private static readonly string[] Shapes = { "Linear", "Log", "Snap" };
    private static readonly string[] StateNames = { "closed", "attack", "open", "hold", "release" };
    private static readonly string[] WindowNames = { "250\u2009ms", "1\u2009s", "4\u2009s" };
    private static readonly string[] RateSync = { "1/4", "1/2", "1 Bar", "2 Bars", "4 Bars" };
    private static readonly string[] RateFree = { "4\u2009Hz", "2\u2009Hz", "1\u2009Hz", "0.5\u2009Hz", "0.25\u2009Hz" };
    private static readonly int[] StepCounts = { 8, 16, 32 }, LevelCounts = { 4, 8, 16 };

    // The pattern browser's shapes (steps in eighths), resampled to the grid; the arrows walk them.
    private static readonly (string Name, int[] Steps)[] Patterns =
    {
        ("Push",      new[] { 8, 0, 8, 8, 0, 8, 8, 0, 8, 0, 8, 8, 0, 8, 0, 8 }),
        ("Offbeat",   new[] { 0, 0, 8, 0, 0, 0, 8, 0, 0, 0, 8, 0, 0, 0, 8, 0 }),
        ("Trance 16", new[] { 8, 0, 8, 0, 8, 8, 0, 8, 8, 0, 8, 0, 8, 8, 0, 8 }),
        ("Stutter",   new[] { 8, 8, 8, 8, 0, 0, 0, 0, 8, 0, 8, 0, 8, 8, 0, 0 }),
        ("Halftime",  new[] { 8, 8, 8, 0, 0, 0, 0, 0, 8, 8, 8, 0, 0, 0, 4, 0 }),
        ("Ramp Up",   new[] { 1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 6, 7, 8 }),
        ("Chop",      new[] { 8, 0, 6, 0, 8, 3, 0, 5, 8, 0, 6, 2, 8, 0, 4, 0 }),
    };
    // Copy / Paste between every Shutter in the session.
    private static float[]? _clipboard;
    private static readonly Random Rng = new();

    public double Width => 760;
    public bool FullBleed => true;
    public string? Subtitle => "Gate";

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
        static TextBlock Caps(string t, IBrush? c = null) => DeviceBodyKit.Label(t, c);
        // An acronym NotaText.Label does not know (GR) keeps its case: the label as written.
        static TextBlock Acr(string t, IBrush? c = null) { var tb = Caps(t, c); tb.Text = t; return tb; }
        // A value (11) in the ink given; `fs` is kept for the call sites and ignored (one value size).
        static TextBlock Mono(string t, double fs, IBrush c) => c == TextPrimary ? DeviceBodyKit.Value(t, c) : DeviceBodyKit.Note(t, c);
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        // `steps` > 1 snaps a discrete param (Lookahead).
        Control K(int p, string name, Func<double, string> fmt, int steps = 0, string? tip = null, double cellW = 46)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Default = Def(p) };
            knob.Format = v => fmt(steps > 1 ? Math.Round(v * (steps - 1)) / (steps - 1) : v);   // read-out at any value: typing searches it
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
            return KnobCell(name, knob, val, cellW);
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

        // A single choice drawn as segments (the picked one raised).
        Control Chips(string[] names, Func<int, bool> lit, Action<int> pick, int learnParam, string tip)
        {
            int Cur() { for (int i = 0; i < names.Length; i++) if (lit(i)) return i; return -1; }
            var row = Segments(names, Cur, iv => { pick(iv); RefreshAll(); }, out var sync, padX: 6);
            readouts.Add(sync);
            Learn(row, learnParam);
            ToolTip.SetTip(row, tip);
            return row;
        }

        // A latching button (raised): lit when `lit()` (the soft orange), a click runs `click`.
        // `fill` = a full-width button.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false)
        {
            var b = TextButton(text());
            var tb = (TextBlock)b.Child!;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            b.VerticalAlignment = VerticalAlignment.Center;
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? AccentBright : TextPrimary;
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

        // Slider row: label · value bar · value.
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 58)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, v); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: 48);
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
            var name = new TextBlock { FontSize = NotaType.Caption, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chev = new Glyph(GlyphKind.ChevronDown, 8) { Margin = new Thickness(6, 0, 0, 0) };
            var box = new Border
            {
                Height = 22, MinWidth = 90, Background = NotaPalette.SurfaceHover, CornerRadius = NotaRadius.Field,
                Padding = new Thickness(8, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(chev, Dock.Right), name } },
            };
            box.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            if (stretch) box.HorizontalAlignment = HorizontalAlignment.Stretch;
            readouts.Add(() =>
            {
                bool ext = ExtOn();
                name.Text = KeyName();
                name.Foreground = TextPrimary;   // an external key: a stronger weight, not the accent
                name.FontWeight = ext ? FontWeight.SemiBold : FontWeight.Normal;
                chev.Foreground = ext ? TextSecondary : TextTertiary;
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
                    mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = cur;
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
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 26, ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control KnobRow(Control[] knobs, Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 11, TextSecondary); i1.HorizontalAlignment = HorizontalAlignment.Right;
            var i2 = Mono("", 11, TextSecondary); i2.HorizontalAlignment = HorizontalAlignment.Right;
            i1.TextTrimming = i2.TextTrimming = TextTrimming.CharacterEllipsis;
            i1.FontSize = i2.FontSize = NotaType.RowLabel;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
            g.Children.Add(Row(13, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 8, Margin = new Thickness(-14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));   // may run under the last knob cell's spare width
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true,
            Children = { Docked(head, Dock.Top), Docked(new Border { Margin = new Thickness(0, 6, 0, 0), Child = knobs }, Dock.Bottom), new Border { Margin = new Thickness(0, 8, 0, 0), Child = window } },
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
        var stateTitle = DeviceBodyKit.Caption("State");
        var inPill = new UprightMeter { Ink = NotaPalette.VizInk, VerticalAlignment = VerticalAlignment.Stretch };
        var grPill = new UprightMeter { Ink = NotaPalette.TextSecondary, FromTop = true, VerticalAlignment = VerticalAlignment.Stretch };
        var inLbl = Caps("In"); inLbl.HorizontalAlignment = HorizontalAlignment.Center;
        var grLbl = Acr("GR"); grLbl.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(inPill, "The level the detector compares — this track's input, or the key when an external sidechain drives it; the dashed tick is the threshold");
        ToolTip.SetTip(grPill, "Gain reduction — how far the gate has turned the signal down now");
        var stateIn = Mono("", 7, TextPrimary); stateIn.HorizontalAlignment = HorizontalAlignment.Center;
        var stateGr = Mono("", 7, TextPrimary); stateGr.HorizontalAlignment = HorizontalAlignment.Center;
        static double Norm60(double db) => Math.Clamp((db + 60) / 60, 0, 1);
        readouts.Add(() =>
        {
            bool ext = Sc(S_ExtKey) > 0.5;
            double lvl = ext ? Sc(S_KeyDb) : Sc(S_InDb);
            inLbl.Text = ext ? "SC" : "In";
            inPill.Set(Norm60(lvl), Norm60(ThrDb()));
            grPill.Set(Math.Clamp(Sc(S_GrDb) / 60, 0, 1), double.NaN);
            stateIn.Text = lvl <= -119 ? "−∞" : NotaNum.F($"{lvl:0.0}");
            stateGr.Text = Sc(S_GrDb) < 0.05 ? "0.0\u2009dB" : Sc(S_GrDb) > 99 ? "−∞\u2009dB" : NotaNum.F($"−{Sc(S_GrDb):0.0}\u2009dB");
            stateTitle.Foreground = State() is 1 or 2 or 3 ? AccentBright : TextTertiary;   // lit while the gate is open
        });
        Control PillCol(UprightMeter pill, TextBlock lbl) => new DockPanel { Children = { Docked(lbl, Dock.Bottom), new Border { Margin = new Thickness(0, 0, 0, 4), Child = pill } } };
        var pills = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2) };
        pills.Children.Add(PillCol(inPill, inLbl));
        pills.Children.Add(Col(PillCol(grPill, grLbl), 1));
        var stateCol = DeviceBodyKit.Sec(
            new DockPanel { Children = { Docked(stateTitle, Dock.Top), Docked(stateGr, Dock.Bottom), Docked(stateIn, Dock.Bottom), pills } },
            new Thickness(4, 8, 4, 6), 75);
        DockPanel.SetDock(stateCol, Dock.Left);

        // ======================================================================
        // CENTRE — Signal
        // ======================================================================
        ShSignalView? signal = null;
        Control SignalTab()
        {
            signal = new ShSignalView();
            WireDrag(signal, h => h == ShSignalView.HThreshold ? Threshold : Return);
            ToolTip.SetTip(signal, "The input (light grey) and what passes (bright ink) over the window, with the threshold (dashed) and where the gate closes again (dashed). Drag a line up or down; double-click resets it.");
            Learn(signal, Threshold);

            var liveBtn = Latch(-1, () => live, () => { live = !live; if (!live) Array.Copy(scope, frozen, kScope); }, () => live ? "Live" : "Frozen",
                "Live — the graph follows the audio; click to freeze the picture");
            var ret = Mono("", 7, TextSecondary);
            readouts.Add(() => ret.Text = NotaNum.F($"return {RetDb():0.0}\u2009dB"));
            var head = HeadRow(Row(8, Caps("Mode"), ModeSeg(), new Border { Width = 4 }, Caps("Look"),
                Seg(Lookahead, new[] { "0", "1", "5\u2009ms" }, "Lookahead — the gate opens this much before the transient arrives (the track is delayed by it)"), liveBtn), ret);
            var knobs = KnobRow(new[]
            {
                K(Threshold, "Thresh", ThrShort),
                K(Attack, "Attack", AtkF),
                K(Hold, "Hold", HoldF),
                K(Release, "Release", RelF),
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
            ToolTip.SetTip(envView, "One opening: attack, hold, release in the chosen shape, down to the floor (inverted for Duck). Drag a node sideways for its time, the floor up or down; double-click resets.");
            Learn(envView, Release);

            var shapes = Chips(Shapes, i => Sel(Shape, 3) == i, i => SetP(Shape, i / 2f), Shape,
                "Shape — Linear: straight ramps over the times; Log: the classic one-pole curve; Snap: stays open, then shuts hard");
            var retrig = ToggleLatch(Retrigger, "Retrig", "Retrigger — trigger mode: each hit fires one attack → hold → release, however long the signal stays above the threshold");
            var times = Mono("", 7, TextSecondary);
            readouts.Add(() => times.Text = Times());
            var head = HeadRow(Row(8, Caps("Shape"), shapes, ModeSeg(), retrig), times);
            var knobs = KnobRow(new[]
            {
                K(Attack, "Attack", AtkF),
                K(Hold, "Hold", HoldF),
                K(Release, "Release", RelF),
                K(Floor, "Floor", FloorF, tip: "Floor — the level when closed (−∞ = silence); in Duck, how far the signal goes down"),
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
            ToolTip.SetTip(keyView, "The detector's band-pass (grey) — only this part of the key opens the gate — over the reduction in the window (neutral ink). Drag the nodes sideways; double-click resets.");
            Learn(keyView, DetHP);

            var eq = Latch(DetFilter, () => On(DetFilter), () => SetP(DetFilter, On(DetFilter) ? 0f : 1f), () => On(DetFilter) ? "SC EQ on" : "SC EQ off",
                "SC EQ — the detector hears the key through its band-pass; off = the full band");
            var look = Mono("", 7, TextSecondary);
            readouts.Add(() => look.Text = NotaNum.F($"look {LookMs()}\u2009ms"));
            var head = HeadRow(Row(8, Caps("Key"), KeyDrop(false), ModeSeg(), ListenLatch(), eq), look);
            var knobs = KnobRow(new[]
            {
                K(Threshold, "Thresh", ThrShort),
                K(Floor, "Range", FloorF, tip: "Range — how far the gate closes or the duck goes down (−∞ = all the way)"),
                K(Return, "Return", RetF, tip: "Return — how far under the threshold the key must fall before the gate closes (stops chatter)"),
                K(Lookahead, "Look", LookF, steps: 3, tip: "Lookahead — 0, 1 or 5\u2009ms"),
            },
            () => NotaNum.F($"key {KeyName()} · {(On(DetFilter) ? "fires on " + HpF(P(DetHP)) + "…" + LpF(P(DetLP)) : "full band")}"),
            () => NotaNum.F($"{(Duck() ? "ducks" : "closes")} to {(P(Floor) <= 0.001f ? "−∞\u2009dB" : NotaNum.F($"{FloorDb():0.0}\u2009dB"))} · {GrText()}"));
            return TabBody(head, keyView, knobs);
        }

        // ======================================================================
        // CENTRE — Pattern (rhythmic gate)
        // ======================================================================
        int NSteps() => StepCounts[Sel(PatSteps, 3)];
        int NLevels() => LevelCounts[Sel(PatLevels, 3)];
        int RateIdx() => Sel(PatRate, 5);
        string RateName() => On(PatSync) ? RateSync[RateIdx()] : RateFree[RateIdx()];
        float Qz(double v) { int l = NLevels(); return (float)(Math.Round(Math.Clamp(v, 0, 1) * l) / l); }
        float[] StepsNow() { int n = NSteps(); var a = new float[n]; for (int k = 0; k < n; k++) a[k] = P(Step1 + k); return a; }
        static float[] Resample(float[] a, int n) { var r = new float[n]; for (int i = 0; i < n; i++) r[i] = a[(int)((long)i * a.Length / n)]; return r; }
        float[] Template(int i) => Resample(Array.ConvertAll(Patterns[i].Steps, v => v / 8f), NSteps());
        // The browser's name: the shape the steps match, else Custom.
        int PatternIdx()
        {
            var cur = StepsNow();
            for (int i = 0; i < Patterns.Length; i++)
            {
                var t = Template(i);
                bool same = true;
                for (int k = 0; k < cur.Length && same; k++) same = Math.Abs(Qz(t[k]) - Qz(cur[k])) < 1e-3;
                if (same) return i;
            }
            return -1;
        }
        string PatternName() { int i = PatternIdx(); return i < 0 ? "Custom" : Patterns[i].Name; }
        // A one-shot write of the whole grid (each changed step is its own automation gesture).
        void WriteSteps(float[] a)
        {
            for (int k = 0; k < a.Length && k < kMaxSteps; k++)
                if (Math.Abs(P(Step1 + k) - a[k]) > 1e-5) SetP(Step1 + k, a[k]);
            RefreshAll();
        }

        var patBuf = new float[kMaxSteps];
        ShPatternView? patView = null;
        int patTool = 0;

        // A slider drawn as a filled bar with its value inside (Complexity / Smooth / Swing).
        Control FillBar(int p, Func<string> text, string tip)
        {
            var fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = NotaPalette.AccentSoft };
            var line = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = 3, Background = Brass };
            var tb = new TextBlock { FontSize = NotaType.KnobValue, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var grid = new Grid { Children = { fill, line, tb } };
            var host = new Border
            {
                Height = 20, Background = NotaPalette.Groove, CornerRadius = NotaRadius.ValueBar,
                ClipToBounds = true, Cursor = new Cursor(StandardCursorType.SizeWestEast), Child = grid,
            };
            bool drag = false;
            void Paint()
            {
                double v = P(p);
                double w = Math.Max(0, grid.Bounds.Width * v);
                fill.Width = w;
                line.Margin = new Thickness(Math.Max(0, w - 3), 0, 0, 0);
                line.IsVisible = v > 0.004;
                tb.Text = text();
                tb.Foreground = v > 0.004 ? TextPrimary : TextSecondary;
            }
            void SetX(double x) { Raw(p, x / Math.Max(1, grid.Bounds.Width)); Paint(); RefreshAll(); }
            host.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
                if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { Reset(p); Paint(); RefreshAll(); e.Handled = true; return; }
                drag = true; Begin(p); e.Pointer.Capture(host); SetX(e.GetPosition(grid).X); e.Handled = true;
            };
            host.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(grid).X); };
            host.PointerReleased += (_, e) => { if (!drag) return; drag = false; e.Pointer.Capture(null); End(p); };
            host.PointerCaptureLost += (_, _) => { if (!drag) return; drag = false; End(p); };
            grid.SizeChanged += (_, _) => Paint();
            readouts.Add(() => { if (!drag) Paint(); });
            Learn(host, p);
            ToolTip.SetTip(host, tip);
            return host;
        }

        // A plain push button (fills its cell).
        Border Button(string text, Action click, string tip, Func<bool>? enabled = null)
        {
            var b = TextButton(text);
            var tb = (TextBlock)b.Child!;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.VerticalAlignment = VerticalAlignment.Center;
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed || enabled?.Invoke() == false) return;
                click(); RefreshAll(); e.Handled = true;
            };
            readouts.Add(() => tb.Foreground = enabled?.Invoke() == false ? TextDisabled : TextPrimary);
            ToolTip.SetTip(b, tip);
            return b;
        }

        void Randomize()
        {
            double c = P(PatComplexity);
            int n = NSteps();
            var a = new float[n];
            for (int i = 0; i < n; i++)
            {
                bool on = i == 0 || Rng.NextDouble() < 0.35 + c * 0.45;
                a[i] = !on ? 0 : Qz(c < 0.3 ? 1 : Math.Clamp(1 - Rng.NextDouble() * c * 0.9, 0.125, 1));
            }
            WriteSteps(a);
        }
        void StepPattern(int dir)
        {
            int cur = PatternIdx();
            int i = ((cur < 0 ? 0 : cur + dir) % Patterns.Length + Patterns.Length) % Patterns.Length;
            WriteSteps(Array.ConvertAll(Template(i), v => Qz(v)));
        }

        Control PatternTab()
        {
            patView = new ShPatternView { Tool = patTool };
            var touched = new HashSet<int>();
            patView.StepsChanged += a =>
            {
                for (int k = 0; k < a.Length; k++)
                {
                    if (Math.Abs(P(Step1 + k) - a[k]) < 1e-5) continue;
                    if (touched.Add(k)) Begin(Step1 + k);
                    Raw(Step1 + k, a[k]);
                }
                RefreshAll();
            };
            patView.GestureEnd += () => { foreach (int k in touched) End(Step1 + k); touched.Clear(); };
            ToolTip.SetTip(patView, "The step pattern — each column is one step's level, the line is the gain it makes (smooth and swing bend it). Draw sets steps, Line draws a ramp, Erase clears, Flip toggles on / off.");

            var tools = Segments(new[] { "Draw", "Line", "Erase", "Flip" }, () => patTool, i => { patTool = i; if (patView is not null) patView.Tool = i; }, out var toolSync, padX: 6);
            readouts.Add(toolSync);
            ToolTip.SetTip(tools, "Mouse tool for the grid");

            Border Arrow(GlyphKind kind, int dir)
            {
                var b = new Border { Width = 18, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = new Glyph(kind, 7) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; StepPattern(dir); e.Handled = true; };
                return b;
            }
            var pname = new TextBlock { FontSize = NotaType.KnobValue, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            readouts.Add(() => pname.Text = PatternName());
            var browser = new Border
            {
                Height = 20, Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field,
                Child = new DockPanel { Children = { Docked(Arrow(GlyphKind.ChevronLeft, -1), Dock.Left), Docked(Arrow(GlyphKind.ChevronRight, 1), Dock.Right), pname } },
            };
            ToolTip.SetTip(browser, "Pattern shapes — the arrows step through them (fitted to the grid)");
            var rnd = Button("Rnd", Randomize, "Roll a new pattern — Complexity sets how busy and how varied it is");
            var cx = FillBar(PatComplexity, () => "Complexity " + Pct(P(PatComplexity)), "Complexity — how busy and varied Rnd makes the pattern");
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,120"), ColumnSpacing = 6, Height = 22, Margin = new Thickness(0, 8, 0, 0) };
            head.Children.Add(tools); head.Children.Add(Col(browser, 1)); head.Children.Add(Col(rnd, 2)); head.Children.Add(Col(cx, 3));

            var smooth = FillBar(PatSmooth, () => "Smooth " + Pct(P(PatSmooth)), "Smooth — ramps into each step instead of jumping (up to half a step)");
            var swing = FillBar(PatSwing, () => NotaNum.F($"Swing {P(PatSwing) * 75:0} %"), "Swing — every second step comes late");
            var nSt = Mono("", 11, TextPrimary); var nLv = Mono("", 11, TextPrimary);
            readouts.Add(() => { nSt.Text = NSteps().ToString(); nLv.Text = NLevels().ToString(); });
            Border Cycle(TextBlock t, Action a, string tip)
            {
                var b = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = t };
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; a(); RefreshAll(); e.Handled = true; };
                ToolTip.SetTip(b, tip);
                return b;
            }
            var gridSize = new Border
            {
                Height = 20, Padding = new Thickness(8, 0), Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field,
                Child = Row(4, Caps("Grid"),
                    Cycle(nSt, () =>
                    {
                        var cur = StepsNow();
                        int si = (Sel(PatSteps, 3) + 1) % 3;
                        var next = Resample(cur, StepCounts[si]);
                        SetP(PatSteps, si / 2f);
                        WriteSteps(next);
                    }, "Steps — 8, 16 or 32 (the pattern is stretched to fit)"),
                    Mono("×", 11, TextTertiary),
                    Cycle(nLv, () =>
                    {
                        var cur = StepsNow();
                        SetP(PatLevels, (Sel(PatLevels, 3) + 1) % 3 / 2f);
                        WriteSteps(Array.ConvertAll(cur, v => Qz(v)));
                    }, "Levels — 4, 8 or 16 heights a step snaps to")),
            };
            var target = Seg(PatTarget, new[] { "Off", "Volume", "Map" },
                "Target — Off; Volume: the pattern gates the audio; Map: the audio is left alone and the pattern drives Pattern Out, a CV source in the Modular view");
            var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto"), ColumnSpacing = 6, Height = 22 };
            foot.Children.Add(smooth); foot.Children.Add(Col(swing, 1)); foot.Children.Add(Col(gridSize, 2)); foot.Children.Add(Col(target, 3));

            return new DockPanel
            {
                LastChildFill = true,
                Children = { Docked(head, Dock.Top), Docked(foot, Dock.Bottom), new Border { Margin = new Thickness(0, 6), Child = patView } },
            };
        }

        Control RhythmTab()
        {
            var rate = K(PatRate, "Rate", _ => RateName(), steps: 5, cellW: 80, tip: "Rate — how long the whole pattern takes: 1/4 … 4 bars in Sync, 4 … 0.25\u2009Hz in Free");
            var depth = K(PatDepth, "Depth", v => Pct((float)v), cellW: 80, tip: "Depth — how far an empty step turns the signal down");
            var knobs = new UniformGrid { Columns = 2, Children = { rate, depth } };
            var clock = Segments(new[] { "Sync", "Free" }, () => On(PatSync) ? 0 : 1, i => { SetP(PatSync, i == 0 ? 1f : 0f); RefreshAll(); }, out var clockSync, fill: true);
            readouts.Add(clockSync);
            Learn(clock, PatSync);
            ToolTip.SetTip(clock, "Clock — Sync locks the pattern to the song's bars, Free runs it in Hz");
            var clockRow = LabelRow("Clock", clock);
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
            btns.Children.Add(Button("Copy", () => _clipboard = StepsNow(), "Copy the steps"));
            btns.Children.Add(Col(Button("Paste", () => { if (_clipboard is { } c) WriteSteps(Array.ConvertAll(Resample(c, NSteps()), v => Qz(v))); }, "Paste copied steps (fitted to the grid)", () => _clipboard is not null), 1));
            btns.Children.Add(Col(Button("Clear", () => WriteSteps(new float[NSteps()]), "Clear every step"), 2));
            return Spread(
                knobs,
                clockRow,
                btns,
                new StackPanel { Spacing = 5, Children = {
                    Toggle(PatRetrig, "Retrig on MIDI note", "Retrig — every MIDI note on this track (or the key track) restarts the pattern"),
                    Toggle(PatGated, "Only while gate open", "Gated — the pattern plays only while the threshold gate is open (gate × pattern)") } });
        }

        // ======================================================================
        // RIGHT — Detector / Meters
        // ======================================================================
        static Control LabelRow(string label, Control c, double labW = 58)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")) };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(c, 1));
            return g;
        }
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(0, 4, 0, 0), Child = g };
        }

        Control StateBox()
        {
            var t = Caps("State");
            var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Group, VerticalAlignment = VerticalAlignment.Center };
            var l1 = Mono("", 11, TextPrimary);
            var l2 = Mono("", 11, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var b = DeviceBodyKit.Well(new StackPanel { Spacing = 2, Children = { Row(8, t, Row(6, dot, l1)), l2 } }, new Thickness(10, 4));
            readouts.Add(() =>
            {
                int st = State();
                bool moving = st is 1 or 3 or 4;
                string name = Duck() ? st switch { 2 => "ducking", 0 => "clear", _ => StateNames[st] } : StateNames[st];
                l1.Text = NotaNum.F($"{name} · {Sc(S_GateGain) * 100:0}\u2009%");
                l2.Text = P(Floor) > 0.001f && st == 0 ? NotaNum.F($"{GrText()} · floor {FloorDb():0.0}") : NotaNum.F($"{GrText()} · look {LookMs()}\u2009ms");
                dot.Background = st == 2 ? AccentBright : moving ? Brass : TextTertiary;
                l1.Foreground = moving ? AccentBright : TextPrimary;
            });
            ToolTip.SetTip(b, "What the gate is doing now — closed, attack, open, hold or release — and its gain");
            return b;
        }

        Control DetectorTab()
        {
            var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
            filters.Children.Add(Toggle(DetFilter, "Detector filter", "Detector filter — the key goes through the band-pass (Sidechain tab) before the detector"));
            filters.Children.Add(Col(Toggle(PeakHold, "Peak hold", "Peak hold — the detector holds each peak longer, so low notes and slow waves don't chatter the gate"), 1));
            return Spread(
                LabelRow("Source", KeyDrop(true)),
                SliderRow("Thresh", Threshold, ThrShort),
                SliderRow("Return", Return, RetShort),
                SliderRow("Floor", Floor, FloorShort),
                StateBox(),
                new StackPanel { Spacing = 3, Children = { ExtSwitch(), filters } });
        }

        Control MetersTab()
        {
            Control Line(Func<string> k, Func<string> v, IBrush? ink = null)
            {
                var key = Caps("", TextSecondary);
                var val = Mono("", 11, ink ?? TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => { key.Text = k(); val.Text = v(); });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(key);
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = DeviceBodyKit.Well(new StackPanel { Spacing = 2, Children = {
                    Caps("Measurements"),
                    Line(() => Sc(S_ExtKey) > 0.5 ? "sc in" : "in", () => DbF(Sc(S_ExtKey) > 0.5 ? Sc(S_KeyDb) : Sc(S_InDb))),
                    Line(() => "out", () => DbF(Sc(S_OutDb))),
                    Line(() => "GR · peak", () => NotaNum.F($"{(Sc(S_GrDb) < 0.05 ? "0.0" : Sc(S_GrDb) > 99 ? "−∞" : NotaNum.F($"−{Sc(S_GrDb):0.0}"))} · {(Sc(S_PeakGrDb) < 0.05 ? "0.0" : Sc(S_PeakGrDb) > 99 ? "−∞" : NotaNum.F($"−{Sc(S_PeakGrDb):0.0}"))}\u2009dB"), TextPrimary) } });
            var sliders = new StackPanel { Spacing = 6, Children = {
                SliderRow("SC HP", DetHP, HpF),
                SliderRow("SC LP", DetLP, LpF),
                SliderRow("Range", Floor, FloorShort) } };
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
            btns.Children.Add(ListenLatch(fill: true));
            btns.Children.Add(Col(Latch(-1, () => false, () => engine.DeviceAction(track, di, A_ResetMeters, 0, 0), () => "Reset",
                "Reset — clear the peak reduction, the opening count and the history", fill: true), 1));
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(box, Dock.Top),
                Docked(new Border { Margin = new Thickness(0, 8, 0, 0), Child = sliders }, Dock.Top),
                Docked(new StackPanel { Spacing = 6, Children = {
                    ExtSwitch(),
                    Toggle(DetFilter, "Detector filter", "Detector filter — the key goes through the band-pass before the detector") } }, Dock.Bottom),
                Docked(new Border { Padding = new Thickness(0, 0, 0, 8), Child = btns }, Dock.Bottom) } };
            return new Border { Padding = new Thickness(0, 8, 0, 0), Child = body };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        int centreTab = 0;
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var rhythmHost = new ContentControl();
        var centreBodies = new Control?[4];
        var rightBodies = new Control?[2];
        var rhythmBodies = new Control?[2];
        var extras = Mono("", 11, TextSecondary);
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
                3 => NotaNum.F($"step {(int)Sc(kPatAt + P_Step) + 1}/{NSteps()} · {(On(PatSync) ? $"{Sc(S_Bpm):0} BPM" : RateFree[RateIdx()])}"),
                _ => NotaNum.F($"window {WindowText()} · look {LookMs()}\u2009ms"),
            };
            extrasHost.Cursor = centreTab == 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        });
        ToolTip.SetTip(extrasHost, "Signal tab: click to change the window — " + string.Join(" · ", WindowNames));
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => EnvelopeTab(), 2 => SidechainTab(), 3 => PatternTab(), _ => SignalTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? MetersTab() : DetectorTab();
        Control RhythmBody(int t) => rhythmBodies[t] ??= t == 1 ? DetectorTab() : RhythmTab();

        // The right panel follows the centre: Rhythm / Detector beside the pattern, else Detector / Meters.
        var rightFrame = DeviceBodyKit.TabFrame(new[] { "Detector", "Meters" }, 0, rightHost, RightBody, _ => RefreshAll());
        Control? rhythmFrame = null;
        var right = DeviceBodyKit.Sec(rightFrame, SectionPad, 229);
        var centre = DeviceBodyKit.TabFrame(new[] { "Signal", "Envelope", "Sidechain", "Pattern" }, 0, centreHost, CentreBody, t =>
        {
            centreTab = t;
            right.Child = t == 3 ? rhythmFrame ??= DeviceBodyKit.TabFrame(new[] { "Rhythm", "Detector" }, 0, rhythmHost, RhythmBody, _ => RefreshAll()) : rightFrame;
            Refresh();
        }, extrasHost);
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = DeviceBodyKit.Sec(centre);
        centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        var root = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { stateCol, right, centreBox } });

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
            if (centreTab == 3 && patView is not null)
            {
                int ns = NSteps();
                for (int k = 0; k < ns; k++) patBuf[k] = P(Step1 + k);
                int tg = Sel(PatTarget, 3);
                bool on = tg > 0 && scN >= kScope;
                patView.Set(patBuf, ns, NLevels(), on ? (int)Sc(kPatAt + P_Step) : -1, on ? Sc(kPatAt + P_Phase) : -1,
                    P(PatSmooth), P(PatSwing) * 0.375, P(PatDepth), tg == 2, tg > 0,
                    tg == 2 ? "Map → Pattern Out" : tg == 1 ? "gain" : "off");
            }
            if (centreTab == 2 && keyView is not null)
                keyView.Set(Exp(P(DetHP), 20, 2000), Exp(P(DetLP), 200, 20000), On(DetFilter), h, H_Gate, n, Duck() ? "duck" : "reduction");
            RefreshAll();
        }
        // Graphs follow the params every tick, also while dragged (the envelope keeps its time span then).
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
