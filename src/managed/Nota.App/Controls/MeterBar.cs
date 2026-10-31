// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Stereo level meter (style guide, spec 038):
//   · each channel 3px wide, 1.5 apart, corner 2, on the well — narrower hosts scale both
//     down in the same proportion;
//   · three zones in dBFS, bottom to top: safe green below −6 dB, hot amber to 0 dBFS,
//     over red at and above 0 dBFS, so red always means a clip;
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
    private readonly bool _horizontal;
    private double _levelL, _levelR;       // 0..1 on the meter scale
    private double _holdL, _holdR;         // peak hold, 0..1

    public MeterBar(bool horizontal = false)
    {
        _horizontal = horizontal;
        if (horizontal) { Height = MeterScale.StereoWidth; MinWidth = 80; }
        else { Width = MeterScale.StereoWidth; MinHeight = 40; }
        ToolTip.SetTip(this, "Clear the peak hold");
    }

    /// <summary>Feed a fresh reading. The bar takes the value at once; the hold keeps the maximum.</summary>
    public void Push(NotaMeter m)
    {
        double l = MeterScale.Norm(m.PeakL), r = MeterScale.Norm(m.PeakR);
        double hl = Math.Max(_holdL, l), hr = Math.Max(_holdR, r);
        // Readings arrive at the UI clock even in silence: repaint only when something moved,
        // or every meter redraws ~30×/s while idle (#55).
        if (l == _levelL && r == _levelR && hl == _holdL && hr == _holdR) return;
        _levelL = l; _levelR = r; _holdL = hl; _holdR = hr;
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
    public const double ChannelW = 3, Step = 1.5, StereoWidth = ChannelW * 2 + Step;
    public const double FloorDb = -60, CeilDb = 6;

    private static readonly IBrush Ground = NotaPalette.Well;

    public static double Norm(float amp)
    {
        if (amp <= 1e-5f) return 0;
        double db = AudioMath.LinToDb(amp);
        return Math.Clamp((db - FloorDb) / (CeilDb - FloorDb), 0, 1);
    }

    public static double NormDb(double db) => Math.Clamp((db - FloorDb) / (CeilDb - FloorDb), 0, 1);

    // Zones are tied to dBFS: amber from −6 dB, red only at and above 0 dBFS, so red always
    // means a clip (develop's meaning, kept through the style guide).
    public const double CautionDb = -6, AlertDb = 0;
    public static readonly double CautionAt = NormDb(CautionDb), AlertAt = NormDb(AlertDb);

    /// <summary>The zone colour at a point on the scale.</summary>
    public static IBrush Zone(double norm) => norm >= AlertAt ? NotaPalette.MeterOver : norm >= CautionAt ? NotaPalette.MeterHot : NotaPalette.MeterSafe;

    /// <summary>Channel width, gap and leading offset for a stereo meter <paramref name="across"/> wide.</summary>
    public static (double Ch, double Gap, double Offset) Channels(double across)
    {
        if (across >= StereoWidth) return (ChannelW, Step, (across - StereoWidth) / 2);
        double k = across / StereoWidth;
        double gap = Math.Max(1, Step * k);
        return ((across - gap) / 2, gap, 0);
    }

    /// <summary>One channel: ground, zone-coloured level, a 2px hold mark in its zone colour.
    /// A meter on its own dB scale passes where −6 dB and 0 dBFS fall on it (<paramref name="caution"/>,
    /// <paramref name="alert"/>, 0..1); otherwise the shared scale's zones apply.</summary>
    public static void DrawChannel(DrawingContext ctx, Rect area, double level, double hold, bool horizontal,
        double caution = double.NaN, double alert = double.NaN)
    {
        double cAt = double.IsNaN(caution) ? CautionAt : caution, aAt = double.IsNaN(alert) ? AlertAt : alert;
        double r = Math.Min(NotaRadius.FineValue, Math.Min(area.Width, area.Height) / 2);
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
        Seg(0, Math.Min(level, cAt), NotaPalette.MeterSafe);
        Seg(cAt, Math.Min(level, aAt), NotaPalette.MeterHot);
        Seg(aAt, level, NotaPalette.MeterOver);

        if (hold > 0.02)
        {
            const double t = 2;
            var mark = horizontal
                ? new Rect(area.X + Math.Clamp(hold * len - t, 0, len - t), area.Y, t, area.Height)
                : new Rect(area.X, area.Bottom - Math.Clamp(hold * len, t, len), area.Width, t);
            ctx.FillRectangle(hold >= aAt ? NotaPalette.MeterOver : hold >= cAt ? NotaPalette.MeterHot : NotaPalette.MeterSafe, mark);
        }
    }
}
