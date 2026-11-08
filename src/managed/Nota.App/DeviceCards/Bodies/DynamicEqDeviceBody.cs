// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Dynamic EQ-8 body (device kind 13), a build of the "Nota
// Dynamic EQ" mockup (700 × 260): the response graph takes almost the whole width — nodes drag
// right on the curve, a row of eight band chips above it shows each band's dynamic activity,
// the Dynamic master switch and the Output level sit under it — and a panel on the right edits
// the selected band (type, FREQ / GAIN / Q, the dynamics mode with THRESH — the band's level
// marked on it — RANGE, ATTACK, RELEASE, its key and Solo). The live readings come from the
// engine (DynamicEq.h scopeRead). Every control is a device param (raw units), so automation /
// MIDI learn / presets / A-B / persistence come for free; the key track is the device's
// sidechain routing. S / L (#531, CardLayout): S (260) is the band chips over the graph and the
// selected band's Freq, Gain, Q and the output. XL (specs/085) is the L body in the panel above
// the chain: the graph takes the extra width and height.
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

internal sealed class DynamicEqDeviceBody : IDeviceBody
{
    public double Width => 700;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex, xl: true);
    public bool SupportsXl => true;   // XL (specs/085): the L body below, stretched in the panel above the chain
    public bool FullBleed => true;
    public string? Subtitle => "Dynamic EQ";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        int pc = Math.Min(engine.DeviceParamCount(track, di), DynEq.ParamCount);
        var mn = new float[DynEq.ParamCount];
        var mx = new float[DynEq.ParamCount];
        for (int p = 0; p < DynEq.ParamCount; p++)
        {
            mn[p] = p < pc ? engine.DeviceParamMin(track, di, p) : 0;
            mx[p] = p < pc ? engine.DeviceParamMax(track, di, p) : 1;
            if (mx[p] <= mn[p]) mx[p] = mn[p] + 1;
        }
        float P(int p) => p < pc ? engine.DeviceGetParam(track, di, p) : 0f;
        float B(int b, int f) => P(DynEq.P(b, f));
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) { if (p < pc) engine.DeviceSetParam(track, di, p, Math.Clamp(v, mn[p], mx[p])); }
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        var readouts = new List<Action>();
        var bandReadouts = new List<Action>();
        void RefreshAll()
        {
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
        }
        var scope = new float[DynEq.kScope];
        int scN = 0;

        var curve = new DynamicEqCurve(engine, track, di);
        int Sel() => curve.SelectedBand;
        bool BandOn(int b) => B(b, DynEq.On) > 0.5f;
        int TypeOf(int b) => Math.Clamp((int)Math.Round(B(b, DynEq.TypeF)), 0, 5);
        int ModeOf(int b) => Math.Clamp((int)Math.Round(B(b, DynEq.ModeF)), 0, 2);
        bool DynMaster() => P(DynEq.DynamicsP) >= 0.5f;
        int Solo() => Math.Clamp((int)Math.Round(P(DynEq.SoloP)), 0, DynEq.Bands) - 1;
        bool Capable(int b) => DynEq.HasGain(TypeOf(b));
        bool IsDyn(int b) => DynMaster() && BandOn(b) && Capable(b) && ModeOf(b) != DynEq.Static;
        bool KeyExt(int b) => P(DynEq.SidechainP) >= 0.5f || P(DynEq.KeyBase + b) >= 0.5f;
        // Boost vs cut, by shade (style guide): a boost the lighter grey, a cut the darker one.
        IBrush DirInk(int b, bool bright = false) => B(b, DynEq.RangeF) >= 0
            ? (bright ? NotaPalette.TextPrimary : DeviceCardKit.ModInkBright)
            : (bright ? DeviceCardKit.ModInkBright : DeviceCardKit.ModInk);

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // Key source: the device's sidechain routing (a track, or one pad / chain / voice of it,
        // specs/050), picked from a menu.
        int Src() => engine.DeviceSidechainSource(track, di);
        void ShowKeyMenu(Control at)
        {
            var fly = new MenuFlyout();
            var none = new MenuItem { Header = "No key track", ToggleType = MenuItemToggleType.Radio, IsChecked = Src() < 0 };
            none.Click += (_, _) => { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); RefreshAll(); };
            fly.Items.Add(none);
            fly.Items.Add(new Separator());
            foreach (var row in SidechainSourcePicker.MenuRows(engine, track, di, () => { ctx.NotifyChanged(); RefreshAll(); }))
                fly.Items.Add(SidechainSourcePicker.MenuItemFor(row));
            fly.ShowAt(at);
        }

        // ======================================================================
        // LEFT — band chips over the response graph
        // ======================================================================
        var chipRow = new UniformGrid { Rows = 1, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int b = 0; b < DynEq.Bands; b++)
        {
            int bb = b;
            var num = new TextBlock { FontSize = 8, FontWeight = FontWeight.SemiBold, Text = (b + 1).ToString(), VerticalAlignment = VerticalAlignment.Center };
            num.BindResource(TextBlock.FontFamilyProperty, "Font.UI");
            var type = new TextBlock { FontSize = 7, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            var hz = Mono("", 7, TextPrimary); hz.HorizontalAlignment = HorizontalAlignment.Right;
            var bar = new Border { Height = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            var inner = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(4, 0) };
            inner.Children.Add(num); inner.Children.Add(Col(type, 1)); inner.Children.Add(Col(hz, 2));
            var chip = new Border
            {
                Height = 15, Margin = new Thickness(1, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand), Child = new Grid { Children = { inner, bar } },
            };
            ToolTip.SetTip(chip, Loc.F("device.dynamic_eq.band_click_to_edit_it", b + 1));
            chip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2) SetP(DynEq.P(bb, DynEq.On), BandOn(bb) ? 0 : 1);
                curve.SelectedBand = bb; RefreshAll(); e.Handled = true;
            };
            readouts.Add(() =>
            {
                bool on = BandOn(bb), sel = bb == Sel(), dimmed = Solo() >= 0 && !sel;
                num.Foreground = !on || dimmed ? TextDisabled : sel ? AccentBright : TextSecondary;
                type.Text = DynEq.TypeShort[TypeOf(bb)];
                type.Foreground = on && !dimmed ? TextSecondary : TextDisabled;
                hz.Text = DynEq.HzShort(B(bb, DynEq.FreqF));
                hz.Foreground = on && !dimmed ? TextPrimary : TextDisabled;
                chip.BorderBrush = sel ? Brass : Brushes.Transparent;
                chip.Background = sel ? Raised : Sunken;
                double r = Math.Abs(B(bb, DynEq.RangeF));
                double frac = IsDyn(bb) && r > 0.05 ? Math.Clamp(Math.Abs(curve.Gr(bb)) / r, 0, 1) : 0;
                bar.Width = frac * Math.Max(0, chip.Bounds.Width - 2);
                bar.Background = DirInk(bb);
            });
            chipRow.Children.Add(chip);
        }
        var chipStrip = new Border { Height = 20, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = chipRow };
        DockPanel.SetDock(chipStrip, Dock.Top);
        ToolTip.SetTip(curve, Loc.T("device.dynamic_eq.drag_a_node_frequency_and")
            + Loc.T("device.dynamic_eq.double_click_empty_space_a"));
        // Footer under the graph: the master Dynamic switch and the output level.
        var dynSw = Switch("Dynamic", DynMaster, () => { SetP(DynEq.DynamicsP, DynMaster() ? 0 : 1); RefreshAll(); curve.InvalidateVisual(); }, out var dynSync);
        Learn(dynSw, DynEq.DynamicsP);
        ToolTip.SetTip(dynSw, Loc.T("device.dynamic_eq.dynamic_off_parks_every_band"));
        readouts.Add(dynSync);
        var outBar = DeviceCardKit.SliderRow("", () => (P(DynEq.OutputP) + 18) / 36.0, n => { Raw(DynEq.OutputP, (float)(n * 36 - 18)); RefreshAll(); },
            () => DynEq.Db(P(DynEq.OutputP)) + " dB", out var outSync, begin: () => Begin(DynEq.OutputP), end: () => End(DynEq.OutputP),
            reset: () => { SetP(DynEq.OutputP, 0); RefreshAll(); }, bipolar: true, valueWidth: 44);
        readouts.Add(outSync);
        Learn(outBar, DynEq.OutputP);
        var outRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Width = 220, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        outRow.Children.Add(Caps(Loc.T("common.output")));
        outRow.Children.Add(Col(outBar, 1));
        var footer = new DockPanel { Height = 20, Margin = new Thickness(8, 0, 8, 3), Children = { Docked(dynSw, Dock.Left), outRow } };
        DockPanel.SetDock(footer, Dock.Bottom);

        Control GraphPanel() => new RuleSection
        {
            Margin = new Thickness(0, 0, 5, 0),
            Child = new DockPanel { LastChildFill = true, Children = { chipStrip, footer, new Border { Padding = new Thickness(5, 5, 5, 2), Child = curve } } },
        };

        // ======================================================================
        // RIGHT — the selected band (rebuilt when the selection moves, so MIDI learn and
        // automation bind to that band's own params)
        // ======================================================================
        var bandHost = new ContentControl();

        Control BandPanel(int b)
        {
            bandReadouts.Clear();
            int pOn = DynEq.P(b, DynEq.On), pType = DynEq.P(b, DynEq.TypeF), pF = DynEq.P(b, DynEq.FreqF), pG = DynEq.P(b, DynEq.GainF),
                pQ = DynEq.P(b, DynEq.QF), pMode = DynEq.P(b, DynEq.ModeF), pThr = DynEq.P(b, DynEq.ThrF), pRange = DynEq.P(b, DynEq.RangeF),
                pAtk = DynEq.P(b, DynEq.AtkF), pRel = DynEq.P(b, DynEq.RelF), pKey = DynEq.KeyBase + b;

            // 0..1 mappings (frequency, Q, attack and release are log)
            bool IsLog(int p) => p == pF || p == pQ || p == pAtk || p == pRel;
            double ToN(int p, double v) => IsLog(p)
                ? Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p])
                : (Math.Clamp(v, mn[p], mx[p]) - mn[p]) / (mx[p] - mn[p]);
            double FromN(int p, double n) { n = Math.Clamp(n, 0, 1); return IsLog(p) ? mn[p] * Math.Pow(mx[p] / mn[p], n) : mn[p] + n * (mx[p] - mn[p]); }
            double DefOf(int p) => engine.DeviceParamDefault(track, di, p);

            // ---- header: badge · name · ON ----
            var badgeTxt = Mono((b + 1).ToString(), 8, OnAccent); badgeTxt.FontWeight = FontWeight.Bold; badgeTxt.HorizontalAlignment = HorizontalAlignment.Center;
            var badge = new Border { Width = 13, Height = 13, CornerRadius = NotaRadius.Group, VerticalAlignment = VerticalAlignment.Center, Child = badgeTxt };
            var name = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var onSw = Switch("On", () => BandOn(b), () => { SetP(pOn, BandOn(b) ? 0 : 1); RefreshAll(); }, out var onSync);
            Learn(onSw, pOn);
            ToolTip.SetTip(onSw, Loc.T("device.dynamic_eq.band_on_off_leaves_the"));
            bandReadouts.Add(onSync);
            bandReadouts.Add(() =>
            {
                bool on = BandOn(b);
                badge.Background = on ? AccentBright : NotaPalette.TextAxis;
                badgeTxt.Foreground = on ? OnAccent : TextTertiary;
                name.Text = DynEq.TypeNames[TypeOf(b)];
                name.Foreground = on ? TextPrimary : TextTertiary;
            });
            var head = new DockPanel { Height = 20, Margin = new Thickness(8, 0), Children = { Docked(onSw, Dock.Right), badge, name } };
            var headBorder = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };

            // ---- type ----
            var typeSeg = Segments(DynEq.TypeSeg, () => TypeOf(b), i =>
            {
                SetP(pType, i);
                if (!DynEq.HasGain(i) && ModeOf(b) != DynEq.Static) SetP(pMode, DynEq.Static);
                RefreshAll();
            }, out var typeSync, fill: true, padX: 0);
            Learn(typeSeg, pType);
            ToolTip.SetTip(typeSeg, Loc.T("device.dynamic_eq.type_high_pass_low_shelf"));
            bandReadouts.Add(typeSync);

            // ---- knobs ----
            Control K(int p, string label, Func<string> fmt, string tip, Func<string>? liveLabel = null, Func<bool>? inactive = null)
            {
                var val = Mono(fmt(), 7, TextPrimary);
                var knob = new Knob(ToN(p, P(p)), 1.0) { Accent = true, Default = ToN(p, DefOf(p)), Width = 34, Height = 34 };
                knob.ValueChanged += v => { Raw(p, (float)FromN(p, v)); val.Text = fmt(); curve.InvalidateVisual(); RefreshAll(); };
                knob.GestureBegin += () => Begin(p);
                knob.GestureEnd += () => End(p);
                Learn(knob, p);
                ToolTip.SetTip(knob, tip);
                var cell = KnobCell(label, knob, val, 54);
                TextBlock? lbl = null;
                if (cell is Panel pl) foreach (var c in pl.Children) if (c is TextBlock tb && tb != val) { lbl = tb; break; }
                bool? wasInactive = null;
                bandReadouts.Add(() =>
                {
                    if (!knob.Dragging) { double c = ToN(p, P(p)); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                    val.Text = fmt();
                    if (lbl is not null && liveLabel is not null) lbl.Text = liveLabel();
                    bool ina = inactive?.Invoke() ?? false;
                    if (wasInactive != ina) { Inactive.Set(cell, ina); wasInactive = ina; }
                });
                return cell;
            }
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Children =
            {
                K(pF, "FREQ", () => DynEq.HzShort(B(b, DynEq.FreqF)) + (B(b, DynEq.FreqF) < 1000 ? " Hz" : ""), Loc.T("device.dynamic_eq.frequency_or_drag_the_node")),
                Col(K(pG, "GAIN", () => Capable(b) ? DynEq.Db(B(b, DynEq.GainF)) + " dB" : "—", Loc.T("device.dynamic_eq.gain_the_static_gain_or"),
                    inactive: () => !Capable(b)), 1),
                Col(K(pQ, "Q", () => NotaNum.F($"{B(b, DynEq.QF):0.00}"), Loc.T("device.dynamic_eq.q_the_width_the_resonance"),
                    liveLabel: () => TypeOf(b) is DynEq.LowCut or DynEq.HighCut ? "Reso" : "Q"), 2),
            } };

            // ---- dynamics mode ----
            var modeSeg = Segments(DynEq.ModeNames, () => ModeOf(b), i =>
            {
                if (i != DynEq.Static && !Capable(b)) return;
                DynamicEqCurve.SetMode(engine, track, di, b, i);
                RefreshAll(); curve.InvalidateVisual();
            }, out var modeSync, padX: 6, dim: () => !Capable(b));
            Learn(modeSeg, pMode);
            ToolTip.SetTip(modeSeg, Loc.T("device.dynamic_eq.static_a_plain_eq_band")
                + Loc.T("device.dynamic_eq.lift_acts_as_it_falls"));
            bandReadouts.Add(modeSync);
            var dynLabel = Caps("DYNAMICS");
            bandReadouts.Add(() => dynLabel.Foreground = IsDyn(b) ? TextSecondary : TextTertiary);
            var modeRow = new Border
            {
                BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0),
                Child = new DockPanel { Children = { Docked(modeSeg, Dock.Right), dynLabel } },
            };

            // ---- sliders ----
            bool DynOp() => IsDyn(b);
            Control SliderRow(string label, int p, Func<string> fmt, string tip, Func<double>? getN = null, Action<double>? setN = null,
                Func<double>? marker = null, Func<IBrush?>? ink = null, Action? reset = null, Control? valueCtl = null)
            {
                getN ??= () => ToN(p, P(p));
                setN ??= n => Raw(p, (float)FromN(p, n));
                reset ??= () => SetP(p, (float)DefOf(p));
                var trk = new SliderTrack { Reset = () => { reset(); RefreshAll(); } };
                trk.Changed += v => { setN(v); RefreshAll(); curve.InvalidateVisual(); };
                trk.GestureBegin += () => Begin(p);
                trk.GestureEnd += () => End(p);
                Learn(trk, p);
                ToolTip.SetTip(trk, tip);
                var val = Mono(fmt(), 8, TextPrimary); val.TextAlignment = TextAlignment.Right; val.HorizontalAlignment = HorizontalAlignment.Right;
                // Range's read-out sits in its flip arrow, which a click toggles — it stays click-only.
                if (valueCtl is null)
                    ValueEntry.Attach(val, getN, setN, fmt, () => Begin(p), () => End(p), () => { RefreshAll(); curve.InvalidateVisual(); }, control: trk);
                var lbl = Caps(label);
                bandReadouts.Add(() =>
                {
                    bool op = DynOp();
                    if (!trk.Dragging) trk.Norm = getN();
                    trk.IsDim = !op;
                    trk.Ink = ink?.Invoke();
                    trk.Marker = marker is not null && op ? marker() : double.NaN;
                    val.Text = fmt();
                    val.Foreground = op ? TextPrimary : TextDisabled;
                    lbl.Foreground = op ? TextTertiary : TextDisabled;
                });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,40"), ColumnSpacing = 6, Height = 11 };
                g.Children.Add(lbl);
                g.Children.Add(Col(trk, 1));
                g.Children.Add(Col(valueCtl ?? val, 2));
                if (valueCtl is Border { Child: Panel vp }) vp.Children.Add(val);
                return g;
            }
            float Rng() => B(b, DynEq.RangeF);
            // Range: the slider sets the magnitude, the arrow the direction (click it to flip).
            var flip = new Border
            {
                Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Right,
            };
            ToolTip.SetTip(flip, Loc.T("device.dynamic_eq.flip_cut_boost_a_duck"));
            var flipPanel = new Panel();
            flip.Child = flipPanel;
            flip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(flip).Properties.IsLeftButtonPressed) return;
                float r = Rng(); if (Math.Abs(r) < 0.05f) r = ModeOf(b) == DynEq.Lift ? -6 : 6;
                SetP(pRange, -r); RefreshAll(); curve.InvalidateVisual(); e.Handled = true;
            };
            var sliders = new StackPanel
            {
                Spacing = 5, Children =
                {
                    SliderRow("THRESH", pThr, () => NotaNum.F($"{B(b, DynEq.ThrF):0} dB"),
                        Loc.T("device.dynamic_eq.threshold_where_the_band_starts"),
                        marker: () => ToN(pThr, Math.Max(mn[pThr], curve.Level(b)))),
                    SliderRow("RANGE", pRange, () => (Rng() >= 0 ? "↑ " : "↓ ") + NotaNum.F($"{Math.Abs(Rng()):0.0}"),
                        Loc.T("device.dynamic_eq.range_how_far_the_gain"),
                        getN: () => Math.Abs(Rng()) / 18.0,
                        setN: n =>
                        {
                            float sgn = Rng() > 0 || (Math.Abs(Rng()) < 0.05f && ModeOf(b) == DynEq.Lift) ? 1 : -1;
                            Raw(pRange, sgn * (float)(Math.Clamp(n, 0, 1) * 18.0));
                        },
                        ink: () => DirInk(b),
                        reset: () => SetP(pRange, ModeOf(b) == DynEq.Lift ? 6 : -6),
                        valueCtl: flip),
                    SliderRow("ATTACK", pAtk, () => DynEq.Ms(B(b, DynEq.AtkF)), Loc.T("device.dynamic_eq.attack_how_fast_the_band")),
                    SliderRow("RELEASE", pRel, () => DynEq.Ms(B(b, DynEq.RelF)), Loc.T("device.dynamic_eq.release_how_fast_it_returns")),
                },
            };

            // ---- key + solo ----
            var keySeg = Segments(new[] { "Self", "Ext" }, () => KeyExt(b) ? 1 : 0, i =>
            {
                if (i == 0)
                {
                    if (P(DynEq.SidechainP) >= 0.5f)
                    {
                        // Leave the legacy "every band keyed" switch: the other bands keep Ext.
                        for (int o = 0; o < DynEq.Bands; o++) if (o != b) SetP(DynEq.KeyBase + o, 1);
                        SetP(DynEq.SidechainP, 0);
                    }
                    SetP(pKey, 0);
                }
                else
                {
                    bool was = KeyExt(b);
                    SetP(pKey, 1);
                    if (was || Src() < 0) ShowKeyMenu(bandHost);
                }
                RefreshAll();
            }, out var keySync);
            Learn(keySeg, pKey);
            ToolTip.SetTip(keySeg, Loc.T("device.dynamic_eq.key_self_the_band_listens"));
            bandReadouts.Add(keySync);
            var soloTb = new TextBlock { Text = "Solo", FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
            var solo = new Border
            {
                Height = 15, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = soloTb,
            };
            solo.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(solo).Properties.IsLeftButtonPressed) return;
                SetP(DynEq.SoloP, Solo() == b ? 0 : b + 1); RefreshAll(); curve.InvalidateVisual(); e.Handled = true;
            };
            Learn(solo, DynEq.SoloP);
            ToolTip.SetTip(solo, Loc.T("device.dynamic_eq.solo_hear_this_band_alone"));
            bandReadouts.Add(() =>
            {
                bool on = Solo() == b;
                solo.Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent;
                solo.BorderBrush = on ? Brass : NotaPalette.BorderStrong;
                soloTb.Foreground = on ? AccentBright : TextSecondary;
                soloTb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            });
            var keyRow = new DockPanel
            {
                Children = { Docked(solo, Dock.Right), Docked(new Border { Width = 46, Child = Caps("KEY") }, Dock.Left), new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = keySeg } },
            };

            var body = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto,*,Auto"), Margin = new Thickness(8, 6, 8, 6),
            };
            void Row(Control c, int r) { Grid.SetRow(c, r); body.Children.Add(c); }
            Row(typeSeg, 0); Row(knobs, 2); Row(modeRow, 4); Row(sliders, 6); Row(keyRow, 8);
            var panel = new DockPanel { LastChildFill = true, Children = { Docked(headBorder, Dock.Top), body } };
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
            return panel;
        }

        // ---- L: the graph with the band chips and the Dynamic / Output footer; the selected
        // band on the right (an EQ card: the band editor fills the panel, so the output stays
        // in the footer under the graph) ------------------------------------------------------
        Control LargeBody()
        {
            var right = new RuleSection { Width = 186, Child = bandHost };
            DockPanel.SetDock(right, Dock.Right);
            bandHost.Content = BandPanel(Sel());
            curve.SelectionChanged += () => { bandHost.Content = BandPanel(Sel()); RefreshAll(); };
            var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, GraphPanel() } };
            return new Border { Background = NotaPalette.SurfaceCard, Child = bodyRow };
        }

        // ---- S: the band chips over the graph and the selected band's Freq · Gain · Q and the
        // output level ---------------------------------------------------------------------
        Control MiniBody()
        {
            var host = new ContentControl();
            Control Knobs(int b)
            {
                bandReadouts.Clear();
                int pF = DynEq.P(b, DynEq.FreqF), pG = DynEq.P(b, DynEq.GainF), pQ = DynEq.P(b, DynEq.QF);
                Control K(int p, string label, bool log, Func<string> fmt, Func<bool>? inactive = null)
                {
                    double ToN(double v) => log ? Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p]) : (Math.Clamp(v, mn[p], mx[p]) - mn[p]) / (mx[p] - mn[p]);
                    double FromN(double n) { n = Math.Clamp(n, 0, 1); return log ? mn[p] * Math.Pow(mx[p] / mn[p], n) : mn[p] + n * (mx[p] - mn[p]); }
                    var val = Mono(fmt(), 7, TextPrimary);
                    var knob = new Knob(ToN(P(p)), 1.0) { Accent = true, Default = ToN(engine.DeviceParamDefault(track, di, p)), Width = 34, Height = 34 };
                    knob.ValueChanged += v => { Raw(p, (float)FromN(v)); val.Text = fmt(); curve.InvalidateVisual(); RefreshAll(); };
                    knob.GestureBegin += () => Begin(p);
                    knob.GestureEnd += () => End(p);
                    Learn(knob, p);
                    bandReadouts.Add(() =>
                    {
                        if (!knob.Dragging) { double c = ToN(P(p)); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                        val.Text = fmt();
                        if (inactive is not null) knob.IsDim = inactive();
                    });
                    return KnobCell(label, knob, val, 56);
                }
                var outVal = Mono("", 7, TextPrimary);
                var outKnob = new Knob((P(DynEq.OutputP) + 18) / 36.0, 1.0) { Accent = true, Bipolar = true, Default = 0.5, Width = 34, Height = 34 };
                outKnob.ValueChanged += v => { Raw(DynEq.OutputP, (float)(v * 36 - 18)); RefreshAll(); };
                outKnob.GestureBegin += () => Begin(DynEq.OutputP);
                outKnob.GestureEnd += () => End(DynEq.OutputP);
                Learn(outKnob, DynEq.OutputP);
                bandReadouts.Add(() =>
                {
                    if (!outKnob.Dragging) { double c = (P(DynEq.OutputP) + 18) / 36.0; if (Math.Abs(c - outKnob.Value) > 1e-4) outKnob.Value = c; }
                    outVal.Text = DynEq.Db(P(DynEq.OutputP)) + " dB";
                });
                return new UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 0), Children = {
                    K(pF, "FREQ", true, () => DynEq.HzShort(B(b, DynEq.FreqF)) + (B(b, DynEq.FreqF) < 1000 ? " Hz" : "")),
                    K(pG, "GAIN", false, () => Capable(b) ? DynEq.Db(B(b, DynEq.GainF)) + " dB" : "—", () => !Capable(b)),
                    K(pQ, "Q", true, () => NotaNum.F($"{B(b, DynEq.QF):0.00}")),
                    KnobCell(Loc.T("common.output"), outKnob, outVal, 56) } };
            }
            host.Content = Knobs(Sel());
            curve.SelectionChanged += () => { host.Content = Knobs(Sel()); RefreshAll(); };
            var body = new DockPanel { LastChildFill = true, Margin = new Thickness(6), Children = { chipStrip, Docked(host, Dock.Bottom), new Border { Margin = new Thickness(0, 4, 0, 0), Child = curve } } };
            return new Border { Background = NotaPalette.SurfaceCard, Child = new RuleSection { Margin = new Thickness(5), Child = body } };
        }

        curve.Edited += RefreshAll;
        Control root = CardLayout.IsMini(ctx, di) ? MiniBody() : LargeBody();

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, DynEq.kScope);
            curve.Update(scope, scN);
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
