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
    internal static readonly IBrush AccentSubtleB = new SolidColorBrush(Color.FromArgb(0x28, 0xD8, 0xA0, 0x3D));

    internal static string Pct(float v) => $"{(int)Math.Round(v * 100)}%";

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // Octave convention matches PianoRollView (pitch/12 - 1), so a note's label is the
    // same the user draws in the piano roll (MIDI 36 = "C2"). Shared by the Sampler root
    // readout and the Drum Rack pad labels.
    internal static string NoteName(int note) => $"{NoteNames[((note % 12) + 12) % 12]}{note / 12 - 1}";

    // A labeled rotary knob cell (mockup style): knob, value readout, caption below.
    internal static Control KnobCell(string name, Knob knob, TextBlock value, double cellW = 58)
    {
        knob.HorizontalAlignment = HorizontalAlignment.Center;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.TextAlignment = TextAlignment.Center;
        if (cellW < 52) value.FontSize = 7;
        var lbl = new TextBlock
        {
            Text = name.ToUpperInvariant(), FontSize = cellW < 52 ? 7 : 8, Foreground = TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = cellW - 2,
        };
        return new StackPanel { Width = cellW, Spacing = 1, Children = { knob, value, lbl } };
    }

    internal static Border SimpleCard(string title, double width, Control body)
    {
        var headerBar = new Border
        {
            Height = 26, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0),
            Child = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center },
        };
        var stack = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        stack.Children.Add(headerBar);
        var bodyHost = new Border { Padding = new Thickness(10), Child = body };
        Grid.SetRow(bodyHost, 1);
        stack.Children.Add(bodyHost);
        return new Border
        {
            Width = width, Height = CardH, Background = Raised, BorderBrush = BorderDef,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = stack,
        };
    }

    internal static Control Hint(string text) => new TextBlock
    {
        Text = text, FontSize = 11, Foreground = TextTertiary,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0),
    };

    internal static TextBlock Glyph(string glyph, bool enabled, Action onClick)
    {
        var t = new TextBlock
        {
            Text = glyph, FontSize = 10, Foreground = enabled ? TextSecondary : TextDisabled,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
        };
        if (enabled) t.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return t;
    }

    internal static Border TextButton(string text)
    {
        var b = new Border
        {
            Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 4), HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = TextPrimary },
        };
        return b;
    }

    internal static Control BypassTag() => new Border
    {
        BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
        Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = "BYPASSED", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextSecondary },
    };

    // A caption above a control (centered, uppercase tertiary label).
    internal static Control Labeled(string label, Control c)
    {
        var lbl = new TextBlock { Text = label.ToUpperInvariant(), FontSize = 8, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center };
        return new StackPanel { Spacing = 2, Children = { lbl, c } };
    }

    // A horizontal row of single-select chips over an integer choice (get/set).
    internal static Control ChipRow(string[] options, Func<int> get, Action<int> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        var chips = new Border[options.Length];
        void Sync() { int cur = Math.Clamp(get(), 0, options.Length - 1); for (int i = 0; i < chips.Length; i++) { bool on = i == cur; chips[i].Background = on ? Brass : Card2; ((TextBlock)chips[i].Child!).Foreground = on ? OnAccent : TextSecondary; } }
        for (int i = 0; i < options.Length; i++)
        {
            int vi = i;
            var chip = new Border
            {
                Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = options[i], FontSize = 9, Foreground = TextSecondary },
            };
            chip.PointerPressed += (_, _) => { set(vi); Sync(); };
            chips[i] = chip; row.Children.Add(chip);
        }
        Sync();
        return row;
    }
}
