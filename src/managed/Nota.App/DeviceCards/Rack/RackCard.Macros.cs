// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's macro column (specs/040): eight macros as small
// knobs, 2 × 4, each with its name, value and target count, and the Map key that maps the
// chosen macro in place on the Chains tab. A click picks a macro; while mapping, it moves
// mapping to that macro. A macro knob is a full value control: vertical drag, fine with a
// modifier, double-click resets, typed values, automation write and MIDI Learn.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed partial class RackCardView
{
    private Control RcMacroColumn()
    {
        bool mapping = _rs.Mapping;
        var map = RcKey("Map", () =>
        {
            if (_rs.Mapping) _rs.MappingMacro = -1;
            else { _rs.MappingMacro = _rs.SelectedMacro; _rs.Centre = RackCentreTab.Chains; }
            DeferRebuild();
        }, on: mapping, tip: mapping ? "Stop mapping (Esc)" : "Map the chosen macro: click parameters in the chain");
        map.HorizontalAlignment = HorizontalAlignment.Right;
        var title = RcText("Macros", NotaPalette.TextTertiary, NotaType.KnobLabel, FontWeight.SemiBold);
        var head = new Grid { Height = RcTabHeight, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { title, map } };
        Grid.SetColumn(map, 1);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,*,*,*"),
            ColumnSpacing = 3, RowSpacing = 3, Margin = new Thickness(0, 4, 0, 0),
        };
        for (int i = 0; i < 8; i++)
        {
            var cell = RcMacroCell(i);
            Grid.SetColumn(cell, i % 2);
            Grid.SetRow(cell, i / 2);
            grid.Children.Add(cell);
        }
        DockPanel.SetDock(head, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { head, grid } };
    }

    private Control RcMacroCell(int i)
    {
        var a = _ra;
        int count = RcTargetCount(i);
        bool has = count > 0, chosen = _rs.SelectedMacro == i, mapping = _rs.MappingMacro == i;
        string name = a.MacroName(i);

        var knob = new Knob(a.MacroGet(i), 1.0)
        {
            Inline = true, Accent = has, IsDim = !has, Default = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Format = v => NotaNum.F($"{v * 100:0}"),
        };
        if (mapping) knob.ArcColor = NotaPalette.Teal;
        var value = RcMono(has ? NotaNum.F($"{a.MacroGet(i) * 100:0}") : "—", has ? NotaPalette.TextPrimary : NotaPalette.TextDisabled, 7.5);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        var label = RcName(name, mapping ? NotaPalette.Teal : chosen ? NotaPalette.AccentBright : has ? NotaPalette.TextSecondary : NotaPalette.TextDisabled, 7.5);
        label.MaxWidth = 50;

        knob.ValueChanged += v =>
        {
            a.MacroSet(i, (float)v);
            if (has) value.Text = NotaNum.F($"{v * 100:0}");
            _ctx.InvokeRackParamRefreshers();
        };
        var (target, device, param, id) = a.MacroAutomation(i);
        bool picked = false;
        knob.GestureBegin += () =>
        {
            // Turning a macro picks it (and, while mapping, maps to it); the card catches up
            // once the drag ends, so the knob isn't torn down under the pointer.
            picked = _rs.SelectedMacro != i || (_rs.Mapping && _rs.MappingMacro != i);
            _rs.SelectedMacro = i;
            if (_rs.Mapping) _rs.MappingMacro = i;
            E.BeginAutomationWrite(T, target, device, param, id);
        };
        knob.GestureEnd += () =>
        {
            E.EndAutomationWrite(T, target, device, param, id);
            if (picked) DeferRebuild();
        };
        ValueEntry.Attach(knob, $"Macro · {name}", value);
        MidiLearn.Bind(knob, a.MacroLearnTarget(T, i), $"Macro · {name}", v => knob.Value = v);
        _irTick.Add(() =>
        {
            if (knob.Dragging) return;
            float v = a.MacroGet(i);
            if (Math.Abs(v - knob.Value) > 1e-4) knob.Value = v;
            if (has) value.Text = NotaNum.F($"{v * 100:0}");
        });

        // Knob and value side by side, the name under them: four rows fit the column.
        knob.HorizontalAlignment = HorizontalAlignment.Left;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Bottom;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { knob, value } };
        Grid.SetColumn(value, 1);
        label.HorizontalAlignment = HorizontalAlignment.Left;
        var stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0), Children = { top, label } };
        var host = new Panel { Children = { stack } };
        if (has)
        {
            var badge = new Border
            {
                CornerRadius = NotaRadius.Cell, Padding = new Thickness(2, 0), Background = NotaPalette.Wash(NotaPalette.Teal, 0x33),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0),
                Child = RcMono(count.ToString(NotaNum.Culture), NotaPalette.TealBright, 7),
            };
            host.Children.Add(badge);
        }
        var cell = new Border
        {
            CornerRadius = NotaRadius.Field, BorderThickness = new Thickness(1), ClipToBounds = true, Cursor = new Cursor(StandardCursorType.Hand),
            Background = mapping ? NotaPalette.Wash(NotaPalette.Teal, 0x24) : chosen ? RowSel : NotaPalette.SurfaceRaised,
            BorderBrush = mapping ? NotaPalette.Teal : chosen ? NotaPalette.Accent : NotaPalette.BorderDefault,
            Child = host,
        };
        ToolTip.SetTip(cell, has
            ? $"{name} → {count} target{(count == 1 ? "" : "s")} · drag to turn · double-click resets"
            : "Not mapped · pick it and press Map");
        cell.PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(cell).Properties;
            if (pt.IsRightButtonPressed) { e.Handled = true; ShowMacroMenu(a, cell, i); return; }
            if (!pt.IsLeftButtonPressed) return;
            if (e.ClickCount == 2 && label.IsPointerOver) { RcPromptRename(label, name, s => a.SetMacroName(i, s)); return; }
            if (_rs.SelectedMacro == i && (!_rs.Mapping || _rs.MappingMacro == i)) return;
            _rs.SelectedMacro = i;
            if (_rs.Mapping) _rs.MappingMacro = i;
            DeferRebuild();
        };
        return cell;
    }
}
