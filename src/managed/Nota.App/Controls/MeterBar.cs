// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Stereo level meter, drawn to the almanac (§ Visualisations · level meters):
//   · each channel 11px wide with a 5px step between, radius 2, on the well ground —
//     narrower hosts (a 64px track header) scale both down in the same proportion;
//   · three zones, bottom to top: Signal up to −6 dB, Caution up to 0, Alert above 0;
//   · no animation: the level is set the moment a reading arrives (a meter that eases
//     lies about the sound), and the peak mark neither blinks nor falls on its own —
//     it holds until the meter is clicked or reset.
// Works vertical (mixer strips, track headers) or horizontal (master in the transport).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed class MeterBar : Control
{
    private static readonly IBrush Ground = NotaPalette.BgSunken;

    private readonly bool _horizontal;
    private double _levelL, _levelR;       // 0..1 on the meter scale
    private double _holdL, _holdR;         // peak hold, 0..1

    public MeterBar(bool horizontal = false)
    {
        _horizontal = horizontal;
        if (horizontal) { Height = 12; MinWidth = 80; }
        else { Width = MeterScale.StereoWidth; MinHeight = 40; }
        ToolTip.SetTip(this, "Clear the peak hold");
    }

    /// <summary>Feed a fresh reading. The bar takes the value at once; the hold keeps the maximum.</summary>
    public void Push(NotaMeter m)
    {
        _levelL = MeterScale.Norm(m.PeakL);
        _levelR = MeterScale.Norm(m.PeakR);
        _holdL = Math.Max(_holdL, _levelL);
        _holdR = Math.Max(_holdR, _levelR);
        InvalidateVisual();
    }

    /// <summary>Clears the meter and its peak hold (e.g. on stop).</summary>
    public void Reset()
    {
        _levelL = _levelR = _holdL = _holdR = 0;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _holdL = _levelL; _holdR = _levelR;
        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double across = _horizontal ? h : w;
        var (ch, gap, off) = MeterScale.Channels(across);
        Rect A(double o) => _horizontal ? new Rect(0, o, w, ch) : new Rect(o, 0, ch, h);
        MeterScale.DrawChannel(ctx, A(off), _levelL, _holdL, _horizontal);
        MeterScale.DrawChannel(ctx, A(off + ch + gap), _levelR, _holdR, _horizontal);
    }
}

/// <summary>The shared meter scale and zones, so every level meter reads the same.</summary>
internal static class MeterScale
{
    public const double ChannelW = 11, Step = 5, StereoWidth = ChannelW * 2 + Step;
    public const double FloorDb = -60, CeilDb = 6;

    private static readonly IBrush Ground = NotaPalette.BgSunken;

    public static double Norm(float amp)
    {
        if (amp <= 1e-5f) return 0;
        double db = AudioMath.LinToDb(amp);
        return Math.Clamp((db - FloorDb) / (CeilDb - FloorDb), 0, 1);
    }

    public static double NormDb(double db) => Math.Clamp((db - FloorDb) / (CeilDb - FloorDb), 0, 1);

    public static readonly double CautionAt = NormDb(-6), AlertAt = NormDb(0);

    /// <summary>The zone colour at a point on the scale.</summary>
    public static IBrush Zone(double norm) => norm >= AlertAt ? NotaPalette.Danger : norm >= CautionAt ? NotaPalette.Warning : NotaPalette.Success;

    /// <summary>Channel width, gap and leading offset for a stereo meter <paramref name="across"/> wide.</summary>
    public static (double Ch, double Gap, double Offset) Channels(double across)
    {
        if (across >= StereoWidth) return (ChannelW, Step, (across - StereoWidth) / 2);
        double k = across / StereoWidth;
        double gap = Math.Max(1, Step * k);
        return ((across - gap) / 2, gap, 0);
    }

    /// <summary>One channel: ground, zone-coloured level, a 2px hold mark in its zone colour.</summary>
    public static void DrawChannel(DrawingContext ctx, Rect area, double level, double hold, bool horizontal)
    {
        double r = Math.Min(NotaRadius.ClipValue, Math.Min(area.Width, area.Height) / 2);
        ctx.DrawRectangle(Ground, null, new RoundedRect(area, r));
        double len = horizontal ? area.Width : area.Height;
        if (len <= 0) return;

        void Seg(double from, double to, IBrush ink)
        {
            if (to <= from) return;
            var rect = horizontal
                ? new Rect(area.X + from * len, area.Y, (to - from) * len, area.Height)
                : new Rect(area.X, area.Bottom - to * len, area.Width, (to - from) * len);
            ctx.FillRectangle(ink, rect);
        }
        Seg(0, Math.Min(level, CautionAt), NotaPalette.Success);
        Seg(CautionAt, Math.Min(level, AlertAt), NotaPalette.Warning);
        Seg(AlertAt, level, NotaPalette.Danger);

        if (hold > 0.02)
        {
            const double t = 2;
            var mark = horizontal
                ? new Rect(area.X + Math.Clamp(hold * len - t, 0, len - t), area.Y, t, area.Height)
                : new Rect(area.X, area.Bottom - Math.Clamp(hold * len, t, len), area.Width, t);
            ctx.FillRectangle(Zone(hold), mark);
        }
    }
}
