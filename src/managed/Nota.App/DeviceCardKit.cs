// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — shared, state-free toolkit for the device-chain cards:
// the palette aliases, the standard card height, and the small atomic builders
// (labeled knob cell, minimal card frame, glyph button, bypass tag, …). Nothing
// here reads engine or view state, so it is a plain static kit; the card
// builders bring it into scope with `using static Nota.App.DeviceCardKit`.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal static class DeviceCardKit
{
    internal const double CardH = 260;
    internal const double HeaderH = 22;   // device header: name · preset · type tag · power
    internal const double CardPad = 4;    // the body's distance from the card edge
    internal const double SectionGap = 6, SectionPad = 8;   // between pressed-in sections; inside one

    internal static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    internal static readonly IBrush Card2 = NotaPalette.SurfaceCard;
    internal static readonly IBrush Sunken = NotaPalette.BgSunken;
    internal static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    internal static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    internal static readonly IBrush Brass = NotaPalette.Accent;   // the accent: live or chosen only
    internal static readonly IBrush VizInk = NotaPalette.VizInk;  // a value at rest
    internal static readonly IBrush Success = NotaPalette.Success;
    internal static readonly IBrush Danger = NotaPalette.Danger;
    internal static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    internal static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    internal static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    internal static readonly IBrush TextDisabled = NotaPalette.TextDisabled;
    internal static readonly IBrush AccentBright = NotaPalette.AccentBright;
    internal static readonly IBrush OnAccent = NotaPalette.TextOnAccent;
    internal static readonly IBrush Teal = NotaPalette.Teal;                                     // modulation accent
    internal static readonly IBrush AccentSubtleB = NotaPalette.AccentSubtle;

    internal static string Pct(float v) => $"{(int)Math.Round(v * 100)}\u2009%";

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // Octave convention matches PianoRollView (pitch/12 - 1), so a note's label is the
    // same the user draws in the piano roll (MIDI 36 = "C2"). Shared by the Sampler root
    // readout and the Drum Rack pad labels.
    internal static string NoteName(int note) => $"{NoteNames[((note % 12) + 12) % 12]}{note / 12 - 1}";

    // A parameter cell: knob, then its value (8, ink 1) over its name (8, sentence case,
    // ink 3), centred, no gap — line heights 9 and 10 (the name's descenders get the taller
    // line), so under a 34 knob the cell is exactly ParamCell (53) tall. A parameter changed from
    // its default (or what the current tab is about — pass `emphasised`) lifts its name to
    // ink 2. Under modulation the arc takes its source's chroma.
    internal static Control KnobCell(string name, Knob knob, TextBlock value, double cellW = 58, bool emphasised = false, IBrush? labelInk = null)
    {
        value.FontSize = NotaType.KnobValue;
        value.LineHeight = 9;
        value.FontFamily = NotaFonts.UiFamily;
        var lbl = new TextBlock
        {
            Text = NotaText.Label(name), FontSize = NotaType.KnobLabel,
            LineHeight = 10, Foreground = labelInk ?? TextTertiary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var valueInk = value.Foreground ?? TextPrimary;
        void Paint()
        {
            bool hot = knob.ArcColor is null && (emphasised || knob.IsModified);
            lbl.Foreground = hot ? TextSecondary : labelInk ?? TextTertiary;
            value.Foreground = valueInk;
        }
        knob.ValueSet += Paint;
        Paint();
        // ⌥-click the knob, or double-click its value, to type one; double-click the knob to reset (specs/035). A knob
        // without a Format is typed by reading its value line back after each probe.
        ValueEntry.Attach(knob, name, value);
        knob.HorizontalAlignment = HorizontalAlignment.Center;
        value.HorizontalAlignment = lbl.HorizontalAlignment = HorizontalAlignment.Center;
        value.TextAlignment = lbl.TextAlignment = TextAlignment.Center;
        lbl.MaxWidth = cellW - 2;
        return new StackPanel { Width = cellW, Spacing = 0, Children = { knob, value, lbl } };
    }

    // An on/off switch with its word 5 to the right (always a word, never an icon; 8 in
    // ink 2, sentence case). Left button toggles; right-click bubbles to the CV menu.
    // `sync` repaints from `get` — register it with the card's live-follow refreshers.
    internal static Border Switch(string label, Func<bool> get, Action toggle, out Action sync, Func<bool>? dim = null, Func<string>? liveLabel = null)
    {
        var track = new SwitchTrack();
        var txt = new TextBlock
        {
            Text = NotaText.Label(label), FontSize = NotaType.DeviceCaption,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { track } };
        if (label.Length > 0 || liveLabel is not null) row.Children.Add(txt);
        var host = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = row };
        void Paint()
        {
            bool on = get(), d = dim?.Invoke() ?? false;
            track.IsOn = on; track.IsDim = d;
            txt.Foreground = d ? TextDisabled : on ? TextPrimary : TextSecondary;
            if (liveLabel is not null) txt.Text = NotaText.Label(liveLabel());
        }
        host.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            toggle(); Paint(); e.Handled = true;
        };
        sync = Paint;
        Paint();
        return host;
    }

    // Segments inside a device: a groove (1 padding, corner 5) holding cells (6 × 1 padding,
    // corner 3, 9 in ink 3); the picked cell rises out of the groove in the soft tint with
    // orange text. Two to four choices; more than four is a dropdown. `current` returns the
    // picked index, or −1 when the value sits between choices. An inactive strip (`dim` —
    // e.g. a shelf band's slope) draws every cell in ink 4 with nothing picked, never faded.
    // `fill` spreads the cells over the full width; `padX` narrows each cell's side padding
    // where a strip must fit a tight column; `fontSize` drops (never below 7) for a dense strip.
    // Left button picks; right-click bubbles. `sync` repaints — register it with the card.
    internal static Border Segments(string[] names, Func<int> current, Action<int> pick, out Action sync,
        bool fill = false, double minSegWidth = 0, Func<bool>? dim = null, double padX = 6, double fontSize = NotaType.RowLabel)
    {
        int n = names.Length;
        var cells = new Border[n];
        var texts = new TextBlock[n];
        Panel row = fill ? new Grid() : new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < n; i++)
        {
            int iv = i;
            var tb = new TextBlock
            {
                Text = NotaText.Label(names[i]), FontSize = Math.Max(7, fontSize), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var c = new Border
            {
                CornerRadius = NotaRadius.Switch, Padding = new Thickness(padX, 1), MinWidth = minSegWidth,
                Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Child = tb,
            };
            c.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                pick(iv); Paint(); e.Handled = true;
            };
            if (fill)
            {
                ((Grid)row).ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                Grid.SetColumn(c, i);
            }
            cells[i] = c; texts[i] = tb; row.Children.Add(c);
        }
        void Paint()
        {
            int cur = current();
            bool d = dim?.Invoke() ?? false;
            for (int i = 0; i < n; i++)
            {
                bool on = i == cur && !d;
                cells[i].Background = on ? NotaPalette.AccentSoft : Brushes.Transparent;
                if (on) cells[i].BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
                else cells[i].BoxShadow = default;
                texts[i].Foreground = on ? AccentBright : d ? TextDisabled : TextTertiary;
            }
        }
        sync = Paint;
        Paint();
        var host = new Border
        {
            Background = NotaPalette.Groove, CornerRadius = NotaRadius.Cell, Padding = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Child = row,
        };
        host.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
        if (!fill) host.HorizontalAlignment = HorizontalAlignment.Left;
        return host;
    }

    /// <summary>The index of the nearest of <paramref name="values"/>, or −1 when none is within 0.02.</summary>
    internal static int NearestExact(float v, float[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
        return Math.Abs(values[best] - v) < 0.02f ? best : -1;
    }

    // A slider row: its name (9, ink 3, sentence case) · a 9px value bar in a groove · the
    // value (9, ink 1) in a fixed-width right-aligned column, so values stacked in a column
    // line up on the right. An inactive row (`dim`) drops its label and value to ink 4 — but
    // the number stays readable.
    //
    // Works in normalised 0..1: `norm` reads, `setNorm` writes (the caller maps to the
    // parameter and does its own follow-up); `text` formats the value for display.
    // `trackWidth` fixes the track, otherwise it stretches. An empty label drops the
    // label column. `modulation` marks a modulation depth (drawn like any value bar). `sync` repaints from
    // `norm` and skips while the hand is on the slider. ⌥-clicking the slider or double-clicking
    // its value types one in, a double-click on the slider runs `reset`. `textAt` (the read-out at a position, pure)
    // lets typing search without writing the parameter; without it the row probes `setNorm`.
    internal static Grid SliderRow(string label, Func<double> norm, Action<double> setNorm, Func<string> text, out Action sync,
        Action? begin = null, Action? end = null, Action? reset = null, bool bipolar = false, Func<bool>? dim = null,
        double labelWidth = 0, double trackWidth = double.NaN, double valueWidth = 42, bool modulation = false,
        Func<double, string>? textAt = null)
    {
        Action repaint = () => { };
        var track = new SliderTrack { Bipolar = bipolar, Modulation = modulation, Reset = reset is null ? null : () => { reset(); repaint(); } };
        if (!double.IsNaN(trackWidth)) track.Width = trackWidth;
        var val = new TextBlock
        {
            FontFamily = NotaFonts.UiFamily, FontSize = 9, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None,
        };
        if (valueWidth > 0) val.Width = valueWidth;
        TextBlock? lbl = null;
        if (label.Length > 0)
        {
            lbl = new TextBlock
            {
                Text = NotaText.Label(label), FontSize = NotaType.RowLabel,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (labelWidth > 0) lbl.Width = labelWidth;
        }
        void Paint()
        {
            bool d = dim?.Invoke() ?? false;
            track.Norm = norm();
            track.IsDim = d;
            val.Text = text();
            val.Foreground = d ? TextDisabled : TextPrimary;
            if (lbl is not null) lbl.Foreground = d ? TextDisabled : TextTertiary;
        }
        repaint = Paint;
        track.Changed += v => { setNorm(v); val.Text = text(); };
        string name = label.Length > 0 ? label : "Value";
        Action? rowReset = reset is null ? null : () => { reset(); Paint(); };
        var field = textAt is null
            ? ValueEntry.Probing(name, norm, setNorm, text, begin, end, Paint, rowReset)
            : new ValueField(name, norm, textAt, setNorm, begin, end) { After = Paint, Reset = rowReset };
        ValueEntry.Attach(track, field, val);
        if (begin is not null) track.GestureBegin += begin;
        if (end is not null) track.GestureEnd += end;
        track.SizeChanged += (_, _) => track.InvalidateVisual();

        var g = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        int col = 0;
        if (lbl is not null)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(lbl, col++); g.Children.Add(lbl);
        }
        g.ColumnDefinitions.Add(double.IsNaN(trackWidth) ? new ColumnDefinition(1, GridUnitType.Star) : new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(track, col++); g.Children.Add(track);
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(val, col); g.Children.Add(val);

        sync = () => { if (!track.Dragging) Paint(); };
        Paint();
        return g;
    }

    /// <summary>A card section, pressed into the card (corner 7, a darker grey with a thin inner
    /// edge). <paramref name="edges"/> is kept for older call sites; a pressed-in section has
    /// no dividing rules. Graph windows keep their own frame — they are not sections.</summary>
    internal static Decorator Section(Control body, SectionEdge edges = SectionEdge.Auto)
        => new RuleSection { Edges = edges, Child = body };

    internal static Control Hint(string text) => new TextBlock
    {
        Text = text, FontSize = 9, Foreground = TextTertiary,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0),
    };

    // A clickable header glyph (move left / right, remove), drawn — not a typed character.
    internal static Border Glyph(GlyphKind glyph, bool enabled, Action onClick)
    {
        var host = new Border
        {
            Width = 12, Height = 14, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center,
            Cursor = enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            Child = new Nota.App.Glyph(glyph, 8) { Foreground = enabled ? TextSecondary : TextDisabled },
        };
        if (enabled) host.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            e.Handled = true; onClick();
        };
        return host;
    }

    internal static Border TextButton(string text)
    {
        // A raised button inside a device: no border, a light top edge.
        var b = new Border
        {
            Background = NotaPalette.SurfaceHover,
            CornerRadius = NotaRadius.Cell, Padding = new Thickness(10, 4), HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = NotaType.DeviceBody, Foreground = TextPrimary },
        };
        b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        return b;
    }

    internal static Control BypassTag() => new Border
    {
        Background = NotaPalette.Well, CornerRadius = NotaRadius.Cell,
        Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = "Bypassed", FontSize = NotaType.RowLabel, Foreground = TextTertiary },
    };

    // A caption above a control (centered, sentence case, secondary ink).
    internal static Control Labeled(string label, Control c)
    {
        var lbl = new TextBlock { Text = NotaText.Label(label), FontSize = NotaType.RowLabel, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center };
        return new StackPanel { Spacing = 2, Children = { lbl, c } };
    }

    // A horizontal row of single-select chips over an integer choice (get/set).
    internal static Control ChipRow(string[] options, Func<int> get, Action<int> set)
        => Segments(options, () => Math.Clamp(get(), 0, options.Length - 1), set, out _);

    // Preset picker "‹ Name ▾ ›" — the name a raised dropdown (22 high, corner 6, 11.5) between
    // two bare arrows, in a device header: the
    // name opens the factory presets for this kind with the current one checked — under an
    // "Init" entry when the caller can reset to defaults (init); ‹ › step to the previous /
    // next preset (from Init, › is the first and ‹ the last). Clicks are handled so they
    // neither select nor start dragging the card. Shared by the card shell and the rack chain
    // full-UI popups.
    internal static Control PresetPicker(IReadOnlyList<string> presets, int cur, string current,
        Action<int> apply, Action<int> step, double nameWidth = 132, double height = NotaSize.Dropdown, Action? init = null)
    {
        double H = height;   // 22: a card header and the device window's title bar
        var label = new TextBlock
        {
            Text = string.IsNullOrEmpty(current) ? "Init" : current, FontSize = NotaType.DeviceValue,
            Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var chevron = new Glyph(GlyphKind.ChevronDown, 7) { Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(chevron, Dock.Right);
        var name = new Border
        {
            Width = nameWidth, Height = H, Padding = new Thickness(8, 0, 7, 0),   // NaN = stretch to the picker's width
            Background = NotaPalette.SurfaceHover, CornerRadius = NotaRadius.Field,
            Cursor = new Cursor(StandardCursorType.Hand),   // the whole field is clickable, not just the text
            Child = new DockPanel { Children = { chevron, label } },
        };
        name.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        ToolTip.SetTip(name, "Choose a preset");
        name.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(name).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            var flyout = new MenuFlyout();
            if (init != null)
            {
                var mi = new MenuItem { Header = "Init", ToggleType = MenuItemToggleType.Radio, IsChecked = cur < 0 && string.IsNullOrEmpty(current) };
                mi.Click += (_, _) => init();
                flyout.Items.Add(mi);
                flyout.Items.Add(new Separator());
            }
            for (int i = 0; i < presets.Count; i++)
            {
                int iv = i;
                var mi = new MenuItem { Header = presets[i], ToggleType = MenuItemToggleType.Radio, IsChecked = i == cur };
                mi.Click += (_, _) => apply(iv);
                flyout.Items.Add(mi);
            }
            flyout.ShowAt(name);
        };

        Border Step(int dir)
        {
            var g = new Glyph(dir < 0 ? GlyphKind.ChevronLeft : GlyphKind.ChevronRight, 8)
                { Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 18, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = g };
            ToolTip.SetTip(b, dir < 0 ? "Previous preset" : "Next preset");
            b.PointerEntered += (_, _) => g.Foreground = TextPrimary;
            b.PointerExited += (_, _) => g.Foreground = TextTertiary;
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                step(dir);
            };
            return b;
        }
        var picker = new Border
        {
            Height = H, VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 2,
                Children = { Step(-1), At(name, 1), At(Step(+1), 2) },
            },
        };
        return picker;
        static Control At(Control c, int col) { Grid.SetColumn(c, col); return c; }
    }
}
