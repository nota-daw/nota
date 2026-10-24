// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Ceiling body (look-ahead limiter, device kind 14), a build of
// the "Nota Ceiling" mockup (700 × 260) on the Beat Repeat / Shutter frame: an always-visible
// LIMIT column (input · reduction · output), a centre panel with Level / Reduction / Loudness
// tabs — the input, the part over the ceiling and the output over 4 s with the reduction under
// it (drag the ceiling), the reduction with the transients that reached the clip, momentary /
// short-term / integrated loudness over 60 s around the Target (drag it) — with Character over
// the graph and Gain / Ceiling / Release under it, and a right panel with Meters / Detector
// tabs. The pictures come from the engine (Ceiling.h scopeRead). Every control
// is a device param (raw units), so automation / MIDI learn / presets / A-B / persistence come
// for free; the key source is the device's sidechain routing, the resets are device actions.
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

internal sealed class CeilingDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Ceiling.h) ─────────────────────────────
    private const int Ceil = 0, Gain = 1, Release = 2, AutoRelease = 3, Character = 4, Lookahead = 5, StereoLink = 6,
        TruePeak = 7, DeltaP = 8, ScHp = 9, Target = 10, kParams = 11;
    // Scope layout (Ceiling::S_* / kTele / kLvl / kLoud).
    private const int S_InPeak = 0, S_OutPeak = 1, S_Gr = 2, S_LufsM = 3, S_LufsS = 4, S_LufsI = 5, S_TruePeak = 6,
        S_PeakInHold = 7, S_PeakOutHold = 8, S_MaxGrHold = 9, S_TpHold = 10, S_Lra = 11, S_Plr = 12, S_OverPct = 13,
        S_AvgGr = 14, S_WinMaxGr = 15, S_RelNow = 16, S_RelMin = 17, S_RelMax = 18, S_Clips = 19, S_SampleRate = 20,
        S_Cpu = 21, S_Latency = 22, S_WinInPeak = 23, S_WinOutPeak = 24, S_Delta = 29, S_Measured = 30,
        kTele = 40, kLvl = 64, kLoud = 120;
    private const int L_In = 0, L_Out = 1, L_GrMax = 2, L_Clip = 3, L_GrAvg = 6;
    private const int kLvlAt = kTele, kLoudAt = kTele + 7 * kLvl, kScope = kLoudAt + 3 * kLoud;
    private const int A_ResetPeaks = 0, A_ResetLoudness = 1;

    private static readonly string[] CharNames = { "Clean", "Punch", "Glue" };
    private static readonly string[] CharWords = { "transparent", "lets the attack through", "slow and dense" };
    private static readonly (string Name, float Lufs)[] Targets =
    {
        ("−23\u2009LUFS · broadcast", -23), ("−18\u2009LUFS · audiobook", -18), ("−16\u2009LUFS · podcast", -16), ("−14\u2009LUFS · streaming", -14),
        ("−11\u2009LUFS · loud", -11), ("−9\u2009LUFS · club", -9), ("−8\u2009LUFS · very loud", -8),
    };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "Limiter";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        var mn = new float[kParams];
        var mx = new float[kParams];
        for (int p = 0; p < kParams; p++) { mn[p] = engine.DeviceParamMin(track, di, p); mx[p] = engine.DeviceParamMax(track, di, p); if (mx[p] <= mn[p]) mx[p] = mn[p] + 1; }
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, mn[p], mx[p]));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        bool On(int p) => P(p) >= 0.5f;
        int CharI() => Math.Clamp((int)Math.Round(P(Character)), 0, 2);

        // ---- 0..1 mappings per param (release and the key high-pass are log) ----------
        double ToN(int p, double v) => p switch
        {
            Release or ScHp => Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p]),
            _ => (v - mn[p]) / (mx[p] - mn[p]),
        };
        double FromN(int p, double n)
        {
            n = Math.Clamp(n, 0, 1);
            return p switch
            {
                Release or ScHp => mn[p] * Math.Pow(mx[p] / mn[p], n),
                _ => mn[p] + n * (mx[p] - mn[p]),
            };
        }
        double N(int p) => ToN(p, P(p));
        void SetN(int p, double n) => Raw(p, (float)FromN(p, n));
        double DefN(int p) => ToN(p, engine.DeviceParamDefault(track, di, p));

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;
        double ScDb(int i) => scN > i ? scope[i] : -120;

        // ---- units ----------------------------------------------------------------
        static string Sgn(double v) => NotaNum.F($"{v:+0.0;−0.0;0.0}");
        static string Lvl(double db) => db <= -119 ? "—" : Sgn(db);
        static string Lufs(double v) => v <= -119 ? "—" : NotaNum.F($"{v:0.0}");
        static string GrF(double gr) => gr > 0.05 ? NotaNum.F($"−{gr:0.0}") : "0.0";
        static string MsF(double ms) => ms < 10 ? NotaNum.F($"{ms:0.0} ms") : NotaNum.F($"{ms:0} ms");
        static string HzF(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0} k") : NotaNum.F($"{hz:0} Hz");
        bool Tp() => On(TruePeak);
        string CeilF(double v) => NotaNum.F($"{v:0.0} dB") + (Tp() ? "\u2009TP" : "");
        string GainF(double v) => Sgn(v);
        string RelF(double v) => On(AutoRelease) ? "auto" : MsF(v);
        string LookF(double v) => MsF(v);
        static string LinkF(double v) => NotaNum.F($"{v:0} %");
        string HpF(double v) => v <= 20.5 ? "off" : HzF(v);
        static string TargetF(double v) => NotaNum.F($"{v:0} LUFS");
        bool HaveI() => ScDb(S_LufsI) > -119;
        double FromTarget() => Sc(S_LufsI) - P(Target);
        string TargetWords()
        {
            if (!HaveI()) return "measuring…";
            double d = FromTarget();
            return Math.Abs(d) <= 1 ? "on target" : d > 0 ? NotaNum.F($"{d:0.0}\u2009LU loud") : NotaNum.F($"{-d:0.0}\u2009LU quiet");
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 0) => FxParts.Label(t, c);
        static TextBlock Mono(string t, double fs, IBrush c) { var tb = FxParts.Info(t, c); tb.TextTrimming = TextTrimming.None; return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, string tip)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(N(p), 1.0) { Accent = true, Default = DefN(p), Width = 34, Height = 34 };
            knob.Format = v => fmt(FromN(p, v));   // read-out at any value: typing searches it
            knob.ValueChanged += v => { SetN(p, v); val.Text = fmt(P(p)); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            var cell = KnobCell(name, knob, val, 48);
            readouts.Add(() =>
            {
                if (!knob.Dragging) { double c = N(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(p));
            });
            return cell;
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

        // A latching chip: lit when `lit()`, a click runs `click`. `fill` = a full-width button.
        Border Chip(int p, Func<bool> lit, Action click, string text, string tip, bool fill = false)
        {
            // Raised at rest; on, the soft tint with orange text.
            var tb = new TextBlock { Text = text, FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = NotaSize.Dropdown, Padding = new Thickness(fill ? 6 : 9, 0), CornerRadius = NotaRadius.Field, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? AccentBright : TextPrimary;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            if (p >= 0) Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Control ParamChip(int p, string text, string tip) => Chip(p, () => On(p), () => SetP(p, On(p) ? 0f : 1f), text, tip, fill: true);

        // Slider row: label · value bar · value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool modulation = false)
        {
            var bar = DeviceCardKit.SliderRow("", () => N(p), v => { SetN(p, v); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: 44, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // A dropdown: [label  value ▾] opens `items`; the current one is dotted.
        Border Drop(string caps, Func<string> text, Func<bool> hot, Func<IEnumerable<(string Label, bool Current, Action Pick)>> items, string tip, int learnParam = -1)
        {
            // Raised like a button: an ink-3 label, the value 11 / ink 1 and a chevron.
            var name = new TextBlock { FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chev = new Glyph(GlyphKind.ChevronDown, 9) { Margin = new Thickness(6, 0, 0, 0) };
            var inner = new DockPanel { Children = { Docked(chev, Dock.Right) } };
            if (caps.Length > 0) inner.Children.Add(Docked(new Border { Padding = new Thickness(0, 0, 6, 0), Child = Caps(caps) }, Dock.Left));
            inner.Children.Add(name);
            var box = new Border
            {
                Height = NotaSize.Dropdown, Background = NotaPalette.SurfaceHover, CornerRadius = NotaRadius.Field,
                Padding = new Thickness(8, 0, 6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = inner,
            };
            box.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            readouts.Add(() =>
            {
                bool h = hot();
                name.Text = text();
                name.Foreground = TextPrimary;
                chev.Foreground = h ? TextPrimary : TextSecondary;
            });
            ToolTip.SetTip(box, tip);
            if (learnParam >= 0) Learn(box, learnParam);
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                foreach (var (label, cur, pick) in items())
                {
                    var mi = new MenuItem { Header = label };
                    mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = cur;
                    var pk = pick;
                    mi.Click += (_, _) => { pk(); RefreshAll(); };
                    fly.Items.Add(mi);
                }
                fly.ShowAt(box);
                e.Handled = true;
            };
            return box;
        }

        // Key source: Internal, or another track (the device's sidechain routing).
        string SrcName(int id) => TrackNames.Of(engine, id);
        int Src() => engine.DeviceSidechainSource(track, di);
        bool ExtOn() => Src() >= 0;
        string KeyName() => ExtOn() ? SrcName(Src()) : "Internal";
        Control KeyDrop() => Drop("SC", KeyName, ExtOn, () =>
        {
            var list = new List<(string, bool, Action)>
            {
                ("Internal — this track", !ExtOn(), () => { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); }),
            };
            for (int i = 0; i < engine.TrackCount; i++)
                if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track)
                {
                    int id = ti.Id;
                    list.Add((SrcName(id), Src() == id, () => { engine.SetDeviceSidechainSource(track, di, id); ctx.NotifyChanged(); }));
                }
            return list;
        }, "Key source — Internal limits on this track's own peaks; pick another track to duck this one when that one peaks");

        Control TargetDrop() => Drop("", () => TargetF(P(Target)), () => false, () =>
        {
            var list = new List<(string, bool, Action)>();
            foreach (var (label, v) in Targets) { float vv = v; list.Add((label, Math.Abs(P(Target) - vv) < 0.05f, () => SetP(Target, vv))); }
            return list;
        }, "Target — the loudness the meters compare against: −14 for streaming, −16 podcasts, −23 broadcast. It does not change the sound; drag the dashed line for any value", Target);

        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 24, ColumnSpacing = 8 };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control CharSeg(bool words = true)
        {
            var seg = Segments(CharNames, CharI, i => { SetP(Character, i); RefreshAll(); }, out var sync, padX: 6);
            seg.VerticalAlignment = VerticalAlignment.Center;
            readouts.Add(sync);
            Learn(seg, Character);
            ToolTip.SetTip(seg, "Character — Clean: transparent, the fastest attack; Punch: a slower attack lets the transient through to the clip; Glue: a slow release and a soft knee under the ceiling");
            var wordsTb = FxParts.Info();
            readouts.Add(() => wordsTb.Text = CharWords[CharI()]);
            return words ? Row(8, Caps("Character"), seg, wordsTb) : Row(8, Caps("Character"), seg);
        }
        Control Info(Func<string> text, IBrush? ink = null)
        {
            var t = FxParts.Info("", ink);
            readouts.Add(() => t.Text = text());
            return t;
        }

        // The knob row under every graph: Gain, Ceiling, Release, the two chips, two info lines.
        int centreTab = 0;
        Control KnobRow(Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 7, TextTertiary); i1.HorizontalAlignment = HorizontalAlignment.Right;
            var i2 = Mono("", 7, TextTertiary); i2.HorizontalAlignment = HorizontalAlignment.Right;
            i1.TextTrimming = i2.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var chips = new StackPanel
            {
                Spacing = 4, Width = 70, VerticalAlignment = VerticalAlignment.Center, Children =
                {
                    ParamChip(AutoRelease, "Auto rel", "Auto release — the release slows as the reduction deepens (up to 5× at 7\u2009dB), so dense passages don't pump"),
                    ParamChip(TruePeak, "True peak", "True peak — the detector reads the inter-sample peak (4×), so the ceiling holds in dBTP after conversion"),
                },
            };
            var knobs = Row(10,
                K(Gain, "Gain", GainF, "Gain — how hard the signal is driven into the ceiling"),
                K(Ceil, "Ceiling", v => NotaNum.F($"{v:0.0}\u2009dB"), "Ceiling — the output never goes above it (in dBTP with True peak on)"),
                K(Release, "Release", RelF, "Release — how fast the gain comes back (the character scales it; Auto rel lets the signal decide)"));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Height = 54, ColumnSpacing = 12 };
            g.Children.Add(knobs);
            g.Children.Add(Col(chips, 1));
            g.Children.Add(Col(new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2), Children = { i1, i2 } }, 2));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(0, 6, 0, 0),
            Children = { Docked(head, Dock.Top), Docked(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 6, 0, 6), Child = window } },
        };
        void WireDrag(ShDragView v, int param)
        {
            v.Value = _ => N(param);
            v.Changed += (_, x) => { SetN(param, x); RefreshAll(); };
            v.GestureBegin += _ => Begin(param);
            v.GestureEnd += _ => End(param);
            v.ResetRequested += _ => { Reset(param); RefreshAll(); };
            Learn(v, param);
        }
        string ReleaseRange()
        {
            if (!On(AutoRelease)) return "release " + MsF(Sc(S_RelNow) > 0 ? Sc(S_RelNow) : P(Release));
            double lo = Sc(S_RelMin), hi = Sc(S_RelMax);
            return Math.Abs(hi - lo) < 1 ? "release " + MsF(lo) + " · auto" : NotaNum.F($"release {lo:0}…{hi:0} ms by the signal");
        }
        string ClipText()
        {
            int n = (int)Sc(S_Clips);
            string times = n == 1 ? "time" : "times";
            return CharI() == 1 ? NotaNum.F($"attack passed to the clip · {n} {times}") : NotaNum.F($"reached the clip · {n} {times}");
        }

        // ======================================================================
        // LEFT — LIMIT column (input · reduction · output)
        // ======================================================================
        var colTitle = Caps("Limit"); colTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var inPill = new FxLevel { VerticalAlignment = VerticalAlignment.Stretch, Width = 12 };
        var grPill = new FxLevel { Ink = Brass, FromTop = true, VerticalAlignment = VerticalAlignment.Stretch, Width = 12 };
        var outPill = new FxLevel { VerticalAlignment = VerticalAlignment.Stretch, Width = 12 };
        ToolTip.SetTip(inPill, "The input after Gain, with the ceiling marked");
        ToolTip.SetTip(grPill, "Gain reduction, 0 … 24\u2009dB");
        ToolTip.SetTip(outPill, "The output");
        var colGr = Mono("", 7, TextPrimary); colGr.HorizontalAlignment = HorizontalAlignment.Center;
        var colOut = Mono("", 7, TextPrimary); colOut.HorizontalAlignment = HorizontalAlignment.Center;
        static double NormLvl(double db) => Math.Clamp((db + 48) / 60, 0, 1);   // −48 … +12 dBFS
        readouts.Add(() =>
        {
            inPill.Set(NormLvl(ScDb(S_InPeak)), NormLvl(P(Ceil)));
            grPill.Set(Math.Clamp(Sc(S_Gr) / 24, 0, 1));
            outPill.Set(NormLvl(ScDb(S_OutPeak)));
            colGr.Text = GrF(Sc(S_Gr));
            colOut.Text = Lvl(ScDb(S_OutPeak));
            colTitle.Foreground = Sc(S_Gr) > 0.1 ? AccentBright : TextTertiary;   // lit while it limits
        });
        Control PillCol(FxLevel pill, string lbl)
        {
            var l = Caps(lbl); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 4, 0, 0);
            return new DockPanel { Children = { Docked(l, Dock.Bottom), new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = pill } } };
        }
        var pills = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), ColumnSpacing = 5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5) };
        pills.Children.Add(PillCol(inPill, "In"));
        pills.Children.Add(Col(PillCol(grPill, "GR"), 1));
        pills.Children.Add(Col(PillCol(outPill, "Out"), 2));
        var leftCol = new RuleSection
        {
            Width = 64, Padding = new Thickness(0, 8, 0, 6),
            Child = new DockPanel { Children = { Docked(colTitle, Dock.Top), Docked(colOut, Dock.Bottom), Docked(colGr, Dock.Bottom), pills } },
        };
        DockPanel.SetDock(leftCol, Dock.Left);

        // ======================================================================
        // CENTRE — Level / Reduction / Loudness
        // ======================================================================
        CeLevelView? level = null;
        Control LevelTab()
        {
            level = new CeLevelView { CeilToNorm = db => ToN(Ceil, db) };
            WireDrag(level, Ceil);
            ToolTip.SetTip(level, "The last 4 s: the input (grey), the part over the ceiling (red) and the output (neutral ink), with the reduction under it. Drag the ceiling line; double-click resets.");
            var head = HeadRow(CharSeg(), Info(() => "ceiling " + CeilF(P(Ceil))));
            var knobs = KnobRow(
                () => ScDb(S_WinInPeak) <= -119 ? "no signal" : NotaNum.F($"in {Lvl(ScDb(S_WinInPeak))} → out {Lvl(ScDb(S_WinOutPeak))}"),
                () => NotaNum.F($"over {Sc(S_OverPct) * 100:0} % of the time"));
            return TabBody(head, level, knobs);
        }

        CeReductionView? reduction = null;
        Control ReductionTab()
        {
            reduction = new CeReductionView();
            ToolTip.SetTip(reduction, "The gain reduction over the last 4 s: lighter grey where a transient got through to the clip, the dashed line is the mean");
            var head = HeadRow(CharSeg(), Info(() => NotaNum.F($"avg {GrF(Sc(S_AvgGr))} · max {GrF(Sc(S_WinMaxGr))}")));
            var knobs = KnobRow(ReleaseRange, ClipText);
            return TabBody(head, reduction, knobs);
        }

        CeLoudnessView? loudness = null;
        Control LoudnessTab()
        {
            loudness = new CeLoudnessView { TargetToNorm = t => ToN(Target, t) };
            WireDrag(loudness, Target);
            ToolTip.SetTip(loudness, "Momentary (light grey), short-term (neutral ink) and integrated (mid grey) loudness over the last 60 s around the target, ±1 LU shaded. Drag the target line; double-click resets.");
            var head = HeadRow(Row(8, CharSeg(false), Caps("Target"), TargetDrop()),
                Info(() => HaveI() ? NotaNum.F($"{FromTarget():+0.0;−0.0;0.0}\u2009LU from target") : "measuring…"));
            var knobs = KnobRow(
                () => NotaNum.F($"LRA {Sc(S_Lra):0.0}\u2009LU · PLR {(HaveI() ? NotaNum.F($"{Sc(S_Plr):0.0}") : "—")}"),
                () => NotaNum.F($"GR on average {GrF(Sc(S_AvgGr))}\u2009dB"));
            return TabBody(head, loudness, knobs);
        }

        // ======================================================================
        // RIGHT — Meters / Detector
        // ======================================================================
        // Rows on the device grid; null = a rule.
        static Control Stack(string defs, params Control?[] rows)
        {
            var g = new Grid { RowDefinitions = new RowDefinitions(defs), Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < rows.Length; i++)
                g.Children.Add(GRow(rows[i] ?? new Border { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center }, i));
            return g;
        }
        Control MeterGrid(params (string Key, Func<string> Val, Func<IBrush> Ink)[] cells)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto"), ColumnSpacing = 6 };
            for (int i = 0; i < (cells.Length + 1) / 2; i++) g.RowDefinitions.Add(new RowDefinition(17, GridUnitType.Pixel));
            for (int i = 0; i < cells.Length; i++)
            {
                var (key, val, ink) = cells[i];
                int r = i / 2, c = (i % 2) * 2;
                var k = Caps(key); k.Margin = new Thickness(c > 0 ? 6 : 0, 0, 0, 0);
                var v = FxParts.Value(); v.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => { v.Text = val(); v.Foreground = ink(); });
                g.Children.Add(GRow(Col(k, c), r));
                g.Children.Add(GRow(Col(v, c + 1), r));
            }
            return new Border { Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 4, 9, 4), Child = g };
        }
        IBrush OverInk(double db, double limit) => db > limit + 0.05 ? NotaPalette.DangerBright : TextPrimary;
        Control ResetRow(bool loud)
        {
            var reset = Chip(-1, () => false, () => engine.DeviceAction(track, di, loud ? A_ResetLoudness : A_ResetPeaks, 0, 0),
                loud ? "Reset" : "Reset peaks", loud ? "Reset — start the integrated loudness, the range and the peaks again" : "Reset peaks — clear the peak holds, the max reduction and the clip count", fill: true);
            reset.HorizontalAlignment = HorizontalAlignment.Right;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
            g.Children.Add(KeyDrop());
            g.Children.Add(Col(reset, 1));
            reset.HorizontalAlignment = HorizontalAlignment.Stretch;
            return g;
        }
        Control DeltaSwitch() => Toggle(DeltaP, "Delta · hear what it removes", "Delta — play the difference between the input and the output: what the limiter takes away");
        Control LookRow() => SliderRow("Look", Lookahead, LookF);
        Control LinkRow() => SliderRow("Link", StereoLink, LinkF);

        Control MetersTab() => Stack("Auto,3,22,22,8,22,*",
            MeterGrid(
                ("peak in", () => Lvl(ScDb(S_PeakInHold)), () => OverInk(ScDb(S_PeakInHold), 0)),
                ("peak out", () => Lvl(ScDb(S_PeakOutHold)), () => TextPrimary),
                ("max GR", () => GrF(Sc(S_MaxGrHold)), () => TextPrimary),
                ("LUFS-S", () => Lufs(ScDb(S_LufsS)), () => TextPrimary),
                ("LUFS-I", () => Lufs(ScDb(S_LufsI)), () => TextPrimary),
                ("true pk", () => Lvl(ScDb(S_TpHold)), () => OverInk(ScDb(S_TpHold), P(Ceil)))),
            new Border(), LookRow(), LinkRow(), null, ResetRow(false), DeltaSwitch());

        Control LoudMetersTab()
        {
            var big = FxParts.Value(); big.FontSize = 16;
            var unit = Caps("LUFS"); unit.VerticalAlignment = VerticalAlignment.Bottom; unit.Margin = new Thickness(0, 0, 0, 3);
            var verdict = FxParts.Info("", Success); verdict.HorizontalAlignment = HorizontalAlignment.Right; verdict.VerticalAlignment = VerticalAlignment.Bottom; verdict.Margin = new Thickness(0, 0, 0, 2);
            readouts.Add(() =>
            {
                big.Text = HaveI() ? NotaNum.F($"{Sc(S_LufsI):0.0}") : "—";
                verdict.Text = TargetWords();
                verdict.Foreground = HaveI() && Math.Abs(FromTarget()) <= 1 ? Success : HaveI() ? NotaPalette.Warning : TextTertiary;
            });
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 4 };
            line.Children.Add(big); line.Children.Add(Col(unit, 1)); line.Children.Add(Col(verdict, 2));
            var box = new Border
            {
                Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 4),
                Child = new StackPanel { Children = { new Border { Height = 16, Child = Caps("Integrated") }, line } },
            };
            ToolTip.SetTip(box, "Integrated loudness since the last Reset, gated (BS.1770), against the Target");
            return Stack("Auto,4,Auto,22,8,22,*", box, new Border(),
                MeterGrid(
                    ("LUFS-S", () => Lufs(ScDb(S_LufsS)), () => TextPrimary),
                    ("LUFS-M", () => Lufs(ScDb(S_LufsM)), () => TextPrimary),
                    ("true pk", () => Lvl(ScDb(S_TpHold)), () => OverInk(ScDb(S_TpHold), P(Ceil))),
                    ("max GR", () => GrF(Sc(S_MaxGrHold)), () => TextPrimary)),
                LookRow(), null, ResetRow(true));
        }

        Control DetectorTab()
        {
            // Title · dot + line on the right, a second line under.
            var t = Caps("Detector");
            var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Group, VerticalAlignment = VerticalAlignment.Center };
            var l1 = FxParts.Value();
            var l2 = FxParts.Info(); l2.Height = 16;
            l1.TextTrimming = TextTrimming.CharacterEllipsis;
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Height = 16 };
            var rt = Row(6, dot, l1); rt.HorizontalAlignment = HorizontalAlignment.Right;
            top.Children.Add(t); top.Children.Add(Col(rt, 1));
            var box = new Border
            {
                Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 4), Margin = new Thickness(0, 2, 0, 0),
                Child = new StackPanel { Children = { top, l2 } },
            };
            readouts.Add(() =>
            {
                bool d = On(DeltaP);
                t.Text = d ? "Delta" : "Detector";
                t.Foreground = d ? AccentBright : TextTertiary;
                l1.Text = d ? "hearing what it removes" : (Tp() ? "true peak · 4×" : "sample peak") + (ExtOn() ? " · key" : "");
                l2.Text = d ? "the difference in − out" : NotaNum.F($"{KeyName()} · HP {HpF(P(ScHp))} · latency {Sc(S_Latency):0} smp");
                l1.Foreground = d ? AccentBright : TextPrimary;
                dot.Background = d ? Brass : Sc(S_Gr) > 0.1 ? Success : NotaPalette.TextDisabled;
            });
            ToolTip.SetTip(box, "What the limiter listens to — or, with Delta on, that you are hearing only what it takes away");
            return Stack("20,20,20,6,22,Auto,*",
                LookRow(), LinkRow(),
                SliderRow("SC HP", ScHp, HpF, modulation: true),
                null, ResetRow(false), box, DeltaSwitch());
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[3];
        int rightTab = 0;
        var extras = FxParts.Info();
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"0 … −{(reduction?.Scale ?? 12):0} dB"),
                2 => "window 60 s",
                _ => "window 4 s",
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => ReductionTab(), 2 => LoudnessTab(), _ => LevelTab() };
        // Meters shows the loudness readings while the Loudness tab is up.
        Control RightBody(int t)
        {
            int slot = t == 1 ? 2 : centreTab == 2 ? 1 : 0;
            return rightBodies[slot] ??= slot switch { 2 => DetectorTab(), 1 => LoudMetersTab(), _ => MetersTab() };
        }

        var centre = FxParts.Tabs(new[] { "Level", "Reduction", "Loudness" }, centreHost, CentreBody, t =>
        {
            centreTab = t;
            rightHost.Content = RightBody(rightTab);
            Refresh();
        }, extras);
        var rightFrame = FxParts.Tabs(new[] { "Meters", "Detector" }, rightHost, RightBody, t => { rightTab = t; RefreshAll(); });
        var right = FxParts.Section(rightFrame); right.Width = 202;
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = FxParts.Section(centre); centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { leftCol, right, centreBox } };
        var root = bodyRow;

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            int n = scN >= kScope ? kLvl : 0;
            if (centreTab == 0 && level is not null)
                level.Set(scope, kLvlAt + L_In * kLvl, kLvlAt + L_Out * kLvl, kLvlAt + L_GrMax * kLvl, n, P(Ceil));
            if (centreTab == 1 && reduction is not null)
                reduction.Set(scope, kLvlAt + L_GrMax * kLvl, kLvlAt + L_Clip * kLvl, n, Sc(S_AvgGr));
            if (centreTab == 2 && loudness is not null)
                loudness.Set(scope, kLoudAt, kLoudAt + kLoud, kLoudAt + 2 * kLoud, scN >= kScope ? kLoud : 0, P(Target));
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
