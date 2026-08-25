// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A numeric field that edits by vertical drag or double-click-to-type
// (HANDOFF §4: "all numerics editable by drag (vertical) and double-click-type;
// mono font keeps width stable"). Shows a mono read-out; drag up/down nudges the
// value, double-click swaps in a flat text box that commits on Enter / blur.

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed class DragNumber : UserControl
{
    private readonly TextBlock _display = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _editor;
    private readonly double _min, _max, _step;
    private readonly string _format;
    private double _value;
    private bool _drag, _editing;
    private double _startY, _startValue;

    public event Action<double>? ValueChanged;

    public DragNumber(double value, double min, double max, double step, string format = "0", double fontSize = 12)
    {
        _min = min; _max = max; _step = step; _format = format;
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

    private void Refresh() => _display.Text = _value.ToString(_format, CultureInfo.InvariantCulture);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_editing) return;
        if (e.ClickCount == 2) { BeginEditor(); e.Handled = true; return; }
        _drag = true;
        _startY = e.GetPosition(this).Y;
        _startValue = _value;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double dy = _startY - e.GetPosition(this).Y;   // up = increase
        double v = Math.Clamp(_startValue + dy * _step, _min, _max);
        if (Math.Abs(v - _value) < 1e-9) return;
        _value = v; Refresh(); ValueChanged?.Invoke(_value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _drag = false;
        e.Pointer.Capture(null);
    }

    private void BeginEditor()
    {
        _editing = true;
        _editor.Text = _value.ToString(_format, CultureInfo.InvariantCulture);
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
        if (double.TryParse(_editor.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
        {
            _value = Math.Clamp(v, _min, _max);
            Refresh();
            ValueChanged?.Invoke(_value);
        }
    }

    private void CancelEditor()
    {
        _editing = false;
        _editor.IsVisible = false;
        _display.IsVisible = true;
    }
}
