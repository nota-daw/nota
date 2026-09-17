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

    private bool _on, _dim;

    public bool IsOn { get => _on; set { if (_on == value) return; _on = value; InvalidateVisual(); } }

    /// <summary>On, but inert — the section it gates is switched off. Loses brass, keeps position.</summary>
    public bool IsDim { get => _dim; set { if (_dim == value) return; _dim = value; InvalidateVisual(); } }

    public SwitchTrack()
    {
        Width = W; Height = H;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    public override void Render(DrawingContext ctx)
    {
        var track = _on ? (_dim || !IsEffectivelyEnabled ? TrackDim : TrackOn) : TrackOff;
        ctx.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, W, H), H / 2));
        double x = _on ? W - Inset - KnobD : Inset;
        ctx.DrawEllipse(_on ? KnobOn : KnobOff, null, new Point(x + KnobD / 2, H / 2), KnobD / 2, KnobD / 2);
    }
}
