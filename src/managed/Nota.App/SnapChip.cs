// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The snap control of an editor (specs/003-adaptive-snap-grid, contracts/grid-ui.md): shows
// the step in effect — live while zooming in Auto — and opens one menu with every grid
// option instead of cycling. The arrangement wears it as the toolbar's GRID cell (value on
// top, AUTO GRID / GRID caption below, so the cell stays narrow); the piano roll as a header
// chip reading "Auto · 1/16". A fixed step finer than the drawn lines dims; snap off strikes
// the value through. Both read and write EditorGrids, so the shortcuts and the magnet latch
// stay in step with it.

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Editing;

namespace Nota.App;

internal sealed class SnapChip
{
    private const string Shortcuts = "⌘1 ⌘2 narrow / widen · ⌘3 triplets · ⌘4 snap · ⌘5 auto";

    private readonly Button _button;
    private readonly TextBlock _value;
    private readonly TextBlock? _caption;
    private readonly EditorKind _kind;
    private readonly Func<ResolvedGrid> _current;
    private string? _shown;

    private static EditorGrids Grids => EditorGrids.Shared;

    private SnapChip(Button button, TextBlock value, TextBlock? caption, EditorKind kind, Func<ResolvedGrid> current)
    {
        _button = button; _value = value; _caption = caption; _kind = kind; _current = current;
        button.Click += (_, _) => OpenMenu();
        // Subscribed only while on screen: piano-roll chips come and go with every clip.
        void OnChanged(EditorKind k) { if (k == _kind) Refresh(); }
        button.AttachedToVisualTree += (_, _) => { Grids.Changed += OnChanged; FitWidest(); Refresh(); };
        button.DetachedFromVisualTree += (_, _) => Grids.Changed -= OnChanged;
        Refresh();
    }

    /// <summary>Drives an existing two-line toolbar cell (the arrangement's GRID cell).</summary>
    public static SnapChip Attach(Button button, TextBlock value, TextBlock caption, EditorKind kind, Func<ResolvedGrid> current)
        => new(button, value, caption, kind, current);

    /// <summary>A header chip (the piano roll's), reading "Auto · 1/16".</summary>
    public static (Button Button, SnapChip Chip) Create(EditorKind kind, Func<ResolvedGrid> current)
    {
        var value = new TextBlock
        {
            FontFamily = NotaFonts.MonoFamily, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        // A grid glyph names the control and a chevron says it opens a menu, like the other
        // header dropdowns — the value alone read as a static label.
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 7,
            Children =
            {
                new Glyph(GlyphKind.Grid, 11) { Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center },
                value,
                new Glyph(GlyphKind.ChevronDown, 8) { Foreground = NotaPalette.TextDisabled, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var b = new Button { Classes = { "chip" }, Padding = new Avalonia.Thickness(10, 0), Content = content };
        return (b, new SnapChip(b, value, null, kind, current));
    }

    // The value keeps the width of its longest possible label ("Auto · 4 bars", "1/16T"), in
    // its own font, so the toolbar and the header never shift while zooming.
    private void FitWidest()
    {
        var face = new Typeface(_value.FontFamily, _value.FontStyle, _value.FontWeight);
        double widest = 0;
        for (int i = 0; i < GridStep.Count; i++)
            foreach (bool t in new[] { false, true })
            {
                string label = GridStep.Of(i, t).Label;
                if (_caption is null) label = "Auto · " + label;
                var ft = new FormattedText(label, NotaNum.Culture,
                    FlowDirection.LeftToRight, face, _value.FontSize, null);
                widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace);
            }
        _value.MinWidth = Math.Ceiling(widest) + 2;
    }

    /// <summary>Relabels from the live setting and zoom; cheap, and a no-op when nothing changed.</summary>
    public void Refresh()
    {
        var s = Grids.Get(_kind);
        var r = _current();
        string text = _caption is null ? SnapGrid.Label(s, r) : r.Snap.Label;
        string key = $"{text}|{s.Adaptive}|{r.SnapOn}|{r.FinerThanVisible}";
        if (key == _shown) return;
        _shown = key;

        _value.Text = text;
        if (_caption is not null) _caption.Text = s.Adaptive ? "Auto grid" : "Grid";
        _value.TextDecorations = r.SnapOn ? null : TextDecorations.Strikethrough;
        _value.Foreground = !r.SnapOn ? NotaPalette.TextDisabled
            : r.FinerThanVisible ? NotaPalette.TextSecondary
            : NotaPalette.TextPrimary;
        string tip = !r.SnapOn ? "Snap off — items move freely (⌘4)"
            : r.FinerThanVisible ? $"{r.Snap.Label} — finer than the visible grid · zoom in to see it"
            : s.Adaptive ? $"Snap grid · Auto follows the zoom ({s.Density})"
            : $"Snap grid · fixed {r.Snap.Label}";
        ToolTip.SetTip(_button, MenuKit.Keys($"{tip}\n{Shortcuts} · hold ⌥ to drag freely"));
    }

    private void OpenMenu()
    {
        var s = Grids.Get(_kind);
        var f = new MenuFlyout();
        void Item(string header, bool on, Func<GridSetting, GridSetting> apply, KeyGesture? gesture = null)
        {
            var mi = new MenuItem { Header = header, InputGesture = gesture };
            mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = on;
            mi.Click += (_, _) => Grids.Set(_kind, apply(Grids.Get(_kind)));
            f.Items.Add(mi);
        }

        foreach (var d in new[] { GridDensity.Narrow, GridDensity.Medium, GridDensity.Wide })
            Item($"Auto · {d}", s.Adaptive && s.Density == d, g => SnapGrid.PickAdaptive(g, d));
        f.Items.Add(new Separator());
        for (int i = 0; i < GridStep.Count; i++)
        {
            int idx = i;
            Item(GridStep.Of(i, s.Triplets).Label, !s.Adaptive && s.FixedIndex == i, g => SnapGrid.PickFixed(g, idx));
        }
        f.Items.Add(new Separator());
        Item("Triplets", s.Triplets, SnapGrid.ToggleTriplets, MenuKit.Cmd(Key.D3));
        Item("Snap to grid", s.SnapOn, SnapGrid.ToggleSnap, MenuKit.Cmd(Key.D4));
        f.ShowAt(_button);
    }
}
