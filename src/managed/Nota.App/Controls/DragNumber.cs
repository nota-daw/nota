// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A numeric field that edits by vertical drag or double-click-to-type
// (HANDOFF §4: "all numerics editable by drag (vertical) and double-click-type;
// mono font keeps width stable"). Shows a mono read-out; drag up/down nudges the
// value, double-click swaps in a flat text box that commits on Enter / blur. The typed text
// goes through the shared value parser (ValueText: sign, decimal comma, fractions) and is
// written as one gesture, so it is one undo step like a drag (specs/035).

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed class DragNumber : UserControl
{
    private readonly TextBlock _display = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _editor;
    private readonly double _min, _max, _step, _dragSnap;
    private readonly string _format;
    private double _value;
    private bool _drag, _editing;
    private double _startY, _startValue;

    public event Action<double>? ValueChanged;
    /// <summary>A drag starts / ends — callers bracket an undo group or automation write.</summary>
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    /// <param name="dragSnap">When &gt; 0, a drag lands only on multiples of this (typing stays exact).</param>
    public DragNumber(double value, double min, double max, double step, string format = "0", double fontSize = 12, double dragSnap = 0)
    {
        _min = min; _max = max; _step = step; _format = format; _dragSnap = dragSnap;
        _value = Math.Clamp(value, min, max);

        _display.FontSize = fontSize;
        _display.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        _display.BindResource(TextBlock.ForegroundProperty, "Brush.TextPrimary");

        _editor = new TextBox
        {
            FontSize = fontSize, IsVisible = false, Padding = new Avalonia.Thickness(0), MinHeight = 0,
            Background = Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        _editor.BindResource(TextBox.FontFamilyProperty, "Font.Mono");
        _editor.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitEditor(); else if (e.Key == Key.Escape) CancelEditor(); };
        _editor.LostFocus += (_, _) => CommitEditor();

        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        Content = new Panel { Children = { _display, _editor } };
        Refresh();
    }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, _min, _max); Refresh(); }
    }

    private void Refresh() => _display.Text = _value.ToString(_format, NotaNum.Culture);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_editing) return;
        if (e.ClickCount == 2) { BeginEditor(); e.Handled = true; return; }
        _drag = true;
        _startY = e.GetPosition(this).Y;
        _startValue = _value;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double dy = _startY - e.GetPosition(this).Y;   // up = increase
        double v = _startValue + dy * _step;
        if (_dragSnap > 0)
        {
            // Hold a typed-in fraction until the drag has travelled half a snap, so a
            // click's jitter doesn't round it away.
            if (Math.Abs(dy * _step) < _dragSnap / 2) return;
            v = Math.Round(v / _dragSnap) * _dragSnap;
        }
        v = Math.Clamp(v, _min, _max);
        if (Math.Abs(v - _value) < 1e-9) return;
        _value = v; Refresh(); ValueChanged?.Invoke(_value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) GestureEnd?.Invoke();
        _drag = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    /// <summary>Open the type-in box, as a double-click does.</summary>
    public void BeginEdit() { if (!_editing) BeginEditor(); }

    private void BeginEditor()
    {
        _editing = true;
        _editor.Text = _value.ToString(_format, NotaNum.Culture);
        _editor.IsVisible = true;
        _display.IsVisible = false;
        _editor.Focus();
        _editor.SelectAll();
    }

    private void CommitEditor()
    {
        if (!_editing) return;
        _editing = false;
        _editor.IsVisible = false;
        _display.IsVisible = true;
        // The field's own unit: a number typed with a unit word ("12ms") keeps the number as written.
        if (ValueText.Read(_editor.Text ?? "", out double mul, out _) is { } read && !double.IsNaN(read))
        {
            double v = Math.Clamp(read / mul, _min, _max);
            if (Math.Abs(v - _value) < 1e-12) return;
            _value = v;
            Refresh();
            GestureBegin?.Invoke();
            ValueChanged?.Invoke(_value);
            GestureEnd?.Invoke();
        }
    }

    private void CancelEditor()
    {
        _editing = false;
        _editor.IsVisible = false;
        _display.IsVisible = true;
    }
}
