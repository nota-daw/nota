// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One header row's share of the group trays (specs/094, new look). A group is drawn as a
// tray — a wash of its colour with a 3 px edge down the left — that holds its header row and
// its members; rows are separate cards (one per lane), so each row paints its own slice of
// every tray it sits in, outer first: the wash (stronger on the tray's own header row), the
// edge, the rounded right corners where a tray opens and closes, and the closing strips at
// the bottom of the row a tray ends on. A member track's card then sits on the trays as a
// plain body, so tints never stack on it. Only paints: the row's controls sit above it.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nota.Presentation;

namespace Nota.App;

internal sealed class GroupTrayLayer : Control
{
    /// <summary>The tray wash on a tray's own header row, and on the rest of the tray.</summary>
    public const double HeadWash = 0.16, TrayWash = 0.08;

    private readonly TraySlice _slice;
    private IBrush? _body;
    private readonly double[] _strips;

    /// <param name="body">A member track's card fill; null for a group's own row (the trays show).</param>
    /// <param name="rowH">The row height the card's controls were laid out for; the closing strips
    /// are sized for it and keep that size while a seam drag resizes the row live, so they always
    /// match the space the controls leave for them.</param>
    public GroupTrayLayer(TraySlice slice, IBrush? body, double rowH)
    {
        _slice = slice;
        _strips = GroupTrayLayout.ClosingStrips(slice.Closings, rowH);
        _body = body;
        IsHitTestVisible = false;
    }

    /// <summary>True when the row is a track sitting in a tray (it has a card body to paint).</summary>
    public bool IsMember => _body is not null;

    /// <summary>The member card's fill (selection repaints it in place).</summary>
    public IBrush? Body
    {
        get => _body;
        set { if (ReferenceEquals(_body, value)) return; _body = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var chain = _slice.Chain;
        if (w <= 0 || h <= 0 || chain.Length == 0) return;
        int n = chain.Length;
        var strips = _strips;
        double band = 0;
        foreach (var s in strips) band += s;
        double bodyBottom = Math.Max(0, h - band);
        // Only the right corners round, by the field step, where a tray opens and closes; the
        // left side is square against the tray edge and the column.
        double r = NotaRadius.FieldValue, square = 0;

        for (int k = 0; k < n; k++)
        {
            var tray = chain[k];
            double x0 = tray.Level * GroupTrayLayout.Step;
            bool heads = _slice.Heads && k == n - 1;
            // The trays closing here are the innermost ones; the innermost strip sits on top.
            int fromInside = n - 1 - k;
            bool closes = fromInside < _slice.Closings;
            double bottom = h;
            if (closes)
            {
                bottom = bodyBottom;
                for (int m = 0; m <= fromInside; m++) bottom += strips[m];
                bottom = Math.Min(h, bottom);
            }
            var rect = new Rect(x0, 0, Math.Max(0, w - x0), bottom);
            var corners = new CornerRadius(square, heads ? r : square, closes ? r : square, square);
            ctx.DrawRectangle(ArrangementView.TrackTint(tray.ColorIndex, heads ? HeadWash : TrayWash), null, new RoundedRect(rect, corners));
            ctx.FillRectangle(ArrangementView.TrackBrush(tray.ColorIndex), new Rect(x0, 0, GroupTrayLayout.EdgeW, bottom));
        }

        // A member's card: a plain body from its own colour bar to just short of the right
        // edge, above the closing strips, with the row seam under it.
        if (_body is { } body)
        {
            double x = _slice.ContentLeft, right = w - GroupTrayLayout.TrayInsetR;
            if (right > x && bodyBottom > 0)
            {
                ctx.FillRectangle(body, new Rect(x, 0, right - x, bodyBottom));
                if (NotaPalette.ByKey("Brush.BorderDefault") is IBrush seam)
                    ctx.FillRectangle(seam, new Rect(x, bodyBottom - 1, right - x, 1));
            }
        }
    }
}
