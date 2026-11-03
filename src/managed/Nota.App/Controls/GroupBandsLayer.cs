// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Mixer's group bands (specs/070): drawn behind the flat strip row so the strips stay one
// row (height fitting, meters and sync untouched). Per group, one bar row per nesting level
// with the group's name in its colour, and a faint tint of that colour behind the group's
// strips; a folded group's bar adds the hidden count. Bands follow the strips' laid-out
// bounds, so the layer repaints on layout and never rebuilds anything.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Nota.App;

internal sealed class GroupBandsLayer : Control, ICustomHitTest
{
    /// <summary>One bar row per nesting level.</summary>
    public const double BarH = 14;

    /// <summary>One group's band: its strip through the last visible strip of its subtree.</summary>
    public readonly record struct Band(string Name, int ColorIndex, int Depth, Control First, Control Last, int Hidden);

    private IReadOnlyList<Band> _bands = Array.Empty<Band>();
    private readonly List<(Rect Bar, string Name)> _bars = new();   // hit areas, from the last render

    /// <summary>Where the first bar row starts, in this layer (the strip row's top margin).</summary>
    public double Top { get; set; } = 10;

    public GroupBandsLayer()
    {
        ClipToBounds = true;
        PointerMoved += (_, e) =>
        {
            var p = e.GetPosition(this);
            string? tip = null;
            foreach (var (bar, name) in _bars) if (bar.Contains(p)) tip = name;   // the innermost wins: drawn last
            if (!Equals(ToolTip.GetTip(this), tip)) ToolTip.SetTip(this, tip);
        };
    }

    public void SetBands(IReadOnlyList<Band> bands) { _bands = bands; InvalidateVisual(); }

    // Only the bars take the pointer (for the name tooltip); the tint lets clicks through to nothing
    // — the strips sit above it anyway.
    public bool HitTest(Point point)
    {
        foreach (var (bar, _) in _bars) if (bar.Contains(point)) return true;
        return false;
    }

    public override void Render(DrawingContext ctx)
    {
        _bars.Clear();
        foreach (var b in _bands)
        {
            if (!b.First.IsVisible || !b.Last.IsVisible) continue;
            if (b.First.TranslatePoint(default, this) is not { } p0 || b.Last.TranslatePoint(default, this) is not { } p1) continue;
            double x0 = p0.X, x1 = p1.X + b.Last.Bounds.Width;
            double y = Top + b.Depth * BarH;
            double bottom = p0.Y + b.First.Bounds.Height;
            if (x1 <= x0 || bottom <= y) continue;
            var hue = ArrangementView.TrackBrush(b.ColorIndex);

            // Tint behind the strips, then the bar on top of it.
            ctx.DrawRectangle(NotaPalette.Wash(hue, 0x12), null, new RoundedRect(new Rect(x0, y, x1 - x0, bottom - y),
                new CornerRadius(NotaRadius.ClipValue, NotaRadius.ClipValue, 0, 0)));
            var bar = new Rect(x0, y, x1 - x0, BarH - 2);
            ctx.DrawRectangle(NotaPalette.Wash(hue, 0x52), null, new RoundedRect(bar, NotaRadius.ClipValue));
            _bars.Add((bar, b.Name));

            double maxW = bar.Width - 10;
            if (maxW <= 4) continue;
            var count = b.Hidden > 0
                ? new FormattedText("+" + b.Hidden.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, NotaFonts.Mono, 9, NotaPalette.TextSecondary)
                : null;
            double countW = count is null ? 0 : count.Width + 4;
            var name = new FormattedText(b.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                NotaFonts.SansSemiBold, 9, NotaPalette.TextPrimary)
            {
                MaxTextWidth = Math.Max(1, maxW - countW), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis,
            };
            double ty = bar.Y + (bar.Height - name.Height) / 2;
            using (ctx.PushClip(bar))
            {
                ctx.DrawText(name, new Point(bar.X + 5, ty));
                if (count is not null)
                    ctx.DrawText(count, new Point(bar.X + 5 + Math.Min(name.WidthIncludingTrailingWhitespace, maxW - countW) + 4, bar.Y + (bar.Height - count.Height) / 2));
            }
        }
    }
}
