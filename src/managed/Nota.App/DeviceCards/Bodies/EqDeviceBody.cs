// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota EQ-8 body (device kind 0), laid out like Ableton's EQ Eight
// so the graph leads (style guide, spec 038): the picked band's Freq, Gain and Q stack in a
// column at the left, the response graph runs across the middle (nodes drag on the curve) with
// the row of eight band cells under it — each a numbered on/off toggle and a filter-shape
// chooser — and a column at the right holds the picked band's Slope and Channel and the
// device's Global controls (Analyzer, Auto gain, Scale, Output). Its arrow folds that column to
// a thin strip (the card narrows from 920 to 776); the fold is session-only view state.
// The live readings come from the engine (Eq.h scopeRead). Every control is a device param
// (raw units), so automation / MIDI learn / presets / A-B / persistence come for free.
// FullBleed — the shared shell draws the header (name · preset · type · power).

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

internal sealed class EqDeviceBody : IDeviceBody
{
    public const double OpenW = 920, FoldedW = OpenW - EqBandUi.ColumnW + EqBandUi.FoldedW;   // 776 folded

    public double Width => OpenW;
    public bool AutoWidth => true;   // the card follows the fold
    public bool FullBleed => true;
    public string? Subtitle => "Equalizer";   // the processing type, shown as the header badge

