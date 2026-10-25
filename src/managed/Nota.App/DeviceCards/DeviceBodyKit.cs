// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — small builders the audio-effect, MIDI-effect and rack bodies share on
// top of DeviceCardKit (style guide, spec 038): the body frame, the tab row inside a device,
// an upright value bar and the fader rail that holds a few of them, row labels and values,
// a readout well and a section title. State-free, like the kit.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal static class DeviceBodyKit
{
    internal const double TabRowH = 26;   // a device tab row
    internal const double TabGap = 16;    // between tabs inside a device

    /// <summary>The body frame: no background of its own (the raised card shows through), 8 under
    /// the header (the shell pads the other three sides).</summary>
    internal static T Frame<T>(T root) where T : Control
    {
        root.Margin = new Thickness(0, CardPad, 0, 0);
        return root;
    }

    /// <summary>A pressed-in section with the standard 12 padding (<paramref name="pad"/> where a
    /// tight section needs less).</summary>
    internal static RuleSection Sec(Control body, double pad = SectionPad, double width = double.NaN)
    {
        var s = new RuleSection { Padding = new Thickness(pad), Child = body };
        if (!double.IsNaN(width)) s.Width = width;
        return s;
    }

    internal static RuleSection Sec(Control body, Thickness pad, double width = double.NaN)
    {
        var s = new RuleSection { Padding = pad, Child = body };
        if (!double.IsNaN(width)) s.Width = width;
        return s;
    }

    /// <summary>A row label: device label size (10.5), sentence case, ink 3.</summary>
    internal static TextBlock Label(string text, IBrush? ink = null) => new()
    {
        Text = NotaText.Label(text), FontSize = NotaType.RowLabel, Foreground = ink ?? TextTertiary,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A value: 11, ink 1 (or the ink given).</summary>
    internal static TextBlock Value(string text = "", IBrush? ink = null) => new()
    {
        Text = text, FontSize = NotaType.KnobValue, Foreground = ink ?? TextPrimary,
        FontFamily = NotaFonts.UiFamily, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A note or summary line: 11 in ink 2 (or the ink given).</summary>
    internal static TextBlock Note(string text = "", IBrush? ink = null) => new()
    {
        Text = text, FontSize = NotaType.KnobValue, Foreground = ink ?? TextSecondary,
        FontFamily = NotaFonts.UiFamily, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A section title: 11 / 600 in ink 1 ("Meters", "Correction").</summary>
    internal static TextBlock Title(string text, IBrush? ink = null) => new()
    {
        Text = NotaText.Label(text), FontSize = NotaType.Caption, FontWeight = FontWeight.SemiBold,
        Foreground = ink ?? TextPrimary, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A rail caption centred over its faders ("Scale", "Tail"): 10.5 in ink 3.</summary>
    internal static TextBlock Caption(string text) => new()
    {
        Text = NotaText.Label(text), FontSize = NotaType.RowLabel, Foreground = TextTertiary,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The head row of a right-panel Output tab, 36 high: the output knob (an inline knob cell) on
    /// the left, the L / R output meter on the right with its 0 and −48 dB marks. <paramref name="add"/>
    /// registers the meter's repaint with the card's readouts.</summary>
    internal static Control OutputHead(Control knob, Func<double> left, Func<double> right, Action<Action> add)
    {
        var l = new PentadLevelBar { Vertical = true, Width = 8, Height = 32 };
        var r = new PentadLevelBar { Vertical = true, Width = 8, Height = 32 };
        add(() => { l.SetLinear(left()); r.SetLinear(right()); });
        TextBlock Mark(string t, VerticalAlignment v) => new()
        {
            Text = t, FontSize = NotaType.Axis, Foreground = TextDisabled, VerticalAlignment = v, LineHeight = 11,
        };
        var marks = new Grid { Height = 32, Margin = new Thickness(2, 0, 0, 0), Children = { Mark("0", VerticalAlignment.Top), Mark("−48", VerticalAlignment.Bottom) } };
        var meter = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Children = { l, r, marks },
        };
        ToolTip.SetTip(meter, "The output level, left and right");
        knob.VerticalAlignment = VerticalAlignment.Center; knob.HorizontalAlignment = HorizontalAlignment.Left;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 36, Children = { knob } };
        Grid.SetColumn(meter, 1); g.Children.Add(meter);
        return g;
    }

    /// <summary>A readout well: the groove grey, corner 6, no border (a cursor readout, a hint box).</summary>
    internal static Border Well(Control child, Thickness? pad = null) => new()
    {
        Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field,
        Padding = pad ?? new Thickness(8, 5), Child = child,
    };

    /// <summary>A 1px rule across a section, splitting groups of rows.</summary>
    internal static Border Rule(double vMargin = 0) => new()
    {
        Height = 1, Background = NotaPalette.Rule, Margin = new Thickness(0, vMargin),
    };

    internal static T At<T>(T c, int col, int row = 0, int colSpan = 1) where T : Control
    {
        Grid.SetColumn(c, col); Grid.SetRow(c, row);
        if (colSpan > 1) Grid.SetColumnSpan(c, colSpan);
        return c;
    }

    internal static T Dock<T>(T c, Avalonia.Controls.Dock d) where T : Control
    {
        DockPanel.SetDock(c, d);
        return c;
    }

    /// <summary>A tab row inside a device (§ Tabs): 26 high with a thin line under it, tabs 16
    /// apart; the current tab is ink 1 at 12 / 580 with a 2px orange underline the width of its
    /// word, the rest ink 3. <paramref name="trailing"/> sits at the right end of the row (a
    /// readout, a control). Left button picks; `sync` repaints from `current`.</summary>
    internal static Control Tabs(string[] names, Func<int> current, Action<int> pick, out Action sync, Control? trailing = null)
    {
        int n = names.Length;
        var texts = new TextBlock[n];
        var lines = new Border[n];
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = TabGap };
        for (int i = 0; i < n; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = names[i], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -1) };
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
                texts[i].FontWeight = on ? (FontWeight)580 : FontWeight.Normal;
                lines[i].Background = on ? Brass : Brushes.Transparent;
            }
        }
        sync = Paint;
        Paint();
        var dock = new DockPanel { LastChildFill = false, Height = TabRowH };
        DockPanel.SetDock(row, Avalonia.Controls.Dock.Left);
        dock.Children.Add(row);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(trailing, Avalonia.Controls.Dock.Right);
            dock.Children.Add(trailing);
        }
        return new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 0, 1), Child = dock };
    }

    /// <summary>A tab row over a swapping body: <paramref name="body"/> builds (or returns the
    /// cached) page for a tab, <paramref name="changed"/> runs after a pick. `current` is kept
    /// inside; <paramref name="trailing"/> sits at the right of the tab row.</summary>
    internal static DockPanel TabFrame(string[] tabs, int initial, ContentControl host, Func<int, Control> body,
        Action<int>? changed = null, Control? trailing = null)
    {
        int sel = Math.Clamp(initial, 0, tabs.Length - 1);
        var row = Tabs(tabs, () => sel, t => { sel = t; host.Content = body(t); changed?.Invoke(t); }, out _, trailing);
        host.Content = body(sel);
        return new DockPanel { LastChildFill = true, Children = { Dock(row, Avalonia.Controls.Dock.Top), host } };
    }

    /// <summary>An upright value bar (a device fader) and its value line, in normalised 0..1:
    /// <paramref name="norm"/> reads, <paramref name="setNorm"/> writes (the caller maps to the
    /// parameter), <paramref name="text"/> formats the value. ⌥-click the bar or double-click
    /// the value to type one; double-click the bar to reset. `sync` repaints (skipping while
    /// dragged) — register it with the card's live follow.</summary>
    internal static SliderTrack Upright(string name, Func<double> norm, Action<double> setNorm, Func<string> text,
        out TextBlock value, out Action sync, Action? begin = null, Action? end = null, Action? reset = null,
        bool bipolar = false, Func<bool>? dim = null)
    {
        Action repaint = () => { };
        var track = new SliderTrack { Vertical = true, Bipolar = bipolar, Reset = reset is null ? null : () => { reset(); repaint(); } };
        var val = Value();
        val.HorizontalAlignment = HorizontalAlignment.Center;
        val.TextAlignment = TextAlignment.Center;
        void Paint()
        {
            bool d = dim?.Invoke() ?? false;
            track.Norm = norm();
            track.IsDim = d;
            val.Text = text();
            val.Foreground = d ? TextDisabled : TextPrimary;
        }
        repaint = Paint;
        track.Changed += v => { setNorm(v); val.Text = text(); };
        if (begin is not null) track.GestureBegin += begin;
        if (end is not null) track.GestureEnd += end;
        Action? rowReset = reset is null ? null : () => { reset(); Paint(); };
        ValueEntry.Attach(track, ValueEntry.Probing(name, norm, setNorm, text, begin, end, Paint, rowReset), val);
        track.SizeChanged += (_, _) => track.InvalidateVisual();
        value = val;
        sync = () => { if (!track.Dragging) Paint(); };
        Paint();
        return track;
    }

    /// <summary>One fader of a rail: its label and the bar and value from <see cref="Upright"/>.</summary>
    internal readonly record struct RailFader(string Label, SliderTrack Bar, TextBlock Value);

    /// <summary>A fader rail (§ Devices): a caption centred at the top, the upright bars side by
    /// side filling the height, each label under its bar; the values either under their own bar
    /// (<paramref name="stacked"/> false) or stacked and centred under the labels.</summary>
    internal static Control FaderRail(string caption, bool stacked, params RailFader[] faders)
        => FaderRail(caption, stacked, out _, faders);

    /// <summary>A fader rail that hands back its labels (for a rail that retargets).</summary>
    internal static Control FaderRail(string caption, bool stacked, out TextBlock[] labels, params RailFader[] faders)
    {
        int n = faders.Length;
        labels = new TextBlock[n];
        var bars = new Grid { RowDefinitions = new RowDefinitions(stacked ? "*,Auto" : "*,Auto,Auto"), RowSpacing = 4, ColumnSpacing = 6 };
        for (int i = 0; i < n; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var f = faders[i];
            bars.Children.Add(At(f.Bar, i, 0));
            var lbl = labels[i] = Label(f.Label);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.TextTrimming = TextTrimming.None;
            lbl.Margin = new Thickness(-12, 0);   // a label may be wider than its bar's column
            bars.Children.Add(At(lbl, i, 1));
            if (!stacked)
            {
                f.Value.Margin = new Thickness(-12, -3, -12, 0);
                bars.Children.Add(At(f.Value, i, 2));
            }
        }
        var dock = new DockPanel { LastChildFill = true };
        if (caption.Length > 0)
        {
            var cap = Caption(caption);
            cap.Margin = new Thickness(0, 0, 0, 8);
            dock.Children.Add(Dock(cap, Avalonia.Controls.Dock.Top));
        }
        if (stacked)
        {
            var vals = new StackPanel { Spacing = 0, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var f in faders) vals.Children.Add(f.Value);
            dock.Children.Add(Dock(vals, Avalonia.Controls.Dock.Bottom));
        }
        dock.Children.Add(bars);
        return dock;
    }
}

