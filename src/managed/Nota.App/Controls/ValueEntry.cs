// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Double-click-to-type for a slider's value read-out (HANDOFF §4: "all numerics editable
// by drag (vertical) and double-click-type"). Double-clicking the read-out lays a flat
// text box over it (an adorner, so it works whatever panel the read-out sits in); Enter
// or a click away commits, Escape cancels.
//
// A slider works in normalised 0..1 and each row formats its own value, so there is no
// inverse mapping to call. Instead the typed text is read as a quantity (sign, number or
// fraction, unit multiplier — k, ms, µs; L/R for pan; ∞) and the slider position whose
// read-out shows that quantity is searched for: the read-out is sampled across the range,
// the bracketing step is bisected, and on a match the middle of the run of positions that
// display it is taken — so typing "0" lands on 0 dB, not on the edge of the "0.0" bucket.
// A word the read-out shows (off, HOLD) is matched as text. The search probes through the
// row's own setter, then the result is written inside one begin/end gesture.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Nota.App;

internal static partial class ValueEntry
{
    /// <summary>Make <paramref name="readout"/> typeable. <paramref name="norm"/> / <paramref name="setNorm"/>
    /// are the slider's 0..1 position, <paramref name="text"/> the read-out at the current position;
    /// <paramref name="after"/> repaints the row once the typed value is in.</summary>
    public static void Attach(TextBlock readout, Func<double> norm, Action<double> setNorm, Func<string> text,
        Action? begin = null, Action? end = null, Action? after = null)
    {
        readout.Background ??= Brushes.Transparent;   // the whole column takes the double-click, not just the glyphs
        readout.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(readout).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Open(readout, typed =>
            {
                double from = norm();
                double? to = Solve(typed, text(), n => { setNorm(n); return text(); });
                begin?.Invoke();
                setNorm(to ?? from);
                end?.Invoke();
                after?.Invoke();
            });
        }, RoutingStrategies.Bubble, handledEventsToo: false);
    }

    private static void Open(TextBlock readout, Action<string> commit)
    {
        if (AdornerLayer.GetAdornerLayer(readout) is null) return;
        double w = readout.Bounds.Width, extra = Math.Max(0, 48 - w);
        // The form field look (sunken well, brass edge while focused), shrunk to the read-out.
        var box = new TextBox
        {
            Classes = { "field" },
            Text = readout.Text, FontFamily = readout.FontFamily, FontSize = readout.FontSize,
            Height = readout.Bounds.Height + 6, Width = w + extra + 4, Padding = new Thickness(3, 0), MinWidth = 0,
            CornerRadius = NotaRadius.Badge,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            // Grow to the left over the track when the read-out column is narrow, and a little taller than the text.
            Margin = new Thickness(-extra - 2, -3, 0, 0),
        };
        TopLevel? top = TopLevel.GetTopLevel(readout);
        bool done = false;
        EventHandler<PointerPressedEventArgs>? outside = null;
        EventHandler<VisualTreeAttachmentEventArgs>? gone = null;
        void Close(bool apply)
        {
            if (done) return;
            done = true;
            if (outside is not null) top?.RemoveHandler(InputElement.PointerPressedEvent, outside);
            readout.DetachedFromVisualTree -= gone;
            AdornerLayer.SetAdorner(readout, null);
            if (apply && !string.IsNullOrWhiteSpace(box.Text)) commit(box.Text!);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return) { Close(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
        };
        box.LostFocus += (_, _) => Close(true);
        gone = (_, _) => Close(false);
        readout.DetachedFromVisualTree += gone;
        // A click on something that doesn't take focus (a canvas, a slider track) still ends the edit.
        outside = (_, e) => { if (e.Source is not Visual v || !box.IsVisualAncestorOf(v) && v != box) Close(true); };
        top?.AddHandler(InputElement.PointerPressedEvent, outside, RoutingStrategies.Tunnel, handledEventsToo: true);

        AdornerLayer.SetIsClipEnabled(box, false);   // it overhangs the read-out
        AdornerLayer.SetAdorner(readout, box);
        Dispatcher.UIThread.Post(() => { if (done) return; box.Focus(); box.SelectAll(); });
    }

    // ---- reading the typed text against the read-out -------------------------------------

    private const int Samples = 48, Steps = 22;

    /// <summary>The 0..1 position whose read-out (<paramref name="at"/>) shows <paramref name="typed"/>
    /// (<paramref name="current"/> is the read-out now, whose unit a bare number takes),
    /// or null when the text says nothing the read-out can show. Assumes the read-out moves
    /// monotonically with the position (or is stepped).</summary>
    internal static double? Solve(string typed, string current, Func<double, string> at)
    {
        var n = new double[Samples + 1];
        var txt = new string[Samples + 1];
        var q = new double[Samples + 1];
        for (int i = 0; i <= Samples; i++)
        {
            n[i] = (double)i / Samples;
            txt[i] = at(n[i]);
            q[i] = Read(txt[i], out _, out _) ?? double.NaN;
        }

        string t = Clean(typed);
        for (int i = 0; i <= Samples; i++)
            if (string.Equals(Clean(txt[i]), t, StringComparison.OrdinalIgnoreCase))
                return Plateau(at, n, q, txt, i);

        double? parsed = Read(typed, out _, out bool typedHasUnit);
        if (parsed is not { } raw || double.IsNaN(raw)) return null;
        double target = Pick(raw, typedHasUnit, current, txt, q);

        // A step of the sampled read-out that brackets the target: bisect it.
        for (int i = 0; i < Samples; i++)
        {
            double a = q[i], b = q[i + 1];
            if (double.IsNaN(a) || double.IsNaN(b) || a == b) continue;
            if (Math.Min(a, b) > target || Math.Max(a, b) < target) continue;
            if (Same(a, target)) return Plateau(at, n, q, txt, i);
            if (Same(b, target)) return Plateau(at, n, q, txt, i + 1);
            double lo = n[i], hi = n[i + 1];
            bool rising = b > a;
            for (int s = 0; s < Steps; s++)
            {
                double mid = (lo + hi) / 2, qm = Read(at(mid), out _, out _) ?? double.NaN;
                if (double.IsNaN(qm)) break;
                if (Same(qm, target)) return Edges(at, lo, mid, hi, target);
                if (qm < target == rising) lo = mid; else hi = mid;
            }
            double ql = Read(at(lo), out _, out _) ?? a, qh = Read(at(hi), out _, out _) ?? b;
            return Math.Abs(ql - target) <= Math.Abs(qh - target) ? lo : hi;
        }

        // Out of range (or no bracketing step): the nearest sample — usually an end of the slider.
        int best = -1;
        for (int i = 0; i <= Samples; i++)
        {
            if (double.IsNaN(q[i])) continue;
            if (best < 0 || Distance(q[i], target) < Distance(q[best], target)) best = i;
        }
        return best < 0 ? null : Plateau(at, n, q, txt, best);
    }

    private static double Distance(double a, double b) => a == b ? 0 : Math.Abs(a - b);

    private static bool Same(double a, double b) =>
        a == b || Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    // The middle of the run of positions around sample i that show the same text — a
    // stepped or rounded read-out lands in the centre of its bucket, not on its edge. A run
    // that reaches an end of the slider takes the end (100 %, −∞, off are the extremes).
    private static double Plateau(Func<double, string> at, double[] n, double[] q, string[] txt, int i)
    {
        int a = i, b = i;
        while (a > 0 && txt[a - 1] == txt[i]) a--;
        while (b < Samples && txt[b + 1] == txt[i]) b++;
        if (a == 0) return 0;
        if (b == Samples) return 1;
        string s = txt[i];
        double lo = a == 0 ? 0 : Edge(at, n[a - 1], n[a], s, insideHigh: true);
        double hi = b == Samples ? 1 : Edge(at, n[b], n[b + 1], s, insideHigh: false);
        return (lo + hi) / 2;
    }

    // Bisect between an outside and an inside position for where the text `s` begins/ends.
    private static double Edge(Func<double, string> at, double x0, double x1, string s, bool insideHigh)
    {
        double outP = insideHigh ? x0 : x1, inP = insideHigh ? x1 : x0;
        for (int k = 0; k < Steps; k++)
        {
            double mid = (outP + inP) / 2;
            if (at(mid) == s) inP = mid; else outP = mid;
        }
        return inP;
    }

    // A bisection hit at `mid` inside (lo, hi): widen to the bucket that shows the same text.
    private static double Edges(Func<double, string> at, double lo, double mid, double hi, double target)
    {
        string s = at(mid);
        return (Edge(at, lo, mid, s, insideHigh: true) + Edge(at, mid, hi, s, insideHigh: false)) / 2;
    }

    // A unitless typed number takes the read-out's own unit when that fits the range
    // ("1.5" on a kHz read-out is 1.5 kHz), else whichever unit the read-out uses that
    // fits ("500" on a seconds read-out that drops to ms is 500 ms).
    private static double Pick(double raw, bool hasUnit, string current, string[] txt, double[] q)
    {
        if (hasUnit) return raw;   // already in base units
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        var muls = new List<double>();
        foreach (var x in q) if (double.IsFinite(x)) { min = Math.Min(min, x); max = Math.Max(max, x); }
        Read(current, out double curMul, out _);
        muls.Add(curMul);
        foreach (var s in txt) { Read(s, out double m, out bool u); if (u && !muls.Contains(m)) muls.Add(m); }
        if (!muls.Contains(1)) muls.Add(1);
        foreach (var m in muls)
        {
            double v = raw * m;
            if (v >= min - 1e-9 && v <= max + 1e-9) return v;
        }
        return raw * curMul;
    }

    private static string Clean(string s) => s.Replace('\u2009', ' ').Replace('\u202F', ' ').Replace('\u00A0', ' ').Trim();

    [GeneratedRegex(@"^(?<pan>[LRC](?=[\s\d]|$))?\s*(?<sign>[+\-])?\s*(?:(?<inf>∞|inf)|(?<num>\d+(?:\.\d*)?|\.\d+)(?:\s*/\s*(?<den>\d+(?:\.\d*)?))?)?\s*(?<unit>[A-Za-zµ%°]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Quantity();

    /// <summary>A read-out or typed text as a number in base units (Hz, s, …), or null.
    /// <paramref name="mul"/> is its unit's multiplier; <paramref name="hasUnit"/> whether it named one.</summary>
    internal static double? Read(string text, out double mul, out bool hasUnit)
    {
        mul = 1; hasUnit = false;
        string s = Clean(text).Replace('−', '-').Replace(',', '.').Replace("±", "");
        var m = Quantity().Match(s);
        if (!m.Success) return null;
        string pan = m.Groups["pan"].Value.ToUpperInvariant();
        string unit = m.Groups["unit"].Value;
        double v;
        if (m.Groups["inf"].Success) v = double.PositiveInfinity;
        else if (m.Groups["num"].Success)
        {
            v = double.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
            if (m.Groups["den"].Success)
            {
                double d = double.Parse(m.Groups["den"].Value, CultureInfo.InvariantCulture);
                if (d == 0) return null;
                v /= d;
            }
        }
        else if (pan == "C") v = 0;
        else return null;
        // "30 L" / "30L" — a pan side written after the number.
        if (pan.Length == 0 && unit.Length == 1 && char.ToUpperInvariant(unit[0]) is 'L' or 'R')
        { pan = unit.ToUpperInvariant(); unit = ""; }
        if (m.Groups["sign"].Value == "-" || pan == "L") v = -v;

        string u = unit.ToLowerInvariant();
        if (u.Length > 0)
        {
            hasUnit = true;
            mul = u == "k" || (u.StartsWith('k') && u.Length > 1) ? 1e3
                : u == "ms" ? 1e-3
                : u is "µs" or "us" ? 1e-6
                : 1;
        }
        return v * mul;
    }
}
