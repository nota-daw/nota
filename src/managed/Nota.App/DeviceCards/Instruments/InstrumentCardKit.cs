// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — small builders the built-in instrument cards share on top of
// DeviceCardKit (style guide, spec 038): the tab row inside a device, the graph window,
// a groove, row labels and section titles, the wheels column and an upright value bar
// bound to a plugin parameter. State-free, like the kit.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal static class InstrumentCardKit
{
    internal const double TabRowH = 22;   // a device tab row
    internal const double TabGap = 16;    // between tabs inside a device

    /// <summary>A tab row inside a device (§ Tabs): 26 high with a thin line under it, tabs 16
    /// apart; the current tab is ink 1 at 12 / 580 with a 2px orange underline the width of its
    /// word, the rest ink 3. <paramref name="trailing"/> sits at the right end of the row
    /// (a readout, a control). Left button picks; `sync` repaints from `current`.</summary>
    internal static Control Tabs(string[] names, Func<int> current, Action<int> pick, out Action sync, Control? trailing = null)
    {
        int n = names.Length;
        var texts = new TextBlock[n];
        var lines = new Border[n];
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = TabGap };
        for (int i = 0; i < n; i++)
        {
            int iv = i;
            var tb = new TextBlock
            {
                Text = names[i], FontSize = NotaType.DeviceTab,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = NotaRadius.Fine };
            var cell = new Panel
            {
                Height = TabRowH, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Children = { tb, line },
            };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                pick(iv); Paint(); e.Handled = true;
            };
            texts[i] = tb; lines[i] = line; row.Children.Add(cell);
        }
        void Paint()
        {
            int cur = current();
            for (int i = 0; i < n; i++)
            {
                bool on = i == cur;
                texts[i].Foreground = on ? TextPrimary : TextTertiary;
                texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Medium;
                lines[i].Background = on ? Brass : Brushes.Transparent;
            }
        }
        sync = Paint;
        Paint();
        var dock = new DockPanel { LastChildFill = false, Height = TabRowH };
        DockPanel.SetDock(row, Dock.Left);
        dock.Children.Add(row);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(trailing, Dock.Right);
            dock.Children.Add(trailing);
        }
        return new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 0, 1), Child = dock };
    }

    /// <summary>A graph window inside a section: the groove ground, corner 6, a 1–2px inner shadow.</summary>
    internal static Border GraphWindow(Control child)
    {
        var b = new Border { Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field, ClipToBounds = true, Child = child };
        b.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
        return b;
    }

    /// <summary>A groove around a strip of controls (segment-like cells): corner 7, 2 padding.</summary>
    internal static Border Groove(Control child)
    {
        var b = new Border { Background = NotaPalette.Groove, CornerRadius = NotaRadius.Group, Padding = new Thickness(2), Child = child, VerticalAlignment = VerticalAlignment.Center };
        b.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
        return b;
    }

    /// <summary>Paint a cell of a hand-built segment strip: the picked one raised in the soft tint.</summary>
    internal static void PaintCell(Border cell, bool on)
    {
        cell.Background = on ? NotaPalette.AccentSoft : Brushes.Transparent;
        if (on) cell.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        else cell.BoxShadow = default;
    }

    /// <summary>A row label or caption: 10.5, ink 3, sentence case.</summary>
    internal static TextBlock Cap(string text, IBrush? ink = null, double size = NotaType.RowLabel) => new()
    {
        Text = NotaText.Label(text), FontSize = size, Foreground = ink ?? TextTertiary,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A section title that names a group of rows (Osc, Sub, Global): 12 / 600, ink 1.</summary>
    internal static TextBlock Title(string text) => new()
    {
        Text = NotaText.Label(text), FontSize = NotaType.DeviceTab, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A value read-out: 11, ink 1.</summary>
    internal static TextBlock Value(string text = "", IBrush? ink = null) => new()
    {
        Text = text, FontSize = NotaType.KnobValue, Foreground = ink ?? TextPrimary, FontFamily = NotaFonts.UiFamily,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A pressed-in section with the standard inside padding.</summary>
    internal static RuleSection Pane(Control body, Thickness? pad = null)
        => new() { Child = body, Padding = pad ?? new Thickness(SectionPad, 8) };

    /// <summary>The wheels panel's width: the same in every instrument that has wheels.</summary>
    internal const double WheelsW = 76;
    private const double WheelsInnerW = 60;

    /// <summary>The wheels panel (§ Wheels) every instrument with wheels leads with, identical in
    /// all of them: a <see cref="WheelsW"/> pane, "Wheels" over the two round wheels filling the
    /// height, "Pitch" and "Mod" under each and the two values side by side.</summary>
    internal static Decorator WheelsPanel(Control pitch, Control mod, TextBlock pitchVal, TextBlock modVal,
        TextBlock? pitchCap = null, TextBlock? modCap = null)
    {
        pitchCap ??= Cap("Pitch");
        modCap ??= Cap("Mod");
        var head = Cap("Wheels");
        foreach (var t in new[] { head, pitchCap, modCap, pitchVal, modVal }) { t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center; }
        Grid Pair(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Width = WheelsInnerW, HorizontalAlignment = HorizontalAlignment.Center };
            a.HorizontalAlignment = b.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(a); Grid.SetColumn(b, 1); g.Children.Add(b);
            return g;
        }
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 4 };
        var rows = new Control[] { head, Pair(pitch, mod), Pair(pitchCap, modCap), Pair(pitchVal, modVal) };
        rows[2].Margin = new Thickness(0, 2, 0, -3);
        for (int i = 0; i < rows.Length; i++) { Grid.SetRow(rows[i], i); grid.Children.Add(rows[i]); }
        var pane = Pane(grid, new Thickness(4, 9, 4, 6));
        pane.Width = WheelsW;
        return pane;
    }

    /// <summary>A round wheel bound to a plugin parameter (pitch springs back to the centre).</summary>
    internal static PerformWheel Wheel(DeviceCardContext ctx, Dictionary<string, int> idx, string id, double def, bool spring, Action refresh, List<Action> readouts, string entryName = "Mod wheel")
    {
        var engine = ctx.Engine; int track = ctx.TrackId;
        int pi = idx.TryGetValue(id, out var i) ? i : -1;
        float G() => pi >= 0 ? engine.PluginParamGet(track, -1, pi) : 0f;
        var w = new PerformWheel { Default = def, Spring = spring, VerticalAlignment = VerticalAlignment.Stretch, Norm = G() };
        w.ValueChanged += v => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); refresh(); };
        w.GestureBegin += () => { if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); };
        w.GestureEnd += () => { if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); };
        if (pi >= 0) MidiLearn.Bind(w, MidiTarget.PluginParam(track, -1, pi), id);
        readouts.Add(() => { if (!w.Dragging) w.Norm = G(); });
        if (!spring) ValueEntry.Attach(w, w.EntryField(entryName, NotaNum.Pct));
        return w;
    }

    /// <summary>An upright value bar (a device fader) over a 0..1 plugin parameter: drag, Shift
    /// fine, double-click resets to the parameter's default, ⌥-click or a double-click on
    /// <paramref name="value"/> types one, right-click learns. Registers its live follow.</summary>
    internal static SliderTrack VBar(DeviceCardContext ctx, Dictionary<string, int> idx, string id, string name, TextBlock value,
        Func<float, string> fmt, Action refresh, List<Action> readouts, bool bipolar = false)
    {
        var engine = ctx.Engine; int track = ctx.TrackId;
        int pi = idx.TryGetValue(id, out var i) ? i : -1;
        float G() => pi >= 0 ? engine.PluginParamGet(track, -1, pi) : 0f;
        void Set(double v) { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)Math.Clamp(v, 0, 1)); value.Text = fmt(G()); refresh(); }
        double def = pi >= 0 ? engine.InstrumentParamDefault(track, pi) : 0;
        var bar = new SliderTrack { Vertical = true, Bipolar = bipolar, Reset = () => Set(def), Norm = G() };
        bar.Changed += Set;
        void Begin() { if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void End() { if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        bar.GestureBegin += Begin; bar.GestureEnd += End;
        var field = ValueEntry.Probing(name, () => G(), Set, () => fmt(G()), Begin, End, () => { bar.Norm = G(); value.Text = fmt(G()); }, () => Set(def));
        ValueEntry.Attach(bar, field, value);
        if (pi >= 0) MidiLearn.Bind(bar, MidiTarget.PluginParam(track, -1, pi), name);
        readouts.Add(() => { if (!bar.Dragging) bar.Norm = G(); value.Text = fmt(G()); });
        value.Text = fmt(G());
        return bar;
    }

    /// <summary>A horizontal value-bar row over a 0..1 plugin parameter (kit SliderRow), with
    /// automation gestures, reset to the parameter's default, MIDI learn and live follow.</summary>
    internal static Grid ParamRow(DeviceCardContext ctx, Dictionary<string, int> idx, string id, string label, Func<float, string> fmt,
        Action refresh, List<Action> readouts, double labelWidth = 0, double trackWidth = double.NaN, double valueWidth = 34,
        bool bipolar = false, Func<bool>? dim = null, string? learnName = null)
    {
        var engine = ctx.Engine; int track = ctx.TrackId;
        int pi = idx.TryGetValue(id, out var i) ? i : -1;
        float G() => pi >= 0 ? engine.PluginParamGet(track, -1, pi) : 0f;
        double def = pi >= 0 ? engine.InstrumentParamDefault(track, pi) : 0;
        var row = SliderRow(label, () => G(), n => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)n); refresh(); }, () => fmt(G()), out var sync,
            begin: () => { if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); },
            end: () => { if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); },
            reset: () => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)def); refresh(); },
            bipolar: bipolar, dim: dim, labelWidth: labelWidth, trackWidth: trackWidth, valueWidth: valueWidth);
        readouts.Add(sync);
        if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), learnName ?? (label.Length > 0 ? label : id));
        return row;
    }

    internal static T At<T>(this T c, int col, int row = 0) where T : Control { Grid.SetColumn(c, col); Grid.SetRow(c, row); return c; }
    internal static T Docked<T>(this T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
}
