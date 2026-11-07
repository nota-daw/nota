// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Ceiling body (look-ahead limiter, device kind 14), a build of
// the "Nota Ceiling" mockup (700 × 260) on the Beat Repeat / Shutter frame: a centre panel with
// Level / Reduction / Loudness tabs — the input, the part over the ceiling and the output over
// 4 s with the reduction under it (drag the ceiling), the reduction with the transients that
// reached the clip, momentary / short-term / integrated loudness over 60 s around the Target
// (drag it) — each filling its tab, and a right panel with no tabs of its own that follows them:
// Meters (True peak and Character in its title bar, the meter box, Delta, Reset peaks), Detector
// (Auto release and the key source in its title bar, Look, Link, SC HP, the detector box),
// Loudness (the Target presets in its title bar, the integrated box, the meters, Reset) — over
// Gain · Ceiling · Release knobs, which stay (DESIGN.md § Devices › Card layout). The pictures come from the engine (Ceiling.h scopeRead).
// Every control is a device param (raw units), so automation / MIDI learn / presets / A-B /
// persistence come for free; the key source is the device's sidechain routing, the resets are
// device actions.
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
using Nota.Application.Localization;

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
    private static readonly string[] CharWords = { "transparent", Loc.T("device.ceiling.lets_the_attack_through"), Loc.T("device.ceiling.slow_and_dense") };
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
            if (!HaveI()) return Loc.T("device.ceiling.measuring");
            double d = FromTarget();
            return Math.Abs(d) <= 1 ? Loc.T("device.ceiling.on_target") : d > 0 ? NotaNum.F($"{d:0.0}\u2009LU loud") : NotaNum.F($"{-d:0.0}\u2009LU quiet");
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        // `lit` brightens the label and value (the knob that matters now); `arc` recolours the arc.
        Control K(int p, string name, Func<double, string> fmt, string tip, Func<bool>? lit = null, Func<bool>? teal = null, double cellW = 52, double size = 34)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(N(p), 1.0) { Accent = true, Default = DefN(p), Width = size, Height = size };
            knob.Format = v => fmt(FromN(p, v));   // read-out at any value: typing searches it
            knob.ValueChanged += v => { SetN(p, v); val.Text = fmt(P(p)); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            var cell = KnobCell(name, knob, val, cellW);
            TextBlock? label = null;
            if (cell is Panel pl) foreach (var c in pl.Children) if (c is TextBlock tb && tb != val) { label = tb; break; }
            readouts.Add(() =>
            {
                knob.ArcColor = teal?.Invoke() == true ? Teal : null;
                if (!knob.Dragging) { double c = N(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(p));
                if (lit is null) return;
                bool on = lit();
                val.Foreground = on ? TextPrimary : TextSecondary;
                if (label is not null) label.Foreground = on ? TextSecondary : TextTertiary;
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
            var tb = new TextBlock { Text = text, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSubtle : fill ? Raised : Brushes.Transparent;
                b.BorderBrush = on ? Brass : fill ? Brushes.Transparent : NotaPalette.BorderStrong;
                tb.Foreground = on ? AccentBright : fill ? TextPrimary : TextSecondary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            if (p >= 0) Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool modulation = false, Func<bool>? lit = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => N(p), v => { SetN(p, v); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: 38, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            if (lit is not null)
                readouts.Add(() =>
                {
                    bool on = lit();
                    foreach (var c in bar.Children) if (c is TextBlock tb && tb.TextAlignment == TextAlignment.Right) tb.Foreground = on ? TextPrimary : TextSecondary;
                });
            return g;
        }

        // A dropdown: [label  value ▾] opens `items`; the current one is dotted.
        // sources: key-source rows (specs/050) after the items, unavailable ones disabled.
        Border Drop(string caps, Func<string> text, Func<bool> hot, Func<IEnumerable<(string Label, bool Current, Action Pick)>> items, string tip, int learnParam = -1,
            Func<IEnumerable<(string Label, bool Checked, Action? Pick, string? Tip)>>? sources = null)
        {
            var name = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Margin = new Thickness(6, 0, 0, 0) };
            var inner = new DockPanel { Children = { Docked(chev, Dock.Right) } };
            if (caps.Length > 0) inner.Children.Add(Docked(new Border { Padding = new Thickness(0, 0, 6, 0), Child = Caps(caps) }, Dock.Left));
            inner.Children.Add(name);
            var box = new Border
            {
                Height = 16, Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = inner,
            };
            readouts.Add(() =>
            {
                bool h = hot();
                name.Text = text();
                name.Foreground = TextPrimary;   // emphasis, not state: a stronger frame
                box.BorderBrush = h ? NotaPalette.BorderStrong : BorderDef;
                chev.Foreground = h ? TextSecondary : TextTertiary;
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
                if (sources != null)
                    foreach (var row in sources()) fly.Items.Add(SidechainSourcePicker.MenuItemFor(row, RefreshAll));
                fly.ShowAt(box);
                e.Handled = true;
            };
            return box;
        }

        // Key source: Internal, another track, or one pad / chain / voice of it (the device's
        // sidechain routing, specs/050).
        int Src() => engine.DeviceSidechainSource(track, di);
        bool ExtOn() => Src() >= 0;
        string KeyName() => SidechainSourcePicker.Name(engine, engine.DeviceSidechainKey(track, di));
        Control KeyDrop() => Drop("SC", KeyName, ExtOn, () =>
        {
            return new List<(string, bool, Action)>
            {
                (Loc.T("device.ceiling.internal_this_track"), !ExtOn(), () => { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); }),
            };
        }, Loc.T("device.ceiling.key_source_internal_limits_on"),
            sources: () => SidechainSourcePicker.MenuRows(engine, track, di, ctx.NotifyChanged));

        Control TargetDrop() => Drop("", () => TargetF(P(Target)), () => false, () =>
        {
            var list = new List<(string, bool, Action)>();
            foreach (var (label, v) in Targets) { float vv = v; list.Add((label, Math.Abs(P(Target) - vv) < 0.05f, () => SetP(Target, vv))); }
            return list;
        }, Loc.T("device.ceiling.target_the_loudness_the_meters"), Target);

        Control CharSeg()
        {
            var seg = Segments(CharNames, CharI, i => { SetP(Character, i); RefreshAll(); }, out var sync, padX: 6);
            readouts.Add(sync);
            Learn(seg, Character);
            ToolTip.SetTip(seg, Loc.T("device.ceiling.character_clean_transparent_the_fastest"));
            return seg;
        }
        // A read-out line in a panel (ink 2, trims).
        Control Info(Func<string> text, Func<IBrush>? ink = null)
        {
            var t = Mono("", 8, TextSecondary);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { t.Text = text(); t.Foreground = ink?.Invoke() ?? TextSecondary; });
            return t;
        }

        // ======================================================================
        // Layout pieces (DESIGN.md § Devices › Card layout: graph, tabs and output)
        // ======================================================================
        static Control View(Control graph) => new Border { Margin = new Thickness(6, 6, 6, 5), Child = graph };
        static Control Head(Control left, Control? right = null)
        {
            var d = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center };
            if (right != null) d.Children.Add(Docked(right, Dock.Right));
            d.Children.Add(Docked(left, Dock.Left));
            return d;
        }
        static Control Rows(params Control[] rows)
        {
            var grid = new Grid { RowSpacing = 2 };
            for (int r = 0; r < rows.Length; r++)
            {
                bool fixedH = rows[r] is Border { Height: 1 } || rows[r] is Border { Background: not null };   // rules and read-out boxes keep their height
                grid.RowDefinitions.Add(new RowDefinition(fixedH ? GridLength.Auto : GridLength.Star));
                grid.Children.Add(GRow(rows[r], r));
            }
            return new Border { Padding = new Thickness(8, 5, 8, 5), Child = grid };
        }
        static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 1) };

        void WireDrag<T>(T v, int param, string name, Func<double, string> fmt) where T : ShDragView
        {
            // ⌥-click the handle to type it (specs/035).
            ValueEntry.Attach(v, new ValueField(name, () => N(param), n => fmt(FromN(param, n)), n => SetN(param, n),
                () => Begin(param), () => End(param), DefN(param)) { After = RefreshAll });
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
            return CharI() == 1 ? Loc.Plural("device.ceiling.attack_passed_to_the_clip", n) : Loc.Plural("device.ceiling.reached_the_clip_times", n);
        }

        // ======================================================================
        // CENTRE — Level / Reduction / Loudness: each graph fills its tab (the Level graph drags
        // the ceiling, the Loudness graph the target).
        // ======================================================================
        int centreTab = 0;
        CeLevelView? level = null;
        Control LevelTab()
        {
            level = new CeLevelView { CeilToNorm = db => ToN(Ceil, db) };
            WireDrag(level, Ceil, "Ceiling", CeilF);
            ToolTip.SetTip(level, MenuKit.Keys(Loc.T("device.ceiling.the_last_4_s_the")));
            return View(level);
        }

        CeReductionView? reduction = null;
        Control ReductionTab()
        {
            reduction = new CeReductionView();
            ToolTip.SetTip(reduction, Loc.T("device.ceiling.the_gain_reduction_over_the"));
            return View(reduction);
        }

        CeLoudnessView? loudness = null;
        Control LoudnessTab()
        {
            loudness = new CeLoudnessView { TargetToNorm = t => ToN(Target, t) };
            WireDrag(loudness, Target, "Target", TargetF);
            ToolTip.SetTip(loudness, MenuKit.Keys(Loc.T("device.ceiling.momentary_teal_short_term_neutral")));
            return View(loudness);
        }

        // ======================================================================
        // RIGHT — follows the centre tab: Meters (Level) · Detector (Reduction) · Loudness (Loudness).
        // The read-out boxes are what the limiter measures; they stay where the view they explain is.
        // ======================================================================
        Control MeterGrid(params (string Key, Func<string> Val, Func<IBrush> Ink)[] cells)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto"), ColumnSpacing = 6, RowSpacing = 3 };
            for (int i = 0; i < (cells.Length + 1) / 2; i++) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < cells.Length; i++)
            {
                var (key, val, ink) = cells[i];
                int r = i / 2, c = (i % 2) * 2;
                var k = new TextBlock { Text = key, FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(c > 0 ? 4 : 0, 0, 0, 0) };
                var v = Mono("", 8, TextPrimary); v.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => { v.Text = val(); v.Foreground = ink(); });
                g.Children.Add(GRow(Col(k, c), r));
                g.Children.Add(GRow(Col(v, c + 1), r));
            }
            return new Border { Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell, Padding = new Thickness(6, 5), Child = g };
        }
        IBrush OverInk(double db, double limit) => db > limit + 0.05 ? NotaPalette.DangerBright : TextPrimary;
        Border ResetChip(bool loud)
        {
            var reset = Chip(-1, () => false, () => engine.DeviceAction(track, di, loud ? A_ResetLoudness : A_ResetPeaks, 0, 0),
                loud ? "Reset" : "Reset peaks", loud ? Loc.T("device.ceiling.reset_start_the_integrated_loudness") : Loc.T("device.ceiling.reset_peaks_clear_the_peak"), fill: true);
            reset.HorizontalAlignment = HorizontalAlignment.Right;
            return reset;
        }

        (Control, Control) MetersPanel() => (
            Head(Toggle(TruePeak, "True peak", Loc.T("device.ceiling.true_peak_the_detector_reads")), CharSeg()),
            Rows(
                MeterGrid(
                    ("peak in", () => Lvl(ScDb(S_PeakInHold)), () => OverInk(ScDb(S_PeakInHold), 0)),
                    ("peak out", () => Lvl(ScDb(S_PeakOutHold)), () => TextPrimary),
                    ("max GR", () => GrF(Sc(S_MaxGrHold)), () => TextPrimary),
                    ("LUFS-S", () => Lufs(ScDb(S_LufsS)), () => TextPrimary),
                    ("LUFS-I", () => Lufs(ScDb(S_LufsI)), () => TextPrimary),
                    ("true pk", () => Lvl(ScDb(S_TpHold)), () => OverInk(ScDb(S_TpHold), P(Ceil)))),
                Info(() => CharWords[CharI()] + " · " + Loc.F("device.ceiling.over_of_the_time", Sc(S_OverPct) * 100)),
                Rule(),
                Toggle(DeltaP, Loc.T("device.ceiling.delta_hear_what_it_removes"), Loc.T("device.ceiling.delta_play_the_difference_between")),
                ResetChip(false)));

        (Control, Control) DetectorPanel()
        {
            var t = Caps("DETECTOR");
            var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Group, VerticalAlignment = VerticalAlignment.Center };
            var l1 = Mono("", 9, TextPrimary);
            var l2 = Mono("", 8, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var box = new Border
            {
                Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = { t, Row(5, dot, l1), l2 } },
            };
            readouts.Add(() =>
            {
                bool d = On(DeltaP);
                t.Text = d ? "Delta" : "Detector";
                t.Foreground = d ? AccentBright : TextTertiary;
                l1.Text = d ? Loc.T("device.ceiling.hearing_what_it_removes") : (Tp() ? Loc.T("device.ceiling.true_peak_4") : "sample peak") + (ExtOn() ? " · key" : "");
                l2.Text = d ? Loc.T("device.ceiling.the_difference_in_out") : NotaNum.F($"{KeyName()} · HP {HpF(P(ScHp))} · latency {Sc(S_Latency):0} smp");
                l1.Foreground = d ? AccentBright : TextPrimary;
                dot.Background = d ? Brass : Sc(S_Gr) > 0.1 ? Success : NotaPalette.BorderStrong;
                box.BorderBrush = d ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
            });
            ToolTip.SetTip(box, Loc.T("device.ceiling.what_the_limiter_listens_to"));
            return (Head(Toggle(AutoRelease, Loc.T("device.ceiling.auto_release"), Loc.T("device.ceiling.auto_release_the_release_slows")), KeyDrop()),
                Rows(
                    SliderRow("LOOK", Lookahead, LookF),
                    SliderRow("LINK", StereoLink, LinkF),
                    SliderRow("SC HP", ScHp, HpF, modulation: true, lit: () => P(ScHp) > 20.5f),
                    Rule(),
                    box));
        }

        (Control, Control) LoudnessPanel()
        {
            var big = Mono("", 16, TextPrimary);
            var unit = new TextBlock { Text = "LUFS", FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) };
            var verdict = Mono("", 8, Success); verdict.HorizontalAlignment = HorizontalAlignment.Right; verdict.VerticalAlignment = VerticalAlignment.Bottom;
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
                Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 1, Children = { Caps("INTEGRATED"), line } },
            };
            ToolTip.SetTip(box, Loc.T("device.ceiling.integrated_loudness_since_the_last"));
            var title = new TextBlock { Text = "Target", FontSize = 9, FontWeight = FontWeight.Medium, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
            return (Head(title, TargetDrop()),
                Rows(
                    box,
                    MeterGrid(
                        ("LUFS-S", () => Lufs(ScDb(S_LufsS)), () => TextPrimary),
                        ("LUFS-M", () => Lufs(ScDb(S_LufsM)), () => TextPrimary),
                        ("true pk", () => Lvl(ScDb(S_TpHold)), () => OverInk(ScDb(S_TpHold), P(Ceil))),
                        ("max GR", () => GrF(Sc(S_MaxGrHold)), () => TextPrimary)),
                    ResetChip(true)));
        }

        // Under every panel: Gain (into the limiter) · Ceiling (the main knob) · Release. Ceiling has
        // no dry/wet, so the ceiling takes the middle.
        Control OutputStrip()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(-4, 5, -4, 4) };
            Control Cell(Control c, int col) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Bottom; return Col(c, col); }
            g.Children.Add(Cell(K(Gain, "GAIN", GainF, Loc.T("device.ceiling.gain_how_hard_the_signal"), cellW: 54), 0));
            g.Children.Add(Cell(K(Ceil, "CEILING", v => NotaNum.F($"{v:0.0} dB"), Loc.T("device.ceiling.ceiling_the_output_never_goes"), cellW: 58, size: 44), 1));
            g.Children.Add(Cell(K(Release, "RELEASE", RelF, Loc.T("device.ceiling.release_how_fast_the_gain"), teal: () => On(AutoRelease), cellW: 54), 2));
            return new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(8, 0), Child = g };
        }

        // ======================================================================
        // Tab frame + the panel that follows it
        // ======================================================================
        var centreHost = new ContentControl();
        var panelHost = new ContentControl();
        var headHost = new ContentControl();
        var centreBodies = new Control?[3];
        var panels = new (Control Head, Control Body)?[3];
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => ReleaseRange() + " · " + ClipText(),
                2 => NotaNum.F($"LRA {Sc(S_Lra):0.0} LU · PLR {(HaveI() ? NotaNum.F($"{Sc(S_Plr):0.0}") : "—")} · ")
                     + (HaveI() ? NotaNum.F($"{FromTarget():+0.0;−0.0;0.0} LU from target") : Loc.T("device.ceiling.measuring")),
                _ => (ScDb(S_WinInPeak) <= -119 ? Loc.T("common.no_signal") : NotaNum.F($"in {Lvl(ScDb(S_WinInPeak))} → out {Lvl(ScDb(S_WinOutPeak))}")) + " · window 4 s",
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => ReductionTab(), 2 => LoudnessTab(), _ => LevelTab() };
        void ShowPanel(int t)
        {
            var (head, body) = panels[t] ??= t switch { 1 => DetectorPanel(), 2 => LoudnessPanel(), _ => MetersPanel() };
            headHost.Content = head;
            panelHost.Content = body;
            RefreshAll();
        }

        var centre = TabFrame(new[] { "Level", "Reduction", "Loudness" }, centreHost, CentreBody, false, t => { centreTab = t; ShowPanel(t); Refresh(); }, extras);
        ShowPanel(centreTab);
        var headBar = new Border { Height = 20, Padding = new Thickness(8, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = headHost };
        var rightCol = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), Docked(OutputStrip(), Dock.Bottom), panelHost } };
        var right = new RuleSection { Width = 186, Child = rightCol };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new RuleSection { Margin = new Thickness(0, 0, 5, 0), Child = centre };

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, centreBox } };
        var root = new Border { Background = NotaPalette.SurfaceCard, Child = bodyRow };

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
                    tb.Foreground = on ? NotaPalette.TextPrimary : TextTertiary;
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
