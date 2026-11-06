// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One envelope panel for the built-in instruments (#531): a header (the envelope's name,
// where it goes, Lin / Exp), the drawn curve (VoltEnv: drag the breakpoints, bend decay and
// release by their diamonds) and one row of A D S R knobs under a rule. The card supplies
// its own knob and chip builders, so the cells match the rest of the card; the times are
// the engine's maps, and every graph of a card can share one EnvTimeScale.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

internal static class EnvPanel
{
    /// <summary>What one envelope is: its A D S R and shape param ids (decay curve, release
    /// curve, Exp), and the engine's time range of A, D and R in seconds.</summary>
    internal sealed record Spec(
        string Title, string[] Ids, string[] Shape,
        (double Lo, double Hi) A, (double Lo, double Hi) D, (double Lo, double Hi) R,
        Func<float, string>? SustainText = null,
        // A Minimoog-style contour (Monolith): no release of its own — it runs the decay time
        // (Seconds says how long), so the row has A D S knobs only.
        Func<float, double>? ReleaseFromDecay = null);

    internal static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

    /// <summary>The panel. <paramref name="knob"/> builds a knob cell (id, label, learn name,
    /// read-out); <paramref name="chips"/> a param-backed segment strip (id, names).</summary>
    internal static Control Build(VoltEnv view, Spec spec, Func<string, (int, string)> param,
        Func<string, string, string, Func<float, string>, Control> knob, Func<string, string[], Control> chips,
        Control? route = null, EnvTimeScale? scale = null, IBrush? captionInk = null)
    {
        Func<float, string>[] fmt =
        {
            v => NotaNum.Time(ExpMap(v, spec.A.Lo, spec.A.Hi)),
            v => NotaNum.Time(ExpMap(v, spec.D.Lo, spec.D.Hi)),
            spec.SustainText ?? (v => NotaNum.Pct(v)),
            v => NotaNum.Time(ExpMap(v, spec.R.Lo, spec.R.Hi)),
        };
        view.Target(spec.Title, param(spec.Ids[0]), param(spec.Ids[1]), param(spec.Ids[2]), param(spec.Ids[3]));
        view.Shape(param(spec.Shape[0]), param(spec.Shape[1]), param(spec.Shape[2]));
        view.Format = (k, n) => fmt[k]((float)n);   // ⌥-click a breakpoint to type it
        view.Seconds = (k, n) => k switch
        {
            0 => ExpMap(n, spec.A.Lo, spec.A.Hi),
            1 => ExpMap(n, spec.D.Lo, spec.D.Hi),
            _ => spec.ReleaseFromDecay is { } rel ? rel((float)n) : ExpMap(n, spec.R.Lo, spec.R.Hi),
        };
        if (scale is not null) view.Scale = scale;
        view.VerticalAlignment = VerticalAlignment.Stretch;

        string[] stage = { "A", "D", "S", "R" };
        int knobs = spec.ReleaseFromDecay is null ? 4 : 3;
        var cells = new Grid { ColumnDefinitions = new ColumnDefinitions(knobs == 4 ? "*,*,*,*" : "*,*,*") };
        for (int k = 0; k < knobs; k++)
        {
            var cell = knob(spec.Ids[k], stage[k], NotaText.Label(spec.Title) + " · " + stage[k], fmt[k]);
            cell.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(cell, k); cells.Children.Add(cell);
        }
        var cellsHost = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 3, 0, 0), Child = cells,
        };

        var curve = chips(spec.Shape[2], new[] { Loc.T("device.env.lin"), Loc.T("device.env.exp") });
        curve.HorizontalAlignment = HorizontalAlignment.Right;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 5 };
        head.Children.Add(new TextBlock
        {
            Text = NotaText.Label(spec.Title), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium,
            Foreground = captionInk ?? NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        });
        if (route is not null) { route.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(route, 1); head.Children.Add(route); }
        Grid.SetColumn(curve, 2); head.Children.Add(curve);

        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 3 };
        g.Children.Add(head);
        Grid.SetRow(view, 1); g.Children.Add(view);
        Grid.SetRow(cellsHost, 2); g.Children.Add(cellsHost);
        return g;
    }

    /// <summary>Follow the sounding voice on the graphs: the engine's envelope telemetry
    /// (EnvShape.h) — stage and progress per envelope — starting at <paramref name="offset"/>
    /// in the instrument's scope.</summary>
    internal static void FollowVoice(DeviceCardContext ctx, int offset, Func<bool>? visible, params VoltEnv[] envs)
    {
        var buf = new float[offset + envs.Length * 2];
        ctx.AddDeviceRefresher(() =>
        {
            if (visible is not null && !visible()) return;
            if (ctx.Engine.InstrumentScope(ctx.TrackId, buf) < buf.Length) return;
            for (int i = 0; i < envs.Length; i++) envs[i].SetPlay((int)buf[offset + i * 2], buf[offset + i * 2 + 1]);
        });
    }
}
