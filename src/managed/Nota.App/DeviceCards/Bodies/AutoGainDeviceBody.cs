// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Level body (loudness leveler, device kind 18), a build of the
// "Nota Level" mockup, laid out the #531 way (CardLayout; DESIGN.md § Devices › Card layout):
// a centre panel with Loudness / Meters tabs — 8 s of loudness (input, output and the target
// with its ±1 LU band; drag the line for Target) with the Target knob beside it and the Scale
// (Mom / Short / Integ) and the Target's standards or a reference track (the device's
// sidechain) under it; IN / OUT / TP / COR meters — and a right panel with no tabs of its own
// that follows them: Correction (Manual / Auto in its title bar, a fader around 0 dB that Auto
// rides, the gain applied in large type with why it is held, Match / Reset), Response (Fast /
// Slow in its title bar, Window, Max gain, True-peak safe with its ceiling) — over the Trim knob.
// S (260) is the loudness over Manual / Auto and Target, Trim, Window and Max gain. Every control is a
// device param (normalized 0..1), so automation / MIDI learn / presets / A-B / persistence come
// for free; double-click resets. FullBleed — the shared shell draws the header (name · preset ·
// badge · bypass).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using static Nota.App.LevelMath;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class AutoGainDeviceBody : IDeviceBody
{
    // Target standards offered by the Target value's menu.
    private static readonly (string Label, double Lufs)[] Standards =
    {
        ("−9\u2009LUFS · club", -9), ("−11\u2009LUFS · loud master", -11), ("−14\u2009LUFS · streaming", -14), ("−16\u2009LUFS · podcast, Apple", -16),
        ("−18\u2009LUFS · audiobook", -18), ("−23\u2009LUFS · EBU R128", -23), ("−24\u2009LUFS · ATSC A/85", -24), ("−27\u2009LUFS · cinema dialogue", -27),
    };

    public double Width => 700;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
    public bool FullBleed => true;
    public string? Subtitle => "Leveler";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        int pc = engine.DeviceParamCount(track, di);
        float P(int p) => p < pc ? engine.DeviceGetParam(track, di, p) : 0.5f;
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) { if (p < pc) engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1)); }
        void SetP(int p, double v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        double Def(int p) => engine.DeviceParamDefault(track, di, p);
        bool On(int p) => P(p) >= 0.5f;
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var sc = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? sc[i] : 0;
        bool Active() => !engine.DeviceBypassed(track, di);
        bool Manual() => !On(Auto);
        bool Primed() => Sc(S_Primed) > 0.5;
        double MaxG() => MaxGainDb(P(MaxGain));

        // ---- reference track (the device's sidechain) ---------------------------------------
        // The reference may be one pad / chain / voice of a track (specs/050).
        string RefName() => SidechainSourcePicker.Name(engine, engine.DeviceSidechainKey(track, di));
        int Ref() => engine.DeviceSidechainSource(track, di);
        bool HasRef() => Ref() >= 0;

        // ---- units ---------------------------------------------------------------------------
        static string TargetF(double v) => Lufs(TargetLufs(v)) + "\u2009LUFS";
        static string TrimF(double v) => Sgn(TrimDb(v)) + "\u2009dB";
        static string WindowF(double v) => NotaNum.F($"{WindowS(v):0.0}\u2009s");
        static string MaxF(double v) => NotaNum.F($"{MaxGainDb(v):0}\u2009dB");

        // ---- small builders ------------------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Measure"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

        // Slider row: label · 3px track · mono 8 value (primary ink once off its default).
        // `valueHost` replaces the plain value (the Target's menu); `dim` greys the track.
        Control Slider(string label, int p, Func<double, string> fmt, string tip, double labelW, double valueW,
            bool bipolar = false, Func<bool>? dim = null, Func<(string Text, IBrush? Ink)>? valueOverride = null, Action<Control>? valueClick = null)
        {
            var lbl = Caps(label);
            lbl.TextTrimming = TextTrimming.None;
            var trk = new SliderTrack { Bipolar = bipolar, Reset = () => { Reset(p); RefreshAll(); }, VerticalAlignment = VerticalAlignment.Center };
            trk.Changed += v => { Raw(p, v); RefreshAll(); };
            trk.GestureBegin += () => Begin(p);
            trk.GestureEnd += () => End(p);
            var val = Mono("", 8, TextPrimary);
            val.TextAlignment = TextAlignment.Right;
            val.HorizontalAlignment = HorizontalAlignment.Right;
            Control valCtl = val;
            if (valueClick is null)
                ValueEntry.Attach(val, () => P(p), v => Raw(p, v), () => fmt(P(p)), () => Begin(p), () => End(p), RefreshAll, control: trk);
            else
            {
                var box = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = val };
                box.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                    valueClick(box); e.Handled = true;
                };
                valCtl = box;
            }
            var g = new Grid { ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(labelW)));
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(valueW)));
            g.Children.Add(lbl); g.Children.Add(Col(trk, 1)); g.Children.Add(Col(valCtl, 2));
            readouts.Add(() =>
            {
                if (!trk.Dragging) trk.Norm = P(p);
                bool d = dim?.Invoke() ?? false;
                trk.IsDim = d;
                var ov = valueOverride?.Invoke();
                if (ov is { } o && o.Text.Length > 0) { val.Text = o.Text; val.Foreground = o.Ink ?? TextPrimary; }
                else
                {
                    val.Text = fmt(P(p));
                    val.Foreground = d ? TextTertiary : Math.Abs(P(p) - Def(p)) > 0.003 ? TextPrimary : TextSecondary;
                }
            });
            Learn(g, p);
            ToolTip.SetTip(g, tip);
            return g;
        }

        Border Seg(string[] names, Func<int> cur, Action<int> pick, int p, string tip, double width)
        {
            var s = Segments(names, cur, i => { pick(i); RefreshAll(); }, out var sync, fill: true, padX: 2);
            s.Width = width;
            readouts.Add(sync);
            Learn(s, p);
            ToolTip.SetTip(s, tip);
            return s;
        }

        // A dropdown menu under a control.
        // Key-source rows (specs/050) go after the others, unavailable ones disabled.
        void Menu(Control at, IEnumerable<(string Label, bool Current, Action Pick)?> items,
            IEnumerable<(string Label, bool Checked, Action? Pick, string? Tip)>? sources = null)
        {
            var fly = new MenuFlyout();
            foreach (var it in items)
            {
                if (it is not { } x) { fly.Items.Add(new Separator()); continue; }
                var mi = new MenuItem { Header = x.Label };
                mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = x.Current;
                var pk = x.Pick;
                mi.Click += (_, _) => { pk(); RefreshAll(); };
                fly.Items.Add(mi);
            }
            foreach (var row in sources ?? Array.Empty<(string, bool, Action?, string?)>())
                fly.Items.Add(SidechainSourcePicker.MenuItemFor(row, RefreshAll));
            fly.ShowAt(at);
        }

        void TargetMenu(Control at)
        {
            var list = new List<(string, bool, Action)?>();
            foreach (var (label, lufs) in Standards)
            {
                double v = TargetNorm(lufs);
                list.Add((label, !HasRef() && Math.Abs(TargetLufs(P(Target)) - lufs) < 0.05, () =>
                {
                    if (HasRef()) { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); }
                    SetP(Target, v);
                }));
            }
            list.Add(null);
            list.Add((Loc.T("device.auto_gain.fixed_target_no_reference"), !HasRef(), () => { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); }));
            Menu(at, list, SidechainSourcePicker.MenuRows(engine, track, di, ctx.NotifyChanged, "Match track: "));
        }

        // Gauge knob over a normalized param (automation gesture, MIDI learn, live follow).
        Control K(int p, string name, Func<double, string> fmt, string tip, double cellW = 50, double size = 34, bool bipolar = false,
            Func<bool>? dim = null, Func<string?>? valueOverride = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Bipolar = bipolar, Default = Def(p), Width = size, Height = size };
            knob.Format = fmt;
            knob.ValueChanged += v => { Raw(p, v); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            readouts.Add(() =>
            {
                if (dim is not null) knob.IsDim = dim();
                if (!knob.Dragging) { double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = valueOverride?.Invoke() ?? fmt(P(p));
            });
            return KnobCell(name, knob, val, cellW);
        }
        // The Target's standards and reference tracks: [value ▾].
        Control TargetDrop()
        {
            var name = new TextBlock { FontSize = 8, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Margin = new Thickness(6, 0, 0, 0), Foreground = TextTertiary };
            var box = new Border
            {
                Height = 16, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(chev, Dock.Right), name } },
            };
            readouts.Add(() => name.Text = HasRef() ? "REF " + RefName() : TargetF(P(Target)));
            box.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return; TargetMenu(box); e.Handled = true; };
            ToolTip.SetTip(box, Loc.T("device.auto_gain.target_loudness_36_0_lufs"));
            Learn(box, Target);
            return box;
        }
        int centreTab = 0;
        var extras = Mono("", 7, TextTertiary);

        // ======================================================================
        // Correction (the right panel on Loudness)
        // ======================================================================
        var modeSeg = Seg(new[] { "Manual", "Auto" }, () => Manual() ? 0 : 1, i =>
        {
            if (i == 0 && !Manual())
            {
                // Hand over: Manual starts from the gain Auto was riding.
                double g = Math.Round(Math.Clamp(Sc(S_Level), -MaxG(), MaxG()) * 10) / 10;
                SetP(Gain, GainNorm(g));
                SetP(Auto, 0);
            }
            else if (i == 1) SetP(Auto, 1);
        }, Auto, Loc.T("device.auto_gain.auto_rides_the_gain_to"), 84);

        var fader = new LvGainFader { Value = () => GainDb(P(Gain)), VerticalAlignment = VerticalAlignment.Stretch };
        fader.GestureBegin += () => Begin(Gain);
        fader.GestureEnd += () => End(Gain);
        fader.Changed += db => { Raw(Gain, GainNorm(db)); RefreshAll(); };
        fader.ResetRequested += () => { Reset(Gain); RefreshAll(); };
        Learn(fader, Gain);
        ToolTip.SetTip(fader, MenuKit.Keys(Loc.T("device.auto_gain.the_correction_around_0_db")));
        ValueEntry.Attach(fader, ValueEntry.DeviceParam(engine, track, di, Gain, "Gain", v => Sgn(GainDb(v)) + "\u2009dB", RefreshAll));

        var maxTop = Mono("", 7, TextDisabled);
        var modeTag = Mono("", 7, TextDisabled);
        var maxBot = Mono("", 7, TextDisabled);
        var big = Mono("", 26, TextPrimary); big.FontWeight = FontWeight.Bold; big.LineHeight = 28; big.LetterSpacing = -0.5;
        var bigSub = new TextBlock { Text = "dB applied", FontSize = 8, Foreground = TextSecondary };
        var reason = Mono("", 7, TextTertiary); reason.Margin = new Thickness(0, 2, 0, 0);
        var matchTxt = new TextBlock { FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary };
        var match = new Border
        {
            Background = Raised, CornerRadius = NotaRadius.Cell, Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Bottom, Child = matchTxt,
        };
        match.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(match).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (Manual())
            {
                if (!Primed()) return;
                double want = Math.Clamp(Sc(S_Target) - Sc(S_Measured) - TrimDb(P(Trim)), -MaxG(), MaxG());
                SetP(Gain, GainNorm(Math.Round(want * 10) / 10));
            }
            else engine.DeviceAction(track, di, 0, 0, 0);
            RefreshAll();
        };
        readouts.Add(() =>
        {
            matchTxt.Text = Manual() ? "Match" : "Reset";
            ToolTip.SetTip(match, Manual()
                ? Loc.T("device.auto_gain.match_set_the_manual_gain")
                : Loc.T("device.auto_gain.reset_restart_the_measurement_from"));
        });

        var topLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        topLine.Children.Add(maxTop); topLine.Children.Add(Col(modeTag, 1));
        var botLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Bottom };
        botLine.Children.Add(maxBot); botLine.Children.Add(Col(match, 1));
        maxBot.VerticalAlignment = VerticalAlignment.Bottom;
        var numCol = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto") };
        numCol.Children.Add(GRow(topLine, 0));
        numCol.Children.Add(GRow(new StackPanel { Spacing = 1, Children = { big, bigSub, reason } }, 2));
        numCol.Children.Add(GRow(botLine, 4));
        var gainArea = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        gainArea.Children.Add(fader); gainArea.Children.Add(Col(numCol, 1));

        // ======================================================================
        // Loudness (the centre view)
        // ======================================================================
        var scaleSeg = Seg(ScaleNames, () => ScaleIndex(P(Scale)), i => SetP(Scale, i / 2.0), Scale,
            Loc.T("device.auto_gain.scale_what_the_correction_reads"), 118);
        var hist = new LvHistoryView { Value = () => P(Target) };
        hist.GestureBegin += () => Begin(Target);
        hist.GestureEnd += () => End(Target);
        hist.Changed += v => { Raw(Target, v); RefreshAll(); };
        hist.ResetRequested += () => { Reset(Target); RefreshAll(); };
        Learn(hist, Target);
        ToolTip.SetTip(hist, MenuKit.Keys(Loc.T("device.auto_gain.the_last_8_s_input")));
        ValueEntry.Attach(hist, ValueEntry.DeviceParam(engine, track, di, Target, "Target", TargetF, RefreshAll));

        // ======================================================================
        // Meters (the second view) and Response (its panel)
        // ======================================================================

        (Control Row, LvMeter M, TextBlock V) MeterRow(string label, string tip)
        {
            var m = new LvMeter { VerticalAlignment = VerticalAlignment.Center };
            var v = Mono("", 8, TextPrimary); v.TextAlignment = TextAlignment.Right; v.HorizontalAlignment = HorizontalAlignment.Right;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,34"), ColumnSpacing = 6, Background = Brushes.Transparent };
            var l = Caps(label); if (label == "TP") l.Text = label;   // true peak: an acronym Label would lowercase
            g.Children.Add(l); g.Children.Add(Col(m, 1)); g.Children.Add(Col(v, 2));
            ToolTip.SetTip(g, tip);
            return (g, m, v);
        }
        var mIn = MeterRow("IN", Loc.T("device.auto_gain.input_loudness_on_the_chosen"));
        var mOut = MeterRow("OUT", Loc.T("device.auto_gain.output_loudness_on_the_chosen"));
        var mTp = MeterRow("TP", Loc.T("device.auto_gain.output_true_peak_dbtp_the"));
        var mCor = MeterRow("COR", Loc.T("device.auto_gain.the_correction_applied_max_gain"));

        var respSeg = Seg(new[] { "Fast", "Slow" }, () => On(Response) ? 1 : 0, i => SetP(Response, i), Response,
            Loc.T("device.auto_gain.response_slow_glides_over_the"), 76);
        var winRow = Slider("WINDOW", WindowP, WindowF, Loc.T("device.auto_gain.window_how_long_the_ride"), 48, 36);
        var maxRow = Slider("MAX GAIN", MaxGain, MaxF, Loc.T("device.auto_gain.max_gain_the_most_the"), 48, 36);

        var safeSw = Switch("True-peak safe", () => On(Safe), () => { SetP(Safe, On(Safe) ? 0 : 1); RefreshAll(); }, out var safeSync);
        readouts.Add(safeSync);
        Learn(safeSw, Safe);
        ToolTip.SetTip(safeSw, Loc.T("device.auto_gain.true_peak_safe_a_look"));
        var ceilTxt = Mono("", 8, TextPrimary);
        var ceilBox = new Border
        {
            Background = Sunken, CornerRadius = NotaRadius.Field, Padding = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = ceilTxt,
        };
        bool cd = false; double cy0 = 0, cv0 = 0;
        ceilBox.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(ceilBox).Properties.IsLeftButtonPressed) return;
            if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { Reset(Ceiling); RefreshAll(); e.Handled = true; return; }
            cd = true; cy0 = e.GetPosition(ceilBox).Y; cv0 = P(Ceiling); Begin(Ceiling); e.Pointer.Capture(ceilBox); e.Handled = true;
        };
        ceilBox.PointerMoved += (_, e) =>
        {
            if (!cd) return;
            bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            double v = cv0 + (cy0 - e.GetPosition(ceilBox).Y) / 140.0 * (fine ? 0.1 : 1);
            Raw(Ceiling, Math.Round(Math.Clamp(v, 0, 1) * 60) / 60);   // 0.1 dB steps
            RefreshAll();
        };
        ceilBox.PointerReleased += (_, e) => { if (cd) { cd = false; End(Ceiling); e.Pointer.Capture(null); } };
        ceilBox.PointerCaptureLost += (_, _) => { if (cd) { cd = false; End(Ceiling); } };
        Learn(ceilBox, Ceiling);
        ToolTip.SetTip(ceilBox, MenuKit.Keys(Loc.T("device.auto_gain.ceiling_6_0_dbtp_drag")));
        ValueEntry.Attach(ceilBox, ValueEntry.DeviceParam(engine, track, di, Ceiling, "Ceiling", v => NotaNum.F($"{CeilingDb(v):0.0;−0.0;0.0}\u2009dBTP"), RefreshAll), ceilTxt);
        readouts.Add(() =>
        {
            ceilTxt.Text = NotaNum.F($"{CeilingDb(P(Ceiling)):0.0;−0.0;0.0}\u2009dBTP");
            ceilTxt.Foreground = !On(Safe) ? TextDisabled : Math.Abs(P(Ceiling) - Def(Ceiling)) > 0.003 ? TextPrimary : TextSecondary;
        });
        var safeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        safeRow.Children.Add(safeSw); safeRow.Children.Add(Col(ceilBox, 1));

        // ---- live state ---------------------------------------------------------------------
        static double MeterX(double lufs) => Math.Clamp((lufs - LevelMath.LBot) / (LevelMath.LTop - LevelMath.LBot), 0, 1);
        readouts.Add(() =>
        {
            bool on = Active(), manual = Manual(), primed = Primed(), silent = Sc(S_Silent) > 0.5;
            double maxG = MaxG(), trim = TrimDb(P(Trim));
            double level = Sc(S_Level), lim = Sc(S_LimGr), g = level - lim;   // the correction as heard (no trim)
            double target = Sc(S_Target), measured = Sc(S_Measured), outMeas = Sc(S_OutMeasured), tp = Sc(S_TruePeak);
            double delta = Sc(S_Delta);
            bool locked = on && primed && Math.Abs(delta) < 1;
            bool tpOver = tp > CeilingDb(P(Ceiling)) + 0.05 && On(Safe);
            int clamp = (int)Math.Round(Sc(S_Clamp));
            bool tpClamp = on && On(Safe) && Sc(S_LimHold) > 0.3;
            if (!primed) target = HasRef() ? target : TargetLufs(P(Target));

            // Correction column.
            fader.Set(!on ? 0 : manual ? Math.Clamp(GainDb(P(Gain)), -maxG, maxG) : g, maxG, manual, on);
            maxTop.Text = NotaNum.F($"+{maxG:0}");
            maxBot.Text = NotaNum.F($"−{maxG:0}");
            modeTag.Text = manual ? "fader" : "auto";
            big.Text = on ? Sgn(g) : "off";
            big.Foreground = on ? TextPrimary : TextDisabled;
            string why = !on ? "" : !primed ? Loc.T("device.auto_gain.waiting_for_signal") : silent ? "silence · holding"
                : tpClamp ? Loc.T("device.auto_gain.limited_by_true_peak") : clamp == 1 && !manual ? Loc.T("device.auto_gain.limited_by_max_gain") : clamp == 2 && !manual ? Loc.T("device.auto_gain.limited_by_max_cut")
                : manual ? "manual" : "auto · " + (On(Response) ? "slow" : "fast");
            reason.Text = why;
            reason.Foreground = on && (tpClamp || (clamp > 0 && !manual)) ? NotaPalette.Warning : TextTertiary;

            // Graph, and its read-out in the tab bar.
            string tl = HasRef() ? "Ref " + Lufs(target) : Lufs(target) + "\u2009LUFS";
            hist.SetState(on, locked, !on ? "bypass" : !primed ? "waiting" : silent ? "holding" : locked ? "locked ±1\u2009LU" : "tracking", tl, !HasRef());
            int si = ScaleIndex(P(Scale));
            extras.Text = centreTab == 1 ? ScaleLong[si]
                : NotaNum.F($"In {Lufs(measured)} · Out {Lufs(outMeas)} · Δ {(primed ? Sgn(delta) : "—")}\u2009LU · TP {Lufs(tp)}\u2009dB");
            extras.Foreground = tpOver ? Danger : TextTertiary;

            // Meters.
            double tx = MeterX(target);
            mIn.M.Set(0, MeterX(measured), TextTertiary, tx); mIn.V.Text = Lufs(measured);
            mOut.M.Set(0, MeterX(outMeas), on ? NotaPalette.VizInk : TextTertiary, tx); mOut.V.Text = Lufs(outMeas);
            mOut.V.Foreground = TextPrimary;
            mTp.M.Set(0, MeterX(tp), tpOver ? Danger : TextSecondary, On(Safe) ? MeterX(CeilingDb(P(Ceiling))) : double.NaN);
            mTp.V.Text = Lufs(tp); mTp.V.Foreground = tpOver ? Danger : TextPrimary;
            double cf = Math.Clamp(g / Math.Max(0.01, maxG), -1, 1) / 2;
            mCor.M.Set(0.5, 0.5 + cf, Teal, 0.5); mCor.V.Text = Sgn(on ? g : 0);
        });

        // ======================================================================
        // Frames: L (Loudness / Meters, the panel that follows them, Trim under it) · S (the
        // loudness over Manual / Auto and Target · Trim · Window · Max gain)
        // ======================================================================
        Control TargetKnob(double cellW) => K(Target, "TARGET", TargetF, Loc.T("device.auto_gain.target_loudness_36_0_lufs"), cellW,
            dim: HasRef, valueOverride: () => HasRef() ? "REF " + RefName() : null);
        Control LoudnessTab() => CardLayout.View(hist, CardLayout.KnobColumn(TargetKnob(50)), CardLayout.Line(8, scaleSeg, TargetDrop()));
        Control MetersTab()
        {
            var g = new Grid { RowDefinitions = new RowDefinitions("*,*,*,*"), Margin = new Thickness(14, 10) };
            g.Children.Add(GRow(mIn.Row, 0)); g.Children.Add(GRow(mOut.Row, 1)); g.Children.Add(GRow(mTp.Row, 2)); g.Children.Add(GRow(mCor.Row, 3));
            foreach (var c in g.Children) c.VerticalAlignment = VerticalAlignment.Center;
            return g;
        }
        (Control, Control) CorrectionPanel() => (CardLayout.Head(CardLayout.Title(Loc.T("device.auto_gain.correction")), modeSeg),
            new Border { Padding = new Thickness(8, 6), Child = gainArea });
        (Control, Control) ResponsePanel() => (CardLayout.Head(CardLayout.Title(Loc.T("device.auto_gain.response")), respSeg),
            CardLayout.Rows(winRow, maxRow, CardLayout.Rule(), safeRow));
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(hist, modeSeg,
                TargetKnob(56), K(Trim, "TRIM", TrimF, Loc.T("device.auto_gain.trim_a_fixed_offset_after"), 56, bipolar: true),
                K(WindowP, "WINDOW", WindowF, Loc.T("device.auto_gain.window_how_long_the_ride"), 56),
                K(MaxGain, "MAX GAIN", MaxF, Loc.T("device.auto_gain.max_gain_the_most_the"), 56))
            : CardLayout.Large(new[] { Loc.T("device.auto_gain.tab_loudness"), Loc.T("device.auto_gain.tab_meters") }, 0,
                t => t == 1 ? MetersTab() : LoudnessTab(),
                t => t == 1 ? ResponsePanel() : CorrectionPanel(),
                extras,
                CardLayout.Output(null, K(Trim, "TRIM", TrimF, Loc.T("device.auto_gain.trim_a_fixed_offset_after"), 58, 44, bipolar: true), null),
                t => { centreTab = t; RefreshAll(); });

        // One history sample per UI tick (the graph spans N ticks ≈ 8 s); edits only repaint.
        void Tick()
        {
            scN = engine.DeviceScope(track, di, sc, kScope);
            bool on = Active();
            float tgt = Primed() || HasRef() ? (float)Sc(S_Target) : (float)TargetLufs(P(Target));
            hist.Push(on ? (float)Sc(S_InMom) : -120f, (float)(on ? Sc(S_OutMom) : Sc(S_InMom)), tgt);
            RefreshAll();
            hist.InvalidateVisual();
        }
        ctx.AddDeviceRefresher(Tick);
        Tick();
        return root;
    }
}
