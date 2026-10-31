// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's Macros tab (specs/040): the chosen macro's name
// (double-click renames), value and response curve, and each of its targets with a range
// whose ends drag (min above max inverts) and a marker where the macro drives it now.
// Curves are kept per target (an older project can hold different ones on one macro): the
// curve keys then show none chosen and "Mixed" until one is picked, which applies to all
// the macro's targets as one undo step.

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

internal sealed partial class RackCardView
{
    private static readonly string[] RcCurveNames = ["Lin", "Exp", "Log", "S"];

    private Control RcMacrosTab()
    {
        var a = _ra;
        int m = _rs.SelectedMacro;
        string name = a.MacroName(m);
        int curve = RcMacroCurve(m);
        var targets = new List<int>();
        for (int i = 0; i < a.MappingCount(); i++)
            if (a.TryGetMapping(i, out var mm) && mm.Macro == m) targets.Add(i);
        bool mixed = curve < 0;

        // Header: slot, name, value · curve keys, preview, Map.
        var slot = RcText($"Macro {m + 1}", NotaPalette.TextTertiary, 7.5);
        var nameTb = RcName(name, NotaPalette.TextPrimary, 10);
        nameTb.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(nameTb, "Double-click to rename");
        nameTb.DoubleTapped += (_, _) => RcPromptRename(nameTb, name, s => a.SetMacroName(m, s));
        var value = RcMono(NotaNum.Pct(a.MacroGet(m)), NotaPalette.TextPrimary, 9);
        value.Margin = new Thickness(8, 0, 0, 0);
        var who = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { slot, nameTb } };

