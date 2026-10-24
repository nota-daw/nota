// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Dynamic EQ-8 body (device kind 13), laid out like the
// classic EQ Eight and drawn in the style guide (spec 038): the response graph leads at the left
// (nodes drag on the curve) under a slim row of eight band chips — the band's number, filter
// shape and frequency on one line, with its dynamics activity along the bottom — and the panel at the
// right is drawn like EQ-8's: two columns on one set of rows, so their parts line up. The picked
// band (its head with the On switch, the six filter shapes, Freq, Gain and Q, its key and Solo)
// sits beside its dynamics (the mode, then Thresh — the band's level ticked on its ring — Range,
// Attack and Release as knobs, over the device's Dynamic switch and Output).
// The live readings come from the engine (DynamicEq.h scopeRead). Every control is a device param
// (raw units), so automation / MIDI learn / presets / A-B / persistence come for free; the key
// track is the device's sidechain routing. As on EQ-8, the arrow folds the panel to a thin strip
// that keeps the band's Freq, Gain and Q (the card narrows from 940 to 584); the fold is
// session-only view state. FullBleed — the shared shell draws the header.

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
    // The right panel: the band column, a gap, the dynamics column, inside the section's padding
    // and its 1px edge.
    // Folded, a strip just wide enough for the picked band's Freq, Gain and Q stacked under the arrow.
    internal const double BandW = 176, DynW = 200, PanelGap = 16, StripW = EqDeviceBody.StripW;
    internal const double PanelW = BandW + PanelGap + DynW + 2 * SectionPad;
    public const double OpenW = 940, FoldedW = OpenW - PanelW + StripW;   // 584 folded

    public double Width => OpenW;
    public bool AutoWidth => true;   // the card follows the fold
    public bool FullBleed => true;
    public string? Subtitle => "Dynamic EQ";

    // Whether the right panel is folded, per track and device: view state for this session only.

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
        if (EqBandUi.PickedBand(track, di) is int picked) curve.SelectedBand = picked;   // survives a chain rebuild
        int Sel() => curve.SelectedBand;
        bool BandOn(int b) => B(b, DynEq.On) > 0.5f;
        int TypeOf(int b) => Math.Clamp((int)Math.Round(B(b, DynEq.TypeF)), 0, 5);
        int ModeOf(int b) => Math.Clamp((int)Math.Round(B(b, DynEq.ModeF)), 0, 2);
        bool DynMaster() => P(DynEq.DynamicsP) >= 0.5f;
        int Solo() => Math.Clamp((int)Math.Round(P(DynEq.SoloP)), 0, DynEq.Bands) - 1;
        bool Capable(int b) => DynEq.HasGain(TypeOf(b));
        bool IsDyn(int b) => DynMaster() && BandOn(b) && Capable(b) && ModeOf(b) != DynEq.Static;
        bool KeyExt(int b) => P(DynEq.SidechainP) >= 0.5f || P(DynEq.KeyBase + b) >= 0.5f;

        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T Row<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Grid Across(params Control[] cells)
        {
            var g = new Grid();
            for (int i = 0; i < cells.Length; i++) { g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star)); g.Children.Add(Col(cells[i], i)); }
            return g;
        }

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
        // CENTRE — the response graph over the row of band cells
        // ======================================================================
        ToolTip.SetTip(curve, "Drag a node — frequency and gain · wheel over a node — Q · double-click a node — band on / off · "
            + "double-click empty space — a new bell there · right-click a node — type, mode, solo");
        void SetType(int b, int i)
        {
            SetP(DynEq.P(b, DynEq.TypeF), i);
            if (!DynEq.HasGain(i) && ModeOf(b) != DynEq.Static) SetP(DynEq.P(b, DynEq.ModeF), DynEq.Static);
            RefreshAll(); curve.InvalidateVisual();
        }
        var tabRow = new Grid { Margin = new Thickness(0, 0, 0, 6), ColumnSpacing = 2 };
        for (int b = 0; b < DynEq.Bands; b++)
        {
            int bb = b, pOn = DynEq.P(b, DynEq.On);
            bool Lit() => BandOn(bb) && (Solo() < 0 || Solo() == bb);   // outside a solo a band reads as off
            // How much of its range the band's dynamics use right now.
            double Activity()
            {
                double r = Math.Abs(B(bb, DynEq.RangeF));
                return IsDyn(bb) && r > 0.05 ? Math.Abs(curve.Gr(bb)) / r : 0;
            }
            var chip = EqBandUi.Chip(b, () => TypeOf(bb), Lit, () => DynEq.HzShort(B(bb, DynEq.FreqF)),
                () => bb == Sel(), () => { curve.SelectedBand = bb; RefreshAll(); },
                () => { SetP(pOn, BandOn(bb) ? 0 : 1); curve.InvalidateVisual(); }, out var chipSync, Activity);
            Learn(chip, pOn);
            ToolTip.SetTip(chip, NotaNum.F($"Band {b + 1} — click to edit it, double-click to switch it on / off; the bar shows how much of its range the dynamics use now"));
            readouts.Add(chipSync);
            tabRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            tabRow.Children.Add(Col(chip, b));
        }
        DockPanel.SetDock(tabRow, Dock.Top);
        var graphSection = Section(new DockPanel { LastChildFill = true, Children = { tabRow, curve } });
        graphSection.Padding = new Thickness(SectionPad - 1);

        // 0..1 mappings (frequency, Q, attack and release are log)
        bool IsLog(int p) => p >= 0 && p < DynEq.Bands * DynEq.PerBand && (p % DynEq.PerBand) is DynEq.FreqF or DynEq.QF or DynEq.AtkF or DynEq.RelF;
        double ToN(int p, double v) => IsLog(p)
            ? Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p])
            : (Math.Clamp(v, mn[p], mx[p]) - mn[p]) / (mx[p] - mn[p]);
        double FromN(int p, double n) { n = Math.Clamp(n, 0, 1); return IsLog(p) ? mn[p] * Math.Pow(mx[p] / mn[p], n) : mn[p] + n * (mx[p] - mn[p]); }
        double DefOf(int p) => engine.DeviceParamDefault(track, di, p);

        var right = Section(new ContentControl());

        // A knob cell bound to param p. `marker` marks a live reading on the track; `def` moves the
        // double-click value with the band's mode; `inactive` draws it in ink 4 (still editable).
        Control K(int p, string label, Func<string> fmt, string tip, double cellW, Func<string>? liveLabel = null, Func<bool>? inactive = null,
            bool inline = false, bool bipolar = false, Func<double>? marker = null, Func<double>? def = null)
        {
            var val = new TextBlock { Text = fmt(), Foreground = TextPrimary };
            var knob = new Knob(ToN(p, P(p)), 1.0) { Accent = true, Inline = inline, Bipolar = bipolar, Default = ToN(p, def?.Invoke() ?? DefOf(p)) };
            knob.ValueChanged += v => { Raw(p, (float)FromN(p, v)); val.Text = fmt(); curve.InvalidateVisual(); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            var cell = KnobCell(label, knob, val, cellW);
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
                if (marker is not null) knob.Marker = ina ? double.NaN : marker();
                if (def is not null) knob.Default = ToN(p, def());
            });
            cell.HorizontalAlignment = HorizontalAlignment.Center;
            return cell;
        }
        // The picked band's Freq, Gain and Q knobs (across in the panel, stacked in the folded strip).
        Control[] BandKnobs(int b, double cw)
        {
            int pF = DynEq.P(b, DynEq.FreqF), pG = DynEq.P(b, DynEq.GainF), pQ = DynEq.P(b, DynEq.QF);
            return new[]
            {
                K(pF, "Freq", () => DynEq.HzShort(B(b, DynEq.FreqF)) + (B(b, DynEq.FreqF) < 1000 ? " Hz" : ""), "Frequency — or drag the node left / right", cw),
                K(pG, "Gain", () => Capable(b) ? DynEq.Db(B(b, DynEq.GainF)) + " dB" : "—", "Gain — the static gain; or drag the node up / down", cw,
                    inactive: () => !Capable(b)),
                K(pQ, "Q", () => NotaNum.F($"{B(b, DynEq.QF):0.00}"), "Q — the width (the resonance on the cuts); or the wheel over the node", cw,
                    liveLabel: () => TypeOf(b) is DynEq.LowCut or DynEq.HighCut ? "Reso" : "Q"),
            };
        }

        // ======================================================================
        // RIGHT — two columns on one grid of rows, so their parts line up: the picked band (head,
        // filter shapes, Freq / Gain / Q, its key and Solo) beside its dynamics (title and fold
        // arrow, mode, Thresh / Range / Attack / Release, the device's Dynamic switch and Out).
        // Rebuilt when the selection moves, so MIDI learn and automation bind to that band's params.
        // ======================================================================
        Control PanelBody(int b)
        {
            int pOn = DynEq.P(b, DynEq.On), pType = DynEq.P(b, DynEq.TypeF);
            int pMode = DynEq.P(b, DynEq.ModeF), pThr = DynEq.P(b, DynEq.ThrF), pRange = DynEq.P(b, DynEq.RangeF),
                pAtk = DynEq.P(b, DynEq.AtkF), pRel = DynEq.P(b, DynEq.RelF), pKey = DynEq.KeyBase + b;
            void Toggle() { SetP(pOn, BandOn(b) ? 0 : 1); RefreshAll(); curve.InvalidateVisual(); }

            // ---- the band
            var badge = EqBandUi.Badge(b, () => BandOn(b), Toggle, out var badgeSync);
            var onSw = Switch("On", () => BandOn(b), Toggle, out var onSync);
            Learn(badge, pOn); Learn(onSw, pOn);
            ToolTip.SetTip(badge, "Band on — off leaves the band out of the signal (double-click its node does the same)");
            ToolTip.SetTip(onSw, "Band on — off leaves the band out of the signal (double-click its node does the same)");
            var bandHead = EqBandUi.Head(badge, onSw, () => DynEq.TypeNames[TypeOf(b)], () => BandOn(b), out var headSync);
            bandReadouts.Add(badgeSync); bandReadouts.Add(onSync); bandReadouts.Add(headSync);

            var types = EqBandUi.TypeRow(DynEq.TypeNames, () => TypeOf(b), i => SetType(b, i), out var typeSync);
            Learn(types, pType);
            bandReadouts.Add(typeSync);

            var bandKnobs = Across(BandKnobs(b, BandW / 3));

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
                    if (was || Src() < 0) ShowKeyMenu(right);
                }
                RefreshAll();
            }, out var keySync, padX: 6);
            Learn(keySeg, pKey);
            ToolTip.SetTip(keySeg, "Key — Self: the band listens to this track · Ext: to the key track (click Ext again to pick the track)");
            bandReadouts.Add(keySync);
            var solo = TextButton("Solo");
            var soloTb = (TextBlock)solo.Child!;
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
                solo.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                soloTb.Foreground = on ? AccentBright : TextSecondary;
            });
            var keyLbl = EqBandUi.Label("Key");
            keyLbl.Margin = new Thickness(0, 0, 6, 0);
            var keyRow = new DockPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { Docked(solo, Dock.Right), Docked(keyLbl, Dock.Left), Docked(keySeg, Dock.Left) },
            };

            // ---- its dynamics
            var fold = EqBandUi.FoldButton(false, () => SetFolded(true));
            fold.Margin = new Thickness(8, 0, 0, 0);
            var title = new DockPanel { Children = { Docked(fold, Dock.Right), EqBandUi.Title("Dynamics") } };
            title.Children[1].VerticalAlignment = VerticalAlignment.Center;

            var modeSeg = Segments(DynEq.ModeNames, () => ModeOf(b), i =>
            {
                if (i != DynEq.Static && !Capable(b)) return;
                DynamicEqCurve.SetMode(engine, track, di, b, i);
                RefreshAll(); curve.InvalidateVisual();
            }, out var modeSync, fill: true, padX: 0, dim: () => !Capable(b));
            Learn(modeSeg, pMode);
            ToolTip.SetTip(modeSeg, "Static — a plain EQ band · Duck — acts as the band's level rises above the threshold (a cut by default) · "
                + "Lift — acts as it falls below it (a boost by default)");
            bandReadouts.Add(modeSync);
            modeSeg.VerticalAlignment = VerticalAlignment.Center;

            // Ink 4 while the band is static (or the Dynamic switch is off); still editable.
            bool Idle() => !IsDyn(b);
            float Rng() => B(b, DynEq.RangeF);
            double cw = DynW / 4;
            var dynKnobs = Across(
                K(pThr, "Thresh", () => NotaNum.F($"{B(b, DynEq.ThrF):0}\u2009dB"),
                    "Threshold — where the band starts to act; the tick on the ring is the band's level now", cw, inactive: Idle,
                    marker: () => ToN(pThr, Math.Max(mn[pThr], curve.Level(b)))),
                K(pRange, "Range", () => DynEq.Db(Rng()) + " dB",
                    "Range — how far the gain can move at full engagement (6\u2009dB past the threshold): left cuts, right boosts "
                    + "(a Duck that boosts is upward expansion, a Lift that cuts is downward expansion)", cw, inactive: Idle, bipolar: true,
                    def: () => ModeOf(b) == DynEq.Lift ? 6 : -6),
                K(pAtk, "Attack", () => DynEq.Ms(B(b, DynEq.AtkF)), "Attack — how fast the band moves toward its range", cw, inactive: Idle),
                K(pRel, "Release", () => DynEq.Ms(B(b, DynEq.RelF)), "Release — how fast it returns to its static gain", cw, inactive: Idle));

            // The device's Dynamic master switch and output, at the bottom right (as EQ-8's Scale and Output).
            var dynSw = Switch("Dynamic", DynMaster, () => { SetP(DynEq.DynamicsP, DynMaster() ? 0 : 1); RefreshAll(); curve.InvalidateVisual(); }, out var dynSync);
            Learn(dynSw, DynEq.DynamicsP);
            ToolTip.SetTip(dynSw, "Dynamic — off parks every band on its static gain: hear what the dynamics do");
            bandReadouts.Add(dynSync);
            dynSw.VerticalAlignment = VerticalAlignment.Center;
            var outK = K(DynEq.OutputP, "Output", () => DynEq.Db(P(DynEq.OutputP)) + " dB", "Output — the level after the EQ, ±18\u2009dB", 80, inline: true);
            outK.HorizontalAlignment = HorizontalAlignment.Right;
            var master = new DockPanel { Children = { Docked(outK, Dock.Right), dynSw } };

            var g = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{BandW},{PanelGap},{DynW}")),
                // The head sits on its shapes / mode as on EQ-8; the spare height splits evenly above
                // and below the knob row (EQ-8 fills it with Slope and Channel).
                RowDefinitions = new RowDefinitions("Auto,6,Auto,*,Auto,*,Auto"),
            };
            void At(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); g.Children.Add(c); }
            At(bandHead, 0, 0); At(title, 0, 2);
            At(types, 2, 0); At(modeSeg, 2, 2);
            At(bandKnobs, 4, 0); At(dynKnobs, 4, 2);
            At(keyRow, 6, 0); At(master, 6, 2);
            return g;
        }

        // The folded strip: the arrow that opens the panel again, the picked band's knobs centred
        // in the space under it (nudged down past the label lines' slack), as on EQ-8.
        Control FoldedStrip(int b)
        {
            var open = EqBandUi.FoldButton(true, () => SetFolded(false));
            open.HorizontalAlignment = HorizontalAlignment.Center;
            var knobs = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var k in BandKnobs(b, StripW - 2)) knobs.Children.Add(k);
            var strip = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            strip.Children.Add(open);
            strip.Children.Add(Row(knobs, 1));
            return strip;
        }

        graphSection.Margin = new Thickness(0, 0, SectionGap, 0);
        var root = new DockPanel { LastChildFill = true, Children = { Docked(right, Dock.Right), graphSection } };

        bool folded = EqBandUi.IsFolded(track, di);
        void Rebuild()
        {
            bandReadouts.Clear();
            int b = Sel();
            right.Width = folded ? StripW : PanelW;
            right.Padding = folded ? new Thickness(0, 6) : new Thickness(SectionPad - 1);
            ((ContentControl)right.Child!).Content = folded ? FoldedStrip(b) : PanelBody(b);
            root.Width = (folded ? FoldedW : OpenW) - 2 * CardPad;
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
        }
        void SetFolded(bool v) { folded = v; EqBandUi.SetFolded(track, di, v); Rebuild(); RefreshAll(); }
        Rebuild();
        curve.SelectionChanged += () => { EqBandUi.SetPickedBand(track, di, curve.SelectedBand); Rebuild(); RefreshAll(); };
        curve.Edited += RefreshAll;

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
