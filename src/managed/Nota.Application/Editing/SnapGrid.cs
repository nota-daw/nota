// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The snap grid shared by the arrangement and the piano roll (specs/003-adaptive-snap-grid).
// One rule turns an editor's grid setting and its zoom into the step to snap to and the step
// to draw, and one position rule serves both snapping and line drawing — so every drawn line
// is a snap point and every snap point is drawn ("what you see is what you snap to").
// Grid positions restart at every bar, so bar lines stay snap points in odd meters (3/4 with
// a 1/2 step: 0 2 | 3 5 | 6 …). Pure and UI-free: the smoke test drives it directly.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Nota.Application.Localization;

namespace Nota.Application.Editing;

/// <summary>How dense the adaptive grid is: the minimum spacing between its finest lines.</summary>
public enum GridDensity { Narrow, Medium, Wide }

/// <summary>One rung of the ladder: 0 = 1/32 … 4 = 1/2 (note values; a beat is a quarter
/// note), 5 = 1 bar, 6 = 2 bars, 7 = 4 bars. Triplet steps are 2/3 of their straight value;
/// bar steps have no triplet form.</summary>
public readonly record struct GridStep(int Index, bool Triplet)
{
    public const int Count = 8, FirstBar = 5;
    private static readonly string[] Names = { "1/32", "1/16", "1/8", "1/4", "1/2", "1 bar", "2 bars", "4 bars" };

    // The rung as the toolbar shows it: note values are numbers, the bar steps are words.
    private static string NameOf(int index) => index switch
    {
        5 => Loc.T("1 bar"), 6 => Loc.T("2 bars"), 7 => Loc.T("4 bars"),
        _ => Names[index],
    };

    /// <summary>A step with the index clamped to the ladder and the triplet flag dropped on bar steps.</summary>
    public static GridStep Of(int index, bool triplet)
    {
        int i = Math.Clamp(index, 0, Count - 1);
        return new GridStep(i, triplet && i < FirstBar);
    }

    public GridStep Normalize() => Of(Index, Triplet);
    [JsonIgnore] public bool IsBar => Normalize().Index >= FirstBar;

    /// <summary>Length in beats; bar steps follow the time signature.</summary>
    public double Beats(int beatsPerBar)
    {
        var s = Normalize();
        if (s.Index >= FirstBar) return SnapGrid.BarBeats(beatsPerBar) * (1 << (s.Index - FirstBar));
        double straight = 0.125 * (1 << s.Index);
        return s.Triplet ? straight * 2.0 / 3.0 : straight;
    }

    [JsonIgnore]
    public string Label
    {
        get
        {
            var s = Normalize();
            return s.Triplet ? NameOf(s.Index) + "T" : NameOf(s.Index);
        }
    }
}

/// <summary>One editor's grid setting (persisted app-wide in Settings, never in a project).
/// <see cref="Density"/> is remembered while Fixed and <see cref="FixedIndex"/> while Adaptive,
/// so ⌘5 goes back to where the user was.</summary>
public sealed record GridSetting
{
    public bool Adaptive { get; init; } = true;
    public GridDensity Density { get; init; } = GridDensity.Medium;
    public int FixedIndex { get; init; } = 3;
    public bool Triplets { get; init; }
    public bool SnapOn { get; init; } = true;

    public static GridSetting DefaultArrangement { get; } = new() { FixedIndex = 3 };   // 1/4
    public static GridSetting DefaultPianoRoll { get; } = new() { FixedIndex = 1 };     // 1/16
}

/// <summary>A setting resolved at a zoom: <see cref="Snap"/> is the step in effect,
/// <see cref="Draw"/> the finest drawn step (equal to Snap except for a Fixed step too fine to
/// draw, which <see cref="FinerThanVisible"/> flags).</summary>
public readonly record struct ResolvedGrid(GridStep Snap, GridStep Draw, bool FinerThanVisible, bool SnapOn);

public static class SnapGrid
{
    private const double Eps = 1e-6;

    /// <summary>Minimum spacing between the finest lines, in px (research R2). Each notch
    /// doubles, so ⌘1 / ⌘2 in Auto move the step one rung.</summary>
    public static double Threshold(GridDensity d) => d switch
    {
        GridDensity.Narrow => 8,
        GridDensity.Wide => 32,
        _ => 16,
    };

    internal static double BarBeats(int beatsPerBar) => beatsPerBar > 0 ? beatsPerBar : 4;

    // A sub-bar step as long as the bar or longer only lands on bar lines; adaptive skips it.
    private static bool Usable(GridStep s, int beatsPerBar)
        => s.IsBar || s.Beats(beatsPerBar) < BarBeats(beatsPerBar) - Eps;

    // The finest step from `from` up whose lines are at least `px` apart (clamped to 4 bars).
    private static GridStep FinestFrom(int from, bool triplet, double pixelsPerBeat, int beatsPerBar, double px)
    {
        for (int i = from; i < GridStep.Count; i++)
        {
            var s = GridStep.Of(i, triplet);
            if (!Usable(s, beatsPerBar)) continue;
            if (s.Beats(beatsPerBar) * pixelsPerBeat >= px) return s;
        }
        return GridStep.Of(GridStep.Count - 1, false);
    }

