// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The on/off switch from the almanac: an 18×10 pill track with a 7px knob inset 1.5 on
// every side. On — a brass track, the knob in the panel colour, sitting right. Off — the
// Track off colour, an Ink 5 knob, sitting left. State reads from colour *and* position,
// never colour alone. It is always paired with a word to its right (DeviceCardKit.Switch);
// never with an icon. Brass only: role chromas do not go on controls.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class SwitchTrack : Control
{
    private static readonly IBrush TrackOn = NotaPalette.Accent;
    private static readonly IBrush TrackDim = NotaPalette.BorderStrong;   // on, but its section is inactive
    private static readonly IBrush TrackOff = NotaPalette.TrackOff;
    private static readonly IBrush KnobOn = NotaPalette.Panel;
    private static readonly IBrush KnobOff = NotaPalette.TextTertiary;

    public const double W = 18, H = 10, KnobD = 7, Inset = 1.5;
    // The settings-window size: a 28×16 track with a 12px knob inset 2 — a standalone row
    // toggle at the shell scale, not a parameter inside a device card.
    public const double LargeW = 28, LargeH = 16, LargeKnobD = 12, LargeInset = 2;

    private readonly double _w, _h, _knob, _inset;
    private bool _on, _dim;

    public bool IsOn { get => _on; set { if (_on == value) return; _on = value; InvalidateVisual(); } }

    /// <summary>On, but inert — the section it gates is switched off. Loses brass, keeps position.</summary>
    public bool IsDim { get => _dim; set { if (_dim == value) return; _dim = value; InvalidateVisual(); } }

    public SwitchTrack(bool large = false)
    {
        (_w, _h, _knob, _inset) = large ? (LargeW, LargeH, LargeKnobD, LargeInset) : (W, H, KnobD, Inset);
        Width = _w; Height = _h;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    public override void Render(DrawingContext ctx)
    {
        var track = _on ? (_dim || !IsEffectivelyEnabled ? TrackDim : TrackOn) : TrackOff;
        ctx.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, _w, _h), _h / 2));
        double x = _on ? _w - _inset - _knob : _inset;
        ctx.DrawEllipse(_on ? KnobOn : KnobOff, null, new Point(x + _knob / 2, _h / 2), _knob / 2, _knob / 2);
    }
}
