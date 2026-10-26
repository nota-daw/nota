// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The key picker: the chosen key named in full over a Major / Minor switch, a one-octave
// keyboard for the tonic, and a way out ("No key" for the project, "Any key" for the browser
// filter). Picking a tonic keeps the mode; switching the mode keeps the tonic. Shared by the
// transport's Key cell and the Files tab's key filter so both read the same.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Samples;

namespace Nota.App;

internal static class KeyPicker
{
    /// <summary>Opens the picker under <paramref name="anchor"/>. <paramref name="pick"/> gets the
    /// chosen key, or null for <paramref name="noneLabel"/>; <paramref name="extras"/> are
    /// shortcut buttons (label, key) shown before it — "Project key · Am".</summary>
    public static void Show(Control anchor, MusicalKey? current, string noneLabel, Action<MusicalKey?> pick,
                            IReadOnlyList<(string Label, MusicalKey Key)>? extras = null)
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        // A note, a shortcut or the way out picks and closes; Major / Minor re-picks and stays open.
        flyout.Content = Build(current, noneLabel, k => { flyout.Hide(); pick(k); }, extras, k => pick(k));
        flyout.ShowAt(anchor);
    }

    /// <summary>The picker's content alone (what <see cref="Show"/> puts in its flyout).
    /// <paramref name="remode"/> gets the key when Major / Minor changes it (default: <paramref name="choose"/>).</summary>
    internal static Control Build(MusicalKey? current, string noneLabel, Action<MusicalKey?> choose,
                                  IReadOnlyList<(string Label, MusicalKey Key)>? extras = null, Action<MusicalKey>? remode = null)
    {
        // A note (KeyMode.Note) reads as its major key here: the picker offers keys only.
        var mode = current is { Mode: KeyMode.Minor } ? KeyMode.Minor : KeyMode.Major;
        int? tonic = current?.Tonic;

        var title = new TextBlock { FontSize = NotaType.Heading, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var relative = new TextBlock { FontSize = NotaType.Caption, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var keyboard = new KeyKeyboard { HorizontalAlignment = HorizontalAlignment.Left };

        var major = new ToggleButton { Content = "Major", Classes = { "seg" } };
        var minor = new ToggleButton { Content = "Minor", Classes = { "seg" } };
        void Paint()
        {
            keyboard.Mode = mode; keyboard.Tonic = tonic;
            major.IsChecked = mode == KeyMode.Major; minor.IsChecked = mode == KeyMode.Minor;
            if (tonic is int t)
            {
                var key = new MusicalKey(t, mode);
                title.Text = key.Long; title.Foreground = NotaPalette.TextPrimary;
                relative.Text = key.Relative is { } r ? $"Relative {r.Long}" : "";
            }
            else
            {
                title.Text = noneLabel; title.Foreground = NotaPalette.TextSecondary;
                relative.Text = "Pick a note for the key";
            }
        }
        void SetMode(KeyMode m)
        {
            if (mode == m) { Paint(); return; }
            mode = m;
            Paint();                                               // the key names follow the mode
            if (tonic is int t)                                    // keeps the tonic
            {
                var k = new MusicalKey(t, mode);
                if (remode is not null) remode(k); else choose(k);
            }
        }
        major.Click += (_, _) => SetMode(KeyMode.Major);
        minor.Click += (_, _) => SetMode(KeyMode.Minor);
        keyboard.Picked += t => { tonic = t; Paint(); choose(new MusicalKey(t, mode)); };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        head.Children.Add(new StackPanel { Spacing = 1, Children = { title, relative } });
        var modes = new Border
        {
            Classes = { "segmented" }, VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { major, minor } },
        };
        Grid.SetColumn(modes, 1); head.Children.Add(modes);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (label, key) in extras ?? Array.Empty<(string, MusicalKey)>())
        {
            var b = new Button { Content = label, Classes = { "ghost" } };
            b.Click += (_, _) => choose(key);
            actions.Children.Add(b);
        }
        var none = new Button { Content = noneLabel, Classes = { "ghost" } };
        none.Click += (_, _) => choose(null);
        actions.Children.Add(none);

        Paint();
        return new StackPanel
        {
            Spacing = 10, Margin = new Thickness(4), Width = KeyKeyboard.W,
            Children = { head, keyboard, actions },
        };
    }
}

/// <summary>One octave, C to B: seven white keys with three or two black keys between them.
/// The picked tonic is the accent; each key carries its name, spelled for the mode (Db major,
/// C# minor). Click picks.</summary>
internal sealed class KeyKeyboard : Control
{
    internal const double WhiteW = 38, WhiteH = 64, BlackW = 24, BlackH = 38, Gap = 2;
    internal const double W = 7 * WhiteW + 6 * Gap;
    private static readonly int[] Whites = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] Blacks = { 1, 3, 6, 8, 10 };

    private KeyMode _mode = KeyMode.Major;
    private int? _tonic;
    private int _hover = -1;

    public event Action<int>? Picked;

    public KeyMode Mode { get => _mode; set { _mode = value; InvalidateVisual(); } }
    public int? Tonic { get => _tonic; set { _tonic = value; InvalidateVisual(); } }

    public KeyKeyboard()
    {
        Width = W; Height = WhiteH;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private static Rect KeyRect(int pc)
    {
        int wi = Array.IndexOf(Whites, pc);
        if (wi >= 0) return new Rect(wi * (WhiteW + Gap), 0, WhiteW, WhiteH);
        int left = Array.IndexOf(Whites, pc - 1);   // a black key sits over the gap after its lower white
        double x = (left + 1) * (WhiteW + Gap) - Gap / 2 - BlackW / 2;
        return new Rect(x, 0, BlackW, BlackH);
    }

    private static int HitTest(Point p)
    {
        foreach (int pc in Blacks) if (KeyRect(pc).Contains(p)) return pc;   // black keys lie on top
        foreach (int pc in Whites) if (KeyRect(pc).Contains(p)) return pc;
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int h = HitTest(e.GetPosition(this));
        if (h != _hover) { _hover = h; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover != -1) { _hover = -1; InvalidateVisual(); }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int pc = HitTest(e.GetPosition(this));
        if (pc < 0) return;
        e.Handled = true;
        Picked?.Invoke(pc);
    }

    public override void Render(DrawingContext ctx)
    {
        foreach (int pc in Whites) DrawKey(ctx, pc, black: false);
        foreach (int pc in Blacks) DrawKey(ctx, pc, black: true);
    }

    private void DrawKey(DrawingContext ctx, int pc, bool black)
    {
        var r = KeyRect(pc);
        bool on = _tonic == pc, hover = _hover == pc;
        IBrush fill = on ? NotaPalette.Accent
            : black ? (hover ? NotaPalette.SurfaceActive : NotaPalette.BgSunken)
            : hover ? NotaPalette.SurfaceHover : NotaPalette.SurfaceRaised;
        ctx.DrawRectangle(fill, new Pen(on ? NotaPalette.Accent : NotaPalette.BorderDefault, 1),
                          new RoundedRect(r.Deflate(0.5), NotaRadius.Bottom(NotaRadius.FieldValue)));
        string name = new MusicalKey(pc, _mode).TonicName;
        var ink = on ? NotaPalette.TextOnAccent : black ? NotaPalette.TextSecondary : NotaPalette.TextPrimary;
        var ft = new NotaFormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                       on ? NotaFonts.SansSemiBold : NotaFonts.SansMedium,
                                       black ? NotaType.Eyebrow : NotaType.Caption, ink);
        ctx.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Bottom - ft.Height - (black ? 5 : 7)));
    }
}