        var curveKeys = Segments(RcCurveNames, () => RcMacroCurve(m) is var k && k >= 0 && targets.Count > 0 ? k : -1, k =>
        {
            E.BeginUndoGroup();
            try { foreach (int i in targets) a.SetMappingCurve(i, k); }
            finally { E.EndUndoGroup(); }
            DeferRebuild();
        }, out var curveSync, padX: 4, fontSize: NotaType.KnobLabel, dim: () => targets.Count == 0);
        var preview = new RackCurvePreview { Curve = targets.Count == 0 ? 0 : curve, Position = a.MacroGet(m), VerticalAlignment = VerticalAlignment.Center };
        _irTick.Add(() =>
        {
            double v = a.MacroGet(m);
            if (Math.Abs(preview.Position - v) > 1e-4) { preview.Position = v; preview.InvalidateVisual(); }
            value.Text = NotaNum.Pct(v);
        });
        var curveLabel = RcText(mixed ? "Curve · mixed" : "Curve", NotaPalette.TextTertiary, 7.5);
        var map = RcKey("Map", () => { _rs.MappingMacro = m; _rs.Centre = RackCentreTab.Chains; DeferRebuild(); },
            tip: "Map this macro: click parameters on the Chains tab");
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { curveLabel, curveKeys, preview, map },
        };
        var header = new DockPanel
        {
            LastChildFill = false, Height = 30,
            Children = { RcDock(who, Dock.Left), RcDock(value, Dock.Left), RcDock(right, Dock.Right) },
        };
        var head = Section(header);
        DockPanel.SetDock(head, Dock.Top);

        if (targets.Count == 0)
        {
            var empty = new Border { Padding = new Thickness(4, 8), Child = Hint("No targets yet. Press Map, then click parameters on the Chains tab.") };
            return new DockPanel { LastChildFill = true, Children = { head, empty } };
        }

        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150,56,12"), ColumnSpacing = 6, Height = 12, Margin = new Thickness(0, 4, 0, 0) };
        cols.Children.Add(RcText("Target", NotaPalette.TextTertiary, 7.5));
        var rangeTitle = RcText("Range", NotaPalette.TextTertiary, 7.5);
        ToolTip.SetTip(rangeTitle, "Drag the ends · min above max inverts");
        Grid.SetColumn(rangeTitle, 1); cols.Children.Add(rangeTitle);
        var rows = new StackPanel { Spacing = 2 };
        rows.Children.Add(cols);
        foreach (int i in targets) rows.Children.Add(RcTargetRow(m, i));
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return new DockPanel { LastChildFill = true, Children = { head, scroll } };
    }

    // What a mapping drives: its chain's colour, "Chain · Device ·", the parameter, and how to
    // read and show its values.
    private (IBrush Hue, string Where, string Param, float Min, float Max, Func<float, string> Text) RcTargetInfo(RackMacroMapping m)
    {
        var a = _ra;
        if (m.DeviceIndex == RackMacroMapping.RackControl)
            return (NotaPalette.VizInk, "Rack ·", "Selector", 0f, 1f, v => NotaNum.F($"{v * 127:0}"));
        string chain = RcChainName(m.Chain);
        if (m.DeviceIndex == -1)
            return (ChainHue(m.Chain), $"{chain} · {RcShort(a.ChainInstrumentName(m.Chain))} ·", a.ChainInstrumentParamName(m.Chain, m.ParamIndex), 0f, 1f, v => Pct(v));
        if (m.DeviceIndex <= RackMacroMapping.MidiDeviceBase)
        {
            int j = RackMacroMapping.MidiDeviceBase - m.DeviceIndex;
            return (ChainHue(m.Chain), $"{chain} · {RcShort(a.ChainMidiName(m.Chain, j))} ·", a.ChainMidiParamName(m.Chain, j, m.ParamIndex),
                a.ChainMidiParamMin(m.Chain, j, m.ParamIndex), a.ChainMidiParamMax(m.Chain, j, m.ParamIndex), DeviceParamControls.Fmt);
        }
        if (m.DeviceIndex == RackMacroMapping.PadControls)
            return (ChainHue(m.Chain), $"{chain} ·", $"Pad {m.ParamIndex + 1}", 0f, 1f, v => Pct(v));
        return (ChainHue(m.Chain), $"{chain} · {RcShort(a.ChainDeviceName(m.Chain, m.DeviceIndex))} ·", a.ChainDeviceParamName(m.Chain, m.DeviceIndex, m.ParamIndex),
            a.ChainDeviceParamMin(m.Chain, m.DeviceIndex, m.ParamIndex), a.ChainDeviceParamMax(m.Chain, m.DeviceIndex, m.ParamIndex), DeviceParamControls.Fmt);
    }

    private static string RcShort(string device) => device.StartsWith("Nota ", StringComparison.Ordinal) ? device[5..] : device;

    private Control RcTargetRow(int macro, int index)
    {
        var a = _ra;
        if (!a.TryGetMapping(index, out var m)) return new Border();
        var (hue, where, param, min, max, text) = RcTargetInfo(m);
        float span = Math.Max(1e-6f, max - min);
        int curve = a.MappingCurve(index);

        var stripe = new Border { Width = 3, Height = 9, CornerRadius = NotaRadius.Fine, Background = hue, VerticalAlignment = VerticalAlignment.Center };
        var whereTb = new TextBlock { Text = where, FontSize = NotaType.KnobLabel, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        var paramTb = new TextBlock { Text = NotaText.Label(param), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { stripe, whereTb, paramTb } };

        var bar = new RackMapRangeBar
        {
            Min = (m.RangeMin - min) / span, Max = (m.RangeMax - min) / span, VerticalAlignment = VerticalAlignment.Center,
        };
        string RangeText(float lo, float hi) => $"{text(lo)} → {text(hi)}";
        var range = RcMono(RangeText(m.RangeMin, m.RangeMax), NotaPalette.TextSecondary, 7.5);
        range.HorizontalAlignment = HorizontalAlignment.Right;
        bool grouped = false;
        bar.Began += () => { if (!grouped) { E.BeginUndoGroup(); grouped = true; } };
        bar.Committed += () => { if (grouped) { E.EndUndoGroup(); grouped = false; } };
        bar.Changed += (lo, hi) =>
        {
            float l = (float)(min + lo * span), h = (float)(min + hi * span);
            a.SetMappingRange(index, l, h);
            range.Text = RangeText(l, h);
            _ctx.InvokeRackParamRefreshers();
        };
        void Place()
        {
            double v = RackCurvePreview.Apply(curve, a.MacroGet(macro));
            bar.Current = bar.Min + v * (bar.Max - bar.Min);
            bar.InvalidateVisual();
        }
        Place();
        _irTick.Add(Place);

        var remove = new Glyph(GlyphKind.Close, 8) { Foreground = NotaPalette.TextTertiary, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(remove, "Unmap");
        remove.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(remove).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            a.RemoveMapping(index);
            DeferRebuild();
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150,56,12"), ColumnSpacing = 6, Height = 20 };
        row.Children.Add(label);
        Grid.SetColumn(bar, 1); row.Children.Add(bar);
        Grid.SetColumn(range, 2); row.Children.Add(range);
        Grid.SetColumn(remove, 3); row.Children.Add(remove);
        return row;
    }
}
