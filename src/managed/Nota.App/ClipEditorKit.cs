// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shared shell pieces of the clip editor (nota-design "Nota Clip Editor"): one frame for
// MIDI and audio — a 38px header whose first 232px is the clip cell (track-colour bar,
// name, kind badge), mode segments after it, and a 232px inspector on the left whose
// sections are split by hairlines and end in a single hint line. Everything here is the
// shell scale (11px words, 10px caps section labels); the theme's Button / .primary /
// ToggleButton.seg carry the states.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal static class ClipEditorKit
{
    public const double HeaderH = 38, InspectorW = 232, ToolsW = 272, FieldH = 26;

    // ---- header -----------------------------------------------------------

    /// <summary>The 38px header: clip cell | modes | (flex) | right-hand controls.</summary>
    public static Border Header(IBrush trackColor, string name, string kind, Control modes, Control? afterModes, Control? right)
        => Header(trackColor, name, kind, modes, afterModes, right, out _, out _);

    /// <summary>As above, handing back the name and the colour bar so a header can follow the
    /// clip it names (the multi-clip editor's active clip).</summary>
    public static Border Header(IBrush trackColor, string name, string kind, Control modes, Control? afterModes, Control? right,
                                out TextBlock nameOut, out Border colorBar)
        => Header(trackColor, name, kind, modes, afterModes, right, InspectorW, out nameOut, out colorBar);

    /// <summary>As above with the clip cell <paramref name="cellW"/> wide, or fitting the name
    /// when NaN (the piano roll, whose settings run in a strip under the header).</summary>
    public static Border Header(IBrush trackColor, string name, string kind, Control modes, Control? afterModes, Control? right,
                                double cellW, out TextBlock nameOut, out Border colorBar)
    {
        var cell = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9 };
        var nameText = new TextBlock
        {
            Text = name, FontSize = NotaType.TrackName, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        nameText.MaxWidth = double.IsNaN(cellW) ? 200 : cellW - 28 - 52;   // the kind sits right after the name
        var badge = new TextBlock
        {
            Text = kind.Length > 4 ? char.ToUpperInvariant(kind[0]) + kind[1..].ToLowerInvariant() : kind,
            FontFamily = NotaFonts.UiFamily, FontSize = NotaType.Caption,
            Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        cell.Children.Add(nameText);
        nameOut = nameText;
        Grid.SetColumn(badge, 1); cell.Children.Add(badge);
        var cellHost = new Border
        {
            Width = cellW, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new Grid
            {
                Children =
                {
                    (colorBar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = trackColor }),
                    new Border { Padding = new Thickness(14, 0, double.IsNaN(cellW) ? 16 : 14, 0), Child = cell },
                },
            },
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(double.IsNaN(cellW) ? "Auto,Auto,*,Auto,Auto" : "Auto,Auto,Auto,*,Auto"), ColumnSpacing = 12 };
        row.Children.Add(cellHost);
        modes.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(modes, 1); row.Children.Add(modes);
        if (afterModes is not null) { afterModes.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(afterModes, 2); row.Children.Add(afterModes); }
        if (right is not null) { right.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(right, 4); row.Children.Add(right); }
        return new Border
        {
            Height = HeaderH, Background = NotaPalette.Panel, BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 12, 0), Child = row,
        };
    }

    /// <summary>A 1×18 hairline between header groups.</summary>
    public static Border HeaderDivider() => new()
    {
        Width = 1, Height = 18, Background = NotaPalette.BorderDefault, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Shell segments (theme <c>Border.segmented</c> + <c>ToggleButton.seg</c>): the
    /// chosen one solid brass. Behaves as a radio group; <paramref name="set"/> repaints
    /// from outside without raising <paramref name="pick"/>.</summary>
    public static Border Segments(string[] names, int initial, Action<int> pick, out Action<int> set, bool fill = false)
    {
        var buttons = new ToggleButton[names.Length];
        Panel row = fill ? new Grid { ColumnSpacing = 2 } : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        void Paint(int idx) { for (int i = 0; i < buttons.Length; i++) buttons[i].IsChecked = i == idx; }
        for (int i = 0; i < names.Length; i++)
        {
            int iv = i;
            var b = new ToggleButton { Content = names[i], Classes = { "seg" }, Padding = new Thickness(12, 0) };
            if (fill)
            {
                ((Grid)row).ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                Grid.SetColumn(b, i);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Center;
            }
            b.Click += (_, _) => { Paint(iv); pick(iv); };
            buttons[i] = b;
            row.Children.Add(b);
        }
        Paint(initial);
        set = Paint;
        return new Border { Classes = { "segmented" }, Child = row, HorizontalAlignment = fill ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };
    }

    // ---- settings strip (piano roll) ----------------------------------------

    /// <summary>The settings row above the notes (style guide § Editor settings strip): groups
    /// left to right, split by hairlines, 40 high on the panel grey.</summary>
    public const double StripH = 40;

    public static Border Strip(params Control[] groups)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < groups.Length; i++)
        {
            if (i > 0) row.Children.Add(HeaderDivider());
            groups[i].VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(groups[i]);
        }
        // A narrow editor scrolls the strip sideways, so the trailing groups stay reachable.
        return new Border
        {
            Height = StripH, Background = NotaPalette.Panel, BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(0, 0, 0, 1), ClipToBounds = true,
            Child = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(14, 0, 12, 0), Child = row },
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
    }

    /// <summary>One strip group: its title (13 / 600) then its controls, 8 apart.</summary>
    public static StackPanel StripGroup(string title, params Control[] items)
    {
        var g = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        g.Children.Add(new TextBlock { Text = title, FontSize = NotaType.TrackName, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
        foreach (var c in items) { c.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(c); }
        return g;
    }

    /// <summary>A label in a strip or row: 12 / 400, ink 3.</summary>
    public static TextBlock StripLabel(string text) => new()
    {
        Text = text, FontSize = NotaType.Body, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };

    // ---- inspector --------------------------------------------------------

    /// <summary>The 232px inspector: scrolling sections with the hint line pinned below.</summary>
    public static Border Inspector(Control sections, TextBlock hint)
    {
        DockPanel.SetDock(hint, Dock.Bottom);
        hint.Margin = new Thickness(14, 6, 14, 12);
        return new Border
        {
            Width = InspectorW, Background = NotaPalette.Panel, BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new DockPanel
            {
                Children =
                {
                    hint,
                    new ScrollViewer
                    {
                        Content = new Border { Padding = new Thickness(14, 16, 14, 8), Child = sections },
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    },
                },
            },
        };
    }

    /// <summary>A vertical run of sections split by hairlines (gap 14 around each rule).</summary>
    public static StackPanel Sections(params Control[] sections)
    {
        var s = new StackPanel { Spacing = 14 };
        for (int i = 0; i < sections.Length; i++)
        {
            if (i > 0) s.Children.Add(Rule());
            s.Children.Add(sections[i]);
        }
        return s;
    }

    public static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder };

    /// <summary>A section title (13 / 600, ink 1), with an optional read-out on the right.</summary>
    public static Grid SectionTitle(string title, TextBlock? right = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(TitleText(title));
        if (right is not null) { Grid.SetColumn(right, 1); g.Children.Add(right); }
        return g;
    }

    public static TextBlock TitleText(string title) => new()
    {
        Text = NotaText.Label(title), FontSize = NotaType.TrackName, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A row label beside a control: 11, ink 3, <paramref name="width"/> wide.</summary>
    public static TextBlock RowLabel(string text, double width) => new()
    {
        Text = text, Width = width, FontSize = NotaType.Caption, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };

    public static StackPanel Section(string title, params Control[] body) => Section(SectionTitle(title), 8, body);

    public static StackPanel Section(Control title, double spacing, params Control[] body)
    {
        var s = new StackPanel { Spacing = spacing };
        s.Children.Add(title);
        foreach (var c in body) s.Children.Add(c);
        return s;
    }

    /// <summary>The one line of Ink 5 at the foot of the inspector.</summary>
    public static TextBlock Hint(string text) => new()
    {
        Text = text, FontSize = NotaType.Caption, LineHeight = 16, Foreground = NotaPalette.TextTertiary, TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Mono(string text, double size = 11, IBrush? fg = null) => new()
    {
        Text = text, FontFamily = NotaFonts.UiFamily, FontSize = size, Foreground = fg ?? NotaPalette.TextPrimary,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A mono read-out for a section title's right side.</summary>
    public static TextBlock Meta(string text) => Mono(text, 10, NotaPalette.TextMuted);

    public static Grid Pair(Control a, Control b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        g.Children.Add(a);
        Grid.SetColumn(b, 1); g.Children.Add(b);
        return g;
    }

    /// <summary>A small Ink 4 label above a field.</summary>
    public static StackPanel Labeled(string label, Control field) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, FontSize = NotaType.Caption, Foreground = NotaPalette.TextTertiary }, field },
    };

    // ---- fields and buttons -----------------------------------------------

    /// <summary>A 26px field on the well (style guide: no border, corner 6).</summary>
    public static Border Field(Control? child = null, double padX = 9) => new()
    {
        Height = FieldH, Background = NotaPalette.Well, CornerRadius = NotaRadius.Field,
        Padding = new Thickness(padX, 0), Child = child, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A dropdown on the well: value left, an ink 3 chevron right. Opens on a left click.</summary>
    public static Border Dropdown(TextBlock value, Action<Control> open)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14 };
        value.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(value);
        var chev = new Glyph(GlyphKind.ChevronDown, 7) { Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chev, 1); g.Children.Add(chev);
        var f = Field(g);
        f.Cursor = new Cursor(StandardCursorType.Hand);
        f.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(f).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            open(f);
        };
        return f;
    }

    public static void ShowMenu(Control anchor, string[] items, int current, Action<int> pick)
    {
        var f = new MenuFlyout();
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var mi = new MenuItem { Header = items[i] };
            mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = i == current;
            mi.Click += (_, _) => pick(idx);
            f.Items.Add(mi);
        }
        f.ShowAt(anchor);
    }

    /// <summary>A raised 26px shell button (theme <c>Button</c>).</summary>
    public static Button Button(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(11, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A 26×26 raised button carrying a glyph (transpose −/+).</summary>
    public static Button GlyphButton(GlyphKind kind, Action click)
    {
        var b = new Button { Width = FieldH, Padding = new Thickness(0), Content = new Glyph(kind, 9) };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A switch with its word to the right (shell: 11px, Ink 2). <paramref name="trailing"/>
    /// rides the far right of the row (e.g. "8 beats").</summary>
    public static Border SwitchRow(string label, Func<bool> get, Action toggle, out Action sync, Control? trailing = null, double height = FieldH)
    {
        var track = new SwitchTrack(shell: true);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8 };
        g.Children.Add(track);
        if (label.Length > 0)
        {
            var word = new TextBlock { Text = label, FontSize = NotaType.Body, Foreground = NotaPalette.TextStrong, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(word, 1); g.Children.Add(word);
        }
        else g.ColumnSpacing = 0;
        if (trailing is not null) { Grid.SetColumn(trailing, 3); g.Children.Add(trailing); }
        var host = new Border { Height = height, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = g };
        void Paint() => track.IsOn = get();
        host.PointerPressed += (_, e) =>
        {
            if (!host.IsEffectivelyEnabled || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            toggle(); Paint(); e.Handled = true;
        };
        sync = Paint;
        Paint();
        return host;
    }

    /// <summary>A sunken field with − and + zones either side of a centred mono value.</summary>
    public static Border Stepper(TextBlock value, Action dec, Action inc, double zone = 22)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{zone},*,{zone}") };
        g.Children.Add(StepZone(GlyphKind.Minus, dec));
        value.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(value, 1); g.Children.Add(value);
        var plus = StepZone(GlyphKind.Plus, inc);
        Grid.SetColumn(plus, 2); g.Children.Add(plus);
        return Field(g, 0);
    }

    private static Border StepZone(GlyphKind kind, Action click)
    {
        var glyph = new Glyph(kind, 8) { Foreground = NotaPalette.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var z = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = glyph };
        z.PointerEntered += (_, _) => glyph.Foreground = NotaPalette.TextPrimary;
        z.PointerExited += (_, _) => glyph.Foreground = NotaPalette.TextMuted;
        z.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(z).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            click();
        };
        return z;
    }

    /// <summary>A 26px key · value row on Card (the Detected read-out, a selection line).</summary>
    public static Grid KeyValue(string key, TextBlock value)
    {
        var g = new Grid { Height = FieldH, ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(new TextBlock { Text = key, FontSize = NotaType.Body, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1); g.Children.Add(value);
        return g;
    }

    /// <summary>The Card frame key · value rows sit in.</summary>
    public static Border Card(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Field, Padding = new Thickness(10, 0), Child = child,
    };

    /// <summary>A shell slider row (style guide): the label (11, ink 3) and a flat bar with its
    /// value written inside. Double-click the bar to type a value, ⌥-click to reset; `textAt`
    /// (the read-out at a position, pure) lets typing search without writing. `valueW` is kept
    /// for older call sites; the value now lives in the bar.</summary>
    public static Grid SliderRow(string label, double labelW, Func<double> norm, Action<double> setNorm, Func<string> text,
        out Action sync, Action? reset = null, double valueW = 36, Func<bool>? dim = null, Func<double, string>? textAt = null)
    {
        Action repaint = () => { };
        var track = new SliderTrack { Shell = true, Text = text, Reset = reset is null ? null : () => { reset(); repaint(); } };
        var lbl = new TextBlock
        {
            Text = label, Width = labelW, FontSize = NotaType.Caption,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        void Paint()
        {
            bool d = dim?.Invoke() ?? false;
            track.Norm = norm();
            track.IsDim = d;
            track.InvalidateVisual();
            lbl.Foreground = d ? NotaPalette.TextDisabled : NotaPalette.TextTertiary;
        }
        repaint = Paint;
        track.Changed += v => { setNorm(v); track.InvalidateVisual(); };
        Action? rowReset = reset is null ? null : () => { reset(); Paint(); };
        var field = textAt is null
            ? ValueEntry.Probing(label, norm, setNorm, text, after: Paint, reset: rowReset)
            : new ValueField(label, norm, textAt, setNorm) { After = Paint, Reset = rowReset };
        ValueEntry.Attach(track, field);
        var g = new Grid { Height = 20, ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        g.Children.Add(lbl);
        Grid.SetColumn(track, 1); g.Children.Add(track);
        sync = () => { if (!track.Dragging) Paint(); };
        Paint();
        return g;
    }

    // ---- positions --------------------------------------------------------

    /// <summary>bar.beat.16th (4/4), 1-based: 11.1.1.</summary>
    public static string Position(double beat)
    {
        int bar = (int)Math.Floor(beat / 4) + 1, be = (int)Math.Floor(beat % 4) + 1, six = (int)Math.Round(beat % 1 * 4) + 1;
        if (six > 4) { six = 1; be++; }
        if (be > 4) { be = 1; bar++; }
        return string.Format(NotaNum.Culture, "{0}.{1}.{2}", bar, be, six);
    }

    /// <summary>bars.beats.16ths as a duration (0-based): 2.0.0.</summary>
    public static string Duration(double beats)
    {
        int bars = (int)Math.Floor(beats / 4), be = (int)Math.Floor(beats % 4), six = (int)Math.Round(beats % 1 * 4);
        if (six > 3) { six = 0; be++; }
        if (be > 3) { be = 0; bars++; }
        return string.Format(NotaNum.Culture, "{0}.{1}.{2}", bars, be, six);
    }

    /// <summary>Signed semitones with the display minus: +3 st, −2 st, 0 st.</summary>
    public static string Semis(int st) => st == 0 ? "0\u2009st" : string.Format(NotaNum.Culture, "{0:+0;−0}\u2009st", st);
}