/// <summary>An upright level meter in a device (a State or Level column): a 16-wide well in the
/// groove grey with a grey fill from the bottom (or from the top, for a reduction) and a 2px
/// line at its edge, plus an optional dashed threshold tick. Display only.</summary>
internal sealed class UprightMeter : Control
{
    private double _v, _tick = double.NaN;
    public bool FromTop { get; init; }
    public IBrush Ink { get; init; } = NotaPalette.VizInk;

    public UprightMeter() { Width = 16; IsHitTestVisible = false; }

    /// <summary>The fill 0..1 and the threshold tick 0..1 (NaN = none).</summary>
    public void Set(double v, double tick = double.NaN)
    {
        v = Math.Clamp(v, 0, 1);
        if (Math.Abs(v - _v) < 1e-3 && (tick.Equals(_tick) || Math.Abs(tick - _tick) < 1e-3)) return;
        _v = v; _tick = tick; InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 8) return;
        var rect = new Rect(0, 0, w, h);
        double r = NotaRadius.ValueBarValue;
        var rr = new RoundedRect(rect, r);
        NotaDepth.Groove(ctx, this, rr);
        using (ctx.PushClip(rr))
        {
            double y = FromTop ? _v * h : (1 - _v) * h;
            var fill = FromTop ? new Rect(0, 0, w, y) : new Rect(0, y, w, h - y);
            if (Ink is SolidColorBrush sb) ctx.FillRectangle(NotaPalette.Wash(sb, 0x40), fill);
            if (_v > 0.004) ctx.FillRectangle(Ink, new Rect(0, Math.Clamp(FromTop ? y - 2 : y, 0, h - 2), w, 2));
            if (!double.IsNaN(_tick))
            {
                double ty = (1 - Math.Clamp(_tick, 0, 1)) * h;
                ctx.DrawLine(new Pen(NotaPalette.TextSecondary, 1, new DashStyle(new double[] { 2, 2 }, 0)), new Point(2, ty), new Point(w - 2, ty));
            }
        }
    }
}
