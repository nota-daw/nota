// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A self-toggling pill switch, first drawn for the Auto Filter card (mockup 2b).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// An on/off switch that owns its state and fires Changed — the same 18×10 brass pill as
// SwitchTrack (almanac § Controls), for call sites that want a self-toggling control.
internal sealed class ToggleSwitch : Control
{
    private readonly SwitchTrack _track = new();
    public event Action<bool>? Changed;
    public ToggleSwitch(bool on)
    {
        _track.IsOn = on;
        Width = SwitchTrack.W; Height = SwitchTrack.H;
        Cursor = new Cursor(StandardCursorType.Hand);
        LogicalChildren.Add(_track); VisualChildren.Add(_track);
    }
    public bool IsOn { get => _track.IsOn; set => _track.IsOn = value; }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Left button only — right-click bubbles to the CV-modulate / MIDI Learn menu.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _track.IsOn = !_track.IsOn; Changed?.Invoke(_track.IsOn); e.Handled = true;
    }
    protected override Size ArrangeOverride(Size finalSize) { _track.Arrange(new Rect(finalSize)); return finalSize; }
    protected override Size MeasureOverride(Size availableSize) { _track.Measure(availableSize); return new Size(SwitchTrack.W, SwitchTrack.H); }
}
