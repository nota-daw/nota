// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The on/off switch (style guide, spec 038): a 20×12 track, corner 3, with an 8px knob,
// corner 2, inset 2. Off — the dark switch track with a slight inner shadow and an ink-3
// knob on the left. On inside a device (and for every power switch) — orange with a dark
// knob on the right; on in the shell — light grey (ink 2) with a deep knob. State reads from
// colour *and* position, never colour alone. It is paired with a word to its right
// (DeviceCardKit.Switch); never with an icon.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class SwitchTrack : Control
{
    private static readonly IBrush TrackOn = NotaPalette.Accent;
    private static readonly IBrush TrackOnShell = NotaPalette.TextSecondary;
    private static readonly IBrush TrackDim = NotaPalette.TextDisabled;   // on, but its section is inactive
    private static readonly IBrush TrackOff = NotaPalette.WellTrack;
    private static readonly IBrush KnobOn = NotaPalette.TextOnAccent;
    private static readonly IBrush KnobOnShell = NotaPalette.SurfaceDeep;
    private static readonly IBrush KnobOff = NotaPalette.TextTertiary;

    public const double W = 20, H = 12, KnobD = 8, Inset = 2;
    // The settings-window size: a 28×16 track with a 12px knob inset 2 — a standalone row
    // toggle at the shell scale, not a parameter inside a device card.
    public const double LargeW = 28, LargeH = 16, LargeKnobD = 12, LargeInset = 2;

    private readonly double _w, _h, _knob, _inset;
    private readonly bool _shell;
    private bool _on, _dim;

    public bool IsOn { get => _on; set { if (_on == value) return; _on = value; InvalidateVisual(); } }

    /// <summary>On, but inert — the section it gates is switched off. Loses orange, keeps position.</summary>
    public bool IsDim { get => _dim; set { if (_dim == value) return; _dim = value; InvalidateVisual(); } }

    /// <param name="shell">A switch outside device cards: on is light grey, not orange.</param>
    public SwitchTrack(bool large = false, bool shell = false)
    {
        (_w, _h, _knob, _inset) = large ? (LargeW, LargeH, LargeKnobD, LargeInset) : (W, H, KnobD, Inset);
        _shell = shell;
        Width = _w; Height = _h;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    public override void Render(DrawingContext ctx)
    {
        var rect = new RoundedRect(new Rect(0, 0, _w, _h), NotaRadius.SwitchValue);
        if (_on)
            ctx.DrawRectangle(_dim || !IsEffectivelyEnabled ? TrackDim : _shell ? TrackOnShell : TrackOn, null, rect);
        else
            ctx.DrawRectangle(TrackOff, null, rect, NotaDepth.Shadow(this, "Shadow.SwitchInset"));
        double x = _on ? _w - _inset - _knob : _inset;
        ctx.DrawRectangle(_on ? (_shell ? KnobOnShell : KnobOn) : KnobOff, null,
            new RoundedRect(new Rect(x, (_h - _knob) / 2, _knob, _knob), NotaRadius.FineValue));
    }
}
