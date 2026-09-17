// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The 200px clip props rail beside the piano roll (mockup 1e): clip name,
// Start/Length in bars.beats.16ths (mono), a Loop chip, a grid picker with a
// one-tap Quantize + strength, a transpose stepper and a live selection line.
// Edits drive the PianoRollView (grid / quantize / transpose); read-outs follow
// its Changed event.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed class ClipPropsView : UserControl
{
    private static readonly IBrush Panel = NotaPalette.SurfaceCard;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush AccentSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;

    // Grid steps in beats (4/4, one beat = a 1/4 note). Straight divisions then triplets.
    private static readonly double[] GridValues =
        { 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625, 2.0 / 3, 1.0 / 3, 1.0 / 6, 1.0 / 12 };
    private static readonly string[] GridLabels =
        { "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64", "1/4T", "1/8T", "1/16T", "1/32T" };

    private readonly PianoRollView _roll;
    private readonly TextBlock _startText;
    private readonly TextBlock _lengthText;
    private readonly TextBlock _loopText;
    private readonly TextBlock _selInfo;
    private readonly TextBlock _transposeText;
    private readonly TextBlock _gridText;
    private int _gridIndex = 4;   // 1/16
    private int _transpose;
    private double _strength = 0.8;

    public ClipPropsView(PianoRollView roll, string clipName, double startBeat)
    {
        _roll = roll;
        Width = 200;
        Background = Panel;
        BorderBrush = BorderDef;
        BorderThickness = new Thickness(0, 0, 1, 0);

        _startText = Mono(Position(startBeat));
        _lengthText = Mono(Duration(roll.LengthBeats));
        _loopText = Mono($"{roll.LengthBeats:0.#}b");
        _loopText.Foreground = AccentBright;
        _selInfo = new TextBlock { Text = roll.SelectionInfo, FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap };
        _transposeText = Mono("0\u2009st");
        _gridText = new TextBlock { Text = GridLabels[_gridIndex], FontSize = 10, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        body.Children.Add(Section("CLIP", ReadoutBox(clipName, TextPrimary, 24, 11, false)));

        var startLen = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        var startBox = FieldBox(22); startBox.Child = _startText;
        startLen.Children.Add(Labeled("START", startBox));
        var lenBox = FieldBox(22); lenBox.Child = _lengthText;
        var lenWrap = Labeled("LENGTH", lenBox);
        Grid.SetColumn(lenWrap, 1);
        startLen.Children.Add(lenWrap);
        body.Children.Add(startLen);

        // Loop chip (clips loop by default in this model).
        body.Children.Add(new Border
        {
            Height = 26, Background = AccentSubtle, BorderBrush = Brass, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children = { new TextBlock { Text = "Loop", FontSize = 11, Foreground = AccentBright, VerticalAlignment = VerticalAlignment.Center }, _loopText },
            },
        });

        // Grid + quantize.
        var gridChip = Chip(Glyph.WithChevron(_gridText));
        gridChip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            var f = new MenuFlyout();
            for (int i = 0; i < GridValues.Length; i++)
            {
                int idx = i;
                var mi = new MenuItem { Header = GridLabels[i] };
                if (i == _gridIndex) mi.Icon = new TextBlock { Text = "•", Foreground = AccentBright };
                mi.Click += (_, _) =>
                {
                    _gridIndex = idx;
                    _gridText.Text = GridLabels[idx];
                    _roll.Grid = GridValues[idx];
                };
                f.Items.Add(mi);
            }
            f.ShowAt(gridChip);
        };
        var quantize = Chip(new TextBlock { Text = "Quantize", FontSize = 10, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        quantize.PointerPressed += (_, _) => _roll.Quantize(_strength);
        var gridRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        gridRow.Children.Add(gridChip);
        Grid.SetColumn(quantize, 1); gridRow.Children.Add(quantize);

        var strengthVal = Mono("80%"); strengthVal.Foreground = TextSecondary;
        var strengthBar = new MiniFader(_strength, 1.0) { VerticalAlignment = VerticalAlignment.Center };
        strengthBar.ValueChanged += v => { _strength = v; strengthVal.Text = $"{v * 100:0}\u2009%"; };
        var strengthRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 2, 0, 0) };
        strengthRow.Children.Add(new TextBlock { Text = "STRENGTH", FontSize = 9, Foreground = TextTertiary, Width = 52, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(strengthBar, 1); strengthRow.Children.Add(strengthBar);
        Grid.SetColumn(strengthVal, 2); strengthVal.VerticalAlignment = VerticalAlignment.Center; strengthVal.Margin = new Thickness(4, 0, 0, 0); strengthRow.Children.Add(strengthVal);

        body.Children.Add(Section("GRID / QUANTIZE", new StackPanel { Spacing = 4, Children = { gridRow, strengthRow } }));

        // Transpose stepper.
        var minus = StepBtn("−", () => Transpose(-1));
        var plus = StepBtn("+", () => Transpose(1));
        var stepper = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        stepper.Children.Add(minus);
        var tBox = FieldBox(22); tBox.Child = _transposeText;
        Grid.SetColumn(tBox, 1); stepper.Children.Add(tBox);
        Grid.SetColumn(plus, 2); stepper.Children.Add(plus);
        body.Children.Add(Section("TRANSPOSE", stepper));

        _selInfo.Margin = new Thickness(0, 8, 0, 0);
        var hint = new TextBlock { Text = "Double-click = add · drag edge = length", FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
        body.Children.Add(_selInfo);
        body.Children.Add(hint);
        Content = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        _roll.Changed += OnRollChanged;
    }

    /// <summary>Re-points the START read-out after the clip moved or was trimmed from the
    /// left in the arrangement. LENGTH / LOOP follow the roll's own Changed event.</summary>
    public void SetStart(double startBeat) => _startText.Text = Position(startBeat);

    private void OnRollChanged()
    {
        _selInfo.Text = _roll.SelectionInfo;
        _lengthText.Text = Duration(_roll.LengthBeats);
        _loopText.Text = $"{_roll.LengthBeats:0.#}b";
    }

    private void Transpose(int d)
    {
        _transpose += d;
        _transposeText.Text = $"{_transpose}\u2009st";
        _roll.TransposeBy(d);
    }

    // --- helpers ----------------------------------------------------------

    private static TextBlock Mono(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 10, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        t.BindResource(FontFamilyProperty, "Font.Mono");
        return t;
    }

    private static Control Section(string title, Control content) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TextTertiary },
            content,
        },
    };

    private static Control Labeled(string title, Control content) => new StackPanel
    {
        Spacing = 2,
        Children = { new TextBlock { Text = title, FontSize = 9, Foreground = TextTertiary }, content },
    };

    private static Border FieldBox(double h) => new()
    {
        Height = h, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
    };

    private static Border ReadoutBox(string text, IBrush fg, double h, double fs, bool mono)
    {
        var t = new TextBlock { Text = text, FontSize = fs, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(mono ? 7 : 8, 0, 0, 0) };
        if (mono) t.BindResource(FontFamilyProperty, "Font.Mono");
        var box = FieldBox(h);
        box.Child = t;
        return box;
    }

    private static Border Chip(Control child) => new()
    {
        Height = 22, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
        Cursor = new Cursor(StandardCursorType.Hand), Child = child,
    };

    private static Border StepBtn(string glyph, Action onClick)
    {
        var b = new Border
        {
            Width = 22, Height = 22, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = glyph, FontSize = 11, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    // bars.beats.16ths (4/4), 1-based position.
    private static string Position(double beat)
    {
        int bar = (int)(beat / 4) + 1, be = (int)(beat % 4) + 1, six = (int)Math.Round(beat % 1 * 4) + 1;
        return string.Format(NotaNum.Culture, "{0}. {1}. {2}", bar, be, six);
    }

    // bars.beats.16ths as a duration (0-based).
    private static string Duration(double beats)
    {
        int bars = (int)(beats / 4), be = (int)(beats % 4), six = (int)Math.Round(beats % 1 * 4);
        return string.Format(NotaNum.Culture, "{0}. {1}. {2}", bars, be, six);
    }
}
