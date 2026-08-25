// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// The full-window glass that appears while MIDI-learn is armed. It walks the live
// visual tree for controls carrying a MidiLearn.Binding, outlines each (teal once
// mapped, bright for the one awaiting a message), and turns a click into a learn
// selection. Doing the highlight + hit-test centrally means a build site only has
// to tag its control — no per-control adorners or pointer plumbing.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Nota.App;

public sealed class MidiLearnOverlay : Control
{
    private MidiLearnService? _service;
    private readonly List<(Rect rect, MidiBinding binding)> _hits = new();
    // While armed, re-scan/repaint on our own tick so highlights follow cards that rebuild
    // or reflow — this makes the overlay self-sufficient in secondary windows that don't
    // drive it from a shared UI tick the way the main window does.
    private readonly DispatcherTimer _tick;

    public MidiLearnOverlay()
    {
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _tick.Tick += (_, _) => { if (_service?.Armed == true) InvalidateVisual(); };
    }

    public MidiLearnService? Service
    {
        get => _service;
        set
        {
            if (_service is { } old) { old.ArmedChanged -= OnStateChanged; old.PendingChanged -= OnStateChanged; old.MappingsChanged -= OnStateChanged; }
            _service = value;
            if (_service is { } s) { s.ArmedChanged += OnStateChanged; s.PendingChanged += OnStateChanged; s.MappingsChanged += OnStateChanged; }
            OnStateChanged();
        }
    }

    private void OnStateChanged()
    {
        bool armed = _service?.Armed == true;
        IsHitTestVisible = armed;
        if (armed) { if (!_tick.IsEnabled) _tick.Start(); }
        else if (_tick.IsEnabled) _tick.Stop();
        InvalidateVisual();
    }

    /// <summary>Re-scan + repaint (call on the UI tick while armed so rebuilt cards get covered).</summary>
    public void Refresh() { if (_service?.Armed == true) InvalidateVisual(); }

    private void Collect()
    {
        _hits.Clear();
        if (_service is null) return;
        if (TopLevel.GetTopLevel(this) is not Visual root) return;
        foreach (var v in root.GetVisualDescendants())
        {
            if (v is not Control c || ReferenceEquals(c, this)) continue;
            if (!c.IsEffectivelyVisible || c.Bounds.Width <= 0 || c.Bounds.Height <= 0) continue;

            // A multi-param control (ADSR / XY pad) exposes its own sub-regions instead of
            // a single whole-control binding.
            if (c is IMidiLearnRegions mr)
            {
                foreach (var (rect, target, name) in mr.GetMidiLearnRegions())
                    if (c.TranslatePoint(rect.TopLeft, this) is { } rtl)
                        _hits.Add((new Rect(rtl, rect.Size), new MidiBinding(target, name)));
                continue;
            }

            if (MidiLearn.GetBinding(c) is not { } b) continue;
            if (c.TranslatePoint(new Point(0, 0), this) is not { } tl) continue;
            _hits.Add((new Rect(tl, c.Bounds.Size), b));
        }
    }

    public override void Render(DrawingContext ctx)
    {
        if (_service?.Armed != true) return;
        Collect();

        var pendingTarget = _service.Pending?.Target;
        foreach (var (rect, binding) in _hits)
        {
            bool pending = pendingTarget is { } pt && pt.Equals(binding.Target);
            bool mapped = _service.MappingFor(binding.Target) is not null;

            var outline = pending ? NotaPalette.Accent : mapped ? NotaPalette.Success : NotaPalette.Accent;
            var fill = pending ? new SolidColorBrush(NotaPalette.AccentColor, 0.30)
                     : mapped ? new SolidColorBrush(NotaPalette.SuccessColor, 0.18)
                     : new SolidColorBrush(NotaPalette.AccentColor, 0.15);
            var pad = rect.Inflate(2);
            ctx.DrawRectangle(fill, new Pen(outline, pending ? 2.5 : 1.5), pad, 4, 4);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_service?.Armed != true) return;
        Collect();
        var p = e.GetPosition(this);
        // Topmost (last drawn / innermost) binding under the cursor wins.
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].rect.Contains(p))
            {
                _service.SelectForLearn(_hits[i].binding);
                e.Handled = true;
                return;
            }
        }
        // Click on empty space cancels a pending selection.
        _service.ClearPending();
        e.Handled = true;
    }
}
