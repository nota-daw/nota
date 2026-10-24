// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Dynamic EQ-8 body (device kind 13), laid out like Ableton's
// EQ Eight so the graph leads (style guide, spec 038): the picked band's Freq, Gain and Q stack
// in a column at the left, the response graph runs across the middle (nodes drag on the curve)
// with the row of eight band cells under it — a numbered on/off toggle, a filter-shape chooser
// and the band's dynamics activity along the bottom — and a column at the right edits the
// picked band's dynamics (mode, Thresh with the band's level marked on it, Range, Attack,
// Release, its key and Solo) over the device's Dynamic master switch and output. The live
// readings come from the engine (DynamicEq.h scopeRead). Every control is a device param (raw
// units), so automation / MIDI learn / presets / A-B / persistence come for free; the key track
// is the device's sidechain routing. FullBleed — the shared shell draws the header.

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
    public double Width => 920;
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

        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T Row<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

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
        var bandRow = new Grid { Margin = new Thickness(0, 8, 0, 0), ColumnSpacing = 6 };
        for (int b = 0; b < DynEq.Bands; b++)
        {
            int bb = b, pOn = DynEq.P(b, DynEq.On), pType = DynEq.P(b, DynEq.TypeF);
            bool Lit() => BandOn(bb) && (Solo() < 0 || Solo() == bb);   // outside a solo a band reads as off
            var badge = EqBandUi.Badge(b, Lit, () => { SetP(pOn, BandOn(bb) ? 0 : 1); RefreshAll(); curve.InvalidateVisual(); }, out var badgeSync);
            var picker = EqBandUi.ShapePicker(DynEq.TypeNames, () => TypeOf(bb), Lit, i => SetType(bb, i), () => { curve.SelectedBand = bb; }, out var pickSync);
            // How much of its range the band's dynamics use right now.
            double Activity()
            {
                double r = Math.Abs(B(bb, DynEq.RangeF));
                return IsDyn(bb) && r > 0.05 ? Math.Abs(curve.Gr(bb)) / r : 0;
            }
            var cell = EqBandUi.Cell(badge, picker, () => bb == Sel(), () => { curve.SelectedBand = bb; RefreshAll(); }, out var cellSync, Activity);
            Learn(badge, pOn); Learn(picker, pType);
            ToolTip.SetTip(cell, NotaNum.F($"Band {b + 1} — click to edit it; the bar shows how much of its range the dynamics use now"));
            readouts.Add(badgeSync); readouts.Add(pickSync); readouts.Add(cellSync);
            bandRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            bandRow.Children.Add(Col(cell, b));
        }
        DockPanel.SetDock(bandRow, Dock.Bottom);
        var graphSection = Section(new DockPanel { LastChildFill = true, Children = { bandRow, curve } });
        graphSection.Padding = new Thickness(SectionPad - 1);

        // 0..1 mappings (frequency, Q, attack and release are log)
        bool IsLog(int p) => p >= 0 && p < DynEq.Bands * DynEq.PerBand && (p % DynEq.PerBand) is DynEq.FreqF or DynEq.QF or DynEq.AtkF or DynEq.RelF;
        double ToN(int p, double v) => IsLog(p)
            ? Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p])
            : (Math.Clamp(v, mn[p], mx[p]) - mn[p]) / (mx[p] - mn[p]);
        double FromN(int p, double n) { n = Math.Clamp(n, 0, 1); return IsLog(p) ? mn[p] * Math.Pow(mx[p] / mn[p], n) : mn[p] + n * (mx[p] - mn[p]); }
        double DefOf(int p) => engine.DeviceParamDefault(track, di, p);

        // The Dynamic master switch and the output, at the bottom of the right column (they were
        // on the status line). Out is a field that drags up and down (±18 dB), double-click 0 dB.
        var dynSw = Switch("Dynamic", DynMaster, () => { SetP(DynEq.DynamicsP, DynMaster() ? 0 : 1); RefreshAll(); curve.InvalidateVisual(); }, out var dynSync);
        Learn(dynSw, DynEq.DynamicsP);
        ToolTip.SetTip(dynSw, "Dynamic — off parks every band on its static gain: hear what the dynamics do");
        readouts.Add(dynSync);
        var outTb = new TextBlock { FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var outBox = new Border
        {
            Height = NotaSize.Dropdown, MinWidth = 58, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Field, Background = NotaPalette.Groove,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth), VerticalAlignment = VerticalAlignment.Center, Child = outTb,
        };
        outBox.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
        bool outDrag = false; double outY = 0;
        outBox.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(outBox).Properties.IsLeftButtonPressed) return;
            if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { SetP(DynEq.OutputP, 0); RefreshAll(); e.Handled = true; return; }
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
        ToolTip.SetTip(outBox, "Output — drag up / down (Shift for fine), double-click for 0\u2009dB");
        readouts.Add(() =>
        {
            float o = P(DynEq.OutputP);
            outTb.Text = DynEq.Db(o) + " dB";
            outTb.Foreground = Math.Abs(o) > 0.05f ? TextPrimary : TextSecondary;
        });
        var outLbl = EqBandUi.Label("Out");
        outLbl.Margin = new Thickness(0, 0, 8, 0);
        var masterRow = new DockPanel { Children = { Docked(outBox, Dock.Right), Docked(outLbl, Dock.Right), dynSw } };

        var left = Section(new ContentControl());
        left.Width = 100;
        left.Padding = new Thickness(7, SectionPad - 1, 7, SectionPad - 1);
        var right = Section(new ContentControl());
        right.Width = 222;
        right.Padding = new Thickness(SectionPad - 1);

        // ======================================================================
        // LEFT — the picked band: toggle and name, then Freq, Gain and Q stacked (rebuilt when
        // the selection moves, so MIDI learn and automation bind to that band's own params)
        // ======================================================================
        Control BandColumn(int b)
        {
            int pOn = DynEq.P(b, DynEq.On), pF = DynEq.P(b, DynEq.FreqF), pG = DynEq.P(b, DynEq.GainF), pQ = DynEq.P(b, DynEq.QF);
            var badge = EqBandUi.Badge(b, () => BandOn(b), () => { SetP(pOn, BandOn(b) ? 0 : 1); RefreshAll(); curve.InvalidateVisual(); }, out var badgeSync);
            Learn(badge, pOn);
            ToolTip.SetTip(badge, "Band on — off leaves the band out of the signal (double-click its node does the same)");
            var name = EqBandUi.Title("");
            name.FontSize = NotaType.Value;
            name.Margin = new Thickness(6, 0, 0, 0);
            bandReadouts.Add(badgeSync);
            bandReadouts.Add(() =>
            {
                name.Text = DynEq.TypeNames[TypeOf(b)];
                name.Foreground = BandOn(b) ? TextPrimary : TextTertiary;
            });
            var head = new DockPanel { Children = { Docked(badge, Dock.Left), name } };

            Control K(int p, string label, Func<string> fmt, string tip, Func<string>? liveLabel = null, Func<bool>? inactive = null)
            {
                var val = new TextBlock { Text = fmt(), Foreground = TextPrimary };
                var knob = new Knob(ToN(p, P(p)), 1.0) { Accent = true, Default = ToN(p, DefOf(p)) };
                knob.ValueChanged += v => { Raw(p, (float)FromN(p, v)); val.Text = fmt(); curve.InvalidateVisual(); RefreshAll(); };
                knob.GestureBegin += () => Begin(p);
                knob.GestureEnd += () => End(p);
                Learn(knob, p);
                ToolTip.SetTip(knob, tip);
                var cell = KnobCell(label, knob, val, 76);
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
                cell.HorizontalAlignment = HorizontalAlignment.Center;
                return cell;
            }
            var col = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto") };
            col.Children.Add(head);
            col.Children.Add(Row(K(pF, "Freq", () => DynEq.HzShort(B(b, DynEq.FreqF)) + (B(b, DynEq.FreqF) < 1000 ? " Hz" : ""), "Frequency — or drag the node left / right"), 2));
            col.Children.Add(Row(K(pG, "Gain", () => Capable(b) ? DynEq.Db(B(b, DynEq.GainF)) + " dB" : "—", "Gain — the static gain; or drag the node up / down",
                inactive: () => !Capable(b)), 4));
            col.Children.Add(Row(K(pQ, "Q", () => NotaNum.F($"{B(b, DynEq.QF):0.00}"), "Q — the width (the resonance on the cuts); or the wheel over the node",
                liveLabel: () => TypeOf(b) is DynEq.LowCut or DynEq.HighCut ? "Reso" : "Q"), 6));
            return col;
        }

        // ======================================================================
        // RIGHT — the picked band's dynamics, then the Dynamic master switch and the output
        // ======================================================================
        Control DynamicsColumn(int b)
        {
            int pMode = DynEq.P(b, DynEq.ModeF), pThr = DynEq.P(b, DynEq.ThrF), pRange = DynEq.P(b, DynEq.RangeF),
                pAtk = DynEq.P(b, DynEq.AtkF), pRel = DynEq.P(b, DynEq.RelF), pKey = DynEq.KeyBase + b;
            var title = EqBandUi.Title(NotaNum.F($"Band {b + 1} dynamics"));

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
            var modeLbl = EqBandUi.Label("Mode");
            bandReadouts.Add(() => modeLbl.Foreground = Capable(b) ? TextTertiary : TextDisabled);
            var modeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*"), Children = { modeLbl, Col(modeSeg, 1) } };

            // A value-bar row; inactive (ink 4) while the band is static.
            bool DynOp() => IsDyn(b);
            Control ValueRow(string label, int p, Func<string> fmt, string tip, Func<double>? getN = null, Action<double>? setN = null,
                Func<double>? marker = null, Action? reset = null, Control? valueCtl = null)
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
                var val = new TextBlock { Text = fmt(), FontSize = NotaType.KnobValue, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                // Range's read-out sits in its flip arrow, which a click toggles — it stays click-only.
                if (valueCtl is null)
                    ValueEntry.Attach(val, getN, setN, fmt, () => Begin(p), () => End(p), () => { RefreshAll(); curve.InvalidateVisual(); }, control: trk);
                var lbl = EqBandUi.Label(label);
                bandReadouts.Add(() =>
                {
                    bool op = DynOp();
                    if (!trk.Dragging) trk.Norm = getN();
                    trk.IsDim = !op;
                    trk.Marker = marker is not null && op ? marker() : double.NaN;
                    val.Text = fmt();
                    val.Foreground = op ? TextPrimary : TextDisabled;
                    lbl.Foreground = op ? TextTertiary : TextDisabled;
                });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,50"), ColumnSpacing = 6 };
                g.Children.Add(lbl);
                g.Children.Add(Col(trk, 1));
                g.Children.Add(Col(valueCtl ?? val, 2));
                if (valueCtl is Border { Child: Panel vp }) vp.Children.Add(val);
                return g;
            }
            float Rng() => B(b, DynEq.RangeF);
            // Range: the bar sets the magnitude, the arrow the direction (click it to flip).
            var flip = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Right };
            ToolTip.SetTip(flip, "Flip — cut ↔ boost (a Duck that boosts is upward expansion, a Lift that cuts is downward expansion)");
            flip.Child = new Panel();
            flip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(flip).Properties.IsLeftButtonPressed) return;
                float r = Rng(); if (Math.Abs(r) < 0.05f) r = ModeOf(b) == DynEq.Lift ? -6 : 6;
                SetP(pRange, -r); RefreshAll(); curve.InvalidateVisual(); e.Handled = true;
            };
            var thr = ValueRow("Thresh", pThr, () => NotaNum.F($"{B(b, DynEq.ThrF):0}\u2009dB"),
                "Threshold — where the band starts to act; the grey mark is the band's level now",
                marker: () => ToN(pThr, Math.Max(mn[pThr], curve.Level(b))));
            var rng = ValueRow("Range", pRange, () => (Rng() >= 0 ? "↑ " : "↓ ") + NotaNum.F($"{Math.Abs(Rng()):0.0}"),
                "Range — how far the gain can move at full engagement (6\u2009dB past the threshold); click the arrow to flip cut / boost",
                getN: () => Math.Abs(Rng()) / 18.0,
                setN: n =>
                {
                    float sgn = Rng() > 0 || (Math.Abs(Rng()) < 0.05f && ModeOf(b) == DynEq.Lift) ? 1 : -1;
                    Raw(pRange, sgn * (float)(Math.Clamp(n, 0, 1) * 18.0));
                },
                reset: () => SetP(pRange, ModeOf(b) == DynEq.Lift ? 6 : -6),
                valueCtl: flip);
            var atk = ValueRow("Attack", pAtk, () => DynEq.Ms(B(b, DynEq.AtkF)), "Attack — how fast the band moves toward its range");
            var rel = ValueRow("Release", pRel, () => DynEq.Ms(B(b, DynEq.RelF)), "Release — how fast it returns to its static gain");

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
            }, out var keySync);
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
            var keyRow = new DockPanel
            {
                Children = { Docked(solo, Dock.Right), Docked(new Border { Width = 56, Child = EqBandUi.Label("Key") }, Dock.Left), keySeg },
            };

            var col = new Grid { RowDefinitions = new RowDefinitions("Auto,6,Auto,6,Auto,4,Auto,4,Auto,4,Auto,6,Auto,*,Auto") };
            col.Children.Add(title);
            col.Children.Add(Row(modeRow, 2));
            col.Children.Add(Row(thr, 4)); col.Children.Add(Row(rng, 6)); col.Children.Add(Row(atk, 8)); col.Children.Add(Row(rel, 10));
            col.Children.Add(Row(keyRow, 12));
            col.Children.Add(Row(masterRow, 14));
            return col;
        }

        graphSection.Margin = new Thickness(SectionGap, 0);
        var root = new DockPanel { LastChildFill = true, Children = { Docked(left, Dock.Left), Docked(right, Dock.Right), graphSection } };

        void Rebuild()
        {
            bandReadouts.Clear();
            if (masterRow.Parent is Panel old) old.Children.Remove(masterRow);   // the shared bottom row moves into the new column
            int b = Sel();
            ((ContentControl)left.Child!).Content = BandColumn(b);
            ((ContentControl)right.Child!).Content = DynamicsColumn(b);
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
        }
        Rebuild();
        curve.SelectionChanged += () => { Rebuild(); RefreshAll(); };
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