    // Whether the right column is folded, per track and device: view state for this session only.
    private static readonly Dictionary<(int, int), bool> Folded = new();

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        int pc = Math.Min(engine.DeviceParamCount(track, di), Eq8.ParamCount);
        var mn = new float[Eq8.ParamCount];
        var mx = new float[Eq8.ParamCount];
        for (int p = 0; p < Eq8.ParamCount; p++)
        {
            mn[p] = p < pc ? engine.DeviceParamMin(track, di, p) : 0;
            mx[p] = p < pc ? engine.DeviceParamMax(track, di, p) : 1;
            if (mx[p] <= mn[p]) mx[p] = mn[p] + 1;
        }
        // Defaults of the appended params, for a device that somehow lacks them.
        float P(int p) => p < pc ? engine.DeviceGetParam(track, di, p) : p == Eq8.ScaleP ? 100f : p == Eq8.AnalyzerP ? Eq8.AnaPost : 0f;
        float B(int b, int f) => P(Eq8.P(b, f));
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) { if (p < pc) engine.DeviceSetParam(track, di, p, Math.Clamp(v, mn[p], mx[p])); }
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Learn(Control c, int p) { if (p < pc) MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p)); }

        var readouts = new List<Action>();
        var bandReadouts = new List<Action>();
        var scope = new float[Eq8.kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        var curve = new Eq8Curve(engine, track, di);
        void RefreshAll()
        {
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
            curve.InvalidateVisual();
        }
        int Sel() => curve.SelectedBand;
        bool BandOn(int b) => B(b, Eq8.On) > 0.5f;
        int TypeOf(int b) => Math.Clamp((int)Math.Round(B(b, Eq8.TypeF)), 0, 5);
        int SlopeOf(int b) => Math.Clamp((int)Math.Round(P(Eq8.SlopeBase + b)), 0, 2);
        int ChanOf(int b) => Math.Clamp((int)Math.Round(P(Eq8.ChannelBase + b)), 0, 4);
        bool Capable(int b) => Eq8.HasGain(TypeOf(b));
        bool Cut(int b) => Eq8.IsCut(TypeOf(b));
        bool AutoOn() => P(Eq8.AutoGainP) >= 0.5f;

        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T Row<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

        // ======================================================================
        // CENTRE — the response graph over the row of band cells
        // ======================================================================
        ToolTip.SetTip(curve, "Drag a node — frequency and gain · wheel over a node — Q · double-click a node — band on / off · "
            + "double-click empty space — a new bell there · right-click a node — type, slope, channel");
        var bandRow = new Grid { Margin = new Thickness(0, 8, 0, 0), ColumnSpacing = 6 };
        for (int b = 0; b < Eq8.Bands; b++)
        {
            int bb = b, pOn = Eq8.P(b, Eq8.On), pType = Eq8.P(b, Eq8.TypeF);
            var badge = EqBandUi.Badge(b, () => BandOn(bb), () => { SetP(pOn, BandOn(bb) ? 0 : 1); RefreshAll(); }, out var badgeSync);
            var picker = EqBandUi.ShapePicker(Eq8.TypeNames, () => TypeOf(bb), () => BandOn(bb),
                i => { SetP(pType, i); RefreshAll(); }, () => { curve.SelectedBand = bb; }, out var pickSync);
            var cell = EqBandUi.Cell(badge, picker, () => bb == Sel(), () => { curve.SelectedBand = bb; RefreshAll(); }, out var cellSync);
            Learn(badge, pOn); Learn(picker, pType);
            ToolTip.SetTip(cell, NotaNum.F($"Band {b + 1} — click to edit it"));
            readouts.Add(badgeSync); readouts.Add(pickSync); readouts.Add(cellSync);
            bandRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            bandRow.Children.Add(Col(cell, b));
        }
        DockPanel.SetDock(bandRow, Dock.Bottom);
        var graphSection = Section(new DockPanel { LastChildFill = true, Children = { bandRow, curve } });
        graphSection.Padding = new Thickness(SectionPad - 1);

        // 0..1 mappings (frequency and Q are log)
        bool IsLog(int p) => p >= 0 && p < Eq8.Bands * Eq8.PerBand && (p % Eq8.PerBand == Eq8.FreqF || p % Eq8.PerBand == Eq8.QF);
        double ToN(int p, double v) => IsLog(p)
            ? Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p])
            : (Math.Clamp(v, mn[p], mx[p]) - mn[p]) / (mx[p] - mn[p]);
        double FromN(int p, double n) { n = Math.Clamp(n, 0, 1); return IsLog(p) ? mn[p] * Math.Pow(mx[p] / mn[p], n) : mn[p] + n * (mx[p] - mn[p]); }

        // ======================================================================
        // LEFT — the picked band: toggle and name, then Freq, Gain and Q stacked (rebuilt when
        // the selection moves, so MIDI learn and automation bind to that band's own params)
        // ======================================================================
        Control BandColumn(int b)
        {
            int pOn = Eq8.P(b, Eq8.On), pF = Eq8.P(b, Eq8.FreqF), pG = Eq8.P(b, Eq8.GainF), pQ = Eq8.P(b, Eq8.QF);
            double DefOf(int p) => p == pF ? 1000 : p == pG ? 0 : p == pQ ? 0.71 : engine.DeviceParamDefault(track, di, p);

            var badge = EqBandUi.Badge(b, () => BandOn(b), () => { SetP(pOn, BandOn(b) ? 0 : 1); RefreshAll(); }, out var badgeSync);
            Learn(badge, pOn);
            ToolTip.SetTip(badge, "Band on — off leaves the band out of the signal (double-click its node does the same)");
            var name = EqBandUi.Title("");
            name.FontSize = NotaType.Value;
            name.Margin = new Thickness(6, 0, 0, 0);
            bandReadouts.Add(badgeSync);
            bandReadouts.Add(() =>
            {
                name.Text = Eq8.TypeNames[TypeOf(b)];
                name.Foreground = BandOn(b) ? TextPrimary : TextTertiary;
            });
            var head = new DockPanel { Children = { Docked(badge, Dock.Left), name } };

            Control K(int p, string label, Func<string> fmt, string tip, Func<string>? liveLabel = null, Func<bool>? inactive = null)
            {
                var val = new TextBlock { Text = fmt(), Foreground = TextPrimary };
                var knob = new Knob(ToN(p, P(p)), 1.0) { Accent = true, Default = ToN(p, DefOf(p)) };
                knob.ValueChanged += v => { Raw(p, (float)FromN(p, v)); val.Text = fmt(); RefreshAll(); };
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
            col.Children.Add(Row(K(pF, "Freq", () => Eq8.HzShort(B(b, Eq8.FreqF)) + (B(b, Eq8.FreqF) < 1000 ? " Hz" : ""), "Frequency — or drag the node left / right"), 2));
            col.Children.Add(Row(K(pG, "Gain", () => Capable(b) ? Eq8.Db(B(b, Eq8.GainF)) + " dB" : "—", "Gain — or drag the node up / down (Scale multiplies it)",
                inactive: () => !Capable(b)), 4));
            col.Children.Add(Row(K(pQ, "Q", () => NotaNum.F($"{B(b, Eq8.QF):0.00}"), "Q — the width (the resonance on the cuts); or the wheel over the node",
                liveLabel: () => Cut(b) ? "Reso" : "Q"), 6));
            return col;
        }

        // ======================================================================
        // RIGHT — the picked band's Slope and Channel, then the device's Global controls
        // ======================================================================
        Control SegRow(string label, Border seg, Func<bool>? dim = null)
        {
            var lbl = EqBandUi.Label(label);
            if (dim is not null) bandReadouts.Add(() => lbl.Foreground = dim() ? TextDisabled : TextTertiary);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*") };
            g.Children.Add(lbl);
            g.Children.Add(Col(seg, 1));
            return g;
        }
        Control ValueRow(string label, int p, Func<string> fmt, string tip, double def)
        {
            var row = SliderRow(label, () => (P(p) - mn[p]) / (mx[p] - mn[p]), v => { Raw(p, (float)(mn[p] + v * (mx[p] - mn[p]))); RefreshAll(); },
                fmt, out var sync, () => Begin(p), () => End(p), () => { SetP(p, (float)def); RefreshAll(); },
                labelWidth: 56, valueWidth: 46, textAt: v => fmt());
            Learn(row, p);
            ToolTip.SetTip(row, tip);
            bandReadouts.Add(sync);
            return row;
        }
        Control SettingsColumn(int b)
        {
            int pSlope = Eq8.SlopeBase + b, pChan = Eq8.ChannelBase + b;
            var title = EqBandUi.Title(NotaNum.F($"Band {b + 1}"));
            var fold = EqBandUi.FoldButton(false, () => SetFolded(true));
            var head = new DockPanel { Children = { Docked(fold, Dock.Right), title } };

            var slopeSeg = Segments(new[] { "12", "24", "48" }, () => SlopeOf(b), i => { if (Cut(b)) { SetP(pSlope, i); RefreshAll(); } },
                out var slopeSync, fill: true, padX: 0, dim: () => !Cut(b));
            Learn(slopeSeg, pSlope);
            ToolTip.SetTip(slopeSeg, "Slope — how steep the high-pass / low-pass falls: 12, 24 or 48\u2009dB per octave");
            bandReadouts.Add(slopeSync);
            var chanSeg = Segments(Eq8.ChannelShort, () => ChanOf(b), i => { SetP(pChan, i); RefreshAll(); }, out var chanSync, fill: true, padX: 0);
            Learn(chanSeg, pChan);
            ToolTip.SetTip(chanSeg, "Channel — St: both channels · M: the centre · S: the edges · L / R: one channel alone");
            bandReadouts.Add(chanSync);

            var anaSeg = Segments(Eq8.AnalyzerSeg, () => Math.Clamp((int)Math.Round(P(Eq8.AnalyzerP)), 0, 2),
                i => { SetP(Eq8.AnalyzerP, i); RefreshAll(); }, out var anaSync, fill: true, padX: 0);
            Learn(anaSeg, Eq8.AnalyzerP);
            ToolTip.SetTip(anaSeg, "Analyzer — Pre: the input spectrum · Post: the output over a dimmer input · Off");
            bandReadouts.Add(anaSync);
            // The auto gain in effect rides on the switch's word, where the status line used to show it.
            var autoSw = Switch("Auto gain", AutoOn, () => { SetP(Eq8.AutoGainP, AutoOn() ? 0 : 1); RefreshAll(); }, out var autoSync,
                liveLabel: () => AutoOn() && scN > Eq8.S_AutoGain ? "Auto gain " + Eq8.Db(Sc(Eq8.S_AutoGain)) + " dB" : "Auto gain");
            Learn(autoSw, Eq8.AutoGainP);
            ToolTip.SetTip(autoSw, "Auto gain — adds the opposite of the shelves' and bells' average level, so edits are judged at the same loudness");
            bandReadouts.Add(autoSync);

            var col = new Grid { RowDefinitions = new RowDefinitions("Auto,6,Auto,4,Auto,*,Auto,6,Auto,6,Auto,6,Auto,4,Auto") };
            col.Children.Add(head);
            col.Children.Add(Row(SegRow("Slope", slopeSeg, () => !Cut(b)), 2));
            col.Children.Add(Row(SegRow("Channel", chanSeg), 4));
            col.Children.Add(Row(EqBandUi.Title("Global"), 6));
            col.Children.Add(Row(SegRow("Analyzer", anaSeg), 8));
            col.Children.Add(Row(autoSw, 10));
            col.Children.Add(Row(ValueRow("Scale", Eq8.ScaleP, () => NotaNum.F($"{P(Eq8.ScaleP):0}\u2009%"),
                "Scale — multiplies every shelf and bell gain: 100\u2009% as set, 0\u2009% flat, 200\u2009% twice as much", 100), 12));
            col.Children.Add(Row(ValueRow("Output", Eq8.OutputP, () => Eq8.Db(P(Eq8.OutputP)) + " dB", "Output — the level after the EQ, ±12\u2009dB", 0), 14));
            return col;
        }

        // The folded strip: just the arrow that opens the column again.
        Control FoldedStrip() => new StackPanel { Children = { EqBandUi.FoldButton(true, () => SetFolded(false)) }, HorizontalAlignment = HorizontalAlignment.Center };

        var left = Section(new ContentControl());
        left.Width = 100;
        left.Padding = new Thickness(7, SectionPad - 1, 7, SectionPad - 1);
        var right = Section(new ContentControl());
        right.Padding = new Thickness(SectionPad - 1);
        var bodyRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        graphSection.Margin = new Thickness(SectionGap, 0);
        bodyRow.Children.Add(left); bodyRow.Children.Add(right); bodyRow.Children.Add(graphSection);

        bool folded = Folded.TryGetValue((track, di), out var f0) && f0;
        void Rebuild()
        {
            bandReadouts.Clear();
            int b = Sel();
            ((ContentControl)left.Child!).Content = BandColumn(b);
            right.Width = folded ? EqBandUi.FoldedW : EqBandUi.ColumnW;
            right.Padding = folded ? new Thickness(0, SectionPad - 1) : new Thickness(SectionPad - 1);
            ((ContentControl)right.Child!).Content = folded ? FoldedStrip() : SettingsColumn(b);
            bodyRow.Width = (folded ? FoldedW : OpenW) - 2 * CardPad;
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
        }
        void SetFolded(bool v) { folded = v; Folded[(track, di)] = v; Rebuild(); RefreshAll(); }
        Rebuild();
        curve.SelectionChanged += () => { Rebuild(); RefreshAll(); };
        curve.Edited += RefreshAll;
        var root = bodyRow;

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, Eq8.kScope);
            curve.Update(scope, scN);
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
