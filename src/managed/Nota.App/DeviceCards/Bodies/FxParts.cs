// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — small builders the audio-effect bodies share under the style guide
// (spec 038): the tab row inside a device section, an upright value bar over a parameter,
// and the three text roles (row label, value, read-out). State-free like DeviceCardKit.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal static class FxParts
{
    public const double TabRowH = 22, TabGap = 16, TabSize = NotaType.DeviceTab;
    private static readonly FontWeight TabWeight = (FontWeight)580;

    /// <summary>A row label: 10.5, ink 3, sentence case.</summary>
    internal static TextBlock Label(string text, IBrush? ink = null) => new()
    {
        Text = NotaText.Label(text), FontSize = NotaType.RowLabel, Foreground = ink ?? TextTertiary,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A value: 11, ink 1.</summary>
    internal static TextBlock Value(string text = "", IBrush? ink = null) => new()
    {
        Text = text, FontSize = NotaType.KnobValue, Foreground = ink ?? TextPrimary,
        VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.UiFamily,
    };

    /// <summary>A read-out that summarises what the device is doing: 11, ink 3.</summary>
    internal static TextBlock Info(string text = "", IBrush? ink = null) => new()
    {
        Text = text, FontSize = NotaType.KnobValue, Foreground = ink ?? TextTertiary,
        VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.UiFamily,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A section title (10.5, ink 3).</summary>
    internal static TextBlock Title(string text) => Label(text);

    /// <summary>
    /// A tab row inside a device section and the body it swaps: 26 high with a thin rule under
    /// it, tabs 16 apart, the current one ink 1 at 12 / 580 over a 2px orange underline, the rest
    /// ink 3. <paramref name="extras"/> sits at the right end of the row. <paramref name="select"/>,
    /// when given, receives a setter that switches tab from code.
    /// </summary>
    internal static Control Tabs(string[] tabs, ContentControl host, Func<int, Control> body, Action<int>? changed = null,
        Control? extras = null, int initial = 0, Action<Action<int>>? select = null, Func<int, string>? tip = null)
    {
        int sel = initial;
        var texts = new TextBlock[tabs.Length];
        var lines = new Border[tabs.Length];
        void Hi()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                bool on = i == sel;
                texts[i].Foreground = on ? TextPrimary : TextTertiary;
                texts[i].FontWeight = on ? TabWeight : FontWeight.Normal;
                lines[i].Background = on ? Brass : Brushes.Transparent;
            }
        }
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = TabGap };
        void Pick(int i) { sel = i; Hi(); host.Content = body(i); changed?.Invoke(i); }
        for (int i = 0; i < tabs.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock
            {
                Text = tabs[i], FontSize = TabSize, FontWeight = TabWeight, Foreground = TextTertiary,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = NotaRadius.Fine };
            var cell = new Panel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { tb, line } };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                Pick(iv);
            };
            if (tip?.Invoke(i) is { } t) ToolTip.SetTip(cell, t);
            texts[i] = tb; lines[i] = line;
            bar.Children.Add(cell);
        }
        // The tabs are laid out first, so a long readout on the right never squeezes them; the
        // readout takes what's left and is right-aligned in it.
        var row = new DockPanel { Height = TabRowH, LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Left);
        row.Children.Add(bar);
        if (extras is not null)
        {
            extras.VerticalAlignment = VerticalAlignment.Center;
            extras.HorizontalAlignment = HorizontalAlignment.Right;
            extras.Margin = new Thickness(8, 0, 0, 2);
            if (extras is TextBlock xt) xt.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(extras);
        }
        var rule = new Border { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Bottom };
        var head = new Panel { Children = { rule, row } };
        DockPanel.SetDock(head, Dock.Top);
        Hi();
        host.Content = body(sel);
        select?.Invoke(Pick);
        return new DockPanel { LastChildFill = true, Children = { head, host } };
    }

    /// <summary>
    /// An upright value bar (a device fader) over a parameter in 0..1: drag up and down, a click
    /// never jumps, double-click runs <paramref name="reset"/>, ⌥-click (or a double-click on
    /// <paramref name="readout"/>) types a value, right-click opens the parameter menu.
    /// <paramref name="sync"/> follows the parameter and skips while dragged.
    /// </summary>
    internal static SliderTrack Fader(string name, Func<double> norm, Action<double> setNorm, Func<string> text,
        Action begin, Action end, Action reset, TextBlock? readout, out Action sync, bool bipolar = false, Action? after = null)
    {
        Action repaint = () => { };
        var track = new SliderTrack { Vertical = true, Bipolar = bipolar, Reset = () => { reset(); repaint(); after?.Invoke(); } };
        track.Changed += v => { setNorm(v); if (readout is not null) readout.Text = text(); after?.Invoke(); };
        track.GestureBegin += begin;
        track.GestureEnd += end;
        void Paint() { track.Norm = norm(); if (readout is not null) readout.Text = text(); }
        repaint = Paint;
        ValueEntry.Attach(track, ValueEntry.Probing(name, norm, setNorm, text, begin, end, () => { Paint(); after?.Invoke(); },
            () => { reset(); Paint(); after?.Invoke(); }), readout);
        sync = () => { if (!track.Dragging) Paint(); };
        Paint();
        return track;
    }

    /// <summary>A pressed-in section with the standard padding (12, or less where a column is tight).</summary>
    internal static RuleSection Section(Control body, double padX = SectionPad, double padY = SectionPad) =>
        new() { Padding = new Thickness(padX, padY), Child = body };

    internal static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
}

/// <summary>
/// A level well for a device's left column: an upright groove (corner 4, 16 wide unless set)
/// filled in a wash of <see cref="Ink"/> with a 2px line at the level — from the bottom, or
/// hanging from the top (<see cref="FromTop"/>, a reduction, whose line shows even at 0).
/// <see cref="Tick"/> marks a threshold with a short orange dash. Empty when there is no signal.
/// </summary>
internal sealed class FxLevel : Control
{
    private double _v, _tick = double.NaN;
    public bool FromTop { get; init; }
    public IBrush Ink { get; init; } = NotaPalette.VizInk;

    public FxLevel() { Width = 16; }

    /// <summary>The level 0..1, and the threshold mark 0..1 (NaN = none).</summary>
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
        NotaDepth.Groove(ctx, this, new RoundedRect(rect, 4));
        using (ctx.PushClip(new RoundedRect(rect, 4)))
        {
            if (FromTop || _v > 0.004)
            {
                double y = FromTop ? _v * h : (1 - _v) * h;
                ctx.FillRectangle(NotaPalette.Wash((SolidColorBrush)Ink, 0x30), FromTop ? new Rect(0, 0, w, y) : new Rect(0, y, w, h - y));
                ctx.FillRectangle(Ink, new Rect(2, Math.Clamp(y - 1, 1, h - 3), w - 4, 2));
            }
            if (!double.IsNaN(_tick))
            {
                double ty = Math.Clamp((1 - _tick) * h, 1, h - 2);
                double dw = Math.Max(2, (w - 6) / 2);
                ctx.FillRectangle(NotaPalette.Accent, new Rect(2, ty - 0.5, dw, 1.5));
                ctx.FillRectangle(NotaPalette.Accent, new Rect(w - 2 - dw, ty - 0.5, dw, 1.5));
            }
        }
    }
}