    /// <summary>The step to snap to and the step to draw for <paramref name="s"/> at a zoom.</summary>
    public static ResolvedGrid Resolve(GridSetting s, double pixelsPerBeat, int beatsPerBar)
    {
        if (!(pixelsPerBeat > 0))
        {
            var q = GridStep.Of(3, s.Triplets);
            return new ResolvedGrid(q, q, false, s.SnapOn);
        }
        if (s.Adaptive)
        {
            var a = FinestFrom(0, s.Triplets, pixelsPerBeat, beatsPerBar, Threshold(s.Density));
            return new ResolvedGrid(a, a, false, s.SnapOn);
        }
        var snap = GridStep.Of(s.FixedIndex, s.Triplets);
        var draw = snap.Beats(beatsPerBar) * pixelsPerBeat >= Threshold(GridDensity.Medium)
            ? snap
            : FinestFrom(snap.Index, s.Triplets, pixelsPerBeat, beatsPerBar, Threshold(GridDensity.Medium));
        return new ResolvedGrid(snap, draw, draw.Index > snap.Index, s.SnapOn);
    }

    /// <summary>The grid position nearest <paramref name="beat"/> (never negative). Sub-bar
    /// steps restart at every bar; bar steps are multiples of their length from beat 0.</summary>
    public static double SnapBeat(double beat, double stepBeats, int beatsPerBar)
    {
        if (!(stepBeats > 0)) return Math.Max(0, beat);
        double bar = BarBeats(beatsPerBar);
        if (stepBeats >= bar - Eps) return Math.Max(0, Math.Round(beat / stepBeats) * stepBeats);
        double barStart = Math.Floor(beat / bar) * bar;
        double rel = beat - barStart;
        int last = LastIndexInBar(stepBeats, bar);
        double a = Math.Clamp(Math.Round(rel / stepBeats), 0, last) * stepBeats;
        double best = Math.Abs(bar - rel) < Math.Abs(a - rel) ? bar : a;
        return Math.Max(0, barStart + best);
    }

    /// <summary>The grid positions in [from, to], ascending — the same set
    /// <see cref="SnapBeat"/> snaps to, for drawing.</summary>
    public static IEnumerable<double> GridLines(double from, double to, double stepBeats, int beatsPerBar)
    {
        if (!(stepBeats > 0) || to < from) yield break;
        from = Math.Max(0, from);
        double bar = BarBeats(beatsPerBar);
        if (stepBeats >= bar - Eps)
        {
            for (double k = Math.Ceiling(from / stepBeats - Eps); k * stepBeats <= to + Eps; k++)
                yield return k * stepBeats;
            yield break;
        }
        int last = LastIndexInBar(stepBeats, bar);
        for (double b = Math.Floor(from / bar); b * bar <= to + Eps; b++)
            for (int n = 0; n <= last; n++)
            {
                double p = b * bar + n * stepBeats;
                if (p > to + Eps) yield break;
                if (p >= from - Eps) yield return p;
            }
    }

    // The highest n with n·step still inside the bar (the next bar start is n = 0 of the next bar).
    private static int LastIndexInBar(double stepBeats, double bar)
        => Math.Max(0, (int)Math.Ceiling((bar - Eps) / stepBeats) - 1);

    /// <summary>The snap control's text: "Auto · 1/8" in Adaptive mode, "1/8" when Fixed.</summary>
    public static string Label(GridSetting s, ResolvedGrid r) => s.Adaptive ? Loc.F("Auto · {0}", r.Snap.Label) : r.Snap.Label;

    // ---- transitions (⌘1–⌘5 and the menu) ------------------------------------------------

    /// <summary>⌘1: a fixed step one rung finer; in Auto, the density one notch finer.</summary>
    public static GridSetting Narrower(GridSetting s) => s.Adaptive
        ? s with { Density = s.Density switch { GridDensity.Wide => GridDensity.Medium, _ => GridDensity.Narrow } }
        : s with { FixedIndex = Math.Max(0, Math.Clamp(s.FixedIndex, 0, GridStep.Count - 1) - 1) };

    /// <summary>⌘2: a fixed step one rung coarser; in Auto, the density one notch coarser.</summary>
    public static GridSetting Wider(GridSetting s) => s.Adaptive
        ? s with { Density = s.Density switch { GridDensity.Narrow => GridDensity.Medium, _ => GridDensity.Wide } }
        : s with { FixedIndex = Math.Min(GridStep.Count - 1, Math.Clamp(s.FixedIndex, 0, GridStep.Count - 1) + 1) };

    /// <summary>⌘3.</summary>
    public static GridSetting ToggleTriplets(GridSetting s) => s with { Triplets = !s.Triplets };

    /// <summary>⌘4.</summary>
    public static GridSetting ToggleSnap(GridSetting s) => s with { SnapOn = !s.SnapOn };

    /// <summary>⌘5: Auto ↔ Fixed, each side keeping what it had.</summary>
    public static GridSetting ToggleAdaptive(GridSetting s) => s with { Adaptive = !s.Adaptive };

    /// <summary>Menu "Auto · Narrow/Medium/Wide": picking a value means snapping to it.</summary>
    public static GridSetting PickAdaptive(GridSetting s, GridDensity d) => s with { Adaptive = true, Density = d, SnapOn = true };

    /// <summary>Menu fixed step.</summary>
    public static GridSetting PickFixed(GridSetting s, int index)
        => s with { Adaptive = false, FixedIndex = Math.Clamp(index, 0, GridStep.Count - 1), SnapOn = true };
}
