// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Dynamic EQ-8 body (device kind 13), a build of the "Nota
// Dynamic EQ" mockup (700 × 260): the response graph takes almost the whole width — nodes drag
// right on the curve, a row of eight band chips above it shows each band's dynamic activity, a
// panel on the right edits the selected band (type, FREQ / GAIN / Q, the dynamics mode with
// THRESH — the band's level marked on it — RANGE, ATTACK, RELEASE, its key and Solo), and a
// status strip carries the summary, the selected band's gain now, the Dynamic master switch
// and the output. The live readings come from the engine (DynamicEq.h scopeRead). Every
// control is a device param (raw units), so automation / MIDI learn / presets / A-B /
// persistence come for free; the key track is the device's sidechain routing.
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

internal sealed class DynamicEqDeviceBody : IDeviceBody
{
    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "DYNAMIC EQ";

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
        double Sc(int i) => scN > i ? scope[i] : 0;

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
        IBrush DirInk(int b, bool bright = false) => B(b, DynEq.RangeF) >= 0 ? (bright ? NotaPalette.RoseBright : NotaPalette.Rose) : (bright ? NotaPalette.TealBright : Teal);

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // Key source: the device's sidechain routing (a track), picked from a menu.
        string SrcName(int id) => TrackNames.Of(engine, id);
        int Src() => engine.DeviceSidechainSource(track, di);
        void ShowKeyMenu(Control at)
        {
            var fly = new MenuFlyout();
            var none = new MenuItem { Header = "No key track", ToggleType = MenuItemToggleType.Radio, IsChecked = Src() < 0 };
            none.Click += (_, _) => { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); RefreshAll(); };
            fly.Items.Add(none);
            fly.Items.Add(new Separator());
            for (int i = 0; i < engine.TrackCount; i++)
                if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track)
                {
                    int id = ti.Id;
                    var mi = new MenuItem { Header = SrcName(id), ToggleType = MenuItemToggleType.Radio, IsChecked = Src() == id };
                    mi.Click += (_, _) => { engine.SetDeviceSidechainSource(track, di, id); ctx.NotifyChanged(); RefreshAll(); };
                    fly.Items.Add(mi);
                }
            fly.ShowAt(at);
        }

        // ======================================================================
        // LEFT — band chips over the response graph
        // ======================================================================
        var chipRow = new UniformGrid { Rows = 1, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int b = 0; b < DynEq.Bands; b++)
        {
            int bb = b;
            var num = new TextBlock { FontSize = 8, FontWeight = FontWeight.Bold, Text = (b + 1).ToString(), VerticalAlignment = VerticalAlignment.Center };
            num.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var type = new TextBlock { FontSize = 7, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            var hz = Mono("", 7, TextPrimary); hz.HorizontalAlignment = HorizontalAlignment.Right;
            var bar = new Border { Height = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            var inner = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(4, 0) };
            inner.Children.Add(num); inner.Children.Add(Col(type, 1)); inner.Children.Add(Col(hz, 2));
            var chip = new Border
            {
                Height = 15, Margin = new Thickness(1, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand), Child = new Grid { Children = { inner, bar } },
            };
            ToolTip.SetTip(chip, NotaNum.F($"Band {b + 1} — click to edit it; the bar shows how much of its range the dynamics use now"));
            chip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2) SetP(DynEq.P(bb, DynEq.On), BandOn(bb) ? 0 : 1);
                curve.SelectedBand = bb; RefreshAll(); e.Handled = true;
            };
            readouts.Add(() =>
            {
                bool on = BandOn(bb), sel = bb == Sel(), dimmed = Solo() >= 0 && !sel;
                num.Foreground = !on || dimmed ? TextDisabled : sel ? AccentBright : Brass;
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
        ToolTip.SetTip(curve, "Drag a node — frequency and gain · wheel over a node — Q · double-click a node — band on / off · "
            + "double-click empty space — a new bell there · right-click a node — type, mode, solo");
        var graphPanel = new Border
        {
            Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
            ClipToBounds = true, Margin = new Thickness(0, 0, 5, 0),
            Child = new DockPanel { LastChildFill = true, Children = { chipStrip, new Border { Padding = new Thickness(5), Child = curve } } },
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
            var badge = new Border { Width = 13, Height = 13, CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Center, Child = badgeTxt };
            var name = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var onSw = Switch("On", () => BandOn(b), () => { SetP(pOn, BandOn(b) ? 0 : 1); RefreshAll(); }, out var onSync);
            Learn(onSw, pOn);
            ToolTip.SetTip(onSw, "Band on — off leaves the band out of the signal (double-click its node does the same)");
            bandReadouts.Add(onSync);
            bandReadouts.Add(() =>
            {
                bool on = BandOn(b);
                badge.Background = on ? AccentBright : NotaPalette.TextAxis;
                badgeTxt.Foreground = on ? OnAccent : TextTertiary;
                name.Text = DynEq.TypeNames[TypeOf(b)].ToUpperInvariant();
                name.Foreground = on ? AccentBright : TextTertiary;
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
            ToolTip.SetTip(typeSeg, "Type — high-pass, low shelf, bell, notch, high shelf, low-pass; only shelves and bells take gain and dynamics");
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
                K(pF, "FREQ", () => DynEq.HzShort(B(b, DynEq.FreqF)) + (B(b, DynEq.FreqF) < 1000 ? " Hz" : ""), "Frequency — or drag the node left / right"),
                Col(K(pG, "GAIN", () => Capable(b) ? DynEq.Db(B(b, DynEq.GainF)) + " dB" : "—", "Gain — the static gain; or drag the node up / down",
                    inactive: () => !Capable(b)), 1),
                Col(K(pQ, "Q", () => NotaNum.F($"{B(b, DynEq.QF):0.00}"), "Q — the width (the resonance on the cuts); or the wheel over the node",
                    liveLabel: () => TypeOf(b) is DynEq.LowCut or DynEq.HighCut ? "RESO" : "Q"), 2),
            } };

            // ---- dynamics mode ----
            var modeSeg = Segments(DynEq.ModeNames, () => ModeOf(b), i =>
            {
                if (i != DynEq.Static && !Capable(b)) return;
                DynamicEqCurve.SetMode(engine, track, di, b, i);
                RefreshAll(); curve.InvalidateVisual();
            }, out var modeSync, padX: 6, dim: () => !Capable(b));
            Learn(modeSeg, pMode);
            ToolTip.SetTip(modeSeg, "Static — a plain EQ band · Duck — acts as the band's level rises above the threshold (a cut by default) · "
                + "Lift — acts as it falls below it (a boost by default)");
            bandReadouts.Add(modeSync);
            var dynLabel = Caps("DYNAMICS");
            bandReadouts.Add(() => dynLabel.Foreground = IsDyn(b) ? AccentBright : TextTertiary);
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
            ToolTip.SetTip(flip, "Flip — cut ↔ boost (a Duck that boosts is upward expansion, a Lift that cuts is downward expansion)");
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
                        "Threshold — where the band starts to act; the teal mark is the band's level now",
                        marker: () => ToN(pThr, Math.Max(mn[pThr], curve.Level(b)))),
                    SliderRow("RANGE", pRange, () => (Rng() >= 0 ? "↑ " : "↓ ") + NotaNum.F($"{Math.Abs(Rng()):0.0}"),
                        "Range — how far the gain can move at full engagement (6 dB past the threshold); click the arrow to flip cut / boost",
                        getN: () => Math.Abs(Rng()) / 18.0,
                        setN: n =>
                        {
                            float sgn = Rng() > 0 || (Math.Abs(Rng()) < 0.05f && ModeOf(b) == DynEq.Lift) ? 1 : -1;
                            Raw(pRange, sgn * (float)(Math.Clamp(n, 0, 1) * 18.0));
                        },
                        ink: () => DirInk(b),
                        reset: () => SetP(pRange, ModeOf(b) == DynEq.Lift ? 6 : -6),
                        valueCtl: flip),
                    SliderRow("ATTACK", pAtk, () => DynEq.Ms(B(b, DynEq.AtkF)), "Attack — how fast the band moves toward its range"),
                    SliderRow("RELEASE", pRel, () => DynEq.Ms(B(b, DynEq.RelF)), "Release — how fast it returns to its static gain"),
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
            ToolTip.SetTip(keySeg, "Key — Self: the band listens to this track · Ext: to the key track (click Ext again to pick the track)");
            bandReadouts.Add(keySync);
            var soloTb = new TextBlock { Text = "Solo", FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
            var solo = new Border
            {
                Height = 15, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = soloTb,
            };
            solo.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(solo).Properties.IsLeftButtonPressed) return;
                SetP(DynEq.SoloP, Solo() == b ? 0 : b + 1); RefreshAll(); curve.InvalidateVisual(); e.Handled = true;
            };
            Learn(solo, DynEq.SoloP);
            ToolTip.SetTip(solo, "Solo — hear this band alone");
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

        var right = new Border
        {
            Width = 186, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
            ClipToBounds = true, Child = bandHost,
        };
        DockPanel.SetDock(right, Dock.Right);
        bandHost.Content = BandPanel(Sel());
        curve.SelectionChanged += () => { bandHost.Content = BandPanel(Sel()); RefreshAll(); };
        curve.Edited += RefreshAll;

        // ======================================================================
        // Status strip: summary · gain now · Dynamic · output · engine
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        string StatusText()
        {
            int s = Sel(), nOn = 0, nDyn = 0;
            for (int b = 0; b < DynEq.Bands; b++) { if (BandOn(b)) nOn++; if (IsDyn(b)) nDyn++; }
            var parts = new List<string> { NotaNum.F($"{nOn} bands · {nDyn} dynamic") };
            string band = NotaNum.F($"B{s + 1} {DynEq.TypeNames[TypeOf(s)]} {DynEq.Hz(B(s, DynEq.FreqF))}");
            if (Capable(s)) band += " " + DynEq.Db(B(s, DynEq.GainF)) + " dB";
            band += NotaNum.F($" · Q {B(s, DynEq.QF):0.00}");
            parts.Add(band);
            bool anyExt = false;
            for (int b = 0; b < DynEq.Bands; b++) if (IsDyn(b) && KeyExt(b)) anyExt = true;
            if (anyExt) parts.Add("key " + (Src() >= 0 ? SrcName(Src()) : "none"));
            return string.Join(" · ", parts);
        }
        var nowRead = Mono("", 8, TextTertiary);
        ToolTip.SetTip(nowRead, "The selected band's dynamic gain right now");
        readouts.Add(() =>
        {
            int s = Sel();
            if (IsDyn(s))
            {
                double g = curve.Gr(s);
                nowRead.Text = NotaNum.F($"B{s + 1} ") + (B(s, DynEq.RangeF) >= 0 ? "↑ " : "↓ ") + NotaNum.F($"{Math.Abs(g):0.0} dB");
                nowRead.Foreground = DirInk(s, bright: true);
            }
            else
            {
                nowRead.Text = Solo() >= 0 ? NotaNum.F($"SOLO B{Solo() + 1}") : NotaNum.F($"B{s + 1} static");
                nowRead.Foreground = Solo() >= 0 ? AccentBright : TextTertiary;
            }
        });
        var dynSw = Switch("Dynamic", DynMaster, () => { SetP(DynEq.DynamicsP, DynMaster() ? 0 : 1); RefreshAll(); curve.InvalidateVisual(); }, out var dynSync);
        Learn(dynSw, DynEq.DynamicsP);
        ToolTip.SetTip(dynSw, "Dynamic — off parks every band on its static gain: hear what the dynamics do");
        readouts.Add(dynSync);

        // OUT: a mono readout that drags vertically (±18 dB), double-click resets to 0.
        var outTb = Mono("", 8, TextSecondary);
        var outBox = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), VerticalAlignment = VerticalAlignment.Center, Child = outTb };
        bool outDrag = false; double outY = 0;
        outBox.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(outBox).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { SetP(DynEq.OutputP, 0); RefreshAll(); e.Handled = true; return; }
            outDrag = true; outY = e.GetPosition(outBox).Y; e.Pointer.Capture(outBox); Begin(DynEq.OutputP); e.Handled = true;
        };
        outBox.PointerMoved += (_, e) =>
        {
            if (!outDrag) return;
            double y = e.GetPosition(outBox).Y, dy = outY - y; outY = y;
            bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            Raw(DynEq.OutputP, P(DynEq.OutputP) + (float)(dy * 36.0 / (fine ? 1400.0 : 140.0)));
            RefreshAll();
        };
        void OutEnd() { if (!outDrag) return; outDrag = false; End(DynEq.OutputP); }
        outBox.PointerReleased += (_, e) => { OutEnd(); e.Pointer.Capture(null); };
        outBox.PointerCaptureLost += (_, _) => OutEnd();
        Learn(outBox, DynEq.OutputP);
        ToolTip.SetTip(outBox, "Output — drag up / down (Shift for fine), double-click for 0 dB");
        readouts.Add(() =>
        {
            float o = P(DynEq.OutputP);
            outTb.Text = "OUT " + DynEq.Db(o) + " dB";
            outTb.Foreground = Math.Abs(o) > 0.05f ? AccentBright : TextSecondary;
        });
        var engineRead = Mono("", 8, TextTertiary);
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            double sr = Sc(DynEq.S_SampleRate);
            engineRead.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · CPU {Sc(DynEq.S_Cpu) * 100:0.0} %") : "";
        });
        var statusRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { nowRead, dynSw, outBox, engineRead } };
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, graphPanel } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

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
