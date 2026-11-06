// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Appearance → Accent (spec 084, #311): a row of preset swatches, then Custom,
// which opens a colour picker. One accent for the whole app, worn in whichever theme is
// active and tuned to it (Nota.Application.AccentColours). A pick applies at once and is
// saved; in the picker every accepted step applies live, closing keeps it, Esc puts back the
// accent worn before it opened. A colour that cannot be readable in every theme is shown
// with the reason and never worn. Layout and copy: specs/084 contracts/ui-contract.md.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Automation;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private const double SwatchSize = 22, SwatchFill = 14;

    private static Control AccentPicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var rings = new List<(string Id, Border Ring, Border Fill)>();

        // The colour a choice wears in the theme on screen.
        static IBrush Worn(AccentChoice c) =>
            new ImmutableSolidColorBrush(NotaAccent.AccentIn(NotaPalette.Theme ?? NotaThemes.Default, c));

        void Choose(AccentChoice c)
        {
            if (c == NotaThemeService.Accent) return;
            Accent.Store(settings.Current, c);
            settings.Save();
            NotaThemeService.SetAccent(c);
        }

        foreach (var preset in Accent.Presets)
        {
            var choice = new AccentChoice(preset.Id);
            var fill = new Border { Width = SwatchFill, Height = SwatchFill, CornerRadius = NotaRadius.Cell };
            var ring = Ring(fill, SwatchSize);
            ToolTip.SetTip(ring, AccentName(preset.Id));
            // Reachable from the keyboard too: Tab to a swatch, Return or Space picks it, the
            // arrows move along the row. Screen readers read its name.
            ring.Focusable = true;
            ring.IsTabStop = true;
            AutomationProperties.SetName(ring, AccentName(preset.Id));
            ring.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(ring).Properties.IsLeftButtonPressed) return;
                ring.Focus(NavigationMethod.Pointer);
                Choose(choice);
                e.Handled = true;
            };
            int index = rings.Count;
            ring.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space) { Choose(choice); e.Handled = true; }
                else if (e.Key is Key.Left or Key.Right)
                {
                    int next = index + (e.Key == Key.Left ? -1 : 1);
                    if (next >= 0 && next < rings.Count) { rings[next].Ring.Focus(NavigationMethod.Directional); e.Handled = true; }
                }
            };
            rings.Add((preset.Id, ring, fill));
            row.Children.Add(ring);
        }

        // Custom: a button with the custom colour's chip; its ring marks it chosen like a swatch.
        var customChip = new Border { Width = SwatchFill, Height = SwatchFill, CornerRadius = NotaRadius.Cell };
        var custom = new Button
        {
            Height = 22, FontSize = 12, CornerRadius = NotaRadius.Group, Padding = new Thickness(4, 0, 8, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    customChip,
                    new TextBlock { Text = Loc.T("preferences.accent_custom"), VerticalAlignment = VerticalAlignment.Center },
                    new Glyph(GlyphKind.Edit, 10) { Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        var customRing = Ring(custom, null);
        customRing.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(customRing);

        var readout = new TextBlock { FontSize = 12, Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var readoutHex = new TextBlock { FontSize = 12, Foreground = TextMuted, FontFamily = NotaFonts.UiFamily, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { readout, readoutHex } });

        void Show()
        {
            var now = NotaThemeService.Accent;
            foreach (var (id, ring, fill) in rings)
            {
                fill.Background = Worn(new AccentChoice(id));
                // Chosen: the primary ink. Hovered or focused: the rule. Colour only, never size.
                ring.BorderBrush = id == now.Preset ? NotaPalette.TextPrimary
                    : ring.IsPointerOver || ring.IsFocused ? NotaPalette.Rule : Brushes.Transparent;
            }
            var remembered = now.Custom ?? Accent.RememberedCustom(settings.Current);
            customChip.Background = remembered is { } rgb ? Worn(new AccentChoice(Accent.Custom, rgb)) : NotaPalette.SurfaceHover;
            customRing.BorderBrush = now.Preset == Accent.Custom ? NotaPalette.TextPrimary : Brushes.Transparent;
            bool isCustom = now.Preset == Accent.Custom && now.Custom is not null;
            readout.Text = isCustom ? Loc.T("preferences.accent_custom") + " · " : AccentName(now.Preset);
            readoutHex.Text = isCustom ? Accent.Hex(now.Custom!.Value) : "";
        }

        // Hover and keyboard focus show the ring slot in the rule colour.
        foreach (var (_, ring, _) in rings)
        {
            ring.PointerEntered += (_, _) => Show();
            ring.PointerExited += (_, _) => Show();
            ring.GotFocus += (_, _) => Show();
            ring.LostFocus += (_, _) => Show();
        }

        custom.Click += (_, _) => OpenCustomPicker(custom, settings);

        Show();
        var listening = false;
        void OnChanged() => Show();
        row.AttachedToVisualTree += (_, _) => { Show(); if (!listening) { listening = true; NotaThemeService.Changed += OnChanged; } };
        row.DetachedFromVisualTree += (_, _) => { if (listening) { listening = false; NotaThemeService.Changed -= OnChanged; } };
        return row;
    }

    // A 2 px ring slot around a swatch or the Custom button: transparent at rest, so choosing
    // changes only its colour.
    private static Border Ring(Control child, double? size)
    {
        var ring = new Border
        {
            BorderThickness = new Thickness(2), CornerRadius = NotaRadius.Group, Padding = new Thickness(size is null ? 1 : 2),
            BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Child = child,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (size is { } s) { ring.Width = s; ring.Height = s; ring.Cursor = new Cursor(StandardCursorType.Hand); }
        return ring;
    }

    // The Custom popover: spectrum and hue, before/after chip and hex, how each theme would wear
    // the colour, and a note.
    private static void OpenCustomPicker(Button anchor, ISettingsService settings)
    {
        var (_, flyout, focus) = CustomPicker(settings);
        flyout.ShowAt(anchor);
        Dispatcher.UIThread.Post(focus, DispatcherPriority.Input);
    }

    // The popover's content and behaviour, apart from where it opens (a headless capture
    // puts the body in a window of its own).
    private static (Control Body, Flyout Flyout, Action Focus) CustomPicker(ISettingsService settings)
    {
        var before = NotaThemeService.Accent;
        var active = NotaPalette.Theme ?? NotaThemes.Default;
        // The last custom colour is offered again (US2-5); the worn accent only when there is none.
        uint start = before.Custom ?? Accent.RememberedCustom(settings.Current) ?? NotaAccent.ToRgb(NotaAccent.AccentIn(active, before));
        uint? accepted = null;
        bool cancelled = false, typing = false;
        string shown = Accent.Hex(start);   // what the hex field holds; TextChanged can arrive late

        var picker = new ColourPicker();
        var oldHalf = new Border { Width = 18, Background = new ImmutableSolidColorBrush(NotaAccent.AccentIn(active, before)) };
        var newHalf = new Border { Width = 18 };
        ToolTip.SetTip(oldHalf, Loc.T("preferences.accent_before"));
        var chip = new Border
        {
            Height = 22, CornerRadius = NotaRadius.Field, BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(1), ClipToBounds = true,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { oldHalf, newHalf } },
        };
        var hex = new TextBox { Classes = { "field" }, FontSize = 12, FontFamily = NotaFonts.UiFamily, Height = 22, MinHeight = 22, Width = 216 - 38 - 6, MaxLength = 7 };
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { chip, hex } };

        var themes = NotaThemes.BuiltIn;
        var strip = new UniformGrid { Columns = themes.Count, Width = ColourPicker.W };
        var cells = new List<(Border Fill, Ellipse Dot)>();
        foreach (var t in themes)
        {
            var fill = new Border { Height = 14, CornerRadius = NotaRadius.Cell, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(1) };
            var dot = new Ellipse { Width = 4, Height = 4, Fill = NotaPalette.TextPrimary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0), IsVisible = false };
            var cell = new StackPanel
            {
                Spacing = 3, Margin = new Thickness(2, 0),
                Children =
                {
                    new Grid { Children = { fill, dot } },
                    new TextBlock { Text = t.Name.Split(' ').Last(), FontSize = 9, Foreground = TextMuted, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            };
            ToolTip.SetTip(cell, t.Name);
            cells.Add((fill, dot));
            strip.Children.Add(cell);
        }
        var note = new TextBlock { FontSize = 11, LineHeight = 15, Height = 30, Width = ColourPicker.W, TextWrapping = TextWrapping.Wrap, Foreground = TextMuted };

        var body = new StackPanel
        {
            Spacing = 8, Width = ColourPicker.W,
            Children =
            {
                picker, hexRow,
                new TextBlock { Text = Loc.T("preferences.accent_in_themes"), Classes = { "CellLabel" } },
                strip, note,
            },
        };
        var flyout = new Flyout { Content = body, Placement = PlacementMode.BottomEdgeAlignedLeft };

        void Try(uint rgb)
        {
            var choice = new AccentChoice(Accent.Custom, rgb);
            newHalf.Background = new ImmutableSolidColorBrush(NotaAccent.FromRgb(rgb));
            shown = Accent.Hex(rgb);
            if (!typing) hex.Text = shown;
            var results = NotaAccent.Preview(choice).ToList();
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i].Result;
                cells[i].Fill.Background = r.Tokens is { } tk ? new ImmutableSolidColorBrush(NotaAccent.FromRgb(tk["Accent"])) : NotaPalette.BgSunken;
                cells[i].Dot.IsVisible = r.Adjusted;
            }
            var refused = results.Where(r => r.Result.Refusal is not null).ToList();
            note.Foreground = refused.Count > 0 ? NotaPalette.Danger : TextMuted;
            note.Text = refused.Count > 0 ? Refusal(refused) : Adjusted(results.Where(r => r.Result.Adjusted).Select(r => r.Theme.Name).ToList(), results.Count);
            if (refused.Count > 0) return;
            accepted = rgb;
            NotaThemeService.SetAccent(choice);
        }

        picker.ColourChanged += Try;
        hex.TextChanged += (_, _) =>
        {
            if (typing || !Accent.TryParseHex(hex.Text, out var rgb) || (hex.Text ?? "").Trim().TrimStart('#').Length is not (3 or 6)
                || Accent.Hex(rgb) == shown) return;
            typing = true;
            picker.Set(rgb);
            Try(rgb);
            typing = false;
        };
        // Return keeps the colour in the field — also the remembered one the picker opened on,
        // which nothing has applied yet. A refused colour is not worn; closing keeps the last
        // colour that was accepted, or puts back the accent from before.
        hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (Accent.TryParseHex(hex.Text, out var rgb) && accepted != rgb)
            {
                typing = true;
                Try(rgb);
                typing = false;
            }
            flyout.Hide();
            e.Handled = true;
        };
        // Esc: back to what was worn before, and close. Caught on the way down so the
        // flyout's own Esc handling cannot close it first.
        body.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            cancelled = true;
            flyout.Hide();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        flyout.Closed += (_, _) =>
        {
            if (cancelled || accepted is null) { NotaThemeService.SetAccent(before); return; }
            var c = new AccentChoice(Accent.Custom, accepted.Value);
            Accent.Store(settings.Current, c);
            settings.Save();
            NotaThemeService.SetAccent(c);
        };

        picker.Set(start);
        typing = true;
        hex.Text = Accent.Hex(start);
        typing = false;
        newHalf.Background = oldHalf.Background;
        // Opening shows how the current colour sits in each theme, without wearing anything new.
        var preview = NotaAccent.Preview(new AccentChoice(Accent.Custom, start)).ToList();
        for (int i = 0; i < preview.Count; i++)
        {
            var r = preview[i].Result;
            cells[i].Fill.Background = r.Tokens is { } tk ? new ImmutableSolidColorBrush(NotaAccent.FromRgb(tk["Accent"])) : NotaPalette.BgSunken;
            cells[i].Dot.IsVisible = r.Adjusted;
        }
        note.Text = before.Preset == Accent.Custom
            ? Adjusted(preview.Where(r => r.Result.Adjusted).Select(r => r.Theme.Name).ToList(), preview.Count)
            : Loc.T("preferences.accent_note_pick");
        return (body, flyout, () => { hex.SelectAll(); hex.Focus(); });
    }

    // The preset's name (spec 084 contracts § Preset names).
    private static string AccentName(string id) => id switch
    {
        "lime" => Loc.T("accent.lime"),
        "aqua" => Loc.T("accent.aqua"),
        "sky" => Loc.T("accent.sky"),
        "violet" => Loc.T("accent.violet"),
        "magenta" => Loc.T("accent.magenta"),
        "pink" => Loc.T("accent.pink"),
        _ => Loc.T("accent.orange"),
    };

    // The state colour a refused custom colour sits on, as the note names it.
    private static string ClashName(string? token) =>
        token is "Record" ? Loc.T("preferences.accent_clash_record")
        : token is "Mute" ? Loc.T("preferences.accent_clash_mute")
        : token is "Solo" ? Loc.T("preferences.accent_clash_solo")
        : Loc.T("preferences.accent_clash_meter");

    private static string Adjusted(List<string> themes, int all) =>
        themes.Count == 0 ? Loc.T("preferences.accent_note_as_is")
        : themes.Count == all ? Loc.T("preferences.accent_note_adjusted_all")
        : Loc.F("preferences.accent_note_adjusted", string.Join(", ", themes));

    private static string Refusal(List<(NotaColourTheme Theme, AccentResult Result)> refused)
    {
        var first = refused[0].Result.Refusal!;
        var where = string.Join(", ", refused.Select(r => r.Theme.Name));
        return first.Reason switch
        {
            AccentRefusalReason.Grey => Loc.T("preferences.accent_note_grey"),
            AccentRefusalReason.Clash => Loc.F("preferences.accent_note_clash", ClashName(first.ClashWith), where),
            _ => Loc.F("preferences.accent_note_unreadable", where),
        };
    }
}
