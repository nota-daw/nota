// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — shared, state-free toolkit for the device-chain cards:
// the palette aliases, the standard card height, and the small atomic builders
// (labeled knob cell, minimal card frame, glyph button, bypass tag, …). Nothing
// here reads engine or view state, so it is a plain static kit; the card
// builders bring it into scope with `using static Nota.App.DeviceCardKit`.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal static class DeviceCardKit
{
    internal const double CardH = 260;
    internal const double HeaderH = 22;   // almanac device header: 20–22

    internal static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    internal static readonly IBrush Card2 = NotaPalette.SurfaceCard;
    internal static readonly IBrush Sunken = NotaPalette.BgSunken;
    internal static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    internal static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    internal static readonly IBrush Brass = NotaPalette.Accent;
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

    // A parameter cell, as the almanac draws it: knob, then a caps label 7/700 over a mono
    // value 7, centred, no gap — line heights 9 and 10, so under a 34 knob the cell is
    // exactly 53px tall. Label and value turn Brass Light when the parameter is changed
    // from its default (or is what the current tab is about — pass `emphasised`). Under
    // modulation the arc takes its source's chroma and the label stays neutral.
    internal static Control KnobCell(string name, Knob knob, TextBlock value, double cellW = 58, bool emphasised = false, IBrush? labelInk = null)
    {
        knob.HorizontalAlignment = HorizontalAlignment.Center;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.TextAlignment = TextAlignment.Center;
        value.FontSize = NotaType.KnobValue;
        value.LineHeight = 10;
        value.FontFamily = NotaFonts.MonoFamily;
        var lbl = new TextBlock
        {
            Text = name.ToUpperInvariant(), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, LineHeight = 9, Foreground = labelInk ?? TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = cellW - 2,
        };
        var valueInk = value.Foreground ?? TextPrimary;
        void Paint()
        {
            bool hot = knob.ArcColor is null && (emphasised || knob.IsModified);
            lbl.Foreground = hot ? NotaPalette.AccentBright : labelInk ?? TextTertiary;
            value.Foreground = hot ? NotaPalette.AccentBright : valueInk;
        }
        knob.ValueSet += Paint;
        Paint();
        return new StackPanel { Width = cellW, Spacing = 0, Children = { knob, lbl, value } };
    }

    // An on/off switch with its word to the right (almanac: always a word, never an icon;
    // 7px caps inside a device). Left button toggles; right-click bubbles to the CV menu.
    // `sync` repaints from `get` — register it with the card's live-follow refreshers.
    internal static Border Switch(string label, Func<bool> get, Action toggle, out Action sync, Func<bool>? dim = null, Func<string>? liveLabel = null)
    {
        var track = new SwitchTrack();
        var txt = new TextBlock
        {
            Text = label.ToUpperInvariant(), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { track } };
        if (label.Length > 0 || liveLabel is not null) row.Children.Add(txt);
        var host = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = row };
        void Paint()
        {
            bool on = get(), d = dim?.Invoke() ?? false;
            track.IsOn = on; track.IsDim = d;
            txt.Foreground = on && !d ? TextPrimary : TextTertiary;
            if (liveLabel is not null) txt.Text = liveLabel().ToUpperInvariant();
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

    // Segments, as the almanac draws them: a recessed container, and the chosen segment in
    // solid brass with dark text — a fill, not an outline. 9px inside a device. Two to four
    // choices; more than four is a dropdown. `current` returns the lit index, or −1 when
    // the value sits between choices. `fill` spreads the segments over the full width.
    // Left button picks; right-click bubbles. `sync` repaints — register it with the card.
    internal static Border Segments(string[] names, Func<int> current, Action<int> pick, out Action sync,
        bool fill = false, double minSegWidth = 0, Func<bool>? dim = null)
    {
        int n = names.Length;
        var cells = new Border[n];
        var texts = new TextBlock[n];
        Panel row = fill ? new Grid { ColumnSpacing = 1 } : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        for (int i = 0; i < n; i++)
        {
            int iv = i;
            var tb = new TextBlock
            {
                Text = names[i], FontSize = NotaType.DeviceSection, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var c = new Border
            {
                CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 1), MinWidth = minSegWidth,
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
                bool on = i == cur;
                cells[i].Background = on ? (d ? NotaPalette.BorderStrong : Brass) : Brushes.Transparent;
                texts[i].Foreground = on ? OnAccent : d ? TextDisabled : TextTertiary;
                texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
        sync = Paint;
        Paint();
        var host = new Border
        {
            Background = NotaPalette.BgSunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Control, Padding = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Child = row,
        };
        if (!fill) host.HorizontalAlignment = HorizontalAlignment.Left;
        host.BindResource(Border.BoxShadowProperty, "Shadow.Sunken");
        return host;
    }

    /// <summary>The index of the nearest of <paramref name="values"/>, or −1 when none is within 0.02.</summary>
    internal static int NearestExact(float v, float[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
        return Math.Abs(values[best] - v) < 0.02f ? best : -1;
    }

    // A slider row, as the almanac draws it: caps label 8/700 · a 3px track with a 6×7
    // Brass Light handle · a mono 9 value in a fixed-width right-aligned column, so values
    // stacked in a column line up on the right. An inactive row (`dim`) loses its brass
    // and its label and value drop to Ink 6 — but the number stays readable.
    //
    // Works in normalised 0..1: `norm` reads, `setNorm` writes (the caller maps to the
    // parameter and does its own follow-up); `text` formats the value for display.
    // `trackWidth` fixes the track, otherwise it stretches. An empty label drops the
    // label column. `sync` repaints from `norm` and skips while the hand is on the slider.
    internal static Grid SliderRow(string label, Func<double> norm, Action<double> setNorm, Func<string> text, out Action sync,
        Action? begin = null, Action? end = null, Action? reset = null, bool bipolar = false, Func<bool>? dim = null,
        double labelWidth = 0, double trackWidth = double.NaN, double valueWidth = 42)
    {
        Action repaint = () => { };
        var track = new SliderTrack { Bipolar = bipolar, Reset = reset is null ? null : () => { reset(); repaint(); } };
        if (!double.IsNaN(trackWidth)) track.Width = trackWidth;
        var val = new TextBlock
        {
            FontFamily = NotaFonts.MonoFamily, FontSize = 9, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None,
        };
        if (valueWidth > 0) val.Width = valueWidth;
        TextBlock? lbl = null;
        if (label.Length > 0)
        {
            lbl = new TextBlock
            {
                Text = label.ToUpperInvariant(), FontSize = NotaType.RowLabel, FontWeight = FontWeight.Bold,
                LetterSpacing = NotaType.RowLabelTracking, VerticalAlignment = VerticalAlignment.Center,
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

    internal static Border SimpleCard(string title, double width, Control body)
    {
        var headerBar = new Border
        {
            Height = HeaderH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0),
            Child = new TextBlock { Text = title, FontSize = NotaType.DeviceName, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center },
        };
        var stack = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        stack.Children.Add(headerBar);
        var bodyHost = new Border { Padding = new Thickness(10), Child = body };
        Grid.SetRow(bodyHost, 1);
        stack.Children.Add(bodyHost);
        return new Border
        {
            Width = width, Height = CardH, Background = Raised, BorderBrush = BorderDef,
            BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Body, ClipToBounds = true, Child = stack,
        };
    }

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
        var b = new Border
        {
            Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Tile, Padding = new Thickness(10, 4), HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 9, Foreground = TextPrimary },
        };
        return b;
    }

    internal static Control BypassTag() => new Border
    {
        BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
        Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = "BYPASSED", FontSize = NotaType.RowLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.RowLabelTracking, Foreground = TextSecondary },
    };

    // A caption above a control (centered, uppercase tertiary label).
    internal static Control Labeled(string label, Control c)
    {
        var lbl = new TextBlock { Text = label.ToUpperInvariant(), FontSize = NotaType.RowLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.RowLabelTracking, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center };
        return new StackPanel { Spacing = 2, Children = { lbl, c } };
    }

    // A horizontal row of single-select chips over an integer choice (get/set).
    internal static Control ChipRow(string[] options, Func<int> get, Action<int> set)
        => Segments(options, () => Math.Clamp(get(), 0, options.Length - 1), set, out _);
}
